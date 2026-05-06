using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Zhouxiangyang
{
    public enum MeasurementMode
    {
        None,
        Distance,
        Angle,
        Arc
    }

    public class MeasurementTool : MonoBehaviour
    {
        public Material lineMaterial;
        public Camera mainCamera;
        public MeasurementMode mode = MeasurementMode.None;
        public Action<string> OnInfo;

        private List<Vector3> points = new List<Vector3>();
        private List<GameObject> visuals = new List<GameObject>();

        void Awake()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (lineMaterial == null)
            {
                lineMaterial = new Material(Shader.Find("Sprites/Default"));
                lineMaterial.color = Color.magenta;
            }
        }

        public void SetModeDistance() { Clear(); mode = MeasurementMode.Distance; }
        public void SetModeAngle() { Clear(); mode = MeasurementMode.Angle; }
        public void SetModeArc() { Clear(); mode = MeasurementMode.Arc; }
        public void Exit() { Clear(); mode = MeasurementMode.None; }
        public void Clear()
        {
            points.Clear();
            foreach (var v in visuals) if (v) Destroy(v);
            visuals.Clear();
        }
        public bool IsActive() { return mode != MeasurementMode.None; }

        public bool HandleMouseClick(Camera cam)
        {
            if (!IsActive()) return false;
            if (Mouse.current == null || cam == null) return false;
            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!TryPickPointOnPlane(ray, out Vector3 pickedPoint, out Vector3 pickedNormal)) return false;
            points.Add(pickedPoint);
            AddPointMarker(pickedPoint, pickedNormal);
            OnInfo?.Invoke(BuildStatusText());
            if (mode == MeasurementMode.Distance && points.Count >= 2)
            {
                Vector3 a = points[0];
                Vector3 b = points[1];
                DrawLine(a, b, Color.green);
                float sc = Mathf.Max(1f, OcxSystemManager.ModelVisualScale);
                float d = Vector3.Distance(a, b) / sc;
                float dMm = d * 1000f;
                OnInfo?.Invoke("测距结果\n--------------------------------\n距离: " + dMm.ToString("F1") + " mm");
                PlaceText((a + b) * 0.5f, dMm.ToString("F1") + " mm", Color.white);
                points.Clear();
                return true;
            }
            if (mode == MeasurementMode.Angle && points.Count >= 3)
            {
                Vector3 a = points[0];
                Vector3 b = points[1];
                Vector3 c = points[2];
                Vector3 v1 = (a - b);
                Vector3 v2 = (c - b);
                float angle = Vector3.Angle(v1, v2);
                DrawLine(b, a, Color.yellow);
                DrawLine(b, c, Color.yellow);
                DrawAngleArc(b, v1, v2, Color.yellow);
                OnInfo?.Invoke("测角结果\n--------------------------------\n角度: " + angle.ToString("F1") + "°");
                PlaceText(b, angle.ToString("F1") + "°", Color.white);
                points.Clear();
                return true;
            }
            if (mode == MeasurementMode.Arc && points.Count >= 3)
            {
                Vector3 a = points[0];
                Vector3 b = points[1];
                Vector3 c = points[2];
                if (TryCircumcenter(a, b, c, out Vector3 center))
                {
                    float sc = Mathf.Max(1f, OcxSystemManager.ModelVisualScale);
                    float rWorld = Vector3.Distance(center, a);
                    float r = rWorld / sc;
                    Vector3 planeNormal = Vector3.Cross(a - center, c - center);
                    if (planeNormal.sqrMagnitude < 1e-8f)
                    {
                        points.Clear();
                        return true;
                    }
                    planeNormal.Normalize();

                    float angle = ArcAngleThroughPoint(center, a, c, b, planeNormal);
                    DrawArc(center, rWorld, a, c, b, planeNormal, Color.cyan);
                    float length = r * angle * Mathf.Deg2Rad;
                    float lengthMm = length * 1000f;
                    float rMm = r * 1000f;
                    OnInfo?.Invoke("测圆弧结果\n--------------------------------\n圆弧角度: " + angle.ToString("F1") + "°\n圆弧长度: " + lengthMm.ToString("F1") + " mm\n半径: " + rMm.ToString("F1") + " mm");
                    PlaceText(center + (a - center).normalized * (rWorld + 0.05f), angle.ToString("F1") + "° / " + lengthMm.ToString("F1") + " mm", Color.white);
                    points.Clear();
                    return true;
                }
                points.Clear();
                return true;
            }
            return true;
        }

        private bool TryPickPointOnPlane(Ray ray, out Vector3 point, out Vector3 normal)
        {
            point = Vector3.zero;
            normal = Vector3.up;

            var hits = Physics.RaycastAll(ray, 100000f);
            if (hits == null || hits.Length == 0) return false;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.collider == null) continue;

                var pd = hit.collider.GetComponentInParent<PartData>();
                if (pd == null) continue;
                string pt = pd.PartType ?? "";
                if (!string.Equals(pt, "Plate", System.StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(pt, "Panel", System.StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(pt, "Stiffener", System.StringComparison.OrdinalIgnoreCase)) continue;

                if (string.Equals(pt, "Stiffener", System.StringComparison.OrdinalIgnoreCase))
                {
                    point = hit.point;
                    normal = hit.normal.sqrMagnitude > 1e-10f ? hit.normal.normalized : Vector3.up;
                    return true;
                }

                var boundary = pd.Boundary;
                if (boundary == null || boundary.Count < 3) continue;

                Vector3 n = pd.FaceNormal.sqrMagnitude > 1e-8f ? pd.FaceNormal.normalized : EstimateNormalWorld(boundary);
                if (n.sqrMagnitude < 1e-8f) continue;

                Vector3 p0 = boundary[0];
                Vector3 proj = hit.point - n * Vector3.Dot(hit.point - p0, n);

                if (!IsPointOnPlateSurface(pd, proj, n)) continue;

                point = proj;
                normal = n;
                return true;
            }

            return false;
        }

        private bool IsPointOnPlateSurface(PartData pd, Vector3 worldPoint, Vector3 normalWorld)
        {
            if (pd == null) return false;
            var boundary = pd.Boundary;
            if (boundary == null || boundary.Count < 3) return false;

            Vector3 n = normalWorld.sqrMagnitude > 1e-8f ? normalWorld.normalized : EstimateNormalWorld(boundary);
            if (n.sqrMagnitude < 1e-8f) return false;

            Vector3 u = Vector3.Normalize(Vector3.Cross(n, Vector3.forward));
            if (u.sqrMagnitude < 1e-8f) u = Vector3.right;
            Vector3 v = Vector3.Cross(n, u);

            var outer2 = new List<Vector2>(boundary.Count);
            for (int i = 0; i < boundary.Count; i++)
            {
                var p = boundary[i];
                outer2.Add(new Vector2(Vector3.Dot(p - boundary[0], u), Vector3.Dot(p - boundary[0], v)));
            }
            EnsureWinding2D(outer2, ccw: true);

            Vector3 baseOrigin = boundary[0];
            Vector2 p2 = new Vector2(Vector3.Dot(worldPoint - baseOrigin, u), Vector3.Dot(worldPoint - baseOrigin, v));
            if (!PointInPolygonOrOnEdge(outer2, p2)) return false;

            var holes = pd.OpeningBoundaries;
            if (holes != null && holes.Count > 0)
            {
                for (int h = 0; h < holes.Count; h++)
                {
                    var hb = holes[h];
                    if (hb == null || hb.Count < 3) continue;
                    var hole2 = new List<Vector2>(hb.Count);
                    for (int i = 0; i < hb.Count; i++)
                    {
                        var p = hb[i];
                        hole2.Add(new Vector2(Vector3.Dot(p - baseOrigin, u), Vector3.Dot(p - baseOrigin, v)));
                    }
                    EnsureWinding2D(hole2, ccw: true);
                    if (PointInPolygon(hole2, p2)) return false;
                }
            }

            return true;
        }

        private Vector3 EstimateNormalWorld(List<Vector3> boundaryWorld)
        {
            if (boundaryWorld == null || boundaryWorld.Count < 3) return Vector3.zero;
            Vector3 n = Vector3.zero;
            for (int i = 0; i < boundaryWorld.Count; i++)
            {
                Vector3 current = boundaryWorld[i];
                Vector3 next = boundaryWorld[(i + 1) % boundaryWorld.Count];
                n.x += (current.y - next.y) * (current.z + next.z);
                n.y += (current.z - next.z) * (current.x + next.x);
                n.z += (current.x - next.x) * (current.y + next.y);
            }
            if (n == Vector3.zero) return Vector3.up;
            return n.normalized;
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

        private bool PointInPolygonOrOnEdge(List<Vector2> poly, Vector2 p)
        {
            if (PointInPolygon(poly, p)) return true;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % poly.Count];
                if (PointOnSegment2D(a, b, p)) return true;
            }
            return false;
        }

        private bool PointOnSegment2D(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            Vector2 ap = p - a;
            float cross = ab.x * ap.y - ab.y * ap.x;
            if (Mathf.Abs(cross) > 1e-6f) return false;
            float dot = ap.x * ab.x + ap.y * ab.y;
            if (dot < -1e-6f) return false;
            float ab2 = ab.x * ab.x + ab.y * ab.y;
            if (dot > ab2 + 1e-6f) return false;
            return true;
        }

        private void DrawLine(Vector3 a, Vector3 b, Color color)
        {
            GameObject go = new GameObject("MeasureLine");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = lineMaterial;
            lr.startColor = color;
            lr.endColor = color;
            lr.positionCount = 2;
            lr.SetPositions(new[] { a, b });
            lr.startWidth = 0.03f;
            lr.endWidth = 0.03f;
            lr.useWorldSpace = true;
            visuals.Add(go);
        }

        private void PlaceText(Vector3 pos, string text, Color color)
        {
            GameObject go = new GameObject("MeasureText");
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.color = color;
            tm.characterSize = 0.25f;
            tm.anchor = TextAnchor.MiddleCenter;
            go.transform.position = pos;
            var bb = go.AddComponent<Billboard>();
            bb.targetCamera = mainCamera;
            visuals.Add(go);
        }

        private string BuildStatusText()
        {
            if (mode == MeasurementMode.Distance) return "测距模式\n--------------------------------\n请依次点击 2 个点\n当前已选点: " + points.Count + "/2";
            if (mode == MeasurementMode.Angle) return "测角模式\n--------------------------------\n请依次点击 3 个点(第2点为角点)\n当前已选点: " + points.Count + "/3";
            if (mode == MeasurementMode.Arc) return "测圆弧模式\n--------------------------------\n请依次点击 3 个点(起点-中间-终点)\n当前已选点: " + points.Count + "/3";
            return "";
        }

        private void AddPointMarker(Vector3 point, Vector3 normal)
        {
            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "MeasurePoint";
            dot.transform.position = point + normal.normalized * 0.002f;
            dot.transform.localScale = Vector3.one * 0.03f;
            var col = dot.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var r = dot.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = lineMaterial != null ? lineMaterial : new Material(Shader.Find("Sprites/Default"));
                r.material.color = Color.red;
            }
            visuals.Add(dot);
        }

        private void DrawAngleArc(Vector3 origin, Vector3 v1, Vector3 v2, Color color)
        {
            Vector3 n = Vector3.Cross(v1, v2);
            if (n.sqrMagnitude < 1e-6f) return;
            float r = Mathf.Min(v1.magnitude, v2.magnitude) * 0.2f;
            v1.Normalize();
            v2.Normalize();
            n.Normalize();
            float angle = Vector3.SignedAngle(v1, v2, n);
            int segments = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(angle) / 5f), 6, 72);
            List<Vector3> pts = new List<Vector3>();
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Quaternion q = Quaternion.AngleAxis(angle * t, n);
                Vector3 dir = q * v1;
                pts.Add(origin + dir * r);
            }
            GameObject go = new GameObject("AngleArc");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = lineMaterial;
            lr.startColor = color;
            lr.endColor = color;
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
            lr.startWidth = 0.03f;
            lr.endWidth = 0.03f;
            lr.useWorldSpace = true;
            visuals.Add(go);
        }

        private bool TryCircumcenter(Vector3 a, Vector3 b, Vector3 c, out Vector3 center)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 abXac = Vector3.Cross(ab, ac);
            float denom = 2f * abXac.sqrMagnitude;
            if (denom < 1e-8f) { center = Vector3.zero; return false; }
            float ab2 = ab.sqrMagnitude;
            float ac2 = ac.sqrMagnitude;
            Vector3 term1 = Vector3.Cross(abXac, ab) * ac2;
            Vector3 term2 = Vector3.Cross(ac, abXac) * ab2;
            center = a + (term1 + term2) / denom;
            return true;
        }

        private float ArcAngleThroughPoint(Vector3 center, Vector3 start, Vector3 end, Vector3 through, Vector3 planeNormal)
        {
            Vector3 va = (start - center).normalized;
            Vector3 vb = (end - center).normalized;
            Vector3 vt = (through - center).normalized;

            float signedAB = Vector3.SignedAngle(va, vb, planeNormal);
            float signedAT = Vector3.SignedAngle(va, vt, planeNormal);
            signedAB = NormalizeSignedAngle(signedAB);
            signedAT = NormalizeSignedAngle(signedAT);

            bool throughOnMinor = IsAngleBetween(0f, signedAT, signedAB);
            float minor = Mathf.Abs(signedAB);
            float major = 360f - minor;
            return throughOnMinor ? minor : major;
        }

        private void DrawArc(Vector3 center, float r, Vector3 start, Vector3 end, Vector3 through, Vector3 planeNormal, Color color)
        {
            Vector3 va = (start - center).normalized;
            Vector3 vb = (end - center).normalized;
            Vector3 vt = (through - center).normalized;

            Vector3 u = va;
            Vector3 v = Vector3.Cross(planeNormal, u).normalized;

            float signedAB = Vector3.SignedAngle(va, vb, planeNormal);
            float signedAT = Vector3.SignedAngle(va, vt, planeNormal);
            signedAB = NormalizeSignedAngle(signedAB);
            signedAT = NormalizeSignedAngle(signedAT);
            bool throughOnMinor = IsAngleBetween(0f, signedAT, signedAB);

            float minor = signedAB;
            float major = signedAB > 0f ? signedAB - 360f : signedAB + 360f;
            float signedSweep = throughOnMinor ? minor : major;

            int segments = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(signedSweep) / 5f), 12, 128);
            List<Vector3> pts = new List<Vector3>();
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float ang = signedSweep * t * Mathf.Deg2Rad;
                Vector3 dir = (u * Mathf.Cos(ang) + v * Mathf.Sin(ang)) * r;
                pts.Add(center + dir);
            }
            GameObject go = new GameObject("Arc");
            var lr = go.AddComponent<LineRenderer>();
            lr.material = lineMaterial;
            lr.startColor = color;
            lr.endColor = color;
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
            lr.startWidth = 0.03f;
            lr.endWidth = 0.03f;
            lr.useWorldSpace = true;
            visuals.Add(go);
        }

        private float NormalizeSignedAngle(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            if (a < -180f) a += 360f;
            return a;
        }

        private bool IsAngleBetween(float start, float value, float end)
        {
            float s = NormalizeSignedAngle(start);
            float v = NormalizeSignedAngle(value);
            float e = NormalizeSignedAngle(end);

            if (e >= 0f)
            {
                if (v < 0f) v += 360f;
                return v >= s && v <= e;
            }
            else
            {
                if (v > 0f) v -= 360f;
                return v <= s && v >= e;
            }
        }
    }
}
