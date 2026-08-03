// ============================================
// UnitConfigEditor - 精简后的 Inspector
// ============================================

using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Editor
{


    [CustomEditor(typeof(UnitConfig))]
    public class UnitConfigEditor : UnityEditor.Editor
    {
        private const string VOICE_ROOT_PATH = "Assets/Resources/Audios/Voices/";

        private ReorderableListWithMenu listWithMenu;

        private void OnEnable()
        {
            var listProperty = serializedObject.FindProperty("unitDataList");

            listWithMenu = new ReorderableListWithMenu(
                serializedObject, listProperty,
                draggable: true, displayHeader: true, displayAdd: true, displayRemove: true,
                headerText: "单位数据列表",
                drawElement: DrawElement,
                elementHeight: EditorGUIUtility.singleLineHeight + 2f,
                onEdit: (index) => UnitDataEditorWindow.OpenWindow((UnitConfig)target, index)
            );
        }

        private void DrawElement(Rect rect, int index)
        {
            rect.x += 20f;
            rect.width -= 20f;

            var listProperty = serializedObject.FindProperty("unitDataList");
            var element = listProperty.GetArrayElementAtIndex(index);
            var unitNameProperty = element.FindPropertyRelative("unitName");
            var starLevelProperty = element.FindPropertyRelative("starLevel");
            var unitTypeProperty = element.FindPropertyRelative("unitType");
            var factionsProperty = element.FindPropertyRelative("factions");

            // 修正：使用 intValue 而不是 enumValueIndex
            var unitNameValue = (UnitName)unitNameProperty.intValue;
            var starLevel = starLevelProperty.intValue;

            string displayName = unitNameValue.GetInspectorName();

            string starIcons = new string('★', starLevel);
            string combinedTag = GetCombinedTag(unitTypeProperty, factionsProperty);
            string title = $"{combinedTag} {displayName}  {starIcons}";

            Rect summaryRect = new Rect(rect.x, rect.y, rect.width - 60f, EditorGUIUtility.singleLineHeight);
            Rect buttonRect = new Rect(rect.x + rect.width - 55f, rect.y, 55f, EditorGUIUtility.singleLineHeight);

            EditorGUI.LabelField(summaryRect, title);

            if (GUI.Button(buttonRect, "编辑"))
            {
                UnitDataEditorWindow.OpenWindow((UnitConfig)target, index);
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            listWithMenu.DoLayoutList();

            if (Event.current.type == UnityEngine.EventType.ContextClick)
            {
                listWithMenu.ShowContextMenu();
            }

            // ========== 批量填充工具 ==========
            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField("批量填充工具", EditorStyles.boldLabel);

            if (GUILayout.Button("自动加载所有角色语音", GUILayout.Height(30)))
            {
                AutoFillAllVoices((UnitConfig)target);
            }

            if (GUILayout.Button("仅加载缺失的语音", GUILayout.Height(30)))
            {
                AutoFillMissingVoices((UnitConfig)target);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private string GetCombinedTag(SerializedProperty unitTypeProperty, SerializedProperty factionsProperty)
        {
            string typeName = ((UnitType)unitTypeProperty.intValue).GetInspectorName();
            string factionName = GetFirstFactionDisplayName(factionsProperty);

            if (!string.IsNullOrEmpty(factionName) && !string.IsNullOrEmpty(typeName))
                return $"[{factionName}{typeName}]";
            if (!string.IsNullOrEmpty(typeName)) return $"[{typeName}]";
            if (!string.IsNullOrEmpty(factionName)) return $"[{factionName}]";
            return "[未知]";
        }

        private string GetFirstFactionDisplayName(SerializedProperty factionsProperty)
        {
            if (factionsProperty == null || factionsProperty.arraySize == 0) return "";
            // 修正：使用 intValue 获取实际枚举值
            int enumValue = factionsProperty.GetArrayElementAtIndex(0).intValue;
            return ((FactionType)enumValue).GetInspectorName();
        }

        #region 语音自动填充

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

            Debug.Log($"语音加载完成: 成功 {success}, 失败 {fail}");
            if (warnings.Count > 0)
                Debug.LogWarning($"语音加载警告 ({warnings.Count}):\n" + string.Join("\n", warnings));
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

            Debug.Log($"语音加载完成: 加载 {loaded}, 跳过 {skipped}");
            if (warnings.Count > 0)
                Debug.LogWarning($"语音加载警告 ({warnings.Count}):\n" + string.Join("\n", warnings));
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
    }

    // ============================================
    // UnitDataEditorWindow - 精简后的编辑窗口
    // ============================================

    public class UnitDataEditorWindow : EditorWindow
    {
        // 数据
        private UnitConfig targetConfig;
        private int elementIndex;
        private SerializedObject configSerializedObject;
        private string unitDisplayName;
        private bool dataChanged = false;

        // 滚动
        private Vector2 unitScrollPosition;
        private Vector2 skillScrollPosition;

        // 技能列表
        private ReorderableListWithMenu skillsListWithMenu;
        private int selectedSkillIndex = -1;
        private SerializedProperty selectedSkillProperty;

        // 自定义参数列表
        private ReorderableListWithMenu customParamsListWithMenu;
        private SerializedProperty currentCustomParamsProperty;

        // 分栏
        private float splitRatio = 0.45f;
        private bool isDraggingSplit = false;

        public static void OpenWindow(UnitConfig config, int index)
        {
            var window = GetWindow<UnitDataEditorWindow>("编辑单位数据");
            window.targetConfig = config;
            window.elementIndex = index;
            window.dataChanged = false;
            window.selectedSkillIndex = -1;
            window.selectedSkillProperty = null;
            window.customParamsListWithMenu = null;
            window.currentCustomParamsProperty = null;
            window.configSerializedObject = new SerializedObject(config);

            // 获取显示名称
            var listProp = window.configSerializedObject.FindProperty("unitDataList");
            if (listProp != null && index < listProp.arraySize)
            {
                var element = listProp.GetArrayElementAtIndex(index);
                var unitNameProp = element.FindPropertyRelative("unitName");
                // 修正：使用 intValue 而不是 enumValueIndex
                var unitNameValue = (UnitName)unitNameProp.intValue;
                window.unitDisplayName = unitNameValue.GetInspectorName();
            }

            window.InitializeSkillsList();

            float width = 1200, height = 900;
            window.minSize = new Vector2(800, 600);
            var mainPos = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(
                mainPos.x + (mainPos.width - width) / 2,
                mainPos.y + (mainPos.height - height) / 2,
                width, height);
            window.Show();
        }

        #region 初始化列表

        private void InitializeSkillsList()
        {
            if (targetConfig == null || configSerializedObject == null) return;

            var listProp = configSerializedObject.FindProperty("unitDataList");
            if (elementIndex < 0 || elementIndex >= listProp.arraySize) return;

            var element = listProp.GetArrayElementAtIndex(elementIndex);
            var skillsProperty = element.FindPropertyRelative("skills");

            skillsListWithMenu = new ReorderableListWithMenu(
                configSerializedObject, skillsProperty,
                draggable: true, displayHeader: true, displayAdd: true, displayRemove: true,
                headerText: "技能列表",
                drawElement: DrawSkillElement,
                elementHeight: EditorGUIUtility.singleLineHeight + 4f,
                onEdit: (index) => { },
                onChanged: () => dataChanged = true,
                onAdd: (list) =>
                {
                    list.serializedProperty.arraySize++;
                    var newEl = list.serializedProperty.GetArrayElementAtIndex(list.serializedProperty.arraySize - 1);
                    newEl.FindPropertyRelative("skillID").intValue = 0;
                    newEl.FindPropertyRelative("skillType").intValue = 0;
                    newEl.FindPropertyRelative("energyCost").intValue = 0;
                    newEl.FindPropertyRelative("moraCost").intValue = 0;
                    newEl.FindPropertyRelative("staminaCost").intValue = 0;
                    configSerializedObject.ApplyModifiedProperties();
                    dataChanged = true;
                    SelectSkill(list.serializedProperty.arraySize - 1);
                },
                onRemove: (list) =>
                {
                    if (selectedSkillIndex >= list.serializedProperty.arraySize - 1)
                    {
                        selectedSkillIndex = -1;
                        selectedSkillProperty = null;
                        customParamsListWithMenu = null;
                        currentCustomParamsProperty = null;
                    }
                    ReorderableList.defaultBehaviours.DoRemoveButton(list);
                    dataChanged = true;
                },
                onReorder: (list) =>
                {
                    if (selectedSkillProperty != null)
                    {
                        string path = selectedSkillProperty.propertyPath;
                        for (int i = 0; i < list.serializedProperty.arraySize; i++)
                        {
                            if (list.serializedProperty.GetArrayElementAtIndex(i).propertyPath == path)
                            {
                                selectedSkillIndex = i;
                                break;
                            }
                        }
                    }
                    dataChanged = true;
                }
            );
        }

        private void DrawSkillElement(Rect rect, int index)
        {
            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;

            var skillsProperty = configSerializedObject.FindProperty("unitDataList")
                .GetArrayElementAtIndex(elementIndex).FindPropertyRelative("skills");
            var skillElement = skillsProperty.GetArrayElementAtIndex(index);

            var skillIDValue = (SkillName)skillElement.FindPropertyRelative("skillID").intValue;
            var skillType = (SkillType)skillElement.FindPropertyRelative("skillType").intValue;

            string summary = $"[{skillType.GetInspectorName()}] {skillIDValue.GetInspectorName()}";

            if (index == selectedSkillIndex)
                EditorGUI.DrawRect(rect, new Color(0.3f, 0.5f, 0.8f, 0.3f));

            Rect summaryRect = new Rect(rect.x, rect.y, rect.width, rect.height);

            EditorGUI.LabelField(summaryRect, summary);

            if (Event.current.type == UnityEngine.EventType.MouseDown &&
                Event.current.button == 0 &&
                summaryRect.Contains(Event.current.mousePosition))
            {
                SelectSkill(index);
                Event.current.Use();
            }
        }

        private void InitializeCustomParamsList(SerializedProperty customParamsProperty)
        {
            currentCustomParamsProperty = customParamsProperty;

            customParamsListWithMenu = new ReorderableListWithMenu(
                configSerializedObject, customParamsProperty,
                draggable: true, displayHeader: true, displayAdd: true, displayRemove: true,
                headerText: "自定义参数",
                drawElement: DrawCustomParamElement,
                elementHeight: EditorGUIUtility.singleLineHeight * 2 + 12f,
                onChanged: () => dataChanged = true,
                onAdd: (list) =>
                {
                    list.serializedProperty.arraySize++;
                    var newEl = list.serializedProperty.GetArrayElementAtIndex(list.serializedProperty.arraySize - 1);
                    newEl.FindPropertyRelative("key").intValue = 0;
                    newEl.FindPropertyRelative("value").intValue = 0;
                    newEl.FindPropertyRelative("baseType").intValue = 0;
                    configSerializedObject.ApplyModifiedProperties();
                    dataChanged = true;
                },
                onRemove: (list) =>
                {
                    ReorderableList.defaultBehaviours.DoRemoveButton(list);
                    dataChanged = true;
                },
                onReorder: (list) => dataChanged = true
            );
        }

        private void DrawCustomParamElement(Rect rect, int index)
        {
            float dragHandleWidth = 15f;
            Rect contentRect = new Rect(rect.x + dragHandleWidth, rect.y + 2f,
                rect.width - dragHandleWidth, rect.height - 4f);

            var paramElement = currentCustomParamsProperty.GetArrayElementAtIndex(index);
            var keyProperty = paramElement.FindPropertyRelative("key");
            var valueProperty = paramElement.FindPropertyRelative("value");
            var baseTypeProperty = paramElement.FindPropertyRelative("baseType");

            float lineH = EditorGUIUtility.singleLineHeight;
            float pad = 2f;

            Rect row1 = new Rect(contentRect.x, contentRect.y, contentRect.width, lineH);
            float labelW1 = 30f, labelW2 = 30f;
            float fieldW = (row1.width - labelW1 - labelW2 - pad) / 2;

            EditorGUI.LabelField(new Rect(row1.x, row1.y, labelW1, lineH), "参数");
            keyProperty.intValue = EditorGUI.Popup(
                new Rect(row1.x + labelW1, row1.y, fieldW, lineH),
                keyProperty.intValue, keyProperty.enumDisplayNames);

            EditorGUI.LabelField(new Rect(row1.x + labelW1 + fieldW + pad, row1.y, labelW2, lineH), "类型");
            baseTypeProperty.intValue = EditorGUI.Popup(
                new Rect(row1.x + labelW1 + fieldW + pad + labelW2, row1.y, fieldW, lineH),
                baseTypeProperty.intValue, baseTypeProperty.enumDisplayNames);

            Rect row2 = new Rect(contentRect.x, contentRect.y + lineH + pad, contentRect.width, lineH);
            float vw1 = 30f;
            float vFieldW = row2.width - vw1 - pad;

            EditorGUI.LabelField(new Rect(row2.x, row2.y, vw1, lineH), "值");
            valueProperty.intValue = EditorGUI.IntField(
                new Rect(row2.x + vw1, row2.y, vFieldW, lineH), valueProperty.intValue);
        }

        #endregion

        #region 技能选择

        private void SelectSkill(int index)
        {
            selectedSkillIndex = index;
            customParamsListWithMenu = null;
            currentCustomParamsProperty = null;

            var listProp = configSerializedObject.FindProperty("unitDataList");
            if (listProp == null || elementIndex >= listProp.arraySize) return;

            var element = listProp.GetArrayElementAtIndex(elementIndex);
            var skillsProperty = element.FindPropertyRelative("skills");

            if (skillsProperty != null && index < skillsProperty.arraySize)
            {
                selectedSkillProperty = skillsProperty.GetArrayElementAtIndex(index);
                var customParamsProperty = selectedSkillProperty.FindPropertyRelative("customParams");
                if (customParamsProperty != null)
                    InitializeCustomParamsList(customParamsProperty);
            }

            Repaint();
        }

        #endregion

        #region GUI 绘制

        private void OnGUI()
        {
            if (targetConfig == null || configSerializedObject == null)
            {
                EditorGUILayout.LabelField("无数据可编辑");
                return;
            }

            var listProperty = configSerializedObject.FindProperty("unitDataList");
            if (elementIndex < 0 || elementIndex >= listProperty.arraySize)
            {
                EditorGUILayout.LabelField("单位数据不存在，可能已被删除");
                return;
            }

            var element = listProperty.GetArrayElementAtIndex(elementIndex);

            EditorGUILayout.LabelField($"编辑: {unitDisplayName}", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();

            // === 左侧：单位属性 ===
            float leftWidth = position.width * splitRatio;
            EditorGUILayout.BeginVertical(GUILayout.Width(leftWidth));
            EditorGUILayout.LabelField("单位属性", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);
            unitScrollPosition = EditorGUILayout.BeginScrollView(unitScrollPosition);
            
            // 绘制所有属性，排除 skills 和 displayName（已移除）
            SerializedProperty property = element.Copy();
            SerializedProperty endProperty = element.GetEndProperty();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren) && !SerializedProperty.EqualContents(property, endProperty))
            {
                enterChildren = false;
                if (property.name == "skills") continue;
                if (property.name == "displayName") continue;
                
                EditorGUILayout.PropertyField(property, true);
            }
            
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // 分割线
            DrawSplitDivider();

            // === 右侧：技能配置 ===
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("技能配置", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();

            // 技能列表
            EditorGUILayout.BeginVertical(GUILayout.Width(position.width * (1 - splitRatio) * 0.45f));
            if (skillsListWithMenu != null)
                skillsListWithMenu.DoLayoutList();
            EditorGUILayout.EndVertical();

            // 技能编辑区
            EditorGUILayout.BeginVertical();
            if (selectedSkillProperty != null && selectedSkillIndex >= 0)
            {
                skillScrollPosition = EditorGUILayout.BeginScrollView(skillScrollPosition);
                
                // 绘制技能属性
                SerializedProperty skillProp = selectedSkillProperty.Copy();
                SerializedProperty skillEnd = selectedSkillProperty.GetEndProperty();
                bool skillEnterChildren = true;
                while (skillProp.NextVisible(skillEnterChildren) && !SerializedProperty.EqualContents(skillProp, skillEnd))
                {
                    skillEnterChildren = false;
                    EditorGUILayout.PropertyField(skillProp, true);
                }
                
                EditorGUILayout.EndScrollView();
            }
            else
            {
                EditorGUILayout.HelpBox("请选择左侧技能进行编辑", MessageType.Info);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            // 处理右键菜单
            if (Event.current.type == UnityEngine.EventType.ContextClick)
            {
                if (skillsListWithMenu?.ContextClickIndex >= 0)
                    skillsListWithMenu.ShowContextMenu();
                else if (customParamsListWithMenu?.ContextClickIndex >= 0)
                    customParamsListWithMenu.ShowContextMenu();
            }

            if (EditorGUI.EndChangeCheck())
            {
                dataChanged = true;
                configSerializedObject.SetIsDifferentCacheDirty();
            }

            DrawBottomButtons();
        }

        private void DrawSplitDivider()
        {
            Rect dividerRect = GUILayoutUtility.GetRect(5f, 5f, GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(new Rect(dividerRect.x + 2, dividerRect.y, 1, dividerRect.height), Color.gray);
            EditorGUIUtility.AddCursorRect(dividerRect, MouseCursor.ResizeHorizontal);

            if (Event.current.type == UnityEngine.EventType.MouseDown && dividerRect.Contains(Event.current.mousePosition))
                isDraggingSplit = true;

            if (isDraggingSplit)
            {
                if (Event.current.type == UnityEngine.EventType.MouseDrag)
                {
                    splitRatio = Mathf.Clamp(Event.current.mousePosition.x / position.width, 0.3f, 0.7f);
                    Repaint();
                }
                if (Event.current.type == UnityEngine.EventType.MouseUp)
                    isDraggingSplit = false;
            }
        }

        private void DrawBottomButtons()
        {
            EditorGUILayout.Space(10);
            if (dataChanged)
                EditorGUILayout.HelpBox("数据已修改，请点击保存", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = dataChanged;
            if (GUILayout.Button("保存", GUILayout.Height(35))) ApplyAndSaveChanges();
            GUI.enabled = true;
            if (GUILayout.Button("取消", GUILayout.Height(35))) Close();
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region 保存

        private void ApplyAndSaveChanges()
        {
            if (configSerializedObject != null)
            {
                configSerializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(targetConfig);
                AssetDatabase.SaveAssets();
                dataChanged = false;
                Debug.Log($"单位数据 '{unitDisplayName}' 已保存");
            }
        }

        private void OnDestroy()
        {
            if (configSerializedObject == null) return;
            if (dataChanged)
            {
                bool shouldSave = EditorUtility.DisplayDialog("未保存的更改",
                    $"是否保存对 '{unitDisplayName}' 的更改？", "保存", "放弃");
                if (shouldSave)
                {
                    configSerializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(targetConfig);
                    AssetDatabase.SaveAssets();
                }
            }
            configSerializedObject.Dispose();
        }

        #endregion
    }
}


