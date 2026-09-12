using System.Collections.Generic;

namespace GIC.Framework
{
    /// <summary>
    /// 手势面 —— 一个交互表面的识别器集合 + 指针准入过滤（Apple shouldReceiveTouch 等价）。
    /// 消费方（MapCamera/BattleCamera/游戏内桌宠，docs/24 §5）实现本接口：
    /// · 构造识别器并订阅其回调，再 RegisterSurface 到 GestureHub（Start 注册 / OnDestroy 注销）；
    /// · Recognizers 集合注册后固定（hub 据此挂仲裁钩子），勿动态增删；
    /// · 准入过滤只在指针 Began 时评估一次（Flutter hit 模型：按下定归属，序列内不再改判——
    ///   "按下在模型上、快速甩出模型的拖拽也要能抓"的既有桌宠语义由绑定保持）。
    /// </summary>
    public interface IGestureSurface
    {
        /// <summary>surface 是否参与交互（入场动画/面板打开等自身原因关停时置 false；输入级冻结走 InputLocks，hub 门1 统一处理）</summary>
        bool Enabled { get; }

        /// <summary>指针准入过滤（如游戏内桌宠"命中模型才收"）；hub 的 UI 门先于此执行</summary>
        bool ShouldReceivePointer(in PointerEvent e);

        /// <summary>本面的识别器集合（注册后固定）</summary>
        IReadOnlyList<GestureRecognizer> Recognizers { get; }

        /// <summary>
        /// 绕过 hub 门2（UI 命中门）——**覆盖型 overlay 面专用**（游戏内桌宠：其挡板/输入条自身即 UI，
        /// raycastTarget 动态开关会把命中区域报告为"UI"，若不豁免则永远收不到自己的指针；面内自行
        /// 排除对话元素）。默认 false 的世界面照旧尊重 UI 命中（按住按钮不拖地图）。
        /// </summary>
        bool BypassUIGate { get; }

        /// <summary>
        /// 忽略门1 输入锁——**元游戏陪伴体专用**（游戏内桌宠：旧全轮询实现从不理 InputLocks，
        /// 游戏弹窗/转场动画锁冻结它属行为回归）。默认 false：锁生效时手势冻结。
        /// </summary>
        bool IgnoresInputLocks { get; }
    }
}
