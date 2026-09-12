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

        [Header("UI 层级根（Boot 场景 UIRoot 容器，docs/23 §2）")]
        [UnityEngine.InspectorName("全屏层根")] [SerializeField] private Transform layerFullscreen;
        [UnityEngine.InspectorName("弹窗层根")] [SerializeField] private Transform layerPopup;
        [UnityEngine.InspectorName("提示层根")] [SerializeField] private Transform layerToast;
        [UnityEngine.InspectorName("顶层根")] [SerializeField] private Transform layerTop;

        /// <summary>栈单元：Id 可为 null（未注册场景匿名入栈）</summary>
        private sealed class ScreenUnit
        {
            public ScreenId Id;
            public ScreenBase Instance;
            public string SceneName;
            public bool IsPrefabHost => Id != null && Id.Host == ScreenHostKind.Prefab;
        }

        private readonly List<ScreenUnit> _stack = new();

        /// <summary>Open 时暂存的面板参数（面板 OnEnable→RegisterScreen 消费后清空）</summary>
        private object _pendingOpenArgs;

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

            var id = screen.GetId();
            if (id != null && id.Host == ScreenHostKind.RootScene)
                return; // 根场景=context 不入栈（Splash/Battle；MainHall 非 ScreenBase 天然不入）

            if (FindEntry(screen) != null) return; // 幂等：重复 OnEnable 不重复入栈

            string sceneName = id != null && id.Host == ScreenHostKind.Prefab
                ? id.Name  // 面板无场景语义，用 ScreenId 名（事件 FromScene/宠物桥寻址用）
                : screen.gameObject.scene.name;

            if (id == null)
                GICLog.Warn($"[UIManager] 场景 '{sceneName}' 未在 Screens 注册表登记，匿名入栈（P4 清查，docs/23 D11）");

            // 下方 Fullscreen 弹层被遮挡 → OnPause（面板化后弹层互叠生效，场景制单弹层不触发）
            var below = Top;
            if (below?.Instance != null)
                below.Instance.RaisePause();

            var unit = new ScreenUnit { Id = id, Instance = screen, SceneName = sceneName };
            _stack.Add(unit);

            // 生命周期：OnInit 一次性 + OnShow（消费 Open 暂存参数；场景制无来源为 null）
            var args = _pendingOpenArgs;
            _pendingOpenArgs = null;
            screen.RaiseOnInit();
            screen.RaiseShow(args);
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

        private ScreenUnit FindEntryById(ScreenId id)
        {
            for (int i = 0; i < _stack.Count; i++)
                if (id != null && ReferenceEquals(_stack[i].Id, id)) return _stack[i];
            return null;
        }

        private ScreenUnit Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        /// <summary>栈深度（结构化断言/调试用）</summary>
        public int StackCount => _stack.Count;

        /// <summary>指定弹层是否在栈（面板+场景制通用；PetChatIntent 已打开判定/断言用）</summary>
        public bool IsOpen(ScreenId id) => FindEntryById(id) != null;

        /// <summary>栈顶弹层名（断言用；空栈返回 null）</summary>
        public string TopSceneName => Top?.SceneName;

        // ==================== 打开 ====================

        /// <summary>
        /// 打开弹层（docs/23 §3.4）。场景制走 GameScene 场景原语；Prefab 面板即时实例化（PanelHost）。
        /// MainHall 的预载→退场→激活编排仍走其自有路径（ExitToSceneAsync），面板分支已在其内部接入。
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
                    OpenPanel(id, args);
                    break;
            }
        }

        /// <summary>
        /// PanelHost（docs/23 §3.5）：Resources 加载 prefab → 实例化至全屏层容器。
        /// 注册/OnInit/OnShow 经 ScreenBase.OnEnable 自动链（args 由 _pendingOpenArgs 传递）；
        /// 层级带内排序=100+栈深（层级权威，覆盖宿主场景 Canvas 的 sortingOrder，docs/23 §3.2）。
        /// </summary>
        private void OpenPanel(ScreenId id, object args)
        {
            if (layerFullscreen == null)
            {
                GICLog.Error($"[UIManager] 全屏层级根未配置（Boot UIRoot 缺失），无法打开 {id.Name}");
                return;
            }
            if (FindEntryById(id) != null)
            {
                GICLog.Warn($"[UIManager] {id.Name} 已打开，重复 Open 忽略（幂等）");
                return;
            }

            var prefab = Resources.Load<GameObject>(id.PrefabPath);
            if (prefab == null)
            {
                GICLog.Error($"[UIManager] 面板 prefab 加载失败: Resources/{id.PrefabPath}");
                return;
            }

            _pendingOpenArgs = args;
            var go = Instantiate(prefab, layerFullscreen);
            _pendingOpenArgs = null;
            go.name = id.Name;

            // 层级带内排序：面板根 Canvas 为 root canvas（宿主容器无 Canvas），强制层级带序
            // MainHall Canvas order=0，Fullscreen 带 100+ 恒在其上（与迁移前 Settings order=100 等效）
            var canvas = go.GetComponentInChildren<Canvas>(true);
            if (canvas != null)
                canvas.sortingOrder = LayerBand(UILayer.Fullscreen) + _stack.Count;
        }

        /// <summary>层级排序带：Fullscreen=100 / Popup=200 / Toast=300 / Top=400，带内用栈深递增</summary>
        private static int LayerBand(UILayer layer) => 100 + (int)layer * 100;

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
                newTop.Instance.RaiseResume(); // 下方弹层重新成为栈顶 → OnResume

            string toScene = newTop?.SceneName ?? GameScene.Instance.CurrentRootSceneName;

            if (entry.IsPrefabHost)
            {
                // PanelHost：Destroy 实例即可（无场景卸载/无资源清理，docs/23 §3.5）；
                // OnDisable/OnDestroy 自动出栈注销与四件套兜底。同样发返回事件保持语义统一
                if (entry.Instance != null) Destroy(entry.Instance.gameObject);
                EventBusHub.Instance?.Send(new OnGoBackEvent { FromScene = fromScene, ToScene = toScene });
                return true;
            }

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
