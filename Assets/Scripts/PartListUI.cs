using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;

namespace Zhouxiangyang
{
    public class PartListUI : MonoBehaviour
    {
        public UIDocument uiDocument;
        public OcxSystemManager systemManager;
        public VisualAnalyticsInteractor interactor;

        private ScrollView partList;
        private TextField searchField;
        private string searchQuery = "";
        private readonly Dictionary<Transform, (Button expandBtn, VisualElement childrenContainer)> nodeUi = new Dictionary<Transform, (Button, VisualElement)>();
        private readonly Dictionary<GameObject, VisualElement> rowByObject = new Dictionary<GameObject, VisualElement>();
        private VisualElement lastHighlightedRow;
        private bool isSyncRebuilding;
        private IVisualElementScheduledItem pendingScrollItem;
        private VisualElement pendingScrollRow;
        private int pendingScrollRemaining;
        private int pendingScrollVisibleStreak;

        void Start()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (systemManager == null) systemManager = FindAnyObjectByType<OcxSystemManager>();
            if (interactor == null) interactor = FindAnyObjectByType<VisualAnalyticsInteractor>();

            var root = uiDocument.rootVisualElement;
            partList = root.Q<ScrollView>("PartList");
            SetupSearchUI();

            if (systemManager != null)
            {
                systemManager.ModelsChanged += Rebuild;
            }
            if (interactor != null)
            {
                interactor.SelectionChanged += OnSelectionChanged;
            }

            Rebuild();
        }

        void OnDestroy()
        {
            if (systemManager != null) systemManager.ModelsChanged -= Rebuild;
            if (interactor != null) interactor.SelectionChanged -= OnSelectionChanged;
        }

        private void Rebuild()
        {
            if (partList == null || systemManager == null) return;

            partList.Clear();
            nodeUi.Clear();
            rowByObject.Clear();
            ClearHighlight();

            var rootContainer = systemManager.RootContainer;
            if (rootContainer == null) return;

            foreach (Transform fileGroup in rootContainer)
            {
                var fileNode = CreateNode(fileGroup.name, fileGroup, 0, searchQuery);
                if (fileNode != null) partList.Add(fileNode);
            }

            SyncListToSelection();
        }

        private void OnSelectionChanged()
        {
            SyncListToSelection();
        }

        private void SyncListToSelection()
        {
            if (interactor == null) return;
            var selected = interactor.GetSelectedObjects();
            if (selected == null || selected.Count != 1) { ClearHighlight(); return; }
            var go = selected[0];
            if (go == null) { ClearHighlight(); return; }
            SyncSelectionToList(go);
        }

        private void ClearHighlight()
        {
            if (lastHighlightedRow != null)
            {
                lastHighlightedRow.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
                lastHighlightedRow = null;
            }
        }

        private void SyncSelectionToList(GameObject selectedGo)
        {
            if (selectedGo == null || partList == null || systemManager == null) return;
            if (!rowByObject.TryGetValue(selectedGo, out var row))
            {
                if (!isSyncRebuilding && !string.IsNullOrWhiteSpace(searchQuery))
                {
                    isSyncRebuilding = true;
                    searchQuery = "";
                    if (searchField != null) searchField.SetValueWithoutNotify("");
                    Rebuild();
                    isSyncRebuilding = false;
                }
                return;
            }

            CollapseAllNodes();

            var rootContainer = systemManager.RootContainer;
            var t = selectedGo.transform.parent;
            while (t != null && rootContainer != null && t != rootContainer)
            {
                if (nodeUi.TryGetValue(t, out var ui))
                {
                    ui.childrenContainer.style.display = DisplayStyle.Flex;
                    if (ui.expandBtn != null && ui.expandBtn.enabledSelf) ui.expandBtn.text = "▾";
                }
                t = t.parent;
            }

            ClearHighlight();
            row.style.backgroundColor = new StyleColor(new Color(0.35f, 0.65f, 1.00f, 0.18f));
            lastHighlightedRow = row;

            EnsureRowVisible(row);
        }

        private void CollapseAllNodes()
        {
            foreach (var kv in nodeUi)
            {
                var expandBtn = kv.Value.expandBtn;
                var children = kv.Value.childrenContainer;
                if (children != null) children.style.display = DisplayStyle.None;
                if (expandBtn != null)
                {
                    if (expandBtn.enabledSelf) expandBtn.text = "▸";
                    else expandBtn.text = "";
                }
            }
        }

        private void EnsureRowVisible(VisualElement row)
        {
            if (partList == null || row == null) return;
            pendingScrollRow = row;
            pendingScrollRemaining = 60;
            pendingScrollVisibleStreak = 0;
            pendingScrollItem?.Pause();
            pendingScrollItem = partList.schedule.Execute(EnsureRowVisibleTick).Every(16);
        }

        private void EnsureRowVisibleTick()
        {
            if (partList == null || pendingScrollRow == null)
            {
                pendingScrollItem?.Pause();
                return;
            }

            if (pendingScrollRemaining-- <= 0)
            {
                pendingScrollItem?.Pause();
                return;
            }

            var viewport = partList.contentViewport;
            var content = partList.contentContainer;
            if (viewport == null || viewport.layout.height <= 0.1f)
            {
                return;
            }
            if (content == null || content.layout.height <= 0.1f) return;

            float max = Mathf.Max(0f, content.layout.height - viewport.layout.height);
            if (partList.verticalScroller != null)
            {
                partList.verticalScroller.lowValue = 0f;
                partList.verticalScroller.highValue = max;
            }

            float scrollY = partList.verticalScroller != null ? partList.verticalScroller.value : partList.scrollOffset.y;
            float viewH = viewport.layout.height;
            float padding = 10f;

            Vector2 rowMin = content.WorldToLocal(new Vector2(pendingScrollRow.worldBound.xMin, pendingScrollRow.worldBound.yMin));
            Vector2 rowMax = content.WorldToLocal(new Vector2(pendingScrollRow.worldBound.xMax, pendingScrollRow.worldBound.yMax));

            float desired = scrollY;
            if (rowMin.y < scrollY + padding) desired = rowMin.y - padding;
            else if (rowMax.y > scrollY + viewH - padding) desired = rowMax.y - viewH + padding;

            desired = Mathf.Clamp(desired, 0f, max);
            if (partList.verticalScroller != null) partList.verticalScroller.value = desired;
            partList.scrollOffset = new Vector2(partList.scrollOffset.x, desired);

            bool visible = rowMin.y >= desired && rowMax.y <= desired + viewH;
            if (visible)
            {
                pendingScrollVisibleStreak++;
                if (pendingScrollVisibleStreak >= 2) pendingScrollItem?.Pause();
            }
            else
            {
                pendingScrollVisibleStreak = 0;
            }
        }

        private void SetupSearchUI()
        {
            if (uiDocument == null) return;
            var root = uiDocument.rootVisualElement;
            if (root == null) return;
            if (partList == null) partList = root.Q<ScrollView>("PartList");
            if (partList == null) return;

            if (searchField != null && searchField.parent != null) return;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 6;
            row.style.paddingRight = 6;
            row.style.paddingTop = 6;
            row.style.paddingBottom = 6;

            searchField = new TextField();
            searchField.style.flexGrow = 1;
            searchField.style.height = 26;
            searchField.style.fontSize = 14;
            searchField.style.color = Color.black;
            searchField.style.backgroundColor = Color.white;
            searchField.style.borderBottomWidth = 1;
            searchField.style.borderTopWidth = 1;
            searchField.style.borderLeftWidth = 1;
            searchField.style.borderRightWidth = 1;
            searchField.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            searchField.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            searchField.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            searchField.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            searchField.tooltip = "点击后输入关键词（支持名称/类型/标识）。回车或点“定位”选中第一个匹配。";
            searchField.focusable = false;
            searchField.value = searchQuery ?? "";
            searchField.RegisterCallback<GeometryChangedEvent>(_ => ApplySearchFieldInputStyle());
            searchField.RegisterCallback<PointerDownEvent>(evt =>
            {
                searchField.focusable = true;
                searchField.Focus();
                evt.StopImmediatePropagation();
            });
            searchField.RegisterCallback<FocusOutEvent>(_ =>
            {
                searchField.focusable = false;
            });
            searchField.RegisterValueChangedCallback(evt =>
            {
                searchQuery = evt.newValue ?? "";
                Rebuild();
            });
            searchField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                FocusFirstMatch(searchQuery);
                evt.StopPropagation();
            });

            var btnLocate = new Button(() => FocusFirstMatch(searchQuery)) { text = "定位" };
            btnLocate.style.height = 24;
            btnLocate.style.minWidth = 56;
            btnLocate.style.color = Color.white;

            var btnClear = new Button(() =>
            {
                searchQuery = "";
                if (searchField != null) searchField.value = "";
                Rebuild();
            })
            { text = "清空" };
            btnClear.style.height = 24;
            btnClear.style.minWidth = 56;
            btnClear.style.color = Color.white;

            row.Add(searchField);
            row.Add(btnLocate);
            row.Add(btnClear);

            var parent = partList.parent;
            if (parent != null)
            {
                int idx = parent.IndexOf(partList);
                if (idx < 0) idx = 0;
                parent.Insert(idx, row);
            }
            else
            {
                root.Insert(0, row);
            }

            ApplySearchFieldInputStyle();
        }

        private void ApplySearchFieldInputStyle()
        {
            if (searchField == null) return;
            var input = searchField.Q<VisualElement>("unity-text-input") ?? searchField.Q<VisualElement>(className: "unity-text-input");
            if (input != null)
            {
                input.style.color = Color.black;
                input.style.unityTextAlign = TextAnchor.MiddleLeft;
                input.style.fontSize = 14;
                input.style.backgroundColor = Color.white;
                input.style.paddingLeft = 6;
                input.style.paddingRight = 6;
                input.style.paddingTop = 3;
                input.style.paddingBottom = 3;
            }

            var innerText = searchField.Q<TextElement>("unity-text-element") ?? searchField.Q<TextElement>(className: "unity-text-element");
            if (innerText != null)
            {
                innerText.style.color = Color.black;
                innerText.style.fontSize = 14;
                innerText.style.unityTextAlign = TextAnchor.MiddleLeft;
            }
        }

        private void FocusFirstMatch(string query)
        {
            if (interactor == null || systemManager == null) return;
            if (string.IsNullOrWhiteSpace(query)) return;

            var rootContainer = systemManager.RootContainer;
            if (rootContainer == null) return;

            string q = query.Trim();
            var all = rootContainer.GetComponentsInChildren<PartData>(true);
            if (all == null || all.Length == 0) return;

            PartData best = all.FirstOrDefault(p => p != null && string.Equals(p.PartType ?? "", "Plate", System.StringComparison.OrdinalIgnoreCase) && PartDataMatches(p, q));
            if (best == null) best = all.FirstOrDefault(p => p != null && PartDataMatches(p, q));
            if (best == null) return;

            interactor.SelectObjectsExternal(new[] { best.gameObject }, false);
        }

        private VisualElement CreateNode(string label, Transform target, int indent, string filter)
        {
            if (!string.IsNullOrWhiteSpace(filter) && !TransformHasMatch(target, label, filter)) return null;

            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = indent * 12;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;

            bool hasAnyChildren = HasAnyChildren(target);
            var expandBtn = new Button();
            expandBtn.text = hasAnyChildren ? "▸" : "";
            expandBtn.style.width = 22;
            expandBtn.style.height = 20;
            expandBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            expandBtn.SetEnabled(hasAnyChildren);

            var selectBtn = new Button();
            selectBtn.text = label;
            selectBtn.style.flexGrow = 1;
            selectBtn.style.height = 20;
            selectBtn.style.unityTextAlign = TextAnchor.MiddleLeft;
            selectBtn.style.color = Color.white;
            expandBtn.style.color = Color.white;

            row.Add(expandBtn);
            row.Add(selectBtn);
            container.Add(row);

            var childrenContainer = new VisualElement();
            childrenContainer.style.display = DisplayStyle.None;
            childrenContainer.style.flexDirection = FlexDirection.Column;
            container.Add(childrenContainer);
            if (target != null) nodeUi[target] = (expandBtn, childrenContainer);
            if (target != null) rowByObject[target.gameObject] = row;

            if (hasAnyChildren)
            {
                expandBtn.clicked += () =>
                {
                    bool open = childrenContainer.style.display == DisplayStyle.None;
                    childrenContainer.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                    expandBtn.text = open ? "▾" : "▸";
                };
            }

            selectBtn.clicked += () =>
            {
                if (interactor == null) return;
                if (target.GetComponent<PartData>() != null)
                {
                    interactor.SelectObjectsExternal(new[] { target.gameObject }, false);
                }
                else
                {
                    var objs = CollectSelectableObjects(target);
                    interactor.SelectObjectsExternal(objs, false);
                }
            };

            foreach (Transform child in target)
            {
                if (child.GetComponent<PartData>() != null)
                {
                    var pd = child.GetComponent<PartData>();
                    string baseLabel = (!string.IsNullOrEmpty(pd?.SchemaLevel) && string.Equals(pd.SchemaLevel, "S2", System.StringComparison.OrdinalIgnoreCase) && string.Equals(pd.PartType ?? "", "Plate", System.StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(pd.GeometryName))
                        ? pd.GeometryName
                        : (!string.IsNullOrEmpty(pd?.PartId) ? pd.PartId : child.name);

                    if (pd != null && string.Equals(pd.PartType ?? "", "Plate", System.StringComparison.OrdinalIgnoreCase) && pd.OpeningNames != null && pd.OpeningNames.Count > 0)
                    {
                        string extra = $" (开孔:{pd.OpeningNames.Count})";
                        var distinct = pd.OpeningNames.Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
                        if (distinct.Count > 0)
                        {
                            string tail = string.Join(",", distinct.Take(3));
                            if (distinct.Count > 3) tail += "...";
                            extra += $" {tail}";
                        }
                        baseLabel += extra;
                    }

                    string leafLabel = baseLabel;
                    bool hasPdChildren = child.Cast<Transform>().Any(ch => ch.GetComponent<PartData>() != null);
                    if (hasPdChildren)
                    {
                        var node = CreateNode(leafLabel, child, indent + 1, filter);
                        if (node != null) childrenContainer.Add(node);
                    }
                    else
                    {
                        var leaf = CreateLeaf(leafLabel, child.gameObject, indent + 1, filter);
                        if (leaf != null) childrenContainer.Add(leaf);
                    }
                }
                else
                {
                    bool groupHasAny = child.GetComponentsInChildren<PartData>(true).Length > 0;
                    if (!groupHasAny) continue;
                    var node = CreateNode(child.name, child, indent + 1, filter);
                    if (node != null) childrenContainer.Add(node);
                }
            }

            if (!string.IsNullOrWhiteSpace(filter) && childrenContainer.childCount == 0 && !SelfMatches(target, label, filter)) return null;

            return container;
        }

        private VisualElement CreateLeaf(string label, GameObject target, int indent, string filter)
        {
            if (!string.IsNullOrWhiteSpace(filter))
            {
                var pd = target != null ? target.GetComponent<PartData>() : null;
                bool ok = false;
                if (pd != null) ok = PartDataMatches(pd, filter);
                if (!ok && !string.IsNullOrEmpty(label) && label.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) ok = true;
                if (!ok) return null;
            }

            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Column;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = indent * 12 + 22;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;

            var expandBtn = new Button();
            expandBtn.text = "▸";
            expandBtn.style.width = 22;
            expandBtn.style.height = 20;
            expandBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
            expandBtn.style.color = Color.white;

            var btn = new Button();
            btn.text = label;
            btn.style.flexGrow = 1;
            btn.style.height = 20;
            btn.style.unityTextAlign = TextAnchor.MiddleLeft;
            btn.style.color = Color.white;
            btn.clicked += () =>
            {
                if (interactor == null) return;
                interactor.SelectObjectsExternal(new[] { target }, false);
            };

            row.Add(expandBtn);
            row.Add(btn);
            container.Add(row);
            if (target != null) rowByObject[target] = row;

            var actions = new VisualElement();
            actions.style.display = DisplayStyle.None;
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.paddingLeft = indent * 12 + 44;
            actions.style.paddingTop = 2;
            actions.style.paddingBottom = 6;

            var btnSelect = new Button { text = "选择" };
            var btnScaleUp = new Button { text = "放大一点" };
            var btnScaleDown = new Button { text = "缩小一点" };

            btnSelect.style.height = 22;
            btnScaleUp.style.height = 22;
            btnScaleDown.style.height = 22;
            btnSelect.style.color = Color.white;
            btnScaleUp.style.color = Color.white;
            btnScaleDown.style.color = Color.white;

            btnSelect.clicked += () =>
            {
                if (interactor == null) return;
                interactor.SelectObjectsExternal(new[] { target }, false);
            };
            btnScaleUp.clicked += () =>
            {
                if (interactor == null) return;
                interactor.SelectObjectsExternal(new[] { target }, false);
                interactor.NudgeScaleSelected(1.1f);
            };
            btnScaleDown.clicked += () =>
            {
                if (interactor == null) return;
                interactor.SelectObjectsExternal(new[] { target }, false);
                interactor.NudgeScaleSelected(0.9f);
            };

            actions.Add(btnSelect);
            actions.Add(btnScaleUp);
            actions.Add(btnScaleDown);
            container.Add(actions);

            expandBtn.clicked += () =>
            {
                bool open = actions.style.display == DisplayStyle.None;
                actions.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                expandBtn.text = open ? "▾" : "▸";
            };

            return container;
        }

        private bool TransformHasMatch(Transform t, string label, string filter)
        {
            if (SelfMatches(t, label, filter)) return true;
            foreach (Transform child in t)
            {
                string childLabel = child.name;
                var pd = child.GetComponent<PartData>();
                if (pd != null)
                {
                    childLabel = (!string.IsNullOrEmpty(pd?.SchemaLevel) && string.Equals(pd.SchemaLevel, "S2", System.StringComparison.OrdinalIgnoreCase) && string.Equals(pd.PartType ?? "", "Plate", System.StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(pd.GeometryName))
                        ? pd.GeometryName
                        : (!string.IsNullOrEmpty(pd?.PartId) ? pd.PartId : child.name);
                    if (PartDataMatches(pd, filter)) return true;
                }
                if (childLabel.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (TransformHasMatch(child, childLabel, filter)) return true;
            }
            return false;
        }

        private bool SelfMatches(Transform t, string label, string filter)
        {
            if (t == null) return false;
            if (!string.IsNullOrEmpty(label) && label.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            var pd = t.GetComponent<PartData>();
            if (pd != null && PartDataMatches(pd, filter)) return true;
            return false;
        }

        private bool PartDataMatches(PartData pd, string filter)
        {
            if (pd == null || string.IsNullOrEmpty(filter)) return false;
            if (!string.IsNullOrEmpty(pd.GeometryName) && pd.GeometryName.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(pd.PartId) && pd.PartId.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(pd.PartType) && pd.PartType.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(pd.GuidRef) && pd.GuidRef.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(pd.MaterialRef) && pd.MaterialRef.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(pd.Thickness) && pd.Thickness.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(pd.SectionRef) && pd.SectionRef.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(pd.EndCutCode) && pd.EndCutCode.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(pd.SourceFilePath) && pd.SourceFilePath.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(pd.SourceElementType) && pd.SourceElementType.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (pd.OpeningNames != null && pd.OpeningNames.Any(n => !string.IsNullOrEmpty(n) && n.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            if (pd.OpeningTypes != null && pd.OpeningTypes.Any(n => !string.IsNullOrEmpty(n) && n.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            if (pd.OpeningParams != null && pd.OpeningParams.Count > 0)
            {
                foreach (var kv in pd.OpeningParams)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Key.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (!string.IsNullOrEmpty(kv.Value) && kv.Value.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            if (pd.EndCutParams != null && pd.EndCutParams.Count > 0)
            {
                foreach (var kv in pd.EndCutParams)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && kv.Key.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (!string.IsNullOrEmpty(kv.Value) && kv.Value.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            return false;
        }

        private bool HasAnyChildren(Transform t)
        {
            if (t.GetComponent<PartData>() != null)
            {
                foreach (Transform child in t)
                {
                    if (child.GetComponent<PartData>() != null) return true;
                }
                return false;
            }
            foreach (Transform child in t)
            {
                if (child.GetComponent<PartData>() != null) return true;
                if (child.GetComponentsInChildren<PartData>(true).Length > 0) return true;
            }
            return false;
        }

        private List<GameObject> CollectSelectableObjects(Transform t)
        {
            return t.GetComponentsInChildren<PartData>(true).Select(p => p.gameObject).ToList();
        }
    }
}
