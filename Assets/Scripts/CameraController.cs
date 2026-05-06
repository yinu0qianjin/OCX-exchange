using UnityEngine;
using UnityEngine.InputSystem;

namespace Zhouxiangyang
{
    public class CameraController : MonoBehaviour
    {
        [Header("相机移动设置 (自由)")]
        [Tooltip("相机移动速度")]
        public float moveSpeed = 5f;
        [Tooltip("按住 Shift 时的加速倍数")]
        public float fastMoveMultiplier = 3f;

        [Header("视角控制设置 (鼠标)")]
        [Tooltip("按住右键旋转视角的灵敏度")]
        public float lookSpeed = 0.15f;
        [Tooltip("按住中键平移的灵敏度")]
        public float panSpeed = 12f;
        [Tooltip("滚轮缩放/前进后退速度")]
        public float zoomSpeed = 51f;

        // 自动对焦使用
        public Vector3 focusPoint = Vector3.zero;

        // 视角记录
        private float pitch = 0f;
        private float yaw = 0f;

        [Header("环绕整体查看模式")]
        public bool isOrbitMode = false;
        private Vector3 orbitCenter = Vector3.zero;
        private float orbitDistance = 10f;

        // 组件引用
        private Camera cam;
        private float baseFov;

        private enum SectionAxis
        {
            None,
            X,
            Y,
            Z
        }

        [Header("切面浏览 (正交+斜截面)")]
        public float sectionThickness = 0.6f;
        public float sectionScrollSpeed = 16.0f;
        public float sectionViewPadding = 1.18f;
        public float sectionViewPaddingZ = 1.40f;
        public float sectionMinThicknessRatio = 0.02f;
        public int sectionModelLayer = 30;
        public int sectionGizmoLayer = 31;
        public float sectionBoxMinWorldSize = 0.05f;
        public float sectionBoxPaddingRatio = 0.06f;
        public float sectionBoxPaddingWorld = 0.2f;

        private bool sectionBoxActive;
        private Bounds sectionRootBounds;
        private Bounds sectionBoxBounds;
        private bool sectionGizmoVisible = true;

        private bool savedCameraState;
        private bool prevOrthographic;
        private float prevFov;
        private float prevOrthoSize;
        private float prevNear;
        private float prevFar;
        private Matrix4x4 prevProj;
        private int prevCullingMask;
        private CameraClearFlags prevClearFlags;
        private Color prevBackgroundColor;
        private Vector3 prevPos;
        private Quaternion prevRot;
        private bool prevIsOrbitMode;
        private Vector3 prevOrbitCenter;
        private float prevOrbitDistance;

        void Start()
        {
            cam = GetComponent<Camera>();
            if (cam != null) baseFov = cam.fieldOfView;

            // 初始化时记录当前视角
            Vector3 angles = transform.eulerAngles;
            pitch = angles.x;
            yaw = angles.y;

            // 移除可能存在的碰撞体，让相机可以自由穿梭查看
            var cc = GetComponent<CharacterController>();
            if (cc != null) Destroy(cc);
            var col = GetComponent<Collider>();
            if (col != null) Destroy(col);
            var rb = GetComponent<Rigidbody>();
            if (rb != null) Destroy(rb);
        }

        void Update()
        {
            if (Mouse.current == null || Keyboard.current == null) return;

            // 当前帧累计位移，最后统一执行移动
            Vector3 frameMovement = Vector3.zero;
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            float wheelZoom = Mouse.current.scroll.ReadValue().y;

            if (isOrbitMode || sectionBoxActive)
            {
                // 环绕模式
                if (Mouse.current.rightButton.isPressed)
                {
                    yaw += mouseDelta.x * lookSpeed;
                    pitch -= mouseDelta.y * lookSpeed;
                    pitch = Mathf.Clamp(pitch, -89f, 89f);
                }

                if (Mouse.current.middleButton.isPressed)
                {
                    Vector3 pan = (-transform.right * mouseDelta.x - transform.up * mouseDelta.y) * 0.0025f * Mathf.Max(0.5f, orbitDistance);
                    orbitCenter += pan;
                    if (sectionBoxActive) focusPoint = orbitCenter;
                }

                if (Mathf.Abs(wheelZoom) > 0.01f)
                {
                    float notches = wheelZoom / 120f;
                    float step = notches * zoomSpeed * Mathf.Max(0.08f, orbitDistance * 0.06f);
                    orbitDistance = Mathf.Clamp(orbitDistance - step, 0.5f, 500000f);
                }

                Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
                transform.position = orbitCenter - rot * Vector3.forward * orbitDistance;
                transform.rotation = rot;
            }
            else
            {
                // 自由模式
                // ================= 1. WASD 键盘移动 =================
                Vector3 keyboardDir = Vector3.zero;
                if (Keyboard.current.wKey.isPressed) keyboardDir += transform.forward;
                if (Keyboard.current.sKey.isPressed) keyboardDir -= transform.forward;
                if (Keyboard.current.aKey.isPressed) keyboardDir -= transform.right;
                if (Keyboard.current.dKey.isPressed) keyboardDir += transform.right;

                if (Keyboard.current.eKey.isPressed) keyboardDir += Vector3.up;
                if (Keyboard.current.qKey.isPressed) keyboardDir -= Vector3.up;

                if (keyboardDir.magnitude > 1f) keyboardDir.Normalize();

                float currentSpeed = moveSpeed;
                if (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed)
                {
                    currentSpeed *= fastMoveMultiplier;
                }

                frameMovement += keyboardDir * currentSpeed * Time.deltaTime;


                // ================= 2. 鼠标视角控制 =================
                // 按住右键：旋转视角
                if (Mouse.current.rightButton.isPressed)
                {
                    yaw += mouseDelta.x * lookSpeed;
                    pitch -= mouseDelta.y * lookSpeed;
                    pitch = Mathf.Clamp(pitch, -89f, 89f);
                    transform.eulerAngles = new Vector3(pitch, yaw, 0f);
                }

                // 按住中键：平移
                if (Mouse.current.middleButton.isPressed)
                {
                    frameMovement += -transform.right * mouseDelta.x * panSpeed * Time.deltaTime
                                     - transform.up * mouseDelta.y * panSpeed * Time.deltaTime;
                }

                // 滚轮：前进/后退
                if (Mathf.Abs(wheelZoom) > 0.01f)
                {
                    float notches = wheelZoom / 120f;
                    Vector3 pivot = focusPoint;
                    float dist = Vector3.Distance(transform.position, pivot);
                    if (dist < 0.5f) dist = 0.5f;
                    float step = notches * zoomSpeed * Mathf.Max(0.08f, dist * 0.06f);
                    frameMovement += transform.forward * step;
                }

                // ================= 3. 执行相机移动 =================
                if (frameMovement != Vector3.zero)
                {
                    transform.position += frameMovement;
                }
            }

            UpdateCameraViewQuality();
        }

        private void UpdateCameraViewQuality()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null) return;
            if (cam.orthographic) return;

            Vector3 pivot = focusPoint;
            float dist = Vector3.Distance(transform.position, pivot);
            if (dist < 0.5f) dist = 0.5f;

            float targetNear = Mathf.Clamp(dist * 0.01f, 0.05f, 8.0f);
            float targetFar = Mathf.Clamp(dist * 6f + 60f, 200f, 250000f);
            if (targetFar < targetNear + 0.1f) targetFar = targetNear + 0.1f;

            cam.nearClipPlane = Mathf.Lerp(cam.nearClipPlane, targetNear, 0.2f);
            cam.farClipPlane = Mathf.Lerp(cam.farClipPlane, targetFar, 0.2f);

            if (baseFov <= 1f) baseFov = cam.fieldOfView;
            float t = Mathf.InverseLerp(0.8f, 60f, dist);
            float targetFov = Mathf.Lerp(28f, baseFov, t);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 0.15f);
        }

        // 外部强制传送（例如自动对焦）
        public void TeleportTo(Vector3 position, Vector3 lookAtPoint)
        {
            transform.position = position;
            transform.LookAt(lookAtPoint);

            Vector3 angles = transform.eulerAngles;
            pitch = angles.x;
            yaw = angles.y;
        }

        public void UpdateOrbitFromFocus(Vector3 center, Vector3 cameraPos)
        {
            orbitCenter = center;
            orbitDistance = Mathf.Max(0.5f, Vector3.Distance(cameraPos, center));
            Vector3 dir = center - cameraPos;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            Vector3 angles = rot.eulerAngles;
            pitch = angles.x;
            yaw = angles.y;
            transform.position = cameraPos;
            transform.rotation = rot;
        }

        public void ToggleOrbitMode(bool enable, Vector3 center, float size)
        {
            isOrbitMode = enable;
            if (enable)
            {
                orbitCenter = center;
                orbitDistance = Mathf.Max(size * 1.2f, Vector3.Distance(transform.position, center));
                
                // 自动看向中心点
                transform.LookAt(center);
                Vector3 angles = transform.eulerAngles;
                pitch = angles.x;
                yaw = angles.y;
            }
        }

        public void EnterSectionView(char axis, Bounds bounds)
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null) return;

            if (!savedCameraState)
            {
                prevOrthographic = cam.orthographic;
                prevFov = cam.fieldOfView;
                prevOrthoSize = cam.orthographicSize;
                prevNear = cam.nearClipPlane;
                prevFar = cam.farClipPlane;
                prevProj = cam.projectionMatrix;
                prevCullingMask = cam.cullingMask;
                prevClearFlags = cam.clearFlags;
                prevBackgroundColor = cam.backgroundColor;
                prevPos = transform.position;
                prevRot = transform.rotation;
                prevIsOrbitMode = isOrbitMode;
                prevOrbitCenter = orbitCenter;
                prevOrbitDistance = orbitDistance;
                savedCameraState = true;
            }

            sectionRootBounds = bounds;
            sectionGizmoVisible = true;
            sectionRootBounds = ExpandSectionBounds(bounds);
            sectionBoxBounds = sectionRootBounds;
            sectionBoxActive = true;

            cam.orthographic = false;
            cam.ResetProjectionMatrix();
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            UpdateSectionCullingMask();
            cam.clearFlags = prevClearFlags;
            cam.backgroundColor = prevBackgroundColor;

            focusPoint = sectionBoxBounds.center;
            ToggleOrbitMode(true, sectionBoxBounds.center, sectionBoxBounds.size.magnitude);
        }

        public void ExitSectionView()
        {
            if (cam == null) cam = GetComponent<Camera>();
            sectionBoxActive = false;
            if (cam == null) return;

            if (savedCameraState)
            {
                cam.orthographic = prevOrthographic;
                cam.fieldOfView = prevFov;
                cam.orthographicSize = prevOrthoSize;
                cam.nearClipPlane = prevNear;
                cam.farClipPlane = prevFar;
                cam.projectionMatrix = prevProj;
                cam.cullingMask = prevCullingMask;
                cam.clearFlags = prevClearFlags;
                cam.backgroundColor = prevBackgroundColor;
                transform.position = prevPos;
                transform.rotation = prevRot;
                Vector3 angles = transform.eulerAngles;
                pitch = angles.x;
                yaw = angles.y;
                isOrbitMode = prevIsOrbitMode;
                orbitCenter = prevOrbitCenter;
                orbitDistance = prevOrbitDistance;
                savedCameraState = false;
            }
        }

        public bool IsInSectionView()
        {
            return sectionBoxActive;
        }

        public bool IsSectionGizmoVisible()
        {
            return sectionGizmoVisible;
        }

        public void SetSectionGizmoVisible(bool visible)
        {
            sectionGizmoVisible = visible;
            if (sectionBoxActive) UpdateSectionCullingMask();
        }

        public char GetSectionAxis()
        {
            return '\0';
        }

        public Bounds GetSectionBounds()
        {
            return sectionBoxBounds;
        }

        public Vector3 GetSectionForward()
        {
            return Vector3.forward;
        }

        public float GetSectionCenter()
        {
            return sectionBoxBounds.center.z;
        }

        public float GetSectionBoxSize()
        {
            return sectionBoxBounds.size.z;
        }

        public Bounds GetSectionRootBounds()
        {
            return sectionRootBounds;
        }

        public Bounds GetSectionBoxBounds()
        {
            return sectionBoxBounds;
        }

        public void SetSectionBoxBounds(Bounds box)
        {
            if (!sectionBoxActive) return;

            Vector3 rootMin = sectionRootBounds.min;
            Vector3 rootMax = sectionRootBounds.max;

            Vector3 size = box.size;
            size.x = Mathf.Max(sectionBoxMinWorldSize, size.x);
            size.y = Mathf.Max(sectionBoxMinWorldSize, size.y);
            size.z = Mathf.Max(sectionBoxMinWorldSize, size.z);

            size.x = Mathf.Min(size.x, Mathf.Max(sectionBoxMinWorldSize, sectionRootBounds.size.x));
            size.y = Mathf.Min(size.y, Mathf.Max(sectionBoxMinWorldSize, sectionRootBounds.size.y));
            size.z = Mathf.Min(size.z, Mathf.Max(sectionBoxMinWorldSize, sectionRootBounds.size.z));

            Vector3 half = size * 0.5f;
            Vector3 center = box.center;
            center.x = Mathf.Clamp(center.x, rootMin.x + half.x, rootMax.x - half.x);
            center.y = Mathf.Clamp(center.y, rootMin.y + half.y, rootMax.y - half.y);
            center.z = Mathf.Clamp(center.z, rootMin.z + half.z, rootMax.z - half.z);

            sectionBoxBounds = new Bounds(center, size);
            focusPoint = sectionBoxBounds.center;
        }

        private Bounds ExpandSectionBounds(Bounds b)
        {
            float ratio = Mathf.Max(0f, sectionBoxPaddingRatio);
            float world = Mathf.Max(0f, sectionBoxPaddingWorld);
            Vector3 pad = b.extents * ratio + Vector3.one * world;
            return new Bounds(b.center, b.size + pad * 2f);
        }

        private void UpdateSectionCullingMask()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam == null) return;
            int mask = 1 << sectionModelLayer;
            if (sectionGizmoVisible) mask |= 1 << sectionGizmoLayer;
            cam.cullingMask = mask;
        }
    }
}
