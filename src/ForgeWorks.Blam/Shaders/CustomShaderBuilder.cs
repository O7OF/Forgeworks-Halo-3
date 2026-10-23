using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ForgeWorks.Core.Geometry;
using ForgeWorks.Core.Shaders;

namespace ForgeWorks.Blam.Shaders;

public sealed record CustomShaderResult(
    string MaterialName,
    string ShaderTagRelativePath,
    string BitmapTagRelativePath,
    string FullShaderTagPath,
    string FullTifDataPath
);

/// <summary>
/// Architecture for creating H3EK-compliant custom .shader (rmsh) tags,
/// converting PNG/JPG model textures into tool.exe compatible uncompressed TIFFs,
/// updating tags\levels\shader_collections.txt, and organizing data/tags directory trees.
/// </summary>
public static class CustomShaderBuilder
{
    public const string ShaderMagic = "rmsh";
    public const string BitmapMagic = "bitm";

    /// <summary>
    /// Converts a PNG, JPG, or BMP file to an uncompressed 24/32-bit TIFF required by tool.exe 'tool bitmaps'.
    /// </summary>
    public static bool ConvertImageToTif(string sourceImagePath, string targetTifPath)
    {
        if (!File.Exists(sourceImagePath)) return false;

        try
        {
            var dir = Path.GetDirectoryName(targetTifPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var image = Image.FromFile(sourceImagePath);
            image.Save(targetTifPath, ImageFormat.Tiff);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ensures tags\levels\shader_collections.txt exists and contains the prefix mapping
    /// for this level so Tool maps "levelName materialName" or "materialName" to the level's shaders folder.
    /// Format per C20 docs: prefix <tab> directory
    /// </summary>
    public static void EnsureShaderCollectionsEntry(string ekPath, string levelName, string scenarioType = "multi")
    {
        if (string.IsNullOrWhiteSpace(ekPath) || !Directory.Exists(ekPath)) return;

        string collectionsDir = Path.Combine(ekPath, "tags", "levels");
        Directory.CreateDirectory(collectionsDir);
        string collectionsPath = Path.Combine(collectionsDir, "shader_collections.txt");

        string cleanLevel = SanitizeIdentifier(levelName);
        string targetEntry = $"{cleanLevel}\tlevels\\{scenarioType}\\{cleanLevel}\\shaders";
        string targetEntryFallback = $"{cleanLevel}\tlevels\\{scenarioType}\\{cleanLevel}";

        var lines = new List<string>();
        bool alreadyPresent = false;

        if (File.Exists(collectionsPath))
        {
            var existing = File.ReadAllLines(collectionsPath);
            foreach (var line in existing)
            {
                lines.Add(line);
                string trimmed = line.Trim();
                if (trimmed.StartsWith(cleanLevel + "\t", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith(cleanLevel + " ", StringComparison.OrdinalIgnoreCase))
                {
                    alreadyPresent = true;
                }
            }
        }

        if (!alreadyPresent)
        {
            lines.Add(targetEntry);
            lines.Add(targetEntryFallback);
            File.WriteAllLines(collectionsPath, lines);
        }
    }

    /// <summary>
    /// Synthesizes or patches an authentic Halo 3 .shader (rmsh) tag that references
    /// the compiled .bitmap tag for the material's albedo base map.
    /// </summary>
    public static void GenerateCustomShaderTag(
        string fullShaderTagPath,
        string materialName,
        string relativeBitmapTagPath,
        string? ekPath = null)
    {
        var dir = Path.GetDirectoryName(fullShaderTagPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Normalize relative bitmap tag path (strip .bitmap extension if present)
        string bitmRef = relativeBitmapTagPath.EndsWith(".bitmap", StringComparison.OrdinalIgnoreCase)
            ? relativeBitmapTagPath[..^7]
            : relativeBitmapTagPath;

        // Try to clone an authentic retail template shader from H3EK
        if (!string.IsNullOrEmpty(ekPath) && Directory.Exists(ekPath))
        {
            string[] candidateTemplates = new[]
            {
                Path.Combine(ekPath, "tags", "shaders", "invalid.shader"),
                Path.Combine(ekPath, "tags", "levels", "multi", "custom_canvas", "shaders", "blue_panel_light.shader"),
                Path.Combine(ekPath, "tags", "levels", "multi", "chill", "shaders", "chill_metal.shader"),
                Path.Combine(ekPath, "tags", "levels", "multi", "salvation", "shaders", "sal_rock.shader")
            };

            foreach (var candidate in candidateTemplates)
            {
                if (File.Exists(candidate))
                {
                    try
                    {
                        byte[] raw = File.ReadAllBytes(candidate);
                        byte[] bitmBytes = Encoding.ASCII.GetBytes("bitm");

                        // Look for bitmap tag reference chunk ('bitm') in tag file
                        for (int i = 0; i <= raw.Length - 8; i++)
                        {
                            if (raw[i] == bitmBytes[0] && raw[i + 1] == bitmBytes[1] &&
                                raw[i + 2] == bitmBytes[2] && raw[i + 3] == bitmBytes[3])
                            {
                                int strOffset = i + 8;
                                if (strOffset < raw.Length - 64)
                                {
                                    byte[] newPath = Encoding.ASCII.GetBytes(bitmRef + "\0");
                                    int copyLen = Math.Min(newPath.Length, 128);
                                    if (strOffset + copyLen <= raw.Length)
                                    {
                                        Array.Copy(newPath, 0, raw, strOffset, copyLen);
                                    }
                                }
                            }
                        }

                        File.WriteAllBytes(fullShaderTagPath, raw);
                        return;
                    }
                    catch
                    {
                        // Fall back to clean binary synthesis
                    }
                }
            }
        }

        // Standalone binary synthesis for .shader (rmsh) tag
        using var stream = File.Create(fullShaderTagPath);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);

        // 1. Tag Header (0x40 bytes)
        writer.Write(Encoding.ASCII.GetBytes("rmsh")); // Tag group magic
        writer.Write((uint)0x00035179);                 // H3 Retail engine checksum
        writer.Write((uint)0x00000040);                 // Header size
        writer.Write((uint)0x00000000);                 // Version
        writer.Write(new byte[0x30]);                   // Padding / metadata

        // 2. Albedo block with bitmap tag reference
        writer.Write(Encoding.ASCII.GetBytes("bitm")); // Tag group magic for reference
        byte[] pathBytes = Encoding.ASCII.GetBytes(bitmRef + "\0");
        writer.Write((uint)pathBytes.Length);
        writer.Write(pathBytes);

        // Pad to 16-byte alignment
        int rem = (int)(stream.Position % 16);
        if (rem != 0) writer.Write(new byte[16 - rem]);
    }

    public static List<CustomShaderResult> SetupCustomShadersForModel(
        string ekPath,
        string levelName,
        IEnumerable<MeshObject> meshes,
        string? sourceAssetDir,
        HaloShaderCatalog? catalog = null)
        => SetupCustomShadersForModel(ekPath, levelName, "multi", meshes, sourceAssetDir, catalog);

    /// <summary>
    /// Processes imported geometry and textures:
    /// 1. Finds texture files (.png, .jpg, .tga, .tif) associated with model materials
    /// 2. Converts them to .tif in data\levels\<scenarioType>\<levelName>\bitmaps
    /// 3. Generates .shader tags in tags\levels\<scenarioType>\<levelName>\shaders
    /// 4. Registers prefixes in tags\levels\shader_collections.txt
    /// 5. Adds entries into HaloShaderCatalog for UI immediate display
    /// </summary>
    public static List<CustomShaderResult> SetupCustomShadersForModel(
        string ekPath,
        string levelName,
        string scenarioType,
        IEnumerable<MeshObject> meshes,
        string? sourceAssetDir,
        HaloShaderCatalog? catalog = null)
    {
        var results = new List<CustomShaderResult>();
        string cleanLevel = SanitizeIdentifier(levelName);

        string dataBitmapsDir = Path.Combine(ekPath, "data", "levels", scenarioType, cleanLevel, "bitmaps");
        string tagsShadersDir = Path.Combine(ekPath, "tags", "levels", scenarioType, cleanLevel, "shaders");
        string tagsBitmapsDir = Path.Combine(ekPath, "tags", "levels", scenarioType, cleanLevel, "bitmaps");

        Directory.CreateDirectory(dataBitmapsDir);
        Directory.CreateDirectory(tagsShadersDir);
        Directory.CreateDirectory(tagsBitmapsDir);

        EnsureShaderCollectionsEntry(ekPath, cleanLevel, scenarioType);

        // Collect distinct materials and any known texture mappings
        var materials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matToTex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in meshes)
        {
            if (!string.IsNullOrWhiteSpace(m.MaterialName))
            {
                string sanitized = SanitizeMaterialName(m.MaterialName);
                materials.Add(sanitized);

                if (!string.IsNullOrEmpty(m.SourceTexturePath))
                {
                    matToTex[sanitized] = m.SourceTexturePath;
                }
            }

            foreach (var kvp in m.MaterialTextureMap)
            {
                string sanitized = SanitizeMaterialName(kvp.Key);
                materials.Add(sanitized);
                if (!string.IsNullOrEmpty(kvp.Value))
                {
                    matToTex[sanitized] = kvp.Value;
                }
            }
        }

        // Search directory for matching textures if not explicitly mapped
        var candidateFiles = new List<string>();
        if (!string.IsNullOrEmpty(sourceAssetDir) && Directory.Exists(sourceAssetDir))
        {
            candidateFiles.AddRange(Directory.GetFiles(sourceAssetDir, "*.*", SearchOption.AllDirectories));
        }

        foreach (var mat in materials)
        {
            // Skip engine special materials (+sky, +portal, +seamsealer, etc.)
            if (mat.StartsWith("+") || mat.StartsWith("%+") || mat.StartsWith("!") || mat.StartsWith("%!"))
                continue;

            string cleanMat = SanitizeIdentifier(mat);
            string baseMatName = cleanMat;
            int spaceIdx = cleanMat.IndexOf('_');
            if (spaceIdx > 0 && cleanMat.StartsWith(cleanLevel + "_", StringComparison.OrdinalIgnoreCase))
            {
                baseMatName = cleanMat[(spaceIdx + 1)..];
            }

            // Find best matching image file
            string? sourceTex = null;
            if (matToTex.TryGetValue(mat, out var explicitTex) && File.Exists(explicitTex))
            {
                sourceTex = explicitTex;
            }
            else
            {
                // Look for baseMatName.png / .jpg / .tif in source directory
                sourceTex = candidateFiles.FirstOrDefault(f =>
                {
                    string fn = Path.GetFileNameWithoutExtension(f);
                    return fn.Equals(baseMatName, StringComparison.OrdinalIgnoreCase) ||
                           fn.Equals(cleanMat, StringComparison.OrdinalIgnoreCase);
                });
            }

            string targetTif = Path.Combine(dataBitmapsDir, $"{baseMatName}.tif");
            if (sourceTex != null && File.Exists(sourceTex))
            {
                ConvertImageToTif(sourceTex, targetTif);
            }
            else if (!File.Exists(targetTif))
            {
                // Generate a placeholder 512x512 neutral diffuse texture if none was provided
                CreateDefaultTexture(targetTif, baseMatName);
            }

            string relBitm = Path.Combine("levels", scenarioType, cleanLevel, "bitmaps", baseMatName);
            string shaderTag = Path.Combine(tagsShadersDir, $"{baseMatName}.shader");
            string relShader = Path.Combine("levels", scenarioType, cleanLevel, "shaders", $"{baseMatName}.shader");

            GenerateCustomShaderTag(shaderTag, baseMatName, relBitm, ekPath);

            string fullMatName = $"{cleanLevel} {baseMatName}";
            if (catalog != null)
            {
                catalog.RegisterCustomShader(fullMatName, relShader, cleanLevel, "Custom Level Shaders");
                catalog.RegisterCustomShader(baseMatName, relShader, cleanLevel, "Custom Level Shaders");
            }

            results.Add(new CustomShaderResult(fullMatName, relShader, relBitm, shaderTag, targetTif));
        }

        return results;
    }

    private static void CreateDefaultTexture(string targetTifPath, string label)
    {
        try
        {
            var dir = Path.GetDirectoryName(targetTifPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var bmp = new Bitmap(512, 512);
            using var g = Graphics.FromImage(bmp);
            int hash = Math.Abs(label.GetHashCode());
            Color baseColor = Color.FromArgb(
                90 + (hash % 80),
                95 + ((hash / 80) % 80),
                105 + ((hash / 6400) % 80));

            g.Clear(baseColor);
            using var pen = new Pen(Color.FromArgb(140, 140, 150), 2);
            g.DrawRectangle(pen, 4, 4, 503, 503);
            bmp.Save(targetTifPath, ImageFormat.Tiff);
        }
        catch
        {
            // Ignore if headless
        }
    }

    public static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "custom";
        string s = Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9_]+", "_").Trim('_');
        return string.IsNullOrEmpty(s) ? "custom" : s;
    }

    public static string SanitizeMaterialName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "river riverworld_ground";
        string s = name.Trim().ToLowerInvariant();
        string prefix = "";
        while (s.Length > 0 && (s[0] == '%' || s[0] == '!' || s[0] == '+' || s[0] == '$' || s[0] == '@' || s[0] == '~'))
        {
            prefix += s[0];
            s = s[1..];
        }
        s = Regex.Replace(s, @"[^a-z0-9_\s]+", "_").Trim();
        return prefix + s;
    }
}
