using System.Collections.Generic;
using UnityEngine;

namespace Zhouxiangyang
{
    public class PartData : MonoBehaviour
    {
        // ================= S0 几何骨架参数 =================
        public string GeometryName;
        public string LineFaceDirection = "默认方向";
        public string Constraints = "无额外约束";
        public List<Vector3> Boundary = new List<Vector3>();
        public Vector3 FaceNormal = Vector3.up;
        public string SchemaLevel = "S2";
        public string GuidRef;

        // ================= S1/S2 核心与制造参数 =================
        public string PartId;
        public string PartType;
        public string MaterialRef;
        public string MaterialName;
        public string Thickness;
        public string SectionRef;
        public float ThicknessValue; // 米
        public float SectionHeight; // 米
        public float SectionWidth; // 米
        public float SectionWebThickness; // 米
        public float SectionFlangeThickness; // 米

        // 用于 S1 统计功能的重量
        public float Weight;

        public string EndCutCode;
        public Dictionary<string, string> EndCutParams = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> OpeningParams = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        public List<string> OpeningNames = new List<string>();
        public List<string> OpeningTypes = new List<string>();
        public List<List<Vector3>> OpeningBoundaries = new List<List<Vector3>>();
        public string SourceFilePath;
        public string SourceElementType;

        private static bool EqualsCI(string a, string b)
        {
            return string.Equals(a ?? "", b ?? "", System.StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatMm(float meters, int decimals = 1)
        {
            float mm = meters * 1000f;
            if (decimals <= 0) return mm.ToString("F0");
            if (decimals == 1) return mm.ToString("F1");
            if (decimals == 2) return mm.ToString("F2");
            return mm.ToString("F3");
        }

        // 格式化输出 UI 文本
        public string GetFormattedData()
        {
            string uiText = $"<b><size=120%>构件识别号: {PartId}</size></b>\n";
            uiText += "--------------------------------\n";

            var r = GetComponent<Renderer>();
            if (r != null)
            {
                var s = r.bounds.size;
                float sc = Mathf.Max(1f, OcxSystemManager.ModelVisualScale);
                uiText += $"尺寸(mm): 长{FormatMm(s.x / sc)}  宽{FormatMm(s.z / sc)}  高{FormatMm(s.y / sc)}\n";
            }

            if (EqualsCI(SchemaLevel, "S0"))
            {
                uiText += $"<color=#88FF88><b>[S0 数据]</b></color>\n";
                if (!string.IsNullOrEmpty(GeometryName)) uiText += $"名称: {GeometryName}\n";
                if (!string.IsNullOrEmpty(GuidRef)) uiText += $"GUIDRef: {GuidRef}\n";
                uiText += $"类型: {PartType}\n";
                uiText += $"约束/参数: {Constraints}\n";
                return uiText;
            }

            uiText += $"<color=#FFAA55><b>[S2 数据]</b></color>\n";
            if (!string.IsNullOrEmpty(GeometryName)) uiText += $"名称: {GeometryName}\n";
            if (!string.IsNullOrEmpty(GuidRef)) uiText += $"GUIDRef: {GuidRef}\n";
            uiText += $"结构种类: {PartType}\n";
            uiText += $"材质: {(!string.IsNullOrEmpty(MaterialName) ? MaterialName : (MaterialRef ?? "未记录"))}\n";
            if (Weight > 0f) uiText += $"干重(DryWeight): {Weight:F2} kg\n";
            else uiText += $"干重(DryWeight): 未记录\n";
            if (EqualsCI(PartType, "Plate"))
            {
                if (ThicknessValue > 0f) uiText += $"板材厚度: {FormatMm(ThicknessValue, 2)} mm\n";
                else uiText += $"板材厚度: {(Thickness ?? "未记录")}\n";
                if (OpeningNames != null && OpeningNames.Count > 0)
                {
                    uiText += $"开孔数量: {OpeningNames.Count}\n";
                    for (int i = 0; i < OpeningNames.Count; i++)
                    {
                        string nm = OpeningNames[i];
                        string tp = (OpeningTypes != null && i < OpeningTypes.Count) ? OpeningTypes[i] : "";
                        uiText += string.IsNullOrEmpty(tp) ? $"  • {nm}\n" : $"  • {nm} ({tp})\n";
                    }
                }
            }
            else if (EqualsCI(PartType, "Stiffener"))
            {
                uiText += $"型材规格: {SectionRef ?? "未记录"}\n";
                if (SectionHeight > 0f || SectionWidth > 0f)
                {
                    uiText += $"型材尺寸(mm): H{FormatMm(SectionHeight, 1)}  W{FormatMm(SectionWidth, 1)}";
                    if (SectionWebThickness > 0f) uiText += $"  Tw{FormatMm(SectionWebThickness, 2)}";
                    if (SectionFlangeThickness > 0f) uiText += $"  Tf{FormatMm(SectionFlangeThickness, 2)}";
                    uiText += "\n";
                }
                uiText += $"端切代码: {EndCutCode ?? "无"}\n";

                if (EndCutParams.Count > 0)
                {
                    uiText += "角隅参数细节:\n";
                    foreach (var kvp in EndCutParams)
                        uiText += $"  • {kvp.Key} = {kvp.Value}\n";
                }
            }

            if (OpeningParams.Count > 0)
            {
                uiText += $"\n<color=#FF5555><b>[开孔参数]</b></color>\n";
                foreach (var kvp in OpeningParams)
                    uiText += $"  • {kvp.Key} = {kvp.Value}\n";
            }

            return uiText;
        }
    }
}
