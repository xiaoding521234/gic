namespace GIC.Framework
{
    /// <summary>
    /// 可关闭的 UI 组件接口。
    /// 按下关闭快捷键时，InputManager 关闭最顶层注册的 IClosable。
    /// 各实现自行在 Close() 入口检查状态（如 isClosing 防重入）。
    /// </summary>
    public interface IClosable
    {
        /// <summary>执行关闭操作</summary>
        void Close();
    }
}
