using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace Zhouxiangyang
{
    public partial class OcxSystemManager
    {
        private class OpeningContour
        {
            public string Name;
            public string Type;
            public List<Vector3> Boundary;
            public Dictionary<string, string> Parameters;
        }

        private enum SectionProfileKind
        {
            Unknown,
            FlatBar,
            LBar
        }

        private class SectionProfile
        {
            public string Id;
            public string GuidRef;
            public string Name;
            public SectionProfileKind Kind;
            public float Height;
            public float Width;
            public float WebThickness;
            public float FlangeThickness;
        }

        private float ExtractDryWeightKg(XElement element, XNamespace ocx)
        {
            if (element == null) return 0f;
            var pp = element.Element(ocx + "PhysicalProperties") ?? element.Elements().FirstOrDefault(e => e.Name.LocalName == "PhysicalProperties");
            if (pp == null) return 0f;
            var dw = pp.Element(ocx + "DryWeight") ?? pp.Elements().FirstOrDefault(e => e.Name.LocalName == "DryWeight");
            if (dw == null) return 0f;
            float v = ParseNumeric(dw.Attribute("numericvalue")?.Value);
            if (v <= 0f) return 0f;
            string unit = dw.Attribute("unit")?.Value ?? "";
            if (!string.IsNullOrEmpty(unit))
            {
                string u = unit.Trim().ToLowerInvariant();
                if (u.Contains("uton") || u.Contains("tonne") || u.Contains("t"))
                {
                    if (u.Contains("kg")) return v;
                    return v * 1000f;
                }
                if (u.Contains("g") && !u.Contains("kg")) return v * 0.001f;
            }
            return v;
        }

        private void BuildFromS2(XDocument doc, XNamespace ocx, string sourcePath, Transform fileGroup, Dictionary<string, Transform> groupCache)
        {
            BuildFromS1S2(doc, ocx, sourcePath, fileGroup, groupCache, "S2", includeDetails: true);
        }

        private void BuildFromS1S2(XDocument doc, XNamespace ocx, string sourcePath, Transform fileGroup, Dictionary<string, Transform> groupCache, string schemaLevel, bool includeDetails)
        {
            var sectionProfilesById = new Dictionary<string, SectionProfile>(System.StringComparer.OrdinalIgnoreCase);
            var sectionProfilesByGuid = new Dictionary<string, SectionProfile>(System.StringComparer.OrdinalIgnoreCase);
            var materialNameById = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            var materialNameByGuid = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            if (doc != null)
            {
                BuildMaterialNameMaps(doc, ocx, materialNameById, materialNameByGuid);
                foreach (var sec in doc.Descendants().Where(e => e.Name.LocalName == "BarSection"))
                {
                    var p = ParseSectionProfile(sec, ocx);
                    if (p == null) continue;
                    if (!string.IsNullOrEmpty(p.Id)) sectionProfilesById[p.Id] = p;
                    if (!string.IsNullOrEmpty(p.GuidRef)) sectionProfilesByGuid[NormalizeGuid(p.GuidRef)] = p;
                }
            }

            var panelOpenings = new Dictionary<string, List<OpeningContour>>();
            var plateIdToPanelOpenings = new Dictionary<string, List<OpeningContour>>();
            if (schemaLevel == "S2")
            {
                foreach (var panel in doc.Descendants().Where(e => e.Name.LocalName == "Panel"))
                {
                    string pid = panel.Attribute("id")?.Value;
                    if (string.IsNullOrEmpty(pid)) continue;
                    var ops = ExtractPanelOpenings(panel, ocx);
                    if (ops == null || ops.Count == 0) continue;
                    panelOpenings[pid] = ops;

                    foreach (var plateId in ExtractPanelPlateIds(panel))
                    {
                        if (string.IsNullOrEmpty(plateId)) continue;
                        if (!plateIdToPanelOpenings.TryGetValue(plateId, out var list))
                        {
                            list = new List<OpeningContour>();
                            plateIdToPanelOpenings[plateId] = list;
                        }
                        list.AddRange(ops);
                    }
                }
            }

            foreach (var plate in doc.Descendants().Where(e => string.Equals(e.Name.LocalName, "Plate", System.StringComparison.OrdinalIgnoreCase)))
            {
                string id = plate.Attribute("id")?.Value;
                string name = plate.Attribute("name")?.Value;
                string guidRef = plate.Attribute(ocx + "GUIDRef")?.Value;
                var plateMat = plate.Element(ocx + "PlateMaterial") ?? plate.Elements().FirstOrDefault(e => e.Name.LocalName == "PlateMaterial");
                string matRef = plateMat?.Attribute("localRef")?.Value;
                string matGuid = plateMat?.Attribute(ocx + "GUIDRef")?.Value
                                 ?? plateMat?.Attributes().FirstOrDefault(a => a.Name.LocalName == "GUIDRef")?.Value;
                string matName = ResolveMaterialName(materialNameById, materialNameByGuid, matGuid, matRef);
                string thickness = ExtractThickness(plate, ocx);
                Vector3 thicknessDir = ExtractPlateThicknessDirection(plate, ocx);
                Vector3 cog = ExtractPosition(plate.Element(ocx + "PhysicalProperties")?.Element(ocx + "CenterOfGravity"), ocx);
                float dryWeightKg = ExtractDryWeightKg(plate, ocx);
                var seamPolys = ExtractPlateSeamPolylinesWorld(plate, ocx);

                var boundaryPoints = ExtractS2PlateBoundaryPoints(plate, ocx);
                if (boundaryPoints == null || boundaryPoints.Count < 3) continue;

                List<OpeningContour> openings = null;
                if (schemaLevel == "S2")
                {
                    openings = new List<OpeningContour>();
                    var plateOps = ExtractS2PlateOpenings(plate, ocx);
                    if (plateOps != null && plateOps.Count > 0) openings.AddRange(plateOps);

                    var panel = plate.Ancestors().FirstOrDefault(a => a.Name.LocalName == "Panel");
                    string panelId = panel?.Attribute("id")?.Value;
                    if (!string.IsNullOrEmpty(panelId) && panelOpenings.TryGetValue(panelId, out var pOps) && pOps != null && pOps.Count > 0)
                    {
                        openings.AddRange(pOps);
                    }

                    if (!string.IsNullOrEmpty(id) && plateIdToPanelOpenings.TryGetValue(id, out var mapped) && mapped != null && mapped.Count > 0)
                    {
                        openings.AddRange(mapped);
                    }
                    if (openings.Count > 0)
                    {
                        var unique = new List<OpeningContour>(openings.Count);
                        var seen = new HashSet<string>();
                        for (int i = 0; i < openings.Count; i++)
                        {
                            var o = openings[i];
                            string key = GetOpeningKey(o);
                            if (seen.Add(key)) unique.Add(o);
                        }
                        openings = unique;
                    }
                    if (openings == null || openings.Count == 0) openings = null;
                }
                string panelName = GetPanelName(plate, ocx);
                var platesGroup = GetOrCreateGroup(fileGroup, groupCache, panelName + "/Plates");
                BuildPrecisePlate(id, name, guidRef, matRef, thickness, thicknessDir, cog, boundaryPoints, openings, platesGroup, sourcePath, schemaLevel, dryWeightKg, matName);
                if (seamPolys != null && seamPolys.Count > 0)
                {
                    var plateTr = platesGroup.Find(id);
                    if (plateTr != null) AddPlateSeamLines(plateTr.gameObject, seamPolys);
                }
            }

            foreach (var stiffener in doc.Descendants(ocx + "Stiffener"))
            {
                string id = stiffener.Attribute("id")?.Value;
                string name = stiffener.Attribute("name")?.Value;
                string guidRef = stiffener.Attribute(ocx + "GUIDRef")?.Value;
                var matNode = stiffener.Element(ocx + "MaterialRef") ?? stiffener.Elements().FirstOrDefault(e => e.Name.LocalName == "MaterialRef");
                string matRef = matNode?.Attribute("localRef")?.Value;
                string matGuid = matNode?.Attribute(ocx + "GUIDRef")?.Value
                                 ?? matNode?.Attributes().FirstOrDefault(a => a.Name.LocalName == "GUIDRef")?.Value;
                string matName = ResolveMaterialName(materialNameById, materialNameByGuid, matGuid, matRef);
                var secNode = stiffener.Element(ocx + "SectionRef");
                string secRef = secNode?.Attribute("localRef")?.Value;
                string secGuid = secNode?.Attribute(ocx + "GUIDRef")?.Value
                                 ?? secNode?.Attributes().FirstOrDefault(a => a.Name.LocalName == "GUIDRef")?.Value;
                var secProfile = ResolveSectionProfile(sectionProfilesByGuid, sectionProfilesById, NormalizeGuid(secGuid), secRef);

                string endCutCode = null;
                var endCutParams = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                if (includeDetails)
                {
                    var endCut = stiffener.Element(ocx + "EndCutEnd1");
                    if (endCut != null)
                    {
                        endCutCode = endCut.Attribute("name")?.Value;
                        var parameters = endCut.Element(ocx + "EndcutParameters")?.Elements(ocx + "OpeningParameter");
                        if (parameters != null)
                        {
                            foreach (var param in parameters) endCutParams[param.Attribute("name")?.Value] = param.Attribute("value")?.Value;
                        }
                    }
                }

                var lineNode = stiffener.Descendants(ocx + "TraceLine").Descendants(ocx + "Line3D").FirstOrDefault();
                if (lineNode == null) continue;

                Vector3 startPt = ParsePoint(lineNode.Element(ocx + "StartPoint"), ocx);
                Vector3 endPt = ParsePoint(lineNode.Element(ocx + "EndPoint"), ocx);
                if (startPt == endPt) continue;

                Vector3 cog = Vector3.zero;
                var cogNode = stiffener.Element(ocx + "PhysicalProperties")?.Element(ocx + "CenterOfGravity");
                if (cogNode != null) cog = ParsePoint(cogNode, ocx);
                float dryWeightKg = ExtractDryWeightKg(stiffener, ocx);

                Vector3 webDir = Vector3.zero;
                Vector3 flangeDir = Vector3.zero;
                var incl = stiffener.Element(ocx + "Inclination");
                Vector3 pos = Vector3.zero;
                if (incl != null)
                {
                    webDir = ParseDirection(incl.Element(ocx + "WebDirection"));
                    flangeDir = ParseDirection(incl.Element(ocx + "FlangeDirection"));
                    var posNode = incl.Element(ocx + "Position");
                    if (posNode != null) pos = ParsePoint(posNode, ocx);
                }

                Vector3 centroid = pos != Vector3.zero ? pos : (cog != Vector3.zero ? cog : (startPt + endPt) * 0.5f);

                string panelName = GetPanelName(stiffener, ocx);
                var stiffenersGroup = GetOrCreateGroup(fileGroup, groupCache, panelName + "/Stiffeners");
                TryProjectStiffenerToPlate(fileGroup, panelName, (startPt + endPt) * 0.5f, ref startPt, ref endPt, ref centroid, ref webDir, out _);
                BuildPreciseStiffener(id, name, guidRef, matRef, secRef, endCutCode, endCutParams, startPt, endPt, centroid, webDir, flangeDir, secProfile, stiffenersGroup, sourcePath, schemaLevel, dryWeightKg, matName);
            }
        }

        private bool TryProjectStiffenerToPlate(Transform fileGroup, string panelName, Vector3 midWorld, ref Vector3 startWorld, ref Vector3 endWorld, ref Vector3 centroidWorld, ref Vector3 webDirWorld, out PartData plate)
        {
            plate = null;
            if (fileGroup == null) return false;
            if (string.IsNullOrEmpty(panelName)) return false;

            var platesGroup = fileGroup.Find(panelName + "/Plates");
            if (platesGroup == null) return false;

            var plates = platesGroup.GetComponentsInChildren<PartData>(true);
            if (plates == null || plates.Length == 0) return false;

            PartData best = null;
            float bestDist = float.PositiveInfinity;
            bool bestInside = false;

            for (int i = 0; i < plates.Length; i++)
            {
                var pd = plates[i];
                if (pd == null || !string.Equals(pd.PartType, "Plate", System.StringComparison.OrdinalIgnoreCase)) continue;
                var b = pd.Boundary;
                if (b == null || b.Count < 3) continue;
                Vector3 n = pd.FaceNormal.sqrMagnitude > 1e-8f ? pd.FaceNormal : EstimateNormalWorld(b);
                if (n.sqrMagnitude < 1e-8f) continue;
                n.Normalize();

                Vector3 refAxis = Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.95f ? Vector3.up : Vector3.right;
                Vector3 u = Vector3.Cross(refAxis, n);
                if (u.sqrMagnitude < 1e-10f) u = Vector3.Cross(Vector3.forward, n);
                if (u.sqrMagnitude < 1e-10f) u = Vector3.right;
                u.Normalize();
                Vector3 v = Vector3.Cross(n, u).normalized;
                Vector3 o = b[0];

                var poly2 = new List<Vector2>(b.Count);
                for (int k = 0; k < b.Count; k++)
                {
                    var p = b[k];
                    poly2.Add(new Vector2(Vector3.Dot(p - o, u), Vector3.Dot(p - o, v)));
                }
                EnsureWinding2D(poly2, ccw: true);

                Vector2 m2 = new Vector2(Vector3.Dot(midWorld - o, u), Vector3.Dot(midWorld - o, v));
                bool inside = PointInPolygon(poly2, m2);
                float dist = Mathf.Abs(Vector3.Dot(midWorld - o, n));

                if (inside)
                {
                    if (!bestInside || dist < bestDist)
                    {
                        best = pd;
                        bestDist = dist;
                        bestInside = true;
                    }
                }
                else if (!bestInside && dist < bestDist)
                {
                    best = pd;
                    bestDist = dist;
                }
            }

            if (best == null || best.Boundary == null || best.Boundary.Count < 3) return false;
            plate = best;

            Vector3 planeN = best.FaceNormal.sqrMagnitude > 1e-8f ? best.FaceNormal : EstimateNormalWorld(best.Boundary);
            if (planeN.sqrMagnitude < 1e-8f) return false;
            planeN.Normalize();

            if (webDirWorld.sqrMagnitude > 1e-8f)
            {
                if (Vector3.Dot(webDirWorld, planeN) < 0f) planeN = -planeN;
                webDirWorld = planeN;
            }
            else
            {
                webDirWorld = planeN;
            }

            Vector3 planeP = best.Boundary[0];
            startWorld = startWorld - planeN * Vector3.Dot(startWorld - planeP, planeN);
            endWorld = endWorld - planeN * Vector3.Dot(endWorld - planeP, planeN);

            if (centroidWorld != Vector3.zero)
            {
                centroidWorld = centroidWorld - planeN * Vector3.Dot(centroidWorld - planeP, planeN);
            }
            else
            {
                centroidWorld = (startWorld + endWorld) * 0.5f;
            }

            if (best.ThicknessValue > 0f)
            {
                float thVisual = best.ThicknessValue * OcxSystemManager.ModelVisualScale;
                Vector3 shift = planeN * (thVisual * 0.5f);
                startWorld += shift;
                endWorld += shift;
                centroidWorld += shift;
            }
            return true;
        }

        private Vector3 ExtractPlateThicknessDirection(XElement plate, XNamespace ocx)
        {
            if (plate == null) return Vector3.zero;
            var incl = plate.Element(ocx + "Inclination") ?? plate.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "Inclination", System.StringComparison.OrdinalIgnoreCase));
            if (incl == null) return Vector3.zero;
            var bd = incl.Element(ocx + "bDirection") ?? incl.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "bDirection", System.StringComparison.OrdinalIgnoreCase));
            if (bd == null) return Vector3.zero;
            string ax = bd.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "x", System.StringComparison.OrdinalIgnoreCase))?.Value;
            string ay = bd.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "y", System.StringComparison.OrdinalIgnoreCase))?.Value;
            string az = bd.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "z", System.StringComparison.OrdinalIgnoreCase))?.Value;
            float x = ParseNumeric(ax);
            float y = ParseNumeric(ay);
            float z = ParseNumeric(az);
            var v = new Vector3(x, z, y);
            if (v.sqrMagnitude < 1e-10f) return Vector3.zero;
            return v.normalized;
        }

        private List<List<Vector3>> ExtractPlateSeamPolylinesWorld(XElement plate, XNamespace ocx)
        {
            if (plate == null) return null;
            var doc = plate.Document;
            var splitBy = plate.Element(ocx + "SplitBy") ?? plate.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "SplitBy", System.StringComparison.OrdinalIgnoreCase));
            if (splitBy == null) return null;

            var seams = splitBy.Elements().Where(e => string.Equals(e.Name.LocalName, "Seam", System.StringComparison.OrdinalIgnoreCase)).ToList();
            if (seams == null || seams.Count == 0) return null;

            var list = new List<List<Vector3>>();
            for (int i = 0; i < seams.Count; i++)
            {
                var seam = seams[i];
                var trace = seam.Element(ocx + "TraceLine") ?? seam.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, "TraceLine", System.StringComparison.OrdinalIgnoreCase));
                if (trace == null) continue;

                var line3d = trace.Descendants().FirstOrDefault(e => e.Name.LocalName == "Line3D");
                if (line3d != null)
                {
                    var sp = ParsePoint(line3d.Elements().FirstOrDefault(e => e.Name.LocalName == "StartPoint"), ocx);
                    var ep = ParsePoint(line3d.Elements().FirstOrDefault(e => e.Name.LocalName == "EndPoint"), ocx);
                    if ((sp - ep).sqrMagnitude > 1e-10f) list.Add(new List<Vector3> { sp, ep });
                    continue;
                }

                var compNode = trace.Descendants().FirstOrDefault(e => e.Name.LocalName == "CompositeCurve3D");
                var comp = ResolveCompositeCurve3D(compNode, doc);
                var poly = comp != null ? ExtractCompositeCurvePolyline(comp, ocx) : null;
                if (poly != null && poly.Count >= 2) list.Add(poly);
            }

            return list.Count > 0 ? list : null;
        }

        private Vector3 ParseDirection(XElement dirNode)
        {
            if (dirNode == null) return Vector3.zero;
            string ax = dirNode.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "x", System.StringComparison.OrdinalIgnoreCase))?.Value;
            string ay = dirNode.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "y", System.StringComparison.OrdinalIgnoreCase))?.Value;
            string az = dirNode.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "z", System.StringComparison.OrdinalIgnoreCase))?.Value;
            float x = ParseNumeric(ax);
            float y = ParseNumeric(ay);
            float z = ParseNumeric(az);
            var v = new Vector3(x, z, y);
            if (v.sqrMagnitude < 1e-10f) return Vector3.zero;
            return v.normalized;
        }

        private SectionProfile ResolveSectionProfile(Dictionary<string, SectionProfile> byGuid, Dictionary<string, SectionProfile> byId, string guid, string id)
        {
            if (!string.IsNullOrEmpty(guid) && byGuid != null && byGuid.TryGetValue(NormalizeGuid(guid), out var pg) && pg != null) return pg;
            if (!string.IsNullOrEmpty(id) && byId != null && byId.TryGetValue(id, out var pi) && pi != null) return pi;
            return null;
        }

        private string NormalizeGuid(string g)
        {
            if (string.IsNullOrWhiteSpace(g)) return "";
            string s = g.Trim();
            if (s.StartsWith("{") && s.EndsWith("}") && s.Length > 2) s = s.Substring(1, s.Length - 2);
            return s.ToUpperInvariant();
        }

        private void BuildMaterialNameMaps(XDocument doc, XNamespace ocx, Dictionary<string, string> byId, Dictionary<string, string> byGuid)
        {
            if (doc == null) return;
            foreach (var m in doc.Descendants().Where(e => string.Equals(e.Name.LocalName, "Material", System.StringComparison.OrdinalIgnoreCase)))
            {
                string id = m.Attribute("id")?.Value ?? "";
                string name = m.Attribute("name")?.Value ?? "";
                string guid = m.Attribute(ocx + "GUIDRef")?.Value
                              ?? m.Attributes().FirstOrDefault(a => a.Name.LocalName == "GUIDRef")?.Value
                              ?? "";

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name) && byId != null && !byId.ContainsKey(id)) byId[id] = name;
                if (!string.IsNullOrEmpty(guid) && !string.IsNullOrEmpty(name) && byGuid != null)
                {
                    string ng = NormalizeGuid(guid);
                    if (!byGuid.ContainsKey(ng)) byGuid[ng] = name;
                }
            }
        }

        private string ResolveMaterialName(Dictionary<string, string> byId, Dictionary<string, string> byGuid, string guid, string id)
        {
            if (!string.IsNullOrEmpty(guid) && byGuid != null && byGuid.TryGetValue(NormalizeGuid(guid), out var gName) && !string.IsNullOrEmpty(gName)) return gName;
            if (!string.IsNullOrEmpty(id) && byId != null && byId.TryGetValue(id, out var iName) && !string.IsNullOrEmpty(iName)) return iName;
            return "";
        }

        private SectionProfile ParseSectionProfile(XElement barSection, XNamespace ocx)
        {
            if (barSection == null) return null;
            string id = barSection.Attribute("id")?.Value ?? "";
            string guid = barSection.Attribute(ocx + "GUIDRef")?.Value
                          ?? barSection.Attributes().FirstOrDefault(a => a.Name.LocalName == "GUIDRef")?.Value
                          ?? "";
            string name = barSection.Attribute("name")?.Value ?? "";

            var flat = barSection.Element(ocx + "FlatBar") ?? barSection.Elements().FirstOrDefault(e => e.Name.LocalName == "FlatBar");
            if (flat != null)
            {
                float h = ParseNumeric(flat.Element(ocx + "Height")?.Attribute("numericvalue")?.Value);
                float w = ParseNumeric(flat.Element(ocx + "Width")?.Attribute("numericvalue")?.Value);
                return new SectionProfile
                {
                    Id = id,
                    GuidRef = guid,
                    Name = name,
                    Kind = SectionProfileKind.FlatBar,
                    Height = h,
                    Width = w,
                    WebThickness = w,
                    FlangeThickness = 0f
                };
            }

            var lbar = barSection.Element(ocx + "LBar") ?? barSection.Elements().FirstOrDefault(e => e.Name.LocalName == "LBar");
            if (lbar != null)
            {
                float h = ParseNumeric(lbar.Element(ocx + "Height")?.Attribute("numericvalue")?.Value);
                float w = ParseNumeric(lbar.Element(ocx + "Width")?.Attribute("numericvalue")?.Value);
                float wt = ParseNumeric(lbar.Element(ocx + "WebThickness")?.Attribute("numericvalue")?.Value);
                float ft = ParseNumeric(lbar.Element(ocx + "FlangeThickness")?.Attribute("numericvalue")?.Value);
                return new SectionProfile
                {
                    Id = id,
                    GuidRef = guid,
                    Name = name,
                    Kind = SectionProfileKind.LBar,
                    Height = h,
                    Width = w,
                    WebThickness = wt,
                    FlangeThickness = ft
                };
            }

            if (!string.IsNullOrEmpty(name))
            {
                char c = char.ToUpperInvariant(name[0]);
                if (c == 'F')
                {
                    TryParseFlatBarFromName(name, out float h, out float w);
                    return new SectionProfile
                    {
                        Id = id,
                        GuidRef = guid,
                        Name = name,
                        Kind = SectionProfileKind.FlatBar,
                        Height = h,
                        Width = w,
                        WebThickness = w,
                        FlangeThickness = 0f
                    };
                }
                if (c == 'L')
                {
                    TryParseLBarFromName(name, out float h, out float w, out float wt, out float ft);
                    return new SectionProfile
                    {
                        Id = id,
                        GuidRef = guid,
                        Name = name,
                        Kind = SectionProfileKind.LBar,
                        Height = h,
                        Width = w,
                        WebThickness = wt,
                        FlangeThickness = ft
                    };
                }
            }

            return new SectionProfile { Id = id, GuidRef = guid, Name = name, Kind = SectionProfileKind.Unknown };
        }

        private bool TryParseFlatBarFromName(string name, out float heightM, out float widthM)
        {
            heightM = 0f;
            widthM = 0f;
            if (string.IsNullOrWhiteSpace(name)) return false;
            string s = name.Trim();
            if (s.Length < 3) return false;
            if (!(s.StartsWith("FL", System.StringComparison.OrdinalIgnoreCase) || s.StartsWith("F", System.StringComparison.OrdinalIgnoreCase))) return false;

            s = s.StartsWith("FL", System.StringComparison.OrdinalIgnoreCase) ? s.Substring(2) : s.Substring(1);
            var parts = s.Split(new[] { 'x', 'X' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return false;
            if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hRaw)) return false;
            if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float wRaw)) return false;

            heightM = hRaw > 10f ? hRaw / 1000f : hRaw;
            widthM = wRaw > 10f ? wRaw / 1000f : wRaw;
            return true;
        }

        private bool TryParseLBarFromName(string name, out float heightM, out float widthM, out float webThkM, out float flangeThkM)
        {
            heightM = 0f;
            widthM = 0f;
            webThkM = 0f;
            flangeThkM = 0f;
            if (string.IsNullOrWhiteSpace(name)) return false;
            string s = name.Trim();
            if (s.Length < 2) return false;
            if (!s.StartsWith("L", System.StringComparison.OrdinalIgnoreCase)) return false;

            s = s.Substring(1);
            var parts = s.Split(new[] { 'x', 'X' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) return false;
            if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hRaw)) return false;
            if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float wRaw)) return false;
            if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float wtRaw)) return false;
            if (!float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ftRaw)) return false;

            heightM = hRaw > 10f ? hRaw / 1000f : hRaw;
            widthM = wRaw > 10f ? wRaw / 1000f : wRaw;
            webThkM = wtRaw > 1f ? wtRaw / 1000f : wtRaw;
            flangeThkM = ftRaw > 1f ? ftRaw / 1000f : ftRaw;
            return true;
        }

        private List<Vector3> ExtractS2PlateBoundaryPoints(XElement plate, XNamespace ocx)
        {
            if (plate == null) return null;

            var outerContour = plate.Elements().FirstOrDefault(e => e.Name.LocalName == "OuterContour")
                            ?? plate.Descendants().FirstOrDefault(e => e.Name.LocalName == "OuterContour");
            if (outerContour == null) return null;

            var pointNodes = outerContour.Descendants().Where(e => e.Name.LocalName == "Point3D").ToList();
            if (pointNodes.Count >= 3)
            {
                var pts = new List<Vector3>(pointNodes.Count);
                foreach (var pt in pointNodes) pts.Add(ParsePoint(pt, ocx));
                return pts;
            }

            var comp = outerContour.Descendants().FirstOrDefault(e => e.Name.LocalName == "CompositeCurve3D");
            comp = ResolveCompositeCurve3D(comp, outerContour.Document ?? plate.Document);
            if (comp != null)
            {
                return ExtractCompositeCurvePolyline(comp, ocx);
            }

            return null;
        }

        private List<Vector3> ExtractCompositeCurvePolyline(XElement compositeCurve3D, XNamespace ocx)
        {
            if (compositeCurve3D == null) return null;
            var poly = new List<Vector3>();
            foreach (var seg in compositeCurve3D.Elements())
            {
                if (seg.Name.LocalName == "Line3D")
                {
                    var sp = ParsePoint(seg.Elements().FirstOrDefault(e => e.Name.LocalName == "StartPoint"), ocx);
                    var ep = ParsePoint(seg.Elements().FirstOrDefault(e => e.Name.LocalName == "EndPoint"), ocx);
                    AppendPoint(poly, sp);
                    AppendPoint(poly, ep);
                }
                else if (seg.Name.LocalName == "CircumArc3D")
                {
                    var sp = ParsePoint(seg.Elements().FirstOrDefault(e => e.Name.LocalName == "StartPoint"), ocx);
                    var mp = ParsePoint(seg.Elements().FirstOrDefault(e => e.Name.LocalName == "IntermediatePoint"), ocx);
                    var ep = ParsePoint(seg.Elements().FirstOrDefault(e => e.Name.LocalName == "EndPoint"), ocx);
                    var arc = SampleArcPoints(sp, mp, ep, 32);
                    for (int i = 0; i < arc.Count; i++) AppendPoint(poly, arc[i]);
                }
            }
            return poly.Count >= 2 ? poly : null;
        }

        private List<OpeningContour> ExtractS2PlateOpenings(XElement plate, XNamespace ocx)
        {
            if (plate == null) return null;
            var doc = plate.Document;
            var inners = plate.Descendants()
                .Where(e => e.Name.LocalName == "InnerContour"
                            && e.Ancestors().Any(a => a.Name.LocalName == "CutBy"))
                .Distinct()
                .ToList();
            if (inners.Count == 0) return null;

            var list = new List<OpeningContour>(inners.Count);
            foreach (var inner in inners)
            {
                string openingName = inner.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "openingName", System.StringComparison.OrdinalIgnoreCase))?.Value ?? "";
                string openingType = inner.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "openingType", System.StringComparison.OrdinalIgnoreCase))?.Value ?? "";
                string slotProfileName = inner.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "slotProfileName", System.StringComparison.OrdinalIgnoreCase))?.Value ?? "";
                string slotProfileSectionType = inner.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "slotProfileSectionType", System.StringComparison.OrdinalIgnoreCase))?.Value ?? "";

                string nm = !string.IsNullOrEmpty(slotProfileName) ? slotProfileName : (!string.IsNullOrEmpty(openingName) ? openingName : openingType);
                string tp = !string.IsNullOrEmpty(openingName) ? openingName : openingType;

                List<Vector3> pts = null;
                var pointNodes = inner.Descendants().Where(e => e.Name.LocalName == "Point3D").ToList();
                if (pointNodes.Count >= 3)
                {
                    pts = new List<Vector3>(pointNodes.Count);
                    for (int i = 0; i < pointNodes.Count; i++) pts.Add(ParsePoint(pointNodes[i], ocx));
                }
                if (pts == null)
                {
                    var compNode = inner.Elements().FirstOrDefault(e => e.Name.LocalName == "CompositeCurve3D")
                                ?? inner.Descendants().FirstOrDefault(e => e.Name.LocalName == "CompositeCurve3D");
                    var comp = ResolveCompositeCurve3D(compNode, doc);
                    if (comp != null) pts = ExtractCompositeCurvePolyline(comp, ocx);
                }
                if (pts == null || pts.Count < 3) continue;

                var paramDict = new Dictionary<string, string>();
                var paramNodes = inner.Descendants().Where(e => e.Name.LocalName == "OpeningParameter");
                foreach (var p in paramNodes)
                {
                    string pn = p.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "name", System.StringComparison.OrdinalIgnoreCase))?.Value;
                    string pv = p.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "value", System.StringComparison.OrdinalIgnoreCase))?.Value;
                    if (!string.IsNullOrEmpty(pn)) paramDict[pn] = pv;
                }

                if (!string.IsNullOrEmpty(openingType)) paramDict["openingType"] = openingType;
                if (!string.IsNullOrEmpty(slotProfileName)) paramDict["slotProfileName"] = slotProfileName;
                if (!string.IsNullOrEmpty(slotProfileSectionType)) paramDict["slotProfileSectionType"] = slotProfileSectionType;

                list.Add(new OpeningContour
                {
                    Name = nm,
                    Type = tp,
                    Boundary = pts,
                    Parameters = paramDict
                });
            }

            return list.Count > 0 ? list : null;
        }

        private XElement ResolveCompositeCurve3D(XElement node, XDocument doc)
        {
            if (node == null) return null;
            if (node.Name.LocalName == "CompositeCurve3D") return node;
            string refid = node.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "refid", System.StringComparison.OrdinalIgnoreCase))?.Value;
            string localRef = node.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "localRef", System.StringComparison.OrdinalIgnoreCase))?.Value;
            string key = !string.IsNullOrEmpty(refid) ? refid : localRef;
            if (!string.IsNullOrEmpty(key) && doc != null)
            {
                var target = doc.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "CompositeCurve3D"
                    && string.Equals(e.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value, key, System.StringComparison.Ordinal));
                if (target != null) return target;
            }
            if (node.Name.LocalName != "CompositeCurve3D")
            {
                var nested = node.Descendants().FirstOrDefault(e => e.Name.LocalName == "CompositeCurve3D");
                if (nested != null) return nested;
            }
            return node.Name.LocalName == "CompositeCurve3D" ? node : null;
        }

        private List<OpeningContour> ExtractPanelOpenings(XElement panel, XNamespace ocx)
        {
            if (panel == null) return null;
            var doc = panel.Document;

            var inners = panel.Descendants()
                .Where(e => e.Name.LocalName == "InnerContour"
                            && e.Ancestors().Any(a => a.Name.LocalName == "CutBy"))
                .Distinct()
                .ToList();
            if (inners.Count == 0) return null;

            var list = new List<OpeningContour>(inners.Count);
            foreach (var inner in inners)
            {
                string openingName = inner.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "openingName", System.StringComparison.OrdinalIgnoreCase))?.Value ?? "";
                string openingType = inner.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "openingType", System.StringComparison.OrdinalIgnoreCase))?.Value ?? "";
                string slotProfileName = inner.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "slotProfileName", System.StringComparison.OrdinalIgnoreCase))?.Value ?? "";
                string slotProfileSectionType = inner.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "slotProfileSectionType", System.StringComparison.OrdinalIgnoreCase))?.Value ?? "";

                string nm = !string.IsNullOrEmpty(slotProfileName) ? slotProfileName : (!string.IsNullOrEmpty(openingName) ? openingName : openingType);
                string tp = !string.IsNullOrEmpty(openingName) ? openingName : openingType;

                List<Vector3> pts = null;
                var pointNodes = inner.Descendants().Where(e => e.Name.LocalName == "Point3D").ToList();
                if (pointNodes.Count >= 3)
                {
                    pts = new List<Vector3>(pointNodes.Count);
                    for (int i = 0; i < pointNodes.Count; i++) pts.Add(ParsePoint(pointNodes[i], ocx));
                }
                if (pts == null)
                {
                    var compNode = inner.Elements().FirstOrDefault(e => e.Name.LocalName == "CompositeCurve3D")
                                ?? inner.Descendants().FirstOrDefault(e => e.Name.LocalName == "CompositeCurve3D");
                    var comp = ResolveCompositeCurve3D(compNode, doc);
                    if (comp != null) pts = ExtractCompositeCurvePolyline(comp, ocx);
                }
                if (pts == null || pts.Count < 3) continue;

                var paramDict = new Dictionary<string, string>();
                var paramNodes = inner.Descendants().Where(e => e.Name.LocalName == "OpeningParameter");
                foreach (var p in paramNodes)
                {
                    string pn = p.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "name", System.StringComparison.OrdinalIgnoreCase))?.Value;
                    string pv = p.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "value", System.StringComparison.OrdinalIgnoreCase))?.Value;
                    if (!string.IsNullOrEmpty(pn)) paramDict[pn] = pv;
                }

                if (!string.IsNullOrEmpty(openingType)) paramDict["openingType"] = openingType;
                if (!string.IsNullOrEmpty(slotProfileName)) paramDict["slotProfileName"] = slotProfileName;
                if (!string.IsNullOrEmpty(slotProfileSectionType)) paramDict["slotProfileSectionType"] = slotProfileSectionType;

                list.Add(new OpeningContour
                {
                    Name = nm,
                    Type = tp,
                    Boundary = pts,
                    Parameters = paramDict
                });
            }

            return list.Count > 0 ? list : null;
        }

        private List<OpeningContour> FilterOpeningsAffectingPlate(List<OpeningContour> openings, List<Vector3> plateBoundaryWorld)
        {
            if (openings == null || openings.Count == 0) return openings;
            if (plateBoundaryWorld == null || plateBoundaryWorld.Count < 3) return null;

            Vector3 n = EstimateNormalWorld(plateBoundaryWorld);
            if (n.sqrMagnitude < 1e-8f) n = Vector3.up;
            n.Normalize();
            Vector3 u = Vector3.Normalize(Vector3.Cross(n, Vector3.forward));
            if (u.sqrMagnitude < 1e-8f) u = Vector3.right;
            Vector3 v = Vector3.Cross(n, u);

            var outer2 = new List<Vector2>(plateBoundaryWorld.Count);
            for (int i = 0; i < plateBoundaryWorld.Count; i++)
            {
                var p = plateBoundaryWorld[i];
                outer2.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)));
            }
            EnsureWinding2D(outer2, ccw: true);

            var kept = new List<OpeningContour>();
            for (int i = 0; i < openings.Count; i++)
            {
                var o = openings[i];
                var b = o?.Boundary;
                if (b == null || b.Count < 3) continue;

                bool anyInside = false;
                for (int k = 0; k < b.Count; k += Mathf.Max(1, b.Count / 16))
                {
                    var p = b[k];
                    var p2 = new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v));
                    if (PointInPolygon(outer2, p2)) { anyInside = true; break; }
                }
                if (anyInside) { kept.Add(o); continue; }

                var hole2 = new List<Vector2>(b.Count);
                for (int k = 0; k < b.Count; k++)
                {
                    var p = b[k];
                    hole2.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)));
                }
                EnsureWinding2D(hole2, ccw: true);
                if (PolygonsIntersect(outer2, hole2)) kept.Add(o);
            }

            return kept;
        }

        private HashSet<string> ExtractPanelPlateIds(XElement panel)
        {
            var ids = new HashSet<string>();
            if (panel == null) return ids;

            foreach (var plate in panel.Descendants().Where(e => e.Name.LocalName == "Plate"))
            {
                string id = plate.Attribute("id")?.Value;
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }

            foreach (var e in panel.Descendants())
            {
                if (!e.Name.LocalName.Contains("Plate")) continue;
                string rid = e.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "refid", System.StringComparison.OrdinalIgnoreCase))?.Value;
                if (!string.IsNullOrEmpty(rid)) ids.Add(rid);
                string lid = e.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "localRef", System.StringComparison.OrdinalIgnoreCase))?.Value;
                if (!string.IsNullOrEmpty(lid)) ids.Add(lid);
            }

            foreach (var e in panel.Descendants().Where(x => x.Name.LocalName == "ComposedOf").Descendants())
            {
                if (!e.Name.LocalName.Contains("Plate")) continue;
                string rid = e.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "refid", System.StringComparison.OrdinalIgnoreCase))?.Value;
                if (!string.IsNullOrEmpty(rid)) ids.Add(rid);
            }

            return ids;
        }

        private string GetOpeningKey(OpeningContour o)
        {
            if (o == null) return "";
            var b = o.Boundary;
            if (b == null || b.Count == 0) return (o.Name ?? "") + "|" + (o.Type ?? "");
            Vector3 c = Vector3.zero;
            for (int i = 0; i < b.Count; i++) c += b[i];
            c /= b.Count;
            float rx = Mathf.Round(c.x * 1000f) / 1000f;
            float ry = Mathf.Round(c.y * 1000f) / 1000f;
            float rz = Mathf.Round(c.z * 1000f) / 1000f;
            return (o.Name ?? "") + "|" + (o.Type ?? "") + "|" + rx.ToString("F3") + "," + ry.ToString("F3") + "," + rz.ToString("F3");
        }

        private bool PolygonsIntersect(List<Vector2> a, List<Vector2> b)
        {
            if (a == null || a.Count < 3 || b == null || b.Count < 3) return false;
            for (int i = 0; i < a.Count; i++)
            {
                int i2 = (i + 1) % a.Count;
                for (int j = 0; j < b.Count; j++)
                {
                    int j2 = (j + 1) % b.Count;
                    if (SegmentsIntersect2D(a[i], a[i2], b[j], b[j2])) return true;
                }
            }
            if (PointInPolygon(a, b[0])) return true;
            if (PointInPolygon(b, a[0])) return true;
            return false;
        }

        private bool SegmentsIntersect2D(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            float o1 = (p2.x - p1.x) * (q1.y - p1.y) - (p2.y - p1.y) * (q1.x - p1.x);
            float o2 = (p2.x - p1.x) * (q2.y - p1.y) - (p2.y - p1.y) * (q2.x - p1.x);
            float o3 = (q2.x - q1.x) * (p1.y - q1.y) - (q2.y - q1.y) * (p1.x - q1.x);
            float o4 = (q2.x - q1.x) * (p2.y - q1.y) - (q2.y - q1.y) * (p2.x - q1.x);

            if (Mathf.Abs(o1) < 1e-10f && OnSegment2D(p1, p2, q1)) return true;
            if (Mathf.Abs(o2) < 1e-10f && OnSegment2D(p1, p2, q2)) return true;
            if (Mathf.Abs(o3) < 1e-10f && OnSegment2D(q1, q2, p1)) return true;
            if (Mathf.Abs(o4) < 1e-10f && OnSegment2D(q1, q2, p2)) return true;

            return (o1 > 0f) != (o2 > 0f) && (o3 > 0f) != (o4 > 0f);
        }

        private bool OnSegment2D(Vector2 a, Vector2 b, Vector2 p)
        {
            return p.x >= Mathf.Min(a.x, b.x) - 1e-10f
                && p.x <= Mathf.Max(a.x, b.x) + 1e-10f
                && p.y >= Mathf.Min(a.y, b.y) - 1e-10f
                && p.y <= Mathf.Max(a.y, b.y) + 1e-10f;
        }
    }
}
