using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Zhouxiangyang
{
    public class CadGridPlane : MonoBehaviour
    {
        public Camera targetCamera;
        public float gridRange = 120f;
        public float minorStep = 1f;
        public int majorEvery = 10;
        public float yLevel = 0f;

        public Color minorColor = new Color(0.10f, 0.50f, 0.60f, 0.15f);
        public Color majorColor = new Color(0.30f, 0.90f, 0.95f, 0.25f);
        public Color axisXColor = new Color(0.95f, 0.25f, 0.25f, 0.55f);
        public Color axisZColor = new Color(0.25f, 0.55f, 0.95f, 0.55f);

        private MeshFilter minorFilter;
        private MeshFilter majorFilter;
        private MeshFilter axisFilter;
        private MeshRenderer minorRenderer;
        private MeshRenderer majorRenderer;
        private MeshRenderer axisRenderer;

        private float lastRange;
        private float lastMinorStep;
        private int lastMajorEvery;

        void Awake()
        {
            EnsureChildren();
            RebuildIfNeeded(force: true);
        }

        void LateUpdate()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;
            if (minorStep <= 0.01f) return;

            RebuildIfNeeded(force: false);

            Vector3 camPos = targetCamera.transform.position;
            float ox = Mathf.Round(camPos.x / minorStep) * minorStep;
            float oz = Mathf.Round(camPos.z / minorStep) * minorStep;
            transform.position = new Vector3(ox, yLevel, oz);
        }

        private void RebuildIfNeeded(bool force)
        {
            if (!force && Mathf.Approximately(lastRange, gridRange) && Mathf.Approximately(lastMinorStep, minorStep) && lastMajorEvery == majorEvery) return;
            lastRange = gridRange;
            lastMinorStep = minorStep;
            lastMajorEvery = majorEvery;

            EnsureChildren();
            ApplyMaterial(minorRenderer, minorColor);
            ApplyMaterial(majorRenderer, majorColor);
            ApplyMaterial(axisRenderer, Color.white);

            minorFilter.sharedMesh = BuildGridMesh(includeMajor: false);
            majorFilter.sharedMesh = BuildGridMesh(includeMajor: true);
            axisFilter.sharedMesh = BuildAxisMesh();

            if (axisRenderer != null && axisRenderer.sharedMaterial != null)
            {
                axisRenderer.sharedMaterial.color = Color.white;
            }
        }

        private void EnsureChildren()
        {
            if (minorFilter == null) CreateLayer("Minor", out minorFilter, out minorRenderer);
            if (majorFilter == null) CreateLayer("Major", out majorFilter, out majorRenderer);
            if (axisFilter == null) CreateLayer("Axis", out axisFilter, out axisRenderer);
        }

        private void CreateLayer(string name, out MeshFilter filter, out MeshRenderer renderer)
        {
            Transform child = transform.Find(name);
            GameObject go;
            if (child == null)
            {
                go = new GameObject(name);
                go.transform.SetParent(transform, false);
            }
            else
            {
                go = child.gameObject;
            }

            filter = go.GetComponent<MeshFilter>();
            if (filter == null) filter = go.AddComponent<MeshFilter>();

            renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = go.AddComponent<MeshRenderer>();

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void ApplyMaterial(MeshRenderer r, Color c)
        {
            if (r == null) return;
            if (r.sharedMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Unlit/Transparent");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (shader == null) return;
                r.sharedMaterial = new Material(shader);
            }
            r.sharedMaterial.color = c;
        }

        private Mesh BuildGridMesh(bool includeMajor)
        {
            float range = Mathf.Max(1f, gridRange);
            int count = Mathf.FloorToInt(range / minorStep);
            var vertices = new List<Vector3>(count * 8);
            var indices = new List<int>(count * 8);

            int v = 0;
            for (int i = -count; i <= count; i++)
            {
                bool major = majorEvery > 0 && (i % majorEvery == 0);
                if (major != includeMajor) continue;

                float t = i * minorStep;
                vertices.Add(new Vector3(t, 0f, -range));
                vertices.Add(new Vector3(t, 0f, range));
                indices.Add(v++);
                indices.Add(v++);

                vertices.Add(new Vector3(-range, 0f, t));
                vertices.Add(new Vector3(range, 0f, t));
                indices.Add(v++);
                indices.Add(v++);
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh BuildAxisMesh()
        {
            float range = Mathf.Max(1f, gridRange);
            var vertices = new List<Vector3>(8);
            var indices = new List<int>(8);
            int v = 0;

            vertices.Add(new Vector3(-range, 0f, 0f));
            vertices.Add(new Vector3(range, 0f, 0f));
            indices.Add(v++); indices.Add(v++);

            vertices.Add(new Vector3(0f, 0f, -range));
            vertices.Add(new Vector3(0f, 0f, range));
            indices.Add(v++); indices.Add(v++);

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

