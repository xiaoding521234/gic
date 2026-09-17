using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 单位立牌表现（双端同构表现层：Snapshot 建场、Segment 驱动动画，不持逻辑状态）。
    /// 风格参考饥荒：2D 立牌 + 底座投影；格子表现尺寸明显大于单位立牌。
    /// 立牌朝向 = 饥荒式"斜插卡片"：yaw 跟随相机（root，billboard），绕底边固定后倾（2026-09-18 拍板，
    /// 35°=90°−55°俯角，立牌面正对相机视线——完全垂直会被俯角透视压扁，与饥荒观感差异大的根因）。
    /// </summary>
    public class UnitView : MonoBehaviour
    {
        public string UnitId { get; private set; }
        public string DisplayName { get; private set; }
        public BattleCell Cell { get; set; }
        public bool IsCorpse { get; private set; }

        private SpriteRenderer _avatarRenderer;
        private Transform _baseDisc;
        private Color _baseColor = Color.white;

        private const float AvatarHeight = 0.55f;

        /// <summary>
        /// 创建立牌（头像 SpriteRenderer + 阵营色底座）
        /// </summary>
        /// <param name="tiltDegrees">立牌后倾角（饥荒式"斜插卡片"：相机固定俯角 55°，垂直立牌被俯视压扁；
        /// 绕底边后倾后立牌面正对相机视线。35°=90°−55° 恰好正对，2026-09-18 用户目检拍板）</param>
        public static UnitView Create(Transform parent, string unitId, string displayName, Sprite avatar, Color teamColor, Quaternion billboardRotation, float tiltDegrees = 35f)
        {
            var root = new GameObject($"UnitView_{unitId}");
            root.transform.SetParent(parent, false);
            root.transform.rotation = billboardRotation;

            var view = root.AddComponent<UnitView>();
            view.UnitId = unitId;
            view.DisplayName = displayName;

            // 头像立牌（SpriteRenderer 自动处理图集 UV）：
            // 外层 AvatarTilt 原点=格面底边（旋转轴=底边），内层挂 sprite 居于半高处——
            // 倾斜时立牌绕底边倒（底边保持贴地），非绕中心转（那会让底边翘起/插地）
            var avatarGo = new GameObject("AvatarTilt");
            avatarGo.transform.SetParent(root.transform, false);
            avatarGo.transform.localPosition = Vector3.zero;
            avatarGo.transform.localRotation = Quaternion.Euler(-tiltDegrees, 0f, 0f);

            var spriteGo = new GameObject("Avatar");
            spriteGo.transform.SetParent(avatarGo.transform, false);
            view._avatarRenderer = spriteGo.AddComponent<SpriteRenderer>();
            view._avatarRenderer.sprite = avatar;
            view._avatarRenderer.sortingOrder = 10;

            if (avatar != null)
            {
                float worldHeight = avatar.bounds.size.y;
                float scale = worldHeight > 0f ? AvatarHeight / worldHeight : 1f;
                spriteGo.transform.localScale = Vector3.one * scale;
                // sprite 中心置于半高处（外层原点=底边 → 底边贴地、立牌居中于半高）
                spriteGo.transform.localPosition = new Vector3(0f, AvatarHeight * 0.5f, 0f);
            }

            // 阵营色底座（平铺地面的圆盘替代：薄方块投影感）
            var baseGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            baseGo.name = "BaseDisc";
            UnityEngine.Object.Destroy(baseGo.GetComponent<Collider>());
            baseGo.transform.SetParent(root.transform, false);
            baseGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            baseGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            baseGo.transform.localScale = new Vector3(0.42f, 0.42f, 1f);

            var baseRenderer = baseGo.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var material = new Material(shader);
            material.color = teamColor;
            baseRenderer.sharedMaterial = material;
            view._baseDisc = baseGo.transform;
            view._baseColor = teamColor;

            return view;
        }

        /// <summary>
        /// 应用格位 + 队形偏移（世界坐标由 BattleBoard 换算）
        /// </summary>
        public void ApplyPosition(Vector3 worldPosition)
        {
            transform.position = worldPosition;
        }

        /// <summary>
        /// 尸体灰显（docs/05 §5.4：血量 0 永不复苏，立牌变灰）
        /// </summary>
        public void SetCorpseVisual(bool corpse)
        {
            IsCorpse = corpse;
            if (_avatarRenderer != null)
                _avatarRenderer.color = corpse ? new Color(0.45f, 0.45f, 0.45f, 0.9f) : Color.white;
            if (_baseDisc != null)
                _baseDisc.localScale = corpse
                    ? new Vector3(0.42f, 0.28f, 1f) // 尸体底座压扁
                    : new Vector3(0.42f, 0.42f, 1f);
        }

        /// <summary>
        /// 受击闪红
        /// </summary>
        public void FlashHit()
        {
            if (_avatarRenderer != null)
                _avatarRenderer.color = new Color(1f, 0.35f, 0.3f);
        }

        public void RestoreColor()
        {
            if (_avatarRenderer != null && !IsCorpse)
                _avatarRenderer.color = Color.white;
        }
    }
}
