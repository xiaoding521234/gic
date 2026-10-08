using System;
using System.Collections.Generic;
using GIC.Framework;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 战斗传输通道接口（B1-B6 本地直通；B7 换 Mirror 实现上层零改动）
    /// 铁律：战斗逻辑不走 EventBusHub，全部消息经本通道（docs/22 §4）
    /// </summary>
    public interface IBattleTransport
    {
        /// <summary>Host → 全部客户端</summary>
        void HostSend(BattleMessageType type, object message);

        /// <summary>客户端 → Host</summary>
        void ClientSend(BattleMessageType type, object message);

        /// <summary>注册 Host 侧收包回调（客户端上行消息）</summary>
        void RegisterHostHandler(Action<BattleMessageType, string> handler);

        /// <summary>注册客户端收包回调（Host 下行消息）</summary>
        void RegisterClientHandler(Action<BattleMessageType, string> handler);
    }

    /// <summary>
    /// 本地传输实现（Host 与 Client 同进程）。
    /// 仍走 JSON wire format + 信封，保证本地全链路验证了可序列化性——
    /// B7 换 Mirror 时两侧反序列化代码不变，仅传输介质变化。
    /// </summary>
    public class LocalBattleTransport : IBattleTransport
    {
        private readonly IBattleSerializer _serializer;
        private Action<BattleMessageType, string> _hostHandler;
        private Action<BattleMessageType, string> _clientHandler;

        public LocalBattleTransport(IBattleSerializer serializer = null)
        {
            _serializer = serializer ?? BattleJsonSerializer.Instance;
        }

        public void RegisterHostHandler(Action<BattleMessageType, string> handler) => _hostHandler = handler;
        public void RegisterClientHandler(Action<BattleMessageType, string> handler) => _clientHandler = handler;

        public void HostSend(BattleMessageType type, object message)
        {
            var envelope = new BattleMessageEnvelope
            {
                messageType = type,
                json = _serializer.Serialize(message),
            };
            // 双层信封序列化持续验证可序列化性（B7 联机前置保障——Serialize 抛异常即暴露消息缺陷；
            // 批9 复审①：原每消息 Debug.Log 刷屏已删——命令流每回合几十条×2 方向持续刷、且
            // Debug.Log 不受 GICLog 编译期删除纪律管；验证本质=Serialize 调用本身每条消息都在执行）
            _serializer.Serialize(envelope);
            _clientHandler?.Invoke(envelope.messageType, envelope.json);
        }

        public void ClientSend(BattleMessageType type, object message)
        {
            var envelope = new BattleMessageEnvelope
            {
                messageType = type,
                json = _serializer.Serialize(message),
            };
            _serializer.Serialize(envelope); // 序列化验证保留、Log 删（同 HostSend）
            _hostHandler?.Invoke(envelope.messageType, envelope.json);
        }
    }

    /// <summary>
    /// 类型化消息路由器（两侧各持一个；按消息类型分发表回调，避免大 switch）
    /// </summary>
    public class BattleMessageRouter
    {
        private readonly Dictionary<BattleMessageType, Action<string>> _handlers = new Dictionary<BattleMessageType, Action<string>>();

        public void Register<T>(BattleMessageType type, Action<T> handler) where T : class
        {
            _handlers[type] = json => handler(BattleJsonSerializer.Instance.Deserialize<T>(json));
        }

        public void RegisterRaw(BattleMessageType type, Action<string> handler)
        {
            _handlers[type] = handler;
        }

        public void Handle(BattleMessageType type, string json)
        {
            if (_handlers.TryGetValue(type, out var handler))
                handler(json);
            else
                GICLog.Warn($"[BattleMessageRouter] 未注册的消息类型: {type}");
        }
    }
}
