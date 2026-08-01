using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    /// <summary>
    /// 网络事件类型注册表 — 用 ushort ID 代替 AssemblyQualifiedName
    /// 启动时自动扫描所有 BaseEvent 子类，ID 基于型名哈希保证多端一致
    /// </summary>
    public static class NetworkEventRegistry
    {
        private static readonly Dictionary<Type, ushort> _typeToId = new();
        private static readonly Dictionary<ushort, Type> _idToType = new();
        private static bool _initialized;

        /// <summary>
        /// 启动时扫描所有 BaseEvent 子类自动注册，无需手动维护列表
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            // 扫描所有程序集中继承 BaseEvent 的非抽象类
            var eventTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(asm =>
                {
                    try { return asm.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .Where(t => t.IsClass && !t.IsAbstract && typeof(BaseEvent).IsAssignableFrom(t));

            foreach (var type in eventTypes)
            {
                RegisterType(type);
            }

            Debug.Log($"[NetworkEventRegistry] 自动扫描注册 {_typeToId.Count} 种网络事件类型");
        }

        /// <summary>
        /// 确定性 ID：基于类型全名的 FNV-1a 哈希（所有平台一致）
        /// </summary>
        private static ushort ComputeId(string fullName)
        {
            // FNV-1a 16-bit
            ushort hash = 0x811C;
            foreach (char c in fullName)
            {
                hash ^= (ushort)c;
                hash *= 0x0101; // FNV prime for 16-bit
            }
            if (hash == 0) hash = 1; // 0 保留为无效
            return hash;
        }

        private static void RegisterType(Type type)
        {
            ushort id = ComputeId(type.FullName ?? type.Name);

            // 冲突检测
            if (_idToType.TryGetValue(id, out var existing) && existing != type)
                Debug.LogWarning($"[NetworkEventRegistry] ID 冲突: {id} 已被 {existing.Name} 占用，{type.Name} 使用备用 ID");

            // 线性探测解冲突
            while (_idToType.ContainsKey(id))
                id = (ushort)(id == ushort.MaxValue ? 1 : id + 1);

            _typeToId[type] = id;
            _idToType[id] = type;
        }

        /// <summary>
        /// 将事件序列化为 NetworkEventMessage
        /// </summary>
        public static NetworkEventMessage Pack(BaseEvent eventData, string senderID = null,
            string targetPlayerID = null, List<string> targetIDs = null)
        {
            if (!_initialized) Initialize();

            return new NetworkEventMessage
            {
                EventId = _typeToId.TryGetValue(eventData.GetType(), out var id) ? id : (ushort)0,
                SenderID = senderID ?? "",
                TargetID = targetPlayerID ?? "",
                Json = JsonUtility.ToJson(eventData)
            };
        }

        /// <summary>
        /// 从 NetworkEventMessage 反序列化为事件对象
        /// </summary>
        public static BaseEvent Unpack(NetworkEventMessage msg)
        {
            if (!_initialized) Initialize();

            if (msg.EventId == 0 || !_idToType.TryGetValue(msg.EventId, out var type))
            {
                Debug.LogWarning($"[NetworkEventRegistry] 未知的 EventId: {msg.EventId}");
                return null;
            }

            try
            {
                var evt = JsonUtility.FromJson(msg.Json, type) as BaseEvent;
                if (evt != null)
                    evt.SourcePlayerID = msg.SenderID;
                return evt;
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkEventRegistry] 反序列化失败: {type.Name}, error={e.Message}");
                return null;
            }
        }
    }

}


