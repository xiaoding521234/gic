using System.IO;
using System.Linq;
using GIC.Framework;
using Mirror;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GIC.Editor
{
    /// <summary>所有发布入口共用的场景、联机组件和资产设置检查。</summary>
    public sealed class GicBuildPreflight : IPreprocessBuildWithReport
    {
        private const string BootPath = "Assets/Scenes/Boot.unity";
        private const string BattlePath = "Assets/Scenes/BattleScreen.unity";
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            string error = Validate();
            if (error != null) throw new BuildFailedException(error);
        }

        public static string Validate()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0 || scenes[0] != BootPath || !scenes.Contains(BattlePath))
                return "构建场景必须以 Boot 开头，并包含 BattleScreen";
            foreach (string path in scenes)
            {
                if (!File.Exists(Path.Combine(Application.dataPath, "..", path))) return $"构建场景不存在：{path}";
                var open = SceneManager.GetSceneByPath(path);
                if (open.IsValid() && open.isDirty) return $"请先保存打开的构建场景：{path}";
            }
            if (AddressableAssetSettingsDefaultObject.Settings == null)
                return "找不到 Addressables 设置；请恢复原项目的 Assets/AddressableAssetsData 和 .meta";

            var boot = SceneManager.GetSceneByPath(BootPath);
            if (boot.IsValid() && boot.isLoaded) return ValidateBoot(boot);

            // 不打开或保存用户的场景；检查导入后的真实组件引用而不是猜 YAML 文件格式。
            var preview = EditorSceneManager.OpenPreviewScene(BootPath);
            if (!preview.IsValid()) return "Boot 场景无法加载";
            try { return ValidateBoot(preview); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        private static string ValidateBoot(Scene scene)
        {
            var roots = scene.GetRootGameObjects().Where(go => go.name == "NetworkManager").ToArray();
            if (roots.Length != 1 || !roots[0].activeSelf) return "Boot 必须有且只有一个启用的 NetworkManager 根物体";
            var root = roots[0];
            var manager = root.GetComponent<MyNetworkManager>();
            if (manager == null || !manager.enabled || manager.transport == null || !manager.autoCreatePlayer
                || manager.playerPrefab == null || manager.playerPrefab.GetComponent<NetworkIdentity>() == null)
                return "Boot 的网络管理器缺少传输组件或自动创建的 NetworkPlayer";
            var bus = root.transform.Find("NetworkEventBus");
            var identity = bus == null ? null : bus.GetComponent<NetworkIdentity>();
            var events = bus == null ? null : bus.GetComponent<NetworkEventBus>();
            if (bus == null || !bus.gameObject.activeSelf || events == null || !events.enabled
                || identity == null || identity.sceneId == 0)
                return "Boot 的 NetworkEventBus 缺失、未启用或未配置有效的场景身份";
            return null;
        }
    }
}
