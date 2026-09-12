using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using GIC.Framework;
using GIC.Data;
using GIC.UI;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 派蒙→主进程游戏桥（2026-08-30）：Intent 层异步动作的协程宿主与场景导航。
    /// 导航铁律（2026-08-30 重做 open_screen，教训见 docs/19 §6.5）：必须与手动完全同路径——
    /// 弹层界面先标准关闭（ScreenBase.Close：退场动画+音乐恢复+GoBack）回大厅，再走
    /// MainHallScreen.ExitToSceneAsync（预加载+按钮退场动画+激活）；绝不直接 SceneType.Load()
    /// 叠层——大厅 Overlay Canvas 恒渲染在 ScreenSpaceCamera 主 Canvas 之上，直接叠层=按钮透出
    /// 叠穿且可交互。
    /// 只在主进程使用（游戏内形态直调/桌面形态经 PetIntentIpc 转发），桌宠进程不会走到这里。
    /// </summary>
    public static class PetGameBridge
    {
        static MonoBehaviour _runner;

        /// <summary>隐藏常驻协程宿主（DontDestroyOnLoad，跨场景存活；懒创建避开启动期时序）</summary>
        public static MonoBehaviour Runner
        {
            get
            {
                if (_runner != null) return _runner;
                var go = new GameObject("[PetGameBridge]");
                Object.DontDestroyOnLoad(go);
                _runner = go.AddComponent<PetBridgeRunner>();
                return _runner;
            }
        }

        /// <summary>在协程宿主上启动异步流程</summary>
        public static void Run(IEnumerator routine) => Runner.StartCoroutine(routine);

        /// <summary>
        /// 导航到目标界面（target=null 表示只回大厅）。与手动路径完全一致：
        /// ①弹层界面逐层标准关闭回大厅（支持大厅→联机→战斗类嵌套栈）→
        /// ②等大厅入场动画头部完成（按钮大部分滑入；退场动画从当前位置接续，视觉自然）→
        /// ③等转场锁释放 → ④MainHallScreen.ExitToSceneAsync（预加载+按钮退场+激活）→
        /// ⑤等目标场景加载完成。各阶段超时兜底继续走（关不掉/等不到就不卡死）。
        /// </summary>
        public static IEnumerator NavigateToScreen(SceneType target)
        {
            var gs = GameScene.Instance;
            if (gs == null) yield break;

            // 1. 弹层界面逐层标准关闭回大厅（UIManager 栈清空=回到根场景，docs/23 D12）
            int safety = 0;
            while (UIManager.Instance != null && UIManager.Instance.StackCount > 0 && safety++ < 4)
            {
                CloseCurrentScreen(UIManager.Instance.TopSceneName);
                float deadline = Time.unscaledTime + 3f;
                while (UIManager.Instance != null && UIManager.Instance.StackCount > 0 && Time.unscaledTime < deadline) yield return null;
            }

            if (target == null) yield break; // hall：关闭流程已回大厅

            // 2. 大厅入场动画头部（0.35s 动画+交错延迟，不硬等完成——退场动画从按钮当前位置接续）
            yield return Wait.Seconds(0.45f);

            // 3. 等转场锁释放（GoBack 协程末尾才 pop；持锁期间加载会被拦截丢弃）
            float deadline2 = Time.unscaledTime + 3f;
            while (gs.IsTransitioning && Time.unscaledTime < deadline2) yield return null;

            // 4. 与手动同路径：大厅的退出流程（预加载+按钮退场动画+激活）
            var hall = Object.FindFirstObjectByType<MainHallScreen>(FindObjectsInactive.Exclude);
            if (hall == null)
            {
                Debug.LogWarning("[PetGameBridge] 找不到 MainHallScreen，导航中止（游戏可能还在启动）");
                yield break;
            }
            hall.ExitToSceneAsync(target);

            // 5. 等目标场景加载完成
            float deadline3 = Time.unscaledTime + 12f;
            while (!SceneManager.GetSceneByName(target.SceneName).isLoaded && Time.unscaledTime < deadline3)
                yield return null;
        }

        /// <summary>标准关闭当前弹层界面：场景内找 ScreenBase 走 Close()（与用户按 ESC 同路径：
        /// 退场动画+音乐恢复+GoBack）；找不到（异常态）兜底直接弹出原语。</summary>
        static void CloseCurrentScreen(string sceneName)
        {
            // 栈顶匹配（面板/场景制统一）：标准 Close——含退场动画与模板四件套
            // （P4 修正：此前面板走场景查找失败→兜底 PopToPrevious(null) 绕过退场动画）
            var ui = UIManager.Instance;
            if (ui != null && ui.TopSceneName == sceneName)
            {
                ui.GoBack();
                return;
            }

            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.isLoaded)
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    var screen = root.GetComponentInChildren<ScreenBase>(true);
                    if (screen != null)
                    {
                        screen.Close();
                        return;
                    }
                }
            }
            // 场景内未找到 ScreenBase 的兜底：弹栈顶（弹出原语，原 GameScene.GoBack 职责，docs/23 D10）
            UIManager.Instance.PopToPrevious(null);
        }

        /// <summary>协程宿主标记（空组件）</summary>
        class PetBridgeRunner : MonoBehaviour { }
    }
}
