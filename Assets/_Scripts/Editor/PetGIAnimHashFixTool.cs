using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GIC.Editor
{
    /// <summary>
    /// GI 原始动画哈希路径修复（2026-08-25，docs/19 §2.2 落地记录）：
    /// AssetStudio 导出的 199 条 GI 原始 .anim 中，辅助骨（+EarB 王冠/+HairS 头发/+AmiceB 披风链/
    /// CloakRoot/部分手指骨）曲线的 path 是未解析哈希字面量 "path_&lt;CRC32(骨完整路径)&gt;"
    /// ——场景里不存在该名字的节点，Animation 按路径逐字绑定 → 这些骨全程冻在绑定姿势
    /// （Show_3 摘王冠动作王冠纹丝不动的根因；头发/披风/手指同理）。
    /// 修复=按场景 GI 模型层级建 CRC32→真实骨路径表，对曲线段的哈希 path 做文本级重键
    /// （m_RotationCurves/m_PositionCurves/m_ScaleCurves 三段；m_ClipBindingConstant 纯数字哈希
    /// 表是 Unity 派生数据，导入时自动重建，不动）。幂等：修过的文件无 path_ 匹配自然跳过。
    /// 方案B重定向管线（PaimonRetargetPipeline v23）在转出 MMD clip 时已做同样的还原，
    /// 本工具补齐"GI 原始 clip 零重定向直读"路线（2026-08-24 场景切官方模型后唯一在播路线）。
    /// CRC 算法与管线 v23 完全一致（标准 CRC32/IEEE，UTF-16 低位字节）。
    /// 性能红线（2026-08-25 首跑 330s 超时实证）：199×20MB 大文件禁止 text.Split('\n')
    /// （500k 行数组分配+GC 风暴）——用指针式行迭代 + "path:" IndexOf 预筛，全流程单次 ReadAllText 复用。
    /// </summary>
    public static class PetGIAnimHashFixTool
    {
        const string AnimFolder = "Assets/Art/PaimonPet/GI/Animations";
        const string Marker = "[GIHashFix]";

        [MenuItem("Tools/桌宠/GI原始动画: 哈希路径修复")]
        public static void Run() => FixAll(false);

        [MenuItem("Tools/桌宠/GI原始动画: 哈希路径修复(干跑)")]
        public static void DryRun() => FixAll(true);

        /// <summary>execute_csharp_script 直调入口：返回一行摘要（干跑不落盘、不重导）。</summary>
        public static string FixAll(bool dryRun)
        {
            var log = new StringBuilder();
            try
            {
                var result = FixInternal(dryRun, log);
                Debug.Log($"{Marker} {(dryRun ? "干跑完成" : "完成")} {result}\n{log}");
                return $"{Marker} {(dryRun ? "DRY" : "DONE")} {result}";
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"{Marker} 失败: {ex}\n{log}");
                throw;
            }
        }

        static string FixInternal(bool dryRun, StringBuilder log)
        {
            log.AppendLine($"{Marker} START {(dryRun ? "DRY" : "APPLY")}");

            // ---- 1) 场景 GI 模型实例 → CRC32→骨路径表 ----
            Transform model = null;
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
                if (t.name == "NPC_Kanban_Paimon_Model" && t.Find("Bip001/Bip001 Pelvis") != null) { model = t; break; }
            if (model == null) throw new System.InvalidOperationException(
                "场景中未找到 GI 模型实例 NPC_Kanban_Paimon_Model（需打开 PaimonPet 场景）");

            var crcToPath = new Dictionary<uint, string>();
            void Walk(Transform t, string prefix)
            {
                var p = prefix == "" ? t.name : prefix + "/" + t.name;
                crcToPath[Crc32(p)] = p;
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), p);
            }
            foreach (Transform c in model) Walk(c, "");
            log.AppendLine($"{Marker} 骨路径表: {crcToPath.Count} 条（模型子树，相对 {model.name}）");

            // ---- 2) 遍历 .anim 文件 ----
            var files = Directory.GetFiles(Path.GetFullPath(AnimFolder), "*.anim")
                .OrderBy(f => f).ToList();
            if (files.Count == 0) throw new System.InvalidOperationException($"目录无 .anim: {AnimFolder}");

            // 段级"具名/还原路径不相交"校验：同一曲线段里若真实路径已有具名条目，
            // 再还原哈希会产生重复 path 条目（行为未定义）→ 该文件跳过并告警。
            var curveSections = new HashSet<string> { "m_RotationCurves", "m_PositionCurves", "m_ScaleCurves" };

            int filesChanged = 0, filesClean = 0, filesSkipped = 0, totalRepl = 0, totalHashLines = 0;
            var unresolved = new SortedSet<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (var file in files)
            {
                var assetPath = "Assets" + file.Substring(Application.dataPath.Length).Replace('\\', '/');
                string text = File.ReadAllText(file);

                if (text.IndexOf("path_", System.StringComparison.Ordinal) < 0) { filesClean++; continue; } // 幂等快路径

                // 校验 pass：指针式行迭代（无 Split/无逐行正则），只细看含 "path:" 的行
                var namedBySection = new Dictionary<string, HashSet<string>>();
                var resolvedBySection = new Dictionary<string, HashSet<string>>();
                string section = "";
                int hashLines = 0;
                int pos = 0, len = text.Length;
                while (pos < len)
                {
                    int nl = text.IndexOf('\n', pos);
                    int end = nl < 0 ? len : nl;
                    // 快速预筛：绝大多数行是 keyframe 数字行。放行两类：含 "path:" 的行、
                    // 顶级段头行（"  m_XXX:"——本身不含 "path:"，漏放行会导致 section 永远为空，首版实证）
                    int rawLen = end - pos;
                    if (rawLen < 300 && (rawLen > 5
                        ? (text.IndexOf("path:", pos, rawLen, System.StringComparison.Ordinal) >= 0
                           || (text[pos] == ' ' && text[pos + 1] == ' ' && text[pos + 2] == 'm' && text[pos + 3] == '_'))
                        : false))
                    {
                        int lead = pos;
                        while (lead < end && (text[lead] == ' ' || text[lead] == '\t' || text[lead] == '\r')) lead++;
                        int trail = end;
                        while (trail > lead && (text[trail - 1] == '\r' || text[trail - 1] == ' ')) trail--;
                        int indent = lead - pos;
                        int lineLen = trail - lead;
                        if (lineLen > 3 && text[lead] == 'm' && text[lead + 1] == '_')
                        {
                            if (indent == 2 && text[trail - 1] == ':')
                                section = text.Substring(lead, lineLen - 1); // 顶级 "  m_XXX:" 段名
                        }
                        else
                        {
                            int p5 = text.IndexOf("path:", lead, lineLen, System.StringComparison.Ordinal);
                            if (p5 >= 0)
                            {
                                int v0 = p5 + 5;
                                while (v0 < trail && text[v0] == ' ') v0++;
                                string val = text.Substring(v0, trail - v0);
                                if (val.StartsWith("path_"))
                                {
                                    hashLines++;
                                    if (curveSections.Contains(section) && uint.TryParse(val.Substring(5), out uint hv))
                                    {
                                        if (crcToPath.TryGetValue(hv, out var real))
                                        {
                                            if (!resolvedBySection.TryGetValue(section, out var s)) resolvedBySection[section] = s = new HashSet<string>();
                                            s.Add(real);
                                        }
                                        else unresolved.Add(val);
                                    }
                                }
                                else if (curveSections.Contains(section))
                                {
                                    if (!namedBySection.TryGetValue(section, out var s)) namedBySection[section] = s = new HashSet<string>();
                                    s.Add(val);
                                }
                            }
                        }
                    }
                    pos = nl + 1;
                }

                // 段内冲突判定：还原目标路径已存在具名条目 → 跳过该文件
                bool conflict = false;
                foreach (var kv in resolvedBySection)
                    if (namedBySection.TryGetValue(kv.Key, out var named) && kv.Value.Overlaps(named)) { conflict = true; break; }
                if (conflict)
                {
                    filesSkipped++;
                    log.AppendLine($"{Marker} [跳过] {assetPath}：还原路径与具名条目冲突");
                    continue;
                }
                if (resolvedBySection.Count == 0 || resolvedBySection.All(kv => kv.Value.Count == 0))
                {
                    filesClean++; // 只有未解析哈希（保留原样，无死可救）
                    totalHashLines += hashLines;
                    continue;
                }

                // 替换 pass：单遍正则（只动可解析的 path_ 行；未解析哈希保留）
                int repl = 0;
                var newText = Regex.Replace(text, @"path: path_(\d+)", mm =>
                {
                    if (uint.TryParse(mm.Groups[1].Value, out uint hv) && crcToPath.TryGetValue(hv, out var real))
                    { repl++; return "path: " + real; }
                    return mm.Value;
                });
                totalHashLines += hashLines;

                if (dryRun)
                {
                    log.AppendLine($"[干跑] {assetPath}: path_行={hashLines} 替换={repl}");
                    totalRepl += repl;
                    if (repl > 0) filesChanged++;
                    else filesClean++;
                    continue;
                }

                File.WriteAllText(file, newText, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                filesChanged++;
                totalRepl += repl;
            }
            sw.Stop();

            var summary = $"files={files.Count} 改={filesChanged} 净={filesClean} 跳={filesSkipped} " +
                          $"path_行={totalHashLines} 替换={totalRepl} " +
                          $"未解析哈希={unresolved.Count}{(unresolved.Count > 0 ? "（" + string.Join(",", unresolved.Take(10)) + "）" : "")} " +
                          $"耗时={sw.Elapsed.TotalSeconds:F0}s";
            log.AppendLine($"{Marker} DONE {summary}");

            // 完整日志落盘（诊断靠文件，console 只读首行）
            try
            {
                var logPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "gi_hash_fix_log.txt");
                Directory.CreateDirectory(Directory.GetParent(logPath).FullName);
                File.WriteAllText(logPath, log.ToString());
            }
            catch (System.Exception exF) { Debug.LogError($"{Marker} 日志写盘失败: {exF.Message}"); }

            return summary;
        }

        /// <summary>标准 CRC32/IEEE（与 PaimonRetargetPipeline.Crc32 一致；GI 骨路径全 ASCII）。</summary>
        static uint Crc32(string s)
        {
            uint crc = 0xFFFFFFFF;
            foreach (var ch in s)
            {
                crc ^= (uint)(ch & 0xFF);
                for (int k = 0; k < 8; k++)
                    crc = (crc & 1) != 0 ? (0xEDB88320u ^ (crc >> 1)) : (crc >> 1);
            }
            return crc ^ 0xFFFFFFFF;
        }
    }
}
