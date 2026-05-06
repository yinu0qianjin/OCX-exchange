using System.Collections.Generic;
using UnityEngine;

namespace Zhouxiangyang
{
    public class SelectionHighlighter : MonoBehaviour
    {
        public Color highlightColor = Color.red;

        private VisualAnalyticsInteractor interactor;
        private readonly HashSet<GameObject> highlighted = new HashSet<GameObject>();
        private readonly Dictionary<Renderer, Color> rendererColors = new Dictionary<Renderer, Color>();
        private readonly Dictionary<LineRenderer, (Color start, Color end)> lineColors = new Dictionary<LineRenderer, (Color start, Color end)>();
        private readonly Dictionary<GameObject, GameObject> openingOutlines = new Dictionary<GameObject, GameObject>();
        private Material dashedMat;
        private MaterialPropertyBlock mpb;

        void OnEnable()
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            interactor = GetComponent<VisualAnalyticsInteractor>();
            if (interactor == null) interactor = FindAnyObjectByType<VisualAnalyticsInteractor>();
            if (interactor != null) interactor.SelectionChanged += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            if (interactor != null) interactor.SelectionChanged -= Refresh;
            ClearAll();
        }

        private void Refresh()
        {
            if (interactor == null) return;
            var selected = interactor.GetSelectedObjects();
            var next = new HashSet<GameObject>();
            for (int i = 0; i < selected.Count; i++)
            {
                var go = selected[i];
                if (go == null) continue;
                var pd = go.GetComponent<PartData>();
                if (pd == null) continue;
                string pt = pd.PartType ?? "";
                if (!string.Equals(pt, "Plate", System.StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(pt, "Panel", System.StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(pt, "Stiffener", System.StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(pt, "Opening", System.StringComparison.OrdinalIgnoreCase)) continue;
                next.Add(go);
            }

            foreach (var go in highlighted)
            {
                if (!next.Contains(go)) Restore(go);
            }
            foreach (var go in next)
            {
                if (!highlighted.Contains(go)) Apply(go);
            }
            highlighted.Clear();
            foreach (var go in next) highlighted.Add(go);
        }

        private void Apply(GameObject go)
        {
            if (go == null) return;

            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (!rendererColors.ContainsKey(r))
                {
                    mpb.Clear();
                    r.GetPropertyBlock(mpb);
                    Color baseColor = r.sharedMaterial != null ? r.sharedMaterial.color : Color.white;
                    if (mpb.HasColor("_Color")) baseColor = mpb.GetColor("_Color");
                    else if (mpb.HasColor("_BaseColor")) baseColor = mpb.GetColor("_BaseColor");
                    rendererColors[r] = baseColor;
                }

                mpb.Clear();
                r.GetPropertyBlock(mpb);
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor")) mpb.SetColor("_BaseColor", highlightColor);
                mpb.SetColor("_Color", highlightColor);
                r.SetPropertyBlock(mpb);
            }

            var lr = go.GetComponent<LineRenderer>();
            if (lr != null)
            {
                if (!lineColors.ContainsKey(lr)) lineColors[lr] = (lr.startColor, lr.endColor);
                lr.startColor = highlightColor;
                lr.endColor = highlightColor;
            }

            var pd = go.GetComponent<PartData>();
            if (pd != null && string.Equals(pd.PartType ?? "", "Opening", System.StringComparison.OrdinalIgnoreCase))
            {
                EnsureDashedResources();
                CreateOrUpdateOpeningOutline(go, pd);
            }
        }

        private void Restore(GameObject go)
        {
            if (go == null) return;

            var r = go.GetComponent<Renderer>();
            if (r != null && rendererColors.TryGetValue(r, out var c))
            {
                mpb.Clear();
                r.GetPropertyBlock(mpb);
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor")) mpb.SetColor("_BaseColor", c);
                mpb.SetColor("_Color", c);
                r.SetPropertyBlock(mpb);
                rendererColors.Remove(r);
            }

            var lr = go.GetComponent<LineRenderer>();
            if (lr != null && lineColors.TryGetValue(lr, out var lc))
            {
                lr.startColor = lc.start;
                lr.endColor = lc.end;
                lineColors.Remove(lr);
            }

            if (openingOutlines.TryGetValue(go, out var outline) && outline != null)
            {
                Destroy(outline);
            }
            openingOutlines.Remove(go);
        }

        private void ClearAll()
        {
            foreach (var go in highlighted) Restore(go);
            highlighted.Clear();
            rendererColors.Clear();
            lineColors.Clear();
            openingOutlines.Clear();
        }

        private void EnsureDashedResources()
        {
            if (dashedMat == null)
            {
                var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                dashedMat = new Material(shader);
                dashedMat.color = Color.red;
            }
        }

        private void CreateOrUpdateOpeningOutline(GameObject openingGo, PartData pd)
        {
            if (openingGo == null || pd == null) return;
            var worldLoop = pd.Boundary;
            if (worldLoop == null || worldLoop.Count < 3) return;

            if (!openingOutlines.TryGetValue(openingGo, out var outline) || outline == null)
            {
                outline = new GameObject("SelectedOpeningOutline");
                outline.transform.SetParent(openingGo.transform, false);
                openingOutlines[openingGo] = outline;
            }

            var lr = outline.GetComponent<LineRenderer>();
            if (lr == null) lr = outline.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.material = dashedMat;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.startWidth = 0.03f;
            lr.endWidth = 0.03f;
            lr.startColor = Color.red;
            lr.endColor = Color.red;

            int n = worldLoop.Count;
            lr.positionCount = n;
            for (int i = 0; i < n; i++) lr.SetPosition(i, worldLoop[i]);
        }
    }
}
