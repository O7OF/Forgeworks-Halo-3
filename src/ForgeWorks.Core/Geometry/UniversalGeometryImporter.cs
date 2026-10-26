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
    public bool AutoWeldVertices { get; set; } = false;
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

        var meshes = ext switch
        {
            ".ass" => AssImporter.ImportFromFile(filePath),
            ".jms" => JmsImporter.ImportFromFile(filePath),
            ".rmf" => RmfGeometryImporter.ImportFromFile(filePath, options),
            ".map" => MapBrushImporter.ImportFromFile(filePath, options),
            _ => ImportWithAssimpOrFallback(filePath, options)
        };

        if (options.AutoWeldVertices && meshes != null)
        {
            var repReport = new Diagnostics.RepairReport();
            float weldDist = 0.005f * options.ScaleFactor;
            foreach (var m in meshes)
            {
                Diagnostics.GeometryAutoRepair.WeldVertices(m, weldDist, repReport);
                Diagnostics.GeometryAutoRepair.FixDegenerateTriangles(m, repReport);
                Diagnostics.GeometryAutoRepair.FixOverlappingSurfaces(m, repReport);
            }
        }

        return meshes ?? new List<MeshObject>();
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
        // Collapse dummy FBX pivot nodes directly into transform matrices
        context.SetConfig(new Assimp.Configs.BooleanPropertyConfig("IMPORT_FBX_PRESERVE_PIVOTS", false));

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
        string modelDir = Path.GetDirectoryName(filePath) ?? string.Empty;

        var resolvedMaterials = new (string name, string? texturePath)[scene.MaterialCount];
        for (int i = 0; i < scene.MaterialCount; i++)
        {
            var aMat = scene.Materials[i];
            string mName = options.DefaultMaterial;
            if (!string.IsNullOrWhiteSpace(aMat.Name) && aMat.Name != "DefaultMaterial")
            {
                mName = SanitizeMaterialName(aMat.Name);
            }

            // Map two-sided and render-only flags from Assimp material properties and naming
            bool isTwoSided = aMat.IsTwoSided || mName.Contains("foliage") || mName.Contains("leaves");
            
            bool isSolidKeyword = mName.Contains("ground") || mName.Contains("cliff") || mName.Contains("rock") ||
                                  mName.Contains("terrain") || mName.Contains("wall") || mName.Contains("floor") ||
                                  mName.Contains("plate") || mName.Contains("panel") || mName.Contains("metal") ||
                                  mName.Contains("stone") || mName.Contains("wood") || mName.Contains("concrete") ||
                                  mName.Contains("brick") || mName.Contains("ramp") || mName.Contains("bridge") ||
                                  mName.Contains("roof") || mName.Contains("sand") || mName.Contains("ice") ||
                                  mName.Contains("dirt") || mName.Contains("grass") || mName.Contains("ladder") ||
                                  mName.Contains("scratchy") || mName.Contains("graytech");

            bool isRenderOnly = !isSolidKeyword && ((aMat.HasOpacity && aMat.Opacity < 0.95f) ||
                                mName.Contains("light") || mName.Contains("lens") ||
                                mName.Contains("glow") || mName.Contains("decal") ||
                                mName.Contains("sprite") || aMat.Name.StartsWith("!") || aMat.Name.EndsWith("!"));

            if (isTwoSided && !mName.Contains("%"))
            {
                mName += "%";
            }
            if (isRenderOnly && !mName.EndsWith("!"))
            {
                mName += "!";
            }

            string? texPath = null;
            if (aMat.HasTextureDiffuse)
            {
                texPath = ResolveTexturePath(modelDir, aMat.TextureDiffuse.FilePath, mName);
            }
            else
            {
                texPath = ResolveTexturePath(modelDir, null, mName);
            }

            resolvedMaterials[i] = (mName, texPath);
        }

        // Traverse scene node graph to collect meshes with their true hierarchy transforms
        void ProcessNode(Node node, Matrix4x4 parentTransform)
        {
            var currentTransform = node.Transform * parentTransform;

            if (node.HasMeshes)
            {
                float subDet = SubDeterminant3x3(currentTransform);
                bool flipWinding = subDet < 0;

                // Strip any remaining FBX internal pivot tags from node name
                string cleanNodeName = node.Name ?? string.Empty;
                int fbxMarker = cleanNodeName.IndexOf("_$AssimpFbx$_", StringComparison.OrdinalIgnoreCase);
                if (fbxMarker >= 0)
                {
                    cleanNodeName = cleanNodeName[..fbxMarker];
                }

                for (int i = 0; i < node.MeshCount; i++)
                {
                    int mIdx = node.MeshIndices[i];
                    if (mIdx < 0 || mIdx >= scene.MeshCount) continue;

                    var aMesh = scene.Meshes[mIdx];
                    string meshName = !string.IsNullOrWhiteSpace(cleanNodeName)
                        ? cleanNodeName
                        : (!string.IsNullOrWhiteSpace(aMesh.Name) ? aMesh.Name : $"{baseName}_{results.Count}");

                    if (node.MeshCount > 1)
                    {
                        meshName = $"{meshName}_{i}";
                    }

                    var meshObj = BuildMeshObject(aMesh, meshName, currentTransform, flipWinding, options, resolvedMaterials);
                    results.Add(meshObj);
                }
            }

            for (int c = 0; c < node.ChildCount; c++)
            {
                ProcessNode(node.Children[c], currentTransform);
            }
        }

        if (scene.RootNode != null)
        {
            ProcessNode(scene.RootNode, Matrix4x4.Identity);
        }

        // Fallback if node traversal didn't pick up meshes
        if (results.Count == 0)
        {
            for (int mIdx = 0; mIdx < scene.MeshCount; mIdx++)
            {
                var aMesh = scene.Meshes[mIdx];
                string meshName = string.IsNullOrWhiteSpace(aMesh.Name) ? $"{baseName}_{mIdx}" : aMesh.Name;
                var meshObj = BuildMeshObject(aMesh, meshName, Matrix4x4.Identity, false, options, resolvedMaterials);
                results.Add(meshObj);
            }
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

    private static MeshObject BuildMeshObject(
        Mesh aMesh,
        string meshName,
        Matrix4x4 transform,
        bool flipWinding,
        GeometryImportOptions options,
        (string name, string? texturePath)[] resolvedMaterials)
    {
        var meshObj = new MeshObject(meshName);

        // Material resolution
        string matName = options.DefaultMaterial;
        string? texPath = null;
        if (aMesh.MaterialIndex >= 0 && aMesh.MaterialIndex < resolvedMaterials.Length)
        {
            (matName, texPath) = resolvedMaterials[aMesh.MaterialIndex];
        }

        meshObj.MaterialName = matName;
        meshObj.MaterialSlots.Add(matName);
        meshObj.IsCollideable = !matName.StartsWith("!") && !meshName.Contains("light", StringComparison.OrdinalIgnoreCase) && !meshName.Contains("lens", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(texPath))
        {
            meshObj.SourceTexturePath = texPath;
            meshObj.MaterialTextureMap[matName] = texPath;
        }

        bool convertYUp = options.ConvertYUpToZUp;

        // Pre-allocate vertices for high-poly performance
        meshObj.Vertices = new List<Vertex>(aMesh.VertexCount);
        for (int i = 0; i < aMesh.VertexCount; i++)
        {
            var p = aMesh.Vertices[i];
            var pos = TransformPoint(transform, p);

            if (convertYUp)
            {
                pos = new Vector3(pos.X, -pos.Z, pos.Y);
            }
            pos *= options.ScaleFactor;

            Vector3 normal = Vector3.UnitZ;
            if (aMesh.HasNormals && i < aMesh.Normals.Count)
            {
                normal = TransformNormal(transform, aMesh.Normals[i]);
                if (convertYUp)
                {
                    normal = new Vector3(normal.X, -normal.Z, normal.Y);
                }
                float nLen = normal.Length();
                if (nLen > 1e-5f) normal /= nLen;
            }

            var v = new Vertex(pos, normal);

            if (aMesh.HasTextureCoords(0))
            {
                var uv = aMesh.TextureCoordinateChannels[0][i];
                v.UVs.Add(new Vector2(uv.X, 1.0f - uv.Y));
            }
            else
            {
                v.UVs.Add(new Vector2(pos.X * 0.005f, pos.Y * 0.005f));
            }

            if (aMesh.HasVertexColors(0))
            {
                var c = aMesh.VertexColorChannels[0][i];
                v.Color = new Vector4(c.X, c.Y, c.Z, c.W);
            }

            meshObj.Vertices.Add(v);
        }

        // Triangles with winding check
        meshObj.Triangles = new List<Triangle>(aMesh.FaceCount);
        for (int f = 0; f < aMesh.FaceCount; f++)
        {
            var face = aMesh.Faces[f];
            if (face.IndexCount == 3)
            {
                int i0 = face.Indices[0];
                int i1 = flipWinding ? face.Indices[2] : face.Indices[1];
                int i2 = flipWinding ? face.Indices[1] : face.Indices[2];
                meshObj.Triangles.Add(new Triangle(i0, i1, i2, 0));
            }
            else if (face.IndexCount >= 4)
            {
                for (int j = 1; j < face.IndexCount - 1; j++)
                {
                    int i0 = face.Indices[0];
                    int i1 = flipWinding ? face.Indices[j + 1] : face.Indices[j];
                    int i2 = flipWinding ? face.Indices[j] : face.Indices[j + 1];
                    meshObj.Triangles.Add(new Triangle(i0, i1, i2, 0));
                }
            }
        }

        if (!aMesh.HasNormals)
        {
            meshObj.RecalculateNormals();
        }

        return meshObj;
    }

    private static string? ResolveTexturePath(string modelDir, string? rawFilePath, string materialName)
    {
        var searchDirs = new List<string>
        {
            modelDir,
            Path.Combine(modelDir, "textures"),
            Path.Combine(modelDir, "bitmaps"),
            Path.Combine(modelDir, "Scenery"),
            Path.Combine(modelDir, "scenery")
        };

        if (!string.IsNullOrEmpty(rawFilePath))
        {
            string unescaped = rawFilePath;
            try { unescaped = Uri.UnescapeDataString(rawFilePath); } catch { }

            if (File.Exists(unescaped)) return unescaped;
            if (File.Exists(rawFilePath)) return rawFilePath;

            string fn1 = Path.GetFileName(unescaped);
            string fn2 = Path.GetFileName(rawFilePath);

            foreach (var d in searchDirs)
            {
                if (!Directory.Exists(d)) continue;
                string p1 = Path.Combine(d, fn1);
                if (File.Exists(p1)) return p1;
                string p2 = Path.Combine(d, fn2);
                if (File.Exists(p2)) return p2;
            }

            string rel = Path.Combine(modelDir, unescaped.Replace('/', '\\'));
            if (File.Exists(rel)) return rel;
        }

        // Test variations of material name (stripping prefixes like Material_ or level name)
        var nameCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { materialName };
        string stripped = materialName;
        if (stripped.StartsWith("Material_", StringComparison.OrdinalIgnoreCase)) stripped = stripped[9..];
        if (stripped.StartsWith("Mat_", StringComparison.OrdinalIgnoreCase)) stripped = stripped[4..];
        nameCandidates.Add(stripped);
        nameCandidates.Add(stripped.Replace('_', ' '));
        nameCandidates.Add(materialName.Replace('_', ' '));

        int firstUs = stripped.IndexOf('_');
        if (firstUs > 0 && firstUs < stripped.Length - 1)
        {
            string sub = stripped[(firstUs + 1)..];
            nameCandidates.Add(sub);
            nameCandidates.Add(sub.Replace('_', ' '));
        }

        string[] exts = { ".png", ".tif", ".tiff", ".jpg", ".jpeg", ".tga", ".dds", ".bmp" };
        foreach (var d in searchDirs)
        {
            if (!Directory.Exists(d)) continue;
            foreach (var cand in nameCandidates)
            {
                foreach (var ext in exts)
                {
                    string p = Path.Combine(d, cand + ext);
                    if (File.Exists(p)) return p;
                }
            }
        }

        return null;
    }

    public static string SanitizeMaterialName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "river riverworld_ground";
        string s = name.Trim().ToLowerInvariant();

        // Extract prefix flags
        string prefix = "";
        while (s.Length > 0 && (s[0] == '%' || s[0] == '!' || s[0] == '+' || s[0] == '$' || s[0] == '@' || s[0] == '~'))
        {
            prefix += s[0];
            s = s[1..];
        }

        // Extract suffix flags (e.g. lights! -> prefix = !)
        while (s.Length > 0 && (s[^1] == '!' || s[^1] == '%' || s[^1] == '$' || s[^1] == '@' || s[^1] == '~'))
        {
            char flag = s[^1];
            if (!prefix.Contains(flag)) prefix = flag + prefix;
            s = s[..^1];
        }

        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9_]+", "_").Trim('_');
        return prefix + (string.IsNullOrEmpty(s) ? "custom" : s);
    }

    private static Vector3 TransformPoint(Matrix4x4 m, Vector3 p)
    {
        return Vector3.Transform(p, m);
    }

    private static Vector3 TransformNormal(Matrix4x4 m, Vector3 n)
    {
        var vec = Vector3.TransformNormal(n, m);
        float len = vec.Length();
        return len > 1e-5f ? vec / len : Vector3.UnitZ;
    }

    private static float SubDeterminant3x3(Matrix4x4 m)
    {
        return m.M11 * (m.M22 * m.M33 - m.M23 * m.M32)
             - m.M12 * (m.M21 * m.M33 - m.M23 * m.M31)
             + m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);
    }
}
