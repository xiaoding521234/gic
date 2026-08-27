using UnityEngine;

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
        public const int 形态_桌面 = 0;
        public const int 形态_游戏内 = 1;

        private static PetInGameHost _instance;
        private GameObject _游戏内实例;

        /// <summary>游戏内宿主接口（IPetHost）——行为层/视线层经此分发（桌面形态走 PetWindowController 字段）。
        /// 由游戏内宿主组件（渲染管线批次 B 落地后）在 Awake 注入。</summary>
        public static IPetHost 宿主接口 { get; internal set; }

        /// <summary>当前实际生效形态（-1=未初始化；启动分发/热切换后更新）</summary>
        public static int 当前形态 { get; private set; } = -1;

        /// <summary>宿主单例（按需创建，DontDestroyOnLoad 跨场景存活）</summary>
        private static PetInGameHost 实例
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

        /// <summary>启动分发（GameScene.Awake 读档后调用）：按存档形态初始化，不触发切换逻辑</summary>
        public static void 启动形态(int form)
        {
            form = 钳制合法形态(form);
            if (当前形态 == form) return;
            当前形态 = form;
            if (form == 形态_游戏内)
            {
                PetProcessLauncher.LaunchSuppressed = true; // 游戏内形态：本轮启动不拉桌面进程
                实例.创建游戏内实例();
                Debug.Log("[PetInGameHost] 启动形态=游戏画面内版");
            }
            else
            {
                Debug.Log("[PetInGameHost] 启动形态=桌面版（独立进程由 GameScene 拉起）");
            }
        }

        /// <summary>热切换形态（设置"派蒙"栏目下拉回调，即时生效）：
        /// 桌面→游戏内=关闭桌面进程+创建本进程实例；游戏内→桌面=销毁实例+拉起桌面进程。
        /// 同形态重复调用幂等。</summary>
        public static void 热切换形态(int form)
        {
            form = 钳制合法形态(form);
            if (当前形态 == form) return;
            int 旧形态 = 当前形态;
            当前形态 = form;

            if (form == 形态_游戏内)
            {
                // 旧桌面进程关闭（pid 文件+进程名校验，手动启动的旧派蒙关不掉——设置切换前先双击关掉她）
                PetSingleInstance.TryKillPet();
                PetProcessLauncher.LaunchSuppressed = true;
                实例.创建游戏内实例();
                Debug.Log($"[PetInGameHost] 热切换：桌面版→游戏画面内版（旧形态={旧形态}）");
            }
            else
            {
                实例.销毁游戏内实例();
                PetProcessLauncher.LaunchSuppressed = false;
                PetProcessLauncher.Launch();
                Debug.Log($"[PetInGameHost] 热切换：游戏画面内版→桌面版（旧形态={旧形态}）");
            }
        }

        private static int 钳制合法形态(int form)
        {
            if (form != 形态_桌面 && form != 形态_游戏内) return 形态_游戏内;
#if !UNITY_STANDALONE_WIN
            if (form == 形态_桌面) return 形态_游戏内; // 非 Windows：桌面版不可用恒游戏内
#endif
            return form;
        }

        /// <summary>创建游戏内派蒙实例（渲染管线批次 B 落地：RT 画中画+Paimon prefab 化+宿主接口接线）</summary>
        private void 创建游戏内实例()
        {
            if (_游戏内实例 != null) return;
            var prefab = Resources.Load<GameObject>("PaimonPet/PaimonInGameRoot");
            if (prefab == null)
            {
                Debug.LogWarning("[PetInGameHost] PaimonInGameRoot prefab 未找到（Resources/PaimonPet/），游戏内派蒙不可用");
                return;
            }
            _游戏内实例 = Instantiate(prefab);
            DontDestroyOnLoad(_游戏内实例);
            // 批次 B 尾段：RT 相机+RawImage 画中画+输入桥接线（当前仅实例化，行为层空转安全）
            Debug.Log("[PetInGameHost] 游戏内实例已创建（渲染管线待接）");
        }

        private void 销毁游戏内实例()
        {
            if (_游戏内实例 == null) return;
            Destroy(_游戏内实例);
            _游戏内实例 = null;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
