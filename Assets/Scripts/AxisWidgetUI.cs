using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace Zhouxiangyang
{
    public class AxisWidgetUI : MonoBehaviour
    {
        public UIDocument uiDocument;
        public string containerName = "AxisWidget";
        public float axisLength = 34f;
        public float lineThickness = 3f;

        private VisualElement container;
        private VisualElement xLine;
        private VisualElement yLine;
        private VisualElement zLine;
        private Label xLabel;
        private Label yLabel;
        private Label zLabel;
        private VisualElement centerDot;
        private readonly List<(VisualElement line, Label label, Color color, string text, Vector3 worldAxis)> axes = new();

        void Start()
        {
            if (uiDocument == null) uiDocument = FindAnyObjectByType<UIDocument>();
            if (uiDocument == null) return;

            var root = uiDocument.rootVisualElement;
            if (root == null) return;
            container = root.Q<VisualElement>(containerName);
            if (container == null) return;

            container.Clear();
            container.style.flexDirection = FlexDirection.Column;
            container.style.justifyContent = Justify.Center;
            container.style.alignItems = Align.Center;
            container.pickingMode = PickingMode.Ignore;

            centerDot = new VisualElement();
            centerDot.style.position = Position.Absolute;
            centerDot.style.width = 6;
            centerDot.style.height = 6;
            centerDot.style.borderTopLeftRadius = 3;
            centerDot.style.borderTopRightRadius = 3;
            centerDot.style.borderBottomLeftRadius = 3;
            centerDot.style.borderBottomRightRadius = 3;
            centerDot.style.backgroundColor = new StyleColor(new Color(0.92f, 0.92f, 0.92f, 0.95f));
            container.Add(centerDot);

            xLine = CreateLine(new Color(1f, 0.25f, 0.25f, 0.95f));
            yLine = CreateLine(new Color(0.25f, 1f, 0.35f, 0.95f));
            zLine = CreateLine(new Color(0.35f, 0.65f, 1f, 0.95f));
            xLabel = CreateLabel("X", new Color(1f, 0.35f, 0.35f, 1f));
            yLabel = CreateLabel("Y", new Color(0.35f, 1f, 0.45f, 1f));
            zLabel = CreateLabel("Z", new Color(0.45f, 0.75f, 1f, 1f));

            container.Add(xLine);
            container.Add(yLine);
            container.Add(zLine);
            container.Add(xLabel);
            container.Add(yLabel);
            container.Add(zLabel);

            axes.Clear();
            axes.Add((xLine, xLabel, new Color(1f, 0.25f, 0.25f, 0.95f), "X", Vector3.right));
            axes.Add((yLine, yLabel, new Color(0.25f, 1f, 0.35f, 0.95f), "Y", Vector3.forward));
            axes.Add((zLine, zLabel, new Color(0.35f, 0.65f, 1f, 0.95f), "Z", Vector3.up));
        }

        void Update()
        {
            if (container == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            float w = container.resolvedStyle.width;
            float h = container.resolvedStyle.height;
            if (w <= 1f || h <= 1f) return;

            Vector2 center = new Vector2(w * 0.5f, h * 0.5f);
            SetCenteredRect(centerDot, center, new Vector2(6, 6));

            var list = new List<(VisualElement line, Label label, Color color, string text, Vector3 axis, float depth)>(axes.Count);
            for (int i = 0; i < axes.Count; i++)
            {
                var a = axes[i];
                Vector3 dirCam = cam.transform.InverseTransformDirection(a.worldAxis);
                list.Add((a.line, a.label, a.color, a.text, dirCam, dirCam.z));
            }

            list.Sort((a, b) => a.depth.CompareTo(b.depth));

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                Vector2 d2 = new Vector2(item.axis.x, -item.axis.y);
                float mag = d2.magnitude;
                bool facing = mag < 1e-5f;
                if (!facing) d2 /= mag;

                float depth = Mathf.Clamp(item.depth, -1f, 1f);
                float len = facing ? 0f : axisLength * (0.78f + 0.22f * (1f + depth) * 0.5f);
                float thick = lineThickness * (0.75f + 0.25f * (1f + depth) * 0.5f);
                float alpha = 0.55f + 0.45f * (1f + depth) * 0.5f;

                Vector2 end = center + d2 * len;
                DrawLine(item.line, center, end, thick, new Color(item.color.r, item.color.g, item.color.b, alpha));
                PlaceLabel(item.label, item.text, end, center, d2, facing, new Color(item.color.r, item.color.g, item.color.b, 1f));

                item.line.BringToFront();
                item.label.BringToFront();
            }
            centerDot.BringToFront();
        }

        private VisualElement CreateLine(Color c)
        {
            var ve = new VisualElement();
            ve.style.position = Position.Absolute;
            ve.style.backgroundColor = new StyleColor(c);
            ve.pickingMode = PickingMode.Ignore;
            return ve;
        }

        private Label CreateLabel(string text, Color c)
        {
            var lb = new Label(text);
            lb.style.position = Position.Absolute;
            lb.style.unityFontStyleAndWeight = FontStyle.Bold;
            lb.style.fontSize = 14;
            lb.style.color = new StyleColor(c);
            lb.style.unityTextAlign = TextAnchor.MiddleCenter;
            lb.pickingMode = PickingMode.Ignore;
            return lb;
        }

        private void DrawLine(VisualElement ve, Vector2 from, Vector2 to, float thicknessPx, Color c)
        {
            if (ve == null) return;
            ve.style.backgroundColor = new StyleColor(c);

            Vector2 d = to - from;
            float len = d.magnitude;
            if (len < 0.5f)
            {
                ve.style.display = DisplayStyle.None;
                return;
            }
            ve.style.display = DisplayStyle.Flex;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            ve.style.width = len;
            ve.style.height = thicknessPx;
            ve.style.left = from.x;
            ve.style.top = from.y - thicknessPx * 0.5f;
            ve.style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(new Length(0f, LengthUnit.Percent), new Length(50f, LengthUnit.Percent), 0f));
            ve.style.rotate = new StyleRotate(new Rotate(new Angle(angle, AngleUnit.Degree)));
            ve.style.borderTopLeftRadius = thicknessPx * 0.5f;
            ve.style.borderTopRightRadius = thicknessPx * 0.5f;
            ve.style.borderBottomLeftRadius = thicknessPx * 0.5f;
            ve.style.borderBottomRightRadius = thicknessPx * 0.5f;
        }

        private void PlaceLabel(Label lb, string text, Vector2 end, Vector2 center, Vector2 dir2, bool facing, Color c)
        {
            if (lb == null) return;
            lb.text = text;
            lb.style.color = new StyleColor(c);
            if (facing)
            {
                SetCenteredRect(lb, center + new Vector2(0f, -14f), new Vector2(18, 18));
                return;
            }

            Vector2 dir = dir2;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.right;
            dir.Normalize();
            Vector2 p = end + dir * 10f;
            SetCenteredRect(lb, p, new Vector2(18, 18));
        }

        private void SetCenteredRect(VisualElement ve, Vector2 center, Vector2 size)
        {
            if (ve == null) return;
            ve.style.left = center.x - size.x * 0.5f;
            ve.style.top = center.y - size.y * 0.5f;
            ve.style.width = size.x;
            ve.style.height = size.y;
        }
    }
}
