using System.Collections.Generic;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>
    /// 语境相机解析单源（2026-10-06 拍板方案 A）。
    /// Camera.main 按 MainCamera tag 解析，而 GIC 战斗/地图相机均 Untagged：
    /// Single 战斗场景解析为 null、地图 additive 弹层解析成大厅相机——
    /// 历史两坑（战斗立牌朝向需显式指定、Pet 相机 tag 抢占致大厅背景缩爆）均属此族。
    /// 主相机由 CameraContextAnchor 随场景生命周期自动入栈/出栈（additive 弹层嵌套天然栈序：
    /// 大厅恒在栈底、地图弹层打开压顶、卸载自动弹回）。
    /// Resolve() = 栈顶语境相机；未注册语境（Splash/测试场/桌宠独立场景）回落 Camera.main，
    /// 与旧解析恒等。
    /// </summary>
    public static class CameraContext
    {
        // 尾部=栈顶（最新注册的语境）。Anchor 的 OnEnable/OnDisable 顺序不保证严格配对，
        // 故 Pop 按引用移除而非弹顶，乱序注销也安全。
        private static readonly List<Camera> _stack = new();

        /// <summary>当前语境相机：无任何注册时回落 Camera.main（与旧解析恒等）</summary>
        public static Camera Resolve()
        {
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var cam = _stack[i];
                if (cam != null) return cam;
                _stack.RemoveAt(i); // 惰性清 destroyed 残留（场景卸载漏注销的兜底）
            }
            return Camera.main;
        }

        public static void Push(Camera cam)
        {
            if (cam != null && !_stack.Contains(cam)) _stack.Add(cam);
        }

        public static void Pop(Camera cam)
        {
            _stack.Remove(cam);
        }
    }

    /// <summary>
    /// 挂场景主相机的自登记组件：OnEnable 入栈、OnDisable 出栈。
    /// 场景加载/卸载自动维护语境栈，无需任何代码手工登记/注销。
    /// </summary>
    public class CameraContextAnchor : MonoBehaviour
    {
        private Camera _camera;

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            CameraContext.Push(_camera);
        }

        private void OnDisable()
        {
            if (_camera == null) return;
            CameraContext.Pop(_camera);
            _camera = null;
        }
    }
}
