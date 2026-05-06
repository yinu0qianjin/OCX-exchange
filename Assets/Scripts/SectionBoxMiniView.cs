using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace Zhouxiangyang
{
    public class SectionBoxMiniView : MonoBehaviour
    {
        public int gizmoLayer = 31;
        public float boxAlpha = 0.2f;
        public float handleSize = 0.22f;
        public float handleScaleFactor = 0.03f;
        public float minBoxSize = 0.05f;

        private Camera mainCam;
        private CameraController mainCtrl;
        private GameObject gizmoRoot;
        private Transform boxTf;
        private Transform handleCenterTf;
        private Transform handleMinXTf;
        private Transform handleMaxXTf;
        private Transform handleMinYTf;
        private Transform handleMaxYTf;
        private Transform handleMinZTf;
        private Transform handleMaxZTf;
        private Material boxMat;
        private Material handleMatMin;
        private Material handleMatMax;
        private Material handleMatCenter;
        private bool setupCollisionIgnores;

        private enum DragMode
        {
            None,
            Move,
            MoveAxis,
            MinX,
            MaxX,
            MinY,
            MaxY,
            MinZ,
            MaxZ
        }

        private DragMode dragMode;
        private Plane dragPlane;
        private float dragAxisOffset;
        private Vector3 dragAxis;
        private Bounds dragStartBounds;
        private Vector3 dragStartPoint;

        void OnEnable()
        {
            mainCam = GetComponent<Camera>();
            mainCtrl = GetComponent<CameraController>();
        }

        void LateUpdate()
        {
            if (mainCtrl == null) mainCtrl = GetComponent<CameraController>();
            if (mainCam == null) mainCam = GetComponent<Camera>();
            if (mainCtrl == null || mainCam == null) return;

            if (!mainCtrl.IsInSectionView())
            {
                Cleanup();
                return;
            }

            gizmoLayer = mainCtrl.sectionGizmoLayer;
            EnsureGizmo();
            if (gizmoRoot != null) gizmoRoot.SetActive(mainCtrl.IsSectionGizmoVisible());
            if (!mainCtrl.IsSectionGizmoVisible())
            {
                dragMode = DragMode.None;
                return;
            }
            SyncGizmoFromMain();
            HandleGizmoInput();
        }

        private void Cleanup()
        {
            if (gizmoRoot != null)
            {
                Destroy(gizmoRoot);
                gizmoRoot = null;
                boxTf = null;
                handleCenterTf = null;
                handleMinXTf = null;
                handleMaxXTf = null;
                handleMinYTf = null;
                handleMaxYTf = null;
                handleMinZTf = null;
                handleMaxZTf = null;
                setupCollisionIgnores = false;
            }
        }

        private void EnsureGizmo()
        {
            if (gizmoRoot != null) return;

            gizmoRoot = new GameObject("SectionBoxGizmo");
            gizmoRoot.layer = gizmoLayer;

            var shader = Shader.Find("Sprites/Default");
            boxMat = new Material(shader);
            boxMat.color = new Color(0.35f, 0.75f, 1f, Mathf.Clamp01(boxAlpha));

            handleMatMin = new Material(shader);
            handleMatMin.color = new Color(1f, 0.35f, 0.35f, 1f);
            handleMatMax = new Material(shader);
            handleMatMax.color = new Color(0.35f, 1f, 0.35f, 1f);
            handleMatCenter = new Material(shader);
            handleMatCenter.color = new Color(0.35f, 0.65f, 1f, 1f);

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Box";
            box.transform.SetParent(gizmoRoot.transform, false);
            box.layer = gizmoLayer;
            var boxCol = box.GetComponent<Collider>();
            if (boxCol != null) boxCol.enabled = true;
            var boxR = box.GetComponent<Renderer>();
            if (boxR != null) boxR.sharedMaterial = boxMat;
            boxTf = box.transform;

            handleCenterTf = CreateHandle("HandleMove", handleMatCenter).transform;
            handleMinXTf = CreateHandle("HandleMinX", handleMatMin).transform;
            handleMaxXTf = CreateHandle("HandleMaxX", handleMatMax).transform;
            handleMinYTf = CreateHandle("HandleMinY", handleMatMin).transform;
            handleMaxYTf = CreateHandle("HandleMaxY", handleMatMax).transform;
            handleMinZTf = CreateHandle("HandleMinZ", handleMatMin).transform;
            handleMaxZTf = CreateHandle("HandleMaxZ", handleMatMax).transform;
            SetupGizmoCollisionIgnores();
        }

        private GameObject CreateHandle(string name, Material mat)
        {
            var h = GameObject.CreatePrimitive(PrimitiveType.Cube);
            h.name = name;
            h.transform.SetParent(gizmoRoot.transform, false);
            h.layer = gizmoLayer;
            h.transform.localScale = Vector3.one * Mathf.Max(0.01f, handleSize);
            var r = h.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
            return h;
        }

        private void SyncGizmoFromMain()
        {
            if (boxTf == null || handleCenterTf == null || handleMinXTf == null || handleMaxXTf == null || handleMinYTf == null || handleMaxYTf == null || handleMinZTf == null || handleMaxZTf == null) return;
            if (mainCam == null || mainCtrl == null) return;

            var b = mainCtrl.GetSectionBoxBounds();
            Vector3 c = b.center;
            Vector3 size = b.size;
            Vector3 e = b.extents;
            float handleScale = Mathf.Max(handleSize, b.size.magnitude * handleScaleFactor);

            boxTf.position = c;
            boxTf.rotation = Quaternion.identity;
            boxTf.localScale = new Vector3(Mathf.Max(0.001f, size.x), Mathf.Max(0.001f, size.y), Mathf.Max(0.001f, size.z));

            handleCenterTf.position = c;
            handleMinXTf.position = c + Vector3.left * e.x;
            handleMaxXTf.position = c + Vector3.right * e.x;
            handleMinYTf.position = c + Vector3.down * e.y;
            handleMaxYTf.position = c + Vector3.up * e.y;
            handleMinZTf.position = c + Vector3.back * e.z;
            handleMaxZTf.position = c + Vector3.forward * e.z;

            handleCenterTf.localScale = Vector3.one * handleScale;
            handleMinXTf.localScale = Vector3.one * handleScale;
            handleMaxXTf.localScale = Vector3.one * handleScale;
            handleMinYTf.localScale = Vector3.one * handleScale;
            handleMaxYTf.localScale = Vector3.one * handleScale;
            handleMinZTf.localScale = Vector3.one * handleScale;
            handleMaxZTf.localScale = Vector3.one * handleScale;
        }

        private void HandleGizmoInput()
        {
            if (mainCam == null) return;
            if (Mouse.current == null) return;

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                TryBeginDrag();
            }
            if (dragMode != DragMode.None)
            {
                if (Mouse.current.leftButton.isPressed)
                {
                    UpdateDrag();
                }
                else
                {
                    dragMode = DragMode.None;
                }
            }
        }

        private void TryBeginDrag()
        {
            if (mainCam == null || mainCtrl == null) return;
            Vector2 mp = Mouse.current.position.ReadValue();
            Ray ray = mainCam.ScreenPointToRay(mp);
            int mask = 1 << gizmoLayer;
            if (!Physics.Raycast(ray, out var hit, 100000f, mask)) return;

            var tf = hit.collider != null ? hit.collider.transform : null;
            if (tf == null) return;

            dragStartBounds = mainCtrl.GetSectionBoxBounds();
            dragStartPoint = hit.point;
            dragAxis = Vector3.zero;
            dragAxisOffset = 0f;

            if (tf == handleCenterTf)
            {
                dragMode = DragMode.Move;
            }
            else if (tf == boxTf)
            {
                dragMode = DragMode.MoveAxis;
                dragAxis = GetDominantAxis(hit.normal);
            }
            else if (tf == handleMinXTf)
            {
                dragMode = DragMode.MinX;
                dragAxis = Vector3.right;
                float axisHit = Vector3.Dot(hit.point, dragAxis);
                dragAxisOffset = dragStartBounds.min.x - axisHit;
            }
            else if (tf == handleMaxXTf)
            {
                dragMode = DragMode.MaxX;
                dragAxis = Vector3.right;
                float axisHit = Vector3.Dot(hit.point, dragAxis);
                dragAxisOffset = dragStartBounds.max.x - axisHit;
            }
            else if (tf == handleMinYTf)
            {
                dragMode = DragMode.MinY;
                dragAxis = Vector3.up;
                float axisHit = Vector3.Dot(hit.point, dragAxis);
                dragAxisOffset = dragStartBounds.min.y - axisHit;
            }
            else if (tf == handleMaxYTf)
            {
                dragMode = DragMode.MaxY;
                dragAxis = Vector3.up;
                float axisHit = Vector3.Dot(hit.point, dragAxis);
                dragAxisOffset = dragStartBounds.max.y - axisHit;
            }
            else if (tf == handleMinZTf)
            {
                dragMode = DragMode.MinZ;
                dragAxis = Vector3.forward;
                float axisHit = Vector3.Dot(hit.point, dragAxis);
                dragAxisOffset = dragStartBounds.min.z - axisHit;
            }
            else if (tf == handleMaxZTf)
            {
                dragMode = DragMode.MaxZ;
                dragAxis = Vector3.forward;
                float axisHit = Vector3.Dot(hit.point, dragAxis);
                dragAxisOffset = dragStartBounds.max.z - axisHit;
            }
            else
            {
                dragMode = DragMode.None;
            }

            if (dragMode != DragMode.None)
            {
                dragPlane = new Plane(mainCam.transform.forward, hit.point);
            }
        }

        private void UpdateDrag()
        {
            if (mainCam == null || mainCtrl == null) return;
            if (Mouse.current == null) return;

            Vector2 mp = Mouse.current.position.ReadValue();
            Ray ray = mainCam.ScreenPointToRay(mp);
            if (!dragPlane.Raycast(ray, out float enter)) return;
            Vector3 p = ray.GetPoint(enter);

            var root = mainCtrl.GetSectionRootBounds();
            Vector3 rootMin = root.min;
            Vector3 rootMax = root.max;

            Vector3 min = dragStartBounds.min;
            Vector3 max = dragStartBounds.max;

            if (dragMode == DragMode.Move)
            {
                Vector3 delta = p - dragStartPoint;
                min += delta;
                max += delta;
            }
            else if (dragMode == DragMode.MoveAxis)
            {
                Vector3 axis = dragAxis.sqrMagnitude < 1e-6f ? Vector3.right : dragAxis.normalized;
                float delta = Vector3.Dot(p - dragStartPoint, axis);
                Vector3 move = axis * delta;
                min += move;
                max += move;
            }
            else
            {
                float axis = Vector3.Dot(p, dragAxis) + dragAxisOffset;
                if (dragMode == DragMode.MinX) min.x = Mathf.Clamp(axis, rootMin.x, max.x - minBoxSize);
                else if (dragMode == DragMode.MaxX) max.x = Mathf.Clamp(axis, min.x + minBoxSize, rootMax.x);
                else if (dragMode == DragMode.MinY) min.y = Mathf.Clamp(axis, rootMin.y, max.y - minBoxSize);
                else if (dragMode == DragMode.MaxY) max.y = Mathf.Clamp(axis, min.y + minBoxSize, rootMax.y);
                else if (dragMode == DragMode.MinZ) min.z = Mathf.Clamp(axis, rootMin.z, max.z - minBoxSize);
                else if (dragMode == DragMode.MaxZ) max.z = Mathf.Clamp(axis, min.z + minBoxSize, rootMax.z);
            }

            var b = new Bounds();
            b.SetMinMax(min, max);
            mainCtrl.SetSectionBoxBounds(b);
        }

        private Vector3 GetDominantAxis(Vector3 normal)
        {
            float ax = Mathf.Abs(normal.x);
            float ay = Mathf.Abs(normal.y);
            float az = Mathf.Abs(normal.z);
            if (ax >= ay && ax >= az) return normal.x >= 0f ? Vector3.right : Vector3.left;
            if (ay >= ax && ay >= az) return normal.y >= 0f ? Vector3.up : Vector3.down;
            return normal.z >= 0f ? Vector3.forward : Vector3.back;
        }

        private void SetupGizmoCollisionIgnores()
        {
            if (setupCollisionIgnores) return;
            if (boxTf == null) return;

            var boxCol = boxTf.GetComponent<Collider>();
            if (boxCol == null) return;

            var handleCols = new List<Collider>(8);
            var c = handleCenterTf != null ? handleCenterTf.GetComponent<Collider>() : null;
            if (c != null) handleCols.Add(c);
            c = handleMinXTf != null ? handleMinXTf.GetComponent<Collider>() : null;
            if (c != null) handleCols.Add(c);
            c = handleMaxXTf != null ? handleMaxXTf.GetComponent<Collider>() : null;
            if (c != null) handleCols.Add(c);
            c = handleMinYTf != null ? handleMinYTf.GetComponent<Collider>() : null;
            if (c != null) handleCols.Add(c);
            c = handleMaxYTf != null ? handleMaxYTf.GetComponent<Collider>() : null;
            if (c != null) handleCols.Add(c);
            c = handleMinZTf != null ? handleMinZTf.GetComponent<Collider>() : null;
            if (c != null) handleCols.Add(c);
            c = handleMaxZTf != null ? handleMaxZTf.GetComponent<Collider>() : null;
            if (c != null) handleCols.Add(c);

            for (int i = 0; i < handleCols.Count; i++)
            {
                Physics.IgnoreCollision(boxCol, handleCols[i], true);
            }
            setupCollisionIgnores = true;
        }
    }
}
