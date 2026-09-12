using System;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>
    /// "任意输入继续"统一判定（docs/24 §5，P3）——替代散落各处的
    /// `Input.anyKeyDown ‖ Input.GetMouseButtonDown(0/1/2)` 惯用式。
    /// 语义 = 任意键 or 任意指针按下（含按在 UI 上；触摸/鼠标归一由 PointerInputPump 收口）。
    /// **输入锁期间照常可用**（Splash 保护期/祈愿抽卡锁内都要能"点击继续"）——
    /// hub 在锁门之前就计算 AnyPointerBeganThisFrame，锁只冻结手势分发不冻结本判定。
    /// InputLocks 同款静态门面模式：GestureHub 构造时 Bind，Wargame 未初始化时自动回退纯键判。
    /// </summary>
    public static class WaitForAnyTap
    {
        private static GestureHub _backend;

        /// <summary>GestureHub 构造时绑定（勿手动调用）</summary>
        internal static void Bind(GestureHub hub) => _backend = hub;

        /// <summary>本帧是否有"任意继续"输入：任意键 or 任意指针按下</summary>
        public static bool Any()
        {
            if (Input.anyKeyDown) return true;
            return _backend != null && _backend.AnyPointerBeganThisFrame;
        }
    }
}
