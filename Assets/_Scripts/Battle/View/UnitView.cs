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
        public static UnitView Create(Transform parent, string unitId, string displayName, Sprite avatar, Color teamColor, Quaternion billboardRotation)
        {
            var root = new GameObject($"UnitView_{unitId}");
            root.transform.SetParent(parent, false);
            root.transform.rotation = billboardRotation;

            var view = root.AddComponent<UnitView>();
            view.UnitId = unitId;
            view.DisplayName = displayName;

            // 头像立牌（SpriteRenderer 自动处理图集 UV）
            var avatarGo = new GameObject("Avatar");
            avatarGo.transform.SetParent(root.transform, false);
            view._avatarRenderer = avatarGo.AddComponent<SpriteRenderer>();
            view._avatarRenderer.sprite = avatar;
            view._avatarRenderer.sortingOrder = 10;

            if (avatar != null)
            {
                float worldHeight = avatar.bounds.size.y;
                float scale = worldHeight > 0f ? AvatarHeight / worldHeight : 1f;
                avatarGo.transform.localScale = Vector3.one * scale;
                // 贴图中心对齐根点 → 抬高半高使立牌底部贴地
                avatarGo.transform.localPosition = new Vector3(0f, AvatarHeight * 0.5f, 0f);
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
