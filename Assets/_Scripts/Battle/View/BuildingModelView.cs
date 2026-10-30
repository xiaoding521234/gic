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
        private const string ContactShadowName = "ContactShadow";
        private const string BuildingShaderName = "GIC/Battle/GIBuilding";
        private const string ContactShadowShaderName = "GIC/Battle/ContactShadow";
        private const int MaxNightSpillSources = 8;

        private Renderer[] _renderers;
        private Material[] _materials;
        private Bounds _localBounds;
        private bool _transparentForDissolve;
        private Color _lastTint = Color.white;
        private float _dissolveAlpha;

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
            instance.transform.localRotation = prefab.transform.localRotation;
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
                if (filter.name == ContactShadowName) continue;
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
            view.ConfigureLocalEnhancements(instance);
            return view;
        }

        private void ConfigureLocalEnhancements(GameObject instance)
        {
            var centers = new List<Vector4>();
            var extents = new List<Vector4>();
            Color spillColor = new Color(1f, 0.71353996f, 0.3098039f, 1f);
            bool spillColorResolved = false;

            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.name == ContactShadowName || filter.sharedMesh == null) continue;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null) continue;
                bool isWindow = false;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null || material.shader.name != BuildingShaderName) continue;
                    if (material.GetFloat("_EmissionType") <= 0.5f) continue;
                    isWindow = true;
                    if (!spillColorResolved && material.HasProperty("_EmissionColor"))
                    {
                        spillColor = material.GetColor("_EmissionColor");
                        spillColorResolved = true;
                    }
                }
                if (!isWindow || centers.Count >= MaxNightSpillSources) continue;

                var bounds = filter.sharedMesh.bounds;
                Vector3 center = instance.transform.InverseTransformPoint(filter.transform.TransformPoint(bounds.center));
                Vector3 size = Vector3.Scale(bounds.extents, filter.transform.localScale) * 1.15f + Vector3.one * 0.75f;
                centers.Add(new Vector4(center.x, center.y, center.z, 0f));
                extents.Add(new Vector4(size.x, size.y, size.z, 0f));
            }

            Vector3 facadeDirection = instance.transform.TransformDirection(Vector3.right).normalized;
            foreach (var material in _materials)
            {
                if (material == null || material.shader == null || material.shader.name != BuildingShaderName) continue;
                material.SetVector("_FacadeFillDirection", facadeDirection);
                material.SetFloat("_NightSpillCount", centers.Count);
                material.SetVectorArray("_NightSpillCenters", centers);
                material.SetVectorArray("_NightSpillExtents", extents);
                material.SetColor("_NightSpillColor", spillColor);
                if (material.GetFloat("_EmissionType") > 0.5f)
                    material.SetFloat("_NightSpillStrength", 0f);
            }
        }

        private static bool IsContactShadow(Material material)
        {
            return material.shader != null && material.shader.name == ContactShadowShaderName;
        }

        public void SetVisible(bool visible)
        {
            if (_renderers == null) return;
            foreach (var renderer in _renderers)
                if (renderer != null) renderer.enabled = visible;
        }

        public void SetTint(Color tint)
        {
            _lastTint = tint;
            tint.a *= 1f - _dissolveAlpha;
            if (_materials == null) return;
            foreach (var material in _materials)
            {
                if (material == null || IsContactShadow(material)) continue;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
                if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
            }
        }

        public void SetDissolveAlpha(float alpha01)
        {
            if (alpha01 <= 0f) return;
            _dissolveAlpha = Mathf.Clamp01(alpha01);
            if (_transparentForDissolve)
            {
                foreach (var material in _materials)
                    if (material != null && IsContactShadow(material))
                        material.SetFloat("_DissolveAlpha", _dissolveAlpha);
                return;
            }
            _transparentForDissolve = true;
            foreach (var material in _materials)
            {
                if (material == null) continue;
                if (IsContactShadow(material))
                {
                    material.SetFloat("_DissolveAlpha", _dissolveAlpha);
                    continue;
                }
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
            SetTint(_lastTint);
        }

        /// <summary>战斗时钟转窗户夜间发光系数：8:00—20:00 熄灭，其余时段点亮。</summary>
        public static float NightTargetForMinutes(int minutes)
        {
            int hour = (minutes / 60) % 24;
            return hour >= GIC.Tool.TimeUtility.DayStartHour && hour < GIC.Tool.TimeUtility.DayEndHour ? 0f : 1f;
        }

        public void SetNightEmission(float factor)
        {
            if (_materials == null) return;
            foreach (var material in _materials)
                if (material != null && material.HasProperty("_NightEmission"))
                    material.SetFloat("_NightEmission", Mathf.Clamp01(factor));
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
