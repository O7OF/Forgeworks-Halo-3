using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Assimp;

namespace ForgeWorks.Core.Geometry;

public sealed class GeometryImportOptions
{
    public float ScaleFactor { get; set; } = 1.0f;
    public bool ConvertYUpToZUp { get; set; } = true;
    public string DefaultMaterial { get; set; } = "river riverworld_ground";
    public bool AutoGenerateNormals { get; set; } = true;
    public bool MergeMeshes { get; set; } = false;
}

public static class UniversalGeometryImporter
{
    public static readonly string[] SupportedExtensions = new[]
    {
        ".obj", ".fbx", ".gltf", ".glb", ".ass", ".jms", ".rmf", ".map",
        ".dae", ".blend", ".stl", ".ply", ".3ds", ".lwo", ".x"
    };

    public static bool IsSupported(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return Array.IndexOf(SupportedExtensions, ext) >= 0;
    }

    public static List<MeshObject> Import(string filePath, GeometryImportOptions? options = null)
    {
        options ??= new GeometryImportOptions();
        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        return ext switch
        {
            ".ass" => AssImporter.ImportFromFile(filePath),
            ".jms" => JmsImporter.ImportFromFile(filePath),
            ".rmf" => RmfGeometryImporter.ImportFromFile(filePath, options),
            ".map" => MapBrushImporter.ImportFromFile(filePath, options),
            _ => ImportWithAssimpOrFallback(filePath, options)
        };
    }

    private static List<MeshObject> ImportWithAssimpOrFallback(string filePath, GeometryImportOptions options)
    {
        try
        {
            return ImportWithAssimp(filePath, options);
        }
        catch
        {
            // Fallback for .obj if Assimp native dll has platform issues
            if (Path.GetExtension(filePath).Equals(".obj", StringComparison.OrdinalIgnoreCase))
            {
                return ObjImporter.ImportFromFile(filePath);
            }
            throw;
        }
    }

    private static List<MeshObject> ImportWithAssimp(string filePath, GeometryImportOptions options)
    {
        using var context = new AssimpContext();
        var steps = PostProcessSteps.Triangulate |
                    PostProcessSteps.JoinIdenticalVertices |
                    PostProcessSteps.ImproveCacheLocality;

        if (options.AutoGenerateNormals)
            steps |= PostProcessSteps.GenerateSmoothNormals;

        var scene = context.ImportFile(filePath, steps);
        if (scene == null || scene.MeshCount == 0)
        {
            throw new InvalidOperationException($"No mesh geometry found in '{filePath}'.");
        }

        var results = new List<MeshObject>();
        string baseName = Path.GetFileNameWithoutExtension(filePath);

        for (int mIdx = 0; mIdx < scene.MeshCount; mIdx++)
        {
            var aMesh = scene.Meshes[mIdx];
            string meshName = string.IsNullOrWhiteSpace(aMesh.Name) ? $"{baseName}_{mIdx}" : aMesh.Name;
            var meshObj = new MeshObject(meshName);

            // Material resolution
            string matName = options.DefaultMaterial;
            if (aMesh.MaterialIndex >= 0 && aMesh.MaterialIndex < scene.MaterialCount)
            {
                var aMat = scene.Materials[aMesh.MaterialIndex];
                if (!string.IsNullOrWhiteSpace(aMat.Name) && aMat.Name != "DefaultMaterial")
                {
                    matName = aMat.Name;
                }
            }
            meshObj.MaterialName = matName;
            meshObj.MaterialSlots.Add(matName);

            // Vertices
            for (int i = 0; i < aMesh.VertexCount; i++)
            {
                var p = aMesh.Vertices[i];
                Vector3 pos = new(p.X, p.Y, p.Z);

                // Coordinate conversion: DCC Y-Up (Assimp default) to Blam! Z-Up
                if (options.ConvertYUpToZUp)
                {
                    // X = X, Y = -Z, Z = Y or X=Forward, Y=Left, Z=Up
                    pos = new Vector3(pos.X, -pos.Z, pos.Y);
                }
                pos *= options.ScaleFactor;

                Vector3 normal = Vector3.UnitZ;
                if (aMesh.HasNormals && i < aMesh.Normals.Count)
                {
                    var n = aMesh.Normals[i];
                    normal = options.ConvertYUpToZUp ? new Vector3(n.X, -n.Z, n.Y) : new Vector3(n.X, n.Y, n.Z);
                    float nLen = normal.Length();
                    if (nLen > 1e-5f) normal /= nLen;
                }

                var v = new Vertex(pos, normal);

                // UVs
                if (aMesh.HasTextureCoords(0))
                {
                    var uv = aMesh.TextureCoordinateChannels[0][i];
                    v.UVs.Add(new Vector2(uv.X, 1.0f - uv.Y));
                }
                else
                {
                    v.UVs.Add(new Vector2(pos.X * 0.005f, pos.Y * 0.005f));
                }

                // Vertex colors
                if (aMesh.HasVertexColors(0))
                {
                    var c = aMesh.VertexColorChannels[0][i];
                    v.Color = new System.Numerics.Vector4(c.X, c.Y, c.Z, c.W);
                }

                meshObj.Vertices.Add(v);
            }

            // Triangles / Faces
            for (int f = 0; f < aMesh.FaceCount; f++)
            {
                var face = aMesh.Faces[f];
                if (face.IndexCount == 3)
                {
                    meshObj.Triangles.Add(new Triangle(face.Indices[0], face.Indices[1], face.Indices[2], 0));
                }
                else if (face.IndexCount == 4)
                {
                    // Quad split
                    meshObj.Triangles.Add(new Triangle(face.Indices[0], face.Indices[1], face.Indices[2], 0));
                    meshObj.Triangles.Add(new Triangle(face.Indices[0], face.Indices[2], face.Indices[3], 0));
                }
            }

            meshObj.RecalculateNormals();
            results.Add(meshObj);
        }

        if (options.MergeMeshes && results.Count > 1)
        {
            var merged = new MeshObject(baseName) { MaterialName = results[0].MaterialName };
            foreach (var r in results)
            {
                CsgBooleanEngine.MergeInto(merged, r);
            }
            return new List<MeshObject> { merged };
        }

        return results;
    }
}
