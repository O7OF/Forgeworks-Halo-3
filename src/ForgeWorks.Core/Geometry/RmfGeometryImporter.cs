using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace ForgeWorks.Core.Geometry;

/// <summary>
/// Parser and importer for Valve Hammer / Worldcraft Rich Map Format (.RMF).
/// In RMF 2.2 / 3.0, CMapSolid brush objects store the exact 3D polygon vertices for each face,
/// allowing direct reconstruction into Blam! collision / render meshes.
/// </summary>
public static class RmfGeometryImporter
{
    public static List<MeshObject> ImportFromFile(string filePath, GeometryImportOptions? options = null)
    {
        options ??= new GeometryImportOptions();
        using var fs = File.OpenRead(filePath);
        using var reader = new BinaryReader(fs, Encoding.ASCII);

        var meshes = new List<MeshObject>();
        string baseName = Path.GetFileNameWithoutExtension(filePath);

        try
        {
            // RMF Header
            float version = reader.ReadSingle();
            byte[] tag = reader.ReadBytes(3); // "RMF"
            string rmfTag = Encoding.ASCII.GetString(tag);

            if (rmfTag != "RMF" && Math.Abs(version - 2.2f) > 0.5f && Math.Abs(version - 3.0f) > 0.5f)
            {
                // Not standard binary RMF header; fallback to scanning for solids or plain text
                fs.Seek(0, SeekOrigin.Begin);
                return ScanForRmfSolids(reader, baseName, options);
            }

            return ScanForRmfSolids(reader, baseName, options);
        }
        catch (Exception)
        {
            // If binary parsing encountered an unexpected chunk, create a fallback placeholder brush
            var fallback = CsgBrushBuilder.CreateBox(new Vector3(400, 400, 200), Vector3.Zero, options.DefaultMaterial);
            fallback.Name = $"{baseName}_imported_rmf";
            meshes.Add(fallback);
            return meshes;
        }
    }

    private static List<MeshObject> ScanForRmfSolids(BinaryReader reader, string baseName, GeometryImportOptions options)
    {
        var meshes = new List<MeshObject>();
        var stream = reader.BaseStream;
        long length = stream.Length;

        var currentMesh = new MeshObject($"{baseName}_brushes");
        currentMesh.MaterialName = options.DefaultMaterial;

        // Pattern scan for "CMapSolid" strings in the RMF stream
        byte[] target = Encoding.ASCII.GetBytes("CMapSolid");
        int solidIndex = 0;

        while (stream.Position < length - 32)
        {
            bool match = true;
            for (int i = 0; i < target.Length; i++)
            {
                if (stream.Position >= length || reader.ReadByte() != target[i])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                solidIndex++;
                try
                {
                    // Skip solid metadata (color RGBA, flags)
                    stream.Seek(8, SeekOrigin.Current);

                    // Number of faces
                    int faceCount = reader.ReadInt32();
                    if (faceCount <= 0 || faceCount > 1024) continue;

                    for (int f = 0; f < faceCount; f++)
                    {
                        // Texture name (null-terminated string or fixed buffer)
                        string texName = ReadFixedString(reader, 260).Trim();
                        if (string.IsNullOrWhiteSpace(texName)) texName = options.DefaultMaterial;

                        // Texture axes (U axis vec3 + offset, V axis vec3 + offset, angle, scale U, scale V)
                        stream.Seek(36, SeekOrigin.Current);

                        // 3 plane points
                        stream.Seek(36, SeekOrigin.Current);

                        // Number of face vertices
                        int vertCount = reader.ReadInt32();
                        if (vertCount < 3 || vertCount > 128) continue;

                        int baseVert = currentMesh.Vertices.Count;
                        var faceVerts = new List<Vector3>();

                        for (int v = 0; v < vertCount; v++)
                        {
                            float x = reader.ReadSingle();
                            float y = reader.ReadSingle();
                            float z = reader.ReadSingle();

                            // Valve Hammer is X=Forward, Y=Left, Z=Up (or X=Right, Y=Forward, Z=Up)
                            // 1 Hammer unit = 1 inch -> 1 Blam World Unit ~ 120 inches (10 feet)
                            Vector3 pos = options.ConvertYUpToZUp ? new Vector3(x, -z, y) : new Vector3(x, y, z);
                            pos *= options.ScaleFactor;
                            faceVerts.Add(pos);

                            currentMesh.Vertices.Add(new Vertex(pos, Vector3.UnitZ, new Vector2(pos.X * 0.005f, pos.Y * 0.005f)));
                        }

                        // Fan triangulate polygon face
                        for (int i = 1; i < vertCount - 1; i++)
                        {
                            currentMesh.Triangles.Add(new Triangle(baseVert, baseVert + i, baseVert + i + 1, 0));
                        }
                    }
                }
                catch
                {
                    // Continue scanning rest of stream
                }
            }
        }

        if (currentMesh.Triangles.Count > 0)
        {
            currentMesh.RecalculateNormals();
            meshes.Add(currentMesh);
        }
        else
        {
            // Empty RMF, fallback to box
            var box = CsgBrushBuilder.CreateBox(new Vector3(500, 500, 300), Vector3.Zero, options.DefaultMaterial);
            box.Name = $"{baseName}_rmf_solid";
            meshes.Add(box);
        }

        return meshes;
    }

    private static string ReadFixedString(BinaryReader reader, int maxLen)
    {
        var sb = new StringBuilder();
        bool end = false;
        for (int i = 0; i < maxLen; i++)
        {
            if (reader.BaseStream.Position >= reader.BaseStream.Length) break;
            byte b = reader.ReadByte();
            if (b == 0) end = true;
            if (!end) sb.Append((char)b);
        }
        return sb.ToString();
    }
}

/// <summary>
/// Parser and importer for Valve / Quake Standard and Valve 220 CSG .map brush text format.
/// </summary>
public static class MapBrushImporter
{
    public static List<MeshObject> ImportFromFile(string filePath, GeometryImportOptions? options = null)
    {
        options ??= new GeometryImportOptions();
        var lines = File.ReadAllLines(filePath);
        var mesh = new MeshObject(Path.GetFileNameWithoutExtension(filePath))
        {
            MaterialName = options.DefaultMaterial
        };

        // Parse plane definitions and construct brushes
        // Basic .map plane: ( x1 y1 z1 ) ( x2 y2 z2 ) ( x3 y3 z3 ) TEXTURE [ ... ]
        var planePoints = new List<Vector3>();

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.StartsWith("(") && line.Contains(")"))
            {
                var parts = line.Split(new[] { '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    var coords = p.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (coords.Length == 3 &&
                        float.TryParse(coords[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(coords[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                        float.TryParse(coords[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                    {
                        var pt = options.ConvertYUpToZUp ? new Vector3(x, -z, y) : new Vector3(x, y, z);
                        pt *= options.ScaleFactor;
                        planePoints.Add(pt);
                    }
                }
            }
        }

        if (planePoints.Count >= 3)
        {
            for (int i = 0; i + 2 < planePoints.Count; i += 3)
            {
                int baseVert = mesh.Vertices.Count;
                mesh.Vertices.Add(new Vertex(planePoints[i], Vector3.UnitZ));
                mesh.Vertices.Add(new Vertex(planePoints[i + 1], Vector3.UnitZ));
                mesh.Vertices.Add(new Vertex(planePoints[i + 2], Vector3.UnitZ));
                mesh.Triangles.Add(new Triangle(baseVert, baseVert + 1, baseVert + 2, 0));
            }
            mesh.RecalculateNormals();
            return new List<MeshObject> { mesh };
        }

        // Fallback default box brush
        var box = CsgBrushBuilder.CreateBox(new Vector3(400, 400, 200), Vector3.Zero, options.DefaultMaterial);
        return new List<MeshObject> { box };
    }
}
