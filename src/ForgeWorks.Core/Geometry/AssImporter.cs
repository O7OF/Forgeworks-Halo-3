using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace ForgeWorks.Core.Geometry;

public sealed class AssImporter
{
    public static List<MeshObject> ImportFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return new List<MeshObject>();
        using var reader = new StreamReader(filePath);
        return ImportFromReader(reader);
    }

    public static List<MeshObject> ImportFromStream(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        return ImportFromReader(reader);
    }

    public static List<MeshObject> ImportFromReader(TextReader reader)
    {
        var result = new List<MeshObject>();
        var materials = new List<string>();
        var inv = CultureInfo.InvariantCulture;
        string? line;

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

        int headerVersion = 3;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(";### HEADER ###"))
            {
                string vStr = NextNonCommentLine();
                if (int.TryParse(vStr, out int v)) headerVersion = v;
            }
            else if (trimmed.StartsWith(";### MATERIALS ###"))
            {
                string countStr = NextNonCommentLine();
                if (int.TryParse(countStr, out int matCount))
                {
                    for (int i = 0; i < matCount; i++)
                    {
                        string mName = NextNonCommentLine().Trim('"');
                        NextNonCommentLine(); // variant
                        if (headerVersion >= 4)
                        {
                            string propCountStr = NextNonCommentLine(); // property count
                            if (int.TryParse(propCountStr, out int propCount))
                            {
                                for (int p = 0; p < propCount; p++)
                                {
                                    NextNonCommentLine(); // consume property
                                }
                            }
                        }
                        materials.Add(mName);
                    }
                }
            }
            else if (trimmed.StartsWith(";### OBJECTS ###"))
            {
                string countStr = NextNonCommentLine();
                if (int.TryParse(countStr, out int objCount))
                {
                    for (int o = 0; o < objCount; o++)
                    {
                        NextNonCommentLine(); // "MESH"
                        NextNonCommentLine(); // ""
                        string objName = NextNonCommentLine().Trim('"');
                        string vertCountStr = NextNonCommentLine();
                        if (!int.TryParse(vertCountStr, out int vertCount)) continue;

                        var mesh = new MeshObject(string.IsNullOrEmpty(objName) ? $"ass_mesh_{o}" : objName);
                        for (int v = 0; v < vertCount; v++)
                        {
                            // Position
                            string posLine = NextNonCommentLine();
                            var pParts = posLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            var pos = Vector3.Zero;
                            if (pParts.Length >= 3 &&
                                float.TryParse(pParts[0], NumberStyles.Float, inv, out float x) &&
                                float.TryParse(pParts[1], NumberStyles.Float, inv, out float y) &&
                                float.TryParse(pParts[2], NumberStyles.Float, inv, out float z))
                            {
                                pos = new Vector3(x, y, z);
                            }

                            // Normal
                            string normLine = NextNonCommentLine();
                            var nParts = normLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            var norm = Vector3.UnitZ;
                            if (nParts.Length >= 3 &&
                                float.TryParse(nParts[0], NumberStyles.Float, inv, out float nx) &&
                                float.TryParse(nParts[1], NumberStyles.Float, inv, out float ny) &&
                                float.TryParse(nParts[2], NumberStyles.Float, inv, out float nz))
                            {
                                norm = new Vector3(nx, ny, nz);
                            }

                            // Color (version >= 6)
                            if (headerVersion >= 6)
                            {
                                NextNonCommentLine(); // color
                            }
                            NextNonCommentLine(); // node influences count

                            // UV count & UVs
                            string uvCountStr = NextNonCommentLine();
                            var uvList = new List<Vector2>();
                            if (int.TryParse(uvCountStr, out int uvCount))
                            {
                                for (int u = 0; u < uvCount; u++)
                                {
                                    string uvLine = NextNonCommentLine();
                                    var uvParts = uvLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (uvParts.Length >= 2 &&
                                        float.TryParse(uvParts[0], NumberStyles.Float, inv, out float uVal) &&
                                        float.TryParse(uvParts[1], NumberStyles.Float, inv, out float vVal))
                                    {
                                        uvList.Add(new Vector2(uVal, 1.0f - vVal)); // Flip V back
                                    }
                                }
                            }

                            var vertex = new Vertex(pos, norm);
                            vertex.UVs.AddRange(uvList);
                            mesh.Vertices.Add(vertex);
                        }

                        // Triangles
                        string triCountStr = NextNonCommentLine();
                        if (int.TryParse(triCountStr, out int triCount))
                        {
                            for (int t = 0; t < triCount; t++)
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
                                        if (string.IsNullOrEmpty(mesh.MaterialName) || mesh.MaterialName == "default")
                                        {
                                            mesh.MaterialName = materials[matIdx];
                                        }
                                    }
                                }
                            }
                        }

                        if (materials.Count > 0)
                        {
                            foreach (var mat in materials)
                            {
                                if (!mesh.MaterialSlots.Contains(mat))
                                    mesh.MaterialSlots.Add(mat);
                            }
                        }

                        mesh.RecalculateNormals();
                        result.Add(mesh);
                    }
                }
            }
        }

        return result;
    }
}
