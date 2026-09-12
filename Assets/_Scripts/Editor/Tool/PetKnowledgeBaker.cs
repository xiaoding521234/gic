#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;
using GIC.Tool;
using GIC.Pet.Chat;
using Newtonsoft.Json;

namespace GIC.Editor
{
    /// <summary>
    /// 派蒙知识库烘焙器（docs/19 §6.5.9）：ETL 三段——
    /// Read（UnitConfig.asset + zh-Hans 本地化表 + docs/19-派蒙知识库FAQ.md）
    /// → Transform（技能描述 {ParamKey} 代入纯文本值、TMP 富文本剥离、别名/关键词组装）
    /// → Write（Assets/StreamingAssets/pet_knowledge.json——随任意 Player 构建打包，桌宠独立进程
    /// 与游戏内形态共用同一份知识；运行时检索=PetKnowledgeIndex 词法打分）。
    /// 改角色配置/FAQ 后重跑本工具再构建即可；PetSpikeBuildTool 构建前自动调用（防知识过期）。
    /// 全量重写=天然幂等（docs/14 §8.2.1）。Menu: Tools/桌宠/烘焙派蒙知识库
    /// </summary>
    public static class PetKnowledgeBaker
    {
        private const string OutputPath = "Assets/StreamingAssets/pet_knowledge.json";
        private const string FaqSourcePath = "docs/19-派蒙知识库FAQ.md";

        [MenuItem("Tools/桌宠/烘焙派蒙知识库")]
        public static void BakeMenu() => Bake();

        /// <summary>烘焙入口（PetSpikeBuildTool 构建前自动调用；也可手动菜单重烘焙）</summary>
        public static void Bake()
        {
            var entries = new List<PetKnowledgeIndex.Entry>();
            int unitCount = BakeUnits(entries);
            int faqCount = BakeFaq(entries);
            WriteFile(entries, unitCount, faqCount);
        }

        // ==================== Read/Transform：角色条目（UnitConfig + 本地化表） ====================

        static int BakeUnits(List<PetKnowledgeIndex.Entry> entries)
        {
            var guids = AssetDatabase.FindAssets("t:UnitConfig");
            if (guids.Length == 0)
            {
                Debug.LogWarning("[PetKnowledgeBaker] 未找到 UnitConfig.asset，跳过角色条目");
                return 0;
            }
            var config = AssetDatabase.LoadAssetAtPath<UnitConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (config == null)
            {
                Debug.LogWarning("[PetKnowledgeBaker] UnitConfig.asset 加载失败，跳过角色条目");
                return 0;
            }

            int count = 0;
            foreach (var u in config.unitDataList)
            {
                if (u == null) continue;
                string enumName = u.unitName.ToString();
                string zhName = Zh(TableName.UnitName.ToString(), enumName) ?? enumName;
                string title = Zh(TableName.UnitTitle.ToString(), enumName);

                // 别名：枚举英文名（LLM 可能传 Kaeya/kaeya）+ 称号
                var aliases = new List<string> { enumName };
                if (!string.IsNullOrEmpty(title)) aliases.Add(title);

                // 关键词：元素/武器/势力/星级/标签/技能名（弱命中泛查用）
                var keywords = new List<string>();
                string elementZh = u.selfElement.GetInspectorName();
                keywords.Add(elementZh);
                keywords.Add(u.selfElement.ToString()); // Cryo 等枚举名
                keywords.Add(u.weaponType.GetInspectorName());
                keywords.Add($"{u.starLevel}星");
                keywords.Add(StarZh(u.starLevel));
                if (u.factions != null)
                    foreach (var f in u.factions) keywords.Add(f.GetInspectorName());
                if (u.tags != null)
                    foreach (var t in u.tags) keywords.Add(t.GetInspectorName());

                // 正文：属性概览 + 技能（描述模板代入参数值、富文本剥离）
                var body = new StringBuilder();
                if (!u.IsCharacter) body.Append("（造物）");
                body.Append(zhName);
                if (!string.IsNullOrEmpty(title)) body.Append("（").Append(title).Append("）");
                body.Append("——").Append(u.starLevel).Append("星").Append(elementZh).Append("元素");
                body.Append(u.IsCharacter ? "角色" : "造物");
                body.Append("，武器").Append(u.weaponType.GetInspectorName());
                if (u.factions != null && u.factions.Length > 0)
                    body.Append("，势力").Append(string.Join("、", u.factions.Select(f => f.GetInspectorName())));
                body.Append("。");
                body.Append("\n出战摩拉").Append(u.GetEffectiveDeployCost())
                    .Append("；生命").Append(u.GetEffectiveHP())
                    .Append("，攻击").Append(u.GetEffectiveAttack())
                    .Append("，攻速").Append(u.GetEffectiveAttackSpeed())
                    .Append("，移速").Append(u.GetEffectiveMoveSpeed())
                    .Append("，元能").Append(u.GetEffectiveEnergy())
                    .Append("，视野").Append(u.GetEffectiveVisionRange())
                    .Append("。");

                if (u.skills != null && u.skills.Length > 0)
                {
                    body.Append("\n技能：");
                    foreach (var s in u.skills)
                    {
                        if (s == null) continue;
                        string skillName = Zh(TableName.SkillName.ToString(), s.skillID.ToString()) ?? s.skillID.ToString();
                        string skillTypeZh = Zh(TableName.SkillType.ToString(), s.skillType.ToString())
                                            ?? s.skillType.GetInspectorName();
                        string desc = ResolveTemplate(
                            Zh(TableName.SkillDescription.ToString(), s.skillID.ToString()), s.customParams);
                        keywords.Add(skillName);
                        keywords.Add(s.skillID.ToString());
                        body.Append("\n").Append(skillName).Append("（").Append(skillTypeZh).Append("）：")
                            .Append(desc.Replace("\n", "；"));
                    }
                }

                entries.Add(new PetKnowledgeIndex.Entry
                {
                    id = "unit_" + enumName.ToLowerInvariant(),
                    category = "unit",
                    title = zhName,
                    aliases = aliases.ToArray(),
                    keywords = keywords.Distinct().ToArray(),
                    body = body.ToString(),
                });
                count++;
            }
            GICLog.Info($"[PetKnowledgeBaker] 角色条目 {count} 条（UnitConfig {config.unitDataList.Count} 项）");
            return count;
        }

        /// <summary>技能描述模板代入参数值（纯文本，无 &lt;color&gt;——LLM 上下文不吃富文本，也防模型
        /// 模仿标记符号进回复）。数值语义与 SkillDescriptionBuilder 一致：固定值→原值，
        /// 上下文百分比→N%，基底值→N%+基底名（走 SkillBaseType 表）</summary>
        static string ResolveTemplate(string template, SkillParam[] parameters)
        {
            if (string.IsNullOrEmpty(template)) return "";
            if (parameters == null || parameters.Length == 0) return StripRichText(template);

            var sb = new StringBuilder(template);
            foreach (var p in parameters)
            {
                if (p == null || p.key == SkillParamKey.None) continue;
                string placeholder = "{" + p.key + "}";
                if (!template.Contains(placeholder)) continue;

                string value;
                if (p.baseType == SkillBaseType.Fixed) value = p.value.ToString();
                else if (p.baseType == SkillBaseType.Percent) value = p.value + "%";
                else
                {
                    string baseName = Zh(TableName.SkillBaseType.ToString(), p.baseType.ToString())
                                      ?? BaseNameFallback(p.baseType);
                    value = p.value + "%" + baseName;
                }
                sb.Replace(placeholder, value);
            }
            return StripRichText(sb.ToString());
        }

        /// <summary>TMP 富文本剥离（&lt;color&gt;/&lt;u&gt;/&lt;link&gt; 等全剥，保留内文）</summary>
        static string StripRichText(string s) => Regex.Replace(s ?? "", "<[^>]+>", "");

        static string BaseNameFallback(SkillBaseType t) => t switch
        {
            SkillBaseType.BasedOnAttack => "攻击力",
            SkillBaseType.BasedOnMaxHealth => "最大生命值",
            SkillBaseType.BasedOnCurrentHealth => "当前生命值",
            SkillBaseType.BasedOnLostHealth => "已损生命值",
            SkillBaseType.BasedOnDefense => "防御力",
            SkillBaseType.BasedOnTargetMaxHealth => "目标最大生命值",
            SkillBaseType.BasedOnTargetCurrentHealth => "目标当前生命值",
            SkillBaseType.BasedOnTargetLostHealth => "目标已损生命值",
            SkillBaseType.BasedOnMoveSpeed => "移速",
            SkillBaseType.BasedOnSanity => "理智",
            _ => "",
        };

        static string StarZh(int star) => star switch
        {
            1 => "一星", 2 => "二星", 3 => "三星", 4 => "四星", 5 => "五星", _ => star + "星",
        };

        /// <summary>zh-Hans 表值读取（表/条目缺失返回 null 由调用方兜底）</summary>
        static string Zh(string tableName, string key)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
            if (collection == null) return null;
            foreach (var tableRef in collection.Tables)
            {
                var st = tableRef.asset as StringTable;
                if (st != null && st.LocaleIdentifier.Code == "zh-Hans")
                    return st.GetEntry(key)?.Value;
            }
            return null;
        }

        // ==================== Read/Transform：FAQ 条目（玩家向 markdown 源） ====================

        /// <summary>解析 docs/19-派蒙知识库FAQ.md：`## 标题 [别名1,别名2]` 起一条，正文到下一 ## 止；
        /// 一级标题与引言块（首个 ## 前）忽略。源文件不存在只警告不烘焙（角色条目不受影响）</summary>
        static int BakeFaq(List<PetKnowledgeIndex.Entry> entries)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string path = Path.Combine(projectRoot, FaqSourcePath);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[PetKnowledgeBaker] FAQ 源不存在（跳过 FAQ 条目）: {path}");
                return 0;
            }

            var headerRegex = new Regex(@"^##\s+(.+?)\s*(?:\[([^\]]*)\])?\s*$");
            int count = 0;
            string title = null;
            List<string> aliases = null;
            var body = new StringBuilder();

            void Flush()
            {
                if (title == null) return;
                string bodyText = body.ToString().Trim();
                if (bodyText.Length > 0)
                {
                    count++;
                    entries.Add(new PetKnowledgeIndex.Entry
                    {
                        id = $"faq_{count:D2}",
                        category = "faq",
                        title = title,
                        aliases = (aliases ?? new List<string>()).ToArray(),
                        keywords = Array.Empty<string>(),
                        body = bodyText,
                    });
                }
                title = null;
                aliases = null;
                body.Clear();
            }

            foreach (var raw in File.ReadAllLines(path))
            {
                var m = headerRegex.Match(raw);
                if (m.Success)
                {
                    Flush();
                    title = m.Groups[1].Value.Trim();
                    aliases = (m.Groups[2].Value ?? "")
                        .Split(',', '，')
                        .Select(a => a.Trim())
                        .Where(a => a.Length > 0)
                        .ToList();
                }
                else if (title != null)
                {
                    body.Append(raw.TrimEnd()).Append('\n');
                }
                // 首个 ## 之前的行（一级标题/引言）忽略
            }
            Flush();

            GICLog.Info($"[PetKnowledgeBaker] FAQ 条目 {count} 条（源: {FaqSourcePath}）");
            return count;
        }

        // ==================== Write：落盘 ====================

        static void WriteFile(List<PetKnowledgeIndex.Entry> entries, int unitCount, int faqCount)
        {
            var file = new
            {
                version = 1,
                bakedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                entries,
            };
            string dir = Path.GetDirectoryName(OutputPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            bool existed = File.Exists(OutputPath);
            File.WriteAllText(OutputPath, JsonConvert.SerializeObject(file, Formatting.Indented));
            AssetDatabase.ImportAsset(OutputPath);
            GICLog.Info($"[PetKnowledgeBaker] 烘焙完成：角色 {unitCount} + FAQ {faqCount} = {entries.Count} 条" +
                        $" → {OutputPath}（{(existed ? "覆盖旧文件" : "新建")}，bakedAt={file.bakedAt}）");
        }
    }
}
#endif
