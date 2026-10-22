using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;

namespace ForgeWorks.Core.Diagnostics;

public sealed record WrlErrorLine(
    Vector3 PointA,
    Vector3 PointB,
    Vector3 Color
);

public sealed class WrlImporter
{
    public static List<WrlErrorLine> ParseWrlFile(string filePath)
    {
        var result = new List<WrlErrorLine>();
        if (!File.Exists(filePath))
            return result;

        string content = File.ReadAllText(filePath);
        var inv = CultureInfo.InvariantCulture;

        // Parse points: "point [ x y z, ... ]"
        var points = new List<Vector3>();
        var pointBlockMatch = Regex.Match(content, @"point\s*\[(.*?)\]", RegexOptions.Singleline);
        if (pointBlockMatch.Success)
        {
            var coords = Regex.Matches(pointBlockMatch.Groups[1].Value, @"([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)");
            for (int i = 0; i + 2 < coords.Count; i += 3)
            {
                if (float.TryParse(coords[i].Value, NumberStyles.Float, inv, out float x) &&
                    float.TryParse(coords[i + 1].Value, NumberStyles.Float, inv, out float y) &&
                    float.TryParse(coords[i + 2].Value, NumberStyles.Float, inv, out float z))
                {
                    points.Add(new Vector3(x, y, z));
                }
            }
        }

        // Parse color: "diffuseColor [ r g b, ... ]" or "diffuseColor r g b"
        Vector3 defaultColor = new(1.0f, 0.0f, 0.0f); // Default Red
        var colorMatch = Regex.Match(content, @"diffuseColor\s*\[?\s*([0-9\.]+)\s+([0-9\.]+)\s+([0-9\.]+)", RegexOptions.Singleline);
        if (colorMatch.Success)
        {
            float.TryParse(colorMatch.Groups[1].Value, NumberStyles.Float, inv, out float r);
            float.TryParse(colorMatch.Groups[2].Value, NumberStyles.Float, inv, out float g);
            float.TryParse(colorMatch.Groups[3].Value, NumberStyles.Float, inv, out float b);
            defaultColor = new Vector3(r, g, b);
        }

        // Parse coordIndex: "coordIndex [ 0, 1, -1, 2, 3, -1, ... ]"
        var indexBlockMatch = Regex.Match(content, @"coordIndex\s*\[(.*?)\]", RegexOptions.Singleline);
        if (indexBlockMatch.Success && points.Count > 0)
        {
            var indexMatches = Regex.Matches(indexBlockMatch.Groups[1].Value, @"([-0-9]+)");
            int prevIdx = -1;

            foreach (Match m in indexMatches)
            {
                if (int.TryParse(m.Value, out int idx))
                {
                    if (idx == -1)
                    {
                        prevIdx = -1;
                    }
                    else
                    {
                        if (prevIdx != -1 && prevIdx < points.Count && idx < points.Count)
                        {
                            result.Add(new WrlErrorLine(points[prevIdx], points[idx], defaultColor));
                        }
                        prevIdx = idx;
                    }
                }
            }
        }

        return result;
    }
}
