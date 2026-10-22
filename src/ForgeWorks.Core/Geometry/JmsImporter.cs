using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace ForgeWorks.Core.Geometry;

public sealed class JmsImporter
{
    public static List<MeshObject> ImportFromFile(string filePath)
    {
        var result = new List<MeshObject>();
        if (!File.Exists(filePath)) return result;

        using var reader = new StreamReader(filePath);
        var inv = CultureInfo.InvariantCulture;

        string NextNonCommentLine()
        {
            string? l;
            while ((l = reader.ReadLine()) != null)
            {
                var trimmed = l.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith(";"))
                    return trimmed;
            }
            return string.Empty;
        }

        string versionStr = NextNonCommentLine();
        if (string.IsNullOrEmpty(versionStr)) return result;

        NextNonCommentLine(); // node checksum
        NextNonCommentLine(); // node count
        // Nodes section
        string nodeCountStr = NextNonCommentLine();
        int nodeCount = int.TryParse(nodeCountStr, out int nc) ? nc : 0;
        for (int i = 0; i < nodeCount; i++)
        {
            NextNonCommentLine(); // name
            NextNonCommentLine(); // parent
            NextNonCommentLine(); // rotation
            NextNonCommentLine(); // translation
        }

        // Materials section
        string matCountStr = NextNonCommentLine();
        int matCount = int.TryParse(matCountStr, out int mc) ? mc : 0;
        var materials = new List<string>();
        for (int i = 0; i < matCount; i++)
        {
            string mLine = NextNonCommentLine();
            string mName = mLine.Split('\t')[0].Trim();
            materials.Add(mName);
        }

        // Markers section
        string markerCountStr = NextNonCommentLine();
        int markerCount = int.TryParse(markerCountStr, out int mkc) ? mkc : 0;
        for (int i = 0; i < markerCount; i++)
        {
            NextNonCommentLine(); // marker name
            NextNonCommentLine(); // node / pos
        }

        // Regions section
        string regCountStr = NextNonCommentLine();
        int regCount = int.TryParse(regCountStr, out int rc) ? rc : 0;
        for (int i = 0; i < regCount; i++)
        {
            NextNonCommentLine();
        }

        // Vertices section
        string vertCountStr = NextNonCommentLine();
        int vertCount = int.TryParse(vertCountStr, out int vc) ? vc : 0;

        string meshName = Path.GetFileNameWithoutExtension(filePath);
        var mesh = new MeshObject(meshName);

        for (int i = 0; i < vertCount; i++)
        {
            NextNonCommentLine(); // node idx
            string posLine = NextNonCommentLine();
            string normLine = NextNonCommentLine();
            NextNonCommentLine(); // node influences
            string uvLine = NextNonCommentLine();

            var pParts = posLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var pos = Vector3.Zero;
            if (pParts.Length >= 3 &&
                float.TryParse(pParts[0], NumberStyles.Float, inv, out float x) &&
                float.TryParse(pParts[1], NumberStyles.Float, inv, out float y) &&
                float.TryParse(pParts[2], NumberStyles.Float, inv, out float z))
            {
                pos = new Vector3(x, y, z);
            }

            var nParts = normLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var norm = Vector3.UnitZ;
            if (nParts.Length >= 3 &&
                float.TryParse(nParts[0], NumberStyles.Float, inv, out float nx) &&
                float.TryParse(nParts[1], NumberStyles.Float, inv, out float ny) &&
                float.TryParse(nParts[2], NumberStyles.Float, inv, out float nz))
            {
                norm = new Vector3(nx, ny, nz);
            }

            var uvParts = uvLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var uv = Vector2.Zero;
            if (uvParts.Length >= 2 &&
                float.TryParse(uvParts[0], NumberStyles.Float, inv, out float u) &&
                float.TryParse(uvParts[1], NumberStyles.Float, inv, out float v))
            {
                uv = new Vector2(u, v);
            }

            mesh.Vertices.Add(new Vertex(pos, norm, uv));
        }

        // Triangles section
        string triCountStr = NextNonCommentLine();
        int triCount = int.TryParse(triCountStr, out int tc) ? tc : 0;
        for (int i = 0; i < triCount; i++)
        {
            string triLine = NextNonCommentLine();
            var tParts = triLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tParts.Length >= 4 &&
                int.TryParse(tParts[0], out int matIdx) &&
                int.TryParse(tParts[1], out int v0) &&
                int.TryParse(tParts[2], out int v1) &&
                int.TryParse(tParts[3], out int v2))
            {
                mesh.Triangles.Add(new Triangle(v0, v1, v2, matIdx));
                if (matIdx >= 0 && matIdx < materials.Count)
                {
                    mesh.MaterialName = materials[matIdx];
                }
            }
        }

        mesh.RecalculateNormals();
        result.Add(mesh);
        return result;
    }
}
