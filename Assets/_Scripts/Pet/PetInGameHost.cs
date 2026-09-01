using UnityEngine;
using GIC.Framework;
using GIC.Data.Event;

namespace GIC.Pet
{
    /// <summary>
    /// 游戏画面内版派蒙宿主（docs/19 §6.4，2026-08-27 立项）——主游戏进程内的派蒙形态，
    /// 目标安卓（Win32 不适用）与想省内存/单进程的 Windows 玩家。与桌面版（独立进程）二选一，
    /// 由设置"派蒙"栏目切换（PlayerSaveData.petForm），**热切换即时生效**。
    ///
    /// 职责：
    /// 1. 形态状态机（启动分发 + 热切换）：桌面=拉起/关闭独立进程；游戏内=创建/销毁本进程内实例
    /// 2. 游戏内实例（渲染+交互）——批次 B 落地：RenderTexture 画中画（专用小相机渲 RT →
    ///    DontDestroyOnLoad 的 UGUI RawImage，光照隔离+跨场景稳定），模型 prefab 化复用
    ///    PaimonPet 的 Paimon 子树全套控制器（P10 深拷贝铁律）。
    ///
    /// 双进程铁律不变（docs/19 §5.8）：游戏内形态没有第二进程，天然规避；桌面形态维持原
    /// PetProcessLauncher/Mutex 体系。编辑器内：形态状态机照常（桌面=PetEditorAutoLauncher 那套
    /// 由 Play 事件自理），游戏内实例编辑器可用（纯 Unity 代码，无 Win32）。
    /// </summary>
    public class PetInGameHost : MonoBehaviour
    {
        /// <summary>形态常量（与 PlayerSaveData.petForm / SettingsScreen 对应）</summary>
        public const int FormDesktop = 0;
        public const int FormInGame = 1;

        private static PetInGameHost _instance;
        private GameObject _inGameInstance;

        /// <summary>游戏内宿主接口（IPetHost）——行为层/视线层经此分发（桌面形态走 PetWindowController 字段）。
        /// 由游戏内宿主组件（渲染管线批次 B 落地后）在 Awake 注入。</summary>
        public static IPetHost HostInterface { get; internal set; }

        /// <summary>当前实际生效形态（-1=未初始化；启动分发/热切换后更新）</summary>
        public static int CurrentForm { get; private set; } = -1;

        /// <summary>宿主单例（按需创建，DontDestroyOnLoad 跨场景存活）</summary>
        private static PetInGameHost instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[PetInGameHost]");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<PetInGameHost>();
                }
                return _instance;
            }
        }

        /// <summary>启动分发（GameScene 读档后调用）：按存档形态初始化，不触发切换逻辑</summary>
        public static void StartupDispatch(int form)
        {
            form = ClampForm(form);
            if (CurrentForm == form) return;
            CurrentForm = form;
            if (form == FormInGame)
            {
                // 游戏内形态：残留桌面进程发退出请求（播 Disappear 再退出），非硬杀
                PetSingleInstance.RequestQuit();
                PetProcessLauncher.LaunchSuppressed = true; // 游戏内形态：本轮启动不拉桌面进程
                instance.CreateInGameInstance();
                Debug.Log("[PetInGameHost] 启动形态=游戏画面内版");
            }
            else
            {
                Debug.Log("[PetInGameHost] 启动形态=桌面版（独立进程由 GameScene 拉起）");
            }
        }

        /// <summary>热切换形态（设置"派蒙"栏目下拉回调，即时生效）：
        /// 桌面→游戏内=写退出请求让桌宠播 Disappear 退场→等进程退出→创建本进程实例；
        /// 游戏内→桌面=播 Disappear 退场→销毁实例+拉起桌面进程。
        /// 2026-08-28 用户要求"切换时先播退出动作再杀死"。同形态重复调用幂等。</summary>
        public static void HotSwitchForm(int form)
        {
            form = ClampForm(form);
            if (CurrentForm == form) return;
            int oldForm = CurrentForm;
            CurrentForm = form;

            // 广播形态变更（Local UI 事件）：设置界面"派蒙"栏下拉据此刷新显示（见事件类注释）
            EventBusHub.Instance.Send(new OnPetFormChangedEvent { NewForm = form });

            if (form == FormInGame)
            {
                // 桌面→游戏内：写退出请求让桌宠进程播 Disappear 再退出（非硬杀）
                PetSingleInstance.RequestQuit();
                PetProcessLauncher.LaunchSuppressed = true;
                instance.StartCoroutine(instance.CreateInstanceAfterDesktopExit());
                Debug.Log($"[PetInGameHost] 热切换：桌面版→游戏画面内版（旧形态={oldForm}，等退场动画）");
            }
            else
            {
                // 游戏内→桌面：播 Disappear 退场→销毁→拉桌面进程
                instance.StartCoroutine(instance.LaunchDesktopAfterInGameExit());
                Debug.Log($"[PetInGameHost] 热切换：游戏画面内版→桌面版（旧形态={oldForm}，等退场动画）");
            }
        }

        /// <summary>三连击手势入口（2026-09-01）：持久化形态到存档 + 热切换（与设置下拉回调同路径，
        /// 差异只在持久化也归拢于此）。桌宠三连击经 IPC→PetChatIntent.ExecutePetGoInGame 调用
        ///（切游戏内形态）；游戏内宿主三连击直接调用（切桌面形态——游戏内宿主在主进程内，无需 IPC）。</summary>
        public static void GestureSwitchTo(int form)
        {
            form = ClampForm(form);
            var saveManager = Wargame.Instance?.Context?.Get<SaveManager>();
            var save = saveManager?.CurrentSave;
            if (save != null && save.petForm != form)
            {
                save.petForm = form;
                saveManager.SaveGame();
            }
            HotSwitchForm(form);
        }

        /// <summary>等桌面进程退出（最多 5s 超时强杀）后创建游戏内实例</summary>
        System.Collections.IEnumerator CreateInstanceAfterDesktopExit()
        {
            float deadline = Time.unscaledTime + 5f;
            while (Time.unscaledTime < deadline)
            {
                if (!PetSingleInstance.Probe()) break; // 桌宠进程已退出
                yield return new UnityEngine.WaitForSeconds(0.2f);
            }
            if (PetSingleInstance.Probe()) PetSingleInstance.TryKillPet(); // 超时强杀
            PetSingleInstance.ClearQuitRequest();
            CreateInGameInstance();
        }

        /// <summary>游戏内实例播 Disappear 退场→销毁→拉桌面进程。
        /// 2026-08-28 修复"退场播完后仍停留一会"：旧等待条件是 behavior.enabled（永为 true，空等满 4s
        /// 超时才销毁，期间行为层还落回待机把派蒙"站起来"）——改为退场完成回调标志，动画播完当帧即销毁
        /// （行为层同步加 _退场完成 冻结保持末帧，宿主控制器按 退场中 冻结拖拽/滚轮）。</summary>
        System.Collections.IEnumerator LaunchDesktopAfterInGameExit()
        {
            if (_inGameInstance != null)
            {
                var behavior = _inGameInstance.GetComponentInChildren<PetBehaviorController>();
                bool exitDone = false;
                bool exitTakeover = behavior != null && behavior.RequestExit(() => exitDone = true);
                if (exitTakeover)
                {
                    // 等退场动画播完（回调置标志；Disappear clip 约 2s，4s 超时兜底防动画异常卡死）
                    float deadline = Time.unscaledTime + 4f;
                    while (!exitDone && Time.unscaledTime < deadline) yield return null;
                }
            }
            DestroyInGameInstance();
            PetProcessLauncher.LaunchSuppressed = false;
            PetProcessLauncher.Launch();
        }

        private static int ClampForm(int form)
        {
            if (form != FormDesktop && form != FormInGame) return FormInGame;
#if !UNITY_STANDALONE_WIN
            if (form == FormDesktop) return FormInGame; // 非 Windows：桌面版不可用恒游戏内
#endif
            return form;
        }

        /// <summary>创建游戏内派蒙实例（渲染管线批次 B 落地：RT 画中画+Paimon prefab 化+宿主接口接线）</summary>
        private void CreateInGameInstance()
        {
            if (_inGameInstance != null) return;
            var prefab = Resources.Load<GameObject>("PaimonPet/PaimonInGameRoot");
            if (prefab == null)
            {
                Debug.LogWarning("[PetInGameHost] PaimonInGameRoot prefab 未找到（Resources/PaimonPet/），游戏内派蒙不可用");
                return;
            }
            _inGameInstance = Instantiate(prefab);
            DontDestroyOnLoad(_inGameInstance);
            // 宿主控制器（渲染+交互+IPetHost）——prefab 未预挂，运行时补挂（保 prefab 最小）
            var ctrl = _inGameInstance.GetComponent<PetInGameHostController>();
            if (ctrl == null) ctrl = _inGameInstance.AddComponent<PetInGameHostController>();
            HostInterface = ctrl;
            // 实例挪到远离游戏视锥的位置（RT 相机自含视野，主游戏相机不渲染派蒙——免层管理）
            _inGameInstance.transform.position = new Vector3(0f, 10000f, 0f);
            Debug.Log("[PetInGameHost] 游戏内实例已创建（RT 画中画）");
        }

        private void DestroyInGameInstance()
        {
            if (_inGameInstance == null) return;
            HostInterface = null;
            Destroy(_inGameInstance);
            _inGameInstance = null;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
