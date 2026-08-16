using System.Collections;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Editor
{


    /// <summary>
    /// 验证 LocalEventBus 在不同渲染帧率下是否固定每秒处理50个事件（FixedUpdate 默认 50fps）。
    /// 挂到场景中任意 GameObject 上，运行后看 Console 输出。
    /// </summary>
    public class EventBusFrameRateTest : MonoBehaviour
    {
        [Header("测试时长（秒）")]
        [SerializeField] private float testDuration = 2f;

        [Header("测试渲染帧率列表")]
        [SerializeField] private int[] testFrameRates = { 30, 60, 120 };

        private int _processedCount;
        private bool _isTesting;

        void Start()
        {
            StartCoroutine(RunAllTests());
        }

        private IEnumerator RunAllTests()
        {
            GICLog.Info("[EventBus测试] ========== 开始测试 ==========");

            foreach (int fps in testFrameRates)
            {
                yield return RunSingleTest(fps);
                yield return new WaitForSecondsRealtime(0.3f);
            }

            Application.targetFrameRate = -1; // 恢复默认
            GICLog.Info("[EventBus测试] ========== 全部测试完成 ==========");
        }

        private IEnumerator RunSingleTest(int targetFps)
        {
            GICLog.Info($"[EventBus测试] --- 渲染帧率 {targetFps} fps ---");

            Application.targetFrameRate = targetFps;

            // 等待帧率稳定
            yield return new WaitForSecondsRealtime(0.5f);

            // 清理旧测试残留的 handler
            EventBusHub.Instance.LocalEventBus?.ClearSubscriptions<TestCountEvent>();

            _processedCount = 0;
            _isTesting = true;
            var handler = new TestEventHandler(this);
            EventBusHub.Instance.Subscribe(handler, this);

            // 持续入队事件，持续 testDuration 秒
            float startTime = Time.realtimeSinceStartup;
            int enqueued = 0;

            while (Time.realtimeSinceStartup - startTime < testDuration)
            {
                EventBusHub.Instance.Send(new TestCountEvent());
                enqueued++;
                yield return null;
            }

            // 等待队列里剩余事件处理完
            float waitStart = Time.realtimeSinceStartup;
            while (EventBusHub.Instance.LocalEventBus.QueuedEventCount > 0
                   && Time.realtimeSinceStartup - waitStart < 2f)
            {
                yield return null;
            }

            EventBusHub.Instance.UnsubscribeOwner(this);
            _isTesting = false;

            float actualElapsed = Time.realtimeSinceStartup - startTime;
            float actualRate = _processedCount / actualElapsed;
            float expectedRate = 50f;
            bool pass = Mathf.Abs(actualRate - expectedRate) <= expectedRate * 0.15f;

            string result = pass ? "✅ 通过" : "❌ 失败";
            GICLog.Info($"[EventBus测试] 渲染帧率={targetFps}fps | 入队={enqueued} | 处理={_processedCount} | "
                    + $"实际耗时={actualElapsed:F2}s | 速率={actualRate:F1}/s (期望~{expectedRate:F0}/s) | {result}");
        }

        /// <summary>测试用事件</summary>
        public class TestCountEvent : BaseEvent { }

        /// <summary>测试用handler，每次被调用时计数+1</summary>
        private class TestEventHandler : IEventHandler<TestCountEvent>
        {
            private readonly EventBusFrameRateTest _owner;
            public int Priority => EventPriority.Lowest;

            public TestEventHandler(EventBusFrameRateTest owner) => _owner = owner;

            public bool CanHandle(TestCountEvent evt) => _owner._isTesting;

            public void Handle(TestCountEvent evt)
            {
                _owner._processedCount++;
            }
        }
    }

}

