using System;
using System.IO;
using UnityEngine;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 派蒙聊天指令跨进程通道（桌面形态 → 主游戏进程，2026-08-30）：persistentDataPath 共享目录下的
    /// 请求/响应 JSON 文件对（pet_intent_req.json / pet_intent_resp.json）——桌面宠进程是独立进程，
    /// 无主进程引用（docs/19 §8 IPC 正式版未建），文件通道是聊天指令场景的轻量过渡方案：
    /// 单生产者单消费者、250ms 级轮询、小文件 IO，聊天指令低频（人打字速度）完全够用。
    ///
    /// 协议：请求带 id（Guid）+Unix 秒级时间戳；响应按 id 匹配（陈旧响应不会被误读）。
    /// 桌面侧同步等响应（Thread.Sleep 轮询，超时 1.5s——典型往返=主进程 250ms 轮询+执行，
    /// 约 0.3-0.8s；主游戏未运行时超时返回错误文案，LLM 自行向用户解释）。
    /// 主进程侧 PollRequest 幂等：已消费 id 缓存跳过；>3s 陈旧请求（桌面侧早已超时放弃）只消费不执行。
    /// 主游戏不在运行=请求文件无人消费，桌面侧超时报"游戏未运行"——语义正确（游戏时间/界面是
    /// 主进程状态，主进程不在则指令无意义）。
    /// 编辑器与构建共享 persistentDataPath：开发时编辑器 Play 也能消费桌面宠 exe 的指令。
    /// </summary>
    public static class PetIntentIpc
    {
        [Serializable]
        public class IntentRequest
        {
            public string id;
            public double t;      // Unix 秒（发请求时刻；主进程判陈旧用）
            public string tool;
            public string args;   // 参数 JSON 原文
        }

        [Serializable]
        public class IntentResponse
        {
            public string id;
            public string result; // 执行回执 JSON（PetChatIntent.Ok/Error 格式）
        }

        public static string ReqPath => Path.Combine(Application.persistentDataPath, "pet_intent_req.json");
        public static string RespPath => Path.Combine(Application.persistentDataPath, "pet_intent_resp.json");

        /// <summary>主进程已消费的最近请求 id（PollRequest 幂等跳过用——请求文件不删，靠 id 防重执行）</summary>
        static string _lastConsumedId;

        // ==================== 桌面宠侧（请求方） ====================

        /// <summary>发指令并同步等响应（在聊天工具回调里执行——桌宠主线程短暂停顿，聊天场景可接受；
        /// 主游戏未运行/响应超时返回错误回执，LLM 据此回复用户）。</summary>
        public static string RequestWithWait(string tool, string argsJson, float timeoutSec = 1.5f)
        {
            string id = Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(ReqPath, JsonUtility.ToJson(new IntentRequest
                {
                    id = id,
                    t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
                    tool = tool,
                    args = argsJson ?? "",
                }));
            }
            catch (Exception e)
            {
                return Error($"请求写入失败：{e.Message}");
            }

            float deadline = Time.realtimeSinceStartup + timeoutSec;
            while (Time.realtimeSinceStartup < deadline)
            {
                System.Threading.Thread.Sleep(60);
                try
                {
                    if (File.Exists(RespPath))
                    {
                        var resp = JsonUtility.FromJson<IntentResponse>(File.ReadAllText(RespPath));
                        if (resp != null && resp.id == id && !string.IsNullOrEmpty(resp.result))
                            return resp.result;
                    }
                }
                catch { /* 响应文件写一半被读到：跳过下轮重读 */ }
            }
            return Error("主游戏没有在运行，或游戏正忙（开一下游戏再喊派蒙试试）");
        }

        // ==================== 主进程侧（消费方） ====================

        /// <summary>轮询取待执行请求（无/已消费/陈旧返回 null）。执行后调 WriteResponse 回写结果。</summary>
        public static IntentRequest PollRequest()
        {
            try
            {
                if (!File.Exists(ReqPath)) return null;
                var req = JsonUtility.FromJson<IntentRequest>(File.ReadAllText(ReqPath));
                if (req == null || string.IsNullOrEmpty(req.id) || string.IsNullOrEmpty(req.tool)) return null;
                if (req.id == _lastConsumedId) return null; // 已消费过（请求文件未清，id 防重执行）
                if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - req.t > 3.0)
                {
                    _lastConsumedId = req.id; // 陈旧请求（桌面侧早已超时放弃）：只消费不执行
                    return null;
                }
                return req;
            }
            catch { return null; }
        }

        /// <summary>回写执行结果（同步调用于执行后；写失败只告警不重试——桌面侧超时自兜底）</summary>
        public static void WriteResponse(string id, string resultJson)
        {
            _lastConsumedId = id;
            try
            {
                File.WriteAllText(RespPath, JsonUtility.ToJson(new IntentResponse { id = id, result = resultJson }));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PetIntentIpc] 响应写入失败：{e.Message}");
            }
        }

        static string Error(string message) => $"{{\"error\":\"{message}\"}}";
    }
}
