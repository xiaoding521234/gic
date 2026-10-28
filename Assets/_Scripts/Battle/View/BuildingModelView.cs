using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GIC.Battle
{
    /// <summary>
    /// 建筑 3D 模型表现件：按目标宽度等比缩放到格内、居中并贴地。
    /// UnitView 仍负责单位状态；本组件只持有模型渲染器与材质实例，承接显隐、着色和消散。
    /// </summary>
    public sealed class BuildingModelView : MonoBehaviour
    {
        private Renderer[] _renderers;
        private Material[] _materials;
        private Bounds _localBounds;
        private bool _transparentForDissolve;

        public float DisplayHeight { get; private set; }

        /// <summary>当前是否有任一模型渲染器在显（迷雾/维度隐藏时=false——拾取链据此拒绝命中）</summary>
        public bool IsVisible
        {
            get
            {
                if (_renderers == null) return false;
                foreach (var renderer in _renderers)
                    if (renderer != null && renderer.enabled) return true;
                return false;
            }
        }

        public static BuildingModelView Create(Transform parent, GameObject prefab, float targetWidth)
        {
            var view = parent.gameObject.AddComponent<BuildingModelView>();
            var instance = Instantiate(prefab);
            instance.name = prefab.name + "_Model";
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            var filters = instance.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0)
            {
                Destroy(instance);
                throw new System.InvalidOperationException($"建筑模型 {prefab.name} 不含 MeshFilter");
            }

            var bounds = new Bounds(instance.transform.position, Vector3.zero);
            foreach (var filter in filters)
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer != null) bounds.Encapsulate(renderer.bounds);
            }

            float width = targetWidth > 0f ? targetWidth : 1f;
            float scale = bounds.size.x > 0.0001f ? width / bounds.size.x : 1f;
            instance.transform.SetParent(parent, false);
            instance.transform.localScale = Vector3.one * scale;
            instance.transform.localPosition = new Vector3(
                -bounds.center.x * scale, -bounds.min.y * scale, -bounds.center.z * scale);

            view.DisplayHeight = bounds.size.y * scale;
            view._localBounds = new Bounds(
                new Vector3(0f, view.DisplayHeight * 0.5f, 0f),
                new Vector3(bounds.size.x * scale, view.DisplayHeight, bounds.size.z * scale));
            view._renderers = instance.GetComponentsInChildren<Renderer>(true);

            var uniqueMaterials = new List<Material>();
            var replacements = new Dictionary<Material, Material>();
            foreach (var renderer in view._renderers)
            {
                var shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                {
                    var source = shared[i];
                    if (source == null) continue;
                    if (!replacements.TryGetValue(source, out var clone))
                    {
                        clone = new Material(source);
                        replacements.Add(source, clone);
                        uniqueMaterials.Add(clone);
                    }
                    shared[i] = clone;
                }
                renderer.sharedMaterials = shared;
            }
            view._materials = uniqueMaterials.ToArray();
            return view;
        }

        public void SetVisible(bool visible)
        {
            if (_renderers == null) return;
            foreach (var renderer in _renderers)
                if (renderer != null) renderer.enabled = visible;
        }

        public void SetTint(Color tint)
        {
            if (_materials == null) return;
            foreach (var material in _materials)
            {
                if (material == null) continue;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
                if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
            }
        }

        public void SetDissolveAlpha(float alpha01)
        {
            if (alpha01 <= 0f || _transparentForDissolve) return;
            _transparentForDissolve = true;
            foreach (var material in _materials)
            {
                if (material == null) continue;
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
        }

        /// <summary>供 UnitView 的既有面片拾取链消费：返回模型朝相机一侧的世界包围四角。</summary>
        public bool TryGetWorldCorners(Vector3[] corners)
        {
            if (corners == null || corners.Length < 4) return false;
            var c = _localBounds.center;
            var e = _localBounds.extents;
            float frontZ = c.z - e.z;
            corners[0] = transform.TransformPoint(new Vector3(c.x - e.x, c.y - e.y, frontZ));
            corners[1] = transform.TransformPoint(new Vector3(c.x + e.x, c.y - e.y, frontZ));
            corners[2] = transform.TransformPoint(new Vector3(c.x + e.x, c.y + e.y, frontZ));
            corners[3] = transform.TransformPoint(new Vector3(c.x - e.x, c.y + e.y, frontZ));
            return true;
        }

        private void OnDestroy()
        {
            if (_materials == null) return;
            foreach (var material in _materials)
                if (material != null) Destroy(material);
        }
    }
}
