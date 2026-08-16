using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>
    /// 统一输入管理器 — 管理按键绑定、动作派发和 UI 关闭。
    /// 架构参考米哈游：InputLock 列表控制输入权限 + IClosable 栈管理 UI 关闭。
    /// </summary>
    [Component]
    public class InputManager : IWargameManager
    {
        private readonly SaveManager _saveManager;

        /// <summary>
        /// 动作触发事件 — 外部订阅（如 SplashScreen 订阅 Confirm）
        /// </summary>
        public event Action<KeyAction> OnActionTriggered;

        // ──────────────────────────────────────────────
        //  InputLock（米哈游风格）
        //  按 (owner, reason) 精确配对，取代盲目弹栈：
        //   · Push 同 (owner,reason) 去重 — 协程重入时无需先"补 pop"
        //   · Pop 与栈顶无关且可重复调用 — 不会误删别人的锁
        //   · PopAll(owner) 在 OnDestroy 兜底 — 锁泄漏（输入永久冻结）的保险丝
        // ──────────────────────────────────────────────

        private sealed class InputLockEntry
        {
            public object Owner;
            public string Reason;
        }

        private readonly List<InputLockEntry> _inputLocks = new List<InputLockEntry>();

        public void PushInputLock(object owner, string reason)
        {
            if (owner == null || string.IsNullOrEmpty(reason)) return;
            if (HasInputLock(owner, reason)) return; // 去重：同一持有者的同名锁只入列一次

            _inputLocks.Add(new InputLockEntry { Owner = owner, Reason = reason });
        }

        public void PopInputLock(object owner, string reason)
        {
            // 从最新向旧查找匹配项移除；未找到时静默（幂等，允许重复/中断路径多调一次）
            for (int i = _inputLocks.Count - 1; i >= 0; i--)
            {
                if (_inputLocks[i].Owner == owner && _inputLocks[i].Reason == reason)
                {
                    _inputLocks.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// 释放 owner 持有的全部输入锁 — 各持有锁的 MonoBehaviour 在 OnDestroy 中调用兜底。
        /// 正常配对的锁早已 pop，此处清理到锁即说明泄漏，记录警告便于定位。
        /// </summary>
        public int PopAllInputLocks(object owner)
        {
            int removed = 0;
            for (int i = _inputLocks.Count - 1; i >= 0; i--)
            {
                if (_inputLocks[i].Owner != owner) continue;

                GICLog.Warn($"[InputLock] 释放泄漏锁: {DescribeLock(_inputLocks[i])}");
                _inputLocks.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        /// <summary>持有任意输入锁（所有动作派发暂停）</summary>
        public bool IsInputLocked => _inputLocks.Count > 0;

        /// <summary>是否持有指定原因的锁（如 GameScene 查询 SceneTransition）</summary>
        public bool HasInputLock(string reason)
        {
            for (int i = 0; i < _inputLocks.Count; i++)
                if (_inputLocks[i].Reason == reason) return true;
            return false;
        }

        /// <summary>当前活跃锁描述（诊断用），如 "Entering(SettingsScreen), SceneTransition(GameScene)"</summary>
        public string GetActiveLocksDescription()
        {
            if (_inputLocks.Count == 0) return "(无)";

            var sb = new StringBuilder();
            for (int i = 0; i < _inputLocks.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(DescribeLock(_inputLocks[i]));
            }
            return sb.ToString();
        }

        private static string DescribeLock(InputLockEntry entry)
            => $"{entry.Reason}({entry.Owner?.GetType().Name ?? "null"})";

        private bool HasInputLock(object owner, string reason)
        {
            for (int i = 0; i < _inputLocks.Count; i++)
                if (_inputLocks[i].Owner == owner && _inputLocks[i].Reason == reason) return true;
            return false;
        }

        // ──────────────────────────────────────────────
        //  可关闭 UI 栈（后注册 = 更顶层）
        // ──────────────────────────────────────────────

        private readonly List<IClosable> _closables = new List<IClosable>();

        private void CloseTopUI()
        {
            // 从顶层向下查找，关闭第一个注册的 IClosable
            // 各 IClosable 自行在 Close() 入口检查状态（isClosing 防重入等）
            for (int i = _closables.Count - 1; i >= 0; i--)
            {
                if (_closables[i] == null)
                {
                    _closables.RemoveAt(i);
                    continue;
                }

                _closables[i].Close();
                return;
            }

            // 无注册的 IClosable — 不执行任何操作（不 fallback 到 GoBack）
        }

        public void RegisterClosable(IClosable closable)
        {
            if (closable != null && !_closables.Contains(closable))
                _closables.Add(closable);
        }

        public void UnregisterClosable(IClosable closable)
        {
            _closables.Remove(closable);
        }

        // ──────────────────────────────────────────────
        //  按键绑定与动作派发
        // ──────────────────────────────────────────────

        private readonly Dictionary<KeyAction, List<KeyCode>> _bindings = new Dictionary<KeyAction, List<KeyCode>>();

        // 内置动作处理注册表 — 新增内置动作时在此（或运行时 RegisterBuiltinHandler）注册，
        // 派发循环无需修改
        private readonly Dictionary<KeyAction, Action> _builtinHandlers;

        /// <summary>注册内置动作处理器（如 CloseUI → CloseTopUI）</summary>
        public void RegisterBuiltinHandler(KeyAction action, Action handler)
        {
            _builtinHandlers[action] = handler;
        }

        public InputManager(SaveManager saveManager)
        {
            _saveManager = saveManager;

            _builtinHandlers = new Dictionary<KeyAction, Action>
            {
                { KeyAction.CloseUI, CloseTopUI },
            };
        }

        [PostConstruct]
        public void Init()
        {
            InputLocks.RegisterBackend(this); // 静态门面后端注册（此前门面调用为空操作）
            LoadBindings();
        }

        public void Start() { }

        public void Update(float deltaTime)
        {
            if (_isRebinding) return;

            // InputLock 非空时暂停所有动作派发
            if (IsInputLocked) return;

            // 重绑定完成后跳过一帧，避免捕获的按键立即触发动作
            if (_skipNextFrame)
            {
                _skipNextFrame = false;
                return;
            }

            foreach (var kvp in _bindings)
            {
                if (!IsAnyKeyDown(kvp.Value)) continue;

                OnActionTriggered?.Invoke(kvp.Key);

                if (_builtinHandlers.TryGetValue(kvp.Key, out var handler))
                    handler();
            }
        }

        private static bool IsAnyKeyDown(List<KeyCode> keys)
        {
            for (int i = 0; i < keys.Count; i++)
                if (Input.GetKeyDown(keys[i])) return true;
            return false;
        }

        // ──────────────────────────────────────────────
        //  绑定查询与修改
        // ──────────────────────────────────────────────

        /// <summary>获取动作在指定槽位的按键（主键 slot=0 / 副键 slot=1），未绑定为 KeyCode.None</summary>
        public KeyCode GetKey(KeyAction action, int slot)
        {
            if (_bindings.TryGetValue(action, out var keys) && slot >= 0 && slot < keys.Count)
                return keys[slot];
            return KeyCode.None;
        }

        /// <summary>获取动作的全部按键副本（修改请走 RebindKey）</summary>
        public List<KeyCode> GetKeys(KeyAction action)
        {
            if (_bindings.TryGetValue(action, out var keys))
                return new List<KeyCode>(keys);
            return new List<KeyCode>();
        }

        public KeyAction? FindKeyConflict(KeyCode key, KeyAction excludeAction)
        {
            foreach (var kvp in _bindings)
            {
                if (kvp.Key == excludeAction) continue;
                foreach (var bound in kvp.Value)
                {
                    if (bound == key)
                        return kvp.Key;
                }
            }
            return null;
        }

        public void RebindKey(KeyAction action, int slot, KeyCode newKey)
        {
            if (!_bindings.ContainsKey(action))
                _bindings[action] = new List<KeyCode>();

            var keys = _bindings[action];
            while (keys.Count <= slot)
                keys.Add(KeyCode.None);

            keys[slot] = newKey;
            SaveBindings();
        }

        public void ResetAction(KeyAction action)
        {
            if (DefaultBindings.TryGetValue(action, out var defaults))
            {
                _bindings[action] = new List<KeyCode>(defaults);
                SaveBindings();
            }
        }

        public void ResetAllBindings()
        {
            _bindings.Clear();
            foreach (var kvp in DefaultBindings)
                _bindings[kvp.Key] = new List<KeyCode>(kvp.Value);
            SaveBindings();
        }

        // ──────────────────────────────────────────────
        //  重绑定支持
        // ──────────────────────────────────────────────

        private bool _isRebinding;
        /// <summary>正在重绑定 — 暂停所有动作派发</summary>
        public bool IsRebinding => _isRebinding;

        /// <summary>
        /// 重绑定被抢占时触发 — 当前监听者应取消监听。
        /// </summary>
        public event Action OnRebindCancelled;

        private bool _skipNextFrame;

        public void BeginRebind()
        {
            if (_isRebinding)
                OnRebindCancelled?.Invoke();

            _isRebinding = true;
        }

        public void EndRebind()
        {
            _isRebinding = false;
            _skipNextFrame = true;
        }

        // ──────────────────────────────────────────────
        //  存档
        // ──────────────────────────────────────────────

        private static readonly Dictionary<KeyAction, KeyCode[]> DefaultBindings = new Dictionary<KeyAction, KeyCode[]>
        {
            { KeyAction.CloseUI, new[] { KeyCode.Escape, KeyCode.Mouse1 } },
            { KeyAction.Confirm, new[] { KeyCode.Space, KeyCode.Return } },
        };

        private void LoadBindings()
        {
            _bindings.Clear();

            var savedBindings = _saveManager?.CurrentSave?.keyBindings;
            if (savedBindings != null)
            {
                foreach (var entry in savedBindings)
                {
                    _bindings[entry.action] = new List<KeyCode>(entry.keys);
                }
            }

            foreach (var kvp in DefaultBindings)
            {
                if (!_bindings.ContainsKey(kvp.Key))
                    _bindings[kvp.Key] = new List<KeyCode>(kvp.Value);
            }
        }

        private void SaveBindings()
        {
            if (_saveManager?.CurrentSave == null) return;

            var save = _saveManager.CurrentSave;
            save.keyBindings.Clear();
            foreach (var kvp in _bindings)
            {
                save.keyBindings.Add(new KeyBindingEntry
                {
                    action = kvp.Key,
                    keys = new List<KeyCode>(kvp.Value)
                });
            }
            _saveManager.SaveGame();
        }
    }
}
