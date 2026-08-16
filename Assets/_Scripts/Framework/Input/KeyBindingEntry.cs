using System;
using System.Collections.Generic;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>
    /// 单个动作的按键绑定数据（可序列化，用于存档）
    /// </summary>
    [Serializable]
    public class KeyBindingEntry
    {
        public KeyAction action;
        public List<KeyCode> keys = new List<KeyCode>();
    }
}
