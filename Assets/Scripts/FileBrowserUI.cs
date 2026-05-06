using UnityEngine;
using UnityEngine.UIElements;
using System.IO;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System;
using System.Collections;

namespace Zhouxiangyang
{
    public class FileBrowserUI : MonoBehaviour
    {
        [Header("UI & 系统设置")]
        public UIDocument uiDocument;
        public OcxSystemManager systemManager;
        public MeasurementTool measurementTool;
        public VisualAnalyticsInteractor interactor;

        [Header("工具栏图标 Key (StreamingAssets/UIIcons/<key>.png)")]
        public string iconAxisXKey = "axis_x";
        public string iconAxisYKey = "axis_y";
        public string iconAxisZKey = "axis_z";
        public string iconAxisFreeKey = "axis_free";
        public string iconSectionKey = "section";

        private Button btnImportFile;
        private Button btnClearAll;
        private Button btnMeasureDistance;
        private Button btnMeasureAngle;
        private Button btnMeasureArc;
        private Button btnClearMeasure;
        private Button btnTransformMove;
        private Button btnTransformRotate;
        private Button btnTransformScale;
        private Button btnTransformExit;
        private Button btnOpenDetail;
        private Button btnCloseDetail;
        private VisualElement detailOverlay;
        private ScrollView detailScroll;
        private VisualElement panelContainer;
        private Button btnScaleUp;
        private Button btnScaleDown;
        private Button btnAxisX;
        private Button btnAxisY;
        private Button btnAxisZ;
        private Button btnAxisFree;
        private Button btnExport3docx;
        private VisualElement exitOverlay;
        private VisualElement importChoiceOverlay;
        private VisualElement exportChoiceOverlay;
        private VisualElement importOverlay;
        private Label importLabel;
        private VisualElement importBarFill;
        private int suppressMeasureClickUntilFrame;

        private Button activeMeasureBtn;
        private Button activeTransformBtn;
        private Button activeAxisBtn;
        private Button activeSectionBtn;
        private Button activeOrbitBtn;
        private bool sectionModeEnabled = false;
        private bool sectionBoxVisible = true;
        private Button sectionBoxToggleBtn;
        private Button tbOrbitViewButton;
        private VisualElement tabsContainer;
        private Button btnNewProject;
        private Button btnOpenProject;
        private Button btnFullscreen;
        private Button btnSaveExit;
        private readonly Dictionary<string, Button> projectTabButtons = new Dictionary<string, Button>();

        void Start()
        {
            var root = uiDocument.rootVisualElement;
            root.style.color = Color.white;
            if (measurementTool == null) measurementTool = FindAnyObjectByType<MeasurementTool>();
            if (measurementTool == null) measurementTool = new GameObject("MeasurementTool").AddComponent<MeasurementTool>();
            if (interactor == null) interactor = FindAnyObjectByType<VisualAnalyticsInteractor>();
            if (interactor != null) interactor.measurementTool = measurementTool;
            if (measurementTool != null && interactor != null) measurementTool.OnInfo = interactor.SetInfoText;
            if (interactor != null && interactor.GetComponent<SelectionHighlighter>() == null) interactor.gameObject.AddComponent<SelectionHighlighter>();

            var env = FindAnyObjectByType<CadOceanEnvironment>();
            if (env == null)
            {
                var goEnv = new GameObject("CadOceanEnvironment");
                env = goEnv.AddComponent<CadOceanEnvironment>();
            }
            env.Apply();

            var partListUi = FindAnyObjectByType<PartListUI>();
            if (partListUi == null)
            {
                var go = new GameObject("PartListUI");
                partListUi = go.AddComponent<PartListUI>();
                partListUi.uiDocument = uiDocument;
                partListUi.systemManager = systemManager;
                partListUi.interactor = interactor;
            }

            var axisWidget = FindAnyObjectByType<AxisWidgetUI>();
            if (axisWidget == null)
            {
                var go = new GameObject("AxisWidgetUI");
                axisWidget = go.AddComponent<AxisWidgetUI>();
                axisWidget.uiDocument = uiDocument;
            }

            btnCloseDetail = root.Q<Button>("BtnCloseDetail");
            detailOverlay = root.Q<VisualElement>("PartDetailOverlay");
            detailScroll = root.Q<ScrollView>("DetailScroll");
            panelContainer = root.Q<VisualElement>("PanelContainer");
            tabsContainer = root.Q<VisualElement>("TabsContainer");
            btnNewProject = root.Q<Button>("BtnNewProject");
            btnOpenProject = root.Q<Button>("BtnOpenProject");
            btnFullscreen = root.Q<Button>("BtnFullscreen");
            btnSaveExit = root.Q<Button>("BtnSaveExit");
            var tbUi = root.Q<Button>("TBUI");
            var tbImport = root.Q<Button>("TBImport");
            var tbExport = root.Q<Button>("TBExport");
            var tbMeasureDistance = root.Q<Button>("TBMeasureDistance");
            var tbMeasureAngle = root.Q<Button>("TBMeasureAngle");
            var tbMeasureArc = root.Q<Button>("TBMeasureArc");
            var tbMove = root.Q<Button>("TBMove");
            var tbRotate = root.Q<Button>("TBRotate");
            var tbScale = root.Q<Button>("TBScale");
            var tbScaleUp = root.Q<Button>("TBScaleUp");
            var tbScaleDown = root.Q<Button>("TBScaleDown");
            var tbAxisFree = root.Q<Button>("TBAxisFree");
            var tbAxisX = root.Q<Button>("TBAxisX");
            var tbAxisY = root.Q<Button>("TBAxisY");
            var tbAxisZ = root.Q<Button>("TBAxisZ");
            var tbOrbitView = root.Q<Button>("TBOrbitView");
            var tbSection = root.Q<Button>("TBSection");
            var tbDetail = root.Q<Button>("TBDetail");
            var tbSectionBox = root.Q<Button>("TBSectionBox");
            var tbClear = root.Q<Button>("TBClear");

            ApplyToolbarIcons(new Dictionary<Button, string>
            {
                { tbUi, "ui_toggle" },
                { tbImport, "import" },
                { tbExport, "export" },
                { tbMeasureDistance, "measure_distance" },
                { tbMeasureAngle, "measure_angle" },
                { tbMeasureArc, "measure_arc" },
                { tbMove, "transform_move" },
                { tbRotate, "transform_rotate" },
                { tbScale, "transform_scale" },
                { tbScaleUp, "scale_up" },
                { tbScaleDown, "scale_down" },
                { tbAxisFree, iconAxisFreeKey },
                { tbAxisX, iconAxisXKey },
                { tbAxisY, iconAxisYKey },
                { tbAxisZ, iconAxisZKey },
                { tbOrbitView, "orbit_view" },
                { tbSection, iconSectionKey },
                { tbSectionBox, iconSectionKey },
                { tbDetail, "detail" },
                { tbClear, "clear" },
            });

            if (tbImport != null) tbImport.clicked += ShowImportChoice;
            if (tbExport != null) tbExport.clicked += ShowExportChoice;
            if (tbClear != null) tbClear.clicked += () => systemManager.ClearAllModels();
            if (tbUi != null)
            {
                tbUi.clicked += () =>
                {
                    if (panelContainer == null) return;
                    bool visible = panelContainer.style.display != DisplayStyle.None;
                    panelContainer.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
                };
            }

            if (measurementTool != null)
            {
                if (tbMeasureDistance != null) tbMeasureDistance.clicked += () => ToggleMeasure(tbMeasureDistance, () => measurementTool.SetModeDistance());
                if (tbMeasureAngle != null) tbMeasureAngle.clicked += () => ToggleMeasure(tbMeasureAngle, () => measurementTool.SetModeAngle());
                if (tbMeasureArc != null) tbMeasureArc.clicked += () => ToggleMeasure(tbMeasureArc, () => measurementTool.SetModeArc());
            }

            if (interactor != null)
            {
                if (tbMove != null) tbMove.clicked += () => ToggleTransform(tbMove, () => interactor.SetTransformTranslate());
                if (tbRotate != null) tbRotate.clicked += () => ToggleTransform(tbRotate, () => interactor.SetTransformRotate());
                if (tbScale != null) tbScale.clicked += () => ToggleTransform(tbScale, () => interactor.SetTransformScale());
                if (tbAxisFree != null) tbAxisFree.clicked += () => { interactor.SetAxisFree(); SetActiveAxis(tbAxisFree); };
                if (tbAxisX != null) tbAxisX.clicked += () => { interactor.SetAxisX(); SetActiveAxis(tbAxisX); };
                if (tbAxisY != null) tbAxisY.clicked += () => { interactor.SetAxisY(); SetActiveAxis(tbAxisY); };
                if (tbAxisZ != null) tbAxisZ.clicked += () => { interactor.SetAxisZ(); SetActiveAxis(tbAxisZ); };
                if (tbDetail != null) tbDetail.clicked += () => { if (detailOverlay != null) { detailOverlay.style.display = DisplayStyle.Flex; RefreshDetailOverlay(); } };
                if (tbScaleUp != null) tbScaleUp.clicked += () => interactor.NudgeScaleSelected(1.1f);
                if (tbScaleDown != null) tbScaleDown.clicked += () => interactor.NudgeScaleSelected(0.9f);
            if (tbAxisFree != null) SetActiveAxis(tbAxisFree);
        }

        tbOrbitViewButton = tbOrbitView;
        if (tbOrbitViewButton != null)
        {
            activeOrbitBtn = tbOrbitViewButton;
            tbOrbitViewButton.clicked -= OnOrbitViewClicked;
            tbOrbitViewButton.clicked += OnOrbitViewClicked;
            var camCtrlInit = Camera.main?.GetComponent<CameraController>();
            SetToggleButtonVisual(tbOrbitViewButton, camCtrlInit != null && camCtrlInit.isOrbitMode, new Color(0.25f, 0.85f, 0.25f));
        }

        if (tbSection != null)
        {
                tbSection.clicked += () =>
                {
                    sectionModeEnabled = !sectionModeEnabled;
                    SetActiveSection(sectionModeEnabled ? tbSection : null);
                    if (systemManager != null)
                    {
                        if (sectionModeEnabled) systemManager.EnterSectionBoxView();
                        else systemManager.ExitSectionView();
                    }
                    if (sectionModeEnabled)
                    {
                        sectionBoxVisible = true;
                        if (systemManager != null) systemManager.SetSectionGizmoVisible(true);
                        if (sectionBoxToggleBtn != null)
                            SetToggleButtonVisual(sectionBoxToggleBtn, sectionBoxVisible, new Color(0.25f, 0.85f, 0.25f));
                    }
                };
            }
            if (tbSectionBox != null)
            {
                sectionBoxToggleBtn = tbSectionBox;
                SetToggleButtonVisual(sectionBoxToggleBtn, sectionBoxVisible, new Color(0.25f, 0.85f, 0.25f));
                sectionBoxToggleBtn.clicked += () =>
                {
                    sectionBoxVisible = !sectionBoxVisible;
                    SetToggleButtonVisual(sectionBoxToggleBtn, sectionBoxVisible, new Color(0.25f, 0.85f, 0.25f));
                    if (systemManager != null) systemManager.SetSectionGizmoVisible(sectionBoxVisible);
                };
            }

            if (btnNewProject != null)
            {
                btnNewProject.clicked += () =>
                {
                    if (systemManager != null) systemManager.CreateNewProject();
                    RebuildProjectTabs();
                };
            }
            if (btnOpenProject != null)
            {
                btnOpenProject.clicked += ShowImportChoice;
            }
            if (btnFullscreen != null)
            {
                btnFullscreen.clicked += () => { Screen.fullScreen = !Screen.fullScreen; };
            }
            if (btnSaveExit != null)
            {
                btnSaveExit.clicked += ShowExitConfirm;
            }
            if (systemManager != null)
            {
                systemManager.ProjectsChanged -= RebuildProjectTabs;
                systemManager.ProjectsChanged += RebuildProjectTabs;
            }
            RebuildProjectTabs();

            if (btnCloseDetail != null)
            {
                btnCloseDetail.clicked += () =>
                {
                    if (detailOverlay != null) detailOverlay.style.display = DisplayStyle.None;
                };
            }

            SetDefaultInfoText();
        }

        private void RebuildProjectTabs()
        {
            if (tabsContainer == null || systemManager == null) return;
            tabsContainer.Clear();
            projectTabButtons.Clear();

            var projects = systemManager.GetProjects();
            string activeId = systemManager.ActiveProjectId;
            for (int i = 0; i < projects.Count; i++)
            {
                var p = projects[i];
                string pid = p.Id;
                var tab = new VisualElement();
                tab.style.flexDirection = FlexDirection.Row;
                tab.style.alignItems = Align.Center;
                tab.style.height = 24;
                tab.style.minWidth = 110;
                tab.style.marginRight = 6;
                tab.style.borderTopLeftRadius = 8;
                tab.style.borderTopRightRadius = 8;
                tab.style.borderBottomLeftRadius = 8;
                tab.style.borderBottomRightRadius = 8;
                tab.style.backgroundColor = (p.Id == activeId) ? new Color(0.25f, 0.65f, 1.0f, 0.55f) : new Color(0.18f, 0.18f, 0.18f, 1f);
                tab.style.paddingLeft = 6;
                tab.style.paddingRight = 6;

                var btn = new Button();
                btn.text = string.IsNullOrWhiteSpace(p.Name) ? ("项目" + (i + 1)) : p.Name;
                btn.style.flexGrow = 1;
                btn.style.height = 24;
                btn.style.unityTextAlign = TextAnchor.MiddleLeft;
                btn.style.color = Color.white;
                btn.style.backgroundColor = new Color(0, 0, 0, 0);
                btn.style.borderLeftWidth = 0;
                btn.style.borderRightWidth = 0;
                btn.style.borderTopWidth = 0;
                btn.style.borderBottomWidth = 0;
                btn.clicked += () =>
                {
                    if (systemManager != null) systemManager.SwitchProject(pid);
                    RebuildProjectTabs();
                };

                var btnClose = new Button();
                btnClose.text = "×";
                btnClose.style.width = 22;
                btnClose.style.height = 22;
                btnClose.style.unityTextAlign = TextAnchor.MiddleCenter;
                btnClose.style.color = Color.white;
                btnClose.style.backgroundColor = new Color(0, 0, 0, 0);
                btnClose.style.borderLeftWidth = 0;
                btnClose.style.borderRightWidth = 0;
                btnClose.style.borderTopWidth = 0;
                btnClose.style.borderBottomWidth = 0;
                btnClose.SetEnabled(projects.Count > 1);
                btnClose.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    if (systemManager != null) systemManager.CloseProject(pid);
                    RebuildProjectTabs();
                });

                tab.Add(btn);
                tab.Add(btnClose);
                tabsContainer.Add(tab);
                projectTabButtons[p.Id] = btn;
            }
        }

        private void OnOrbitViewClicked()
        {
            if (tbOrbitViewButton == null) return;
            var camCtrl = Camera.main?.GetComponent<CameraController>();
            if (camCtrl == null) return;

            bool isOrbit = !camCtrl.isOrbitMode;
            if (isOrbit && systemManager != null && systemManager.TryGetRootBounds(out var bounds))
            {
                float sc = Mathf.Max(1f, OcxSystemManager.ModelVisualScale);
                camCtrl.ToggleOrbitMode(true, bounds.center, bounds.size.magnitude / sc);
                SetToggleButtonVisual(tbOrbitViewButton, true, new Color(0.25f, 0.85f, 0.25f));
            }
            else
            {
                camCtrl.ToggleOrbitMode(false, Vector3.zero, 0);
                SetToggleButtonVisual(tbOrbitViewButton, false, new Color(0.25f, 0.85f, 0.25f));
            }
        }

        private void SetToggleButtonVisual(Button btn, bool enabled, Color accent)
        {
            if (btn == null) return;
            var baseBg = new Color(0.17f, 0.17f, 0.17f, 1f);
            var baseBorder = new Color(0.35f, 0.35f, 0.35f, 1f);

            btn.style.borderLeftWidth = 2;
            btn.style.borderRightWidth = 2;
            btn.style.borderTopWidth = 2;
            btn.style.borderBottomWidth = 2;

            if (enabled)
            {
                btn.style.backgroundColor = Color.Lerp(baseBg, accent, 0.65f);
                btn.style.borderLeftColor = accent;
                btn.style.borderRightColor = accent;
                btn.style.borderTopColor = accent;
                btn.style.borderBottomColor = accent;
                btn.style.unityBackgroundImageTintColor = accent;
            }
            else
            {
                btn.style.backgroundColor = baseBg;
                btn.style.borderLeftColor = baseBorder;
                btn.style.borderRightColor = baseBorder;
                btn.style.borderTopColor = baseBorder;
                btn.style.borderBottomColor = baseBorder;
                btn.style.unityBackgroundImageTintColor = Color.white;
            }
        }

        private void SetDefaultInfoText()
        {
            if (interactor == null) return;
            interactor.SetInfoText(BuildHelpText());
        }

        private string BuildHelpText()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("功能说明");
            sb.AppendLine("--------------------------------");
            sb.AppendLine("导入：导入 OCX/3DOCX/XML 或 打开工程(.ocxproj)");
            sb.AppendLine("导出：导出 OCX 或 保存工程(.ocxproj)");
            sb.AppendLine("测量：测距/测角/测圆弧（左键依次点选所需点）");
            sb.AppendLine("变换：移动/旋转/缩放（拖拽操作）");
            sb.AppendLine("轴向：自由 / 锁定 X/Y/Z（切面模式下用于指定剖切方向）");
            sb.AppendLine("环绕：整体环绕查看（右键旋转，滚轮拉近拉远）");
            sb.AppendLine("剖切：进入切面后，右下角小视图调整剖切长方体范围；主视图滚轮移动展示位置");
            sb.AppendLine("详情：编辑材质/板厚/型材/端切/重量（在详情面板中输入）");
            sb.AppendLine("撤销：Ctrl + Z");
            sb.AppendLine("退出：退出前选择覆盖保存/另存为/不保存");
            sb.AppendLine();
            sb.AppendLine("视角操作");
            sb.AppendLine("--------------------------------");
            sb.AppendLine("右键拖拽：旋转视角");
            sb.AppendLine("中键拖拽：平移视角");
            sb.AppendLine("滚轮：前进/后退（或在环绕模式下缩放距离）");
            sb.AppendLine("W/A/S/D + Q/E：移动相机，Shift 加速");
            sb.AppendLine();
            sb.AppendLine("板件列表搜索");
            sb.AppendLine("--------------------------------");
            sb.AppendLine("点击输入框后再输入；回车或点“定位”选中第一个匹配对象。");
            return sb.ToString();
        }

        private void ShowImportChoice()
        {
            if (uiDocument == null) return;
            var root = uiDocument.rootVisualElement;
            if (root == null) return;

            if (importChoiceOverlay == null)
            {
                importChoiceOverlay = new VisualElement();
                importChoiceOverlay.name = "ImportChoiceOverlay";
                importChoiceOverlay.style.position = Position.Absolute;
                importChoiceOverlay.style.left = 0;
                importChoiceOverlay.style.right = 0;
                importChoiceOverlay.style.top = 0;
                importChoiceOverlay.style.bottom = 0;
                importChoiceOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
                importChoiceOverlay.pickingMode = PickingMode.Position;

                var dialog = new VisualElement();
                dialog.style.width = 460;
                dialog.style.maxWidth = new Length(92, LengthUnit.Percent);
                dialog.style.minWidth = 320;
                dialog.style.backgroundColor = new Color(26f / 255f, 26f / 255f, 26f / 255f, 0.95f);
                dialog.style.borderBottomWidth = 1;
                dialog.style.borderTopWidth = 1;
                dialog.style.borderLeftWidth = 1;
                dialog.style.borderRightWidth = 1;
                dialog.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f, 1f);
                dialog.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f, 1f);
                dialog.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f, 1f);
                dialog.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
                dialog.style.borderBottomLeftRadius = 10;
                dialog.style.borderBottomRightRadius = 10;
                dialog.style.borderTopLeftRadius = 10;
                dialog.style.borderTopRightRadius = 10;
                dialog.style.paddingLeft = 14;
                dialog.style.paddingRight = 14;
                dialog.style.paddingTop = 12;
                dialog.style.paddingBottom = 12;
                dialog.style.alignSelf = Align.Center;
                dialog.style.justifyContent = Justify.Center;

                var title = new Label("导入");
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.fontSize = 18;
                title.style.color = new Color(0.93f, 0.93f, 0.93f, 1f);
                title.style.marginBottom = 8;

                var msg = new Label("请选择导入类型：");
                msg.style.fontSize = 15;
                msg.style.color = new Color(0.93f, 0.93f, 0.93f, 1f);
                msg.style.whiteSpace = WhiteSpace.Normal;
                msg.style.marginBottom = 12;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.flexWrap = Wrap.Wrap;
                row.style.justifyContent = Justify.FlexEnd;

                var btnCancel = new Button(() => importChoiceOverlay.style.display = DisplayStyle.None) { text = "取消" };
                var btnImportOcx = new Button(() => { importChoiceOverlay.style.display = DisplayStyle.None; OnImportLocalFileClicked(); }) { text = "导入OCX" };
                var btnOpenProj = new Button(() => { importChoiceOverlay.style.display = DisplayStyle.None; OnOpenProjectClicked(); }) { text = "打开工程" };

                StyleDialogButton(btnCancel, false);
                StyleDialogButton(btnImportOcx, true);
                StyleDialogButton(btnOpenProj, false);
                btnCancel.style.marginRight = 8;
                btnImportOcx.style.marginRight = 8;

                row.Add(btnCancel);
                row.Add(btnImportOcx);
                row.Add(btnOpenProj);

                dialog.Add(title);
                dialog.Add(msg);
                dialog.Add(row);
                importChoiceOverlay.Add(dialog);
                root.Add(importChoiceOverlay);
            }

            importChoiceOverlay.style.display = DisplayStyle.Flex;
        }

        private void ShowExportChoice()
        {
            if (uiDocument == null) return;
            var root = uiDocument.rootVisualElement;
            if (root == null) return;

            if (exportChoiceOverlay == null)
            {
                exportChoiceOverlay = new VisualElement();
                exportChoiceOverlay.name = "ExportChoiceOverlay";
                exportChoiceOverlay.style.position = Position.Absolute;
                exportChoiceOverlay.style.left = 0;
                exportChoiceOverlay.style.right = 0;
                exportChoiceOverlay.style.top = 0;
                exportChoiceOverlay.style.bottom = 0;
                exportChoiceOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
                exportChoiceOverlay.pickingMode = PickingMode.Position;

                var dialog = new VisualElement();
                dialog.style.width = 480;
                dialog.style.maxWidth = new Length(92, LengthUnit.Percent);
                dialog.style.minWidth = 340;
                dialog.style.backgroundColor = new Color(26f / 255f, 26f / 255f, 26f / 255f, 0.95f);
                dialog.style.borderBottomWidth = 1;
                dialog.style.borderTopWidth = 1;
                dialog.style.borderLeftWidth = 1;
                dialog.style.borderRightWidth = 1;
                dialog.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f, 1f);
                dialog.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f, 1f);
                dialog.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f, 1f);
                dialog.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
                dialog.style.borderBottomLeftRadius = 10;
                dialog.style.borderBottomRightRadius = 10;
                dialog.style.borderTopLeftRadius = 10;
                dialog.style.borderTopRightRadius = 10;
                dialog.style.paddingLeft = 14;
                dialog.style.paddingRight = 14;
                dialog.style.paddingTop = 12;
                dialog.style.paddingBottom = 12;
                dialog.style.alignSelf = Align.Center;
                dialog.style.justifyContent = Justify.Center;

                var title = new Label("导出");
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.fontSize = 18;
                title.style.color = new Color(0.93f, 0.93f, 0.93f, 1f);
                title.style.marginBottom = 8;

                var msg = new Label("请选择导出类型：");
                msg.style.fontSize = 15;
                msg.style.color = new Color(0.93f, 0.93f, 0.93f, 1f);
                msg.style.whiteSpace = WhiteSpace.Normal;
                msg.style.marginBottom = 12;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.flexWrap = Wrap.Wrap;
                row.style.justifyContent = Justify.FlexEnd;

                var btnCancel = new Button(() => exportChoiceOverlay.style.display = DisplayStyle.None) { text = "取消" };
                var btnExportOcx = new Button(() =>
                {
                    exportChoiceOverlay.style.display = DisplayStyle.None;
                    if (systemManager != null) systemManager.ExportEdited3docx();
                    if (interactor != null)
                    {
                        string mode = (systemManager != null && systemManager.overwriteOriginalOnExport) ? "已覆盖源文件" : "已另存为 *_edited（与源文件同后缀）";
                        interactor.SetInfoText("已保存\n--------------------------------\n" + mode);
                    }
                })
                { text = "导出OCX" };
                var btnSaveProj = new Button(() => { exportChoiceOverlay.style.display = DisplayStyle.None; OnSaveProjectClicked(); }) { text = "保存工程" };

                StyleDialogButton(btnCancel, false);
                StyleDialogButton(btnExportOcx, true);
                StyleDialogButton(btnSaveProj, false);
                btnCancel.style.marginRight = 8;
                btnExportOcx.style.marginRight = 8;

                row.Add(btnCancel);
                row.Add(btnExportOcx);
                row.Add(btnSaveProj);

                dialog.Add(title);
                dialog.Add(msg);
                dialog.Add(row);
                exportChoiceOverlay.Add(dialog);
                root.Add(exportChoiceOverlay);
            }

            exportChoiceOverlay.style.display = DisplayStyle.Flex;
        }

        private void OnOpenProjectClicked()
        {
            string path = LocalFileBrowser.OpenProjectFile();
            if (string.IsNullOrWhiteSpace(path)) return;
            StartCoroutine(LoadProjectCoroutine(path));
        }

        private void OnSaveProjectClicked()
        {
            string path = LocalFileBrowser.SaveProjectFile("ocx_project.ocxproj");
            if (string.IsNullOrWhiteSpace(path)) return;
            if (systemManager == null) return;

            string err;
            bool ok = systemManager.TrySaveProject(path, out err);
            if (interactor != null)
            {
                if (ok) interactor.SetInfoText("工程已保存\n--------------------------------\n" + path);
                else interactor.SetInfoText("工程保存失败\n--------------------------------\n" + (string.IsNullOrWhiteSpace(err) ? "未知错误" : err));
            }
        }

        private void ShowExitConfirm()
        {
            if (uiDocument == null) return;
            var root = uiDocument.rootVisualElement;
            if (root == null) return;

            if (exitOverlay == null)
            {
                exitOverlay = new VisualElement();
                exitOverlay.name = "ExitConfirmOverlay";
                exitOverlay.style.position = Position.Absolute;
                exitOverlay.style.left = 0;
                exitOverlay.style.right = 0;
                exitOverlay.style.top = 0;
                exitOverlay.style.bottom = 0;
                exitOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
                exitOverlay.pickingMode = PickingMode.Position;

                var dialog = new VisualElement();
                dialog.style.width = 520;
                dialog.style.maxWidth = new Length(92, LengthUnit.Percent);
                dialog.style.minWidth = 360;
                dialog.style.backgroundColor = new Color(26f / 255f, 26f / 255f, 26f / 255f, 0.95f);
                dialog.style.borderBottomWidth = 1;
                dialog.style.borderTopWidth = 1;
                dialog.style.borderLeftWidth = 1;
                dialog.style.borderRightWidth = 1;
                dialog.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f, 1f);
                dialog.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f, 1f);
                dialog.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f, 1f);
                dialog.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
                dialog.style.borderBottomLeftRadius = 10;
                dialog.style.borderBottomRightRadius = 10;
                dialog.style.borderTopLeftRadius = 10;
                dialog.style.borderTopRightRadius = 10;
                dialog.style.paddingLeft = 14;
                dialog.style.paddingRight = 14;
                dialog.style.paddingTop = 12;
                dialog.style.paddingBottom = 12;
                dialog.style.alignSelf = Align.Center;
                dialog.style.justifyContent = Justify.Center;

                var title = new Label("退出提示");
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.fontSize = 18;
                title.style.color = new Color(0.93f, 0.93f, 0.93f, 1f);
                title.style.marginBottom = 8;

                var msg = new Label("退出前是否需要保存？\n保存时会保持与导入文件相同的后缀格式。");
                msg.style.fontSize = 15;
                msg.style.color = new Color(0.93f, 0.93f, 0.93f, 1f);
                msg.style.whiteSpace = WhiteSpace.Normal;
                msg.style.marginBottom = 12;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.flexWrap = Wrap.Wrap;
                row.style.justifyContent = Justify.FlexEnd;
                row.style.marginLeft = 0;

                var btnCancel = new Button(() => exitOverlay.style.display = DisplayStyle.None) { text = "取消" };
                var btnNoSave = new Button(() => { exitOverlay.style.display = DisplayStyle.None; QuitApp(); }) { text = "不保存退出" };
                var btnOverwrite = new Button(() => { exitOverlay.style.display = DisplayStyle.None; SaveThenQuit(true); }) { text = "覆盖保存退出" };
                var btnSaveAs = new Button(() => { exitOverlay.style.display = DisplayStyle.None; SaveThenQuit(false); }) { text = "另存为退出" };

                StyleDialogButton(btnCancel, false);
                StyleDialogButton(btnNoSave, false);
                StyleDialogButton(btnOverwrite, true);
                StyleDialogButton(btnSaveAs, false);
                btnCancel.style.marginRight = 8;
                btnNoSave.style.marginRight = 8;
                btnOverwrite.style.marginRight = 8;

                row.Add(btnCancel);
                row.Add(btnNoSave);
                row.Add(btnOverwrite);
                row.Add(btnSaveAs);

                dialog.Add(title);
                dialog.Add(msg);
                dialog.Add(row);

                exitOverlay.Add(dialog);
                root.Add(exitOverlay);
            }

            exitOverlay.style.display = DisplayStyle.Flex;
        }

        private void StyleDialogButton(Button b, bool primary)
        {
            if (b == null) return;
            b.style.height = 28;
            b.style.minWidth = 96;
            b.style.unityTextAlign = TextAnchor.MiddleCenter;
            b.style.borderBottomLeftRadius = 8;
            b.style.borderBottomRightRadius = 8;
            b.style.borderTopLeftRadius = 8;
            b.style.borderTopRightRadius = 8;
            b.style.color = Color.white;
            b.style.backgroundColor = primary ? new Color(0.20f, 0.45f, 0.95f, 1f) : new Color(0.17f, 0.17f, 0.17f, 1f);
        }

        private void SaveThenQuit(bool overwrite)
        {
            if (systemManager != null)
            {
                systemManager.overwriteOriginalOnExport = overwrite;
                systemManager.ExportEdited3docx();
            }
            QuitApp();
        }

        private void QuitApp()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void ApplyToolbarIcons(Dictionary<Button, string> map)
        {
            foreach (var kv in map)
            {
                var btn = kv.Key;
                var key = kv.Value;
                if (btn == null) continue;
                btn.text = "";
                btn.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                var tex = LoadIconTexture(key);
                if (tex != null)
                {
                    btn.style.backgroundImage = new StyleBackground(tex);
                }
            }
        }

        private Texture2D LoadIconTexture(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            try
            {
                string dir = Path.Combine(Application.streamingAssetsPath, "UIIcons");
                string path = Path.Combine(dir, key + ".png");
                if (File.Exists(path))
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.LoadImage(bytes);
                    tex.filterMode = FilterMode.Bilinear;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    return tex;
                }
                var clean = TryLoadCleanFlatIcon(key);
                if (clean != null) return clean;
                return GenerateFallbackIcon(key);
            }
            catch
            {
                var clean = TryLoadCleanFlatIcon(key);
                if (clean != null) return clean;
                return GenerateFallbackIcon(key);
            }
        }

        private Texture2D TryLoadCleanFlatIcon(string key)
        {
            int index = key switch
            {
                "import" => 1,
                "export" => 2,
                "measure_distance" => 3,
                "measure_angle" => 4,
                "measure_arc" => 5,
                "transform_move" => 6,
                "transform_rotate" => 7,
                "transform_scale" => 8,
                "scale_up" => 9,
                "scale_down" => 10,
                "axis_free" => 11,
                "axis_x" => 12,
                "axis_y" => 13,
                "axis_z" => 14,
                "detail" => 15,
                "clear" => 16,
                "ui_toggle" => 17,
                _ => -1
            };
            if (index < 0) return null;

            string path = Path.Combine(Application.dataPath, "CleanFlatIcon", "png_128", "icon_line", "icon_line_common", $"icon_line_common_{index}.png");
            if (!File.Exists(path)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                return tex;
            }
            catch
            {
                return null;
            }
        }

        private Texture2D GenerateFallbackIcon(string key)
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    tex.SetPixel(x, y, clear);
                }
            }

            Color fg = Color.white;
            Color dim = new Color(1f, 1f, 1f, 0.6f);

            if (key == "import")
            {
                DrawArrow(tex, 16, 6, 16, 26, fg);
                DrawLine(tex, 8, 24, 24, 24, dim);
            }
            else if (key == "export")
            {
                DrawArrow(tex, 16, 26, 16, 6, fg);
                DrawLine(tex, 8, 8, 24, 8, dim);
            }
            else if (key == "measure_distance")
            {
                DrawRect(tex, 8, 10, 16, 12, dim);
                DrawLine(tex, 10, 12, 22, 12, fg);
                DrawLine(tex, 10, 14, 22, 14, fg);
                DrawLine(tex, 10, 16, 18, 16, fg);
            }
            else if (key == "measure_angle")
            {
                DrawLine(tex, 10, 22, 10, 10, fg);
                DrawLine(tex, 10, 22, 22, 22, fg);
                DrawCircleArc(tex, 10, 22, 10, 0, 90, dim);
            }
            else if (key == "measure_arc")
            {
                DrawCircleArc(tex, 16, 16, 10, 30, 210, fg);
                DrawLine(tex, 16, 16, 24, 12, dim);
                DrawLine(tex, 16, 16, 10, 10, dim);
            }
            else if (key == "transform_move")
            {
                DrawArrow(tex, 16, 6, 16, 26, fg);
                DrawArrow(tex, 6, 16, 26, 16, fg);
            }
            else if (key == "transform_rotate")
            {
                DrawCircleArc(tex, 16, 16, 10, 45, 315, fg);
                DrawArrow(tex, 23, 21, 26, 24, fg);
            }
            else if (key == "transform_scale")
            {
                DrawLine(tex, 10, 10, 22, 22, fg);
                DrawArrow(tex, 10, 10, 6, 6, fg);
                DrawArrow(tex, 22, 22, 26, 26, fg);
            }
            else if (key == "scale_up")
            {
                DrawLine(tex, 16, 10, 16, 22, fg);
                DrawLine(tex, 10, 16, 22, 16, fg);
            }
            else if (key == "scale_down")
            {
                DrawLine(tex, 10, 16, 22, 16, fg);
            }
            else if (key == "axis_free")
            {
                DrawCircle(tex, 16, 16, 9, fg);
                DrawCircle(tex, 16, 16, 3, dim);
            }
            else if (key == "axis_x" || key == "axis_y" || key == "axis_z")
            {
                DrawRect(tex, 9, 9, 14, 14, dim);
                DrawLine(tex, 10, 10, 22, 22, fg);
                DrawLine(tex, 22, 10, 10, 22, fg);
            }
            else if (key == "detail")
            {
                DrawCircle(tex, 14, 16, 7, fg);
                DrawLine(tex, 19, 11, 26, 4, fg);
            }
            else if (key == "clear")
            {
                DrawRect(tex, 10, 12, 12, 14, fg);
                DrawLine(tex, 10, 24, 22, 24, fg);
                DrawLine(tex, 12, 14, 12, 22, dim);
                DrawLine(tex, 16, 14, 16, 22, dim);
                DrawLine(tex, 20, 14, 20, 22, dim);
            }
            else if (key == "ui_toggle")
            {
                DrawRect(tex, 8, 10, 16, 12, fg);
                DrawLine(tex, 10, 20, 22, 20, dim);
                DrawLine(tex, 10, 16, 22, 16, dim);
                DrawLine(tex, 10, 12, 22, 12, dim);
            }
            else if (key == "focus")
            {
                DrawCircle(tex, 16, 16, 10, dim);
                DrawLine(tex, 16, 6, 16, 26, fg);
                DrawLine(tex, 6, 16, 26, 16, fg);
                DrawCircle(tex, 16, 16, 3, fg);
            }
            else
            {
                DrawRect(tex, 8, 8, 16, 16, dim);
            }

            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        private void DrawRect(Texture2D tex, int x, int y, int w, int h, Color c)
        {
            DrawLine(tex, x, y, x + w, y, c);
            DrawLine(tex, x + w, y, x + w, y + h, c);
            DrawLine(tex, x + w, y + h, x, y + h, c);
            DrawLine(tex, x, y + h, x, y, c);
        }

        private void DrawCircle(Texture2D tex, int cx, int cy, int r, Color c)
        {
            int x = r;
            int y = 0;
            int err = 0;
            while (x >= y)
            {
                SetPixelSafe(tex, cx + x, cy + y, c);
                SetPixelSafe(tex, cx + y, cy + x, c);
                SetPixelSafe(tex, cx - y, cy + x, c);
                SetPixelSafe(tex, cx - x, cy + y, c);
                SetPixelSafe(tex, cx - x, cy - y, c);
                SetPixelSafe(tex, cx - y, cy - x, c);
                SetPixelSafe(tex, cx + y, cy - x, c);
                SetPixelSafe(tex, cx + x, cy - y, c);

                y++;
                if (err <= 0) err += 2 * y + 1;
                if (err > 0)
                {
                    x--;
                    err -= 2 * x + 1;
                }
            }
        }

        private void DrawCircleArc(Texture2D tex, int cx, int cy, int r, int startDeg, int endDeg, Color c)
        {
            int s = Mathf.Clamp(startDeg, 0, 360);
            int e = Mathf.Clamp(endDeg, 0, 360);
            if (e < s) { int t = s; s = e; e = t; }
            for (int d = s; d <= e; d += 2)
            {
                float a = d * Mathf.Deg2Rad;
                int x = cx + Mathf.RoundToInt(Mathf.Cos(a) * r);
                int y = cy + Mathf.RoundToInt(Mathf.Sin(a) * r);
                SetPixelSafe(tex, x, y, c);
            }
        }

        private void DrawArrow(Texture2D tex, int x0, int y0, int x1, int y1, Color c)
        {
            DrawLine(tex, x0, y0, x1, y1, c);
            Vector2 dir = new Vector2(x1 - x0, y1 - y0).normalized;
            Vector2 left = new Vector2(-dir.y, dir.x);
            Vector2 p1 = new Vector2(x1, y1) - dir * 5f + left * 3f;
            Vector2 p2 = new Vector2(x1, y1) - dir * 5f - left * 3f;
            DrawLine(tex, x1, y1, Mathf.RoundToInt(p1.x), Mathf.RoundToInt(p1.y), c);
            DrawLine(tex, x1, y1, Mathf.RoundToInt(p2.x), Mathf.RoundToInt(p2.y), c);
        }

        private void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1, Color c)
        {
            int dx = Mathf.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                SetPixelSafe(tex, x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private void SetPixelSafe(Texture2D tex, int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= tex.width || y >= tex.height) return;
            tex.SetPixel(x, y, c);
        }

        void OnEnable()
        {
            if (interactor != null)
            {
                interactor.SelectionChanged -= OnSelectionChanged;
                interactor.SelectionChanged += OnSelectionChanged;
            }
        }

        void OnDisable()
        {
            if (interactor != null)
            {
                interactor.SelectionChanged -= OnSelectionChanged;
            }
        }

        private void OnSelectionChanged()
        {
            if (detailOverlay != null && detailOverlay.style.display == DisplayStyle.Flex)
            {
                RefreshDetailOverlay();
            }
            if (interactor == null) return;
            var selected = interactor.GetSelectedObjects();
            if (selected == null || selected.Count == 0) { SetDefaultInfoText(); return; }

            if (systemManager != null)
            {
                for (int i = 0; i < selected.Count; i++)
                {
                    var pd = selected[i] != null ? selected[i].GetComponent<PartData>() : null;
                    if (pd != null) systemManager.TryRefreshWeightFromSource(pd);
                }
            }

            if (selected.Count == 1)
            {
                var pd = selected[0] != null ? selected[0].GetComponent<PartData>() : null;
                if (pd != null) interactor.SetInfoText(pd.GetFormattedData());
            }
        }

        private void OnImportLocalFileClicked()
        {
            // 调用 Windows 原生文件选择弹窗（支持多选）
            string[] selectedFiles = LocalFileBrowser.OpenFiles();

            if (selectedFiles != null && selectedFiles.Length > 0)
            {
                StartCoroutine(ImportFilesCoroutine(selectedFiles));
            }
            else
            {
                Debug.Log("[系统提示] 用户取消了文件选择");
            }
        }

        private IEnumerator ImportFilesCoroutine(string[] files)
        {
            if (files == null || files.Length == 0) yield break;
            EnsureImportOverlay();
            importOverlay.style.display = DisplayStyle.Flex;

            int total = files.Length;
            int ok = 0;
            var failed = new List<(string path, string error)>();

            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                if (string.IsNullOrWhiteSpace(path)) continue;

                UpdateImportOverlay($"正在导入（{i + 1}/{total}）\n{Path.GetFileName(path)}", (i + 0.05f) / total);
                yield return null;

                string err = "";
                bool success = systemManager != null && systemManager.TryLoadAndBuild(path, appendMode: true, out err);
                if (success) ok++;
                else failed.Add((path, string.IsNullOrWhiteSpace(err) ? "未知错误" : err));

                UpdateImportOverlay($"正在导入（{i + 1}/{total}）\n{Path.GetFileName(path)}", (i + 1f) / total);
                yield return null;
            }

            importOverlay.style.display = DisplayStyle.None;

            if (systemManager != null && systemManager.LastFileGroup != null) StartCoroutine(DelayedSelectAndFocus(systemManager.LastFileGroup));

            if (interactor != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("导入完成");
                sb.AppendLine("--------------------------------");
                sb.AppendLine($"成功: {ok}  失败: {failed.Count}");
                if (failed.Count > 0)
                {
                    sb.AppendLine();
                    for (int i = 0; i < failed.Count; i++)
                    {
                        sb.AppendLine($"{Path.GetFileName(failed[i].path)}: {failed[i].error}");
                    }
                }
                interactor.SetInfoText(sb.ToString());
            }
        }

        private IEnumerator LoadProjectCoroutine(string projectPath)
        {
            if (systemManager == null) yield break;
            EnsureImportOverlay();
            importOverlay.style.display = DisplayStyle.Flex;
            UpdateImportOverlay("正在读取工程...\n" + Path.GetFileName(projectPath), 0.02f);
            yield return null;

            if (!systemManager.TryLoadProjectFile(projectPath, out var proj, out string err))
            {
                importOverlay.style.display = DisplayStyle.None;
                if (interactor != null) interactor.SetInfoText("读取工程失败\n--------------------------------\n" + (string.IsNullOrWhiteSpace(err) ? "未知错误" : err));
                yield break;
            }

            var files = proj.SourceFiles ?? new List<string>();
            systemManager.ClearAllModels();

            int total = files.Count;
            int ok = 0;
            var failed = new List<(string path, string error)>();

            for (int i = 0; i < files.Count; i++)
            {
                string path = files[i];
                if (string.IsNullOrWhiteSpace(path)) continue;

                float p0 = total > 0 ? (i + 0.05f) / total : 0.05f;
                UpdateImportOverlay($"正在载入工程（{i + 1}/{Mathf.Max(1, total)}）\n{Path.GetFileName(path)}", p0);
                yield return null;

                string ferr = "";
                bool success = systemManager.TryLoadAndBuild(path, appendMode: true, out ferr);
                if (success) ok++;
                else failed.Add((path, string.IsNullOrWhiteSpace(ferr) ? "未知错误" : ferr));

                float p1 = total > 0 ? (i + 1f) / total : 1f;
                UpdateImportOverlay($"正在载入工程（{i + 1}/{Mathf.Max(1, total)}）\n{Path.GetFileName(path)}", p1);
                yield return null;
            }

            UpdateImportOverlay("正在应用工程状态...", 0.95f);
            yield return null;

            systemManager.ApplyProjectState(proj, out int applied, out int missing);
            importOverlay.style.display = DisplayStyle.None;

            if (systemManager.LastFileGroup != null) StartCoroutine(DelayedSelectAndFocus(systemManager.LastFileGroup));

            if (interactor != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("工程载入完成");
                sb.AppendLine("--------------------------------");
                sb.AppendLine($"工程文件: {Path.GetFileName(projectPath)}");
                sb.AppendLine($"导入文件: 成功 {ok}  失败 {failed.Count}");
                sb.AppendLine($"状态应用: 成功 {applied}  未匹配 {missing}");
                if (failed.Count > 0)
                {
                    sb.AppendLine();
                    for (int i = 0; i < failed.Count; i++)
                    {
                        sb.AppendLine($"{Path.GetFileName(failed[i].path)}: {failed[i].error}");
                    }
                }
                interactor.SetInfoText(sb.ToString());
            }
        }

        private void EnsureImportOverlay()
        {
            if (uiDocument == null) return;
            var root = uiDocument.rootVisualElement;
            if (root == null) return;
            if (importOverlay != null && importOverlay.parent != null) return;

            importOverlay = new VisualElement();
            importOverlay.name = "ImportOverlay";
            importOverlay.style.position = Position.Absolute;
            importOverlay.style.left = 0;
            importOverlay.style.right = 0;
            importOverlay.style.top = 0;
            importOverlay.style.bottom = 0;
            importOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            importOverlay.pickingMode = PickingMode.Position;
            importOverlay.style.display = DisplayStyle.None;

            var panel = new VisualElement();
            panel.style.width = 520;
            panel.style.backgroundColor = new Color(26f / 255f, 26f / 255f, 26f / 255f, 0.95f);
            panel.style.borderBottomWidth = 1;
            panel.style.borderTopWidth = 1;
            panel.style.borderLeftWidth = 1;
            panel.style.borderRightWidth = 1;
            panel.style.borderBottomColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderTopColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderLeftColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderRightColor = new Color(0.35f, 0.35f, 0.35f, 1f);
            panel.style.borderBottomLeftRadius = 10;
            panel.style.borderBottomRightRadius = 10;
            panel.style.borderTopLeftRadius = 10;
            panel.style.borderTopRightRadius = 10;
            panel.style.paddingLeft = 14;
            panel.style.paddingRight = 14;
            panel.style.paddingTop = 12;
            panel.style.paddingBottom = 12;
            panel.style.alignSelf = Align.Center;

            var title = new Label("正在导入...");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 18;
            title.style.color = new Color(0.93f, 0.93f, 0.93f, 1f);
            title.style.marginBottom = 8;

            importLabel = new Label("");
            importLabel.style.fontSize = 14;
            importLabel.style.color = new Color(0.93f, 0.93f, 0.93f, 1f);
            importLabel.style.whiteSpace = WhiteSpace.Normal;
            importLabel.style.marginBottom = 10;

            var barBg = new VisualElement();
            barBg.style.height = 10;
            barBg.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            barBg.style.borderBottomLeftRadius = 6;
            barBg.style.borderBottomRightRadius = 6;
            barBg.style.borderTopLeftRadius = 6;
            barBg.style.borderTopRightRadius = 6;
            barBg.style.overflow = Overflow.Hidden;

            importBarFill = new VisualElement();
            importBarFill.style.height = 10;
            importBarFill.style.width = new StyleLength(new Length(0, LengthUnit.Percent));
            importBarFill.style.backgroundColor = new Color(0.20f, 0.45f, 0.95f, 1f);
            barBg.Add(importBarFill);

            panel.Add(title);
            panel.Add(importLabel);
            panel.Add(barBg);
            importOverlay.Add(panel);
            root.Add(importOverlay);
        }

        private void UpdateImportOverlay(string text, float progress01)
        {
            if (importLabel != null) importLabel.text = text ?? "";
            if (importBarFill != null)
            {
                float p = Mathf.Clamp01(progress01);
                importBarFill.style.width = new StyleLength(new Length(p * 100f, LengthUnit.Percent));
            }
        }

        private IEnumerator DelayedSelectAndFocus(Transform group)
        {
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(0.2f);

            if (systemManager == null || group == null) yield break;

            if (interactor == null) interactor = FindAnyObjectByType<VisualAnalyticsInteractor>();
            if (interactor != null)
            {
                var parts = group.GetComponentsInChildren<PartData>(true);
                var objs = new List<GameObject>(parts.Length);
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i] != null) objs.Add(parts[i].gameObject);
                }
                interactor.SelectObjectsExternal(objs, false);
            }

            bool ok = systemManager.TryAutoFocusCameraOn(group, out string info);
            Debug.Log($"[AutoFocus] ok={ok} {info}");
            yield return null;
            ok = systemManager.TryAutoFocusCameraOn(group, out info);
            Debug.Log($"[AutoFocus] retry ok={ok} {info}");
        }

        private void SetActiveMeasure(Button btn)
        {
            var baseColor = new Color(0.17f, 0.17f, 0.17f);
            if (activeMeasureBtn != null)
            {
                activeMeasureBtn.style.backgroundColor = new StyleColor(baseColor);
            }
            activeMeasureBtn = btn;
            if (activeMeasureBtn != null)
            {
                activeMeasureBtn.style.backgroundColor = new StyleColor(new Color(0.1f, 0.5f, 0.1f));
            }
        }

        private void SetActiveTransform(Button btn)
        {
            var baseColor = new Color(0.17f, 0.17f, 0.17f);
            if (activeTransformBtn != null)
            {
                activeTransformBtn.style.backgroundColor = new StyleColor(baseColor);
            }
            activeTransformBtn = btn;
            if (activeTransformBtn != null)
            {
                activeTransformBtn.style.backgroundColor = new StyleColor(new Color(0.1f, 0.3f, 0.6f));
            }
        }

        private void ToggleMeasure(Button btn, Action enable)
        {
            if (measurementTool == null) return;
            if (activeMeasureBtn == btn)
            {
                measurementTool.Exit();
                SetActiveMeasure(null);
                if (interactor != null) interactor.enabled = true;
                return;
            }
            if (interactor != null) interactor.ExitTransform();
            measurementTool.Exit();
            enable?.Invoke();
            SetActiveMeasure(btn);
            if (interactor != null) interactor.enabled = false;
            suppressMeasureClickUntilFrame = Time.frameCount + 1;
        }

        private void ToggleTransform(Button btn, Action enable)
        {
            if (interactor == null) return;
            if (activeTransformBtn == btn)
            {
                interactor.ExitTransform();
                SetActiveTransform(null);
                return;
            }
            if (measurementTool != null) measurementTool.Exit();
            if (activeMeasureBtn != null) SetActiveMeasure(null);
            interactor.enabled = true;
            enable?.Invoke();
            SetActiveTransform(btn);
        }

        private void SetActiveAxis(Button btn)
        {
            var baseColor = new Color(0.17f, 0.17f, 0.17f);
            if (activeAxisBtn != null) activeAxisBtn.style.backgroundColor = new StyleColor(baseColor);
            activeAxisBtn = btn;
            if (activeAxisBtn != null) activeAxisBtn.style.backgroundColor = new StyleColor(new Color(0.6f, 0.35f, 0.1f));
        }

        private void SetActiveSection(Button btn)
        {
            var baseColor = new Color(0.17f, 0.17f, 0.17f);
            if (activeSectionBtn != null) activeSectionBtn.style.backgroundColor = new StyleColor(baseColor);
            activeSectionBtn = btn;
            if (activeSectionBtn != null) activeSectionBtn.style.backgroundColor = new StyleColor(new Color(0.15f, 0.55f, 0.85f));
        }

        private void RefreshDetailOverlay()
        {
            if (detailScroll == null || interactor == null) return;
            detailScroll.Clear();
            var selected = interactor.GetSelectedObjects();
            if (selected == null || selected.Count == 0)
            {
                detailScroll.Add(new Label("未选中任何板件"));
                return;
            }
            if (selected.Count == 1)
            {
                var obj = selected[0];
                var data = obj.GetComponent<PartData>();
                if (data == null)
                {
                    detailScroll.Add(new Label(obj.name));
                    return;
                }

                var header = new Label(data.GetFormattedData());
                header.style.whiteSpace = WhiteSpace.Normal;
                header.enableRichText = true;
                detailScroll.Add(header);

                var tfMaterial = new TextField("材质") { value = !string.IsNullOrEmpty(data.MaterialName) ? data.MaterialName : (data.MaterialRef ?? "") };
                var tfThickness = new TextField("板厚") { value = data.ThicknessValue > 0f ? (data.ThicknessValue * 1000f).ToString("F2") + " mm" : (data.Thickness ?? "") };
                var tfSection = new TextField("型材规格") { value = data.SectionRef ?? "" };
                var tfEndCut = new TextField("端切代码") { value = data.EndCutCode ?? "" };
                var tfWeight = new TextField("重量(kg)") { value = data.Weight.ToString("F2") };

                tfMaterial.isReadOnly = true;
                tfMaterial.style.color = Color.white;
                tfThickness.style.color = Color.white;
                tfSection.style.color = Color.white;
                tfEndCut.style.color = Color.white;
                tfWeight.style.color = Color.white;
                tfMaterial.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
                tfThickness.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
                tfSection.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
                tfEndCut.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
                tfWeight.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
                tfMaterial.style.borderTopWidth = 0; tfMaterial.style.borderBottomWidth = 0; tfMaterial.style.borderLeftWidth = 0; tfMaterial.style.borderRightWidth = 0;
                tfThickness.style.borderTopWidth = 0; tfThickness.style.borderBottomWidth = 0; tfThickness.style.borderLeftWidth = 0; tfThickness.style.borderRightWidth = 0;
                tfSection.style.borderTopWidth = 0; tfSection.style.borderBottomWidth = 0; tfSection.style.borderLeftWidth = 0; tfSection.style.borderRightWidth = 0;
                tfEndCut.style.borderTopWidth = 0; tfEndCut.style.borderBottomWidth = 0; tfEndCut.style.borderLeftWidth = 0; tfEndCut.style.borderRightWidth = 0;
                tfWeight.style.borderTopWidth = 0; tfWeight.style.borderBottomWidth = 0; tfWeight.style.borderLeftWidth = 0; tfWeight.style.borderRightWidth = 0;

                tfThickness.RegisterValueChangedCallback(e =>
                {
                    data.Thickness = e.newValue;
                    if (systemManager != null) systemManager.RebuildPlateGeometry(data);
                    if (interactor != null) interactor.SetInfoText(data.GetFormattedData());
                });
                tfSection.RegisterValueChangedCallback(e =>
                {
                    data.SectionRef = e.newValue;
                    if (interactor != null) interactor.SetInfoText(data.GetFormattedData());
                });
                tfEndCut.RegisterValueChangedCallback(e =>
                {
                    data.EndCutCode = e.newValue;
                    if (interactor != null) interactor.SetInfoText(data.GetFormattedData());
                });
                tfWeight.RegisterValueChangedCallback(e =>
                {
                    if (float.TryParse(e.newValue, out var w)) data.Weight = w;
                    if (interactor != null) interactor.SetInfoText(data.GetFormattedData());
                });

                detailScroll.Add(tfMaterial);
                if (string.Equals(data.PartType ?? "", "Plate", StringComparison.OrdinalIgnoreCase)) detailScroll.Add(tfThickness);
                if (string.Equals(data.PartType ?? "", "Stiffener", StringComparison.OrdinalIgnoreCase))
                {
                    detailScroll.Add(tfSection);
                    detailScroll.Add(tfEndCut);
                }
                detailScroll.Add(tfWeight);
                return;
            }

            for (int i = 0; i < selected.Count; i++)
            {
                var obj = selected[i];
                var data = obj.GetComponent<PartData>();
                var lb = new Label(data != null ? data.GetFormattedData() : obj.name);
                lb.style.whiteSpace = WhiteSpace.Normal;
                lb.enableRichText = true;
                detailScroll.Add(lb);
            }
        }

        void Update()
        {
            if (measurementTool != null && measurementTool.IsActive() && Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame && Time.frameCount > suppressMeasureClickUntilFrame)
                {
                    var cam = Camera.main;
                    if (cam != null) measurementTool.HandleMouseClick(cam);
                }
            }

            if (Keyboard.current == null) return;
            if (panelContainer == null) return;

            bool ctrl = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
            if (ctrl && Keyboard.current.zKey.wasPressedThisFrame)
            {
                if (systemManager != null) systemManager.UndoLast();
            }

            if (Keyboard.current.nKey.wasPressedThisFrame)
            {
                panelContainer.style.display = DisplayStyle.Flex;
            }
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                panelContainer.style.display = DisplayStyle.None;
            }
        }
    }
}
