using System;
using System.Xml.Linq;
using System.Xml;
using System.IO;
using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine.Rendering;

namespace Zhouxiangyang
{
    public partial class OcxSystemManager : MonoBehaviour
    {
        [Header("渲染材质设置")]
        public Material plateMaterial;      // 板材材质
        public Material stiffenerMaterial;  // 加劲材材质
        public Material lineMaterial;       // 线框材质（建议使用纯色材质，便于绘制轮廓）
        public bool drawOpeningOutlines = false;

        public bool useMeshColliderForPlates = false;
        public float minPlateColliderSize = 0.12f;
        public float modelVisualScale = 8f;
        public static float ModelVisualScale { get; private set; } = 1f;
        public bool drawPlateOutlineEdges = true;
        public float plateOutlineWidth = 0.0030f;
        public bool drawPlateSeams = true;
        public float seamLineWidth = 0.0020f;

        private Transform projectsRoot;
        private Transform rootContainer;
        private Dictionary<string, Color> materialColors = new Dictionary<string, Color>();
        private MaterialPropertyBlock mpb;
        private Material s0FillMaterial;
        private Material plateOutlineMaterial;
        private Material seamLineMaterial;
        private bool cadAlignmentInitialized;
        private Quaternion cadAlignmentRotation = Quaternion.identity;
        private Vector3 cadAlignmentOffset = Vector3.zero;
        public bool enableUndo = true;
        public int undoMaxSteps = 30;
        public float undoIdleSeconds = 0.25f;
        public bool overwriteOriginalOnExport = true;
        private bool undoSessionActive;
        private float undoLastChangeTime;
        private int undoLastHash;
        private Snapshot undoSessionStart;
        private List<Snapshot> undoStack = new List<Snapshot>();
        private bool applyingUndo;

        public Transform RootContainer => rootContainer;
        public Transform LastFileGroup { get; private set; }
        public event Action ModelsChanged;
        public event Action ProjectsChanged;
        public bool verboseAutoFocusLogging = true;
        public bool showS0DebugEdges = false;

        public struct ProjectInfo
        {
            public string Id;
            public string Name;
        }

        private class ProjectContext
        {
            public string Id;
            public string Name;
            public Transform Root;
            public List<LoadedDocument> LoadedDocuments = new List<LoadedDocument>();
            public bool CadAlignmentInitialized;
            public Quaternion CadAlignmentRotation = Quaternion.identity;
            public Vector3 CadAlignmentOffset = Vector3.zero;
            public bool UndoSessionActive;
            public float UndoLastChangeTime;
            public int UndoLastHash;
            public Snapshot UndoSessionStart;
            public List<Snapshot> UndoStack = new List<Snapshot>();
        }

        private readonly List<ProjectContext> projects = new List<ProjectContext>();
        private ProjectContext activeProject;
        public string ActiveProjectId => activeProject != null ? activeProject.Id : "";

        private class LoadedDocument
        {
            public string SourcePath;
            public Encoding SourceEncoding;
            public Transform FileGroup;
            public XDocument Doc;
            public XNamespace Ocx;
            public Dictionary<string, XElement> PlatesById = new Dictionary<string, XElement>();
            public Dictionary<string, XElement> StiffenersById = new Dictionary<string, XElement>();
        }

        private List<LoadedDocument> loadedDocuments = new List<LoadedDocument>();

        private class PartSnapshot
        {
            public string Key;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            public string MaterialRef;
            public string Thickness;
            public string SectionRef;
            public string EndCutCode;
            public float Weight;
        }

        private class Snapshot
        {
            public Dictionary<string, PartSnapshot> PartsByKey = new Dictionary<string, PartSnapshot>();
        }

        [Serializable]
        public class OcxProjectPartState
        {
            public string Key;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            public string MaterialRef;
            public string Thickness;
            public string SectionRef;
            public string EndCutCode;
            public float Weight;
        }

        [Serializable]
        public class OcxProjectFile
        {
            public int Version = 1;
            public string CreatedAt;
            public bool OverwriteOriginalOnExport;
            public List<string> SourceFiles = new List<string>();
            public List<OcxProjectPartState> Parts = new List<OcxProjectPartState>();
        }

        void Awake()
        {
            ModelVisualScale = Mathf.Max(1f, modelVisualScale);
            GameObject projectsObj = new GameObject("All_Imported_Projects");
            projectsRoot = projectsObj.transform;
            CreateAndActivateProject("项目1");
            mpb = new MaterialPropertyBlock();

            // 兜底：如果没有提供线框材质，自动创建一个默认材质
            if (lineMaterial == null)
            {
                lineMaterial = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));
                lineMaterial.color = Color.cyan;
            }

            if (plateMaterial == null)
            {
                plateMaterial = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default"));
                plateMaterial.color = new Color(0.14f, 0.18f, 0.20f, 1f);
            }

            if (stiffenerMaterial == null)
            {
                stiffenerMaterial = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default"));
                stiffenerMaterial.color = new Color(0.16f, 0.20f, 0.22f, 1f);
            }

            if (s0FillMaterial == null)
            {
                s0FillMaterial = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));
                s0FillMaterial.color = new Color(0.10f, 0.55f, 0.95f, 0.25f);
            }

            ForceUnlitMaterial(plateMaterial);
            ForceUnlitMaterial(stiffenerMaterial);
            ForceUnlitMaterial(lineMaterial);

            if (plateOutlineMaterial == null)
            {
                plateOutlineMaterial = new Material(lineMaterial);
                plateOutlineMaterial.color = Color.white;
                ForceUnlitMaterial(plateOutlineMaterial);
            }
            if (seamLineMaterial == null)
            {
                seamLineMaterial = new Material(lineMaterial);
                seamLineMaterial.color = Color.black;
                ForceUnlitMaterial(seamLineMaterial);
            }

            ApplyHighQualitySettings();
        }

        public IReadOnlyList<ProjectInfo> GetProjects()
        {
            var list = new List<ProjectInfo>(projects.Count);
            for (int i = 0; i < projects.Count; i++)
            {
                var p = projects[i];
                if (p == null) continue;
                list.Add(new ProjectInfo { Id = p.Id, Name = p.Name });
            }
            return list;
        }

        public void CreateNewProject()
        {
            int index = projects.Count + 1;
            CreateAndActivateProject("项目" + index);
            ProjectsChanged?.Invoke();
            ModelsChanged?.Invoke();
        }

        public bool SwitchProject(string projectId)
        {
            if (string.IsNullOrEmpty(projectId)) return false;
            if (activeProject != null && activeProject.Id == projectId) return true;

            var target = projects.FirstOrDefault(p => p != null && p.Id == projectId);
            if (target == null) return false;

            ExitSectionView();
            SaveActiveProjectState();
            ActivateProject(target);
            ProjectsChanged?.Invoke();
            ModelsChanged?.Invoke();
            return true;
        }

        public bool CloseProject(string projectId)
        {
            if (string.IsNullOrEmpty(projectId)) return false;
            int idx = -1;
            for (int i = 0; i < projects.Count; i++)
            {
                if (projects[i] != null && projects[i].Id == projectId) { idx = i; break; }
            }
            if (idx < 0) return false;

            if (projects.Count <= 1)
            {
                ExitSectionView();
                ClearAllModels();
                RenameActiveProject("项目1");
                ProjectsChanged?.Invoke();
                ModelsChanged?.Invoke();
                return true;
            }

            var closing = projects[idx];
            bool wasActive = closing == activeProject;

            ExitSectionView();
            SaveActiveProjectState();

            if (wasActive)
            {
                int nextIdx = idx >= projects.Count - 1 ? idx - 1 : idx + 1;
                nextIdx = Mathf.Clamp(nextIdx, 0, projects.Count - 1);
                var next = projects[nextIdx];
                if (next != null && next != closing) ActivateProject(next);
            }

            if (closing != null && closing.Root != null) Destroy(closing.Root.gameObject);
            projects.RemoveAt(idx);

            ProjectsChanged?.Invoke();
            ModelsChanged?.Invoke();
            return true;
        }

        public void RenameActiveProject(string name)
        {
            if (activeProject == null) return;
            string nm = string.IsNullOrWhiteSpace(name) ? activeProject.Name : name.Trim();
            if (nm == activeProject.Name) return;
            activeProject.Name = nm;
            if (activeProject.Root != null) activeProject.Root.gameObject.name = nm;
            ProjectsChanged?.Invoke();
        }

        private void MaybeRenameActiveProjectFromFile(string fileName)
        {
            if (activeProject == null) return;
            if (rootContainer == null) return;
            if (rootContainer.childCount > 1) return;
            if (string.IsNullOrWhiteSpace(fileName)) return;
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(baseName)) return;
            if (!activeProject.Name.StartsWith("项目", StringComparison.OrdinalIgnoreCase)) return;
            RenameActiveProject(baseName);
        }

        private void CreateAndActivateProject(string name)
        {
            var ctx = new ProjectContext
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = string.IsNullOrWhiteSpace(name) ? "项目" : name.Trim(),
                Root = new GameObject("ProjectRoot").transform
            };
            if (projectsRoot != null) ctx.Root.SetParent(projectsRoot, false);
            ctx.Root.gameObject.name = ctx.Name;
            projects.Add(ctx);

            SaveActiveProjectState();
            ActivateProject(ctx);
        }

        private void SaveActiveProjectState()
        {
            if (activeProject == null) return;
            activeProject.Root = rootContainer;
            activeProject.LoadedDocuments = loadedDocuments;
            activeProject.CadAlignmentInitialized = cadAlignmentInitialized;
            activeProject.CadAlignmentRotation = cadAlignmentRotation;
            activeProject.CadAlignmentOffset = cadAlignmentOffset;
            activeProject.UndoSessionActive = undoSessionActive;
            activeProject.UndoLastChangeTime = undoLastChangeTime;
            activeProject.UndoLastHash = undoLastHash;
            activeProject.UndoSessionStart = undoSessionStart;
            activeProject.UndoStack = undoStack;
        }

        private void ActivateProject(ProjectContext ctx)
        {
            if (ctx == null) return;
            if (rootContainer != null) rootContainer.gameObject.SetActive(false);

            activeProject = ctx;
            rootContainer = ctx.Root;
            if (rootContainer != null) rootContainer.gameObject.SetActive(true);

            loadedDocuments = ctx.LoadedDocuments ?? new List<LoadedDocument>();
            ctx.LoadedDocuments = loadedDocuments;

            cadAlignmentInitialized = ctx.CadAlignmentInitialized;
            cadAlignmentRotation = ctx.CadAlignmentRotation;
            cadAlignmentOffset = ctx.CadAlignmentOffset;

            undoSessionActive = ctx.UndoSessionActive;
            undoLastChangeTime = ctx.UndoLastChangeTime;
            undoLastHash = ctx.UndoLastHash;
            undoSessionStart = ctx.UndoSessionStart;
            undoStack = ctx.UndoStack ?? new List<Snapshot>();
            ctx.UndoStack = undoStack;
        }

        private void ForceUnlitMaterial(Material m)
        {
            if (m == null) return;
            var targetShader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            if (targetShader != null && m.shader != targetShader) m.shader = targetShader;
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
        }

        private void ApplyHighQualitySettings()
        {
            QualitySettings.globalTextureMipmapLimit = 0;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.lodBias = Mathf.Max(QualitySettings.lodBias, 2.0f);
            QualitySettings.antiAliasing = Mathf.Max(QualitySettings.antiAliasing, 8);
            QualitySettings.shadowCascades = Mathf.Max(QualitySettings.shadowCascades, 4);
            QualitySettings.shadowDistance = Mathf.Max(QualitySettings.shadowDistance, 250f);
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;

            var cam = Camera.main;
            if (cam == null)
            {
                var ctrl = FindAnyObjectByType<CameraController>();
                if (ctrl != null) cam = ctrl.GetComponent<Camera>();
            }
            if (cam != null)
            {
                cam.allowHDR = true;
                cam.allowMSAA = true;
                cam.useOcclusionCulling = true;
                TryEnableUrpAntialiasing(cam);
            }
        }

        private void TryEnableUrpAntialiasing(Camera cam)
        {
            if (cam == null) return;

            var urpType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (urpType == null) return;

            var comp = cam.GetComponent(urpType);
            if (comp == null) return;

            var aaProp = urpType.GetProperty("antialiasing", BindingFlags.Instance | BindingFlags.Public);
            if (aaProp != null && aaProp.CanWrite)
            {
                var aaEnumType = aaProp.PropertyType;
                object aaValue = null;
                aaValue = TryParseEnum(aaEnumType, "SubpixelMorphologicalAntialiasing")
                          ?? TryParseEnum(aaEnumType, "SubpixelMorphologicalAntiAliasing")
                          ?? TryParseEnum(aaEnumType, "SMAA")
                          ?? TryParseEnum(aaEnumType, "FastApproximateAntialiasing")
                          ?? TryParseEnum(aaEnumType, "FastApproximateAntiAliasing")
                          ?? TryParseEnum(aaEnumType, "FXAA");

                if (aaValue != null) aaProp.SetValue(comp, aaValue);
            }

            var aaQProp = urpType.GetProperty("antialiasingQuality", BindingFlags.Instance | BindingFlags.Public);
            if (aaQProp != null && aaQProp.CanWrite)
            {
                var qEnumType = aaQProp.PropertyType;
                object qValue = TryParseEnum(qEnumType, "High") ?? TryParseEnum(qEnumType, "Medium");
                if (qValue != null) aaQProp.SetValue(comp, qValue);
            }
        }

        private object TryParseEnum(Type enumType, string name)
        {
            if (enumType == null || !enumType.IsEnum) return null;
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                return Enum.Parse(enumType, name, ignoreCase: true);
            }
            catch
            {
                return null;
            }
        }

        public void ClearAllModels()
        {
            foreach (Transform child in rootContainer)
            {
                Destroy(child.gameObject);
            }
            rootContainer.DetachChildren();
            loadedDocuments.Clear();
            cadAlignmentInitialized = false;
            cadAlignmentRotation = Quaternion.identity;
            cadAlignmentOffset = Vector3.zero;
            ModelsChanged?.Invoke();
        }

        public bool TryRefreshWeightFromSource(PartData pd)
        {
            if (pd == null) return false;
            if (string.IsNullOrEmpty(pd.SourceFilePath)) return false;
            if (loadedDocuments == null || loadedDocuments.Count == 0) return false;

            LoadedDocument loaded = null;
            for (int i = 0; i < loadedDocuments.Count; i++)
            {
                var ld = loadedDocuments[i];
                if (ld == null || string.IsNullOrEmpty(ld.SourcePath)) continue;
                if (string.Equals(ld.SourcePath, pd.SourceFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    loaded = ld;
                    break;
                }
            }
            if (loaded == null) return false;

            XElement el = null;
            if (string.Equals(pd.SourceElementType ?? "", "Plate", StringComparison.OrdinalIgnoreCase))
            {
                loaded.PlatesById.TryGetValue(pd.PartId ?? "", out el);
            }
            else if (string.Equals(pd.SourceElementType ?? "", "Stiffener", StringComparison.OrdinalIgnoreCase))
            {
                loaded.StiffenersById.TryGetValue(pd.PartId ?? "", out el);
            }
            if (el == null) return false;

            float w = ExtractDryWeightKg(el, loaded.Ocx);
            if (w <= 0f) return false;
            if (Mathf.Abs(pd.Weight - w) > 1e-3f) pd.Weight = w;
            return true;
        }

        public void LoadAndBuild(string absolutePath, bool appendMode = true)
        {
            if (!File.Exists(absolutePath)) return;
            if (!appendMode) ClearAllModels();

            string fileName = Path.GetFileName(absolutePath);
            GameObject fileGroup = new GameObject($"FileGroup_{fileName}");
            fileGroup.transform.SetParent(rootContainer);
            LastFileGroup = fileGroup.transform;
            MaybeRenameActiveProjectFromFile(fileName);

            var encoding = DetectEncoding(absolutePath);
            XDocument doc = XDocument.Load(absolutePath, LoadOptions.PreserveWhitespace);
            XNamespace ocx = "http://data.dnvgl.com/Schemas/ocxXMLSchema";

            BuildMaterialColors(doc, ocx);

            var loaded = new LoadedDocument
            {
                SourcePath = absolutePath,
                SourceEncoding = encoding,
                FileGroup = fileGroup.transform,
                Doc = doc,
                Ocx = ocx
            };
            foreach (var plate in doc.Descendants(ocx + "Plate"))
            {
                string id = plate.Attribute("id")?.Value;
                if (!string.IsNullOrEmpty(id)) loaded.PlatesById[id] = plate;
            }
            foreach (var stiffener in doc.Descendants(ocx + "Stiffener"))
            {
                string id = stiffener.Attribute("id")?.Value;
                if (!string.IsNullOrEmpty(id)) loaded.StiffenersById[id] = stiffener;
            }
            loadedDocuments.Add(loaded);

            var groupCache = new Dictionary<string, Transform>();

            bool hasPlates = doc.Descendants(ocx + "Plate").Any();
            bool hasPanels = doc.Descendants(ocx + "Panel").Any();

            if (!hasPlates && hasPanels)
            {
                BuildFromS0(doc, ocx, absolutePath, fileGroup.transform, groupCache);
            }
            else
            {
                bool isS2 = doc.Descendants(ocx + "EndCutEnd1").Any()
                            || doc.Descendants(ocx + "EndcutParameters").Any()
                            || doc.Descendants(ocx + "OpeningParameter").Any();
                if (isS2) BuildFromS2(doc, ocx, absolutePath, fileGroup.transform, groupCache);
                else BuildFromS1(doc, ocx, absolutePath, fileGroup.transform, groupCache);
            }

            AlignGroupToCadGrid(fileGroup.transform);
            AutoFocusCameraOn(fileGroup.transform);
            ModelsChanged?.Invoke();
            StartCoroutine(DelayedAutoFocus(fileGroup.transform));
        }

        public bool TryLoadAndBuild(string absolutePath, bool appendMode, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                error = "文件路径为空";
                return false;
            }
            if (!File.Exists(absolutePath))
            {
                error = "文件不存在";
                return false;
            }

            try
            {
                if (!appendMode) ClearAllModels();

                string fileName = Path.GetFileName(absolutePath);
                var encoding = DetectEncoding(absolutePath);
                XDocument doc = XDocument.Load(absolutePath, LoadOptions.PreserveWhitespace);
                XNamespace ocx = "http://data.dnvgl.com/Schemas/ocxXMLSchema";

                GameObject fileGroup = new GameObject($"FileGroup_{fileName}");
                fileGroup.transform.SetParent(rootContainer);
                LastFileGroup = fileGroup.transform;
                MaybeRenameActiveProjectFromFile(fileName);

                BuildMaterialColors(doc, ocx);

                var loaded = new LoadedDocument
                {
                    SourcePath = absolutePath,
                    SourceEncoding = encoding,
                    FileGroup = fileGroup.transform,
                    Doc = doc,
                    Ocx = ocx
                };
                foreach (var plate in doc.Descendants(ocx + "Plate"))
                {
                    string id = plate.Attribute("id")?.Value;
                    if (!string.IsNullOrEmpty(id)) loaded.PlatesById[id] = plate;
                }
                foreach (var stiffener in doc.Descendants(ocx + "Stiffener"))
                {
                    string id = stiffener.Attribute("id")?.Value;
                    if (!string.IsNullOrEmpty(id)) loaded.StiffenersById[id] = stiffener;
                }
                loadedDocuments.Add(loaded);

                var groupCache = new Dictionary<string, Transform>();

                bool hasPlates = doc.Descendants(ocx + "Plate").Any();
                bool hasPanels = doc.Descendants(ocx + "Panel").Any();

                if (!hasPlates && hasPanels)
                {
                    BuildFromS0(doc, ocx, absolutePath, fileGroup.transform, groupCache);
                }
                else
                {
                    bool isS2 = doc.Descendants(ocx + "EndCutEnd1").Any()
                                || doc.Descendants(ocx + "EndcutParameters").Any()
                                || doc.Descendants(ocx + "OpeningParameter").Any();
                    if (isS2) BuildFromS2(doc, ocx, absolutePath, fileGroup.transform, groupCache);
                    else BuildFromS1(doc, ocx, absolutePath, fileGroup.transform, groupCache);
                }

                AlignGroupToCadGrid(fileGroup.transform);
                AutoFocusCameraOn(fileGroup.transform);
                ModelsChanged?.Invoke();
                StartCoroutine(DelayedAutoFocus(fileGroup.transform));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        void Update()
        {
            UpdateSectionCullingIfNeeded();
            if (!enableUndo || applyingUndo) return;
            if (rootContainer == null) return;

            int h = ComputeStateHash();
            if (h != undoLastHash)
            {
                if (!undoSessionActive)
                {
                    undoSessionStart = CaptureSnapshot();
                    undoSessionActive = true;
                }
                undoLastChangeTime = Time.unscaledTime;
                undoLastHash = h;
            }
            else if (undoSessionActive)
            {
                if (Time.unscaledTime - undoLastChangeTime >= undoIdleSeconds)
                {
                    PushUndoSnapshot(undoSessionStart);
                    undoSessionStart = null;
                    undoSessionActive = false;
                }
            }
        }

        public bool UndoLast()
        {
            if (!enableUndo) return false;
            if (undoSessionActive && undoSessionStart != null)
            {
                applyingUndo = true;
                try
                {
                    RestoreSnapshot(undoSessionStart);
                    undoLastHash = ComputeStateHash();
                    undoSessionActive = false;
                    undoSessionStart = null;
                    return true;
                }
                finally
                {
                    applyingUndo = false;
                }
            }
            if (undoStack.Count == 0) return false;

            applyingUndo = true;
            try
            {
                var snap = undoStack[undoStack.Count - 1];
                undoStack.RemoveAt(undoStack.Count - 1);
                RestoreSnapshot(snap);
                undoLastHash = ComputeStateHash();
                undoSessionActive = false;
                undoSessionStart = null;
                return true;
            }
            finally
            {
                applyingUndo = false;
            }
        }

        private void PushUndoSnapshot(Snapshot snap)
        {
            if (snap == null) return;
            undoStack.Add(snap);
            int max = Mathf.Max(1, undoMaxSteps);
            while (undoStack.Count > max) undoStack.RemoveAt(0);
        }

        private int ComputeStateHash()
        {
            unchecked
            {
                int hash = 17;
                var parts = rootContainer.GetComponentsInChildren<PartData>(true);
                for (int i = 0; i < parts.Length; i++)
                {
                    var pd = parts[i];
                    if (pd == null) continue;
                    string key = BuildPartKey(pd);
                    var t = pd.transform;
                    hash = hash * 31 + (key?.GetHashCode() ?? 0);
                    hash = hash * 31 + t.position.GetHashCode();
                    hash = hash * 31 + t.rotation.GetHashCode();
                    hash = hash * 31 + t.localScale.GetHashCode();
                    hash = hash * 31 + (pd.MaterialRef?.GetHashCode() ?? 0);
                    hash = hash * 31 + (pd.Thickness?.GetHashCode() ?? 0);
                    hash = hash * 31 + (pd.SectionRef?.GetHashCode() ?? 0);
                    hash = hash * 31 + (pd.EndCutCode?.GetHashCode() ?? 0);
                    hash = hash * 31 + pd.Weight.GetHashCode();
                }
                return hash;
            }
        }

        private Snapshot CaptureSnapshot()
        {
            var snap = new Snapshot();
            var parts = rootContainer.GetComponentsInChildren<PartData>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                var pd = parts[i];
                if (pd == null) continue;
                string key = BuildPartKey(pd);
                if (string.IsNullOrEmpty(key)) continue;
                var t = pd.transform;
                snap.PartsByKey[key] = new PartSnapshot
                {
                    Key = key,
                    Position = t.position,
                    Rotation = t.rotation,
                    Scale = t.localScale,
                    MaterialRef = pd.MaterialRef,
                    Thickness = pd.Thickness,
                    SectionRef = pd.SectionRef,
                    EndCutCode = pd.EndCutCode,
                    Weight = pd.Weight
                };
            }
            return snap;
        }

        private void RestoreSnapshot(Snapshot snap)
        {
            if (snap == null) return;
            var parts = rootContainer.GetComponentsInChildren<PartData>(true);
            var map = new Dictionary<string, PartData>();
            for (int i = 0; i < parts.Length; i++)
            {
                var pd = parts[i];
                if (pd == null) continue;
                string key = BuildPartKey(pd);
                if (string.IsNullOrEmpty(key)) continue;
                if (!map.ContainsKey(key)) map[key] = pd;
            }

            foreach (var kv in snap.PartsByKey)
            {
                if (!map.TryGetValue(kv.Key, out var pd) || pd == null) continue;
                var s = kv.Value;
                var t = pd.transform;
                t.position = s.Position;
                t.rotation = s.Rotation;
                t.localScale = s.Scale;
                pd.MaterialRef = s.MaterialRef;
                pd.Thickness = s.Thickness;
                pd.SectionRef = s.SectionRef;
                pd.EndCutCode = s.EndCutCode;
                pd.Weight = s.Weight;
                ApplyMaterialToObject(pd.gameObject, pd.MaterialRef);
            }

            ModelsChanged?.Invoke();
        }

        private string BuildPartKey(PartData pd)
        {
            if (pd == null) return "";
            string src = pd.SourceFilePath ?? "";
            string id = pd.PartId ?? "";
            string type = pd.SourceElementType ?? pd.PartType ?? "";
            return src + "|" + type + "|" + id;
        }

        private IEnumerator DelayedAutoFocus(Transform group)
        {
            yield return null;
            AutoFocusCameraOn(group);
        }

        private void BuildFromS0(XDocument doc, XNamespace ocx, string sourcePath, Transform fileGroup, Dictionary<string, Transform> groupCache)
        {
            var xPlanes = new Dictionary<string, float>();
            var yPlanes = new Dictionary<string, float>();
            var zPlanes = new Dictionary<string, float>();

            var coord = doc.Descendants(ocx + "CoordinateSystem").FirstOrDefault(cs => (cs.Attribute("isGlobal")?.Value ?? "").ToLower() == "true")
                        ?? doc.Descendants(ocx + "CoordinateSystem").FirstOrDefault();
            var frameTables = coord?.Element(ocx + "FrameTables");
            if (frameTables != null)
            {
                foreach (var rp in frameTables.Descendants(ocx + "RefPlane"))
                {
                    string id = rp.Attribute("id")?.Value;
                    string nm = rp.Attribute("name")?.Value ?? "";
                    float.TryParse(rp.Element(ocx + "ReferenceLocation")?.Attribute("numericvalue")?.Value ?? "0",
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float v);
                    if (string.IsNullOrEmpty(id)) continue;
                    v *= ModelVisualScale;

                    char axis = '\0';
                    if (!string.IsNullOrEmpty(nm))
                    {
                        char c = char.ToUpperInvariant(nm[0]);
                        if (c == 'X' || c == 'Y' || c == 'Z') axis = c;
                    }
                    if (axis == '\0')
                    {
                        string parentName = rp.Parent?.Name.LocalName ?? "";
                        string grandName = rp.Parent?.Parent?.Name.LocalName ?? "";
                        string containerName = parentName + "|" + grandName;
                        if (containerName.Contains("XRefPlanes")) axis = 'X';
                        else if (containerName.Contains("YRefPlanes")) axis = 'Y';
                        else if (containerName.Contains("ZRefPlanes")) axis = 'Z';
                    }

                    if (axis == 'X') xPlanes[id] = v;
                    else if (axis == 'Y') yPlanes[id] = v;
                    else if (axis == 'Z') zPlanes[id] = v;
                }
            }

            foreach (var panel in doc.Descendants(ocx + "Panel"))
            {
                string panelId = panel.Attribute("id")?.Value;
                string guidRef = panel.Attribute(ocx + "GUIDRef")?.Value;
                string name =
                    panel.Attribute("name")?.Value
                    ?? panel.Element(ocx + "Description")?.Value
                    ?? panel.Elements().FirstOrDefault(e => e.Name.LocalName == "Description")?.Value
                    ?? panelId
                    ?? "Panel";

                var unboundedRef = panel.Element(ocx + "UnboundedGeometry")?.Element(ocx + "GridRef")?.Attribute("refid")?.Value;
                if (string.IsNullOrEmpty(unboundedRef)) continue;

                float? planeX = xPlanes.TryGetValue(unboundedRef, out var xv) ? xv : null;
                float? planeY = yPlanes.TryGetValue(unboundedRef, out var yv) ? yv : null;
                float? planeZ = zPlanes.TryGetValue(unboundedRef, out var zv) ? zv : null;

                var limitedBy = panel.Element(ocx + "LimitedBy");
                var limitRefs = new List<string>();
                if (limitedBy != null)
                {
                    foreach (var e in limitedBy.Elements())
                    {
                        if (e.Name.LocalName != "GridRef" && e.Name.LocalName != "shellline") continue;
                        string rid = e.Attribute("refid")?.Value;
                        if (!string.IsNullOrEmpty(rid)) limitRefs.Add(rid);
                    }
                }

                var xVals = limitRefs.Where(id => xPlanes.ContainsKey(id)).Select(id => xPlanes[id]).ToList();
                var yVals = limitRefs.Where(id => yPlanes.ContainsKey(id)).Select(id => yPlanes[id]).ToList();
                var zVals = limitRefs.Where(id => zPlanes.ContainsKey(id)).Select(id => zPlanes[id]).ToList();

                List<Vector3> boundaryWorld = null;
                Vector3 normalWorld = Vector3.up;

                float defaultExtent = 10f * ModelVisualScale;
                bool hasShellLine = limitedBy != null && limitedBy.Elements().Any(e => e.Name.LocalName == "shellline");
                var curvePolyline = limitedBy != null ? TryExtractLimitedByCurvePolyline(limitedBy, ocx) : null;
                if (planeY.HasValue)
                {
                    float shipY = planeY.Value;
                    normalWorld = Vector3.forward;

                    if (curvePolyline != null && curvePolyline.Count >= 2 && xVals.Count >= 2 && zVals.Count >= 1)
                    {
                        float xMin = Mathf.Min(xVals[0], xVals[1]);
                        float xMax = Mathf.Max(xVals[0], xVals[1]);
                        float shipZ = zVals[0];
                        boundaryWorld = BuildBoundaryFromCurveOnYPlane(shipY, shipZ, xMin, xMax, curvePolyline);
                        bool isFilled = true;
                        var panelsGroup = GetOrCreateGroup(fileGroup, groupCache, name + "/Panels");
                        var panelGo = BuildS0Panel(panelId, name, guidRef, boundaryWorld, normalWorld, isFilled, panelsGroup, sourcePath);
                        var curveProjected = ProjectCurveToYPlane(shipY, curvePolyline);
                        DrawCurvePolyline(panelGo, curveProjected);
                        if (showS0DebugEdges && boundaryWorld != null && boundaryWorld.Count >= 4)
                        {
                            float startZ = boundaryWorld[1].y;
                            float endZ = boundaryWorld[boundaryWorld.Count - 2].y;
                            DrawDebugEdgesYPlane(panelGo, shipY, shipZ, xMin, xMax, startZ, endZ);
                        }
                    }
                    else
                    {
                        GetMinMax(xVals, 0f, defaultExtent, out float xMin, out float xMax, out bool fx);
                        GetMinMax(zVals, 0f, defaultExtent, out float zMin, out float zMax, out bool fz);
                        boundaryWorld = new List<Vector3>
                        {
                            new Vector3(xMin, zMin, shipY),
                            new Vector3(xMax, zMin, shipY),
                            new Vector3(xMax, zMax, shipY),
                            new Vector3(xMin, zMax, shipY)
                        };
                        bool isFilled = fx || fz || hasShellLine || limitRefs.Count < 4;
                        var panelsGroup = GetOrCreateGroup(fileGroup, groupCache, name + "/Panels");
                        BuildS0Panel(panelId, name, guidRef, boundaryWorld, normalWorld, isFilled, panelsGroup, sourcePath);
                    }
                }
                else if (planeZ.HasValue)
                {
                    float shipZ = planeZ.Value;
                    normalWorld = Vector3.up;
                    var panelsGroup = GetOrCreateGroup(fileGroup, groupCache, name + "/Panels");

                    if (curvePolyline != null && curvePolyline.Count >= 2 && xVals.Count >= 2 && hasShellLine)
                    {
                        float xMin = Mathf.Min(xVals[0], xVals[1]);
                        float xMax = Mathf.Max(xVals[0], xVals[1]);
                        var curveProjected = ProjectCurveToZPlane(shipZ, curvePolyline);
                        boundaryWorld = BuildBoundaryFromCurveOnZPlane(shipZ, xMin, xMax, curveProjected);
                        var panelGo = BuildS0Panel(panelId, name, guidRef, boundaryWorld, normalWorld, true, panelsGroup, sourcePath);
                        DrawCurvePolyline(panelGo, curveProjected);
                    }
                    else
                    {
                        GetMinMax(xVals, 0f, defaultExtent, out float xMin, out float xMax, out bool fx);
                        GetMinMax(yVals, 0f, defaultExtent, out float yMin, out float yMax, out bool fy);
                        boundaryWorld = new List<Vector3>
                        {
                            new Vector3(xMin, shipZ, yMin),
                            new Vector3(xMax, shipZ, yMin),
                            new Vector3(xMax, shipZ, yMax),
                            new Vector3(xMin, shipZ, yMax)
                        };
                        bool isFilled = fx || fy || hasShellLine || limitRefs.Count < 4;
                        BuildS0Panel(panelId, name, guidRef, boundaryWorld, normalWorld, isFilled, panelsGroup, sourcePath);
                    }
                }
                else if (planeX.HasValue)
                {
                    GetMinMax(yVals, 0f, defaultExtent, out float yMin, out float yMax, out bool fy);
                    GetMinMax(zVals, 0f, defaultExtent, out float zMin, out float zMax, out bool fz);
                    float shipX = planeX.Value;
                    normalWorld = Vector3.right;
                    var panelsGroup = GetOrCreateGroup(fileGroup, groupCache, name + "/Panels");

                    var xCurve = limitedBy != null ? TryExtractFreeEdgeCurvePolylineByName(limitedBy, ocx, "X") : null;
                    if (xCurve != null && xCurve.Count >= 2 && zVals.Count >= 1 && hasShellLine)
                    {
                        float shipZ = zVals[0];
                        var curveProjected = ProjectCurveToXPlane(shipX, xCurve);
                        boundaryWorld = BuildBoundaryFromCurveOnXPlane(shipX, shipZ, curveProjected);
                        var panelGo = BuildS0Panel(panelId, name, guidRef, boundaryWorld, normalWorld, true, panelsGroup, sourcePath);
                        DrawCurvePolyline(panelGo, curveProjected);
                    }
                    else
                    {
                        boundaryWorld = new List<Vector3>
                        {
                            new Vector3(shipX, zMin, yMin),
                            new Vector3(shipX, zMin, yMax),
                            new Vector3(shipX, zMax, yMax),
                            new Vector3(shipX, zMax, yMin)
                        };
                        bool isFilled = fy || fz || hasShellLine || limitRefs.Count < 4;
                        BuildS0Panel(panelId, name, guidRef, boundaryWorld, normalWorld, isFilled, panelsGroup, sourcePath);
                    }
                }

                if (boundaryWorld == null || boundaryWorld.Count < 3) continue;

                foreach (var stiff in panel.Element(ocx + "StiffenedBy")?.Elements(ocx + "Stiffener") ?? Enumerable.Empty<XElement>())
                {
                    string sid = stiff.Attribute("id")?.Value;
                    string sname = stiff.Attribute("name")?.Value ?? sid;
                    var line = stiff.Descendants(ocx + "Line3D").FirstOrDefault();
                    if (line == null) continue;
                    Vector3 sp = ParsePoint(line.Element(ocx + "StartPoint"), ocx);
                    Vector3 ep = ParsePoint(line.Element(ocx + "EndPoint"), ocx);
                    if (sp == ep) continue;
                    var stiffGroup = GetOrCreateGroup(fileGroup, groupCache, name + "/Stiffeners");
                    BuildS0Stiffener(sid, sname, sp, ep, stiffGroup, sourcePath);
                }
            }
        }

        private List<Vector3> TryExtractLimitedByCurvePolyline(XElement limitedBy, XNamespace ocx)
        {
            if (limitedBy == null) return null;
            XElement curveNode = limitedBy.Element(ocx + "Ytemplate");
            if (curveNode == null) curveNode = limitedBy.Element(ocx + "FreeEdgeCurve3D");
            var compDirect = limitedBy.Element(ocx + "CompositeCurve3D");
            if (curveNode == null && compDirect == null) return null;

            var comp = compDirect ?? curveNode.Descendants(ocx + "CompositeCurve3D").FirstOrDefault();
            if (comp == null) return null;

            var poly = new List<Vector3>();
            foreach (var seg in comp.Elements())
            {
                if (seg.Name == ocx + "Line3D")
                {
                    var sp = ParsePoint(seg.Element(ocx + "StartPoint"), ocx);
                    var ep = ParsePoint(seg.Element(ocx + "EndPoint"), ocx);
                    AppendPoint(poly, sp);
                    AppendPoint(poly, ep);
                }
                else if (seg.Name == ocx + "CircumArc3D")
                {
                    var sp = ParsePoint(seg.Element(ocx + "StartPoint"), ocx);
                    var mp = ParsePoint(seg.Element(ocx + "IntermediatePoint"), ocx);
                    var ep = ParsePoint(seg.Element(ocx + "EndPoint"), ocx);
                    var arc = SampleArcPoints(sp, mp, ep, 16);
                    for (int i = 0; i < arc.Count; i++) AppendPoint(poly, arc[i]);
                }
            }
            return poly.Count >= 2 ? poly : null;
        }

        private List<Vector3> TryExtractFreeEdgeCurvePolylineByName(XElement limitedBy, XNamespace ocx, string requiredName)
        {
            if (limitedBy == null) return null;
            foreach (var n in limitedBy.Elements(ocx + "FreeEdgeCurve3D"))
            {
                string name = n.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(name)) continue;
                if (!string.Equals(name.Trim(), requiredName, System.StringComparison.OrdinalIgnoreCase)) continue;
                var comp = n.Descendants(ocx + "CompositeCurve3D").FirstOrDefault();
                if (comp != null) return ExtractCompositeCurvePolyline(comp, ocx);
            }
            return null;
        }

        private void AppendPoint(List<Vector3> list, Vector3 p)
        {
            if (list.Count == 0) { list.Add(p); return; }
            if ((list[list.Count - 1] - p).sqrMagnitude < 1e-6f) return;
            list.Add(p);
        }

        private List<Vector3> SampleArcPoints(Vector3 p1, Vector3 p2, Vector3 p3, int steps)
        {
            var pts = new List<Vector3>();
            if (steps < 16) steps = 16;
            if (!TryGetCircleThrough3Points(p1, p2, p3, out Vector3 center, out Vector3 normal))
            {
                pts.Add(p1);
                pts.Add(p2);
                pts.Add(p3);
                return pts;
            }

            Vector3 u = (p1 - center).normalized;
            Vector3 v = Vector3.Cross(normal, u).normalized;
            float a1 = Mathf.Atan2(Vector3.Dot(p1 - center, v), Vector3.Dot(p1 - center, u));
            float a2 = Mathf.Atan2(Vector3.Dot(p3 - center, v), Vector3.Dot(p3 - center, u));
            float am = Mathf.Atan2(Vector3.Dot(p2 - center, v), Vector3.Dot(p2 - center, u));

            float daCCW = DeltaAngleRad(a1, a2);
            bool midOnCCW = IsAngleBetweenCCW(a1, a2, am);
            float da = midOnCCW ? daCCW : daCCW - Mathf.PI * 2f;

            float r = (p1 - center).magnitude;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float a = a1 + da * t;
                pts.Add(center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r);
            }
            return pts;
        }

        private float DeltaAngleRad(float from, float to)
        {
            float d = to - from;
            while (d < 0f) d += Mathf.PI * 2f;
            while (d >= Mathf.PI * 2f) d -= Mathf.PI * 2f;
            return d;
        }

        private bool IsAngleBetweenCCW(float aStart, float aEnd, float aTest)
        {
            float dEnd = DeltaAngleRad(aStart, aEnd);
            float dTest = DeltaAngleRad(aStart, aTest);
            return dTest <= dEnd;
        }

        private bool TryGetCircleThrough3Points(Vector3 p1, Vector3 p2, Vector3 p3, out Vector3 center, out Vector3 normal)
        {
            center = Vector3.zero;
            normal = Vector3.zero;
            Vector3 a = p2 - p1;
            Vector3 b = p3 - p1;
            normal = Vector3.Cross(a, b);
            float n2 = normal.sqrMagnitude;
            if (n2 < 1e-10f) return false;
            normal.Normalize();

            Vector3 u = a.normalized;
            Vector3 v = Vector3.Cross(normal, u).normalized;

            Vector2 p1_2 = Vector2.zero;
            Vector2 p2_2 = new Vector2(Vector3.Dot(p2 - p1, u), Vector3.Dot(p2 - p1, v));
            Vector2 p3_2 = new Vector2(Vector3.Dot(p3 - p1, u), Vector3.Dot(p3 - p1, v));

            float d = 2f * (p2_2.x * p3_2.y - p2_2.y * p3_2.x);
            if (Mathf.Abs(d) < 1e-10f) return false;

            float p2Sq = p2_2.sqrMagnitude;
            float p3Sq = p3_2.sqrMagnitude;
            float ux = (p3_2.y * p2Sq - p2_2.y * p3Sq) / d;
            float uy = (p2_2.x * p3Sq - p3_2.x * p2Sq) / d;

            center = p1 + u * ux + v * uy;
            return true;
        }

        private List<Vector3> BuildBoundaryFromCurveOnYPlane(float shipY, float shipZ, float xMin, float xMax, List<Vector3> curvePolylineWorld)
        {
            var curve = ProjectCurveToYPlane(shipY, curvePolylineWorld);
            if (curve == null || curve.Count < 2) return null;

            float costForward = Mathf.Abs(curve[0].x - xMin) + Mathf.Abs(curve[curve.Count - 1].x - xMax);
            float costReverse = Mathf.Abs(curve[0].x - xMax) + Mathf.Abs(curve[curve.Count - 1].x - xMin);
            if (costReverse < costForward) curve.Reverse();

            var start = curve[0];
            var end = curve[curve.Count - 1];
            start = SnapX(start, xMin, 0.01f);
            end = SnapX(end, xMax, 0.01f);
            curve[0] = start;
            curve[curve.Count - 1] = end;

            var bottomLeft = new Vector3(xMin, shipZ, shipY);
            var bottomRight = new Vector3(xMax, shipZ, shipY);
            var leftTop = new Vector3(xMin, start.y, shipY);
            var rightTop = new Vector3(xMax, end.y, shipY);

            var boundary = new List<Vector3>(curve.Count + 4)
            {
                bottomLeft,
                leftTop
            };
            for (int i = 0; i < curve.Count; i++) boundary.Add(curve[i]);
            boundary.Add(rightTop);
            boundary.Add(bottomRight);

            return RemoveConsecutiveDuplicates(boundary);
        }

        private List<Vector3> BuildBoundaryFromCurveOnZPlane(float shipZ, float xMin, float xMax, List<Vector3> curveProjected)
        {
            if (curveProjected == null || curveProjected.Count < 2) return null;

            float costForward = Mathf.Abs(curveProjected[0].x - xMin) + Mathf.Abs(curveProjected[curveProjected.Count - 1].x - xMax);
            float costReverse = Mathf.Abs(curveProjected[0].x - xMax) + Mathf.Abs(curveProjected[curveProjected.Count - 1].x - xMin);
            if (costReverse < costForward) curveProjected.Reverse();

            var start = curveProjected[0];
            var end = curveProjected[curveProjected.Count - 1];
            start = SnapX(start, xMin, 0.01f);
            end = SnapX(end, xMax, 0.01f);
            curveProjected[0] = start;
            curveProjected[curveProjected.Count - 1] = end;

            float shellY = curveProjected.Min(p => p.z);
            var shellLeft = new Vector3(xMin, shipZ, shellY);
            var shellRight = new Vector3(xMax, shipZ, shellY);
            var leftTop = new Vector3(xMin, shipZ, start.z);
            var rightTop = new Vector3(xMax, shipZ, end.z);

            var boundary = new List<Vector3>(curveProjected.Count + 4)
            {
                shellLeft,
                leftTop
            };
            for (int i = 0; i < curveProjected.Count; i++) boundary.Add(curveProjected[i]);
            boundary.Add(rightTop);
            boundary.Add(shellRight);

            return RemoveConsecutiveDuplicates(boundary);
        }

        private List<Vector3> BuildBoundaryFromCurveOnXPlane(float shipX, float shipZ, List<Vector3> curveProjected)
        {
            if (curveProjected == null || curveProjected.Count < 2) return null;

            curveProjected = ProjectCurveToXPlane(shipX, curveProjected);
            var start = curveProjected[0];
            var end = curveProjected[curveProjected.Count - 1];

            var startProj = new Vector3(shipX, shipZ, start.z);
            var endProj = new Vector3(shipX, shipZ, end.z);

            var boundary = new List<Vector3>(curveProjected.Count + 2)
            {
                startProj
            };
            for (int i = 0; i < curveProjected.Count; i++) boundary.Add(curveProjected[i]);
            boundary.Add(endProj);

            return RemoveConsecutiveDuplicates(boundary);
        }

        private List<Vector3> ProjectCurveToYPlane(float shipY, List<Vector3> curveWorld)
        {
            var curve = new List<Vector3>(curveWorld.Count);
            for (int i = 0; i < curveWorld.Count; i++)
            {
                var p = curveWorld[i];
                p.z = shipY;
                curve.Add(p);
            }
            return curve;
        }

        private List<Vector3> ProjectCurveToZPlane(float shipZ, List<Vector3> curveWorld)
        {
            var curve = new List<Vector3>(curveWorld.Count);
            for (int i = 0; i < curveWorld.Count; i++)
            {
                var p = curveWorld[i];
                p.y = shipZ;
                curve.Add(p);
            }
            return curve;
        }

        private List<Vector3> ProjectCurveToXPlane(float shipX, List<Vector3> curveWorld)
        {
            var curve = new List<Vector3>(curveWorld.Count);
            for (int i = 0; i < curveWorld.Count; i++)
            {
                var p = curveWorld[i];
                p.x = shipX;
                curve.Add(p);
            }
            return curve;
        }

        private Vector3 SnapX(Vector3 p, float x, float eps)
        {
            if (Mathf.Abs(p.x - x) <= eps) p.x = x;
            return p;
        }

        private List<Vector3> RemoveConsecutiveDuplicates(List<Vector3> pts)
        {
            if (pts == null || pts.Count == 0) return pts;
            var list = new List<Vector3>(pts.Count);
            Vector3 last = pts[0];
            list.Add(last);
            for (int i = 1; i < pts.Count; i++)
            {
                var p = pts[i];
                if ((p - last).sqrMagnitude < 1e-8f) continue;
                list.Add(p);
                last = p;
            }
            return list;
        }

        private void GetMinMax(List<float> values, float defaultCenter, float defaultExtent, out float min, out float max, out bool usedFallback)
        {
            usedFallback = false;
            if (values != null && values.Count >= 2)
            {
                min = Mathf.Min(values[0], values[1]);
                max = Mathf.Max(values[0], values[1]);
                return;
            }
            if (values != null && values.Count == 1)
            {
                usedFallback = true;
                min = values[0] - defaultExtent;
                max = values[0] + defaultExtent;
                return;
            }
            usedFallback = true;
            min = defaultCenter - defaultExtent;
            max = defaultCenter + defaultExtent;
        }

        private GameObject BuildS0Panel(string id, string name, string guidRef, List<Vector3> boundaryWorld, Vector3 normalWorld, bool isFilled, Transform parent, string sourcePath)
        {
            GameObject go = new GameObject(id ?? name ?? "Panel");
            go.transform.SetParent(parent);

            boundaryWorld = RemoveConsecutiveDuplicates(boundaryWorld);
            Vector3 center = Vector3.zero;
            for (int i = 0; i < boundaryWorld.Count; i++) center += boundaryWorld[i];
            center /= boundaryWorld.Count;
            go.transform.position = center;

            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = isFilled ? s0FillMaterial : plateMaterial;
            SetRendererColor(mr, isFilled ? new Color(0.10f, 0.55f, 0.95f, 0.25f) : new Color(0.10f, 0.18f, 0.22f, 1f));
            mr.receiveShadows = false;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            Mesh mesh = new Mesh();
            var localLoop = new List<Vector3>(boundaryWorld.Count);
            for (int i = 0; i < boundaryWorld.Count; i++)
            {
                localLoop.Add(go.transform.InverseTransformPoint(boundaryWorld[i]));
            }
            var nrm = EstimateNormal(localLoop);
            SimplifyLoopLocal(localLoop, nrm);
            nrm = EstimateNormal(localLoop);
            EnsureCCW(localLoop, nrm);
            var tris = TriangulateLocalPolygon(localLoop, nrm);
            mesh.vertices = localLoop.ToArray();
            var doubleSided = new int[tris.Length * 2];
            tris.CopyTo(doubleSided, 0);
            for (int i = 0; i < tris.Length; i += 3)
            {
                doubleSided[tris.Length + i] = tris[i];
                doubleSided[tris.Length + i + 1] = tris[i + 2];
                doubleSided[tris.Length + i + 2] = tris[i + 1];
            }
            mesh.triangles = doubleSided;
            mesh.RecalculateNormals();
            mf.mesh = mesh;

            if (doubleSided.Length >= 3)
            {
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }

            var data = go.AddComponent<PartData>();
            data.SchemaLevel = "S0";
            data.PartId = id;
            data.GuidRef = guidRef;
            data.GeometryName = name;
            data.PartType = "Panel";
            data.Constraints = $"UnboundedRefPlane: {normalWorld}";
            data.SourceFilePath = sourcePath;
            data.SourceElementType = "Panel";
            data.Boundary = new List<Vector3>(boundaryWorld);
            data.FaceNormal = normalWorld;
            return go;
        }

        private void BuildS0Stiffener(string id, string name, Vector3 startPt, Vector3 endPt, Transform parent, string sourcePath)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = id ?? name ?? "Stiffener";
            go.transform.SetParent(parent);

            float length = Vector3.Distance(startPt, endPt);
            go.transform.position = (startPt + endPt) / 2f;
            go.transform.LookAt(endPt);
            go.transform.localScale = new Vector3(0.10f, 0.10f, Mathf.Max(0.01f, length));

            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = stiffenerMaterial ? stiffenerMaterial : r.material;
                r.material.color = new Color(1.00f, 0.85f, 0.10f, 1f);
                if (r.material.HasProperty("_Metallic")) r.material.SetFloat("_Metallic", 0f);
                if (r.material.HasProperty("_Glossiness")) r.material.SetFloat("_Glossiness", 0.15f);
            }

            var data = go.AddComponent<PartData>();
            data.SchemaLevel = "S0";
            data.PartId = id;
            data.GeometryName = name;
            data.PartType = "Stiffener";
            data.SourceFilePath = sourcePath;
            data.SourceElementType = "Stiffener";
        }

        private void DrawCurvePolyline(GameObject panelGo, List<Vector3> curveWorld)
        {
            if (panelGo == null || curveWorld == null || curveWorld.Count < 2) return;
            var t = panelGo.transform.Find("CurveEdge");
            GameObject child = t != null ? t.gameObject : new GameObject("CurveEdge");
            child.transform.SetParent(panelGo.transform, false);
            var lr = child.GetComponent<LineRenderer>();
            if (lr == null) lr = child.AddComponent<LineRenderer>();
            lr.material = lineMaterial;
            lr.positionCount = curveWorld.Count;
            var local = new Vector3[curveWorld.Count];
            for (int i = 0; i < curveWorld.Count; i++)
            {
                local[i] = panelGo.transform.InverseTransformPoint(curveWorld[i]);
            }
            lr.SetPositions(local);
            lr.startWidth = 0.05f;
            lr.endWidth = 0.05f;
            lr.loop = false;
            lr.useWorldSpace = false;
            lr.startColor = new Color(1.00f, 0.75f, 0.10f, 1f);
            lr.endColor = lr.startColor;
        }

        private void DrawDebugEdgesYPlane(GameObject panelGo, float shipY, float shipZ, float xMin, float xMax, float startZ, float endZ)
        {
            if (panelGo == null) return;
            var root = panelGo.transform.Find("DebugEdges");
            GameObject go = root != null ? root.gameObject : new GameObject("DebugEdges");
            go.transform.SetParent(panelGo.transform, false);

            DrawDebugSegment(go.transform, "Edge_XMin", new Vector3(xMin, shipZ, shipY), new Vector3(xMin, startZ, shipY), new Color(0.95f, 0.25f, 0.25f, 1f), 0.04f);
            DrawDebugSegment(go.transform, "Edge_XMax", new Vector3(xMax, shipZ, shipY), new Vector3(xMax, endZ, shipY), new Color(0.95f, 0.25f, 0.25f, 1f), 0.04f);
            DrawDebugSegment(go.transform, "Edge_Z", new Vector3(xMin, shipZ, shipY), new Vector3(xMax, shipZ, shipY), new Color(0.25f, 0.95f, 0.35f, 1f), 0.04f);
        }

        private void DrawDebugSegment(Transform parent, string name, Vector3 aWorld, Vector3 bWorld, Color color, float width)
        {
            var t = parent.Find(name);
            GameObject go = t != null ? t.gameObject : new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.GetComponent<LineRenderer>();
            if (lr == null) lr = go.AddComponent<LineRenderer>();
            lr.material = lineMaterial;
            lr.positionCount = 2;
            lr.SetPosition(0, parent.parent.InverseTransformPoint(aWorld));
            lr.SetPosition(1, parent.parent.InverseTransformPoint(bWorld));
            lr.startWidth = width;
            lr.endWidth = width;
            lr.loop = false;
            lr.useWorldSpace = false;
            lr.startColor = color;
            lr.endColor = color;
        }

        private void EnsureCCW(List<Vector3> loopLocal, Vector3 normal)
        {
            if (loopLocal == null || loopLocal.Count < 3) return;
            Vector3 nrm = normal.sqrMagnitude < 1e-8f ? Vector3.up : normal.normalized;
            Vector3 u = Vector3.Normalize(Vector3.Cross(nrm, Vector3.forward));
            if (u.sqrMagnitude < 1e-8f) u = Vector3.right;
            Vector3 v = Vector3.Cross(nrm, u);
            float area = 0f;
            for (int i = 0; i < loopLocal.Count; i++)
            {
                var p0 = loopLocal[i];
                var p1 = loopLocal[(i + 1) % loopLocal.Count];
                float x0 = Vector3.Dot(p0, u);
                float y0 = Vector3.Dot(p0, v);
                float x1 = Vector3.Dot(p1, u);
                float y1 = Vector3.Dot(p1, v);
                area += (x0 * y1 - x1 * y0);
            }
            if (area < 0f) loopLocal.Reverse();
        }

        private void EnsureWinding2D(List<Vector2> pts, bool ccw)
        {
            if (pts == null || pts.Count < 3) return;
            float area = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                var p0 = pts[i];
                var p1 = pts[(i + 1) % pts.Count];
                area += p0.x * p1.y - p1.x * p0.y;
            }
            bool isCcw = area > 0f;
            if (isCcw != ccw) pts.Reverse();
        }

        private bool PointInPolygon(List<Vector2> poly, Vector2 p)
        {
            if (poly == null || poly.Count < 3) return false;
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[j];
                bool intersect = ((a.y > p.y) != (b.y > p.y))
                                 && (p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y + 1e-12f) + a.x);
                if (intersect) inside = !inside;
            }
            return inside;
        }

        private int[] TriangulateLocalPolygon(List<Vector3> loop, Vector3 normal)
        {
            int n = loop.Count;
            if (n < 3) return new int[0];
            Vector3 nrm = normal.sqrMagnitude < 1e-8f ? Vector3.up : normal.normalized;
            Vector3 u = Vector3.Normalize(Vector3.Cross(nrm, Vector3.forward));
            if (u.sqrMagnitude < 1e-8f) u = Vector3.right;
            Vector3 v = Vector3.Cross(nrm, u);
            var pts2 = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                var p = loop[i];
                pts2.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)));
            }
            float signedArea = 0f;
            for (int i = 0; i < pts2.Count; i++)
            {
                var p0 = pts2[i];
                var p1 = pts2[(i + 1) % pts2.Count];
                signedArea += (p0.x * p1.y - p1.x * p0.y);
            }
            bool ccw = signedArea > 0f;
            var indices = new List<int>();
            var V = new List<int>(n);
            for (int i = 0; i < n; i++) V.Add(i);
            int guard = 0;
            while (V.Count > 3 && guard < 10000)
            {
                guard++;
                bool earFound = false;
                for (int i = 0; i < V.Count; i++)
                {
                    int i0 = V[(i + V.Count - 1) % V.Count];
                    int i1 = V[i];
                    int i2 = V[(i + 1) % V.Count];
                    Vector2 a = pts2[i0];
                    Vector2 b = pts2[i1];
                    Vector2 c = pts2[i2];
                    float cross = CrossZ(a, b, c);
                    if (Mathf.Abs(cross) < 1e-8f) continue;
                    if (ccw ? (cross <= 0f) : (cross >= 0f)) continue;
                    bool contains = false;
                    for (int j = 0; j < V.Count; j++)
                    {
                        int k = V[j];
                        if (k == i0 || k == i1 || k == i2) continue;
                        if (PointInTriangle(pts2[k], a, b, c)) { contains = true; break; }
                    }
                    if (contains) continue;
                    indices.Add(i0);
                    indices.Add(i1);
                    indices.Add(i2);
                    V.RemoveAt(i);
                    earFound = true;
                    break;
                }
                if (!earFound)
                {
                    float bestAbs = float.PositiveInfinity;
                    int removeAt = -1;
                    for (int i = 0; i < V.Count; i++)
                    {
                        int i0 = V[(i + V.Count - 1) % V.Count];
                        int i1 = V[i];
                        int i2 = V[(i + 1) % V.Count];
                        Vector2 a = pts2[i0];
                        Vector2 b = pts2[i1];
                        Vector2 c = pts2[i2];
                        float abs = Mathf.Abs(CrossZ(a, b, c));
                        if (abs < bestAbs)
                        {
                            bestAbs = abs;
                            removeAt = i;
                        }
                    }
                    if (removeAt >= 0)
                    {
                        V.RemoveAt(removeAt);
                        continue;
                    }
                    break;
                }
            }
            if (V.Count == 3)
            {
                indices.Add(V[0]);
                indices.Add(V[1]);
                indices.Add(V[2]);
            }
            return indices.ToArray();
        }

        private float CrossZ(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 ab = b - a;
            Vector2 ac = c - a;
            return ab.x * ac.y - ab.y * ac.x;
        }

        private bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float s1 = CrossZ(a, b, p);
            float s2 = CrossZ(b, c, p);
            float s3 = CrossZ(c, a, p);
            bool hasNeg = (s1 < 0f) || (s2 < 0f) || (s3 < 0f);
            bool hasPos = (s1 > 0f) || (s2 > 0f) || (s3 > 0f);
            return !(hasNeg && hasPos);
        }

        private void SimplifyLoopLocal(List<Vector3> loopLocal, Vector3 normal)
        {
            if (loopLocal == null) return;
            if (loopLocal.Count < 4) return;

            Vector3 nrm = normal.sqrMagnitude < 1e-8f ? Vector3.up : normal.normalized;
            Vector3 u = Vector3.Normalize(Vector3.Cross(nrm, Vector3.forward));
            if (u.sqrMagnitude < 1e-8f) u = Vector3.right;
            Vector3 v = Vector3.Cross(nrm, u);

            bool changed = true;
            int guard = 0;
            while (changed && loopLocal.Count >= 4 && guard < 1000)
            {
                guard++;
                changed = false;
                for (int i = 0; i < loopLocal.Count; i++)
                {
                    int i0 = (i + loopLocal.Count - 1) % loopLocal.Count;
                    int i1 = i;
                    int i2 = (i + 1) % loopLocal.Count;
                    Vector3 p0 = loopLocal[i0];
                    Vector3 p1 = loopLocal[i1];
                    Vector3 p2 = loopLocal[i2];
                    if ((p1 - p0).sqrMagnitude < 1e-10f || (p2 - p1).sqrMagnitude < 1e-10f)
                    {
                        loopLocal.RemoveAt(i1);
                        changed = true;
                        break;
                    }
                    float x0 = Vector3.Dot(p0, u);
                    float y0 = Vector3.Dot(p0, v);
                    float x1 = Vector3.Dot(p1, u);
                    float y1 = Vector3.Dot(p1, v);
                    float x2 = Vector3.Dot(p2, u);
                    float y2 = Vector3.Dot(p2, v);
                    float cross = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
                    if (Mathf.Abs(cross) < 1e-6f)
                    {
                        loopLocal.RemoveAt(i1);
                        changed = true;
                        break;
                    }
                }
            }
        }

        private enum OpeningRelation
        {
            Outside,
            Inside,
            Intersect
        }

        private OpeningRelation ClassifyOpeningAgainstOuter(List<Vector3> outerLocal, List<Vector3> openingLocal, Vector3 normal)
        {
            if (outerLocal == null || outerLocal.Count < 3) return OpeningRelation.Outside;
            if (openingLocal == null || openingLocal.Count < 3) return OpeningRelation.Outside;

            Vector3 nrm = normal.sqrMagnitude < 1e-8f ? Vector3.up : normal.normalized;
            Vector3 u = Vector3.Normalize(Vector3.Cross(nrm, Vector3.forward));
            if (u.sqrMagnitude < 1e-8f) u = Vector3.right;
            Vector3 v = Vector3.Cross(nrm, u);
            Vector3 o0 = outerLocal[0];

            var outer2 = ProjectLoop2D(outerLocal, o0, u, v);
            EnsureWinding2D(outer2, ccw: true);
            var hole2 = ProjectLoop2D(openingLocal, o0, u, v);
            EnsureWinding2D(hole2, ccw: true);

            bool edgesIntersect = PolygonsEdgesIntersect2D(outer2, hole2);
            if (edgesIntersect) return OpeningRelation.Intersect;

            bool centroidInside = PointInPolygon(outer2, Centroid2D(hole2));
            return centroidInside ? OpeningRelation.Inside : OpeningRelation.Outside;
        }

        private List<Vector3> MergePolygonWithHolesLocal(List<Vector3> outerLocal, List<List<Vector3>> holesLocal, Vector3 normal, float epsScale = 1f)
        {
            if (outerLocal == null || outerLocal.Count < 3) return outerLocal;
            if (holesLocal == null || holesLocal.Count == 0) return outerLocal;

            Vector3 nrm = normal.sqrMagnitude < 1e-8f ? Vector3.up : normal.normalized;
            Vector3 u = Vector3.Normalize(Vector3.Cross(nrm, Vector3.forward));
            if (u.sqrMagnitude < 1e-8f) u = Vector3.right;
            Vector3 v = Vector3.Cross(nrm, u);

            var merged = new List<Vector3>(outerLocal);
            EnsureCCW(merged, nrm);

            for (int h = 0; h < holesLocal.Count; h++)
            {
                var hole = holesLocal[h];
                if (hole == null || hole.Count < 3) continue;
                hole = new List<Vector3>(hole);
                EnsureCCW(hole, nrm);

                var outer2 = ProjectTo2D(merged, u, v);
                var hole2 = ProjectTo2D(hole, u, v);
                EnsureWinding2D(outer2, ccw: true);
                if (SignedArea2D(hole2) > 0f)
                {
                    hole2.Reverse();
                    hole.Reverse();
                }
                if (!PointInPolygon(outer2, Centroid2D(hole2))) continue;

                int ih = RightmostIndex(hole2);
                var otherHoles2 = new List<List<Vector2>>();
                for (int oh = 0; oh < holesLocal.Count; oh++)
                {
                    if (oh == h) continue;
                    var other = holesLocal[oh];
                    if (other == null || other.Count < 3) continue;
                    otherHoles2.Add(ProjectTo2D(other, u, v));
                }
                int io = FindVisibleBridgeOuterIndex(outer2, hole2, ih, otherHoles2);
                if (io < 0)
                {
                    io = FindBridgeOuterIndexByDistance(outer2, hole2, ih, otherHoles2, strictOtherHoles: true);
                }
                if (io < 0)
                {
                    var holeIndices = GetHoleExtremeIndices(hole2);
                    for (int ti = 0; ti < holeIndices.Count && io < 0; ti++)
                    {
                        int hi = holeIndices[ti];
                        io = FindVisibleBridgeOuterIndex(outer2, hole2, hi, otherHoles2);
                        if (io < 0) io = FindBridgeOuterIndexByDistance(outer2, hole2, hi, otherHoles2, strictOtherHoles: true);
                        if (io >= 0) ih = hi;
                    }
                }
                if (io < 0)
                {
                    io = FindBridgeOuterIndexByDistance(outer2, hole2, ih, otherHoles2, strictOtherHoles: false);
                }
                if (io < 0)
                {
                    io = ClosestIndex(outer2, hole2[ih]);
                }

                float eps = ComputeBridgeEpsilon(outer2, epsScale);
                merged = SpliceHole(merged, hole, io, ih, nrm, eps);
                merged = RemoveConsecutiveDuplicates(merged);
                if (merged.Count < 3) break;
            }

            return merged;
        }

        private int ClosestIndex(List<Vector2> pts, Vector2 p)
        {
            int idx = 0;
            float best = float.PositiveInfinity;
            for (int i = 0; i < pts.Count; i++)
            {
                float d = (pts[i] - p).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    idx = i;
                }
            }
            return idx;
        }

        private List<int> GetHoleExtremeIndices(List<Vector2> hole)
        {
            var list = new List<int>();
            if (hole == null || hole.Count == 0) return list;

            int right = 0, left = 0, top = 0, bottom = 0;
            float maxX = hole[0].x, minX = hole[0].x, maxY = hole[0].y, minY = hole[0].y;
            for (int i = 1; i < hole.Count; i++)
            {
                var p = hole[i];
                if (p.x > maxX) { maxX = p.x; right = i; }
                if (p.x < minX) { minX = p.x; left = i; }
                if (p.y > maxY) { maxY = p.y; top = i; }
                if (p.y < minY) { minY = p.y; bottom = i; }
            }

            void AddUnique(int i)
            {
                if (!list.Contains(i)) list.Add(i);
            }

            AddUnique(right);
            AddUnique(left);
            AddUnique(top);
            AddUnique(bottom);
            return list;
        }

        private int FindBridgeOuterIndexByDistance(List<Vector2> outer, List<Vector2> hole, int holeIndex, List<List<Vector2>> otherHoles, bool strictOtherHoles)
        {
            if (outer == null || outer.Count < 3) return -1;
            if (hole == null || hole.Count < 3) return -1;
            if (holeIndex < 0 || holeIndex >= hole.Count) return -1;

            Vector2 hp = hole[holeIndex];
            var candidates = new List<int>(outer.Count);
            for (int i = 0; i < outer.Count; i++) candidates.Add(i);
            candidates.Sort((a, b) =>
            {
                float da = (outer[a] - hp).sqrMagnitude;
                float db = (outer[b] - hp).sqrMagnitude;
                return da.CompareTo(db);
            });

            for (int ci = 0; ci < candidates.Count; ci++)
            {
                int io = candidates[ci];
                Vector2 op = outer[io];
                if (SegmentIntersectsPolygon(hp, op, outer, skipVertexIndex: io)) continue;
                if (SegmentIntersectsPolygon(hp, op, hole, skipVertexIndex: holeIndex)) continue;
                if (strictOtherHoles && otherHoles != null && otherHoles.Count > 0)
                {
                    if (PointTouchesAnyHole(op, otherHoles)) continue;
                    if (SegmentCrossesAnyHole(hp, op, otherHoles)) continue;
                }
                return io;
            }

            return -1;
        }

        private List<Vector2> ProjectTo2D(List<Vector3> pts, Vector3 u, Vector3 v)
        {
            var list = new List<Vector2>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                list.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)));
            }
            return list;
        }

        private List<Vector2> ProjectLoop2D(List<Vector3> pts, Vector3 origin, Vector3 u, Vector3 v)
        {
            var list = new List<Vector2>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i] - origin;
                list.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)));
            }
            return list;
        }

        private float SignedArea2D(List<Vector2> pts)
        {
            if (pts == null || pts.Count < 3) return 0f;
            float area = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                var p0 = pts[i];
                var p1 = pts[(i + 1) % pts.Count];
                area += p0.x * p1.y - p1.x * p0.y;
            }
            return area * 0.5f;
        }

        private float PolygonArea2DLocal(List<Vector3> loop, Vector3 normal)
        {
            if (loop == null || loop.Count < 3) return 0f;
            Vector3 nrm = normal.sqrMagnitude < 1e-8f ? Vector3.up : normal.normalized;
            Vector3 u = Vector3.Normalize(Vector3.Cross(nrm, Vector3.forward));
            if (u.sqrMagnitude < 1e-8f) u = Vector3.right;
            Vector3 v = Vector3.Cross(nrm, u);
            return Mathf.Abs(SignedArea2D(ProjectTo2D(loop, u, v)));
        }

        private float TrianglesArea2DLocal(List<Vector3> verts, int[] tris, Vector3 normal)
        {
            if (verts == null || verts.Count < 3) return 0f;
            if (tris == null || tris.Length < 3) return 0f;
            Vector3 nrm = normal.sqrMagnitude < 1e-8f ? Vector3.up : normal.normalized;
            Vector3 u = Vector3.Normalize(Vector3.Cross(nrm, Vector3.forward));
            if (u.sqrMagnitude < 1e-8f) u = Vector3.right;
            Vector3 v = Vector3.Cross(nrm, u);

            float area = 0f;
            for (int i = 0; i < tris.Length; i += 3)
            {
                int ia = tris[i];
                int ib = tris[i + 1];
                int ic = tris[i + 2];
                if ((uint)ia >= (uint)verts.Count || (uint)ib >= (uint)verts.Count || (uint)ic >= (uint)verts.Count) continue;
                Vector3 a3 = verts[ia];
                Vector3 b3 = verts[ib];
                Vector3 c3 = verts[ic];
                Vector2 a = new Vector2(Vector3.Dot(a3, u), Vector3.Dot(a3, v));
                Vector2 b = new Vector2(Vector3.Dot(b3, u), Vector3.Dot(b3, v));
                Vector2 c = new Vector2(Vector3.Dot(c3, u), Vector3.Dot(c3, v));
                Vector2 ab = b - a;
                Vector2 ac = c - a;
                area += Mathf.Abs(ab.x * ac.y - ab.y * ac.x) * 0.5f;
            }
            return area;
        }

        private Vector2 Centroid2D(List<Vector2> pts)
        {
            if (pts == null || pts.Count == 0) return Vector2.zero;
            float x = 0f;
            float y = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                x += pts[i].x;
                y += pts[i].y;
            }
            return new Vector2(x / pts.Count, y / pts.Count);
        }

        private int RightmostIndex(List<Vector2> pts)
        {
            int idx = 0;
            float bestX = pts[0].x;
            float bestY = pts[0].y;
            for (int i = 1; i < pts.Count; i++)
            {
                var p = pts[i];
                if (p.x > bestX || (Mathf.Abs(p.x - bestX) < 1e-6f && p.y > bestY))
                {
                    bestX = p.x;
                    bestY = p.y;
                    idx = i;
                }
            }
            return idx;
        }

        private float ComputeBridgeEpsilon(List<Vector2> outer, float scale)
        {
            if (outer == null || outer.Count == 0) return 1e-5f;
            float minX = outer[0].x, maxX = outer[0].x, minY = outer[0].y, maxY = outer[0].y;
            for (int i = 1; i < outer.Count; i++)
            {
                var p = outer[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            float maxDim = Mathf.Max(maxX - minX, maxY - minY);
            if (scale < 1f) scale = 1f;
            return Mathf.Max(1e-5f, maxDim * 1e-5f * scale);
        }

        private bool PolygonsEdgesIntersect2D(List<Vector2> a, List<Vector2> b)
        {
            if (a == null || a.Count < 3 || b == null || b.Count < 3) return false;
            for (int i = 0; i < a.Count; i++)
            {
                int i2 = (i + 1) % a.Count;
                Vector2 a0 = a[i];
                Vector2 a1 = a[i2];
                for (int j = 0; j < b.Count; j++)
                {
                    int j2 = (j + 1) % b.Count;
                    Vector2 b0 = b[j];
                    Vector2 b1 = b[j2];
                    if (SegmentsIntersect(a0, a1, b0, b1)) return true;
                }
            }
            return false;
        }

        private int FindVisibleBridgeOuterIndex(List<Vector2> outer, List<Vector2> hole, int holeIndex, List<List<Vector2>> otherHoles)
        {
            if (outer == null || outer.Count < 3) return -1;
            if (hole == null || hole.Count < 3) return -1;
            if (holeIndex < 0 || holeIndex >= hole.Count) return -1;

            Vector2 hp = hole[holeIndex];
            var candidates = new List<int>();
            for (int i = 0; i < outer.Count; i++)
            {
                if (outer[i].x > hp.x + 1e-6f) candidates.Add(i);
            }
            if (candidates.Count == 0)
            {
                int idx = 0;
                float maxX = outer[0].x;
                for (int i = 1; i < outer.Count; i++)
                {
                    if (outer[i].x > maxX) { maxX = outer[i].x; idx = i; }
                }
                candidates.Add(idx);
            }

            candidates.Sort((a, b) =>
            {
                float da = (outer[a] - hp).sqrMagnitude;
                float db = (outer[b] - hp).sqrMagnitude;
                return da.CompareTo(db);
            });

            for (int ci = 0; ci < candidates.Count; ci++)
            {
                int io = candidates[ci];
                Vector2 op = outer[io];
                if (SegmentIntersectsPolygon(hp, op, outer, skipVertexIndex: io)) continue;
                if (SegmentIntersectsPolygon(hp, op, hole, skipVertexIndex: holeIndex)) continue;
                if (otherHoles != null && otherHoles.Count > 0)
                {
                    if (PointTouchesAnyHole(op, otherHoles)) continue;
                    if (SegmentCrossesAnyHole(hp, op, otherHoles)) continue;
                }
                return io;
            }

            return -1;
        }

        private bool PointTouchesAnyHole(Vector2 p, List<List<Vector2>> holes)
        {
            if (holes == null || holes.Count == 0) return false;
            for (int h = 0; h < holes.Count; h++)
            {
                var hole = holes[h];
                if (hole == null || hole.Count < 2) continue;
                for (int i = 0; i < hole.Count; i++)
                {
                    if ((hole[i] - p).sqrMagnitude < 1e-10f) return true;
                    Vector2 a = hole[i];
                    Vector2 b = hole[(i + 1) % hole.Count];
                    if (PointOnSegment2D(a, b, p)) return true;
                }
            }
            return false;
        }

        private bool PointOnSegment2D(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            Vector2 ap = p - a;
            float cross = ab.x * ap.y - ab.y * ap.x;
            if (Mathf.Abs(cross) > 1e-8f) return false;
            float dot = ap.x * ab.x + ap.y * ab.y;
            if (dot < -1e-8f) return false;
            float ab2 = ab.x * ab.x + ab.y * ab.y;
            if (dot > ab2 + 1e-8f) return false;
            return true;
        }

        private List<Vector3> RemoveConsecutiveDuplicatesTol(List<Vector3> pts, float tol)
        {
            if (pts == null || pts.Count == 0) return pts;
            float tol2 = tol * tol;
            var res = new List<Vector3>(pts.Count);
            Vector3 prev = pts[0];
            res.Add(prev);
            for (int i = 1; i < pts.Count; i++)
            {
                var p = pts[i];
                if ((p - prev).sqrMagnitude <= tol2) continue;
                res.Add(p);
                prev = p;
            }
            return res;
        }

        private bool PointInsideOrOnPolygon(List<Vector2> poly, Vector2 p)
        {
            if (poly == null || poly.Count < 3) return false;
            if (PointInPolygon(poly, p)) return true;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % poly.Count];
                if (PointOnSegment2D(a, b, p)) return true;
            }
            return false;
        }

        private List<Vector3> PrepareOpeningDisplayLoop(List<Vector3> holeLocal, List<Vector3> outerLocal, Vector3 normal)
        {
            if (holeLocal == null || holeLocal.Count < 3) return null;
            var loop = RemoveConsecutiveDuplicatesTol(new List<Vector3>(holeLocal), 1e-5f);
            if (loop.Count >= 3 && (loop[0] - loop[loop.Count - 1]).sqrMagnitude < 1e-10f) loop.RemoveAt(loop.Count - 1);
            if (loop.Count < 3) return null;

            Vector3 nrm = normal.sqrMagnitude < 1e-8f ? Vector3.up : normal.normalized;
            SimplifyLoopLocal(loop, nrm);
            if (loop.Count < 3) return null;

            if (outerLocal == null || outerLocal.Count < 3) return loop;

            Vector3 u = Vector3.Normalize(Vector3.Cross(nrm, Vector3.forward));
            if (u.sqrMagnitude < 1e-8f) u = Vector3.right;
            Vector3 v = Vector3.Cross(nrm, u);
            Vector3 o0 = outerLocal[0];

            var outer2 = ProjectLoop2D(outerLocal, o0, u, v);
            EnsureWinding2D(outer2, ccw: true);

            var kept = new List<Vector3>(loop.Count);
            for (int i = 0; i < loop.Count; i++)
            {
                Vector3 p3 = loop[i];
                Vector2 p2 = new Vector2(Vector3.Dot(p3 - o0, u), Vector3.Dot(p3 - o0, v));
                if (PointInsideOrOnPolygon(outer2, p2)) kept.Add(p3);
            }

            kept = RemoveConsecutiveDuplicatesTol(kept, 1e-5f);
            if (kept.Count >= 3 && (kept[0] - kept[kept.Count - 1]).sqrMagnitude < 1e-10f) kept.RemoveAt(kept.Count - 1);
            if (kept.Count < 3) return null;
            SimplifyLoopLocal(kept, nrm);
            if (kept.Count < 3) return null;
            return kept;
        }

        private bool TryComputeLeakCentroidLocal(List<Vector3> outerLocal, List<List<Vector3>> holesLocal, List<Vector3> vertsLocal, int[] tris, Vector3 normal, out Vector3 leakLocal)
        {
            leakLocal = Vector3.zero;
            if (outerLocal == null || outerLocal.Count < 3) return false;
            if (vertsLocal == null || vertsLocal.Count < 3) return false;
            if (tris == null || tris.Length < 3) return false;

            Vector3 nrm = normal.sqrMagnitude < 1e-8f ? Vector3.up : normal.normalized;
            Vector3 u = Vector3.Normalize(Vector3.Cross(nrm, Vector3.forward));
            if (u.sqrMagnitude < 1e-8f) u = Vector3.right;
            Vector3 v = Vector3.Cross(nrm, u);
            Vector3 o0 = outerLocal[0];

            var outer2 = ProjectLoop2D(outerLocal, o0, u, v);
            EnsureWinding2D(outer2, ccw: true);

            var holes2 = new List<List<Vector2>>();
            if (holesLocal != null)
            {
                for (int i = 0; i < holesLocal.Count; i++)
                {
                    var h = holesLocal[i];
                    if (h == null || h.Count < 3) continue;
                    var h2 = ProjectLoop2D(h, o0, u, v);
                    EnsureWinding2D(h2, ccw: true);
                    holes2.Add(h2);
                }
            }

            var verts2 = new List<Vector2>(vertsLocal.Count);
            for (int i = 0; i < vertsLocal.Count; i++)
            {
                var p3 = vertsLocal[i] - o0;
                verts2.Add(new Vector2(Vector3.Dot(p3, u), Vector3.Dot(p3, v)));
            }

            float minX = outer2[0].x, maxX = outer2[0].x, minY = outer2[0].y, maxY = outer2[0].y;
            for (int i = 1; i < outer2.Count; i++)
            {
                var p = outer2[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }

            int gx = 55;
            int gy = 55;
            float dx = (maxX - minX) / gx;
            float dy = (maxY - minY) / gy;
            if (dx <= 1e-8f || dy <= 1e-8f) return false;

            var uncovered = new List<Vector2>();
            for (int iy = 0; iy <= gy; iy++)
            {
                float y = minY + dy * (iy + 0.5f);
                for (int ix = 0; ix <= gx; ix++)
                {
                    float x = minX + dx * (ix + 0.5f);
                    var p = new Vector2(x, y);
                    if (!PointInsideOrOnPolygon(outer2, p)) continue;
                    bool inHole = false;
                    for (int h = 0; h < holes2.Count; h++)
                    {
                        if (PointInPolygon(holes2[h], p)) { inHole = true; break; }
                    }
                    if (inHole) continue;

                    bool covered = false;
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        int ia = tris[t];
                        int ib = tris[t + 1];
                        int ic = tris[t + 2];
                        if ((uint)ia >= (uint)verts2.Count || (uint)ib >= (uint)verts2.Count || (uint)ic >= (uint)verts2.Count) continue;
                        if (PointInTriangle(p, verts2[ia], verts2[ib], verts2[ic])) { covered = true; break; }
                    }
                    if (!covered) uncovered.Add(p);
                }
            }

            if (uncovered.Count == 0) return false;
            float cx = 0f;
            float cy = 0f;
            for (int i = 0; i < uncovered.Count; i++)
            {
                cx += uncovered[i].x;
                cy += uncovered[i].y;
            }
            cx /= uncovered.Count;
            cy /= uncovered.Count;

            leakLocal = o0 + u * cx + v * cy;
            return true;
        }

        private void CreateLeakMarker(GameObject plateGo, Vector3 leakLocal, float size)
        {
            if (plateGo == null) return;
            if (size <= 0f) size = 0.05f;
            var marker = new GameObject("LeakMarker");
            marker.transform.SetParent(plateGo.transform);
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one;

            var lr = marker.AddComponent<LineRenderer>();
            lr.material = lineMaterial;
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
            lr.positionCount = 5;
            lr.SetPosition(0, leakLocal + new Vector3(-size, 0f, 0f));
            lr.SetPosition(1, leakLocal + new Vector3(size, 0f, 0f));
            lr.SetPosition(2, leakLocal);
            lr.SetPosition(3, leakLocal + new Vector3(0f, -size, 0f));
            lr.SetPosition(4, leakLocal + new Vector3(0f, size, 0f));
            lr.startColor = Color.magenta;
            lr.endColor = Color.magenta;
        }

        private bool SegmentCrossesAnyHole(Vector2 p, Vector2 q, List<List<Vector2>> holes)
        {
            if (holes == null || holes.Count == 0) return false;
            Vector2 mid = (p + q) * 0.5f;
            for (int h = 0; h < holes.Count; h++)
            {
                var hole = holes[h];
                if (hole == null || hole.Count < 3) continue;
                if (PointInPolygon(hole, mid)) return true;
                if (SegmentIntersectsPolygon(p, q, hole, skipVertexIndex: -1)) return true;
            }
            return false;
        }

        private bool SegmentIntersectsPolygon(Vector2 p, Vector2 q, List<Vector2> poly, int skipVertexIndex)
        {
            for (int i = 0; i < poly.Count; i++)
            {
                int j = (i + 1) % poly.Count;
                if (i == skipVertexIndex || j == skipVertexIndex) continue;
                Vector2 a = poly[i];
                Vector2 b = poly[j];
                if (SegmentsIntersectNonTrivial(p, q, a, b)) return true;
            }
            return false;
        }

        private bool SegmentsIntersectNonTrivial(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            if (!SegmentsIntersect(p1, p2, q1, q2)) return false;
            if ((p1 - q1).sqrMagnitude < 1e-10f || (p1 - q2).sqrMagnitude < 1e-10f || (p2 - q1).sqrMagnitude < 1e-10f || (p2 - q2).sqrMagnitude < 1e-10f) return false;
            return true;
        }

        private float Orient(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }

        private bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            return p.x >= Mathf.Min(a.x, b.x) - 1e-10f
                && p.x <= Mathf.Max(a.x, b.x) + 1e-10f
                && p.y >= Mathf.Min(a.y, b.y) - 1e-10f
                && p.y <= Mathf.Max(a.y, b.y) + 1e-10f;
        }

        private bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            float o1 = Orient(p1, p2, q1);
            float o2 = Orient(p1, p2, q2);
            float o3 = Orient(q1, q2, p1);
            float o4 = Orient(q1, q2, p2);

            if (Mathf.Abs(o1) < 1e-10f && OnSegment(p1, p2, q1)) return true;
            if (Mathf.Abs(o2) < 1e-10f && OnSegment(p1, p2, q2)) return true;
            if (Mathf.Abs(o3) < 1e-10f && OnSegment(q1, q2, p1)) return true;
            if (Mathf.Abs(o4) < 1e-10f && OnSegment(q1, q2, p2)) return true;

            return (o1 > 0f) != (o2 > 0f) && (o3 > 0f) != (o4 > 0f);
        }

        private List<Vector3> SpliceHole(List<Vector3> outer, List<Vector3> hole, int outerIndex, int holeIndex, Vector3 normal, float eps)
        {
            var result = new List<Vector3>(outer.Count + hole.Count + 4);

            Vector3 oPt = outer[outerIndex];
            Vector3 hPt = hole[holeIndex];
            Vector3 dir = (hPt - oPt);
            if (dir.sqrMagnitude < 1e-8f) dir = Vector3.right;
            else dir = dir.normalized;
            Vector3 perp = Vector3.Cross(normal, dir).normalized * eps;

            for (int i = 0; i < outer.Count; i++)
            {
                if (i != outerIndex)
                {
                    result.Add(outer[i]);
                    continue;
                }

                result.Add(oPt + perp);
                result.Add(hPt + perp);
                for (int k = 1; k < hole.Count; k++) result.Add(hole[(holeIndex + k) % hole.Count]);
                result.Add(hPt - perp);
                result.Add(oPt - perp);
            }
            return result;
        }

        private List<Vector3> SubtractOpeningFromOuterLocal(List<Vector3> outerLocal, List<Vector3> openingLocal, Vector3 normal, out List<Vector3> clippedHoleLocal)
        {
            clippedHoleLocal = new List<Vector3>(openingLocal);
            if (outerLocal == null || outerLocal.Count < 3) return outerLocal;
            if (openingLocal == null || openingLocal.Count < 3) return outerLocal;

            Vector3 nrm = normal.sqrMagnitude < 1e-8f ? Vector3.up : normal.normalized;
            Vector3 u = Vector3.Normalize(Vector3.Cross(nrm, Vector3.forward));
            if (u.sqrMagnitude < 1e-8f) u = Vector3.right;
            Vector3 v = Vector3.Cross(nrm, u);
            Vector3 o0 = outerLocal[0];

            var outer2 = ProjectLoop2D(outerLocal, o0, u, v);
            EnsureWinding2D(outer2, ccw: true);
            var hole2 = ProjectLoop2D(openingLocal, o0, u, v);
            EnsureWinding2D(hole2, ccw: true);

            var intersections = FindIntersections(outer2, hole2);
            if (intersections.Count < 2) return outerLocal;

            BuildExpandedLoop(outer2, intersections, onOuter: true, out var outerExpanded, out var outerIdToIndex);
            BuildExpandedLoop(hole2, intersections, onOuter: false, out var holeExpanded, out var holeIdToIndex);

            var outerOrder = outerIdToIndex.Keys.ToList();
            outerOrder.Sort((aId, bId) =>
            {
                var a = intersections.First(x => x.Id == aId);
                var b = intersections.First(x => x.Id == bId);
                int c = a.OuterEdge.CompareTo(b.OuterEdge);
                if (c != 0) return c;
                return a.OuterT.CompareTo(b.OuterT);
            });

            if (outerOrder.Count < 2) return outerLocal;

            var removeArcs = new List<(int fromId, int toId)>();
            for (int i = 0; i < outerOrder.Count; i++)
            {
                int aId = outerOrder[i];
                int bId = outerOrder[(i + 1) % outerOrder.Count];
                int ia = outerIdToIndex[aId];
                int ib = outerIdToIndex[bId];
                Vector2 sample = SampleAlongCircular(outerExpanded, ia, ib);
                if (PointInPolygon(holeExpanded, sample)) removeArcs.Add((aId, bId));
            }

            if (removeArcs.Count == 0) return outerLocal;

            var result2 = new List<Vector2>();
            int startId = removeArcs[0].toId;
            int startIdx = outerIdToIndex[startId];
            int curIdx = startIdx;
            int guard = 0;

            var clippedHole2 = new List<Vector2>();

            do
            {
                guard++;
                if (guard > 100000) break;

                int curPointId = GetIntersectionIdAtIndex(outerIdToIndex, curIdx);
                var arc = removeArcs.FirstOrDefault(a => a.fromId == curPointId);
                if (arc.fromId != 0 && arc.toId != 0)
                {
                    int fromIdx = outerIdToIndex[arc.fromId];
                    int toIdx = outerIdToIndex[arc.toId];
                    if (curIdx == fromIdx)
                    {
                        AppendOpeningArc(result2, outerExpanded[fromIdx], outerExpanded[toIdx], arc.fromId, arc.toId, holeExpanded, holeIdToIndex, outerExpanded, out List<Vector2> innerHoleArc);

                        var removedOuterArc = WalkCircular(outerExpanded, fromIdx, toIdx, forward: true);
                        for (int k = 0; k < removedOuterArc.Count; k++) clippedHole2.Add(removedOuterArc[k]);
                        for (int k = innerHoleArc.Count - 1; k >= 0; k--) clippedHole2.Add(innerHoleArc[k]);

                        curIdx = toIdx;
                        continue;
                    }
                }

                result2.Add(outerExpanded[curIdx]);
                curIdx = (curIdx + 1) % outerExpanded.Count;
            } while (curIdx != startIdx);

            var result3 = new List<Vector3>(result2.Count);
            for (int i = 0; i < result2.Count; i++)
            {
                var p = result2[i];
                result3.Add(o0 + u * p.x + v * p.y);
            }

            if (clippedHole2.Count >= 3)
            {
                clippedHoleLocal = new List<Vector3>(clippedHole2.Count);
                for (int i = 0; i < clippedHole2.Count; i++)
                {
                    var p = clippedHole2[i];
                    clippedHoleLocal.Add(o0 + u * p.x + v * p.y);
                }
                clippedHoleLocal = RemoveConsecutiveDuplicates(clippedHoleLocal);
            }

            return RemoveConsecutiveDuplicates(result3);
        }

        private class PolyIntersection
        {
            public int Id;
            public Vector2 P;
            public int OuterEdge;
            public float OuterT;
            public int HoleEdge;
            public float HoleT;
        }

        private List<PolyIntersection> FindIntersections(List<Vector2> outer, List<Vector2> hole)
        {
            var list = new List<PolyIntersection>();
            int id = 1;
            for (int i = 0; i < outer.Count; i++)
            {
                int i2 = (i + 1) % outer.Count;
                Vector2 a0 = outer[i];
                Vector2 a1 = outer[i2];
                for (int j = 0; j < hole.Count; j++)
                {
                    int j2 = (j + 1) % hole.Count;
                    Vector2 b0 = hole[j];
                    Vector2 b1 = hole[j2];
                    if (!TrySegmentIntersection(a0, a1, b0, b1, out Vector2 p, out float ta, out float tb)) continue;
                    bool dup = false;
                    for (int k = 0; k < list.Count; k++)
                    {
                        if ((list[k].P - p).sqrMagnitude < 1e-10f) { dup = true; break; }
                    }
                    if (dup) continue;
                    list.Add(new PolyIntersection { Id = id++, P = p, OuterEdge = i, OuterT = ta, HoleEdge = j, HoleT = tb });
                }
            }
            return list;
        }

        private bool TrySegmentIntersection(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2, out Vector2 p, out float tP, out float tQ)
        {
            p = Vector2.zero;
            tP = 0f;
            tQ = 0f;
            Vector2 r = p2 - p1;
            Vector2 s = q2 - q1;
            float rxs = r.x * s.y - r.y * s.x;
            float qpxr = (q1 - p1).x * r.y - (q1 - p1).y * r.x;
            if (Mathf.Abs(rxs) < 1e-12f) return false;
            float t = ((q1 - p1).x * s.y - (q1 - p1).y * s.x) / rxs;
            float u = qpxr / rxs;
            if (t < -1e-6f || t > 1f + 1e-6f || u < -1e-6f || u > 1f + 1e-6f) return false;
            tP = Mathf.Clamp01(t);
            tQ = Mathf.Clamp01(u);
            p = p1 + tP * r;
            return true;
        }

        private void BuildExpandedLoop(List<Vector2> baseLoop, List<PolyIntersection> inters, bool onOuter, out List<Vector2> expanded, out Dictionary<int, int> idToIndex)
        {
            expanded = new List<Vector2>();
            idToIndex = new Dictionary<int, int>();
            for (int i = 0; i < baseLoop.Count; i++)
            {
                expanded.Add(baseLoop[i]);
                int edge = i;
                var items = onOuter
                    ? inters.Where(x => x.OuterEdge == edge).OrderBy(x => x.OuterT).ToList()
                    : inters.Where(x => x.HoleEdge == edge).OrderBy(x => x.HoleT).ToList();
                for (int k = 0; k < items.Count; k++)
                {
                    int idx = expanded.Count;
                    expanded.Add(items[k].P);
                    idToIndex[items[k].Id] = idx;
                }
            }
        }

        private Vector2 SampleAlongCircular(List<Vector2> loop, int from, int to)
        {
            int next = (from + 1) % loop.Count;
            if (from == to) return loop[from];
            if (next == to) return (loop[from] + loop[to]) * 0.5f;
            return (loop[from] + loop[next]) * 0.5f;
        }

        private int GetIntersectionIdAtIndex(Dictionary<int, int> idToIndex, int idx)
        {
            foreach (var kv in idToIndex)
            {
                if (kv.Value == idx) return kv.Key;
            }
            return 0;
        }

        private void AppendOpeningArc(List<Vector2> dst, Vector2 from, Vector2 to, int fromId, int toId, List<Vector2> holeExpanded, Dictionary<int, int> holeIdToIndex, List<Vector2> outerExpanded, out List<Vector2> innerHoleArc)
        {
            innerHoleArc = new List<Vector2>();
            if (!holeIdToIndex.TryGetValue(fromId, out int hf) || !holeIdToIndex.TryGetValue(toId, out int ht))
            {
                dst.Add(from);
                dst.Add(to);
                innerHoleArc.Add(from);
                innerHoleArc.Add(to);
                return;
            }

            var arc1 = WalkCircular(holeExpanded, hf, ht, forward: true);
            var arc2 = WalkCircular(holeExpanded, hf, ht, forward: false);
            Vector2 s1 = SampleAlongCircular(arc1, 0, arc1.Count - 1);
            Vector2 s2 = SampleAlongCircular(arc2, 0, arc2.Count - 1);
            bool a1In = PointInPolygon(outerExpanded, s1);
            bool a2In = PointInPolygon(outerExpanded, s2);
            var chosen = a1In || !a2In ? arc1 : arc2;
            for (int i = 0; i < chosen.Count; i++) dst.Add(chosen[i]);
            innerHoleArc = new List<Vector2>(chosen);
        }

        private List<Vector2> WalkCircular(List<Vector2> loop, int from, int to, bool forward)
        {
            var list = new List<Vector2>();
            int i = from;
            int guard = 0;
            while (true)
            {
                guard++;
                if (guard > 100000) break;
                list.Add(loop[i]);
                if (i == to) break;
                i = forward ? (i + 1) % loop.Count : (i - 1 + loop.Count) % loop.Count;
            }
            return list;
        }

        private void AlignGroupToCadGrid(Transform group)
        {
            if (group == null) return;

            Quaternion oldRot = group.rotation;
            Vector3 oldPos = group.position;

            if (cadAlignmentInitialized)
            {
                group.rotation = cadAlignmentRotation;
                group.position += cadAlignmentOffset;
                UpdateGroupStoredGeometry(group, oldRot, oldPos, group.rotation, group.position);
                return;
            }

            Vector3 bestNormal = Vector3.zero;
            float bestArea = 0f;
            foreach (var pd in group.GetComponentsInChildren<PartData>(true))
            {
                if (pd == null) continue;
                if (pd.PartType != "Plate" && pd.PartType != "Panel") continue;
                var boundary = pd.Boundary;
                if (boundary == null || boundary.Count < 3) continue;
                Vector3 n = EstimateNormalWorld(boundary);
                float a = EstimatePolygonArea(boundary, n);
                if (a > bestArea)
                {
                    bestArea = a;
                    bestNormal = n;
                }
            }

            if (bestNormal != Vector3.zero)
            {
                Quaternion tilt = Quaternion.FromToRotation(bestNormal, Vector3.up);
                group.rotation = tilt * group.rotation;
            }

            float snapY = Mathf.Round(group.eulerAngles.y / 90f) * 90f;
            group.rotation = Quaternion.Euler(0f, snapY, 0f);

            var renderers = group.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            Vector3 offset = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            group.position += offset;

            cadAlignmentRotation = group.rotation;
            cadAlignmentOffset = offset;
            cadAlignmentInitialized = true;

            UpdateGroupStoredGeometry(group, oldRot, oldPos, group.rotation, group.position);
        }

        private void UpdateGroupStoredGeometry(Transform group, Quaternion oldRot, Vector3 oldPos, Quaternion newRot, Vector3 newPos)
        {
            if (group == null) return;

            Quaternion deltaRot = newRot * Quaternion.Inverse(oldRot);
            var pds = group.GetComponentsInChildren<PartData>(true);
            for (int i = 0; i < pds.Length; i++)
            {
                var pd = pds[i];
                if (pd == null) continue;

                if (pd.Boundary != null && pd.Boundary.Count > 0)
                {
                    for (int k = 0; k < pd.Boundary.Count; k++)
                    {
                        Vector3 p = pd.Boundary[k];
                        pd.Boundary[k] = deltaRot * (p - oldPos) + newPos;
                    }
                }

                if (pd.OpeningBoundaries != null && pd.OpeningBoundaries.Count > 0)
                {
                    for (int h = 0; h < pd.OpeningBoundaries.Count; h++)
                    {
                        var hb = pd.OpeningBoundaries[h];
                        if (hb == null || hb.Count == 0) continue;
                        for (int k = 0; k < hb.Count; k++)
                        {
                            Vector3 p = hb[k];
                            hb[k] = deltaRot * (p - oldPos) + newPos;
                        }
                    }
                }

                if (pd.FaceNormal.sqrMagnitude > 1e-8f)
                {
                    pd.FaceNormal = (deltaRot * pd.FaceNormal).normalized;
                }
            }
        }

        private Vector3 EstimateNormalWorld(List<Vector3> boundaryWorld)
        {
            if (boundaryWorld == null || boundaryWorld.Count < 3) return Vector3.zero;
            var pts = boundaryWorld;
            Vector3 n = Vector3.zero;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 current = pts[i];
                Vector3 next = pts[(i + 1) % pts.Count];
                n.x += (current.y - next.y) * (current.z + next.z);
                n.y += (current.z - next.z) * (current.x + next.x);
                n.z += (current.x - next.x) * (current.y + next.y);
            }
            if (n == Vector3.zero) return Vector3.up;
            return n.normalized;
        }

        private float EstimatePolygonArea(List<Vector3> boundaryWorld, Vector3 normal)
        {
            if (boundaryWorld == null || boundaryWorld.Count < 3) return 0f;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < boundaryWorld.Count; i++)
            {
                Vector3 a = boundaryWorld[i];
                Vector3 b = boundaryWorld[(i + 1) % boundaryWorld.Count];
                sum += Vector3.Cross(a, b);
            }
            return Mathf.Abs(Vector3.Dot(sum, normal)) * 0.5f;
        }

        private string ExtractThickness(XElement plate, XNamespace ocx)
        {
            // 优先在 PlateMaterial 下查 Thickness
            var pm = plate.Element(ocx + "PlateMaterial") ?? plate.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "PlateMaterial", StringComparison.OrdinalIgnoreCase));
            if (pm != null)
            {
                var th = pm.Element(ocx + "Thickness") ?? pm.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "Thickness", StringComparison.OrdinalIgnoreCase));
                if (th != null)
                {
                    string v = th.Attribute("numericvalue")?.Value;
                    string u = th.Attribute("unit")?.Value;
                    if (!string.IsNullOrEmpty(v)) return string.IsNullOrEmpty(u) ? v : (v + " " + u);
                }
            }
            // 兜底：任何名含 Thickness 的节点
            var anyTh = plate.Descendants().FirstOrDefault(e => e.Name.LocalName.ToLower().Contains("thickness"));
            if (anyTh != null)
            {
                string v = anyTh.Attribute("numericvalue")?.Value ?? anyTh.Attribute("value")?.Value ?? anyTh.Value;
                string u = anyTh.Attribute("unit")?.Value;
                if (!string.IsNullOrEmpty(v)) return string.IsNullOrEmpty(u) ? v : (v + " " + u);
            }
            return "未记录";
        }

        private string GetPanelName(XElement element, XNamespace ocx)
        {
            var panel = element.Ancestors(ocx + "Panel").FirstOrDefault();
            string name = panel?.Attribute("name")?.Value ?? panel?.Attribute("id")?.Value ?? "OCX";
            return SanitizeName(name);
        }

        private Transform GetOrCreateGroup(Transform fileGroup, Dictionary<string, Transform> cache, string key)
        {
            if (cache.TryGetValue(key, out var t) && t != null) return t;

            string[] parts = key.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            Transform current = fileGroup;
            string currentKey = "";
            foreach (var part in parts)
            {
                currentKey = string.IsNullOrEmpty(currentKey) ? part : currentKey + "/" + part;
                if (cache.TryGetValue(currentKey, out var existing) && existing != null)
                {
                    current = existing;
                    continue;
                }

                var child = new GameObject(part);
                child.transform.SetParent(current);
                current = child.transform;
                cache[currentKey] = current;
            }

            cache[key] = current;
            return current;
        }

        private string SanitizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "OCX";
            return s.Replace("\\", "_").Replace("/", "_").Replace(":", "_").Replace("*", "_").Replace("?", "_").Replace("\"", "_").Replace("<", "_").Replace(">", "_").Replace("|", "_");
        }

        // --- 基础解析 ---
        private float ParseNumeric(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0f;
            raw = raw.Trim();
            if (raw.Contains(",") && !raw.Contains(".")) raw = raw.Replace(',', '.');
            if (float.TryParse(raw, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out float v)) return v;
            if (float.TryParse(raw, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.CurrentCulture, out v)) return v;
            return 0f;
        }

        private Vector3 ParsePoint(XElement ptNode, XNamespace ocx)
        {
            if (ptNode == null) return Vector3.zero;
            float x = ParseNumeric(ptNode.Element(ocx + "X")?.Attribute("numericvalue")?.Value);
            float y = ParseNumeric(ptNode.Element(ocx + "Y")?.Attribute("numericvalue")?.Value);
            float z = ParseNumeric(ptNode.Element(ocx + "Z")?.Attribute("numericvalue")?.Value);
            return new Vector3(x, z, y) * ModelVisualScale; // Unity 坐标系转换
        }

        private Vector3 ExtractPosition(XElement cogElement, XNamespace ocx)
        {
            return ParsePoint(cogElement, ocx);
        }

        // --- 精确实体构建：板材 ---
        private void BuildPrecisePlate(string id, string name, string guidRef, string mat, string thick, Vector3 cog, List<Vector3> boundary, List<OpeningContour> openings, Transform parent, string sourcePath, string schemaLevel = "S2", float dryWeightKg = 0f, string materialName = null)
        {
            BuildPrecisePlateInternal(id, name, guidRef, mat, materialName, thick, Vector3.zero, cog, boundary, openings, parent, sourcePath, schemaLevel, dryWeightKg);
        }

        private void BuildPrecisePlate(string id, string name, string guidRef, string mat, string thick, Vector3 thicknessDirWorld, Vector3 cog, List<Vector3> boundary, List<OpeningContour> openings, Transform parent, string sourcePath, string schemaLevel = "S2", float dryWeightKg = 0f, string materialName = null)
        {
            BuildPrecisePlateInternal(id, name, guidRef, mat, materialName, thick, thicknessDirWorld, cog, boundary, openings, parent, sourcePath, schemaLevel, dryWeightKg);
        }

        private void BuildPrecisePlate(string id, string name, string guidRef, string mat, string thick, Vector3 cog, List<Vector3> boundary, Transform parent, string sourcePath, string schemaLevel = "S2", float dryWeightKg = 0f, string materialName = null)
        {
            BuildPrecisePlateInternal(id, name, guidRef, mat, materialName, thick, Vector3.zero, cog, boundary, null, parent, sourcePath, schemaLevel, dryWeightKg);
        }

        private void BuildPrecisePlate(string id, string name, string guidRef, string mat, string thick, Vector3 thicknessDirWorld, Vector3 cog, List<Vector3> boundary, Transform parent, string sourcePath, string schemaLevel = "S2", float dryWeightKg = 0f, string materialName = null)
        {
            BuildPrecisePlateInternal(id, name, guidRef, mat, materialName, thick, thicknessDirWorld, cog, boundary, null, parent, sourcePath, schemaLevel, dryWeightKg);
        }

        private void BuildPrecisePlateInternal(string id, string name, string guidRef, string matRef, string materialName, string thick, Vector3 thicknessDirWorld, Vector3 cog, List<Vector3> boundary, List<OpeningContour> openings, Transform parent, string sourcePath, string schemaLevel, float dryWeightKg)
        {
            GameObject go = new GameObject(id);
            go.transform.position = cog;
            go.transform.SetParent(parent);

            if (boundary.Count > 2)
            {
                MeshFilter mf = go.AddComponent<MeshFilter>();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                if (plateMaterial) mr.sharedMaterial = plateMaterial;
                ApplyColor(mr, matRef);

                Mesh mesh = new Mesh();
                boundary = RemoveConsecutiveDuplicates(boundary);
                if (boundary.Count >= 3 && (boundary[0] - boundary[boundary.Count - 1]).sqrMagnitude < 1e-8f) boundary.RemoveAt(boundary.Count - 1);
                var localLoop = new List<Vector3>(boundary.Count);
                for (int i = 0; i < boundary.Count; i++)
                {
                    localLoop.Add(go.transform.InverseTransformPoint(boundary[i]));
                }
                var nrm = EstimateNormal(localLoop);
                SimplifyLoopLocal(localLoop, nrm);
                nrm = EstimateNormal(localLoop);
                EnsureCCW(localLoop, nrm);

                var outerLoopLocal = new List<Vector3>(localLoop);
                List<List<Vector3>> holeLoopsLocal = null;
                List<List<Vector3>> clippedHolesToDraw = null;

                if (openings != null && openings.Count > 0)
                {
                    holeLoopsLocal = new List<List<Vector3>>();
                    clippedHolesToDraw = new List<List<Vector3>>();
                    for (int i = 0; i < openings.Count; i++)
                    {
                        var ob = openings[i]?.Boundary;
                        if (ob == null || ob.Count < 3)
                        {
                            clippedHolesToDraw.Add(null);
                            continue;
                        }
                        var hb = RemoveConsecutiveDuplicates(ob);
                        if (hb.Count >= 3 && (hb[0] - hb[hb.Count - 1]).sqrMagnitude < 1e-8f) hb.RemoveAt(hb.Count - 1);
                        if (hb.Count < 3)
                        {
                            clippedHolesToDraw.Add(null);
                            continue;
                        }

                        var hl = new List<Vector3>(hb.Count);
                        for (int k = 0; k < hb.Count; k++) hl.Add(go.transform.InverseTransformPoint(hb[k]));
                        SimplifyLoopLocal(hl, nrm);
                        if (hl.Count < 3)
                        {
                            clippedHolesToDraw.Add(null);
                            continue;
                        }

                        var rel = ClassifyOpeningAgainstOuter(outerLoopLocal, hl, nrm);
                        if (rel == OpeningRelation.Inside)
                        {
                            holeLoopsLocal.Add(hl);
                            clippedHolesToDraw.Add(hl);
                        }
                        else if (rel == OpeningRelation.Intersect)
                        {
                            var prevOuter = outerLoopLocal;
                            float prevArea = EstimatePolygonArea(prevOuter, nrm);

                            var nextOuter = SubtractOpeningFromOuterLocal(prevOuter, hl, nrm, out var clippedHoleLocal);
                            if (clippedHoleLocal == null || clippedHoleLocal.Count < 3)
                            {
                                outerLoopLocal = prevOuter;
                                clippedHolesToDraw.Add(null);
                                continue;
                            }

                            float nextArea = EstimatePolygonArea(nextOuter, nrm);
                            float removed = prevArea - nextArea;
                            float holeArea = EstimatePolygonArea(hl, nrm);
                            if (removed <= 1e-8f)
                            {
                                outerLoopLocal = prevOuter;
                                clippedHolesToDraw.Add(null);
                                continue;
                            }
                            if (holeArea > 1e-8f && removed > holeArea * 4f)
                            {
                                outerLoopLocal = prevOuter;
                                clippedHolesToDraw.Add(null);
                                continue;
                            }

                            outerLoopLocal = nextOuter;
                            EnsureCCW(outerLoopLocal, nrm);
                            clippedHolesToDraw.Add(clippedHoleLocal);
                        }
                        else
                        {
                            clippedHolesToDraw.Add(null);
                        }
                    }
                    if (holeLoopsLocal.Count == 0) holeLoopsLocal = null;
                }

                localLoop = new List<Vector3>(outerLoopLocal);
                EnsureCCW(localLoop, nrm);

                if (holeLoopsLocal != null && holeLoopsLocal.Count > 0)
                {
                    var mergedLoop = MergePolygonWithHolesLocal(localLoop, holeLoopsLocal, nrm);
                    if (mergedLoop != null && mergedLoop.Count >= 3)
                    {
                        localLoop = mergedLoop;
                        nrm = EstimateNormal(localLoop);
                        EnsureCCW(localLoop, nrm);
                    }
                }

                float expectedArea = PolygonArea2DLocal(outerLoopLocal, nrm);
                if (holeLoopsLocal != null && holeLoopsLocal.Count > 0)
                {
                    for (int hi = 0; hi < holeLoopsLocal.Count; hi++) expectedArea -= PolygonArea2DLocal(holeLoopsLocal[hi], nrm);
                }
                expectedArea = Mathf.Max(0f, expectedArea);

                var bestLoop = localLoop;
                var bestNrm = nrm;
                var bestTris = TriangulateLocalPolygon(bestLoop, bestNrm);
                float bestArea = TrianglesArea2DLocal(bestLoop, bestTris, bestNrm);
                float bestRatio = expectedArea > 1e-6f ? (bestArea / expectedArea) : 1f;

                void ConsiderCandidate(List<Vector3> candLoop, Vector3 candNrm)
                {
                    if (candLoop == null || candLoop.Count < 3) return;
                    SimplifyLoopLocal(candLoop, candNrm);
                    candNrm = EstimateNormal(candLoop);
                    EnsureCCW(candLoop, candNrm);
                    var candTris = TriangulateLocalPolygon(candLoop, candNrm);
                    float candArea = TrianglesArea2DLocal(candLoop, candTris, candNrm);
                    float candRatio = expectedArea > 1e-6f ? (candArea / expectedArea) : 1f;
                    if (candRatio > bestRatio + 1e-4f)
                    {
                        bestLoop = candLoop;
                        bestNrm = candNrm;
                        bestTris = candTris;
                        bestArea = candArea;
                        bestRatio = candRatio;
                    }
                }

                if (holeLoopsLocal != null && holeLoopsLocal.Count > 0 && bestRatio < 0.995f)
                {
                    var retry1 = MergePolygonWithHolesLocal(new List<Vector3>(outerLoopLocal), holeLoopsLocal, nrm, epsScale: 200f);
                    if (retry1 != null && retry1.Count >= 3) ConsiderCandidate(retry1, EstimateNormal(retry1));

                    var retry2 = MergePolygonWithHolesLocal(new List<Vector3>(outerLoopLocal), holeLoopsLocal, nrm, epsScale: 800f);
                    if (retry2 != null && retry2.Count >= 3) ConsiderCandidate(retry2, EstimateNormal(retry2));

                    var sortedHoles = holeLoopsLocal
                        .Where(h => h != null && h.Count >= 3)
                        .Select(h => new { Loop = h, Area = PolygonArea2DLocal(h, nrm) })
                        .OrderByDescending(x => x.Area)
                        .Select(x => x.Loop)
                        .ToList();
                    var retry3 = MergePolygonWithHolesLocal(new List<Vector3>(outerLoopLocal), sortedHoles, nrm, epsScale: 200f);
                    if (retry3 != null && retry3.Count >= 3) ConsiderCandidate(retry3, EstimateNormal(retry3));
                }
                else if (bestRatio < 0.995f)
                {
                    var cleaned = RemoveConsecutiveDuplicates(new List<Vector3>(bestLoop));
                    if (cleaned.Count >= 3 && (cleaned[0] - cleaned[cleaned.Count - 1]).sqrMagnitude < 1e-8f) cleaned.RemoveAt(cleaned.Count - 1);
                    if (cleaned.Count >= 3) ConsiderCandidate(cleaned, EstimateNormal(cleaned));
                }

                localLoop = bestLoop;
                nrm = bestNrm;
                var tris = bestTris ?? new int[0];
                float th = ParseThicknessToMeters(thick);
                if (th <= 0f) th = 0.001f;
                float thVisual = th * ModelVisualScale;
                var thickDirLocal = thicknessDirWorld.sqrMagnitude > 1e-10f ? go.transform.InverseTransformDirection(thicknessDirWorld).normalized : nrm;
                if (Vector3.Dot(thickDirLocal, nrm) < 0f) thickDirLocal = -thickDirLocal;
                mesh = CreatePlateSolidMesh(localLoop, tris, outerLoopLocal, holeLoopsLocal, nrm, thickDirLocal, thVisual);
                mf.mesh = mesh;
                AddPlateOutlineEdges(go, outerLoopLocal, holeLoopsLocal, thickDirLocal, thVisual);

                if (expectedArea > 1e-6f && bestRatio < 0.995f)
                {
                    if (TryComputeLeakCentroidLocal(outerLoopLocal, holeLoopsLocal, localLoop, tris, nrm, out var leakLocal))
                    {
                        float markerSize = Mathf.Max(0.05f, Mathf.Min(0.25f, Mathf.Sqrt(expectedArea) * 0.03f));
                        CreateLeakMarker(go, leakLocal, markerSize);
                    }
                }

                if (mesh != null && mesh.triangles != null && mesh.triangles.Length >= 3)
                {
                    if (useMeshColliderForPlates)
                    {
                        var mc = go.AddComponent<MeshCollider>();
                        mc.sharedMesh = mesh;
                    }
                    else
                    {
                        var bc = go.AddComponent<BoxCollider>();
                        var b = mesh.bounds;
                        bc.center = b.center;
                        bc.size = new Vector3(Mathf.Max(b.size.x, minPlateColliderSize), Mathf.Max(b.size.y, minPlateColliderSize), Mathf.Max(b.size.z, minPlateColliderSize));
                    }
                }

                if (openings != null && openings.Count > 0 && clippedHolesToDraw != null)
                {
                    for (int i = 0; i < openings.Count; i++)
                    {
                        var o = openings[i];
                        if (o == null) continue;

                        var localHb = i < clippedHolesToDraw.Count ? clippedHolesToDraw[i] : null;
                        if (localHb == null || localHb.Count < 3) continue;

                        var displayLoop = drawOpeningOutlines ? PrepareOpeningDisplayLoop(localHb, outerLoopLocal, nrm) : localHb;
                        if (displayLoop == null || displayLoop.Count < 3) continue;

                        var child = new GameObject(SanitizeName("Opening_" + (string.IsNullOrEmpty(o.Name) ? i.ToString() : o.Name)));
                        child.transform.SetParent(go.transform);
                        child.transform.localPosition = Vector3.zero;
                        child.transform.localRotation = Quaternion.identity;
                        child.transform.localScale = Vector3.one;

                        if (drawOpeningOutlines)
                        {
                            var hlr = child.AddComponent<LineRenderer>();
                            hlr.material = lineMaterial;
                            hlr.positionCount = displayLoop.Count;
                            var hlPts = new Vector3[displayLoop.Count];
                            for (int k = 0; k < displayLoop.Count; k++) hlPts[k] = displayLoop[k];
                            hlr.SetPositions(hlPts);
                            hlr.startWidth = 0.04f;
                            hlr.endWidth = 0.04f;
                            hlr.loop = true;
                            hlr.useWorldSpace = false;
                            if (lineMaterial != null)
                            {
                                hlr.startColor = lineMaterial.color;
                                hlr.endColor = lineMaterial.color;
                            }
                        }

                        var opd = child.AddComponent<PartData>();
                        opd.SchemaLevel = schemaLevel;
                        opd.PartId = $"{id}_Opening_{i}";
                        opd.GuidRef = "";
                        opd.GeometryName = string.IsNullOrEmpty(o.Name) ? opd.PartId : o.Name;
                        opd.PartType = "Opening";
                        opd.MaterialRef = "";
                        opd.SourceFilePath = sourcePath;
                        opd.SourceElementType = "Opening";

                        var worldHb = new List<Vector3>(displayLoop.Count);
                        for (int k = 0; k < displayLoop.Count; k++) worldHb.Add(go.transform.TransformPoint(displayLoop[k]));
                        opd.Boundary = worldHb;
                        opd.OpeningNames.Clear();
                        opd.OpeningTypes.Clear();
                        opd.OpeningBoundaries.Clear();
                        if (!string.IsNullOrEmpty(o.Name)) opd.OpeningNames.Add(o.Name);
                        if (!string.IsNullOrEmpty(o.Type)) opd.OpeningTypes.Add(o.Type);
                    }
                }
            }

            // 设置 S0/S1/S2 共有数据
            var data = go.AddComponent<PartData>();
            data.SchemaLevel = schemaLevel;
            data.PartId = id;
            data.GuidRef = guidRef;
            data.GeometryName = string.IsNullOrEmpty(name) ? id : name;
            data.PartType = "Plate";
            data.MaterialRef = matRef;
            data.MaterialName = materialName;
            data.Thickness = thick;
            float pdTh = ParseThicknessToMeters(thick);
            if (pdTh <= 0f) pdTh = 0.001f;
            data.ThicknessValue = pdTh;
            data.Boundary = new List<Vector3>(boundary);
            if (thicknessDirWorld.sqrMagnitude > 1e-10f) data.FaceNormal = thicknessDirWorld.normalized;
            else if (data.Boundary != null && data.Boundary.Count >= 3) data.FaceNormal = EstimateNormalWorld(data.Boundary);
            if (openings != null && openings.Count > 0)
            {
                data.OpeningParams.Clear();
                data.OpeningNames.Clear();
                data.OpeningTypes.Clear();
                data.OpeningBoundaries.Clear();
                for (int i = 0; i < openings.Count; i++)
                {
                    var o = openings[i];
                    if (o == null || o.Boundary == null || o.Boundary.Count < 3) continue;
                    data.OpeningNames.Add(o.Name ?? "");
                    data.OpeningTypes.Add(o.Type ?? "");
                    data.OpeningBoundaries.Add(new List<Vector3>(o.Boundary));
                    if (o.Parameters != null)
                    {
                        foreach (var kv in o.Parameters)
                        {
                            string key = $"{(string.IsNullOrEmpty(o.Name) ? ("Opening" + i) : o.Name)}.{kv.Key}";
                            data.OpeningParams[key] = kv.Value;
                        }
                    }
                }
            }
            data.Weight = dryWeightKg > 0f ? dryWeightKg : 0f;
            data.SourceFilePath = sourcePath;
            data.SourceElementType = "Plate";
        }

        private Mesh CreatePlateSolidMesh(List<Vector3> topLoop, int[] topTris, List<Vector3> outerLoop, List<List<Vector3>> holeLoops, Vector3 polyNormalLocal, Vector3 thicknessDirLocal, float thickness)
        {
            polyNormalLocal = polyNormalLocal.sqrMagnitude > 1e-10f ? polyNormalLocal.normalized : Vector3.up;
            thicknessDirLocal = thicknessDirLocal.sqrMagnitude > 1e-10f ? thicknessDirLocal.normalized : polyNormalLocal;
            Vector3 half = thicknessDirLocal * (thickness * 0.5f);

            int nTop = topLoop != null ? topLoop.Count : 0;
            if (nTop < 3) return new Mesh();

            var verts = new List<Vector3>(nTop * 2 + 256);
            for (int i = 0; i < nTop; i++) verts.Add(topLoop[i] - half);
            for (int i = 0; i < nTop; i++) verts.Add(topLoop[i] + half);

            var tris = new List<int>((topTris != null ? topTris.Length : 0) * 2 + 1024);
            if (topTris != null)
            {
                int topStart = nTop;
                for (int i = 0; i < topTris.Length; i += 3)
                {
                    tris.Add(topStart + topTris[i]);
                    tris.Add(topStart + topTris[i + 1]);
                    tris.Add(topStart + topTris[i + 2]);
                }
                for (int i = 0; i < topTris.Length; i += 3)
                {
                    tris.Add(topTris[i]);
                    tris.Add(topTris[i + 2]);
                    tris.Add(topTris[i + 1]);
                }
            }

            void AddSideLoop(List<Vector3> loop, bool isHole)
            {
                if (loop == null || loop.Count < 2) return;
                var clean = RemoveConsecutiveDuplicates(loop);
                if (clean.Count >= 3 && (clean[0] - clean[clean.Count - 1]).sqrMagnitude < 1e-8f) clean.RemoveAt(clean.Count - 1);
                if (clean.Count < 2) return;

                int baseIndex = verts.Count;
                for (int i = 0; i < clean.Count; i++) verts.Add(clean[i] - half);
                for (int i = 0; i < clean.Count; i++) verts.Add(clean[i] + half);

                int m = clean.Count;
                for (int i = 0; i < m; i++)
                {
                    int j = (i + 1) % m;
                    int bi = baseIndex + i;
                    int bj = baseIndex + j;
                    int ti = baseIndex + m + i;
                    int tj = baseIndex + m + j;
                    if (!isHole)
                    {
                        tris.Add(bi); tris.Add(bj); tris.Add(tj);
                        tris.Add(bi); tris.Add(tj); tris.Add(ti);
                    }
                    else
                    {
                        tris.Add(bi); tris.Add(tj); tris.Add(bj);
                        tris.Add(bi); tris.Add(ti); tris.Add(tj);
                    }
                }
            }

            AddSideLoop(outerLoop, false);
            if (holeLoops != null)
            {
                for (int i = 0; i < holeLoops.Count; i++) AddSideLoop(holeLoops[i], true);
            }

            var doubleSided = new int[tris.Count * 2];
            for (int i = 0; i < tris.Count; i++) doubleSided[i] = tris[i];
            for (int i = 0; i < tris.Count; i += 3)
            {
                doubleSided[tris.Count + i] = tris[i];
                doubleSided[tris.Count + i + 1] = tris[i + 2];
                doubleSided[tris.Count + i + 2] = tris[i + 1];
            }

            var mesh = new Mesh();
            mesh.vertices = verts.ToArray();
            mesh.triangles = doubleSided;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void AddPlateOutlineEdges(GameObject plateGo, List<Vector3> outerLoopLocal, List<List<Vector3>> holeLoopsLocal, Vector3 thicknessDirLocal, float thicknessVisual)
        {
            if (!drawPlateOutlineEdges) return;
            if (plateGo == null) return;
            if (plateOutlineMaterial == null) return;
            if (outerLoopLocal == null || outerLoopLocal.Count < 3) return;
            if (thicknessVisual <= 0f) return;

            var existing = plateGo.transform.Find("PlateOutline");
            if (existing != null) Destroy(existing.gameObject);

            var root = new GameObject("PlateOutline");
            root.transform.SetParent(plateGo.transform);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            thicknessDirLocal = thicknessDirLocal.sqrMagnitude > 1e-10f ? thicknessDirLocal.normalized : Vector3.up;
            Vector3 half = thicknessDirLocal * (thicknessVisual * 0.5f);

            void AddLoopEdges(List<Vector3> loop, int loopIndex)
            {
                if (loop == null || loop.Count < 3) return;
                var clean = RemoveConsecutiveDuplicates(loop);
                if (clean.Count >= 3 && (clean[0] - clean[clean.Count - 1]).sqrMagnitude < 1e-8f) clean.RemoveAt(clean.Count - 1);
                if (clean.Count < 3) return;

                var bottom = new Vector3[clean.Count];
                var top = new Vector3[clean.Count];
                for (int i = 0; i < clean.Count; i++)
                {
                    bottom[i] = clean[i] - half;
                    top[i] = clean[i] + half;
                }

                CreateLoopLine(root.transform, "Outline_Bottom_" + loopIndex, bottom, plateOutlineMaterial, plateOutlineWidth, Color.white);
                CreateLoopLine(root.transform, "Outline_Top_" + loopIndex, top, plateOutlineMaterial, plateOutlineWidth, Color.white);

                for (int i = 0; i < clean.Count; i++)
                {
                    CreateSegmentLine(root.transform, "Outline_V_" + loopIndex + "_" + i, bottom[i], top[i], plateOutlineMaterial, plateOutlineWidth, Color.white);
                }
            }

            AddLoopEdges(outerLoopLocal, 0);
            if (holeLoopsLocal != null && holeLoopsLocal.Count > 0)
            {
                for (int i = 0; i < holeLoopsLocal.Count; i++) AddLoopEdges(holeLoopsLocal[i], i + 1);
            }
        }

        private void AddPlateSeamLines(GameObject plateGo, List<List<Vector3>> seamPolylinesWorld)
        {
            if (!drawPlateSeams) return;
            if (plateGo == null) return;
            if (seamLineMaterial == null) return;
            if (seamPolylinesWorld == null || seamPolylinesWorld.Count == 0) return;

            var existing = plateGo.transform.Find("PlateSeams");
            if (existing != null) Destroy(existing.gameObject);

            var root = new GameObject("PlateSeams");
            root.transform.SetParent(plateGo.transform);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            for (int i = 0; i < seamPolylinesWorld.Count; i++)
            {
                var polyWorld = seamPolylinesWorld[i];
                if (polyWorld == null || polyWorld.Count < 2) continue;
                var ptsLocal = new Vector3[polyWorld.Count];
                for (int k = 0; k < polyWorld.Count; k++) ptsLocal[k] = plateGo.transform.InverseTransformPoint(polyWorld[k]);
                CreatePolylineLine(root.transform, "Seam_" + i, ptsLocal, seamLineMaterial, seamLineWidth, Color.black);
            }
        }

        private void CreateLoopLine(Transform parent, string name, Vector3[] pts, Material mat, float width, Color color)
        {
            if (pts == null || pts.Length < 3) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.material = mat;
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = color;
            lr.endColor = color;
            lr.numCornerVertices = 0;
            lr.numCapVertices = 0;
            lr.alignment = LineAlignment.View;
        }

        private void CreatePolylineLine(Transform parent, string name, Vector3[] pts, Material mat, float width, Color color)
        {
            if (pts == null || pts.Length < 2) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.material = mat;
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = color;
            lr.endColor = color;
            lr.numCornerVertices = 0;
            lr.numCapVertices = 0;
            lr.alignment = LineAlignment.View;
        }

        private void CreateSegmentLine(Transform parent, string name, Vector3 a, Vector3 b, Material mat, float width, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.material = mat;
            lr.positionCount = 2;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = color;
            lr.endColor = color;
            lr.numCornerVertices = 0;
            lr.numCapVertices = 0;
            lr.alignment = LineAlignment.View;
        }

        // --- 精确实体构建：加劲材 ---
        private void BuildPreciseStiffener(string id, string name, string guidRef, string matRef, string sec, string cutCode, Dictionary<string, string> cutParams, Vector3 startPt, Vector3 endPt, Vector3 centroidWorld, Vector3 webDir, Vector3 flangeDir, SectionProfile secProfile, Transform parent, string sourcePath, string schemaLevel = "S2", float dryWeightKg = 0f, string materialName = null)
        {
            GameObject go = new GameObject(id);
            go.transform.SetParent(parent);

            Vector3 axisZ = endPt - startPt;
            float length = axisZ.magnitude;
            if (length < 1e-6f) return;
            axisZ /= length;

            Vector3 axisY = webDir;
            if (axisY.sqrMagnitude < 1e-10f)
            {
                axisY = Vector3.up;
                if (Mathf.Abs(Vector3.Dot(axisY, axisZ)) > 0.95f) axisY = Vector3.right;
            }
            axisY = axisY - Vector3.Dot(axisY, axisZ) * axisZ;
            if (axisY.sqrMagnitude < 1e-10f) axisY = Vector3.Cross(axisZ, Vector3.up);
            axisY.Normalize();

            Vector3 axisX = flangeDir;
            if (axisX.sqrMagnitude < 1e-10f)
            {
                axisX = Vector3.Cross(axisY, axisZ);
            }
            axisX = axisX - Vector3.Dot(axisX, axisZ) * axisZ - Vector3.Dot(axisX, axisY) * axisY;
            if (axisX.sqrMagnitude < 1e-10f) axisX = Vector3.Cross(axisY, axisZ);
            axisX.Normalize();

            Vector3 expectedX = Vector3.Cross(axisY, axisZ);
            if (Vector3.Dot(axisX, expectedX) < 0f) axisX = -axisX;

            Vector3 anchor = centroidWorld != Vector3.zero ? centroidWorld : (startPt + endPt) * 0.5f;
            go.transform.position = anchor;
            var rot = Quaternion.LookRotation(axisZ, axisY);
            if (flangeDir.sqrMagnitude > 1e-10f)
            {
                Vector3 right = rot * Vector3.right;
                if (Vector3.Dot(right, flangeDir) < 0f) rot = rot * Quaternion.AngleAxis(180f, Vector3.up);
            }
            go.transform.rotation = rot;

            float s = ModelVisualScale;
            float h = 0.15f * s;
            float w = 0.15f * s;
            float webThk = 0.01f * s;
            float flangeThk = 0.01f * s;

            if (secProfile != null)
            {
                if (secProfile.Kind == SectionProfileKind.FlatBar)
                {
                    if (secProfile.Height > 0f) h = secProfile.Height * s;
                    if (secProfile.Width > 0f) { w = secProfile.Width * s; webThk = secProfile.Width * s; }
                }
                else if (secProfile.Kind == SectionProfileKind.LBar)
                {
                    if (secProfile.Height > 0f) h = secProfile.Height * s;
                    if (secProfile.Width > 0f) w = secProfile.Width * s;
                    if (secProfile.WebThickness > 0f) webThk = secProfile.WebThickness * s;
                    if (secProfile.FlangeThickness > 0f) flangeThk = secProfile.FlangeThickness * s;
                }
            }

            var poly = new List<Vector3>();
            if (secProfile != null && secProfile.Kind == SectionProfileKind.LBar)
            {
                float y0 = 0f;
                float y1 = Mathf.Max(0f, h);
                float x0 = 0f;
                float xWeb = Mathf.Clamp(webThk, 0.0001f, Mathf.Max(0.0001f, w));
                float xW = Mathf.Max(xWeb, w);
                float yFlange = Mathf.Clamp(y1 - flangeThk, 0f, y1);

                poly.Add(new Vector3(x0, y0, 0f));
                poly.Add(new Vector3(xWeb, y0, 0f));
                poly.Add(new Vector3(xWeb, yFlange, 0f));
                poly.Add(new Vector3(xW, yFlange, 0f));
                poly.Add(new Vector3(xW, y1, 0f));
                poly.Add(new Vector3(x0, y1, 0f));
            }
            else
            {
                float y0 = 0f;
                float y1 = Mathf.Max(0f, h);
                float x0 = 0f;
                float x1 = Mathf.Max(0.0001f, w);
                poly.Add(new Vector3(x0, y0, 0f));
                poly.Add(new Vector3(x1, y0, 0f));
                poly.Add(new Vector3(x1, y1, 0f));
                poly.Add(new Vector3(x0, y1, 0f));
            }

            float area = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                area += a.x * b.y - b.x * a.y;
            }
            if (area < 0f) poly.Reverse();

            var capTris = TriangulateLocalPolygon(poly, Vector3.forward);
            if (capTris == null || capTris.Length < 3) capTris = new int[0];

            int n = poly.Count;
            var verts = new Vector3[n * 2];
            for (int i = 0; i < n; i++)
            {
                verts[i] = new Vector3(poly[i].x, poly[i].y, -length * 0.5f);
                verts[n + i] = new Vector3(poly[i].x, poly[i].y, length * 0.5f);
            }

            var tris = new List<int>(capTris.Length * 2 + n * 6);
            for (int i = 0; i < capTris.Length; i += 3)
            {
                tris.Add(capTris[i]);
                tris.Add(capTris[i + 2]);
                tris.Add(capTris[i + 1]);
            }
            for (int i = 0; i < capTris.Length; i += 3)
            {
                tris.Add(n + capTris[i]);
                tris.Add(n + capTris[i + 1]);
                tris.Add(n + capTris[i + 2]);
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int a = i;
                int b = j;
                int c = n + j;
                int d = n + i;
                tris.Add(a);
                tris.Add(b);
                tris.Add(c);
                tris.Add(a);
                tris.Add(c);
                tris.Add(d);
            }

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            if (stiffenerMaterial) mr.sharedMaterial = stiffenerMaterial;
            SetRendererColor(mr, new Color(1.00f, 0.85f, 0.10f, 1f));

            var mesh = new Mesh();
            mesh.vertices = verts;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mf.mesh = mesh;

            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;

            var data = go.AddComponent<PartData>();
            data.SchemaLevel = schemaLevel;
            data.PartId = id;
            data.GuidRef = guidRef;
            data.GeometryName = string.IsNullOrEmpty(name) ? id : name;
            data.PartType = "Stiffener";
            data.MaterialRef = matRef;
            data.MaterialName = materialName;
            data.SectionRef = (secProfile != null && !string.IsNullOrEmpty(secProfile.Name)) ? secProfile.Name : sec;
            if (secProfile != null)
            {
                data.SectionHeight = secProfile.Height;
                data.SectionWidth = secProfile.Width;
                data.SectionWebThickness = secProfile.WebThickness;
                data.SectionFlangeThickness = secProfile.FlangeThickness;
            }
            data.EndCutCode = cutCode;
            if (cutParams != null) data.EndCutParams = cutParams;
            data.Weight = dryWeightKg > 0f ? dryWeightKg : 0f;
            data.SourceFilePath = sourcePath;
            data.SourceElementType = "Stiffener";
        }

        public void ApplyMaterialToObject(GameObject go, string matRef)
        {
            if (go == null) return;
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var pd = go.GetComponent<PartData>();
            if (pd != null && string.Equals(pd.PartType, "Stiffener", StringComparison.OrdinalIgnoreCase))
            {
                SetRendererColor(r, new Color(1.00f, 0.85f, 0.10f, 1f));
                return;
            }
            ApplyColor(r, matRef);
        }

        public void ExportEdited3docx()
        {
            foreach (var loaded in loadedDocuments)
            {
                if (loaded?.Doc == null || string.IsNullOrEmpty(loaded.SourcePath) || loaded.FileGroup == null) continue;

                foreach (var pd in loaded.FileGroup.GetComponentsInChildren<PartData>(true))
                {
                    if (pd == null || string.IsNullOrEmpty(pd.PartId)) continue;
                    if (string.Equals(pd.SourceElementType ?? "", "Plate", StringComparison.OrdinalIgnoreCase) && loaded.PlatesById.TryGetValue(pd.PartId, out var plateEl))
                    {
                        var pm = plateEl.Element(loaded.Ocx + "PlateMaterial");
                        if (pm == null) continue;
                        if (!string.IsNullOrEmpty(pd.MaterialRef))
                        {
                            var cur = pm.Attribute("localRef")?.Value;
                            if (cur != pd.MaterialRef) pm.SetAttributeValue("localRef", pd.MaterialRef);
                        }
                        UpsertThickness(plateEl, loaded.Ocx, pd.Thickness);
                    }
                    else if (string.Equals(pd.SourceElementType ?? "", "Stiffener", StringComparison.OrdinalIgnoreCase) && loaded.StiffenersById.TryGetValue(pd.PartId, out var stiffEl))
                    {
                        var mr = stiffEl.Element(loaded.Ocx + "MaterialRef");
                        if (mr != null && !string.IsNullOrEmpty(pd.MaterialRef)) mr.SetAttributeValue("localRef", pd.MaterialRef);
                        var sr = stiffEl.Element(loaded.Ocx + "SectionRef");
                        if (sr != null && !string.IsNullOrEmpty(pd.SectionRef)) sr.SetAttributeValue("localRef", pd.SectionRef);
                        var ec = stiffEl.Element(loaded.Ocx + "EndCutEnd1");
                        if (ec != null && !string.IsNullOrEmpty(pd.EndCutCode)) ec.SetAttributeValue("name", pd.EndCutCode);
                    }
                }

                string outPath;
                if (overwriteOriginalOnExport)
                {
                    outPath = loaded.SourcePath;
                }
                else
                {
                    string dir = Path.GetDirectoryName(loaded.SourcePath);
                    string name = Path.GetFileNameWithoutExtension(loaded.SourcePath);
                    string ext = Path.GetExtension(loaded.SourcePath);
                    if (string.IsNullOrEmpty(ext)) ext = ".3docx";
                    outPath = Path.Combine(dir ?? "", name + "_edited" + ext);
                }
                var settings = new XmlWriterSettings
                {
                    Encoding = loaded.SourceEncoding ?? new UTF8Encoding(false),
                    Indent = false,
                    NewLineHandling = NewLineHandling.None,
                    OmitXmlDeclaration = false
                };
                using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var xw = XmlWriter.Create(fs, settings))
                {
                    loaded.Doc.Save(xw);
                }
            }
        }

        public bool TrySaveProject(string projectPath, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(projectPath)) { error = "工程文件路径为空"; return false; }
            if (rootContainer == null) { error = "RootContainer 未初始化"; return false; }

            try
            {
                var proj = new OcxProjectFile
                {
                    Version = 1,
                    CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    OverwriteOriginalOnExport = overwriteOriginalOnExport
                };

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < loadedDocuments.Count; i++)
                {
                    var src = loadedDocuments[i]?.SourcePath;
                    if (string.IsNullOrWhiteSpace(src)) continue;
                    if (seen.Add(src)) proj.SourceFiles.Add(src);
                }

                var parts = rootContainer.GetComponentsInChildren<PartData>(true);
                for (int i = 0; i < parts.Length; i++)
                {
                    var pd = parts[i];
                    if (pd == null) continue;
                    string key = BuildPartKey(pd);
                    if (string.IsNullOrEmpty(key)) continue;
                    var t = pd.transform;
                    proj.Parts.Add(new OcxProjectPartState
                    {
                        Key = key,
                        Position = t.position,
                        Rotation = t.rotation,
                        Scale = t.localScale,
                        MaterialRef = pd.MaterialRef,
                        Thickness = pd.Thickness,
                        SectionRef = pd.SectionRef,
                        EndCutCode = pd.EndCutCode,
                        Weight = pd.Weight
                    });
                }

                string json = JsonUtility.ToJson(proj, true);
                File.WriteAllText(projectPath, json, new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool TryLoadProjectFile(string projectPath, out OcxProjectFile project, out string error)
        {
            project = null;
            error = "";
            if (string.IsNullOrWhiteSpace(projectPath)) { error = "工程文件路径为空"; return false; }
            if (!File.Exists(projectPath)) { error = "工程文件不存在"; return false; }

            try
            {
                string json = File.ReadAllText(projectPath, Encoding.UTF8);
                project = JsonUtility.FromJson<OcxProjectFile>(json);
                if (project == null) { error = "工程文件解析失败"; return false; }
                if (project.SourceFiles == null) project.SourceFiles = new List<string>();
                if (project.Parts == null) project.Parts = new List<OcxProjectPartState>();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void ApplyProjectState(OcxProjectFile project, out int appliedCount, out int missingCount)
        {
            appliedCount = 0;
            missingCount = 0;
            if (project == null) return;
            if (rootContainer == null) return;

            overwriteOriginalOnExport = project.OverwriteOriginalOnExport;

            var parts = rootContainer.GetComponentsInChildren<PartData>(true);
            var byKey = new Dictionary<string, PartData>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < parts.Length; i++)
            {
                var pd = parts[i];
                if (pd == null) continue;
                string key = BuildPartKey(pd);
                if (string.IsNullOrEmpty(key)) continue;
                if (!byKey.ContainsKey(key)) byKey[key] = pd;
            }

            for (int i = 0; i < project.Parts.Count; i++)
            {
                var s = project.Parts[i];
                if (s == null || string.IsNullOrEmpty(s.Key)) continue;
                if (!byKey.TryGetValue(s.Key, out var pd) || pd == null)
                {
                    missingCount++;
                    continue;
                }

                var t = pd.transform;
                t.position = s.Position;
                t.rotation = s.Rotation;
                t.localScale = s.Scale;
                pd.MaterialRef = s.MaterialRef;
                pd.Thickness = s.Thickness;
                pd.SectionRef = s.SectionRef;
                pd.EndCutCode = s.EndCutCode;
                pd.Weight = s.Weight;
                ApplyMaterialToObject(pd.gameObject, pd.MaterialRef);
                appliedCount++;
            }

            ModelsChanged?.Invoke();
        }

        private void UpsertThickness(XElement plateEl, XNamespace ocx, string thicknessText)
        {
            if (plateEl == null) return;
            if (string.IsNullOrEmpty(thicknessText) || thicknessText == "未记录") return;

            string[] parts = thicknessText.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string val = parts.Length > 0 ? parts[0] : thicknessText.Trim();
            string unit = parts.Length > 1 ? parts[1] : null;

            XElement th = null;
            var pm = plateEl.Element(ocx + "PlateMaterial");
            if (pm != null) th = pm.Element(ocx + "Thickness");

            if (th == null)
            {
                th = plateEl.Descendants().FirstOrDefault(e => e.Name.LocalName.ToLower().Contains("thickness"));
            }

            if (th == null && pm != null)
            {
                th = new XElement(ocx + "Thickness");
                pm.Add(th);
            }

            if (th == null) return;
            var curV = th.Attribute("numericvalue")?.Value;
            var curU = th.Attribute("unit")?.Value;
            if (curV != val) th.SetAttributeValue("numericvalue", val);
            if (!string.IsNullOrEmpty(unit) && curU != unit) th.SetAttributeValue("unit", unit);
        }

        private Encoding DetectEncoding(string path)
        {
            try
            {
                byte[] b = File.ReadAllBytes(path);
                if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return new UTF8Encoding(true);
                if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return new UnicodeEncoding(false, true); // UTF-16 LE BOM
                if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return new UnicodeEncoding(true, true); // UTF-16 BE BOM
                if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0x00 && b[3] == 0x00) return new UTF32Encoding(false, true);
                if (b.Length >= 4 && b[0] == 0x00 && b[1] == 0x00 && b[2] == 0xFE && b[3] == 0xFF) return new UTF32Encoding(true, true);
            }
            catch { }
            return new UTF8Encoding(false);
        }

        private float ParseThicknessToMeters(string thicknessText)
        {
            if (string.IsNullOrWhiteSpace(thicknessText) || thicknessText == "未记录") return 0f;
            var s = thicknessText.Trim();
            // 支持：12mm、12 mm、12毫米、12cm、12厘米、12m、12米、12
            string digits = "";
            string unit = "";
            foreach (char ch in s)
            {
                if ((ch >= '0' && ch <= '9') || ch == '.' || ch == ',') digits += (ch == ',' ? '.' : ch);
                else unit += ch;
            }
            if (!float.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
            {
                return 0f;
            }
            unit = unit.Trim().ToLower();
            if (string.IsNullOrEmpty(unit)) unit = "mm"; // 无单位默认 mm
            if (unit.Contains("mm") || unit.Contains("毫米")) return val / 1000f;
            if (unit.Contains("cm") || unit.Contains("厘米")) return val / 100f;
            // m 或 米 视为米
            return val;
        }

        private Vector3 EstimateNormal(List<Vector3> boundary)
        {
            Vector3 n = Vector3.zero;
            for (int i = 0; i < boundary.Count; i++)
            {
                Vector3 current = boundary[i];
                Vector3 next = boundary[(i + 1) % boundary.Count];
                n.x += (current.y - next.y) * (current.z + next.z);
                n.y += (current.z - next.z) * (current.x + next.x);
                n.z += (current.x - next.x) * (current.y + next.y);
            }
            if (n == Vector3.zero) n = Vector3.up;
            return n.normalized;
        }

        private Vector3 Average(List<Vector3> pts)
        {
            Vector3 s = Vector3.zero;
            foreach (var p in pts) s += p;
            return pts.Count > 0 ? s / pts.Count : s;
        }

        private Mesh CreateExtrudedMesh(List<Vector3> boundaryWorld, float thickness, Transform relative)
        {
            // 将世界坐标转换为相对变换的局部坐标
            var boundaryLocal = new List<Vector3>(boundaryWorld.Count);
            foreach (var p in boundaryWorld) boundaryLocal.Add(relative.InverseTransformPoint(p));

            Vector3 normal = EstimateNormal(boundaryLocal);
            Vector3 halfOffset = normal * (thickness * 0.5f);
            int n = boundaryWorld.Count;
            Vector3 centerBottom = Average(boundaryLocal);
            Vector3 centerTop = centerBottom + halfOffset;
            centerBottom = centerBottom - halfOffset;

            var vertices = new List<Vector3>();
            // centers
            vertices.Add(centerBottom);
            vertices.Add(centerTop);
            // bottom/top rings
            for (int i = 0; i < n; i++)
            {
                vertices.Add(boundaryLocal[i] - halfOffset); // bottom i
                vertices.Add(boundaryLocal[i] + halfOffset); // top i
            }

            var triangles = new List<int>();
            // bottom fan (center 0)
            for (int i = 0; i < n; i++)
            {
                int bi = 2 + i * 2;
                int biNext = 2 + ((i + 1) % n) * 2;
                triangles.Add(0); triangles.Add(biNext); triangles.Add(bi);
            }
            // top fan (center 1)
            for (int i = 0; i < n; i++)
            {
                int ti = 3 + i * 2;
                int tiNext = 3 + ((i + 1) % n) * 2;
                triangles.Add(1); triangles.Add(ti); triangles.Add(tiNext);
            }
            // sides
            for (int i = 0; i < n; i++)
            {
                int bi = 2 + i * 2;
                int ti = bi + 1;
                int biNext = 2 + ((i + 1) % n) * 2;
                int tiNext = biNext + 1;
                // quad: bi, biNext, tiNext, ti
                triangles.Add(bi); triangles.Add(biNext); triangles.Add(tiNext);
                triangles.Add(bi); triangles.Add(tiNext); triangles.Add(ti);
            }

            Mesh mesh = new Mesh();
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            return mesh;
        }

        public void RebuildPlateGeometry(PartData data)
        {
            if (data == null || !string.Equals(data.PartType ?? "", "Plate", StringComparison.OrdinalIgnoreCase)) return;
            var go = data.gameObject;
            if (go == null) return;
            if (data.Boundary == null || data.Boundary.Count < 3)
            {
                var lr = go.GetComponent<LineRenderer>();
                if (lr != null && lr.positionCount >= 3)
                {
                    var pts = new Vector3[lr.positionCount];
                    lr.GetPositions(pts);
                    data.Boundary = new List<Vector3>(pts);
                }
            }
            if (data.Boundary == null || data.Boundary.Count < 3) return;
            float th = ParseThicknessToMeters(data.Thickness);
            data.ThicknessValue = th;
            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.AddComponent<MeshFilter>();
            Mesh mesh = CreateExtrudedMesh(data.Boundary, th > 0f ? th : 0.01f, go.transform);
            mf.mesh = mesh;
            var mc = go.GetComponent<MeshCollider>();
            if (mc == null) mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
        }
        private void BuildMaterialColors(XDocument doc, XNamespace ocx)
        {
            materialColors.Clear();
            var mats = doc.Descendants(ocx + "Material");
            foreach (var m in mats)
            {
                string id = m.Attribute("id")?.Value ?? "";
                string name = m.Attribute("name")?.Value ?? id;
                materialColors[id] = ColorFromString(name);
            }
        }

        private void ApplyColor(Renderer renderer, string matRef)
        {
            if (renderer == null) return;
            var pd = renderer.GetComponent<PartData>();
            if (pd != null && string.Equals(pd.PartType, "Stiffener", StringComparison.OrdinalIgnoreCase))
            {
                SetRendererColor(renderer, new Color(1.00f, 0.85f, 0.10f, 1f));
                return;
            }
            if (!string.IsNullOrEmpty(matRef) && materialColors.TryGetValue(matRef, out var c))
            {
                SetRendererColor(renderer, c);
                return;
            }
            SetRendererColor(renderer, new Color(0.6f, 0.6f, 0.6f));
        }

        private void SetRendererColor(Renderer renderer, Color c)
        {
            if (renderer == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            mpb.Clear();
            renderer.GetPropertyBlock(mpb);
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_BaseColor")) mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            renderer.SetPropertyBlock(mpb);
        }

        private Color ColorFromString(string s)
        {
            int hash = s.Aggregate(0, (acc, ch) => acc * 31 + ch);
            float hue = Mathf.Abs(hash % 360) / 360f;
            float sat = 0.6f;
            float val = 0.9f;
            Color c = Color.HSVToRGB(hue, sat, val);
            return c;
        }

        //private void AutoFocusCamera()
        //{
        //    Renderer[] renderers = rootContainer.GetComponentsInChildren<Renderer>();
        //    if (renderers.Length == 0) return;

        //    Bounds bounds = renderers[0].bounds;
        //    foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);

        //    Camera mainCam = Camera.main;
        //    if (mainCam != null)
        //    {
        //        float distance = bounds.extents.magnitude / Mathf.Tan(mainCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        //        mainCam.transform.position = bounds.center + new Vector3(distance * 0.5f, bounds.extents.y + distance * 0.5f, -distance);
        //        mainCam.transform.LookAt(bounds.center);

        //        if (mainCam.TryGetComponent<CameraController>(out var camCtrl))
        //        {
        //            camCtrl.focusPoint = bounds.center;
        //        }
        //    }
        //}
        public void AutoFocusCamera()
        {
            Renderer[] renderers = rootContainer.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);

            var cams = FindSceneCameras();
            if (cams.Count == 0) return;
            ApplyFocusToCameras(cams, bounds, null, out _);
        }

        public void AutoFocusCameraOn(Transform group)
        {
            TryAutoFocusCameraOn(group, out _);
        }

        public bool TryAutoFocusCameraOn(Transform group, out string debugInfo)
        {
            debugInfo = "";
            if (group == null) { debugInfo = "group=null"; return false; }

            bool hasBounds = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            var renderers = group.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }

            if (!hasBounds)
            {
                var lrs = group.GetComponentsInChildren<LineRenderer>(true);
                foreach (var lr in lrs)
                {
                    if (lr == null || lr.positionCount <= 0) continue;
                    var pts = new Vector3[lr.positionCount];
                    lr.GetPositions(pts);
                    for (int i = 0; i < pts.Length; i++)
                    {
                        if (!hasBounds) { bounds = new Bounds(pts[i], Vector3.zero); hasBounds = true; }
                        else bounds.Encapsulate(pts[i]);
                    }
                }
            }

            if (!hasBounds) { debugInfo = "bounds=empty"; return false; }

            var cams = FindSceneCameras();
            if (cams.Count == 0) { debugInfo = "camera=none"; return false; }

            ApplyFocusToCameras(cams, bounds, group, out var targetPos);
            debugInfo = $"group={group.name} cams={cams.Count} boundsCenter={bounds.center} extents={bounds.extents} targetPos={targetPos}";
            return true;
        }

        private List<Camera> FindSceneCameras()
        {
            var primary = EnsureSingleActiveCamera();
            var list = new List<Camera>();
            if (primary != null) list.Add(primary);
            return list;
        }

        private void ApplyFocusToCameras(List<Camera> cameras, Bounds bounds, Transform targetRoot, out Vector3 targetPos)
        {
            ApplyFocusToCameras(cameras, bounds, targetRoot, null, out targetPos);
        }

        private void ApplyFocusToCameras(List<Camera> cameras, Bounds bounds, Transform targetRoot, HashSet<Collider> targetCollidersOverride, out Vector3 targetPos)
        {
            targetPos = Vector3.zero;
            if (cameras == null || cameras.Count == 0) return;

            float focusScale = Mathf.Max(1f, ModelVisualScale);
            bounds.extents = bounds.extents / focusScale;

            var camForDistance = cameras[0];
            float vfov = camForDistance.orthographic ? 60f : camForDistance.fieldOfView;
            float aspect = Mathf.Max(0.01f, camForDistance.aspect);
            float vfovRad = Mathf.Max(0.01f, vfov * Mathf.Deg2Rad);
            float hfovRad = 2f * Mathf.Atan(Mathf.Tan(vfovRad * 0.5f) * aspect);
            float distV = bounds.extents.y / Mathf.Tan(vfovRad * 0.5f);
            float distH = bounds.extents.x / Mathf.Tan(hfovRad * 0.5f);
            float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
            float distSphereV = radius / Mathf.Max(0.01f, Mathf.Sin(vfovRad * 0.5f));
            float distSphereH = radius / Mathf.Max(0.01f, Mathf.Sin(hfovRad * 0.5f));
            float baseDistance = Mathf.Max(distV, distH, distSphereV, distSphereH, 0.8f);

            HashSet<Collider> targetColliders = targetCollidersOverride;
            if (targetColliders == null && targetRoot != null)
            {
                var cols = targetRoot.GetComponentsInChildren<Collider>(true);
                if (cols != null && cols.Length > 0)
                {
                    targetColliders = new HashSet<Collider>();
                    for (int i = 0; i < cols.Length; i++) if (cols[i] != null) targetColliders.Add(cols[i]);
                }
            }

            var cam = cameras[0];
            if (cam == null) return;

            Vector3 center = bounds.center;
            Vector3 fromCenter = cam.transform.position - center;
            Vector3 baseDir = fromCenter.sqrMagnitude > 1e-6f ? fromCenter.normalized : (Vector3.back + Vector3.up).normalized;

            var samplePoints = BuildBoundsSamplePoints(bounds);
            var dirs = BuildFocusDirections(baseDir);

            Vector3 bestPos = center + baseDir * baseDistance;
            Vector3 bestDir = baseDir;
            int bestScore = -1;
            int total = samplePoints.Count;

            float bestTie = float.NegativeInfinity;
            for (int i = 0; i < dirs.Count; i++)
            {
                var d = dirs[i];
                if (d.sqrMagnitude < 1e-6f) continue;
                d.Normalize();

                float elev01 = Mathf.Clamp01(Vector3.Dot(d, Vector3.up));
                if (elev01 < 0.20f || elev01 > 0.82f) continue;

                float required = ComputeFitDistance(bounds, d, vfovRad, hfovRad);
                float distance = Mathf.Max(baseDistance, required) * 1.06f;
                Vector3 pos = center + d * distance;
                int score = GetClearViewScore(pos, samplePoints, targetRoot, targetColliders);
                float tie = -Mathf.Abs(elev01 - 0.52f);
                if (score > bestScore || (score == bestScore && tie > bestTie))
                {
                    bestScore = score;
                    bestTie = tie;
                    bestPos = pos;
                    bestDir = d;
                    if (bestScore >= total) break;
                }
            }

            if (bestScore < total)
            {
                float minDist = Mathf.Max(baseDistance, ComputeFitDistance(bounds, bestDir, vfovRad, hfovRad)) * 1.02f;
                float d = Vector3.Distance(bestPos, center);
                for (int step = 0; step < 10; step++)
                {
                    d = Mathf.Max(minDist, d * 0.82f);
                    Vector3 pos = center + bestDir * d;
                    int score = GetClearViewScore(pos, samplePoints, targetRoot, targetColliders);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPos = pos;
                        if (bestScore >= total) break;
                    }
                    if (Mathf.Abs(d - minDist) < 1e-4f) break;
                }
            }

            targetPos = bestPos;

            if (cam.TryGetComponent<CameraController>(out var camCtrl))
            {
                if (camCtrl.isOrbitMode)
                {
                    camCtrl.UpdateOrbitFromFocus(bounds.center, targetPos);
                    camCtrl.focusPoint = bounds.center;
                }
                else
                {
                    camCtrl.TeleportTo(targetPos, bounds.center);
                    camCtrl.focusPoint = bounds.center;
                }
            }
            else
            {
                cam.transform.position = targetPos;
                cam.transform.LookAt(bounds.center);
            }

            if (verboseAutoFocusLogging)
            {
                Debug.Log($"[AutoFocusCamera] cam={cam.name} depth={cam.depth} enabled={cam.isActiveAndEnabled} clear={cam.clearFlags} pos={cam.transform.position}");
            }
        }

        private float ComputeFitDistance(Bounds bounds, Vector3 dirFromCenter, float vfovRad, float hfovRad)
        {
            if (dirFromCenter.sqrMagnitude < 1e-6f) dirFromCenter = (Vector3.back + Vector3.up).normalized;
            dirFromCenter.Normalize();
            var rot = Quaternion.LookRotation(-dirFromCenter, Vector3.up);

            var c = bounds.center;
            var e = bounds.extents;
            float maxX = 0f;
            float maxY = 0f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        var p = c + new Vector3(e.x * sx, e.y * sy, e.z * sz);
                        var local = Quaternion.Inverse(rot) * (p - c);
                        maxX = Mathf.Max(maxX, Mathf.Abs(local.x));
                        maxY = Mathf.Max(maxY, Mathf.Abs(local.y));
                    }
                }
            }

            float distH = maxX / Mathf.Max(0.01f, Mathf.Tan(hfovRad * 0.5f));
            float distV = maxY / Mathf.Max(0.01f, Mathf.Tan(vfovRad * 0.5f));
            return Mathf.Max(distH, distV, 0.35f);
        }

        public bool TryAutoFocusCameraOnObjects(IReadOnlyList<GameObject> objects, out string debugInfo)
        {
            debugInfo = "";
            if (objects == null || objects.Count == 0) { debugInfo = "objects=empty"; return false; }

            bool hasBounds = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            var targetColliders = new HashSet<Collider>();

            for (int i = 0; i < objects.Count; i++)
            {
                var go = objects[i];
                if (go == null) continue;

                var renderers = go.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    var rr = renderers[r];
                    if (rr == null) continue;
                    if (!hasBounds) { bounds = rr.bounds; hasBounds = true; }
                    else bounds.Encapsulate(rr.bounds);
                }

                var lrs = go.GetComponentsInChildren<LineRenderer>(true);
                for (int lr = 0; lr < lrs.Length; lr++)
                {
                    var line = lrs[lr];
                    if (line == null || line.positionCount <= 0) continue;
                    var pts = new Vector3[line.positionCount];
                    line.GetPositions(pts);
                    for (int p = 0; p < pts.Length; p++)
                    {
                        if (!hasBounds) { bounds = new Bounds(pts[p], Vector3.zero); hasBounds = true; }
                        else bounds.Encapsulate(pts[p]);
                    }
                }

                var cols = go.GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < cols.Length; c++)
                {
                    if (cols[c] != null) targetColliders.Add(cols[c]);
                }
            }

            if (!hasBounds) { debugInfo = "bounds=empty"; return false; }

            var cams = FindSceneCameras();
            if (cams.Count == 0) { debugInfo = "camera=none"; return false; }

            if (targetColliders.Count == 0) targetColliders = null;
            ApplyFocusToCameras(cams, bounds, null, targetColliders, out var targetPos);
            debugInfo = $"objects={objects.Count} cams={cams.Count} boundsCenter={bounds.center} extents={bounds.extents} targetPos={targetPos}";
            return true;
        }

        private List<Vector3> BuildBoundsSamplePoints(Bounds b)
        {
            var pts = new List<Vector3>(27);
            var c = b.center;
            var e = b.extents;

            pts.Add(c);

            var corners = new Vector3[8]
            {
                c + new Vector3(+e.x, +e.y, +e.z),
                c + new Vector3(+e.x, +e.y, -e.z),
                c + new Vector3(+e.x, -e.y, +e.z),
                c + new Vector3(+e.x, -e.y, -e.z),
                c + new Vector3(-e.x, +e.y, +e.z),
                c + new Vector3(-e.x, +e.y, -e.z),
                c + new Vector3(-e.x, -e.y, +e.z),
                c + new Vector3(-e.x, -e.y, -e.z)
            };
            for (int i = 0; i < corners.Length; i++) pts.Add(corners[i]);

            pts.Add(c + new Vector3(+e.x, 0, 0));
            pts.Add(c + new Vector3(-e.x, 0, 0));
            pts.Add(c + new Vector3(0, +e.y, 0));
            pts.Add(c + new Vector3(0, -e.y, 0));
            pts.Add(c + new Vector3(0, 0, +e.z));
            pts.Add(c + new Vector3(0, 0, -e.z));

            int start = 1;
            int end = start + 8;
            for (int i = start; i < end; i++)
            {
                for (int j = i + 1; j < end; j++)
                {
                    var a = pts[i];
                    var b2 = pts[j];
                    int same = 0;
                    if (Mathf.Abs(a.x - b2.x) < 1e-6f) same++;
                    if (Mathf.Abs(a.y - b2.y) < 1e-6f) same++;
                    if (Mathf.Abs(a.z - b2.z) < 1e-6f) same++;
                    if (same == 2) pts.Add((a + b2) * 0.5f);
                    if (pts.Count >= 27) return pts;
                }
            }

            return pts;
        }

        private List<Vector3> BuildFocusDirections(Vector3 baseDir)
        {
            var dirs = new List<Vector3>();
            void Add(Vector3 v)
            {
                if (v.sqrMagnitude < 1e-6f) return;
                v.Normalize();
                for (int i = 0; i < dirs.Count; i++)
                {
                    if (Vector3.Dot(dirs[i], v) > 0.985f) return;
                }
                dirs.Add(v);
            }

            Add(baseDir);
            Add((baseDir + Vector3.up * 0.65f));
            Add((Vector3.back + Vector3.up));
            Add((Vector3.forward + Vector3.up));
            Add((Vector3.left + Vector3.up));
            Add((Vector3.right + Vector3.up));

            var flat = Vector3.ProjectOnPlane(baseDir, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
            flat.Normalize();

            float[] ringElev = { 22f, 30f, 38f, 46f, 54f };
            for (int e = 0; e < ringElev.Length; e++)
            {
                float elev = ringElev[e];
                float upW = Mathf.Tan(elev * Mathf.Deg2Rad);
                for (int yaw = 0; yaw < 360; yaw += 30)
                {
                    var d = Quaternion.AngleAxis(yaw, Vector3.up) * flat;
                    Add(d + Vector3.up * upW);
                }
            }

            float[] yawAngles = { 15f, 30f, 45f, 60f, 75f, 90f, 120f, 150f, 180f };
            for (int i = 0; i < yawAngles.Length; i++)
            {
                float a = yawAngles[i];
                Add((Quaternion.AngleAxis(+a, Vector3.up) * baseDir + Vector3.up * 0.55f));
                Add((Quaternion.AngleAxis(-a, Vector3.up) * baseDir + Vector3.up * 0.55f));
            }

            return dirs;
        }

        private int GetClearViewScore(Vector3 camPos, List<Vector3> targetPoints, Transform targetRoot, HashSet<Collider> targetColliders)
        {
            if (targetPoints == null || targetPoints.Count == 0) return 0;
            int score = 0;
            for (int i = 0; i < targetPoints.Count; i++)
            {
                if (RayHitsTargetFirst(camPos, targetPoints[i], targetRoot, targetColliders)) score++;
            }
            return score;
        }

        private bool RayHitsTargetFirst(Vector3 camPos, Vector3 targetPoint, Transform targetRoot, HashSet<Collider> targetColliders)
        {
            Vector3 dir = targetPoint - camPos;
            float dist = dir.magnitude;
            if (dist < 1e-4f) return true;
            dir /= dist;

            int mask = ~0;
            mask &= ~(1 << 31);
            var hits = Physics.RaycastAll(camPos, dir, dist, mask, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return true;
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                var col = hits[i].collider;
                if (col == null) continue;
                if (targetColliders != null) return targetColliders.Contains(col);
                if (targetRoot != null) return col.transform != null && col.transform.IsChildOf(targetRoot);
                return false;
            }
            return true;
        }

        private Camera EnsureSingleActiveCamera()
        {
            var cams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            Camera preferred = Camera.main;
            if (preferred == null)
            {
                var ctrl = FindAnyObjectByType<CameraController>();
                if (ctrl != null) preferred = ctrl.GetComponent<Camera>();
            }
            if (preferred == null)
            {
                for (int i = 0; i < cams.Length; i++)
                {
                    if (cams[i] != null) { preferred = cams[i]; break; }
                }
            }
            if (preferred == null) return null;

            for (int i = 0; i < cams.Length; i++)
            {
                var c = cams[i];
                if (c == null) continue;
                string cn = c.gameObject != null ? c.gameObject.name : "";
                if (cn == "SectionMiniViewCamera" || cn == "SectionBackgroundCamera") continue;
                c.enabled = (c == preferred);
                if (c != preferred)
                {
                    var al = c.GetComponent<AudioListener>();
                    if (al != null) al.enabled = false;
                }
            }

            preferred.enabled = true;
            preferred.targetDisplay = 0;
            try
            {
                preferred.gameObject.tag = "MainCamera";
            }
            catch { }

            var preferredListener = preferred.GetComponent<AudioListener>();
            if (preferredListener != null) preferredListener.enabled = true;

            return preferred;
        }

        public void EnterSectionViewX() => EnterSectionViewInternal('X');
        public void EnterSectionViewY() => EnterSectionViewInternal('Y');
        public void EnterSectionViewZ() => EnterSectionViewInternal('Z');
        public void EnterSectionBoxView() => EnterSectionBoxViewInternal();
        public void SetSectionGizmoVisible(bool visible)
        {
            var cam = EnsureSingleActiveCamera();
            if (cam != null && cam.TryGetComponent<CameraController>(out var camCtrl))
            {
                camCtrl.SetSectionGizmoVisible(visible);
            }
        }

        public void ExitSectionView()
        {
            var cam = EnsureSingleActiveCamera();
            if (cam != null && cam.TryGetComponent<CameraController>(out var camCtrl))
            {
                camCtrl.ExitSectionView();
            }
            var mini = cam != null ? cam.GetComponent<SectionBoxMiniView>() : null;
            if (mini != null) Destroy(mini);
            RestoreSectionCulling();
            RestoreSectionModelLayers();
        }

        private struct SavedLayer
        {
            public GameObject Go;
            public int Layer;
        }

        private readonly List<SavedLayer> savedSectionLayers = new List<SavedLayer>();
        private bool sectionLayersApplied;

        private struct SavedRendererState
        {
            public Renderer R;
            public bool Enabled;
        }

        private struct SavedColliderState
        {
            public Collider C;
            public bool Enabled;
        }

        private readonly List<SavedRendererState> savedSectionRenderers = new List<SavedRendererState>();
        private readonly List<SavedColliderState> savedSectionColliders = new List<SavedColliderState>();
        private readonly Dictionary<MeshFilter, Mesh> savedSectionMeshes = new Dictionary<MeshFilter, Mesh>();
        private readonly Dictionary<MeshFilter, Mesh> clippedSectionMeshes = new Dictionary<MeshFilter, Mesh>();
        private bool sectionCullingActive;
        private Bounds lastSectionCullingBounds;
        private Camera sectionCullingCamera;

        private void EnterSectionViewInternal(char axis)
        {
            var cam = EnsureSingleActiveCamera();
            if (cam == null) return;
            if (!cam.TryGetComponent<CameraController>(out var camCtrl)) return;
            if (!TryGetRootBounds(out var bounds)) return;

            ApplySectionModelLayers(camCtrl.sectionModelLayer);
            camCtrl.EnterSectionView(axis, bounds);
            camCtrl.SetSectionGizmoVisible(false);
            if (cam.GetComponent<SectionBoxMiniView>() == null) cam.gameObject.AddComponent<SectionBoxMiniView>();
            CacheSectionVisibilityStates();
            sectionCullingCamera = cam;
            ApplySectionCulling(camCtrl.GetSectionBoxBounds());
        }

        private void EnterSectionBoxViewInternal()
        {
            var cam = EnsureSingleActiveCamera();
            if (cam == null) return;
            if (!cam.TryGetComponent<CameraController>(out var camCtrl)) return;
            if (!TryGetRootBounds(out var bounds)) return;

            ApplySectionModelLayers(camCtrl.sectionModelLayer);
            camCtrl.EnterSectionView('B', bounds);
            camCtrl.SetSectionGizmoVisible(true);
            if (cam.GetComponent<SectionBoxMiniView>() == null) cam.gameObject.AddComponent<SectionBoxMiniView>();
            CacheSectionVisibilityStates();
            sectionCullingCamera = cam;
            ApplySectionCulling(camCtrl.GetSectionBoxBounds());
        }

        private void CacheSectionVisibilityStates()
        {
            if (rootContainer == null) return;
            if (sectionCullingActive) return;

            savedSectionRenderers.Clear();
            savedSectionColliders.Clear();
            savedSectionMeshes.Clear();
            foreach (var kv in clippedSectionMeshes)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }
            clippedSectionMeshes.Clear();

            var renderers = rootContainer.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                savedSectionRenderers.Add(new SavedRendererState { R = r, Enabled = r.enabled });
            }

            var colliders = rootContainer.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                var c = colliders[i];
                if (c == null) continue;
                savedSectionColliders.Add(new SavedColliderState { C = c, Enabled = c.enabled });
            }

            var meshFilters = rootContainer.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                var mf = meshFilters[i];
                if (mf == null) continue;
                var m = mf.sharedMesh;
                if (m == null) continue;
                if (!savedSectionMeshes.ContainsKey(mf)) savedSectionMeshes[mf] = m;
            }

            sectionCullingActive = true;
            lastSectionCullingBounds = new Bounds(new Vector3(999999f, 999999f, 999999f), Vector3.zero);
        }

        private void RestoreSectionCulling()
        {
            if (!sectionCullingActive) return;

            for (int i = 0; i < savedSectionRenderers.Count; i++)
            {
                var s = savedSectionRenderers[i];
                if (s.R == null) continue;
                s.R.enabled = s.Enabled;
            }

            for (int i = 0; i < savedSectionColliders.Count; i++)
            {
                var s = savedSectionColliders[i];
                if (s.C == null) continue;
                s.C.enabled = s.Enabled;
            }

            foreach (var kv in savedSectionMeshes)
            {
                if (kv.Key == null) continue;
                kv.Key.sharedMesh = kv.Value;
            }
            savedSectionMeshes.Clear();
            foreach (var kv in clippedSectionMeshes)
            {
                if (kv.Value != null) Destroy(kv.Value);
            }
            clippedSectionMeshes.Clear();

            savedSectionRenderers.Clear();
            savedSectionColliders.Clear();
            sectionCullingActive = false;
            sectionCullingCamera = null;
        }

        private void UpdateSectionCullingIfNeeded()
        {
            if (!sectionCullingActive) return;

            var cam = sectionCullingCamera != null ? sectionCullingCamera : Camera.main;
            if (cam == null) return;
            if (!cam.TryGetComponent<CameraController>(out var camCtrl)) return;
            if (!camCtrl.IsInSectionView()) return;

            var box = camCtrl.GetSectionBoxBounds();
            if (BoundsApproximatelyEqual(lastSectionCullingBounds, box, 0.0005f)) return;
            ApplySectionCulling(box);
        }

        private void ApplySectionCulling(Bounds box)
        {
            lastSectionCullingBounds = box;

            for (int i = 0; i < savedSectionRenderers.Count; i++)
            {
                var s = savedSectionRenderers[i];
                if (s.R == null) continue;
                bool inBox = box.Intersects(s.R.bounds);
                s.R.enabled = s.Enabled && inBox;

                if (s.R is MeshRenderer)
                {
                    var mf = s.R.GetComponent<MeshFilter>();
                    if (mf != null && savedSectionMeshes.TryGetValue(mf, out var original) && original != null)
                    {
                        if (s.Enabled && inBox)
                        {
                            var clipped = GetOrCreateClippedMesh(mf, original);
                            ClipMeshToWorldBox(original, mf.transform, box, clipped);
                            mf.sharedMesh = clipped;
                        }
                        else
                        {
                            mf.sharedMesh = original;
                        }
                    }
                }
            }

            for (int i = 0; i < savedSectionColliders.Count; i++)
            {
                var s = savedSectionColliders[i];
                if (s.C == null) continue;
                bool inBox = box.Intersects(s.C.bounds);
                s.C.enabled = s.Enabled && inBox;
            }
        }

        private Mesh GetOrCreateClippedMesh(MeshFilter mf, Mesh original)
        {
            if (mf == null) return null;
            if (clippedSectionMeshes.TryGetValue(mf, out var m) && m != null) return m;
            var nm = new Mesh
            {
                name = (original != null ? original.name : "Mesh") + "_SectionClipped",
                indexFormat = IndexFormat.UInt32
            };
            clippedSectionMeshes[mf] = nm;
            return nm;
        }

        private static void ClipMeshToWorldBox(Mesh source, Transform tf, Bounds worldBox, Mesh dest)
        {
            if (source == null || tf == null || dest == null) return;

            var planes = new Plane[6];
            Vector3 min = worldBox.min;
            Vector3 max = worldBox.max;
            planes[0] = new Plane(Vector3.right, min);
            planes[1] = new Plane(Vector3.left, max);
            planes[2] = new Plane(Vector3.up, min);
            planes[3] = new Plane(Vector3.down, max);
            planes[4] = new Plane(Vector3.forward, min);
            planes[5] = new Plane(Vector3.back, max);

            var v = source.vertices;
            var n = source.normals;
            var uv = source.uv;
            bool hasNormals = n != null && n.Length == v.Length;
            bool hasUv = uv != null && uv.Length == v.Length;

            int[] tris = source.triangles;
            if (tris == null || tris.Length < 3) return;

            var outVerts = new List<Vector3>(tris.Length);
            var outNormals = new List<Vector3>(tris.Length);
            var outUvs = new List<Vector2>(tris.Length);
            var outTris = new List<int>(tris.Length);

            Matrix4x4 l2w = tf.localToWorldMatrix;
            Matrix4x4 w2l = tf.worldToLocalMatrix;

            for (int t = 0; t < tris.Length; t += 3)
            {
                int i0 = tris[t + 0];
                int i1 = tris[t + 1];
                int i2 = tris[t + 2];
                if ((uint)i0 >= (uint)v.Length || (uint)i1 >= (uint)v.Length || (uint)i2 >= (uint)v.Length) continue;

                var poly = new List<Vtx>(3)
                {
                    MakeVtx(i0, v, n, uv, hasNormals, hasUv, l2w),
                    MakeVtx(i1, v, n, uv, hasNormals, hasUv, l2w),
                    MakeVtx(i2, v, n, uv, hasNormals, hasUv, l2w)
                };

                for (int p = 0; p < planes.Length; p++)
                {
                    poly = ClipPolygon(poly, planes[p]);
                    if (poly.Count < 3) break;
                }
                if (poly.Count < 3) continue;

                int baseIndex = outVerts.Count;
                for (int k = 0; k < poly.Count; k++)
                {
                    Vector3 lp = w2l.MultiplyPoint3x4(poly[k].posW);
                    outVerts.Add(lp);
                    if (hasNormals)
                    {
                        Vector3 ln = w2l.MultiplyVector(poly[k].normalW).normalized;
                        outNormals.Add(ln);
                    }
                    if (hasUv) outUvs.Add(poly[k].uv);
                }

                for (int k = 1; k + 1 < poly.Count; k++)
                {
                    outTris.Add(baseIndex);
                    outTris.Add(baseIndex + k);
                    outTris.Add(baseIndex + k + 1);
                }
            }

            dest.Clear();
            dest.SetVertices(outVerts);
            if (hasNormals) dest.SetNormals(outNormals);
            if (hasUv) dest.SetUVs(0, outUvs);
            dest.SetTriangles(outTris, 0, true);
            dest.RecalculateBounds();
        }

        private struct Vtx
        {
            public Vector3 posW;
            public Vector3 normalW;
            public Vector2 uv;
        }

        private static Vtx MakeVtx(int idx, Vector3[] v, Vector3[] n, Vector2[] uv, bool hasNormals, bool hasUv, Matrix4x4 l2w)
        {
            var r = new Vtx
            {
                posW = l2w.MultiplyPoint3x4(v[idx]),
                normalW = hasNormals ? l2w.MultiplyVector(n[idx]).normalized : Vector3.up,
                uv = hasUv ? uv[idx] : Vector2.zero
            };
            return r;
        }

        private static List<Vtx> ClipPolygon(List<Vtx> input, Plane plane)
        {
            var output = new List<Vtx>(input.Count + 2);
            if (input.Count == 0) return output;

            Vtx prev = input[input.Count - 1];
            bool prevInside = plane.GetDistanceToPoint(prev.posW) >= 0f;

            for (int i = 0; i < input.Count; i++)
            {
                Vtx curr = input[i];
                bool currInside = plane.GetDistanceToPoint(curr.posW) >= 0f;

                if (currInside)
                {
                    if (!prevInside)
                    {
                        output.Add(Intersect(prev, curr, plane));
                    }
                    output.Add(curr);
                }
                else if (prevInside)
                {
                    output.Add(Intersect(prev, curr, plane));
                }

                prev = curr;
                prevInside = currInside;
            }
            return output;
        }

        private static Vtx Intersect(Vtx a, Vtx b, Plane plane)
        {
            float da = plane.GetDistanceToPoint(a.posW);
            float db = plane.GetDistanceToPoint(b.posW);
            float t = da / (da - db);
            t = Mathf.Clamp01(t);
            return new Vtx
            {
                posW = Vector3.Lerp(a.posW, b.posW, t),
                normalW = Vector3.Lerp(a.normalW, b.normalW, t).normalized,
                uv = Vector2.Lerp(a.uv, b.uv, t)
            };
        }

        private static bool BoundsApproximatelyEqual(Bounds a, Bounds b, float eps)
        {
            return (a.center - b.center).sqrMagnitude <= eps * eps
                   && (a.size - b.size).sqrMagnitude <= eps * eps;
        }

        public bool TryGetRootBounds(out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            if (rootContainer == null) return false;

            bool hasBounds = false;
            var renderers = rootContainer.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }

            if (!hasBounds)
            {
                var lrs = rootContainer.GetComponentsInChildren<LineRenderer>(true);
                foreach (var lr in lrs)
                {
                    if (lr == null || lr.positionCount <= 0) continue;
                    var pts = new Vector3[lr.positionCount];
                    lr.GetPositions(pts);
                    for (int i = 0; i < pts.Length; i++)
                    {
                        if (!hasBounds) { bounds = new Bounds(pts[i], Vector3.zero); hasBounds = true; }
                        else bounds.Encapsulate(pts[i]);
                    }
                }
            }

            return hasBounds;
        }

        private void ApplySectionModelLayers(int modelLayer)
        {
            if (rootContainer == null) return;
            if (sectionLayersApplied) return;

            savedSectionLayers.Clear();
            var transforms = rootContainer.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null) continue;
                var go = t.gameObject;
                savedSectionLayers.Add(new SavedLayer { Go = go, Layer = go.layer });
                go.layer = modelLayer;
            }
            sectionLayersApplied = true;
        }

        private void RestoreSectionModelLayers()
        {
            if (!sectionLayersApplied) return;
            for (int i = 0; i < savedSectionLayers.Count; i++)
            {
                var item = savedSectionLayers[i];
                if (item.Go == null) continue;
                item.Go.layer = item.Layer;
            }
            savedSectionLayers.Clear();
            sectionLayersApplied = false;
        }
    }
}
