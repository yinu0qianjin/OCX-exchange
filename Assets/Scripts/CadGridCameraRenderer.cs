using UnityEngine;

namespace Zhouxiangyang
{
    [RequireComponent(typeof(Camera))]
    public class CadGridCameraRenderer : MonoBehaviour
    {
        public float gridRange = 80f;
        public float minorStep = 1f;
        public int majorEvery = 10;
        public float yLevel = 0f;
        public Color minorColor = new Color(0.12f, 0.55f, 0.60f, 0.18f);
        public Color majorColor = new Color(0.22f, 0.85f, 0.90f, 0.28f);
        public Color axisXColor = new Color(0.95f, 0.25f, 0.25f, 0.55f);
        public Color axisZColor = new Color(0.25f, 0.55f, 0.95f, 0.55f);

        private Material lineMaterial;
        private Camera cam;

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (lineMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader != null)
                {
                    lineMaterial = new Material(shader);
                    lineMaterial.hideFlags = HideFlags.HideAndDontSave;
                    lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    lineMaterial.SetInt("_ZWrite", 0);
                }
            }
        }

        void OnDestroy()
        {
            if (lineMaterial != null) Destroy(lineMaterial);
        }

        void OnPostRender()
        {
            if (lineMaterial == null || cam == null) return;
            if (minorStep <= 0.01f) return;

            float range = Mathf.Max(1f, gridRange);
            int count = Mathf.FloorToInt(range / minorStep);
            if (count <= 0) return;

            Vector3 camPos = cam.transform.position;
            float originX = Mathf.Round(camPos.x / minorStep) * minorStep;
            float originZ = Mathf.Round(camPos.z / minorStep) * minorStep;

            lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);

            GL.Begin(GL.LINES);
            for (int i = -count; i <= count; i++)
            {
                bool major = majorEvery > 0 && (i % majorEvery == 0);
                Color c = major ? majorColor : minorColor;
                float x = originX + i * minorStep;
                float z = originZ + i * minorStep;

                GL.Color(c);
                GL.Vertex3(x, yLevel, originZ - range);
                GL.Vertex3(x, yLevel, originZ + range);

                GL.Vertex3(originX - range, yLevel, z);
                GL.Vertex3(originX + range, yLevel, z);
            }

            GL.Color(axisXColor);
            GL.Vertex3(originX - range, yLevel, originZ);
            GL.Vertex3(originX + range, yLevel, originZ);

            GL.Color(axisZColor);
            GL.Vertex3(originX, yLevel, originZ - range);
            GL.Vertex3(originX, yLevel, originZ + range);

            GL.End();
            GL.PopMatrix();
        }
    }
}

