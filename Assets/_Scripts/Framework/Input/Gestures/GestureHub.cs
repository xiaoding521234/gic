using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GIC.Framework
{
    /// <summary>
    /// 手势中枢（docs/24 §3）——指针手势系统中心：PointerInputPump 事件流 → 三门 → surface 识别器，
    /// 并执行 first-accept-wins 仲裁（宣胜者独占，同面其余识别器 ForceFail；双指起手 → 单指识别器
    /// ForceCancel——Map/桌宠"双指取代单指"现有语义）。UI 命中门在此唯一实现（收编原三份
    /// IsPointerOverUI，docs/24 §1.1-2）。
    /// [Component] 注册于 Wargame managers 管线（InputManager 之后，docs/24 §7 驱动点）；
    /// 桌面宠物进程跳过 Wargame 组合根 → 天然无 hub（Win32 路线独立是架构必然，docs/24 §3）。
    /// </summary>
    [Component]
    public class GestureHub : IWargameManager
    {
        /// <summary>指针序列绑定（Flutter hit 模型：按下时定归属面，序列内不再改判——"按下在模型上、甩出模型仍抓"的桌宠语义）</summary>
        private sealed class PointerBinding
        {
            public IGestureSurface Surface;
            public PointerKind Kind;
            public Vector2 LastPos;
        }

        private readonly PointerInputPump _pump = new PointerInputPump();
        private readonly List<PointerEvent> _events = new List<PointerEvent>(16);
        private readonly List<IGestureSurface> _surfaces = new List<IGestureSurface>();
        private readonly Dictionary<int, PointerBinding> _bindings = new Dictionary<int, PointerBinding>();
        private readonly List<int> _removeBuffer = new List<int>(8); // 注销面时解绑用（迭代中不可直接改字典）

        // 位置式 UI 命中的静态缓存（移植自 MapCameraController._uiRaycastBuffer——避免每次分配）
        private static readonly List<RaycastResult> _uiRaycastBuffer = new List<RaycastResult>(8);

        /// <summary>
        /// 任意指针按下（门2 UI 门**之前**触发——含按在 UI 上的按下）。
        /// 消费方"抓停"类语义用（Map：任何按下都掐断惯性滑行，含按在按钮上）；
        /// 也是 WaitForAnyTap（docs/24 P3）的指针侧入口。
        /// </summary>
        public event System.Action<PointerEvent> AnyPointerBegan;

        /// <summary>注册手势面（消费方 Start 调用；识别器集合此后固定——hub 据此挂仲裁钩子）</summary>
        public void RegisterSurface(IGestureSurface surface)
        {
            if (surface == null || _surfaces.Contains(surface)) return;
            _surfaces.Add(surface);
            var list = surface.Recognizers;
            for (int i = 0; i < list.Count; i++)
                list[i].Won += OnRecognizerWon;
        }

        /// <summary>注销手势面（消费方 OnDestroy 调用；面向它的绑定一并解除）</summary>
        public void UnregisterSurface(IGestureSurface surface)
        {
            if (!RemoveSurfaceAndUnhook(surface)) return;
            _removeBuffer.Clear();
            foreach (var kv in _bindings)
                if (kv.Value.Surface == surface)
                    _removeBuffer.Add(kv.Key);
            for (int i = 0; i < _removeBuffer.Count; i++)
                _bindings.Remove(_removeBuffer[i]);
        }

        private bool RemoveSurfaceAndUnhook(IGestureSurface surface)
        {
            if (!_surfaces.Remove(surface)) return false;
            var list = surface.Recognizers;
            for (int i = 0; i < list.Count; i++)
                list[i].Won -= OnRecognizerWon;
            return true;
        }

        // ── IWargameManager（GameScene.Update → Wargame.Update 管线驱动）──

        public void Start() { }

        public void Update(float deltaTime)
        {
            double now = Time.unscaledTimeAsDouble;
            _events.Clear();
            _pump.Poll(now, _events);

            // 门1 输入锁：锁生效 → 新事件不分发 + 活跃手势取消（与今日 Map/Battle"锁期间中断手势"同语义）
            if (InputLocks.IsLocked)
            {
                for (int i = 0; i < _surfaces.Count; i++)
                {
                    var list = _surfaces[i].Recognizers;
                    for (int r = 0; r < list.Count; r++)
                        list[r].ForceCancel();
                }
                _bindings.Clear();
                return;
            }

            for (int i = 0; i < _events.Count; i++)
                Dispatch(_events[i]);

            // deadline 类心跳：长按/按住升级/连击窗口（宣胜可能在此发生 → 仲裁随之）
            for (int s = 0; s < _surfaces.Count; s++)
            {
                var list = _surfaces[s].Recognizers;
                for (int r = 0; r < list.Count; r++)
                    list[r].Tick(now);
            }
        }

        // ── 分发与三门 ──

        private void Dispatch(in PointerEvent e)
        {
            if (e.Phase == PointerPhase.Began)
            {
                AnyPointerBegan?.Invoke(e); // 抓停/任意点击类语义入口（在 UI 门之前）

                bool secondTouch = e.Kind == PointerKind.Touch && HasOtherTouchBinding(e.Id);

                // 门2 UI 命中：UI 指针不进世界面（按住按钮不拖地图/不点锚点）
                if (IsPointerOverUI(e, secondTouch)) return;

                // 门3 surface 准入：第一个收留的面得绑定（注册序；现实中一屏一面）
                for (int i = 0; i < _surfaces.Count; i++)
                {
                    IGestureSurface surface = _surfaces[i];
                    if (!surface.Enabled || !surface.ShouldReceivePointer(e)) continue;
                    _bindings[e.Id] = new PointerBinding { Surface = surface, Kind = e.Kind, LastPos = e.Position };
                    if (secondTouch)
                        CancelSinglePointerGestures(surface); // 双指起手取代单指（先于识别器收事件——Map L292/桌宠 L636 现语义）
                    Deliver(surface, e);
                    return;
                }
                return;
            }

            // 序列事件：按绑定投递（非 Began 帧不改判归属）
            if (!_bindings.TryGetValue(e.Id, out PointerBinding binding)) return;
            binding.LastPos = e.Position;
            if (e.Phase == PointerPhase.Ended)
            {
                // Ended 帧 UI 命中：tap 判定用（"按下在地图、滑到按钮上抬起"不判点击——Map 现规则）
                PointerEvent ended = e;
                ended.OverUI = IsOverUIById(e.Id);
                Deliver(binding.Surface, ended);
            }
            else
            {
                Deliver(binding.Surface, e);
            }
            if (e.Phase == PointerPhase.Ended || e.Phase == PointerPhase.Cancelled)
                _bindings.Remove(e.Id);
        }

        private static void Deliver(IGestureSurface surface, in PointerEvent e)
        {
            var list = surface.Recognizers;
            for (int i = 0; i < list.Count; i++)
                list[i].HandlePointerEvent(e);
        }

        // ── 仲裁（first-accept-wins，docs/24 §7.3）──

        private void OnRecognizerWon(GestureRecognizer winner)
        {
            for (int s = 0; s < _surfaces.Count; s++)
            {
                var list = _surfaces[s].Recognizers;
                if (!ContainsRecognizer(list, winner)) continue;
                for (int r = 0; r < list.Count; r++)
                    if (list[r] != winner)
                        list[r].ForceFail();
                return;
            }
        }

        private static bool ContainsRecognizer(System.Collections.Generic.IReadOnlyList<GestureRecognizer> list, GestureRecognizer target)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == target) return true;
            return false;
        }

        /// <summary>双指起手取代单指：取消该面全部单指识别器（拖拽/长按/连击——Map L292、桌宠 L636 现语义）</summary>
        private static void CancelSinglePointerGestures(IGestureSurface surface)
        {
            var list = surface.Recognizers;
            for (int i = 0; i < list.Count; i++)
                if (list[i].MaxPointers < 2)
                    list[i].ForceCancel();
        }

        private bool HasOtherTouchBinding(int excludeId)
        {
            foreach (var kv in _bindings)
                if (kv.Key != excludeId && kv.Value.Kind == PointerKind.Touch)
                    return true;
            return false;
        }

        // ── UI 命中门（原三份 IsPointerOverUI 的唯一实现，docs/24 §1.1-2）──

        /// <summary>
        /// Began 帧 UI 命中：单指=id 式（Began 帧可靠，Map L462 实证）；双指=位置式兼查既有指
        /// （id 式对非 Began 帧的触摸不可靠——Unity 已知行为，第二指落下时第一指已不在 Began 帧，Map L470 实证）。
        /// </summary>
        private bool IsPointerOverUI(in PointerEvent e, bool secondTouch)
        {
            if (!secondTouch)
                return IsOverUIById(e.Id);
            if (IsOverUIByPosition(e.Position)) return true;
            foreach (var kv in _bindings)
                if (kv.Value.Kind == PointerKind.Touch && IsOverUIByPosition(kv.Value.LastPos))
                    return true;
            return false;
        }

        private static bool IsOverUIById(int pointerId)
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject(pointerId);
        }

        private static bool IsOverUIByPosition(Vector2 screenPos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var pointerData = new PointerEventData(es) { position = screenPos };
            _uiRaycastBuffer.Clear();
            es.RaycastAll(pointerData, _uiRaycastBuffer);
            return _uiRaycastBuffer.Count > 0;
        }
    }
}
