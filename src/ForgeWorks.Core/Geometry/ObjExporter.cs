using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace ForgeWorks.Core.Geometry;

public sealed class ObjExporter
{
    public static void ExportToFile(string filePath, IEnumerable<MeshObject> meshes)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var writer = new StreamWriter(filePath);
        var inv = CultureInfo.InvariantCulture;

        writer.WriteLine("# ForgeWorks Studio Wavefront OBJ Export");
        writer.WriteLine($"# Generated: {DateTime.UtcNow:O}");

        int vertOffset = 1;

        foreach (var mesh in meshes)
        {
            writer.WriteLine($"o {mesh.Name}");
            writer.WriteLine($"usemtl {mesh.MaterialName}");

            foreach (var v in mesh.Vertices)
            {
                writer.WriteLine(string.Format(inv, "v {0:F4} {1:F4} {2:F4}", v.Position.X, v.Position.Y, v.Position.Z));
            }

            foreach (var v in mesh.Vertices)
            {
                var uv = v.UVs.Count > 0 ? v.UVs[0] : Vector2.Zero;
                writer.WriteLine(string.Format(inv, "vt {0:F4} {1:F4}", uv.X, uv.Y));
            }

            foreach (var v in mesh.Vertices)
            {
                writer.WriteLine(string.Format(inv, "vn {0:F4} {1:F4} {2:F4}", v.Normal.X, v.Normal.Y, v.Normal.Z));
            }

            foreach (var tri in mesh.Triangles)
            {
                int v0 = vertOffset + tri.V0;
                int v1 = vertOffset + tri.V1;
                int v2 = vertOffset + tri.V2;
                writer.WriteLine($"f {v0}/{v0}/{v0} {v1}/{v1}/{v1} {v2}/{v2}/{v2}");
            }

            vertOffset += mesh.Vertices.Count;
        }
    }
}

public sealed class ObjImporter
{
    public static List<MeshObject> ImportFromFile(string filePath)
    {
        var result = new List<MeshObject>();
        if (!File.Exists(filePath)) return result;

        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();

        MeshObject? currentMesh = null;
        var vertCache = new Dictionary<(int, int, int), int>();
        var inv = CultureInfo.InvariantCulture;

        using var reader = new StreamReader(filePath);
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                case "o" or "g":
                    currentMesh = new MeshObject(parts.Length > 1 ? parts[1] : "imported_mesh");
                    result.Add(currentMesh);
                    vertCache.Clear();
                    break;
                case "usemtl":
                    if (currentMesh != null && parts.Length > 1)
                        currentMesh.MaterialName = parts[1];
                    break;
                case "v":
                    if (parts.Length >= 4 &&
                        float.TryParse(parts[1], NumberStyles.Float, inv, out float vx) &&
                        float.TryParse(parts[2], NumberStyles.Float, inv, out float vy) &&
                        float.TryParse(parts[3], NumberStyles.Float, inv, out float vz))
                    {
                        positions.Add(new Vector3(vx, vy, vz));
                    }
                    break;
                case "vt":
                    if (parts.Length >= 3 &&
                        float.TryParse(parts[1], NumberStyles.Float, inv, out float u) &&
                        float.TryParse(parts[2], NumberStyles.Float, inv, out float v))
                    {
                        uvs.Add(new Vector2(u, v));
                    }
                    break;
                case "vn":
                    if (parts.Length >= 4 &&
                        float.TryParse(parts[1], NumberStyles.Float, inv, out float nx) &&
                        float.TryParse(parts[2], NumberStyles.Float, inv, out float ny) &&
                        float.TryParse(parts[3], NumberStyles.Float, inv, out float nz))
                    {
                        normals.Add(new Vector3(nx, ny, nz));
                    }
                    break;
                case "f":
                    if (currentMesh == null)
                    {
                        currentMesh = new MeshObject("imported_mesh");
                        result.Add(currentMesh);
                        vertCache.Clear();
                    }

                    var faceIndices = new List<int>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var facePart = parts[i].Split('/');
                        if (int.TryParse(facePart[0], out int pIdx))
                        {
                            int actualIdx = pIdx > 0 ? pIdx - 1 : positions.Count + pIdx;
                            if (actualIdx >= 0 && actualIdx < positions.Count)
                            {
                                int aUv = -1;
                                int aN = -1;
                                var norm = Vector3.UnitZ;
                                Vector2? uvCoord = null;

                                if (facePart.Length > 1 && int.TryParse(facePart[1], out int uvIdx))
                                {
                                    aUv = uvIdx > 0 ? uvIdx - 1 : uvs.Count + uvIdx;
                                    if (aUv >= 0 && aUv < uvs.Count) uvCoord = uvs[aUv];
                                }
                                if (facePart.Length > 2 && int.TryParse(facePart[2], out int nIdx))
                                {
                                    aN = nIdx > 0 ? nIdx - 1 : normals.Count + nIdx;
                                    if (aN >= 0 && aN < normals.Count) norm = normals[aN];
                                }

                                var key = (actualIdx, aUv, aN);
                                if (!vertCache.TryGetValue(key, out int meshVertIdx))
                                {
                                    meshVertIdx = currentMesh.Vertices.Count;
                                    currentMesh.Vertices.Add(new Vertex(positions[actualIdx], norm, uvCoord));
                                    vertCache[key] = meshVertIdx;
                                }

                                faceIndices.Add(meshVertIdx);
                            }
                        }
                    }

                    // Fan triangulate polygon
                    for (int i = 1; i < faceIndices.Count - 1; i++)
                    {
                        currentMesh.Triangles.Add(new Triangle(faceIndices[0], faceIndices[i], faceIndices[i + 1]));
                    }
                    break;
            }
        }

        foreach (var m in result)
        {
            m.RecalculateNormals();
        }

        return result;
    }
}
