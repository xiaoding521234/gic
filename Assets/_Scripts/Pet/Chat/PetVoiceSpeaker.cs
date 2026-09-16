using System;
using System.Collections.Generic;
using System.Text;
using GIC.Framework;
using UnityEngine;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 派蒙语音说话层（docs/19 §6.5.11，2026-09-16）：订阅聊天客户端流式增量 → 句缓冲 → 句边界
    /// 切句 → PetTtsClient 合成（按设置模式分发：1=本地侧车，2=云端；0=关闭时静默丢弃）→
    /// AudioClip 队列顺序播放。反应通道同链路覆盖：LLM 反应流走同一 client 增量；兜底文案经
    /// PetReactionConsumer.onFallbackShown 直喂 SpeakText。
    /// 打断语义：新对话 Send（chatStreamTakingOver）/关闭对话（chatClosed）→ 清句队列停播
    /// ——在播句放完即停（合成在途句被丢弃）。音量：每次起播直读 pet.json（跨进程改动下一句生效）。
    /// 事件接线纪律同 UI/会话层：只摘自己持引用的处理器；播完的 clip 即时 Destroy（AudioClip.Create
    /// 的运行时对象，不放任累积）。
    /// </summary>
    public class PetVoiceSpeaker : MonoBehaviour
    {
        private PetChatClient _client;
        private PetTtsClient _tts;
        private AudioSource _source;
        private readonly Queue<string> _textQueue = new Queue<string>();
        private readonly Queue<AudioClip> _clipQueue = new Queue<AudioClip>();
        private readonly StringBuilder _buffer = new StringBuilder();
        private Action<string> _deltaHandler;
        private Action<string> _completeHandler;
        private PetChatUIController _ui;
        private AudioClip _playingClip;

        /// <summary>宿主接线（WireChat 内调）：订阅 client 流式事件 + 事件性 UI 中断口 +
        /// 运行时建 TTS 客户端与 2D AudioSource（两形态同代码路径，播放设施零 prefab 依赖）。
        /// 重复接线幂等（先摘旧再挂新）。</summary>
        public void Wire(PetChatUIController ui, PetChatClient client)
        {
            Unwire();
            _client = client;
            _ui = ui;
            if (_client == null) return;

            if (_tts == null) _tts = gameObject.AddComponent<PetTtsClient>();
            if (_source == null)
            {
                _source = gameObject.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.spatialBlend = 0f; // 2D：语音不随距离衰减
            }
            _tts.onSynthReady += OnSynthReady;
            _tts.onSynthError += OnSynthError;

            _deltaHandler = delta => AppendText(delta);
            _client.onContentDelta += _deltaHandler;
            _completeHandler = _ => FlushBuffer();
            _client.onComplete += _completeHandler;

            if (ui != null)
            {
                // 打断口三处：新对话 Send（chatStreamTakingOver）/关闭对话（chatClosed）/
                // 新气泡开始（bubbleStarted——反应高频时旧语音不叠播，2026-09-16 用户拍板
                ///「当下一个气泡出来时，之前的停止」）
                ui.chatStreamTakingOver += ClearVoice;
                ui.chatClosed += ClearVoice;
                ui.bubbleStarted += ClearVoice;
            }
        }

        /// <summary>摘自己的全部事件（Wire 重接线/销毁共用）</summary>
        void Unwire()
        {
            if (_ui != null)
            {
                _ui.chatStreamTakingOver -= ClearVoice;
                _ui.chatClosed -= ClearVoice;
                _ui.bubbleStarted -= ClearVoice;
                _ui = null;
            }
            if (_client != null)
            {
                if (_deltaHandler != null) _client.onContentDelta -= _deltaHandler;
                if (_completeHandler != null) _client.onComplete -= _completeHandler;
            }
            if (_tts != null)
            {
                _tts.onSynthReady -= OnSynthReady;
                _tts.onSynthError -= OnSynthError;
            }
            _deltaHandler = null;
            _completeHandler = null;
        }

        void OnDestroy() => Unwire();

        /// <summary>兜底文案/主动文本直喂（PetReactionConsumer.onFallbackShown）：整段进句缓冲并
        /// 立即收尾冲刷（无终端标点的短句不滞留缓冲）。</summary>
        public void SpeakText(string text)
        {
            if (string.IsNullOrEmpty(text) || PetPrefs.ReadVoiceMode() == 0) return;
            AppendText(text);
            DrainSentences();
            FlushBuffer();
        }

        // ---- 流式增量 → 句缓冲 → 切句 ----

        void AppendText(string delta)
        {
            if (PetPrefs.ReadVoiceMode() == 0) { _buffer.Length = 0; return; } // 关闭模式=全链路静默（不积累不合成）
            if (string.IsNullOrEmpty(delta)) return;
            _buffer.Append(delta);
            DrainSentences();
            // 流式即合成（2026-09-16 用户需求"流式输出时就开始合成"）：句一在流中切出就驱动——
            // 不等 onComplete（旧版语音总在气泡打完才响）。TTS 忙时本调用被 IsBusy 守卫吞掉，
            // 由 OnSynthReady 的预取链接力；空闲时此调用即发首句（首句在 LLM 还在打字时就开念）。
            PumpText();
        }

        /// <summary>句边界判定：中英文句号叹号问号/省略号/波浪/换行/分号（句间停顿自然处即切——
        /// GPT-SoVITS 短句合成质量与延迟都更好）</summary>
        static bool IsBoundary(char c) =>
            c == '。' || c == '！' || c == '？' || c == '!' || c == '?' || c == '…' || c == '～' || c == '~'
            || c == '\n' || c == '；' || c == ';';

        /// <summary>把缓冲中已完整的句切出入队（连续边界符并归同句，如“！！”）</summary>
        void DrainSentences()
        {
            while (true)
            {
                int end = -1;
                for (int i = 0; i < _buffer.Length; i++)
                    if (IsBoundary(_buffer[i])) { end = i; break; }
                if (end < 0) break;
                int last = end;
                while (last + 1 < _buffer.Length && IsBoundary(_buffer[last + 1])) last++;
                string sentence = _buffer.ToString(0, last + 1).Trim();
                _buffer.Remove(0, last + 1);
                if (sentence.Length > 0) _textQueue.Enqueue(sentence);
            }
            if (_buffer.Length > 120)
            {
                // 长句无边界保险（人设禁 markdown/列举，正常不会触发）：整段强制出队
                string forced = _buffer.ToString().Trim();
                _buffer.Length = 0;
                if (forced.Length > 0) _textQueue.Enqueue(forced);
            }
        }

        /// <summary>流完成：残句收尾入队（一句常以标点收，无标点尾巴在此兜住）</summary>
        void FlushBuffer()
        {
            DrainSentences();
            if (_buffer.Length > 0)
            {
                string rest = _buffer.ToString().Trim();
                _buffer.Length = 0;
                if (rest.Length > 0) _textQueue.Enqueue(rest);
            }
            PumpText();
        }

        // ---- 合成队列驱动 ----

        private int _gen;          // 打断世代：ClearVoice 递增；在途合成回访世代不符=废弃（打断后旧句不插播）
        private int _pumpGen;      // 发起本次在途合成时的世代（客户端串行——同一时刻至多一个在途）

        void PumpText()
        {
            if (_tts == null || _tts.IsBusy || _textQueue.Count == 0) return;
            int mode = PetPrefs.ReadVoiceMode();
            if (mode == 0) { _textQueue.Clear(); return; }
            string sentence = _textQueue.Dequeue();
            GICLog.DevInfo($"[PetVoice] 合成: {sentence}");
            _pumpGen = _gen; // 在途合成的世代标签（回调时比对）
            if (mode == 1) _tts.SynthesizeSidecar(sentence);
            else _tts.SynthesizeCloud(sentence);
        }

        void OnSynthReady(AudioClip clip)
        {
            if (_pumpGen != _gen) { if (clip != null) Destroy(clip); return; } // 打断后的旧句：clip 即弃不插播
            if (clip == null || _source == null) { PumpText(); return; }
            _clipQueue.Enqueue(clip);
            PumpPlayback();
            PumpText(); // 合成串行间隙预取下一句（在播时后台合成，衔接更顺）
        }

        void OnSynthError(string err)
        {
            if (_pumpGen != _gen) return; // 旧世代的失败：已打断，别再驱动队列
            GICLog.DevInfo($"[PetVoice] 句合成失败跳过: {err}");
            PumpText(); // 单句失败不阻塞队列（下一句继续）
        }

        // ---- 播放队列驱动 ----

        void PumpPlayback()
        {
            if (_source == null || _source.isPlaying || _clipQueue.Count == 0) return;
            if (_playingClip != null) Destroy(_playingClip); // 上一句已播完，即时释放运行时 clip
            _playingClip = _clipQueue.Dequeue();
            _source.volume = PetPrefs.ReadVoiceVolume(); // 起播直读（跨进程设置改动下一句生效）
            _source.clip = _playingClip;
            _source.Play();
        }

        void Update()
        {
            if (_source == null) return; // 未接线静默
            // 在播句结束 → 播下一句（AudioSource 无播完事件，轮询驱动队列前进）
            if (!_source.isPlaying && _clipQueue.Count > 0) PumpPlayback();
            // 队列全空且播完 → 释放尾句 clip
            if (!_source.isPlaying && _playingClip != null && _textQueue.Count == 0 && _clipQueue.Count == 0)
            {
                Destroy(_playingClip);
                _playingClip = null;
            }
        }

        /// <summary>打断（新对话/关闭对话/新气泡开始，2026-09-16 拍板）：清文本与音频双队列+停播+释放 clip；
        /// 世代递增=在途合成句回来时被废弃（打断后旧句不插播）。</summary>
        public void ClearVoice()
        {
            _gen++;
            _textQueue.Clear();
            _buffer.Length = 0;
            while (_clipQueue.Count > 0) Destroy(_clipQueue.Dequeue());
            if (_source != null && _source.isPlaying)
            {
                _source.Stop();
                _source.clip = null;
            }
            if (_playingClip != null) { Destroy(_playingClip); _playingClip = null; }
        }
    }
}
