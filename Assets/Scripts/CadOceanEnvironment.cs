using UnityEngine;
using System;

namespace Zhouxiangyang
{
    public class CadOceanEnvironment : MonoBehaviour
    {
        public bool enableSkybox = false;
        public bool enableFog = false;
        public Color cameraBackground = new Color(0.05f, 0.07f, 0.09f, 1f);
        public bool verboseLogging = true;

        private Material runtimeSkybox;

        void Start()
        {
            Apply();
        }

        void OnDestroy()
        {
            if (runtimeSkybox != null) Destroy(runtimeSkybox);
        }

        public void Apply()
        {
            var cam = FindTargetCamera();
            if (cam != null)
            {
                cam.clearFlags = enableSkybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                cam.backgroundColor = cameraBackground;
                cam.allowHDR = false;
                TryDisablePostProcessing(cam);

                var oldGrid = cam.GetComponent<CadGridCameraRenderer>();
                if (oldGrid != null) oldGrid.enabled = false;

                var gridPlane = FindAnyObjectByType<CadGridPlane>();
                if (gridPlane == null)
                {
                    var go = new GameObject("CadGridPlane");
                    gridPlane = go.AddComponent<CadGridPlane>();
                }
                gridPlane.targetCamera = cam;
                gridPlane.gridRange = 120f;
                gridPlane.minorStep = 1f;
                gridPlane.majorEvery = 10;
                gridPlane.yLevel = 0f;
                gridPlane.minorColor = new Color(0.12f, 0.70f, 0.80f, 0.14f);
                gridPlane.majorColor = new Color(0.30f, 0.90f, 0.95f, 0.26f);
                gridPlane.axisXColor = new Color(0.95f, 0.25f, 0.25f, 0.55f);
                gridPlane.axisZColor = new Color(0.25f, 0.55f, 0.95f, 0.55f);

                if (verboseLogging)
                {
                    Debug.Log($"[CadEnv] camera={cam.name} clearFlags={cam.clearFlags} bg={cam.backgroundColor} HDR={cam.allowHDR}");
                }
            }

            if (enableSkybox)
            {
                if (runtimeSkybox == null)
                {
                    var shader = Shader.Find("Skybox/Procedural");
                    if (shader != null)
                    {
                        runtimeSkybox = new Material(shader);
                        runtimeSkybox.hideFlags = HideFlags.HideAndDontSave;
                    }
                }

                if (runtimeSkybox != null)
                {
                    runtimeSkybox.SetFloat("_SunSize", 0.02f);
                    runtimeSkybox.SetFloat("_SunSizeConvergence", 6f);
                    runtimeSkybox.SetFloat("_AtmosphereThickness", 1.2f);
                    runtimeSkybox.SetColor("_SkyTint", new Color(0.14f, 0.45f, 0.55f, 1f));
                    runtimeSkybox.SetColor("_GroundColor", new Color(0.02f, 0.07f, 0.10f, 1f));
                    runtimeSkybox.SetFloat("_Exposure", 0.9f);
                    RenderSettings.skybox = runtimeSkybox;
                }
            }
            else
            {
                RenderSettings.skybox = null;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.18f, 0.22f, 0.24f, 1f);
            RenderSettings.ambientIntensity = 1f;

            RenderSettings.fog = enableFog;
            if (enableFog)
            {
                RenderSettings.fogMode = FogMode.Exponential;
                RenderSettings.fogColor = new Color(0.02f, 0.08f, 0.10f, 1f);
                RenderSettings.fogDensity = 0.006f;
            }
            if (verboseLogging)
            {
                Debug.Log($"[CadEnv] skybox={(RenderSettings.skybox != null ? RenderSettings.skybox.shader.name : "null")} fog={RenderSettings.fog} ambient={RenderSettings.ambientLight}");
            }
        }

        private Camera FindTargetCamera()
        {
            var main = Camera.main;
            if (main != null) return main;

            var controller = FindAnyObjectByType<CameraController>();
            if (controller != null)
            {
                var cam = controller.GetComponent<Camera>();
                if (cam != null) return cam;
            }

            var any = FindAnyObjectByType<Camera>();
            return any;
        }

        private void TryDisablePostProcessing(Camera cam)
        {
            if (cam == null) return;

            var urpType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (urpType != null)
            {
                var comp = cam.GetComponent(urpType);
                if (comp != null)
                {
                    var prop = urpType.GetProperty("renderPostProcessing");
                    if (prop != null && prop.CanWrite) prop.SetValue(comp, false);
                }
            }

            var ppLayerType = Type.GetType("UnityEngine.Rendering.PostProcessing.PostProcessLayer, Unity.Postprocessing.Runtime");
            if (ppLayerType != null)
            {
                var comp = cam.GetComponent(ppLayerType) as Behaviour;
                if (comp != null) comp.enabled = false;
            }

            int disabledVolumes = 0;
            var volumeType = Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
            if (volumeType != null)
            {
                var volumes = FindObjectsByType(volumeType, FindObjectsSortMode.None);
                foreach (var v in volumes)
                {
                    if (v is Behaviour b && b.enabled)
                    {
                        b.enabled = false;
                        disabledVolumes++;
                    }
                }
            }

            var ppVolumeType = Type.GetType("UnityEngine.Rendering.PostProcessing.PostProcessVolume, Unity.Postprocessing.Runtime");
            if (ppVolumeType != null)
            {
                var volumes = FindObjectsByType(ppVolumeType, FindObjectsSortMode.None);
                foreach (var v in volumes)
                {
                    if (v is Behaviour b && b.enabled)
                    {
                        b.enabled = false;
                        disabledVolumes++;
                    }
                }
            }

            if (verboseLogging)
            {
                Debug.Log($"[CadEnv] postProcessingDisabled volumesDisabled={disabledVolumes}");
            }
        }
    }
}
