using System.Collections.Generic;
using System.Xml.Linq;
using UnityEngine;

namespace Zhouxiangyang
{
    public partial class OcxSystemManager
    {
        private void BuildFromS1(XDocument doc, XNamespace ocx, string sourcePath, Transform fileGroup, Dictionary<string, Transform> groupCache)
        {
            BuildFromS1S2(doc, ocx, sourcePath, fileGroup, groupCache, "S1", includeDetails: false);
        }
    }
}
