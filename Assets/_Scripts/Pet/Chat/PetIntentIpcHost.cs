using System.Collections;
using UnityEngine;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 主进程指令消费宿主（2026-08-30）：常驻 DontDestroyOnLoad，250ms 轮询 PetIntentIpc 请求文件，
    /// 收到桌面宠的聊天指令（set_game_time/get_game_time）转 PetChatIntent.Execute 执行
    /// 并回写响应。由 GameScene.InitializeStartupScene 创建（主进程常在；编辑器也建——与桌面宠 exe
    /// 共享 persistentDataPath，开发时编辑器 Play 可直接联调桌宠指令）。
    /// 桌宠进程自身不建（PetMode 早退在 InitializeStartupScene 之前）；无请求时轮询开销=一次
    /// File.Exists，可忽略。
    /// </summary>
    public class PetIntentIpcHost : MonoBehaviour
    {
        static bool _created;

        /// <summary>创建常驻轮询宿主（幂等；domain reload 后静态标志复位=对象也没了，重建正确）</summary>
        public static void EnsureExists()
        {
            if (_created && FindFirstObjectByType<PetIntentIpcHost>() != null) return;
            var go = new GameObject("[PetIntentIpcHost]");
            DontDestroyOnLoad(go);
            go.AddComponent<PetIntentIpcHost>();
            _created = true;
        }

        private readonly WaitForSeconds _interval = new WaitForSeconds(0.25f);

        IEnumerator Start()
        {
            while (true)
            {
                yield return _interval;
                var req = PetIntentIpc.PollRequest();
                if (req == null) continue;

                // 执行（同步指令，无协程）
                string result;
                try { result = PetChatIntent.Execute(req.tool, req.args); }
                catch (System.Exception e) { result = $"{{\"error\":\"{e.Message}\"}}"; }
                PetIntentIpc.WriteResponse(req.id, result);
            }
        }
    }
}
