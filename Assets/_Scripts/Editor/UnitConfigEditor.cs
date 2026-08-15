// ============================================
// UnitConfigEditor - UI Toolkit 版 Inspector 与编辑窗口
// ============================================
// UnitDataEditorWindow 布局（与 IMGUI 版一致）：
//   左=单位属性字段（排除 skills）；右=技能 ListView + 选中技能字段编辑区；
//   左右分栏可拖拽。旧版 customParamsListWithMenu 从未被渲染（死 UI），不再重建，
//   选中技能的 customParams 由默认数组 PropertyField 绘制。
// 保存语义：绑定后修改即时写入 SerializedObject（可 Ctrl+Z 撤销），
// "保存到磁盘"负责落盘；不再有"未保存更改"拦截弹窗。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using GIC.Framework;
using GIC.Data;
using GIC.Battle;
using GIC.Tool;

namespace GIC.Editor
{
    [CustomEditor(typeof(UnitConfig))]
    public class UnitConfigEditor : ConfigInspectorBase
    {
        private const string VOICE_ROOT_PATH = "Assets/Resources/Audios/Voices/";

        protected override string ListPropertyName => "unitDataList";
        protected override string ListHeaderTitle => "单位数据列表";

        protected override string GetElementName(SerializedProperty element)
            => ((UnitName)element.FindPropertyRelative("unitName").intValue).GetInspectorName();

        protected override string GetElementBadge(SerializedProperty element)
            => GetCombinedTag(
                element.FindPropertyRelative("unitType"),
                element.FindPropertyRelative("factions"));

        protected override int GetElementStars(SerializedProperty element)
            => element.FindPropertyRelative("starLevel").intValue;

        protected override void EditElement(int index)
            => UnitDataEditorWindow.OpenWindow((UnitConfig)target, index);

        protected override VisualElement BuildTools(SerializedObject so)
        {
            var config = (UnitConfig)target;
            var section = ConfigEditorUITK.CreateToolsSection("批量填充工具");
            section.Add(ConfigEditorUITK.CreateToolButton("自动加载所有角色语音", () => AutoFillAllVoices(config)));
            section.Add(ConfigEditorUITK.CreateToolButton("仅加载缺失的语音", () => AutoFillMissingVoices(config)));
            section.Add(ConfigEditorUITK.CreateToolButton("自动加载所有图片", () => AutoFillAllImages(config)));
            section.Add(ConfigEditorUITK.CreateToolButton("仅加载缺失的图片", () => AutoFillMissingImages(config)));
            return section;
        }

        private string GetCombinedTag(SerializedProperty unitTypeProperty, SerializedProperty factionsProperty)
        {
            string typeName = ((UnitType)unitTypeProperty.intValue).GetInspectorName();
            string factionName = GetFirstFactionDisplayName(factionsProperty);

            if (!string.IsNullOrEmpty(factionName) && !string.IsNullOrEmpty(typeName))
                return $"{factionName}{typeName}";
            if (!string.IsNullOrEmpty(typeName)) return typeName;
            if (!string.IsNullOrEmpty(factionName)) return factionName;
            return "未知";
        }

        private string GetFirstFactionDisplayName(SerializedProperty factionsProperty)
        {
            if (factionsProperty == null || factionsProperty.arraySize == 0) return "";
            int enumValue = factionsProperty.GetArrayElementAtIndex(0).intValue;
            return ((FactionType)enumValue).GetInspectorName();
        }

        #region 语音自动填充（逻辑与 IMGUI 版一致，日志走 GICLog）

        private void AutoFillAllVoices(UnitConfig config)
        {
            int success = 0, fail = 0;
            var warnings = new List<string>();

            foreach (var unitData in config.unitDataList)
            {
                if (unitData == null) continue;
                if (LoadVoicesForUnit(unitData, warnings)) success++; else fail++;
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            GICLog.Info($"语音加载完成: 成功 {success}, 失败 {fail}");
            if (warnings.Count > 0)
                GICLog.Warn($"语音加载警告 ({warnings.Count}):\n" + string.Join("\n", warnings));
        }

        private void AutoFillMissingVoices(UnitConfig config)
        {
            int loaded = 0, skipped = 0;
            var warnings = new List<string>();

            foreach (var unitData in config.unitDataList)
            {
                if (unitData == null) continue;
                if (unitData.voices != null && IsVoiceDataComplete(unitData.voices))
                {
                    skipped++;
                    continue;
                }
                if (LoadVoicesForUnit(unitData, warnings)) loaded++; else skipped++;
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            GICLog.Info($"语音加载完成: 加载 {loaded}, 跳过 {skipped}");
            if (warnings.Count > 0)
                GICLog.Warn($"语音加载警告 ({warnings.Count}):\n" + string.Join("\n", warnings));
        }

        private bool LoadVoicesForUnit(UnitConfig.UnitData unitData, List<string> warnings)
        {
            string folderPath = Path.Combine(VOICE_ROOT_PATH, unitData.unitName.ToString());

            if (!Directory.Exists(folderPath))
            {
                warnings.Add($"[{unitData.unitName}] 目录不存在: {folderPath}");
                return false;
            }

            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { folderPath });
            if (guids.Length == 0)
            {
                warnings.Add($"[{unitData.unitName}] 无音频文件");
                return false;
            }

            unitData.voices ??= new UnitConfig.UnitVoiceData();

            // 构建 SkillName snake_case → SkillName 映射
            var skillNameMap = new Dictionary<string, SkillName>();
            foreach (SkillName skill in Enum.GetValues(typeof(SkillName)))
            {
                if (skill == SkillName.None) continue;
                skillNameMap[skill.ToString().ToSnakeCase()] = skill;
            }

            var genericMap = new (string prefix, Action<UnitConfig.UnitVoiceData, AudioClip[]> setter)[]
            {
                ("go_war",         (v, c) => v.onGoWar        = CreateAudioClipRandom(c)),
                ("choose_high_hp", (v, c) => v.onChooseHighHP = CreateAudioClipRandom(c)),
                ("choose_low_hp",  (v, c) => v.onChooseLowHP  = CreateAudioClipRandom(c)),
                ("hit_light",      (v, c) => v.onHitLight     = CreateAudioClipRandom(c)),
                ("hit_heavy",      (v, c) => v.onHitHeavy     = CreateAudioClipRandom(c)),
                ("die",            (v, c) => v.onDie          = CreateAudioClipRandom(c)),
            };

            var genericClips = new Dictionary<string, List<AudioClip>>();
            var skillClips = new Dictionary<SkillName, List<AudioClip>>();
            var unmatched = new List<string>();

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
                if (clip == null) continue;

                bool matched = false;
                foreach (var (prefix, _) in genericMap)
                {
                    if (fileName.StartsWith(prefix + "_") || fileName == prefix)
                    {
                        if (!genericClips.ContainsKey(prefix))
                            genericClips[prefix] = new List<AudioClip>();
                        genericClips[prefix].Add(clip);
                        matched = true;
                        break;
                    }
                }
                if (matched) continue;

                // 技能语音：最长前缀匹配
                string skillPrefix = null;
                SkillName matchedSkill = SkillName.None;

                foreach (var kvp in skillNameMap)
                {
                    if (fileName.StartsWith(kvp.Key + "_"))
                    {
                        if (skillPrefix == null || kvp.Key.Length > skillPrefix.Length)
                        {
                            skillPrefix = kvp.Key;
                            matchedSkill = kvp.Value;
                        }
                    }
                }

                if (skillPrefix != null)
                {
                    if (!skillClips.ContainsKey(matchedSkill))
                        skillClips[matchedSkill] = new List<AudioClip>();
                    skillClips[matchedSkill].Add(clip);
                    continue;
                }

                if (fileName.StartsWith("move_skill_") || fileName == "move_skill")
                {
                    warnings.Add($"[{unitData.unitName}] 未映射的 move_skill: {fileName}");
                    continue;
                }

                unmatched.Add(fileName);
            }

            // 填充通用语音
            foreach (var (prefix, setter) in genericMap)
            {
                if (genericClips.TryGetValue(prefix, out var clips) && clips.Count > 0)
                    setter(unitData.voices, clips.ToArray());
            }

            // 填充技能语音
            if (skillClips.Count > 0)
            {
                var entries = new List<UnitConfig.SkillVoiceEntry>();
                foreach (var kvp in skillClips)
                {
                    if (kvp.Value.Count > 0)
                        entries.Add(new UnitConfig.SkillVoiceEntry
                        {
                            skillName = kvp.Key,
                            voices = CreateAudioClipRandom(kvp.Value.ToArray())
                        });
                }
                unitData.voices.skillVoices = entries.ToArray();
            }

            if (unmatched.Count > 0)
                warnings.Add($"[{unitData.unitName}] 未匹配文件: {string.Join(", ", unmatched)}");

            return true;
        }

        private AudioClipRandom CreateAudioClipRandom(AudioClip[] clips)
        {
            var random = new AudioClipRandom();
            foreach (var clip in clips)
                random.AddClip(clip);
            return random;
        }

        private int CountFilledVoiceGroups(UnitConfig.UnitVoiceData voices)
        {
            int count = 0;
            if (voices.onGoWar != null && voices.onGoWar.Count > 0) count++;
            if (voices.onChooseHighHP != null && voices.onChooseHighHP.Count > 0) count++;
            if (voices.onChooseLowHP != null && voices.onChooseLowHP.Count > 0) count++;
            if (voices.onHitLight != null && voices.onHitLight.Count > 0) count++;
            if (voices.onHitHeavy != null && voices.onHitHeavy.Count > 0) count++;
            if (voices.onDie != null && voices.onDie.Count > 0) count++;
            return count;
        }

        private bool IsVoiceDataComplete(UnitConfig.UnitVoiceData voices)
        {
            return CountFilledVoiceGroups(voices) == 6;
        }

        #endregion

        #region 图片自动填充（逻辑与 IMGUI 版一致，日志走 GICLog）

        private const string AVATAR_PATH = "Resources/UI/Avatars/";
        private const string CARD_PATH = "Resources/UI/Cards/";
        private const string NAMECARD_PATH = "Resources/UI/NameCards/";

        private void AutoFillAllImages(UnitConfig config)
        {
            int success = 0, fail = 0;
            var missing = new List<string>();

            foreach (var unitData in config.unitDataList)
            {
                if (unitData == null) continue;
                if (LoadImagesForUnit(unitData, missing))
                    success++;
                else
                    fail++;
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            GICLog.Info($"图片加载完成: 成功 {success}, 失败 {fail}");
            if (missing.Count > 0)
                GICLog.Warn($"缺失的图片文件 ({missing.Count}个):\n" + string.Join("\n", missing));
            EditorUtility.DisplayDialog("完成", $"图片加载完成\n成功: {success}\n失败: {fail}\n缺失文件数: {missing.Count}", "确定");
        }

        private void AutoFillMissingImages(UnitConfig config)
        {
            int loaded = 0, skipped = 0;
            var missing = new List<string>();

            foreach (var unitData in config.unitDataList)
            {
                if (unitData == null) continue;

                bool needsUpdate = false;

                if (unitData.avatar == null)
                {
                    var sprite = LoadSprite(AVATAR_PATH, unitData.unitName.ToString().ToSnakeCase(), missing);
                    if (sprite != null && !IsNullSprite(sprite, AVATAR_PATH))
                    {
                        unitData.avatar = sprite;
                        loaded++;
                    }
                    needsUpdate = true;
                }

                if (unitData.nameCard == null)
                {
                    var sprite = LoadSprite(NAMECARD_PATH, unitData.unitName.ToString().ToSnakeCase(), missing);
                    if (sprite != null && !IsNullSprite(sprite, NAMECARD_PATH))
                    {
                        unitData.nameCard = sprite;
                        loaded++;
                    }
                    needsUpdate = true;
                }

                if (unitData.cards == null || unitData.cards.Count == 0)
                {
                    LoadCardsForUnit(unitData, missing);
                    loaded++;
                    needsUpdate = true;
                }

                if (!needsUpdate) skipped++;
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            GICLog.Info($"图片加载完成: 加载 {loaded}, 跳过 {skipped}");
            if (missing.Count > 0)
                GICLog.Warn($"缺失的图片文件 ({missing.Count}个):\n" + string.Join("\n", missing));
            EditorUtility.DisplayDialog("完成", $"图片加载完成\n加载: {loaded}\n跳过: {skipped}\n缺失文件数: {missing.Count}", "确定");
        }

        private bool LoadImagesForUnit(UnitConfig.UnitData unitData, List<string> missing)
        {
            string baseName = unitData.unitName.ToString().ToSnakeCase();
            bool allSuccess = true;

            var avatar = LoadSprite(AVATAR_PATH, baseName, missing);
            if (avatar != null && !IsNullSprite(avatar, AVATAR_PATH))
                unitData.avatar = avatar;
            else
                allSuccess = false;

            var nameCard = LoadSprite(NAMECARD_PATH, baseName, missing);
            if (nameCard != null && !IsNullSprite(nameCard, NAMECARD_PATH))
                unitData.nameCard = nameCard;
            else
                allSuccess = false;

            LoadCardsForUnit(unitData, missing);

            return allSuccess;
        }

        private void LoadCardsForUnit(UnitConfig.UnitData unitData, List<string> missing)
        {
            string baseName = unitData.unitName.ToString().ToSnakeCase();

            if (unitData.cards == null)
                unitData.cards = new List<Sprite>();
            else
                unitData.cards.Clear();

            int skinIndex = 0;
            while (true)
            {
                string fileName = skinIndex == 0 ? baseName : $"{baseName}_{skinIndex}";
                var sprite = LoadSprite(CARD_PATH, fileName, missing);

                if (sprite != null && !IsNullSprite(sprite, CARD_PATH))
                {
                    unitData.cards.Add(sprite);
                    skinIndex++;
                }
                else
                    break;
            }

            if (unitData.cards.Count == 0)
            {
                Sprite nullSprite = LoadSprite(CARD_PATH, "null", missing);
                if (nullSprite != null)
                {
                    unitData.cards.Add(nullSprite);
                    GICLog.Warn($"角色 {unitData.unitName} 没有真实卡片，已添加 null 占位");
                }
            }
        }

        private Sprite LoadSprite(string folderPath, string fileName, List<string> missing)
        {
            string cleanPath = folderPath.Replace("Resources/", "");
            string fullPath = $"{cleanPath}{fileName}";
            var sprite = Resources.Load<Sprite>(fullPath);

            if (sprite != null)
            {
                if (IsNullSprite(sprite, folderPath))
                {
                    missing?.Add($"{folderPath}{fileName}");
                    return sprite;
                }
                return sprite;
            }

            Sprite nullSprite = Resources.Load<Sprite>($"{cleanPath}null");
            if (nullSprite != null)
            {
                missing?.Add($"{folderPath}{fileName}");
                return nullSprite;
            }

            return null;
        }

        private bool IsNullSprite(Sprite sprite, string folderPath)
        {
            if (sprite == null) return false;
            string cleanPath = folderPath.Replace("Resources/", "");
            string nullSpritePath = $"{cleanPath}null";
            string assetPath = AssetDatabase.GetAssetPath(sprite);
            return assetPath != null && (
                assetPath.Contains($"{nullSpritePath}.png") ||
                assetPath.Contains($"{nullSpritePath}.jpg") ||
                assetPath.Contains($"{nullSpritePath}.jpeg") ||
                assetPath.Contains($"{nullSpritePath}.psd")
            );
        }

        #endregion
    }

    // ============================================
    // UnitDataEditorWindow - UI Toolkit 版编辑窗口（左右分栏 + 技能子列表）
    // ============================================

    public class UnitDataEditorWindow : EditorWindow
    {
        private static readonly Color SplitterBase = new Color(0.45f, 0.45f, 0.45f, 0.5f);
        private static readonly Color SplitterHot = new Color(0.25f, 0.5f, 0.85f, 0.9f);

        private UnitConfig targetConfig;
        private int elementIndex = -1;
        private SerializedObject serializedObj;
        private string unitDisplayName = "";

        private ListView skillsList;
        private ScrollView skillEditorScroll;

        private VisualElement mainRow;
        private VisualElement leftPane;

        private float splitRatio = 0.45f;
        private bool isDraggingSplit = false;

        private string SkillsPath => $"unitDataList.Array.data[{elementIndex}].skills";

        public static void OpenWindow(UnitConfig config, int index)
        {
            var window = GetWindow<UnitDataEditorWindow>("编辑单位数据");
            window.targetConfig = config;
            window.elementIndex = index;
            window.serializedObj = new SerializedObject(config);

            var listProp = window.serializedObj.FindProperty("unitDataList");
            if (listProp != null && index < listProp.arraySize)
            {
                var element = listProp.GetArrayElementAtIndex(index);
                window.unitDisplayName = ((UnitName)element.FindPropertyRelative("unitName").intValue).GetInspectorName();
            }

            window.minSize = new Vector2(800, 600);
            var main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(
                main.x + (main.width - 1200) / 2,
                main.y + (main.height - 900) / 2,
                1200, 900);

            // GetWindow 复用已打开的窗口时，CreateGUI 不会再触发，需手动重建
            if (window.rootVisualElement.childCount > 0)
            {
                window.rootVisualElement.Clear();
                window.BuildUI();
            }
        }

        // CreateGUI 为按名调用的魔法方法（Tuanjie 中非虚方法），不加 override
        protected void CreateGUI() => BuildUI();

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 8;

            if (targetConfig == null || serializedObj == null)
            {
                root.Add(new Label("无数据可编辑"));
                return;
            }

            var listProp = serializedObj.FindProperty("unitDataList");
            if (elementIndex < 0 || elementIndex >= listProp.arraySize)
            {
                root.Add(new Label("单位数据不存在，可能已被删除"));
                return;
            }

            var element = listProp.GetArrayElementAtIndex(elementIndex);
            int starLevel = element.FindPropertyRelative("starLevel").intValue;
            root.Add(ConfigEditorUITK.CreateTitleRow($"编辑: {unitDisplayName}", starLevel));

            // ===== 主行：左=单位属性 / 分栏条 / 右=技能配置 =====
            mainRow = new VisualElement();
            mainRow.style.flexDirection = FlexDirection.Row;
            mainRow.style.flexGrow = 1f;
            root.Add(mainRow);

            BuildLeftPane();
            var divider = BuildSplitDivider();
            mainRow.Add(divider);
            BuildRightPane();

            root.Bind(serializedObj);

            root.Add(ConfigEditorUITK.CreateFooterBar(
                "修改即时生效（可 Ctrl+Z 撤销），保存到磁盘后写入文件", SaveToDisk));

            ApplySplit();
        }

        private void BuildLeftPane()
        {
            leftPane = new VisualElement();
            leftPane.style.minWidth = 280;
            mainRow.Add(leftPane);

            leftPane.Add(ConfigEditorUITK.CreateSectionHeader("单位属性"));

            var scroll = new ScrollView();
            var element = serializedObj.FindProperty("unitDataList").GetArrayElementAtIndex(elementIndex);

            // 绘制所有属性，排除 skills 和 displayName（Sprite 走预览控件）
            var it = element.Copy();
            var end = element.GetEndProperty();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren) && !SerializedProperty.EqualContents(it, end))
            {
                enterChildren = false;
                if (it.name == "skills") continue;
                if (it.name == "displayName") continue;
                scroll.Add(ConfigEditorUITK.CreateField(serializedObj, it.Copy()));
            }
            leftPane.Add(scroll);
        }

        private void BuildRightPane()
        {
            var rightPane = new VisualElement();
            rightPane.style.flexGrow = 1f;
            rightPane.style.minWidth = 350;
            mainRow.Add(rightPane);

            rightPane.Add(ConfigEditorUITK.CreateSectionHeader("技能配置"));

            var rightRow = new VisualElement();
            rightRow.style.flexDirection = FlexDirection.Row;
            rightRow.style.flexGrow = 1f;
            rightPane.Add(rightRow);

            // 技能列表（点击行选中；类型徽标 + 技能名）
            var skillsPane = new VisualElement();
            skillsPane.style.width = Length.Percent(45f);
            rightRow.Add(skillsPane);

            skillsList = ConfigEditorUITK.CreateList(serializedObj, SkillsPath, new ConfigEditorUITK.ListConfig
            {
                HeaderTitle = "技能列表",
                NameProvider = skillElement => ((SkillName)skillElement.FindPropertyRelative("skillID").intValue).GetInspectorName(),
                BadgeProvider = skillElement => ((SkillType)skillElement.FindPropertyRelative("skillType").intValue).GetInspectorName(),
                OnSelectionChanged = SelectSkill,
            });
            skillsPane.Add(skillsList);

            // 选中技能编辑区
            var editorPane = new VisualElement();
            editorPane.style.flexGrow = 1f;
            editorPane.style.paddingLeft = 8;
            rightRow.Add(editorPane);

            skillEditorScroll = new ScrollView();
            editorPane.Add(skillEditorScroll);
            ShowSkillHint("请选择左侧技能进行编辑");
        }

        private VisualElement BuildSplitDivider()
        {
            var divider = new VisualElement();
            divider.style.width = 5;
            ConfigEditorUITK.SetBorderRadius(divider, 2);
            divider.style.backgroundColor = SplitterBase;

            divider.RegisterCallback<PointerEnterEvent>(_ => divider.style.backgroundColor = SplitterHot);
            divider.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (!isDraggingSplit) divider.style.backgroundColor = SplitterBase;
            });
            divider.RegisterCallback<PointerDownEvent>(e =>
            {
                isDraggingSplit = true;
                divider.style.backgroundColor = SplitterHot;
                divider.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            divider.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!isDraggingSplit) return;
                UpdateSplit(e.position);
            });
            divider.RegisterCallback<PointerUpEvent>(e =>
            {
                isDraggingSplit = false;
                divider.ReleasePointer(e.pointerId);
                divider.style.backgroundColor = SplitterHot; // 指针仍在其上
            });
            return divider;
        }

        private void UpdateSplit(Vector3 panelPosition)
        {
            if (mainRow == null) return;
            var local = mainRow.WorldToLocal(panelPosition);
            float width = mainRow.resolvedStyle.width;
            if (width > 1f)
                splitRatio = Mathf.Clamp(local.x / width, 0.3f, 0.7f);
            ApplySplit();
        }

        private void ApplySplit()
        {
            leftPane.style.width = Length.Percent(splitRatio * 100f);
        }

        #region 技能选择与编辑区

        private void ShowSkillHint(string message)
        {
            skillEditorScroll.Clear();
            skillEditorScroll.Add(new HelpBox(message, HelpBoxMessageType.Info));
        }

        private void SelectSkill(int index)
        {
            if (skillEditorScroll == null) return;

            var skillsProp = serializedObj.FindProperty(SkillsPath);
            if (index < 0 || skillsProp == null || index >= skillsProp.arraySize)
            {
                ShowSkillHint("请选择左侧技能进行编辑");
                return;
            }

            skillEditorScroll.Clear();
            var skillProp = skillsProp.GetArrayElementAtIndex(index);

            var it = skillProp.Copy();
            var end = skillProp.GetEndProperty();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren) && !SerializedProperty.EqualContents(it, end))
            {
                enterChildren = false;
                skillEditorScroll.Add(ConfigEditorUITK.CreateField(serializedObj, it.Copy()));
            }
            skillEditorScroll.Bind(serializedObj);
        }

        #endregion

        private void SaveToDisk()
        {
            if (serializedObj == null || targetConfig == null) return;
            serializedObj.ApplyModifiedProperties();
            EditorUtility.SetDirty(targetConfig);
            AssetDatabase.SaveAssets();
            GICLog.Info($"单位数据 '{unitDisplayName}' 已保存到磁盘");
        }

        private void OnDestroy()
        {
            serializedObj?.Dispose();
        }
    }
}
