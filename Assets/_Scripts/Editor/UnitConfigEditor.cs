// ============================================
// UnitConfigEditor - 精简后的 Inspector
// ============================================

using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(UnitConfig))]
public class UnitConfigEditor : Editor
{
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
                newEl.FindPropertyRelative("key").stringValue = "";
                newEl.FindPropertyRelative("displayName").stringValue = "";
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
        var displayNameProperty = paramElement.FindPropertyRelative("displayName");
        var valueProperty = paramElement.FindPropertyRelative("value");
        var baseTypeProperty = paramElement.FindPropertyRelative("baseType");

        float lineH = EditorGUIUtility.singleLineHeight;
        float pad = 2f;

        Rect row1 = new Rect(contentRect.x, contentRect.y, contentRect.width, lineH);
        float labelW1 = 25f, labelW2 = 35f;
        float fieldW = (row1.width - labelW1 - labelW2 - pad * 2) / 2;

        EditorGUI.LabelField(new Rect(row1.x, row1.y, labelW1, lineH), "Key");
        keyProperty.stringValue = EditorGUI.TextField(
            new Rect(row1.x + labelW1, row1.y, fieldW, lineH), keyProperty.stringValue);

        EditorGUI.LabelField(new Rect(row1.x + labelW1 + fieldW + pad, row1.y, labelW2, lineH), "显示名");
        displayNameProperty.stringValue = EditorGUI.TextField(
            new Rect(row1.x + labelW1 + fieldW + pad + labelW2, row1.y, fieldW, lineH),
            displayNameProperty.stringValue);

        Rect row2 = new Rect(contentRect.x, contentRect.y + lineH + pad, contentRect.width, lineH);
        float vw1 = 25f, vw2 = 30f, vw3 = 100f;
        float vFieldW = row2.width - vw1 - vw2 - vw3 - pad * 2;

        EditorGUI.LabelField(new Rect(row2.x, row2.y, vw1, lineH), "值");
        valueProperty.intValue = EditorGUI.IntField(
            new Rect(row2.x + vw1, row2.y, vFieldW, lineH), valueProperty.intValue);

        EditorGUI.LabelField(new Rect(row2.x + vw1 + vFieldW + pad, row2.y, vw2, lineH), "类型");
        baseTypeProperty.intValue = EditorGUI.Popup(
            new Rect(row2.x + vw1 + vFieldW + pad + vw2, row2.y, vw3, lineH),
            baseTypeProperty.intValue, baseTypeProperty.enumDisplayNames);
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