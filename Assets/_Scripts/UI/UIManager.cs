// UIManager.cs - UI 统一栈管理器（docs/23 §3.4）：弹层栈/生命周期调度/返回原语/场景制 Screen 编排
// 挂 Boot 场景 GameScene 物体下（DontDestroyOnLoad 常驻）；P1 管场景制弹层，P2+ 增 PanelHost（prefab 面板）。
// 依赖方向：UI→Framework/Data（GameScene 原语 + EventBus 事件），Framework 不反向依赖本类（docs/23 D12）。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
namespace GIC.UI
{


    /// <summary>
    /// UI 统一栈管理器。栈只收弹层（docs/23 D12：root 场景=context 不入栈，随 Single 加载经
    /// ScreenBase.OnDisable 自清）；注册制入栈（ScreenBase.OnEnable 钩子自动调用，未知场景匿名入栈+Warn 保零回归）。
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        /// <summary>栈单元：Id 可为 null（未注册场景匿名入栈）；Instance 为 null 表示纯场景弹层（P2+ prefab 宿主启用）</summary>
        private sealed class ScreenUnit
        {
            public ScreenId Id;
            public ScreenBase Instance;
            public string SceneName;
        }

        private readonly List<ScreenUnit> _stack = new();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) { Destroy(this); return; }
        }

        private void OnDestroy()
        {
            InputLocks.PopAll(this); // 兜底：本类持有的全部输入锁
            if (Instance == this) Instance = null;
        }

        // ==================== 注册制（ScreenBase.OnEnable/OnDisable 自动调用） ====================

        internal void RegisterScreen(ScreenBase screen)
        {
            if (screen == null) return;

            string sceneName = screen.gameObject.scene.name;
            var id = Screens.FromSceneName(sceneName);
            if (id != null && id.Host == ScreenHostKind.RootScene)
                return; // 根场景=context 不入栈（Splash/Battle；MainHall 非 ScreenBase 天然不入）

            if (FindEntry(screen) != null) return; // 幂等：重复 OnEnable 不重复入栈

            if (id == null)
                GICLog.Warn($"[UIManager] 场景 '{sceneName}' 未在 Screens 注册表登记，匿名入栈（P4 清查，docs/23 D11）");

            // 下方 Fullscreen 弹层被遮挡 → OnPause（P1 场景制下单弹层不叠加，此路径 P2+ 面板化后生效）
            var below = Top;
            if (below?.Instance != null)
                below.Instance.RaisePause();

            var unit = new ScreenUnit { Id = id, Instance = screen, SceneName = sceneName };
            _stack.Add(unit);

            // 生命周期：OnInit 一次性 + OnShow（args 场景制 P1 无来源，恒 null）
            screen.RaiseOnInit();
            screen.RaiseShow(null);
        }

        internal void UnregisterScreen(ScreenBase screen)
        {
            var entry = FindEntry(screen);
            if (entry == null) return; // 幂等：PopToPrevious 已移除 / 未入栈对象
            _stack.Remove(entry);
        }

        private ScreenUnit FindEntry(ScreenBase screen)
        {
            for (int i = 0; i < _stack.Count; i++)
                if (ReferenceEquals(_stack[i].Instance, screen)) return _stack[i];
            return null;
        }

        private ScreenUnit Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        /// <summary>栈深度（结构化断言/调试用）</summary>
        public int StackCount => _stack.Count;

        /// <summary>栈顶弹层名（断言用；空栈返回 null）</summary>
        public string TopSceneName => Top?.SceneName;

        // ==================== 打开 ====================

        /// <summary>
        /// 打开弹层（docs/23 §3.4）。P1：MainHall 的预载→退场→激活编排仍走其自有路径（ExitToSceneAsync），
        /// 本入口供后续调用点/冒烟使用。P2+ PrefabHost 在此分发实例化。
        /// </summary>
        public void Open(ScreenId id, object args = null)
        {
            if (id == null) return;

            switch (id.Host)
            {
                case ScreenHostKind.Scene:
                case ScreenHostKind.RootScene:
                    id.Scene?.Load(); // 内部走 GameScene.LoadSceneWithConfig（SceneTransition 锁 + 激活设置）
                    break;

                case ScreenHostKind.Prefab:
                    GICLog.Error($"[UIManager] PrefabHost 尚未落地（P2，docs/23）：{id.Name}");
                    break;
            }
        }

        // ==================== 返回语义（两分，docs/23 D10） ====================

        /// <summary>
        /// 返回意图（ESC 级/UX 返回键入口）：关栈顶弹层——走各 Screen 的 Close 语义，
        /// 各屏可按领域响应（如 Coop 房主状态 = 离房回发现页）。返回 false=栈空无可返回。
        /// </summary>
        public bool GoBack()
        {
            var top = Top;
            if (top?.Instance == null) return false;
            top.Instance.Close();
            return true;
        }

        /// <summary>
        /// 弹出原语（原 GameScene.GoBack 职责）：从栈移除指定弹层并执行 卸载场景→恢复上级激活→资源清理→发 OnGoBackEvent。
        /// CloseScreen 模板收尾 / Coop 房客离房 / PetGameBridge 兜底直连本原语（不经 Close 二跳，防递归）。
        /// unit 为 null 时弹栈顶。栈中无该单元返回 false。
        /// </summary>
        public bool PopToPrevious(ScreenBase unit)
        {
            var entry = unit != null ? FindEntry(unit) : Top;
            if (entry == null) return false;

            bool wasTop = ReferenceEquals(entry, Top);
            string fromScene = entry.SceneName;
            _stack.Remove(entry);

            var newTop = Top;
            if (wasTop && newTop?.Instance != null)
                newTop.Instance.RaiseResume(); // 下方弹层重新成为栈顶 → OnResume（P2+ 生效）

            string toScene = newTop?.SceneName ?? GameScene.Instance.CurrentRootSceneName;
            StartCoroutine(UnloadSceneHostSequence(fromScene, toScene, newTop?.SceneName));
            return true;
        }

        /// <summary>场景制弹层弹出序列：SceneTransition 锁 → 卸载 → 恢复激活（栈顶弹层或根场景）→ 资源清理（无 GC.Collect，D7）→ 事件</summary>
        private IEnumerator UnloadSceneHostSequence(string fromScene, string toScene, string activateScene)
        {
            InputLocks.Push(this, InputLockReason.SceneTransition);

            // 1. 卸载弹层场景
            yield return GameScene.Instance.UnloadAdditiveScene(fromScene);

            // 2. 恢复激活场景：优先栈顶弹层场景，否则根场景
            var target = activateScene ?? GameScene.Instance.CurrentRootSceneName;
            if (!string.IsNullOrEmpty(target))
                GameScene.Instance.SetActiveSceneByName(target);

            // 3. 资源清理（去 GC.Collect——主线程尖峰，托管内存交运行时 GC；docs/23 D7）
            yield return GameScene.Instance.CleanupUnusedResources();

            InputLocks.Pop(this, InputLockReason.SceneTransition);

            // 4. 发送返回事件（MainHall.GoBackHandler 等监听方依赖 FromScene/ToScene 语义不变）
            EventBusHub.Instance?.Send(new OnGoBackEvent
            {
                FromScene = fromScene,
                ToScene = toScene,
            });
        }
    }
}
