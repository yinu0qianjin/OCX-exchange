using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using System;
using System.Collections.Generic; // 必须引入，用于 List 和 Dictionary

namespace Zhouxiangyang
{
    public class VisualAnalyticsInteractor : MonoBehaviour
    {
        private enum AxisLock
        {
            Free,
            X,
            Y,
            Z
        }

        private enum TransformMode
        {
            None,
            Translate,
            Rotate,
            Scale
        }

        [Header("UI 引用")]
        public UIDocument uiDocument;
        private Label dataLabel;
        private VisualElement uiRoot;

        [Header("相机引用")]
        public Camera mainCamera;
        public MeasurementTool measurementTool;

        // ================= 核心修复：多选与高亮状态缓存 =================
        // 这两行代码就是 CS0103 报错丢失的变量
        private List<GameObject> selectedObjects = new List<GameObject>();
        private Dictionary<GameObject, Color> originalColors = new Dictionary<GameObject, Color>();
        // ===============================================================

        public float transformTranslateSensitivity = 0.01f;
        public float transformRotateSensitivity = 0.2f;
        public float transformScaleSensitivity = 0.005f;

        private TransformMode transformMode = TransformMode.None;
        private bool isTransformDragging = false;
        private AxisLock axisLock = AxisLock.Free;
        public event Action SelectionChanged;

        void Start()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (measurementTool == null) measurementTool = FindAnyObjectByType<MeasurementTool>();

            uiRoot = uiDocument != null ? uiDocument.rootVisualElement : null;
            dataLabel = uiRoot != null ? uiRoot.Q<Label>("DataLabel") : null;

            if (dataLabel != null)
                dataLabel.text = "已就绪。\n• 左键：选择构件\n• Shift+左键：多选\n• 测距/测角/测圆弧：用左键依次点击所需点";
        }

        void Update()
        {
            // 1. 鼠标左键点选逻辑 (支持 Shift 多选)
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (uiRoot != null && uiRoot.panel != null)
                {
                    Vector2 mp = Mouse.current.position.ReadValue();
                    Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(uiRoot.panel, mp);
                    var picked = uiRoot.panel.Pick(panelPos);
                    if (picked != null) return;
                }

                if (transformMode != TransformMode.None && selectedObjects.Count > 0)
                {
                    isTransformDragging = true;
                    return;
                }
                if (measurementTool != null && measurementTool.IsActive())
                {
                    if (measurementTool.HandleMouseClick(mainCamera)) return;
                }
                ExecuteSelection();
            }

            if (Mouse.current != null && isTransformDragging)
            {
                if (Mouse.current.leftButton.isPressed)
                {
                    ApplyTransform(Mouse.current.delta.ReadValue());
                }
                else if (Mouse.current.leftButton.wasReleasedThisFrame)
                {
                    isTransformDragging = false;
                }
            }
        }

        public void SetTransformTranslate()
        {
            transformMode = TransformMode.Translate;
            isTransformDragging = false;
        }

        public void SetTransformRotate()
        {
            transformMode = TransformMode.Rotate;
            isTransformDragging = false;
        }

        public void SetTransformScale()
        {
            transformMode = TransformMode.Scale;
            isTransformDragging = false;
        }

        public void ExitTransform()
        {
            transformMode = TransformMode.None;
            isTransformDragging = false;
        }

        public bool IsTransformActive()
        {
            return transformMode != TransformMode.None;
        }

        private void ApplyTransform(Vector2 mouseDelta)
        {
            if (selectedObjects.Count == 0) return;

            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            Vector3 pivot = GetSelectionPivotWorld();

            if (transformMode == TransformMode.Translate)
            {
                if (axisLock == AxisLock.Free)
                {
                    Vector3 delta = (mainCamera.transform.right * mouseDelta.x + mainCamera.transform.up * mouseDelta.y) * transformTranslateSensitivity;
                    for (int i = 0; i < selectedObjects.Count; i++)
                    {
                        var obj = selectedObjects[i];
                        if (obj == null) continue;
                        obj.transform.position += delta;
                    }
                }
                else
                {
                    float a = (mouseDelta.x + mouseDelta.y) * transformTranslateSensitivity;
                    Vector3 delta = GetAxisVector(axisLock) * a;
                    for (int i = 0; i < selectedObjects.Count; i++)
                    {
                        var obj = selectedObjects[i];
                        if (obj == null) continue;
                        obj.transform.position += delta;
                    }
                }
                return;
            }

            if (transformMode == TransformMode.Rotate)
            {
                if (axisLock == AxisLock.Free)
                {
                    float ax = mouseDelta.x * transformRotateSensitivity;
                    float ay = -mouseDelta.y * transformRotateSensitivity;
                    Vector3 up = mainCamera.transform.up;
                    Vector3 right = mainCamera.transform.right;
                    for (int i = 0; i < selectedObjects.Count; i++)
                    {
                        var obj = selectedObjects[i];
                        if (obj == null) continue;
                        obj.transform.RotateAround(pivot, up, ax);
                        obj.transform.RotateAround(pivot, right, ay);
                    }
                }
                else
                {
                    float a = mouseDelta.x * transformRotateSensitivity;
                    Vector3 axis = GetAxisVector(axisLock);
                    for (int i = 0; i < selectedObjects.Count; i++)
                    {
                        var obj = selectedObjects[i];
                        if (obj == null) continue;
                        obj.transform.RotateAround(pivot, axis, a);
                    }
                }
                return;
            }

            if (transformMode == TransformMode.Scale)
            {
                float factor = Mathf.Clamp(1f + mouseDelta.y * transformScaleSensitivity, 0.9f, 1.1f);
                Vector3 scaleFactor = Vector3.one;
                if (axisLock == AxisLock.Free) scaleFactor = new Vector3(factor, factor, factor);
                else if (axisLock == AxisLock.X) scaleFactor = new Vector3(factor, 1f, 1f);
                else if (axisLock == AxisLock.Y) scaleFactor = new Vector3(1f, factor, 1f);
                else if (axisLock == AxisLock.Z) scaleFactor = new Vector3(1f, 1f, factor);

                for (int i = 0; i < selectedObjects.Count; i++)
                {
                    var obj = selectedObjects[i];
                    if (obj == null) continue;

                    Vector3 pos = obj.transform.position;
                    Vector3 offset = pos - pivot;
                    offset = Vector3.Scale(offset, scaleFactor);
                    obj.transform.position = pivot + offset;

                    Vector3 s = obj.transform.localScale;
                    s = Vector3.Scale(s, scaleFactor);
                    float m = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                    if (m > 0.001f && m < 5000f) obj.transform.localScale = s;
                }
            }
        }

        public void NudgeScaleSelected(float factor)
        {
            if (selectedObjects.Count == 0) return;
            Vector3 pivot = GetSelectionPivotWorld();
            foreach (var obj in selectedObjects)
            {
                if (obj == null) continue;
                Vector3 pos = obj.transform.position;
                obj.transform.position = pivot + (pos - pivot) * factor;
                Vector3 next = obj.transform.localScale * factor;
                float m = Mathf.Max(next.x, Mathf.Max(next.y, next.z));
                if (m > 0.001f && m < 5000f) obj.transform.localScale = next;
            }
        }

        private Vector3 GetSelectionPivotWorld()
        {
            if (selectedObjects.Count == 0) return Vector3.zero;
            bool hasBounds = false;
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            for (int i = 0; i < selectedObjects.Count; i++)
            {
                var obj = selectedObjects[i];
                if (obj == null) continue;
                var r = obj.GetComponent<Renderer>();
                if (r == null) continue;
                if (!hasBounds) { b = r.bounds; hasBounds = true; }
                else b.Encapsulate(r.bounds);
            }
            if (hasBounds) return b.center;
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < selectedObjects.Count; i++)
            {
                var obj = selectedObjects[i];
                if (obj == null) continue;
                sum += obj.transform.position;
                n++;
            }
            return n > 0 ? sum / n : Vector3.zero;
        }

        public void SetAxisFree() { axisLock = AxisLock.Free; }
        public void SetAxisX() { axisLock = AxisLock.X; }
        public void SetAxisY() { axisLock = AxisLock.Y; }
        public void SetAxisZ() { axisLock = AxisLock.Z; }

        private Vector3 GetAxisVector(AxisLock axis)
        {
            if (axis == AxisLock.X) return Vector3.right;
            if (axis == AxisLock.Y) return Vector3.up;
            if (axis == AxisLock.Z) return Vector3.forward;
            return Vector3.zero;
        }

        public void SelectObjectsExternal(IEnumerable<GameObject> objects, bool append = false)
        {
            if (!append) ClearSelection();
            foreach (var obj in objects)
            {
                if (obj == null) continue;
                HighlightTarget(obj);
            }
            UpdateUIPanel();
        }

        private void ExecuteSelection()
        {
            Vector2 mousePosition = Mouse.current.position.ReadValue();
            Ray ray = mainCamera.ScreenPointToRay(mousePosition);

            // 判断是否按住 Shift 键进行多选
            bool isMultiSelect = Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.collider.TryGetComponent<PartData>(out var partData))
                {
                    var go = hit.collider.gameObject;
                    bool alreadySelected = selectedObjects.Contains(go);
                    if (alreadySelected)
                    {
                        // 再次点击同一构件：取消选中（toggle）
                        UnhighlightTarget(go);
                        UpdateUIPanel();
                        return;
                    }
                    else
                    {
                        if (!isMultiSelect) ClearSelection();
                        HighlightTarget(go);
                        UpdateUIPanel();
                        return;
                    }
                }
            }
            else
            {
                if (!isMultiSelect) ClearSelection();
            }
        }

        private void HighlightTarget(GameObject target)
        {
            if (!selectedObjects.Contains(target))
            {
                selectedObjects.Add(target);
                var renderer = target.GetComponent<Renderer>();
                if (renderer != null)
                {
                    originalColors[target] = renderer.material.color;
                    renderer.material.color = originalColors[target]; // 保持原色
                }
            }
        }

        private void UnhighlightTarget(GameObject target)
        {
            if (selectedObjects.Contains(target))
            {
                selectedObjects.Remove(target);
                if (originalColors.TryGetValue(target, out var col))
                {
                    var renderer = target.GetComponent<Renderer>();
                    if (renderer != null) renderer.material.color = col;
                }
                originalColors.Remove(target);
            }
            if (selectedObjects.Count == 0)
            {
                if (dataLabel != null) dataLabel.text = "等待选择结构件...";
                SelectionChanged?.Invoke();
            }
        }

        private void ClearSelection()
        {
            foreach (var obj in selectedObjects)
            {
                if (obj != null && originalColors.ContainsKey(obj))
                {
                    var renderer = obj.GetComponent<Renderer>();
                    if (renderer != null) renderer.material.color = originalColors[obj];
                }
            }
            selectedObjects.Clear();
            originalColors.Clear();
            if (dataLabel != null) dataLabel.text = "等待选择结构件...";
            SelectionChanged?.Invoke();
        }

        // 更新 UI 面板
        private void UpdateUIPanel()
        {
            if (dataLabel == null) return;

            if (selectedObjects.Count == 1)
            {
                // 单选：展示 S0/S1/S2 全部细节
                var data = selectedObjects[0].GetComponent<PartData>();
                dataLabel.text = data.GetFormattedData();
                SelectionChanged?.Invoke();
            }
            else if (selectedObjects.Count > 1)
            {
                // 多选：统计 + 列出所有详细信息
                float totalWeight = 0f;
                foreach (var obj in selectedObjects)
                {
                    if (obj.TryGetComponent<PartData>(out var data))
                    {
                        totalWeight += data.Weight;
                    }
                }

                string statsText = $"<b><size=120%>批量统计模式 (多选)</size></b>\n";
                statsText += "--------------------------------\n";
                statsText += $"已选对象数量: {selectedObjects.Count} 个\n\n";
                statsText += $"<color=#55AAFF><b>[S1 统计功能]</b></color>\n";
                statsText += $"所选结构总重量: <b>{totalWeight:F2} kg</b>\n";
                statsText += "\n<color=#88FF88><b>详细信息</b></color>\n";
                for (int i = 0; i < selectedObjects.Count; i++)
                {
                    var d = selectedObjects[i].GetComponent<PartData>();
                    if (d != null)
                    {
                        statsText += $"\n—— 第 {i + 1} 项 ——\n";
                        statsText += d.GetFormattedData();
                    }
                }

                dataLabel.text = statsText;
                SelectionChanged?.Invoke();
            }
        }

        public IReadOnlyList<GameObject> GetSelectedObjects() => selectedObjects;

        public void SetInfoText(string text)
        {
            if (dataLabel != null) dataLabel.text = text;
        }

        private Camera ResolveCamera()
        {
            var cam = mainCamera;
            if (cam != null) return cam;
            cam = Camera.main;
            if (cam != null) return cam;
            var ctrl = FindAnyObjectByType<CameraController>();
            if (ctrl != null)
            {
                cam = ctrl.GetComponent<Camera>();
                if (cam != null) return cam;
            }
            cam = FindAnyObjectByType<Camera>();
            return cam;
        }
    }
}
