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

        /// <summary>
        /// 注入 [Autowired] 字段（含基类与子类字段）。
        /// Boot 链路保证 GameScene.Awake 已完成容器构建，Screen 的 Awake 注入安全。
        /// 子类重写 Awake 时必须调用 base.Awake()。
        /// </summary>
        protected virtual void Awake()
        {
            Wargame.Instance?.Context?.Inject(this);
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
        /// 标准关闭模板：防重入 + Closing 输入锁 + 恢复音乐 + 可选退场动画 + GoBack。
        /// exitAnimation 只做动画本身（不要再 Pop/GoBack，模板统一收尾）；
        /// 为 null 时立即返回（转场期间输入由 GoBack 的 SceneTransition 锁封锁）。
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
            GameScene.Instance.GoBack();
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
