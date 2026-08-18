using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UObject = UnityEngine.Object;

namespace GIC.Framework
{
    /// <summary>
    /// 缓存控制中枢 — 统一管理所有 Addressables 资源的生命周期。
    /// [Component] 注册到 DI 容器，通过 Wargame.Update 每帧驱动待启动队列。
    ///
    /// 核心机制：
    /// 1. 引用计数 — 多个消费者共享同一 handle，最后一个 Release 才真正卸载
    /// 2. 去重 — 相同 address 的并发请求合并为一次 Addressables 加载
    /// 3. 帧预算 — 每帧最多花 FrameBudgetMs 启动新加载，避免单帧 I/O 峰值
    /// 4. 优先级队列 — High/Critical 请求插队，Low 延后
    /// 5. 预加载 — Preload 标记 IsPersistent，不消费引用计数，常驻内存
    /// </summary>
    [Component]
    public class AssetCache : IWargameManager
    {
        // ── 配置 ──

        /// <summary>每帧最多花多少毫秒启动新加载请求</summary>
        private const float FrameBudgetMs = 4f;

        /// <summary>同时进行中的加载上限（超过则排队等下一帧）</summary>
        private const int MaxConcurrentLoads = 8;

        // ── 缓存条目 ──

        private class CacheEntry
        {
            public AsyncOperationHandle Handle;
            public Type AssetType;
            public int RefCount;
            public bool IsPersistent;
            public bool IsLoading;
            public bool ReleaseOnLoad; // 加载期间被 Release，完成后释放
            public float LastAccessTime;
            public readonly List<Action<UObject>> PendingCallbacks = new();
        }

        // ── 待启动请求（帧预算控制） ──

        private class PendingRequest
        {
            public string Address;
            public Type AssetType;
            public LoadPriority Priority;
            public int RefCount;
            public bool IsPersistent;
            public readonly List<Action<UObject>> Callbacks = new();
        }

        // ── 状态 ──

        private readonly Dictionary<string, CacheEntry> _cache = new();
        private readonly List<PendingRequest> _pendingQueue = new();
        private int _activeLoadCount;

        // ── 公共 API ──

        /// <summary>
        /// 异步加载资源（引用计数 + 去重）。
        /// 已缓存且加载完 → 立即回调；已缓存但加载中 → 挂载回调；全新 → 入队等待帧预算调度。
        /// 每次 LoadAsync 使 RefCount+1，必须配对调用 Release。
        /// </summary>
        public void LoadAsync<T>(string address, Action<T> onComplete, LoadPriority priority = LoadPriority.Normal)
            where T : UnityEngine.Object
        {
            // 1. 已缓存
            if (_cache.TryGetValue(address, out var entry))
            {
                entry.RefCount++;
                entry.LastAccessTime = Time.realtimeSinceStartup;

                if (entry.IsLoading)
                {
                    entry.PendingCallbacks.Add(obj => onComplete?.Invoke(obj as T));
                }
                else if (entry.Handle.IsValid() && entry.Handle.Status == AsyncOperationStatus.Succeeded)
                {
                    onComplete?.Invoke(entry.Handle.Result as T);
                }
                return;
            }

            // 2. 已在待启动队列 → 合并请求
            for (int i = 0; i < _pendingQueue.Count; i++)
            {
                if (_pendingQueue[i].Address == address)
                {
                    var req = _pendingQueue[i];
                    req.RefCount++;
                    // 类型升级：如果原请求是 Object 但新请求更具体（如 Sprite），升级类型
                    if (req.AssetType == typeof(UObject) && typeof(T) != typeof(UObject))
                        req.AssetType = typeof(T);
                    req.Callbacks.Add(obj => onComplete?.Invoke(obj as T));
                    if (priority > req.Priority)
                    {
                        req.Priority = priority;
                        SortPendingQueue();
                    }
                    return;
                }
            }

            // 3. 全新请求 → 入队
            var pending = new PendingRequest
            {
                Address = address,
                AssetType = typeof(T),
                Priority = priority,
                RefCount = 1
            };
            pending.Callbacks.Add(obj => onComplete?.Invoke(obj as T));
            InsertPending(pending);
        }

        /// <summary>
        /// 预加载资源到缓存，不消费引用计数。
        /// 标记 IsPersistent=true，不会被 LRU 淘汰也不会因 Release 归零而释放。
        /// 后续 LoadAsync 会在预加载基础上 RefCount+1，Release 时 RefCount-1 回到 0 但仍常驻。
        /// </summary>
        public void Preload<T>(string address, LoadPriority priority = LoadPriority.Low)
            where T : UObject
        {
            // 已缓存 → 标记 persistent
            if (_cache.TryGetValue(address, out var entry))
            {
                entry.IsPersistent = true;
                return;
            }

            // 已在队列 → 标记 persistent + 类型升级
            for (int i = 0; i < _pendingQueue.Count; i++)
            {
                if (_pendingQueue[i].Address == address)
                {
                    var req = _pendingQueue[i];
                    req.IsPersistent = true;
                    if (req.AssetType == typeof(UObject) && typeof(T) != typeof(UObject))
                        req.AssetType = typeof(T);
                    return;
                }
            }

            // 全新 → 入队（RefCount=0, persistent=true）
            var pending = new PendingRequest
            {
                Address = address,
                AssetType = typeof(T),
                Priority = priority,
                RefCount = 0,
                IsPersistent = true
            };
            InsertPending(pending);
        }

        /// <summary>
        /// 释放引用（RefCount-1）。归零且非 persistent 时真正释放 Addressables handle。
        /// </summary>
        public void Release(string address)
        {
            if (!_cache.TryGetValue(address, out var entry)) return;

            entry.RefCount--;

            if (entry.RefCount <= 0 && !entry.IsPersistent)
            {
                if (entry.IsLoading)
                {
                    // 加载未完成，标记完成后释放
                    entry.ReleaseOnLoad = true;
                }
                else
                {
                    ReleaseHandle(address, entry);
                }
            }
        }

        /// <summary>
        /// 卸载一个 Persistent 预载：清除 IsPersistent 标记后按普通资源释放。
        /// 有活跃消费者（RefCount&gt;0，如大地图正开着显示该瓦片）时不释放，等最后一个 Release 归零才真卸。
        /// 在待启动队列中的预载直接移除（从未加载）。
        /// </summary>
        public void UnloadPreload(string address)
        {
            if (_cache.TryGetValue(address, out var entry))
            {
                entry.IsPersistent = false;
                if (entry.RefCount <= 0)
                {
                    if (entry.IsLoading)
                        entry.ReleaseOnLoad = true;
                    else
                        ReleaseHandle(address, entry);
                }
                return;
            }

            // 尚在待启动队列（未开始加载）→ 直接移除
            for (int i = 0; i < _pendingQueue.Count; i++)
            {
                if (_pendingQueue[i].Address == address)
                {
                    _pendingQueue.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>资源是否已缓存（加载完成，可直接取用）</summary>
        public bool IsCached(string address)
        {
            return _cache.TryGetValue(address, out var entry)
                   && !entry.IsLoading
                   && entry.Handle.IsValid();
        }

        /// <summary>资源是否正在加载中</summary>
        public bool IsLoading(string address)
        {
            return _cache.TryGetValue(address, out var entry) && entry.IsLoading;
        }

        /// <summary>资源是否在待启动队列中</summary>
        public bool IsPending(string address)
        {
            for (int i = 0; i < _pendingQueue.Count; i++)
            {
                if (_pendingQueue[i].Address == address) return true;
            }
            return false;
        }

        // ── IWargameManager ──

        public void Start() { }

        /// <summary>
        /// 每帧处理待启动队列：在帧预算内启动尽可能多的加载请求。
        /// </summary>
        public void Update(float deltaTime)
        {
            if (_pendingQueue.Count == 0) return;

            float frameStart = Time.realtimeSinceStartup;

            while (_pendingQueue.Count > 0 && _activeLoadCount < MaxConcurrentLoads)
            {
                float elapsedMs = (Time.realtimeSinceStartup - frameStart) * 1000f;
                if (elapsedMs >= FrameBudgetMs) break;

                var req = _pendingQueue[0];
                _pendingQueue.RemoveAt(0);
                InitiateLoad(req);
            }
        }

        // ── 内部方法 ──

        private void InitiateLoad(PendingRequest req)
        {
            _activeLoadCount++;

            var entry = new CacheEntry
            {
                AssetType = req.AssetType,
                RefCount = req.RefCount,
                IsPersistent = req.IsPersistent,
                LastAccessTime = Time.realtimeSinceStartup,
                IsLoading = true
            };
            entry.PendingCallbacks.AddRange(req.Callbacks);
            _cache[req.Address] = entry;

            var handle = CreateLoadHandle(req.Address, req.AssetType);
            entry.Handle = handle;

            string address = req.Address;
            handle.Completed += op =>
            {
                _activeLoadCount--;
                entry.IsLoading = false;
                entry.LastAccessTime = Time.realtimeSinceStartup;

                if (op.Status == AsyncOperationStatus.Succeeded)
                {
                    // 加载期间被 Release 且非 persistent → 直接释放
                    if (entry.ReleaseOnLoad && entry.RefCount <= 0 && !entry.IsPersistent)
                    {
                        ReleaseHandle(address, entry);
                        return;
                    }

                    // 逐个回调
                    foreach (var cb in entry.PendingCallbacks)
                        cb?.Invoke(op.Result as UObject);
                }
                else
                {
                    GICLog.Warn($"[AssetCache] 加载失败: {address}");
                    // 失败也回调（传 null），避免调用方永久阻塞
                    foreach (var cb in entry.PendingCallbacks)
                        cb?.Invoke(null);
                }

                entry.PendingCallbacks.Clear();
            };
        }

        /// <summary>
        /// 根据类型创建正确的 Addressables 加载 handle。
        /// 必须用具体的泛型类型调用 LoadAssetAsync&lt;T&gt;，否则对于
        /// Texture2D+Sprite 子资产的图片，LoadAssetAsync&lt;Object&gt; 会返回 Texture2D 而非 Sprite。
        /// </summary>
        private AsyncOperationHandle CreateLoadHandle(string address, Type type)
        {
            if (type == typeof(Sprite))
                return Addressables.LoadAssetAsync<Sprite>(address);
            if (type == typeof(Texture2D))
                return Addressables.LoadAssetAsync<Texture2D>(address);
            if (type == typeof(GameObject))
                return Addressables.LoadAssetAsync<GameObject>(address);
            if (type == typeof(AudioClip))
                return Addressables.LoadAssetAsync<AudioClip>(address);
            if (type == typeof(Material))
                return Addressables.LoadAssetAsync<Material>(address);
            if (type == typeof(TextAsset))
                return Addressables.LoadAssetAsync<TextAsset>(address);
            return Addressables.LoadAssetAsync<UObject>(address);
        }

        private void ReleaseHandle(string address, CacheEntry entry)
        {
            if (entry.Handle.IsValid())
                Addressables.Release(entry.Handle);
            _cache.Remove(address);
        }

        private void InsertPending(PendingRequest req)
        {
            // 按优先级降序插入（高优先级在前）
            int i = 0;
            for (; i < _pendingQueue.Count; i++)
            {
                if (_pendingQueue[i].Priority < req.Priority)
                    break;
            }
            _pendingQueue.Insert(i, req);
        }

        private void SortPendingQueue()
        {
            _pendingQueue.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }
    }
}
