using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace ForgeWorks.Core.Geometry;

/// <summary>
/// Halo Amalgam Scene Specification (.ASS) Exporter.
/// Fully compliant with H2EK, H3EK, and HREK tool.exe structure parser.
/// </summary>
public sealed class AssExporter
{
    public static void ExportToFile(
        string filepath,
        List<string> materials,
        List<MeshObject> objects,
        int version = 7,
        float scale = 1.0f,
        string? levelName = null)
    {
        var dir = Path.GetDirectoryName(filepath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        levelName ??= Path.GetFileNameWithoutExtension(filepath);

        using var stream = File.Create(filepath);
        ExportToStream(stream, materials, objects, version, scale, levelName);
    }

    public static void ExportToStream(
        Stream destination,
        List<string> materials,
        List<MeshObject> objects,
        int version = 7,
        float scale = 1.0f,
        string? levelName = null)
    {
        using var writer = new StreamWriter(destination, leaveOpen: true);
        var inv = CultureInfo.InvariantCulture;

        if (materials == null || materials.Count == 0)
        {
            materials = new List<string> { "default" };
        }

        // 1. HEADER
        writer.WriteLine(";### HEADER ###");
        writer.WriteLine(version.ToString());
        writer.WriteLine("\"BLENDER\"");
        writer.WriteLine("\"5.2\"");
        writer.WriteLine("\"ForgeWorks\"");
        writer.WriteLine("\"O7OF\"\n");

        var cleanMaterials = new List<string>(materials.Count);
        for (int i = 0; i < materials.Count; i++)
        {
            string clean = SanitizeMaterialName(materials[i], levelName);
            if (!cleanMaterials.Contains(clean)) cleanMaterials.Add(clean);
        }
        if (cleanMaterials.Count == 0) cleanMaterials.Add(!string.IsNullOrEmpty(levelName) ? $"{levelName} ground" : "river riverworld_ground");

        // 2. MATERIALS
        writer.WriteLine($";### MATERIALS ###\n{cleanMaterials.Count}\n");
        for (int i = 0; i < cleanMaterials.Count; i++)
        {
            writer.WriteLine($";MATERIAL {i}");
            writer.WriteLine($"\"{cleanMaterials[i]}\"");
            writer.WriteLine("\"\""); // Material variant
            if (version >= 4)
            {
                writer.WriteLine("0");
            }
            writer.WriteLine();
        }

        // 3. OBJECTS
        writer.WriteLine($";### OBJECTS ###\n{objects.Count}\n");
        for (int i = 0; i < objects.Count; i++)
        {
            var obj = objects[i];
            writer.WriteLine($";OBJECT {i}");
            writer.WriteLine($"\"MESH\"\n\"\"\n\"{obj.Name}\"");
            writer.WriteLine(obj.Vertices.Count.ToString());

            foreach (var v in obj.Vertices)
            {
                // Position X Y Z
                writer.WriteLine(string.Format(inv, "{0:F10}\t{1:F10}\t{2:F10}",
                    CleanFloat(v.Position.X * scale),
                    CleanFloat(v.Position.Y * scale),
                    CleanFloat(v.Position.Z * scale)));

                // Normal I J K
                writer.WriteLine(string.Format(inv, "{0:F10}\t{1:F10}\t{2:F10}",
                    CleanFloat(v.Normal.X),
                    CleanFloat(v.Normal.Y),
                    CleanFloat(v.Normal.Z)));

                // Vertex Color (R G B) - only ASS version >= 6
                if (version >= 6)
                {
                    writer.WriteLine(string.Format(inv, "{0:F10}\t{1:F10}\t{2:F10}",
                        CleanFloat(v.Color.X),
                        CleanFloat(v.Color.Y),
                        CleanFloat(v.Color.Z)));
                }

                // Node influences (0 for static structure)
                writer.WriteLine("0");

                // UVs (2-tuple for ASS < 5, 3-tuple for ASS >= 5)
                var uvs = v.UVs.Count > 0 ? v.UVs : new List<Vector2> { Vector2.Zero };
                writer.WriteLine(uvs.Count.ToString());
                foreach (var uv in uvs)
                {
                    float u = CleanFloat(uv.X);
                    float v_flipped = CleanFloat(1.0f - uv.Y); // Flip V for Blam convention
                    if (version >= 5)
                    {
                        writer.WriteLine(string.Format(inv, "{0:F10}\t{1:F10}\t0.0000000000", u, v_flipped));
                    }
                    else
                    {
                        writer.WriteLine(string.Format(inv, "{0:F10}\t{1:F10}", u, v_flipped));
                    }
                }
                if (version >= 5)
                {
                    writer.WriteLine();
                }
            }

            // Triangles
            writer.WriteLine(obj.Triangles.Count.ToString());
            foreach (var tri in obj.Triangles)
            {
                string matName = (tri.MaterialIndex >= 0 && tri.MaterialIndex < obj.MaterialSlots.Count)
                    ? obj.MaterialSlots[tri.MaterialIndex]
                    : obj.MaterialName;
                if (string.IsNullOrEmpty(matName)) matName = obj.MaterialName;
                matName = SanitizeMaterialName(matName, levelName);

                int matIdx = cleanMaterials.IndexOf(matName);
                if (matIdx < 0)
                {
                    matIdx = Math.Clamp(tri.MaterialIndex, 0, cleanMaterials.Count - 1);
                }
                writer.WriteLine($"{matIdx}\t\t{tri.V0}\t{tri.V1}\t{tri.V2}");
            }
            writer.WriteLine();
        }

        // 4. INSTANCES
        // Explicitly enforces Halo 3 engine hierarchy:
        // Instance 0: Scene Root (Parent -1)
        // Instance 1: b_levelroot (Parent 0) - contains all level geometry meshes
        // Instance 2: z_portals (Parent 0) - contains all cluster portal meshes
        // Instances 3..N: Mesh objects parented under b_levelroot or z_portals
        int baseParentInstances = 3; // Scene Root (0), b_levelroot (1), z_portals (2)
        int instanceCount = objects.Count + baseParentInstances;
        writer.WriteLine($";### INSTANCES ###\n{instanceCount}\n");

        void WriteTransform(Vector3 trans, Quaternion rot, float sScale)
        {
            writer.WriteLine(string.Format(inv, "{0:F10}\t{1:F10}\t{2:F10}\t{3:F10}", rot.X, rot.Y, rot.Z, rot.W));
            writer.WriteLine(string.Format(inv, "{0:F10}\t{1:F10}\t{2:F10}", trans.X, trans.Y, trans.Z));
            writer.WriteLine(version <= 1 ?
                string.Format(inv, "{0:F10}\t{1:F10}\t{2:F10}", sScale, sScale, sScale) :
                string.Format(inv, "{0:F10}", sScale));
            writer.WriteLine(string.Format(inv, "{0:F10}\t{1:F10}\t{2:F10}\t{3:F10}", rot.X, rot.Y, rot.Z, rot.W));
            writer.WriteLine(string.Format(inv, "{0:F10}\t{1:F10}\t{2:F10}", trans.X, trans.Y, trans.Z));
            writer.WriteLine(version <= 1 ?
                string.Format(inv, "{0:F10}\t{1:F10}\t{2:F10}\n", sScale, sScale, sScale) :
                string.Format(inv, "{0:F10}\n", sScale));
        }

        // Instance 0: Scene Root
        writer.WriteLine(";INSTANCE 0\n-1\n\"Scene Root\"\n0\n-1\n0");
        WriteTransform(Vector3.Zero, Quaternion.Identity, 1.0f);

        // Instance 1: b_levelroot (Parent: Scene Root [0])
        writer.WriteLine(";INSTANCE 1\n-1\n\"b_levelroot\"\n1\n0\n0");
        WriteTransform(Vector3.Zero, Quaternion.Identity, 1.0f);

        // Instance 2: z_portals (Parent: Scene Root [0])
        writer.WriteLine(";INSTANCE 2\n-1\n\"z_portals\"\n2\n0\n0");
        WriteTransform(Vector3.Zero, Quaternion.Identity, 1.0f);

        for (int i = 0; i < objects.Count; i++)
        {
            var obj = objects[i];
            int instIdx = i + baseParentInstances;
            int uniqueId = 1000 + i;

            bool isPortal = obj.Name.StartsWith("+portal", StringComparison.OrdinalIgnoreCase);
            int parentIdx = isPortal ? 2 : 1; // z_portals (2) or b_levelroot (1)

            // Decompose object transform
            Vector3 trans = Vector3.Zero;
            Quaternion rot = Quaternion.Identity;
            Vector3 scaleVec = Vector3.One;

            if (Matrix4x4.Decompose(obj.Transform, out var s, out var r, out var t))
            {
                trans = t * scale;
                rot = r;
                scaleVec = s;
            }

            float uniformScale = (scaleVec.X + scaleVec.Y + scaleVec.Z) / 3.0f;

            writer.WriteLine($";INSTANCE {instIdx}");
            writer.WriteLine(i.ToString()); // Object index
            writer.WriteLine($"\"{obj.Name}\"");
            writer.WriteLine(uniqueId.ToString());
            writer.WriteLine(parentIdx.ToString()); // Parent is b_levelroot (1) or z_portals (2)
            writer.WriteLine("0"); // Inheritance flag

            WriteTransform(trans, rot, uniformScale);
        }
    }

    public static string SanitizeMaterialName(string mat, string? defaultLevelPrefix = null)
    {
        if (string.IsNullOrWhiteSpace(mat))
            return !string.IsNullOrEmpty(defaultLevelPrefix) ? $"{defaultLevelPrefix} ground" : "river riverworld_ground";
        string m = mat.Trim();

        // Remap legacy generic editor placeholder 'boxcanvas' brushes to valid Halo 3 environment shaders
        if (m.StartsWith("boxcanvas", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrEmpty(defaultLevelPrefix) ? $"{defaultLevelPrefix} ground" : "river riverworld_ground";
        }

        // If special engine surface (+sky, +portal, etc.), preserve exactly
        if (m.StartsWith("+") || m.StartsWith("%+"))
        {
            return m;
        }

        // Check if material is water or has water material symbol (')
        bool isWater = m.EndsWith("'") ||
                       m.Contains("riverworld_water", StringComparison.OrdinalIgnoreCase) ||
                       m.Contains("shader_water", StringComparison.OrdinalIgnoreCase) ||
                       m.Contains("water_rough", StringComparison.OrdinalIgnoreCase);

        if (isWater)
        {
            // If it's Riverworld water, normalize to authentic Halo 3 shader convention
            if (m.Contains("riverworld", StringComparison.OrdinalIgnoreCase))
            {
                return "river riverworld_water_rough'";
            }

            // Clean leading ! or % from water collection prefix
            m = m.TrimStart('!', '%', '~', '@', '$', '*').Trim();
            if (!m.EndsWith("'")) m = $"{m}'";
            return m;
        }

        // Extract leading semantic flags: %, !, %!, +, $, *, @, ~
        string prefix = "";
        while (m.Length > 0 && (m[0] == '%' || m[0] == '!' || m[0] == '+' || m[0] == '$' || m[0] == '*' || m[0] == '@' || m[0] == '~'))
        {
            prefix += m[0];
            m = m[1..];
        }

        // Also extract any trailing flags (e.g. lights! or foliage%) and normalize to prefix
        while (m.Length > 0 && (m[^1] == '!' || m[^1] == '%' || m[^1] == '$' || m[^1] == '*' || m[^1] == '@' || m[^1] == '~'))
        {
            char flag = m[^1];
            if (!prefix.Contains(flag)) prefix = flag + prefix;
            m = m[..^1];
        }

        // Clean out invalid punctuation without mangling into underscores
        m = System.Text.RegularExpressions.Regex.Replace(m, @"[^A-Za-z0-9_\s]+", "").Trim();
        if (string.IsNullOrEmpty(m)) m = "custom";

        // If regular geometry (no special +sky), does not already have a collection prefix (no space),
        // and defaultLevelPrefix is available, prepend collection prefix so Tool resolves the shader!
        if (!m.Contains(' ') && !string.IsNullOrEmpty(defaultLevelPrefix))
        {
            m = $"{defaultLevelPrefix} {m}";
        }

        return prefix + m;
    }

    private static float CleanFloat(float val, float fallback = 0.0f)
    {
        if (float.IsNaN(val) || float.IsInfinity(val) || MathF.Abs(val) > 1e12f)
            return fallback;
        return val;
    }
}
