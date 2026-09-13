// ==================== CoopScreen.Animation.cs（动画编排层：公共组件 GlassPanelAnimator 接线 + 列表↔房间切换过渡） ====================
// 动画本体（毛玻璃扫入扫出+内容分向滑入滑出）由两面板各自的 GlassPanelAnimator 承载（配方唯一实现），
// 本文件只保留联机特有的编排：双视图切换过渡 + 两动画器复位 + 屏级入场/退场调用。
using System;
using System.Collections;
using UnityEngine;
using GIC.Framework;
using GIC.Battle;
using GIC.Data;
using GIC.Data.Event;
using GIC.Tool;
namespace GIC.UI
{


    public partial class CoopScreen
    {
        [Header("面板动画")]
        [Tooltip("服务器列表面板的毛玻璃动画器（挂 ServerListPanel）")]
        [SerializeField] private GlassPanelAnimator 列表面板动画器;
        [Tooltip("房间详情面板的毛玻璃动画器（挂 RoomPanel）")]
        [SerializeField] private GlassPanelAnimator 房间面板动画器;

        [Header("状态切换动画")]
        [Tooltip("列表↔房间切换时旧面板退场时长系数（相对滑动时长）")]
        [SerializeField] private float 切换退场时长系数 = 0.5f;

        private Coroutine _switchRoutine;

        private GlassPanelAnimator AnimOf(GameObject panel)
            => panel == roomPanel ? 房间面板动画器 : 列表面板动画器;

        // ==================== 屏级入场/退场（UIManager 开关面板路径） ====================

        /// <summary>入场起始态（OnShow 同帧设置，防首帧闪现 §37 ①）+ 起始锁与守卫由基类包装承担</summary>
        private void PlayEnterAnimation()
        {
            if (列表面板动画器 == null) return;
            列表面板动画器.SetEntryOffsets();
            StartCoroutine(PlayGlassEnter(列表面板动画器));
        }

        /// <summary>退场动画本体（CloseScreen 模板收尾：Closing 锁 Pop + PopToPrevious）</summary>
        private IEnumerator PlayExitAnimation()
        {
            if (列表面板动画器 != null)
                yield return 列表面板动画器.ExitRoutine();
        }

        /// <summary>
        /// 两视图静止完成态复位：RefreshUI 每次执行时调用——离房/被踢回列表视图时即刻归零
        /// 动画残留（切换动画被连续状态变化打断后停在半途，靠此恢复；fill 序列化 0，激活即须归 1）。
        /// </summary>
        private void SnapAllPanelsToRest()
        {
            if (列表面板动画器 != null) 列表面板动画器.SnapToRest();
            if (房间面板动画器 != null) 房间面板动画器.SnapToRest();
        }

        // ==================== 状态切换过渡（列表 ↔ 房间） ====================

        /// <summary>
        /// 视图切换动画：旧面板快速扫出 → 刷新状态（SetActive 切换+内容）→ 新面板扫入。期间持 Entering 锁。
        /// 由 SetRoomState 驱动；快速连续切换时 SetRoomState 先 StopCoroutine 本例程再重启
        /// （被中断例程的锁靠 try/finally 释放 + InputLocks 同 owner+reason 去重自愈）。
        /// </summary>
        internal IEnumerator SwitchPanelRoutine(GameObject fromRoot, GameObject toRoot, Action applyState)
        {
            InputLocks.Push(this, InputLockReason.Entering);
            try
            {
                var from = AnimOf(fromRoot);
                if (from != null && from.gameObject.activeSelf)
                    yield return from.ExitRoutine(切换退场时长系数);

                applyState();                       // RefreshUI：SetActive 切换 + 内容刷新 + 复位

                var to = AnimOf(toRoot);
                if (to != null)
                {
                    to.SetEntryOffsets();
                    yield return to.EnterRoutine(); // 锁已持有，不走 PlayGlassEnter
                }
            }
            finally
            {
                _switchRoutine = null; // 运行标志归位（SetRoomState 据此判断"切换动画进行中"吞同态刷新）
                InputLocks.Pop(this, InputLockReason.Entering);
            }
        }
    }
}
