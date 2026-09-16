using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 派蒙语音 TTS 传输层（docs/19 §6.5.11，2026-09-16）：按设置的三模式分发——
    /// ①本地侧车（GPT-SoVITS api_v2，玩家自建，POST /tts 返回 WAV 字节）
    /// ②云端 MiniMax T2A v2（玩家自填 key+音色ID，返回 hex PCM）
    /// ③关闭（调用方判 mode=0 直接跳过，本类不判——读档语义留给说话层）。
    /// 两后端都产出 AudioClip（侧车=WAV 头解析；云端=hex PCM 直转），上层（PetVoiceSpeaker）只管
    /// 逐句排队播放与打断，不关心后端差异。
    /// 侧车自动拉起：模式=侧车且地址无响应且玩家填了程序路径（bat/exe）时，首次使用自动启动一次
    /// （每会话一次；Windows 独立构建限定——游戏内形态在安卓上无 Process.Start）。
    /// </summary>
    public class PetTtsClient : MonoBehaviour
    {
        /// <summary>合成完成（说话层接播放）</summary>
        public event Action<AudioClip> onSynthReady;
        /// <summary>合成失败（说话层决定静默跳过还是提示）</summary>
        public event Action<string> onSynthError;

        private bool _busy;
        /// <summary>当前是否在合成（说话层防重入排队用）</summary>
        public bool IsBusy => _busy;

        private bool _sidecarLaunchTried;   // 本会话侧车自动拉起只试一次（拉起失败不再反复试）
        private bool _sidecarProbed;        // 本会话连通性探测结论缓存
        private bool _sidecarAlive;

        // ---- 侧车模式 ----

        /// <summary>向侧车合成一句话（GPT-SoVITS api_v2 契约：ref_audio_path/prompt_text 每请求必填，
        /// 模型卡调优参数 top_k=20/top_p=0.85/temperature=0.75，短句不切 cut0；输出 wav 32kHz 单声道 16bit）。
        /// 内部串行（同一时刻一个请求）；完成后触发 onSynthReady/onSynthError。</summary>
        public void SynthesizeSidecar(string sentence)
        {
            if (_busy) return;
            StartCoroutine(SidecarRoutine(sentence));
        }

        IEnumerator SidecarRoutine(string sentence)
        {
            _busy = true;
            try
            {
                // 连通性探测（每会话缓存一次）：不通且配置了程序路径→自动拉起一次，再等最多 45s
                if (!_sidecarProbed)
                {
                    _sidecarProbed = true;
                    bool alive = false;
                    yield return ProbeAlive(r => alive = r);
                    if (!alive)
                    {
                        string exe = PetPrefs.ReadVoiceSidecarPath();
#if UNITY_STANDALONE_WIN
                        if (!string.IsNullOrEmpty(exe) && !_sidecarLaunchTried)
                            yield return LaunchSidecarAndWait(exe, r => alive = r);
#endif
                    }
                    _sidecarAlive = alive;
                }
                if (!_sidecarAlive)
                {
                    Fail("侧车未响应");
                    yield break;
                }

                string url = PetPrefs.ReadVoiceSidecarUrl().TrimEnd('/') + "/tts";
                string body = BuildSidecarJson(sentence);
                using (var req = new UnityWebRequest(url, "POST"))
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.timeout = 120; // GPU 稳态 1-2s/句；侧车冷启动预热可达百秒——给足
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Fail($"侧车请求失败: {req.error} {TruncateBody(req.downloadHandler?.text)}");
                        yield break;
                    }
                    AudioClip clip = ParseWav(req.downloadHandler.data);
                    if (clip == null) { Fail("侧车返回不是有效 WAV"); yield break; }
                    _busy = false; // 先释放再触发回调——回调里说话层预取下一句（finally 是后置复位，此刻仍 true，会静默吞掉预取=首句后队列卡死，2026-09-16 用户实测"只听到第一句"根因）
                    onSynthReady?.Invoke(clip);
                    yield break;
                }
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>拼侧车请求 JSON（文本经 JsonConvert 转义；参考音频/文本从 pet.json 语音字段读——
        /// 玩家自建侧车时改 pet.json 侧车参考字段对齐自己的部署）。</summary>
        static string BuildSidecarJson(string sentence)
        {
            return "{\"text\":" + Newtonsoft.Json.JsonConvert.SerializeObject(sentence) +
                   ",\"text_lang\":\"zh\"" +
                   ",\"ref_audio_path\":" + Newtonsoft.Json.JsonConvert.SerializeObject(PetPrefs.ReadVoiceSidecarRefPath()) +
                   ",\"prompt_text\":" + Newtonsoft.Json.JsonConvert.SerializeObject(PetPrefs.ReadVoiceSidecarRefText()) +
                   ",\"prompt_lang\":\"zh\"" +
                   ",\"top_k\":20,\"top_p\":0.85,\"temperature\":0.75" +
                   ",\"text_split_method\":\"cut0\",\"media_type\":\"wav\",\"streaming_mode\":false}";
        }

        /// <summary>连通性探测协程（GET /tts 帮助端点）：HTTP 有响应（含 404/500）=服务活着；
        /// 网络层失败=死。结论经回调回传（迭代器无法带返回值）。</summary>
        IEnumerator ProbeAlive(Action<bool> result)
        {
            using (var req = UnityWebRequest.Get(PetPrefs.ReadVoiceSidecarUrl().TrimEnd('/') + "/tts"))
            {
                req.timeout = 3;
                yield return req.SendWebRequest();
                // HTTP 有响应（含 404/500 协议错误）=服务活着；仅连接层失败判死
                result(req.result != UnityWebRequest.Result.ConnectionError);
            }
        }

        /// <summary>拉起侧车程序（useShellExecute——bat/exe/lnk 通吃）并轮询等就绪（最多 45s：
        /// GPT-SoVITS 冷启动含模型加载，GPU 机型 ~10-30s）。每会话只试一次。</summary>
        IEnumerator LaunchSidecarAndWait(string exePath, Action<bool> result)
        {
            _sidecarLaunchTried = true;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(exePath) ?? "",
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PetTts] 侧车拉起失败: {e.Message}");
                result(false);
                yield break;
            }
            float deadline = Time.realtimeSinceStartup + 45f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSeconds(1.5f);
                bool alive = false;
                yield return ProbeAlive(r => alive = r);
                if (alive) { result(true); yield break; }
            }
            result(false);
        }

        // ---- 云端模式（MiniMax T2A v2） ----

        /// <summary>云端合成一句话。key=玩家自填（JWT）；GroupId 从 JWT payload 的 group_id 解出
        /// （省一个设置项）；音色 ID 空=MiniMax 预置少女声。返回 hex PCM（32kHz 16bit 单声道）。</summary>
        public void SynthesizeCloud(string sentence)
        {
            if (_busy) return;
            StartCoroutine(CloudRoutine(sentence));
        }

        IEnumerator CloudRoutine(string sentence)
        {
            _busy = true;
            try
            {
                string cipher = PetPrefs.ReadVoiceCloudKeyCipher();
                string key = string.IsNullOrEmpty(cipher) ? "" : PetApiKeyCrypto.Decrypt(cipher);
                if (string.IsNullOrEmpty(key)) { Fail("未设置云端 Key"); yield break; }
                string groupId = ExtractGroupId(key);
                if (string.IsNullOrEmpty(groupId)) { Fail("Key 中解不出 GroupId"); yield break; }
                string voiceId = PetPrefs.ReadVoiceCloudVoiceId();
                if (string.IsNullOrEmpty(voiceId)) voiceId = "female-shaonv"; // 预置少女音兜底（最接近派蒙气质的公开预置）

                string body = "{\"model\":\"speech-02-hd\",\"text\":" + Newtonsoft.Json.JsonConvert.SerializeObject(sentence) +
                              ",\"voice_setting\":{\"voice_id\":" + Newtonsoft.Json.JsonConvert.SerializeObject(voiceId) +
                              ",\"speed\":1.0,\"vol\":1.0,\"pitch\":0}" +
                              ",\"audio_setting\":{\"sample_rate\":32000,\"bitrate\":128000,\"format\":\"pcm\",\"channel\":1}}";
                string url = "https://api.minimaxi.com/v1/t2a_v2?GroupId=" + UnityWebRequest.EscapeURL(groupId);
                using (var req = new UnityWebRequest(url, "POST"))
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.SetRequestHeader("Authorization", "Bearer " + key);
                    req.timeout = 60;
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Fail($"云端请求失败: {req.error} {TruncateBody(req.downloadHandler?.text)}");
                        yield break;
                    }
                    string json = req.downloadHandler.text;
                    if (!ExtractJsonStringField(json, "audio", out string hex) || string.IsNullOrEmpty(hex))
                    {
                        Fail("云端返回无音频数据: " + TruncateBody(json));
                        yield break;
                    }
                    _busy = false; // 同侧车：先释放再回调（预取链不断）
                    onSynthReady?.Invoke(ClipFromPcm16(HexToBytes(hex), 32000, 1, "tts_cloud"));
                    yield break;
                }
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>统一失败出口（先释放 busy 再触发错误事件——说话层 OnSynthError 里的预取不被吞；
        /// finally 复位幂等兜底）</summary>
        void Fail(string msg)
        {
            _busy = false;
            Debug.LogWarning($"[PetTts] {msg}");
            onSynthError?.Invoke(msg);
        }

        static string TruncateBody(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 160 ? s.Substring(0, 160) + "…" : s);

        /// <summary>从 MiniMax JWT 的 payload 段解 group_id（JWT = base64url(header).payload.sig）。</summary>
        static string ExtractGroupId(string jwt)
        {
            try
            {
                string[] parts = jwt.Split('.');
                if (parts.Length < 2) return "";
                string payload = parts[1].Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4) { case 2: payload += "=="; break; case 3: payload += "="; break; }
                string json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                return ExtractJsonStringField(json, "group_id", out string gid) ? gid : "";
            }
            catch { return ""; }
        }

        /// <summary>极简 JSON 字符串字段抽取（桥环境禁 JObject 的同款纪律：正则取字段，避免引入解析器依赖）</summary>
        static bool ExtractJsonStringField(string json, string field, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(json)) return false;
            var m = System.Text.RegularExpressions.Regex.Match(json,
                "\"" + field + "\"\\s*:\\s*\"([^\"]*)\"");
            if (!m.Success) return false;
            value = m.Groups[1].Value;
            return true;
        }

        static byte[] HexToBytes(string hex)
        {
            if (hex.Length % 2 == 1) hex = hex.Substring(0, hex.Length - 1);
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        // ---- 音频解码 ----

        /// <summary>WAV（RIFF/PCM16）→ AudioClip。GPT-SoVITS 输出恒 32kHz 单声道 16bit，但解析按头走
        /// （位深/声道/采样率全读头，兼容任意 PCM16 WAV；跳过 fmt 与 data 之间的附加段）。</summary>
        public static AudioClip ParseWav(byte[] wav)
        {
            try
            {
                if (wav == null || wav.Length < 44) return null;
                if (wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F') return null;
                int pos = 12;
                int channels = 1, sampleRate = 32000, bits = 16, dataLen = 0;
                while (pos + 8 <= wav.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
                    int len = BitConverter.ToInt32(wav, pos + 4);
                    if (id == "fmt ")
                    {
                        channels = BitConverter.ToInt16(wav, pos + 10);
                        sampleRate = BitConverter.ToInt32(wav, pos + 12);
                        bits = BitConverter.ToInt16(wav, pos + 22);
                    }
                    else if (id == "data") { dataLen = Math.Min(len, wav.Length - pos - 8); break; }
                    pos += 8 + len + (len % 2); // RIFF 段 2 字节对齐
                }
                if (dataLen <= 0 || bits != 16) return null;
                int sampleCount = dataLen / 2 / channels;
                float[] samples = new float[sampleCount * channels];
                int off = pos + 8;
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = BitConverter.ToInt16(wav, off + i * 2) / 32768f;
                return ClipFromSamples(samples, sampleRate, channels, "tts_sidecar");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PetTts] WAV 解析失败: {e.Message}");
                return null;
            }
        }

        /// <summary>原始 PCM16 → AudioClip（云端 hex PCM 直转）</summary>
        public static AudioClip ClipFromPcm16(byte[] pcm, int sampleRate, int channels, string name)
        {
            int sampleCount = pcm.Length / 2 / channels;
            float[] samples = new float[sampleCount * channels];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
            return ClipFromSamples(samples, sampleRate, channels, name);
        }

        static AudioClip ClipFromSamples(float[] samples, int sampleRate, int channels, string name)
        {
            var clip = AudioClip.Create(name, samples.Length / channels, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
