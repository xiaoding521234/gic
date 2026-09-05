#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GIC.Tool
{
    /// <summary>
    /// TabGlyphs 资产真实 guid 查询工具（绕过加密 meta）：
    /// 项目根存在 .codely-cli/tmp/GlyphGuidQuery.run.flag 时自动执行一次并写 result.txt，然后删除 flag（幂等）。
    /// </summary>
    public static class GlyphGuidQueryTool
    {
        private const string Dir = "Assets/Resources/UI/Backpack/TabGlyphs";

        [MenuItem("Tools/TG/查询 TabGlyphs 真实 guid")]
        public static void Run()
        {
            var names = new[] { "glyph_1", "glyph_2", "glyph_3", "glyph_4", "glyph_5", "glyph_6", "glyph_7", "glyph_8", "glyph_9", "disc" };
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[GlyphGuidQuery] " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (var n in names)
            {
                var p = $"{Dir}/{n}.png";
                var g = AssetDatabase.AssetPathToGUID(p);
                sb.AppendLine($"{n} = {g}");
            }
            sb.AppendLine("DONE");
            Debug.Log(sb.ToString());
            WriteResult(sb.ToString());
        }

        [InitializeOnLoadMethod]
        private static void AutoRunHook()
        {
            void Check()
            {
                string flag = Path.Combine(Application.dataPath, "..", ".codely-cli", "tmp", "GlyphGuidQuery.run.flag");
                if (File.Exists(flag))
                {
                    File.Delete(flag);
                    Run();
                }
            }
            EditorApplication.delayCall += Check;
            EditorApplication.projectChanged += () => EditorApplication.delayCall += Check;
        }

        private static void WriteResult(string content)
        {
            try
            {
                string outPath = Path.Combine(Application.dataPath, "..", ".codely-cli", "tmp", "GlyphGuidQuery.result.txt");
                File.WriteAllText(outPath, content);
            }
            catch { }
        }
    }
}
#endif