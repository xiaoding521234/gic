// ============================================
// ConfigEditorUITK - 配置编辑器 UI Toolkit 公共层
// ============================================
// ListView 通过 bindingPath 绑定 SerializedProperty 数组：
// 底部 +/- 与拖拽排序由绑定系统直接写回序列化数据（可 Undo）。
// 右键菜单与旧 IMGUI 版标签一致（编辑/复制元素/删除元素/Copy Element/Paste Element）；
// 旧版针对"标量值"的 Copy/Paste 项对类元素恒为空操作，不再保留。
//
// 视觉规范（2026-08-15 美化）：行 = [徽标] 名称 ★星级；
// 徽标按文本稳定取色（同名恒色）；星级金色；窗口底部统一页脚工具条（提示 + 主色保存按钮）。

using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using GIC.Framework;

namespace GIC.Editor
{
    /// <summary>
    /// 配置列表（ListView）工厂
    /// </summary>
    public static class ConfigEditorUITK
    {
        private static readonly Color GoldStar = new Color(1f, 0.72f, 0.18f);
        private static readonly Color PrimaryBlue = new Color(0.25f, 0.5f, 0.85f);
        private static readonly Color DangerRed = new Color(0.62f, 0.24f, 0.26f);

        /// <summary>徽标柔和底色盘（白字可读），按文本哈希稳定取色</summary>
        private static readonly Color[] BadgePalette =
        {
            new Color(0.30f, 0.45f, 0.70f), // 蓝
            new Color(0.35f, 0.55f, 0.40f), // 绿
            new Color(0.60f, 0.45f, 0.20f), // 橙棕
            new Color(0.50f, 0.40f, 0.65f), // 紫
            new Color(0.55f, 0.50f, 0.30f), // 橄榄
            new Color(0.45f, 0.50f, 0.55f), // 青灰
        };

        public class ListConfig
        {
            public string HeaderTitle;
            /// <summary>元素属性 → 行主文本（名称）</summary>
            public Func<SerializedProperty, string> NameProvider;
            /// <summary>元素属性 → 行首徽标文本（返回 null/空 则不显示）</summary>
            public Func<SerializedProperty, string> BadgeProvider;
            /// <summary>元素属性 → 星级（≤0 不显示）</summary>
            public Func<SerializedProperty, int> StarsProvider;
            /// <summary>行内"编辑"按钮（为空则隐藏按钮）</summary>
            public Action<int> OnEdit;
            /// <summary>选中行变化（可空）</summary>
            public Action<int> OnSelectionChanged;
            public float RowHeight = 24f;
        }

        /// <summary>
        /// 构建绑定 SerializedProperty 数组的 ListView。调用方需对挂载根执行 Bind(serializedObject)。
        /// </summary>
        public static ListView CreateList(SerializedObject so, string listPath, ListConfig cfg)
        {
            var lv = new ListView
            {
                bindingPath = listPath,
                reorderable = true,
                reorderMode = ListViewReorderMode.Simple,
                showFoldoutHeader = true,
                headerTitle = cfg.HeaderTitle,
                showAddRemoveFooter = true,
                showBorder = true,
                selectionType = SelectionType.Single,
                fixedItemHeight = cfg.RowHeight,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
            };

            lv.makeItem = () =>
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.paddingLeft = 8;
                row.style.paddingRight = 4;

                var badge = new Label { name = "rowBadge" };
                badge.style.unityTextAlign = TextAnchor.MiddleCenter;
                badge.style.color = Color.white;
                badge.style.fontSize = 11;
                badge.style.paddingLeft = 8;
                badge.style.paddingRight = 8;
                badge.style.paddingTop = 1;
                badge.style.paddingBottom = 1;
                badge.style.marginRight = 8;
                badge.style.whiteSpace = WhiteSpace.NoWrap;
                badge.style.display = DisplayStyle.None;
                SetBorderRadius(badge, 9);
                row.Add(badge);

                var label = new Label { name = "rowLabel" };
                label.style.flexGrow = 1f;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                label.style.marginRight = 6;
                row.Add(label);

                var stars = new Label { name = "rowStars" };
                stars.style.color = GoldStar;
                stars.style.marginRight = 8;
                stars.style.whiteSpace = WhiteSpace.NoWrap;
                stars.style.display = DisplayStyle.None;
                row.Add(stars);

                var editBtn = new Button { name = "editBtn", text = "编辑" };
                editBtn.style.width = 55;
                // 无编辑回调的列表（如技能列表，点击行即选中）不显示按钮
                editBtn.style.display = cfg.OnEdit != null ? DisplayStyle.Flex : DisplayStyle.None;
                row.Add(editBtn);

                editBtn.clicked += () =>
                {
                    if (row.userData is int i && cfg.OnEdit != null) cfg.OnEdit(i);
                };

                row.AddManipulator(new ContextualMenuManipulator(evt =>
                    BuildElementMenu(evt, row, so, listPath, cfg)));
                return row;
            };

            lv.bindItem = (element, i) =>
            {
                element.userData = i;
                var prop = so.FindProperty(listPath);
                if (prop == null || i >= prop.arraySize) return;
                var el = prop.GetArrayElementAtIndex(i);

                var badge = element.Q<Label>("rowBadge");
                var badgeText = cfg.BadgeProvider?.Invoke(el);
                if (string.IsNullOrEmpty(badgeText))
                {
                    badge.style.display = DisplayStyle.None;
                }
                else
                {
                    badge.text = badgeText;
                    badge.style.display = DisplayStyle.Flex;
                    badge.style.backgroundColor = PickBadgeColor(badgeText);
                }

                var nameLabel = element.Q<Label>("rowLabel");
                nameLabel.text = cfg.NameProvider != null ? cfg.NameProvider(el) : "";

                var starsLabel = element.Q<Label>("rowStars");
                int stars = cfg.StarsProvider?.Invoke(el) ?? 0;
                if (stars > 0)
                {
                    starsLabel.text = new string('★', stars);
                    starsLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    starsLabel.style.display = DisplayStyle.None;
                }
            };

            if (cfg.OnSelectionChanged != null)
                lv.selectionChanged += _ => cfg.OnSelectionChanged(lv.selectedIndex);

            return lv;
        }

        private static Color PickBadgeColor(string text)
        {
            int hash = 0;
            unchecked
            {
                foreach (char c in text) hash = hash * 31 + c;
            }
            return BadgePalette[(hash & 0x7fffffff) % BadgePalette.Length];
        }

        /// <summary>结构变更（增删/粘贴）后统一走这里：应用 + 刷新列表</summary>
        public static void ApplyAndRefresh(SerializedObject so, ListView lv)
        {
            so.ApplyModifiedProperties();
            so.Update();
            lv.Rebuild();
        }

        private static void BuildElementMenu(ContextualMenuPopulateEvent evt, VisualElement row,
            SerializedObject so, string listPath, ListConfig cfg)
        {
            if (!(row.userData is int index)) return;
            var prop = so.FindProperty(listPath);
            if (prop == null || index < 0 || index >= prop.arraySize) return;

            var element = prop.GetArrayElementAtIndex(index);
            var lv = row.GetFirstAncestorOfType<ListView>();

            if (cfg.OnEdit != null)
            {
                evt.menu.AppendAction("编辑", _ => cfg.OnEdit(index));
                evt.menu.AppendSeparator("/");
            }

            evt.menu.AppendAction("复制元素", _ =>
            {
                so.Update();
                prop.InsertArrayElementAtIndex(index);
                if (lv != null) ApplyAndRefresh(so, lv);
            });

            evt.menu.AppendAction("删除元素", _ =>
            {
                so.Update();
                prop.DeleteArrayElementAtIndex(index);
                if (lv != null) ApplyAndRefresh(so, lv);
            });

            evt.menu.AppendSeparator("/");
            evt.menu.AppendAction("Copy Element", _ =>
            {
                EditorGUIUtility.systemCopyBuffer = EditorJsonUtility.ToJson(element, prettyPrint: false);
            });

            evt.menu.AppendAction("Paste Element", _ =>
            {
                string json = EditorGUIUtility.systemCopyBuffer;
                if (string.IsNullOrEmpty(json)) return;
                try
                {
                    EditorJsonUtility.FromJsonOverwrite(json, element);
                    if (lv != null) ApplyAndRefresh(so, lv);
                }
                catch (Exception e)
                {
                    GICLog.Error($"粘贴元素失败: {e.Message}");
                }
            });
        }

        // ==================== 视觉组件 ====================

        private const string GameFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/zh-cn.ttf";
        private static Font _gameFont;

        /// <summary>
        /// 将编辑器窗口字体切换为游戏主字体（zh-cn SDF 的源 ttf）。
        /// 字体沿 visual tree 继承，根元素设一次即可覆盖全部子控件；
        /// 字体文件缺失时静默保持编辑器默认字体。
        /// </summary>
        public static void ApplyGameFont(VisualElement root)
        {
            if (root == null) return;
            if (_gameFont == null)
                _gameFont = AssetDatabase.LoadAssetAtPath<Font>(GameFontPath);
            if (_gameFont != null)
                root.style.unityFontDefinition = FontDefinition.FromFont(_gameFont);
        }

        /// <summary>Tuanjie 的 IStyle 无 borderRadius/borderWidth 简写，用四边属性设置</summary>
        public static void SetBorderRadius(VisualElement ve, float radius)
        {
            ve.style.borderTopLeftRadius = radius;
            ve.style.borderTopRightRadius = radius;
            ve.style.borderBottomLeftRadius = radius;
            ve.style.borderBottomRightRadius = radius;
        }

        /// <summary>四边统一描边（Tuanjie 无 borderWidth 简写）</summary>
        public static void SetBorder(VisualElement ve, float width, Color color)
        {
            ve.style.borderTopWidth = width;
            ve.style.borderBottomWidth = width;
            ve.style.borderLeftWidth = width;
            ve.style.borderRightWidth = width;
            ve.style.borderTopColor = color;
            ve.style.borderBottomColor = color;
            ve.style.borderLeftColor = color;
            ve.style.borderRightColor = color;
        }

        /// <summary>分区标题：左侧蓝色竖条 + 粗体文字</summary>
        public static VisualElement CreateSectionHeader(string title)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4;

            var bar = new VisualElement();
            bar.style.width = 3;
            bar.style.height = 14;
            SetBorderRadius(bar, 2);
            bar.style.backgroundColor = PrimaryBlue;
            bar.style.marginRight = 6;
            row.Add(bar);

            var label = new Label(title);
            // 中文回退字体无真 Bold 字重，合成粗体会造成"笔画疏的字显瘦"的观感差异，
            // 层级改由竖条 + 亮色 + 字号表达，不加粗
            label.style.color = new Color(0.92f, 0.94f, 0.97f);
            label.style.fontSize = 14;
            row.Add(label);
            return row;
        }

        /// <summary>Inspector 底部"批量填充工具"区块：标题 + 调用方自行追加按钮</summary>
        public static VisualElement CreateToolsSection(string title)
        {
            var section = new VisualElement();
            section.style.marginTop = 16;
            section.Add(CreateSectionHeader(title));
            return section;
        }

        /// <summary>工具按钮（danger=true 红底白字，用于清空类破坏性操作）</summary>
        public static Button CreateToolButton(string text, Action onClick, bool danger = false)
        {
            var btn = new Button(onClick) { text = text };
            btn.style.height = 30;
            btn.style.marginTop = 4;
            if (danger)
            {
                btn.style.backgroundColor = DangerRed;
                btn.style.color = Color.white;
                SetBorderRadius(btn, 4);
            }
            return btn;
        }

        /// <summary>主操作按钮：蓝底白字（工具窗口的第一动作）</summary>
        public static Button CreatePrimaryButton(string text, Action onClick, float height = 34)
        {
            var btn = new Button(onClick) { text = text };
            btn.style.backgroundColor = PrimaryBlue;
            btn.style.color = Color.white;
            btn.style.height = height;
            btn.style.marginTop = 4;
            SetBorderRadius(btn, 4);
            return btn;
        }

        /// <summary>窗口底部页脚工具条：灰色提示（左）+ 主色保存按钮（右）</summary>
        public static VisualElement CreateFooterBar(string hintText, Action onSave)
        {
            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.alignItems = Align.Center;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
            footer.style.marginTop = 8;
            footer.style.paddingTop = 8;

            var hint = new Label(hintText);
            hint.style.color = new Color(0.6f, 0.6f, 0.6f);
            hint.style.fontSize = 11;
            hint.style.flexGrow = 1f;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginRight = 10;
            footer.Add(hint);

            var saveBtn = new Button(onSave) { text = "保存到磁盘" };
            saveBtn.style.backgroundColor = PrimaryBlue;
            saveBtn.style.color = Color.white;
            SetBorderRadius(saveBtn, 4);
            saveBtn.style.height = 30;
            saveBtn.style.width = 120;
            footer.Add(saveBtn);
            return footer;
        }

        /// <summary>窗口标题行：大字号 + 可选金色星级 + 底部细分隔线</summary>
        public static VisualElement CreateTitleRow(string title, int starLevel)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 8;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.4f, 0.4f, 0.4f, 0.4f);
            row.style.paddingBottom = 8;

            var label = new Label(title);
            // 同 CreateSectionHeader：中文伪粗观感差，层级靠字号+亮度拉开
            label.style.fontSize = 17;
            label.style.color = Color.white;
            row.Add(label);

            if (starLevel > 0)
            {
                var stars = new Label(new string('★', starLevel));
                stars.style.color = GoldStar;
                stars.style.marginLeft = 8;
                row.Add(stars);
            }
            return row;
        }

        /// <summary>
        /// 字段分发器：Sprite → 预览行（IMGUI）；Sprite 数组 → 图片列表；其余 → PropertyField。
        /// Tuanjie 的 UITK PropertyField 不调用 CustomPropertyDrawer.CreatePropertyGUI，
        /// UITK ObjectField 点击名称是 Ping 资产、Image 渲染不稳——
        /// Sprite 字段统一走 IMGUI 路径（EditorGUILayout.ObjectField 单击即弹对象选择器，点击即编辑）。
        /// </summary>
        public static VisualElement CreateField(SerializedObject so, SerializedProperty property)
        {
            if (property.isArray && property.arrayElementType == "PPtr<$Sprite>")
                return new SpriteListField(so, property.propertyPath, property.displayName);
            if (property.type == "PPtr<$Sprite>")
                return CreateSpriteField(so, property.propertyPath, property.displayName);
            return new PropertyField(property);
        }

        /// <summary>IMGUI 预览绘制：按 sprite 宽高比适配 rect（不变形），UV 裁切防图集误显整图</summary>
        public static void DrawSpritePreview(Rect rect, Sprite sprite)
        {
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f, 0.6f));
            if (sprite == null || sprite.texture == null)
            {
                GUI.Label(rect, "空", new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter });
                return;
            }

            var tex = sprite.texture;
            var tr = sprite.textureRect;
            // 保持宽高比：在 rect 内取最大居中子区域
            float aspect = tr.width / tr.height;
            var fit = rect;
            if (rect.width / rect.height > aspect)
                fit = new Rect(rect.x + (rect.width - rect.height * aspect) * 0.5f, rect.y, rect.height * aspect, rect.height);
            else
                fit = new Rect(rect.x, rect.y + (rect.height - rect.width / aspect) * 0.5f, rect.width, rect.width / aspect);

            GUI.DrawTextureWithTexCoords(fit, tex, new Rect(
                tr.x / tex.width, tr.y / tex.height,
                tr.width / tex.width, tr.height / tex.height), true);
        }

        /// <summary>单个 Sprite 字段：预览图 + ObjectField 整行 IMGUI 绘制（点击字段弹选择器修改）。
        /// ObjectField 用单行高——传 64 高时其自带预览会在右侧再画一份。</summary>
        public static VisualElement CreateSpriteField(SerializedObject so, string propertyPath, string label)
        {
            var imgui = new IMGUIContainer(() =>
            {
                so.Update();
                var prop = so.FindProperty(propertyPath);
                if (prop == null) return;
                var sprite = prop.objectReferenceValue as Sprite;

                EditorGUILayout.BeginHorizontal();
                var rect = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
                DrawSpritePreview(rect, sprite);

                // 垂直居中的单行字段（避免右侧原生预览重复显示）
                GUILayout.Space(24);
                var fieldRect = GUILayoutUtility.GetRect(0, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                EditorGUI.BeginChangeCheck();
                var newValue = (Sprite)EditorGUI.ObjectField(
                    fieldRect, label, sprite, typeof(Sprite), false);
                if (EditorGUI.EndChangeCheck())
                {
                    prop.objectReferenceValue = newValue;
                    so.ApplyModifiedProperties();
                }
                EditorGUILayout.EndHorizontal();
            });
            imgui.style.marginTop = 2;
            imgui.style.marginBottom = 2;
            return imgui;
        }

        /// <summary>
        /// Sprite 列表字段：逐行预览 + ObjectField（整行 IMGUI）+ 移除，底部添加按钮。
        /// 结构变更后整体重建。
        /// </summary>
        public class SpriteListField : VisualElement
        {
            private readonly SerializedObject so;
            private readonly string propertyPath;
            private readonly VisualElement rows = new VisualElement();
            private readonly Label header;

            public SpriteListField(SerializedObject so, string propertyPath, string displayName)
            {
                this.so = so;
                this.propertyPath = propertyPath;

                header = new Label(displayName);
                header.style.fontSize = 12;
                header.style.color = new Color(0.7f, 0.7f, 0.7f);
                header.style.marginTop = 4;
                Add(header);

                Add(rows);

                var addBtn = new Button(() =>
                {
                    so.Update();
                    var prop = so.FindProperty(propertyPath);
                    if (prop == null) return;
                    prop.arraySize++;
                    so.ApplyModifiedProperties();
                    Rebuild();
                }) { text = "＋ 添加图片" };
                addBtn.style.marginTop = 4;
                Add(addBtn);

                Rebuild();
            }

            private void Rebuild()
            {
                rows.Clear();

                var prop = so.FindProperty(propertyPath);
                if (prop == null) return;

                for (int i = 0; i < prop.arraySize; i++)
                {
                    int index = i;

                    var row = new IMGUIContainer(() =>
                    {
                        so.Update();
                        var p = so.FindProperty(propertyPath);
                        if (p == null || index >= p.arraySize) return;
                        var el = p.GetArrayElementAtIndex(index);
                        var sprite = el.objectReferenceValue as Sprite;

                        EditorGUILayout.BeginHorizontal();
                        var rect = GUILayoutUtility.GetRect(56, 56, GUILayout.Width(56), GUILayout.Height(56));
                        DrawSpritePreview(rect, sprite);

                        // 垂直居中的单行字段（避免右侧原生预览重复显示）
                        GUILayout.Space(21);
                        var fieldRect = GUILayoutUtility.GetRect(0, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
                        EditorGUI.BeginChangeCheck();
                        var newValue = (Sprite)EditorGUI.ObjectField(
                            fieldRect, $"[{index}]", sprite, typeof(Sprite), false);
                        if (EditorGUI.EndChangeCheck())
                        {
                            el.objectReferenceValue = newValue;
                            so.ApplyModifiedProperties();
                        }

                        if (GUILayout.Button("✕", GUILayout.Width(26), GUILayout.Height(56)))
                        {
                            p.DeleteArrayElementAtIndex(index);
                            so.ApplyModifiedProperties();
                            // 结构变更：下一帧重建（IMGUI 事件内改 visual tree 不安全）
                            this.schedule.Execute(Rebuild).StartingIn(0);
                        }
                        EditorGUILayout.EndHorizontal();
                    });
                    row.style.marginTop = 2;
                    row.style.marginBottom = 2;
                    rows.Add(row);
                }

                header.text = prop.displayName + $"（{prop.arraySize}）";
            }
        }
    }

    /// <summary>
    /// 配置类 Inspector 公共基类：列表 + 右键菜单 + 可选工具区
    /// </summary>
    public abstract class ConfigInspectorBase : UnityEditor.Editor
    {
        protected abstract string ListPropertyName { get; }
        protected abstract string ListHeaderTitle { get; }
        protected abstract string GetElementName(SerializedProperty element);
        /// <summary>行首徽标文本（默认无）</summary>
        protected virtual string GetElementBadge(SerializedProperty element) => null;
        /// <summary>行尾星级（默认不显示）</summary>
        protected virtual int GetElementStars(SerializedProperty element) => 0;
        protected abstract void EditElement(int index);

        /// <summary>批量填充等额外工具区（可空）</summary>
        protected virtual VisualElement BuildTools(SerializedObject so) => null;

        public override VisualElement CreateInspectorGUI()
        {
            var so = serializedObject;
            var root = new VisualElement();

            ConfigEditorUITK.ApplyGameFont(root);

            root.Add(ConfigEditorUITK.CreateList(so, ListPropertyName, new ConfigEditorUITK.ListConfig
            {
                HeaderTitle = ListHeaderTitle,
                NameProvider = GetElementName,
                BadgeProvider = GetElementBadge,
                StarsProvider = GetElementStars,
                OnEdit = EditElement,
            }));

            var tools = BuildTools(so);
            if (tools != null) root.Add(tools);

            root.Bind(so);
            return root;
        }
    }

    /// <summary>
    /// 单条配置数据的编辑窗口公共基类（Item/Position 这类"全字段滚动列表"窗口）。
    /// 保存语义：PropertyField 绑定后修改即时写入 SerializedObject（可 Ctrl+Z 撤销），
    /// "保存到磁盘"按钮负责落盘（SetDirty + SaveAssets）；不再有"未保存更改"拦截弹窗。
    /// </summary>
    public abstract class ElementDataEditorWindowBase : EditorWindow
    {
        protected ScriptableObject targetConfig;
        protected int elementIndex = -1;
        protected SerializedObject serializedObj;
        protected string elementDisplayName = "";
        private int titleStarLevel;

        protected abstract string ListPropertyName { get; }
        protected abstract string WindowTitle { get; }
        protected abstract string MissingDataMessage { get; }
        /// <summary>由子类读取元素显示名（枚举 GetInspectorName 等）</summary>
        protected abstract string ReadDisplayName(SerializedProperty element);
        /// <summary>标题行星级（默认不显示）</summary>
        protected virtual int ReadStarLevel(SerializedProperty element) => 0;

        protected void OpenInternal(ScriptableObject config, int index, float width, float height)
        {
            targetConfig = config;
            elementIndex = index;
            serializedObj = new SerializedObject(config);

            var listProp = serializedObj.FindProperty(ListPropertyName);
            if (listProp != null && index < listProp.arraySize)
            {
                var element = listProp.GetArrayElementAtIndex(index);
                elementDisplayName = ReadDisplayName(element);
                titleStarLevel = ReadStarLevel(element);
            }

            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(width, 600);
            var main = EditorGUIUtility.GetMainWindowPosition();
            position = new Rect(
                main.x + (main.width - width) / 2,
                main.y + (main.height - height) / 2,
                width, height);

            // GetWindow 复用已打开的窗口时，CreateGUI 不会再触发，需手动重建
            if (rootVisualElement.childCount > 0)
            {
                rootVisualElement.Clear();
                BuildUI();
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

            ConfigEditorUITK.ApplyGameFont(root);

            if (targetConfig == null || serializedObj == null)
            {
                root.Add(new Label("无数据可编辑"));
                return;
            }

            var listProp = serializedObj.FindProperty(ListPropertyName);
            if (elementIndex < 0 || elementIndex >= listProp.arraySize)
            {
                root.Add(new Label(MissingDataMessage));
                return;
            }

            root.Add(ConfigEditorUITK.CreateTitleRow($"编辑: {elementDisplayName}", titleStarLevel));

            var scroll = new ScrollView();
            BuildElementFields(scroll, listProp.GetArrayElementAtIndex(elementIndex));
            root.Add(scroll);

            root.Add(ConfigEditorUITK.CreateFooterBar(
                "修改即时生效（可 Ctrl+Z 撤销），保存到磁盘后写入文件", SaveToDisk));
        }

        /// <summary>默认绘制元素全部可见字段（Sprite 走预览控件）；子类可重写排除特定字段</summary>
        protected virtual void BuildElementFields(VisualElement container, SerializedProperty element)
        {
            var it = element.Copy();
            var end = element.GetEndProperty();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren) && !SerializedProperty.EqualContents(it, end))
            {
                enterChildren = false;
                container.Add(ConfigEditorUITK.CreateField(serializedObj, it.Copy()));
            }
            container.Bind(serializedObj);
        }

        protected void SaveToDisk()
        {
            if (serializedObj == null || targetConfig == null) return;
            serializedObj.ApplyModifiedProperties();
            EditorUtility.SetDirty(targetConfig);
            AssetDatabase.SaveAssets();
            GICLog.Info($"{WindowTitle}: '{elementDisplayName}' 已保存到磁盘");
        }

        private void OnDestroy()
        {
            serializedObj?.Dispose();
        }
    }

    /// <summary>
    /// Sprite 字段全局 PropertyDrawer（IMGUI 上下文生效，含默认 Inspector）：
    /// 预览图 + ObjectField 并排，单击字段弹对象选择器。
    /// UITK 上下文不经过此 drawer（Tuanjie 未路由 CreatePropertyGUI），
    /// 我们的窗口用 ConfigEditorUITK.CreateField 分发同款 IMGUI 行。
    /// </summary>
    [CustomPropertyDrawer(typeof(Sprite))]
    public class SpritePreviewDrawer : PropertyDrawer
    {
        private const float PreviewSize = 64f;
        private const float Padding = 2f;

        // ========== IMGUI ==========

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var previewRect = new Rect(position.x + Padding, position.y + Padding, PreviewSize, PreviewSize);
            var fieldRect = new Rect(
                position.x + PreviewSize + Padding * 3,
                position.y + (position.height - EditorGUIUtility.singleLineHeight) * 0.5f,
                position.width - PreviewSize - Padding * 3,
                EditorGUIUtility.singleLineHeight);

            var sprite = (Sprite)property.objectReferenceValue;
            if (sprite != null && sprite.texture != null)
            {
                // 按 sprite 在图集/纹理中的 UV 裁剪绘制，避免整图误显示
                var tr = sprite.textureRect;
                var tex = sprite.texture;
                GUI.DrawTextureWithTexCoords(previewRect, tex, new Rect(
                    tr.x / tex.width, tr.y / tex.height,
                    tr.width / tex.width, tr.height / tex.height), true);
            }
            EditorGUI.DrawRect(previewRect, new Color(0, 0, 0, 0.25f));

            var newValue = (Sprite)EditorGUI.ObjectField(
                fieldRect, property.displayName, sprite, typeof(Sprite), false);
            if (newValue != sprite)
            {
                property.objectReferenceValue = newValue;
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return PreviewSize + Padding * 2;
        }
    }
}
