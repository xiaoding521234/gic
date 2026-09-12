// ScreenBase.cs - 弹层 Screen 公共基类（注入/可关闭注册/输入锁兜底/音乐防泄漏/标准关闭流程）
using System;
using System.Collections;
using UnityEngine;
using GIC.Framework;
namespace GIC.UI
{


    /// <summary>
    /// 弹层 Screen 公共基类。
    /// 收敛各 Screen 重复样板：Awake 注入、可关闭注册、OnDestroy 四件套兜底
    /// （注销可关闭 + PopAll 输入锁 + UnsubscribeOwner + 音乐 pop）、音乐 push/pop 幂等管理、
    /// 标准关闭流程（防重入 + Closing 锁 + 退场动画 + GoBack）。
    /// 根场景 MainHallScreen 非弹层不可关闭，不继承本类。
    /// </summary>
    public abstract class ScreenBase : MonoBehaviour, IClosable
    {
        [Autowired] private InputManager _inputManager;

        /// <summary>关闭防重入标志（标准关闭流程与各 Screen 自有关闭逻辑共用）</summary>
        protected bool isClosing = false;

        private bool _musicPushed = false;
        private bool _musicIsState = false;
        private bool _lifeInited = false;

        /// <summary>
        /// 注入 [Autowired] 字段（含基类与子类字段）。
        /// Boot 链路保证 GameScene.Awake 已完成容器构建，Screen 的 Awake 注入安全。
        /// 子类重写 Awake 时必须调用 base.Awake()。
        /// </summary>
        protected virtual void Awake()
        {
            Wargame.Instance?.Context?.Inject(this);
        }

        // ==================== 生命周期（UIManager 调度，docs/23 §3.3） ====================

        /// <summary>一次性初始化（场景加载/prefab 实例化后；幂等由基类保证）</summary>
        protected virtual void OnInit() { }

        /// <summary>每次打开（入栈）；args 携带打开参数（场景制 P1 无来源恒 null）</summary>
        protected virtual void OnShow(object args) { }

        /// <summary>被更高 Fullscreen 弹层遮挡（仅钩子；不自动变暗/停用，重内容屏自行降载）</summary>
        protected virtual void OnPause() { }

        /// <summary>遮挡它的弹层关闭，重新成为栈顶</summary>
        protected virtual void OnResume() { }

        internal void RaiseOnInit()
        {
            if (_lifeInited) return;
            _lifeInited = true;
            OnInit();
        }

        /// <summary>每次打开：复位关闭防重入标志（池化后同一实例会重开，上次关闭置位的
        /// isClosing 若不复位，重开后将永远关不掉——池化冒烟实证）+ 自动注册可关闭（幂等）；
        /// 注销由 OnDisable 收口。子类无需再手动调 RegisterClosableSelf（保留兼容旧写法）</summary>
        internal void RaiseShow(object args)
        {
            isClosing = false;
            RegisterClosableSelf();
            OnShow(args);
        }
        internal void RaisePause() => OnPause();
        internal void RaiseResume() => OnResume();

        // ==================== 注册制入栈（docs/23 §3.6） ====================
        // OnEnable/OnDisable 自动入出栈；根场景 Screen（Splash/Battle）由注册表守卫跳过；
        // 未知场景匿名入栈+Warn（零回归，P4 清查）。子类重写时必须调用 base。

        /// <summary>
        /// Screen 身份（UIManager 注册/栈管理用）。场景制默认按场景名解析；
        /// prefab 面板必须重写为静态身份——实例化进宿主场景后 scene.name 寻址失效（P2 定则）。
        /// </summary>
        protected virtual ScreenId Id => Screens.FromSceneName(gameObject.scene.name);

        internal ScreenId GetId() => Id;

        protected virtual void OnEnable()
        {
            UIManager.Instance?.RegisterScreen(this);
        }

        protected virtual void OnDisable()
        {
            UIManager.Instance?.UnregisterScreen(this);
            // 池化关闭（SetActive false）即注销可关闭——防隐藏面板接走 ESC；
            // 场景销毁路径 OnDestroy 再注销一次，幂等安全
            _inputManager?.UnregisterClosable(this);
            // 音乐幂等 pop（根转换自动入池路径补漏，P3 联机实证：不走 CloseScreen 模板时
            // OnDestroy 不触发，压低音量若不在此恢复会泄漏到新根场景；正常关闭已 pop 则空操作）
            PopMusicSafe();
            // 池化保险丝下移：入池隐藏会打断动画协程（秒开秒关时入场动画持有的 Entering 锁
            // 随之悬空）——原 OnDestroy 四件套的 PopAll 对永不销毁的池化面板不再触发，
            // 兜底移到 OnDisable（场景销毁路径双触发，幂等）
            InputLocks.PopAll(this);
        }

        /// <summary>注册 ESC/右键关闭（注入完成后在 Start 开头调用）</summary>
        protected void RegisterClosableSelf()
        {
            _inputManager?.RegisterClosable(this);
        }

        // ==================== 音乐管理（幂等；Close 与 OnDestroy 双路径 pop 防泄漏） ====================

        protected void PushMusicVolumeSafe()
        {
            AudioManager.Instance.PushMusicVolume();
            _musicPushed = true;
            _musicIsState = false;
        }

        protected void PushMusicStateSafe(AudioClip clip, MusicType musicType, bool loop = true, float fadeInTime = 0f)
        {
            AudioManager.Instance.PushMusicState(clip, musicType, loop, fadeInTime);
            _musicPushed = true;
            _musicIsState = true;
        }

        /// <summary>恢复音乐（未 push 时空操作；重复调用幂等）</summary>
        protected void PopMusicSafe()
        {
            if (!_musicPushed) return;
            _musicPushed = false;
            // 拆除期 AudioManager（DontDestroyOnLoad）可能已被先销毁：Unity 假 null 用 == 判，
            // 管理器已亡即无需恢复——防 OnDestroy 链路 StopCoroutine 抛 MissingReferenceException（2026-09-06，docs/14 §29）
            if (AudioManager.Instance == null) return;
            if (_musicIsState)
                AudioManager.Instance.PopMusicState();
            else
                AudioManager.Instance.PopMusicVolume();
        }

        // ==================== 标准关闭流程 ====================

        /// <summary>
        /// 标准关闭模板：防重入 + Closing 输入锁 + 恢复音乐 + 可选退场动画 + 弹出。
        /// exitAnimation 只做动画本身（不要再 Pop/返回，模板统一收尾）；
        /// 为 null 时立即返回（转场期间输入由 PopToPrevious 的 SceneTransition 锁封锁）。
        /// 收尾走 UIManager.PopToPrevious 弹出原语（原 GameScene.GoBack 职责，docs/23 D10）。
        /// </summary>
        protected void CloseScreen(Func<IEnumerator> exitAnimation)
        {
            if (isClosing) return;
            isClosing = true;
            InputLocks.Push(this, InputLockReason.Closing);
            PopMusicSafe();
            StartCoroutine(CloseScreenCoroutine(exitAnimation?.Invoke()));
        }

        private IEnumerator CloseScreenCoroutine(IEnumerator exitAnimation)
        {
            if (exitAnimation != null)
                yield return StartCoroutine(exitAnimation);
            InputLocks.Pop(this, InputLockReason.Closing);
            UIManager.Instance.PopToPrevious(this);
        }

        // ==================== IClosable / 统一销毁 ====================

        /// <summary>默认关闭（无退场动画）。有专属关闭流程的 Screen 重写本方法。</summary>
        public virtual void Close() => CloseScreen(null);

        protected virtual void OnDestroy()
        {
            _inputManager?.UnregisterClosable(this);
            // 兜底：动画协程被销毁中断时释放本类持有的全部锁
            InputLocks.PopAll(this);
            // owner 登记制：一行退订本 Screen 登记的全部 EventBus 订阅（未订阅时返回 0）
            EventBusHub.Instance?.UnsubscribeOwner(this);
            // 兜底：非标准关闭路径销毁时恢复音乐（Close 已 pop 时幂等跳过）
            PopMusicSafe();
        }
    }
}
