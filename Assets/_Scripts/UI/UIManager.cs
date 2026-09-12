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

        /// <summary>栈单元：Id 可为 null（未注册场景匿名入栈）；PanelRoot=面板根物体（P2 回归修正：销毁必须打面板根而非脚本子物体）</summary>
        private sealed class ScreenUnit
        {
            public ScreenId Id;
            public ScreenBase Instance;
            public string SceneName;
            public UnityEngine.GameObject PanelRoot;
            public bool IsPrefabHost => Id != null && Id.Host == ScreenHostKind.Prefab;
        }

        private readonly List<ScreenUnit> _stack = new();

        /// <summary>Open 时暂存的面板参数（面板 OnEnable→RegisterScreen 消费后清空）</summary>
        private object _pendingOpenArgs;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) { Destroy(this); return; }

            // 根转换自动入池（P3 联机实证必需）：面板挂在 DontDestroyOnLoad 层级根下，
            // 根场景 Single 加载（如联机→战斗）不会销毁/隐藏它们——必须主动清栈入池，
            // 否则联机 UI 叠在战斗画面上（docs/23 root=context 模型的补漏）
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnRootSceneLoaded;
        }

        private void OnDestroy()
        {
            InputLocks.PopAll(this); // 兜底：本类持有的全部输入锁
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnRootSceneLoaded;
            if (Instance == this) Instance = null;
        }

        /// <summary>根场景切换（Single 加载）→ 栈内全部面板强制入池（root context 交替，弹层不属于任何根场景）</summary>
        private void OnRootSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (mode != UnityEngine.SceneManagement.LoadSceneMode.Single) return;

            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var unit = _stack[i];
                _stack.RemoveAt(i);
                if (unit.PanelRoot != null)
                {
                    unit.PanelRoot.SetActive(false);
                    if (!string.IsNullOrEmpty(unit.SceneName)) _panelPool[unit.SceneName] = unit.PanelRoot;
                    GICLog.Info($"[UIManager] 根转换自动入池: {unit.SceneName}");
                }
            }
        }

        // ==================== 注册制（ScreenBase.OnEnable/OnDisable 自动调用） ====================

        internal void RegisterScreen(ScreenBase screen)
        {
            if (screen == null) return;

            // 预热守卫（docs/14 §38）：预热期实例化触发的 OnEnable 不入栈——真正的注册在首次 SetActive(true) 打开时
            if (_prewarming) return;

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
            // 缺失图片兜底已改调用点显式接入（MissingImageGuard.Ensure）：全局自动扫描方案实证不可行
            // ——prefab 序列化 null 加载后即 fake-null，与真断链运行时不可区分，扫描必误伤纯色块
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
        /// PanelHost（docs/23 §3.5，池化版）：优先复用池中预热实例（SetActive(true) 即开——
        /// 零 Instantiate、无尖峰帧，docs/14 §38）；池空时回退同步实例化。
        /// 注册/OnInit/OnShow 经 ScreenBase.OnEnable 自动链（args 由 _pendingOpenArgs 传递）；
        /// 层级带内排序=100+栈深（层级权威，docs/23 §3.2）。
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

            GameObject go = null;

            // ① 池中预热实例复用（快路径）
            if (_panelPool.TryGetValue(id.Name, out var pooled) && pooled != null)
            {
                _panelPool.Remove(id.Name);
                _pendingOpenArgs = args;
                pooled.SetActive(true); // OnEnable→RegisterScreen→OnInit/OnShow 自动链
                _pendingOpenArgs = null;
                go = pooled;
            }

            // ② 池空回退：同步实例化（预热未完成/被赶在前面时）
            if (go == null)
            {
                var prefab = Resources.Load<GameObject>(id.PrefabPath);
                if (prefab == null)
                {
                    GICLog.Error($"[UIManager] 面板 prefab 加载失败: Resources/{id.PrefabPath}");
                    return;
                }

                _pendingOpenArgs = args;
                go = Instantiate(prefab, layerFullscreen);
                _pendingOpenArgs = null;
                go.name = id.Name;
            }

            // 记录面板根（ScreenBase 脚本可能是面板根的子孙物体，销毁/入池必须打 PanelRoot——P2 回归修正）
            var unit = FindEntryById(id);
            if (unit != null) unit.PanelRoot = go;
            else
            {
                GICLog.Warn($"[UIManager] {id.Name} 打开后未自动入栈（OnEnable 注册链异常），请检查 prefab 内 ScreenBase");
            }

            // 层级带内排序：面板根 Canvas 为 root canvas（宿主容器无 Canvas），强制层级带序
            // MainHall Canvas order=0，Fullscreen 带 100+ 恒在其上（与迁移前 Settings order=100 等效）
            var canvas = go.GetComponentInChildren<Canvas>(true);
            if (canvas != null)
                canvas.sortingOrder = LayerBand(UILayer.Fullscreen) + _stack.Count;
        }

        // ==================== 面板池与预热（docs/14 §38：同步 Instantiate 尖峰帧根治） ====================

        /// <summary>池中实例（按 Id.Name 键；隐藏挂层级容器下）——打开复用、关闭回池</summary>
        private readonly Dictionary<string, GameObject> _panelPool = new();

        /// <summary>预热守卫：预热期 OnEnable 注册链跳过（真正的注册发生在首次 SetActive(true) 打开时）</summary>
        private bool _prewarming;

        private void Start()
        {
            StartCoroutine(PrewarmLoop());
        }

        /// <summary>
        /// 预热泵：等 MainHall 就绪（面板只从大厅打开）→ 逐屏空闲实例化（每 30 帧一个，
        /// 把 Instantiate 成本摊进大厅空闲帧——冷打开 286ms 尖峰帧即用户可见的全屏闪烁，docs/14 §38）。
        /// 预热实例 SetActive(false) 挂全屏层容器下，打开时 SetActive(true) 复用。
        /// </summary>
        private IEnumerator PrewarmLoop()
        {
            // 等 MainHall 根场景就绪 + 入场动画头（约 1s）后开泵
            while (UnityEngine.SceneManagement.SceneManager.GetSceneByName("MainHall").isLoaded == false)
                yield return null;
            for (int i = 0; i < 60; i++)
                yield return null;

            foreach (var id in Screens.All)
            {
                if (id == null || id.Host != ScreenHostKind.Prefab) continue;

                var prefab = Resources.Load<GameObject>(id.PrefabPath);
                if (prefab == null)
                {
                    GICLog.Warn($"[UIManager] 预热失败，打开时回退同步实例化: Resources/{id.PrefabPath}");
                    continue;
                }

                _prewarming = true;
                var go = Instantiate(prefab, layerFullscreen);
                go.name = id.Name;

                // 渲染态预热（池化冒烟实证：只暖物体不暖渲染，首开仍有 ~157ms 尖峰帧）：
                // alpha=0 激活两帧走完整渲染管线（Canvas 重建/TMP 网格与字形图集/贴图上传），
                // 成本摊进大厅空闲帧；OnEnable 注册链被 _prewarming 守卫跳过
                bool hadCg = go.TryGetComponent<CanvasGroup>(out var cg);
                float origAlpha = hadCg ? cg.alpha : 1f;
                if (!hadCg) cg = go.AddComponent<CanvasGroup>();
                cg.alpha = 0f;
                go.SetActive(true);
                yield return null;
                yield return null;
                go.SetActive(false);
                cg.alpha = origAlpha;
                if (!hadCg) Destroy(cg);
                _prewarming = false;

                _panelPool[id.Name] = go;

                GICLog.Info($"[UIManager] 面板预热完成（含渲染态）: {id.Name}");
                for (int i = 0; i < 30; i++)
                    yield return null; // 摊开成本：一屏一歇
            }
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
                // PanelHost：面板入池（SetActive(false)，docs/14 §38 池化）——下次打开零成本复用；
                // OnDisable 自动出栈注销，可关闭注册由 ScreenBase.OnDisable 收口。
                // 回退路径沿层级上溯至层级容器为止（禁 transform.root——会走到 GameScene 场景根）
                UnityEngine.GameObject panelRoot = entry.PanelRoot;
                if (panelRoot == null && entry.Instance != null)
                {
                    var t = entry.Instance.transform;
                    while (t.parent != null && t.parent != layerFullscreen) t = t.parent;
                    panelRoot = t.gameObject;
                }
                if (panelRoot != null)
                {
                    panelRoot.SetActive(false);
                    _panelPool[fromScene] = panelRoot;
                }
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
