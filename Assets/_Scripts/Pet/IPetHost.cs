using System;
using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 派蒙宿主接口（docs/19 §6.4，2026-08-27 批次 B）：行为层/视线层对"窗口控制器"的依赖抽象——
    /// 桌面形态由 PetWindowController 实现，游戏画面内形态由 PetInGameHostController 实现。
    /// 现有组件的序列化字段（窗口控制器）类型不变（场景引用零风险），运行时经 宿主 属性分发：
    /// 字段已接线（桌面场景）用之，否则回落 PetInGameHost.宿主（游戏内实例）。
    /// </summary>
    public interface IPetHost
    {
        /// <summary>光标的 Unity 屏幕坐标（派蒙相机视口系，左下原点）——视线跟随/接近判定用。
        /// 桌面版=Win32 全局光标投影；游戏内版=鼠标在画中画内的映射（画布外返回 false 或边缘值）。</summary>
        bool TryGetCursorUnityScreenPos(out Vector2 unityScreenPos);

        /// <summary>物理交互进行中（拖拽跟随/收尾归零）——行为层压制触发用</summary>
        bool PhysicsBusy { get; }

        /// <summary>正在拖拽派蒙（行为层打断打招呼/小动作用；含收尾全程）</summary>
        bool IsDragging { get; }

        /// <summary>最近一次拖拽时长（秒，松手冻结；-1=无）——放下反应分档用</summary>
        float DragSeconds { get; }

        /// <summary>暂停命中网格重烘（行为层在单次动作期间置真）</summary>
        bool PauseHitBaking { get; set; }

        /// <summary>命中网格的世界包围盒（接近判定用）。碰撞体未就绪时返回 false。</summary>
        bool TryGetHitWorldBounds(out Bounds bounds);

        /// <summary>坐定中（行为层据此切换待机动作=坐姿）。桌面版=PetEdgeSitController.坐定中；游戏内版=屏幕坐定中。</summary>
        bool IsSeated { get; }

        /// <summary>坐姿动作名（行为层据此选 clip 播放）。桌面版=PetEdgeSitController.坐姿动作；游戏内版=Inspector 配置。</summary>
        string SitAnim { get; }

        /// <summary>松手时尝试边缘坐接管（2026-08-31 批 7 解耦：行为层原直接引用桌面专属 PetEdgeSitController）。
        /// 桌面形态=边坐控制器磁吸探测；游戏内=恒 false（屏幕坐已在宿主物理收口评估）。
        /// 返回 true=已接管播坐姿，行为层跳过回待机。</summary>
        bool TrySnapAndSit();

        /// <summary>边缘坐掉落中（桌面形态=锚定窗口消失重力下坠，压制打招呼/小动作触发；游戏内恒 false）</summary>
        bool IsEdgeFalling { get; }
    }
}
