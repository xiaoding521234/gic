using System;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 战斗序列化器接口（隔离 JsonUtility，将来可换二进制且不改调用方）
    /// </summary>
    public interface IBattleSerializer
    {
        string Serialize<T>(T message) where T : class;
        T Deserialize<T>(string json) where T : class;
    }

    /// <summary>
    /// JSON 序列化实现（调试友好：片级消息肉眼可读）
    /// </summary>
    public class BattleJsonSerializer : IBattleSerializer
    {
        public static readonly BattleJsonSerializer Instance = new BattleJsonSerializer();

        public string Serialize<T>(T message) where T : class
        {
            return JsonUtility.ToJson(message);
        }

        public T Deserialize<T>(string json) where T : class
        {
            return JsonUtility.FromJson<T>(json);
        }
    }
}
