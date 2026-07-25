public interface IEventHandler<TEvent> where TEvent : BaseEvent
{
    bool CanHandle(TEvent evt);
    void Handle(TEvent evt);
    /// <summary>
    /// 优先级，数字越大越先执行
    /// </summary>
    int Priority => EventPriority.Normal;
}