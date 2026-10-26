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
/// Physical parameters for custom shader synthesis (roughness, metalness, reflections, bump).
/// </summary>
public sealed class CustomShaderProperties
{
    public string ShaderType { get; set; } = "Matte / Environment";
    public float Roughness { get; set; } = 0.95f;
    public float Metalness { get; set; } = 0.02f;
    public float EnvironmentReflection { get; set; } = 0.0f;
    public bool EnableBumpMap { get; set; } = false;
    public float BumpMapStrength { get; set; } = 0.0f;

    public static CustomShaderProperties CreateDefault(string cleanMaterialId)
    {
        var id = (cleanMaterialId ?? "").ToLowerInvariant();
        if (id.Contains("ground") || id.Contains("cliff") || id.Contains("rock") || 
            id.Contains("dirt") || id.Contains("sand") || id.Contains("snow"))
        {
            return new CustomShaderProperties
            {
                ShaderType = "Matte / Environment",
                Roughness = 0.95f,
                Metalness = 0.02f,
                EnvironmentReflection = 0.0f,
                EnableBumpMap = false,
                BumpMapStrength = 0.0f
            };
        }
        if (id.Contains("water"))
        {
            return new CustomShaderProperties
            {
                ShaderType = "Water / Liquid",
                Roughness = 0.1f,
                Metalness = 0.9f,
                EnvironmentReflection = 0.8f,
                EnableBumpMap = false,
                BumpMapStrength = 0.0f
            };
        }
        if (id.Contains("light") || id.Contains("arrow") || id.Contains("banner"))
        {
            return new CustomShaderProperties
            {
                ShaderType = "Matte / Render-Only",
                Roughness = 0.95f,
                Metalness = 0.0f,
                EnvironmentReflection = 0.0f,
                EnableBumpMap = false,
                BumpMapStrength = 0.0f
            };
        }
        // Base structures, panels, metal
        return new CustomShaderProperties
        {
            ShaderType = "Metallic / Structure",
            Roughness = 0.45f,
            Metalness = 0.75f,
            EnvironmentReflection = 0.25f,
            EnableBumpMap = true,
            BumpMapStrength = 0.6f
        };
    }
}

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
    /// Converts a PNG, JPG, or BMP file to an uncompressed 32-bit ARGB TIFF required by tool.exe 'tool bitmaps'.
    /// Ensures power-of-two dimensions, strips GDI+ metadata tags, and enforces CompressionNone.
    /// </summary>
    public static bool ConvertImageToTif(string sourceImagePath, string targetTifPath, bool upscaleToHalo3Standards = false)
    {
        if (!File.Exists(sourceImagePath)) return false;

        try
        {
            var dir = Path.GetDirectoryName(targetTifPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            byte[] bytes = File.ReadAllBytes(sourceImagePath);
            using var ms = new MemoryStream(bytes);
            using var src = Image.FromStream(ms);

            int targetW = EnsurePowerOfTwo(src.Width);
            int targetH = EnsurePowerOfTwo(src.Height);

            // Halo 3 Texture Standard Upscaling:
            // Low-res textures (128x128 / 256x256) upscaled to 1024x1024.
            // Ground & Cliff textures upscaled to 2048x2048 for high-fidelity terrain.
            if (upscaleToHalo3Standards)
            {
                bool isTerrain = targetTifPath.Contains("ground", StringComparison.OrdinalIgnoreCase)
                              || targetTifPath.Contains("cliff", StringComparison.OrdinalIgnoreCase)
                              || targetTifPath.Contains("terrain", StringComparison.OrdinalIgnoreCase)
                              || targetTifPath.Contains("rock", StringComparison.OrdinalIgnoreCase);

                int minDim = isTerrain ? 2048 : 1024;
                if (targetW < minDim || targetH < minDim)
                {
                    float scaleFactor = MathF.Max((float)minDim / targetW, (float)minDim / targetH);
                    targetW = EnsurePowerOfTwo((int)(targetW * scaleFactor));
                    targetH = EnsurePowerOfTwo((int)(targetH * scaleFactor));
                    targetW = Math.Clamp(targetW, 1024, 2048);
                    targetH = Math.Clamp(targetH, 1024, 2048);
                }
            }

            using var bmp = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(src, new Rectangle(0, 0, targetW, targetH));
            }

            return SaveCleanTiff(bmp, targetTifPath);
        }
        catch
        {
            return false;
        }
    }

    public static bool SaveCleanTiff(Bitmap bmp, string targetTifPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(targetTifPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Strip private GDI+ property items (tags 0x5110, 0x5111, 0x5112, 0x0301, etc.)
            foreach (var propId in bmp.PropertyIdList)
            {
                try { bmp.RemovePropertyItem(propId); } catch { }
            }

            var tiffEncoder = GetTiffEncoder();
            if (tiffEncoder != null)
            {
                using var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Compression, (long)EncoderValue.CompressionNone);
                bmp.Save(targetTifPath, tiffEncoder, encoderParams);
            }
            else
            {
                bmp.Save(targetTifPath, ImageFormat.Tiff);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int EnsurePowerOfTwo(int val)
    {
        if (val <= 64) return 64;
        if (val >= 4096) return 4096;
        if ((val & (val - 1)) == 0) return val;

        int p = 64;
        while (p < val && p < 4096) p <<= 1;
        int prev = p >> 1;
        return (val - prev < p - val) ? prev : p;
    }

    private static ImageCodecInfo? GetTiffEncoder()
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        for (int i = 0; i < codecs.Length; i++)
        {
            if (codecs[i].FormatID == ImageFormat.Tiff.Guid)
                return codecs[i];
        }
        return null;
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
        string matPrefixEntry = $"material_{cleanLevel}\tlevels\\{scenarioType}\\{cleanLevel}\\shaders";
        string matPrefixEntryFallback = $"material_{cleanLevel}\tlevels\\{scenarioType}\\{cleanLevel}";

        var lines = new List<string>();
        bool alreadyPresent = false;
        bool matPrefixPresent = false;

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
                if (trimmed.StartsWith($"material_{cleanLevel}\t", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith($"material_{cleanLevel} ", StringComparison.OrdinalIgnoreCase))
                {
                    matPrefixPresent = true;
                }
            }
        }

        bool updated = false;
        if (!alreadyPresent)
        {
            lines.Add(targetEntry);
            lines.Add(targetEntryFallback);
            updated = true;
        }

        if (!matPrefixPresent)
        {
            lines.Add(matPrefixEntry);
            lines.Add(matPrefixEntryFallback);
            updated = true;
        }

        if (updated)
        {
            File.WriteAllLines(collectionsPath, lines);
        }
    }

    /// <summary>
    /// Synthesizes or patches an authentic Halo 3 .shader (rmsh) tag that references
    /// the compiled .bitmap tag for the material's albedo base map.
    /// </summary>
    /// <summary>
    /// Synthesizes or patches an authentic Halo 3 .shader (rmsh) tag that references
    /// the compiled .bitmap tag for the material's albedo base map.
    /// Patches BOTH the parameter block reference AND the tag dependency table in the tag footer.
    /// </summary>
    public static void GenerateCustomShaderTag(
        string fullShaderTagPath,
        string materialName,
        string relativeBitmapTagPath,
        string? ekPath = null,
        CustomShaderProperties? properties = null)
    {
        var dir = Path.GetDirectoryName(fullShaderTagPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        properties ??= CustomShaderProperties.CreateDefault(materialName);

        string templateTag = Path.Combine(ekPath ?? "", "tags", "levels", "multi", "shrine", "shaders", "forerunner", "panel_wallsection.shader");
        if (!File.Exists(templateTag) && !string.IsNullOrEmpty(ekPath))
        {
            string[] candidateTemplates = new[]
            {
                Path.Combine(ekPath, "tags", "levels", "multi", "shrine", "shaders", "forerunner", "panel_wallsection.shader"),
                Path.Combine(ekPath, "tags", "levels", "multi", "chill", "shaders", "waste_panel_trim.shader"),
                Path.Combine(ekPath, "tags", "levels", "multi", "riverworld", "shaders", "riverworld_rock.shader"),
                Path.Combine(ekPath, "tags", "levels", "multi", "shrine", "shaders", "human", "hev1.shader"),
                Path.Combine(ekPath, "tags", "levels", "multi", "chill", "shaders", "chill_wall01.shader"),
                Path.Combine(ekPath, "tags", "levels", "multi", "chill", "shaders", "chill_metal.shader")
            };
            templateTag = candidateTemplates.FirstOrDefault(File.Exists) ?? "";
        }

        if (string.IsNullOrEmpty(templateTag) || !File.Exists(templateTag))
        {
            // If no template is available (e.g. mock test environment), create a placeholder tag file
            if (!File.Exists(fullShaderTagPath))
            {
                using var stream = File.Create(fullShaderTagPath);
                using var writer = new BinaryWriter(stream, Encoding.ASCII);
                writer.Write(Encoding.ASCII.GetBytes("rmsh"));
                writer.Write(new byte[1020]);
            }
            return;
        }

        byte[] raw = File.ReadAllBytes(templateTag);
        string templateStr = Encoding.ASCII.GetString(raw);

        // Standard environment shader template (_0_1_0_1_2_2_0_0_0_0).
        // panel_wallsection.shader is our clean, authentic, non-strata matte environment template.
        // Base map: "levels\multi\shrine\bitmaps\forerunner\panel_wallsection_dif" (60 characters).
        // Bump map: "levels\multi\shrine\bitmaps\forerunner\panel_wallsection_bump" (61 characters).
        // Detail map: "levels\multi\salvation\bitmaps\weathering_smear_detail" (54 characters).
        string templateSearchTarget = @"levels\multi\shrine\bitmaps\forerunner\panel_wallsection_dif";
        int targetLen = 60;
        string templateBumpTarget = @"levels\multi\shrine\bitmaps\forerunner\panel_wallsection_bump";
        int targetBumpLen = 61;
        string templateDetailTarget = @"levels\multi\salvation\bitmaps\weathering_smear_detail";
        int targetDetailLen = 54;

        if (templateStr.Contains(@"levels\multi\shrine\bitmaps\forerunner\panel_wallsection_dif"))
        {
            templateSearchTarget = @"levels\multi\shrine\bitmaps\forerunner\panel_wallsection_dif";
            targetLen = 60;
            templateBumpTarget = @"levels\multi\shrine\bitmaps\forerunner\panel_wallsection_bump";
            targetBumpLen = 61;
            templateDetailTarget = @"levels\multi\salvation\bitmaps\weathering_smear_detail";
            targetDetailLen = 54;
        }
        else if (templateStr.Contains(@"levels\solo\070_waste\bitmaps\waste_panel_trim"))
        {
            templateSearchTarget = @"levels\solo\070_waste\bitmaps\waste_panel_trim";
            targetLen = 46;
            templateBumpTarget = @"levels\solo\070_waste\bitmaps\waste_panel_trim_bump";
            targetBumpLen = 51;
            templateDetailTarget = "";
            targetDetailLen = 0;
        }
        else if (templateStr.Contains(@"levels\multi\riverworld\bitmaps\riverworld_metal"))
        {
            templateSearchTarget = @"levels\multi\riverworld\bitmaps\riverworld_metal";
            targetLen = 48;
            templateBumpTarget = @"levels\multi\riverworld\bitmaps\riverworld_metal_bump";
            targetBumpLen = 53;
            templateDetailTarget = "";
            targetDetailLen = 0;
        }
        else if (templateStr.Contains(@"levels\multi\riverworld\bitmaps\cliff_rock_base_brown"))
        {
            templateSearchTarget = @"levels\multi\riverworld\bitmaps\cliff_rock_base_brown";
            targetLen = 53;
            templateBumpTarget = @"levels\multi\riverworld\bitmaps\cliff_rock_bump";
            targetBumpLen = 47;
            templateDetailTarget = @"levels\multi\riverworld\bitmaps\cliff_rock_detail";
            targetDetailLen = 49;
        }

        string normalizedBitm = relativeBitmapTagPath.Replace('/', '\\').Trim('\\');
        if (normalizedBitm.EndsWith(".bitmap", StringComparison.OrdinalIgnoreCase))
        {
            normalizedBitm = normalizedBitm[..^7];
        }

        string paddedBitmPath = PadPathToLength(normalizedBitm, targetLen);

        string bumpRelPath = normalizedBitm + "_bump";
        bool hasBumpBitmap = !string.IsNullOrEmpty(ekPath) &&
                             (File.Exists(Path.Combine(ekPath, "tags", bumpRelPath + ".bitmap")) ||
                              File.Exists(Path.Combine(ekPath, "data", bumpRelPath + ".tif")));

        byte[] searchBytes = Encoding.ASCII.GetBytes(templateSearchTarget);
        byte[] replaceBytes = Encoding.ASCII.GetBytes(paddedBitmPath);

        byte[] bumpSuffix = Encoding.ASCII.GetBytes("_bump");

        // Replace base albedo map references (guarding against bump map if bump shares base prefix)
        for (int i = 0; i <= raw.Length - searchBytes.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < searchBytes.Length; j++)
            {
                if (raw[i + j] != searchBytes[j]) { match = false; break; }
            }

            if (match)
            {
                bool isBump = (i + searchBytes.Length + bumpSuffix.Length <= raw.Length);
                if (isBump)
                {
                    for (int b = 0; b < bumpSuffix.Length; b++)
                    {
                        if (raw[i + searchBytes.Length + b] != bumpSuffix[b])
                        {
                            isBump = false;
                            break;
                        }
                    }
                }

                if (!isBump)
                {
                    Array.Copy(replaceBytes, 0, raw, i, replaceBytes.Length);
                }
            }
        }

        // Neutralize detail_map (eliminates horizontal strata / high-frequency repeating detail)
        if (!string.IsNullOrEmpty(templateDetailTarget) && targetDetailLen > 0)
        {
            string neutralDetail = PadPathToLength(@"shaders\default_bitmaps\bitmaps\gray_50_percent", targetDetailLen);
            byte[] detailSearchBytes = Encoding.ASCII.GetBytes(templateDetailTarget);
            byte[] neutralDetailBytes = Encoding.ASCII.GetBytes(neutralDetail);

            for (int i = 0; i <= raw.Length - detailSearchBytes.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < detailSearchBytes.Length; j++)
                {
                    if (raw[i + j] != detailSearchBytes[j]) { match = false; break; }
                }
                if (match)
                {
                    Array.Copy(neutralDetailBytes, 0, raw, i, neutralDetailBytes.Length);
                }
            }
        }

        // Configure bump map (if custom bump exists AND is enabled, pad to length; otherwise neutralize to gray_50_percent)
        if (!string.IsNullOrEmpty(templateBumpTarget) && targetBumpLen > 0)
        {
            bool shouldReplaceBump = (hasBumpBitmap && properties.EnableBumpMap) || !templateStr.Contains(@"levels\multi\riverworld\bitmaps\riverworld_metal");
            if (shouldReplaceBump)
            {
                string neutralBump = PadPathToLength(@"shaders\default_bitmaps\bitmaps\gray_50_percent", targetBumpLen);
                string primaryBumpPath = (hasBumpBitmap && properties.EnableBumpMap)
                    ? PadPathToLength(bumpRelPath, targetBumpLen)
                    : neutralBump;
                byte[] bumpSearchBytes = Encoding.ASCII.GetBytes(templateBumpTarget);
                byte[] bumpReplaceBytes = Encoding.ASCII.GetBytes(primaryBumpPath);

                for (int i = 0; i <= raw.Length - bumpSearchBytes.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < bumpSearchBytes.Length; j++)
                    {
                        if (raw[i + j] != bumpSearchBytes[j]) { match = false; break; }
                    }

                    if (match)
                    {
                        Array.Copy(bumpReplaceBytes, 0, raw, i, bumpReplaceBytes.Length);
                    }
                }
            }
        }

        // Patch physical shader properties (roughness, metalness, reflections)
        PatchShaderFloatParam(raw, "specular_coefficient", properties.Metalness, properties.Metalness * 0.3f);
        PatchShaderFloatParam(raw, "roughness", properties.Roughness, properties.Roughness);
        PatchShaderFloatParam(raw, "environment_map_specular_contribution", properties.EnvironmentReflection, properties.EnvironmentReflection);
        PatchShaderFloatParam(raw, "area_specular_contribution", properties.Metalness * 0.15f, properties.Metalness * 0.15f);
        PatchShaderFloatParam(raw, "analytical_specular_contribution", properties.Metalness * 0.4f, properties.Metalness * 0.5f);

        File.WriteAllBytes(fullShaderTagPath, raw);
    }

    private static void PatchShaderFloatParam(byte[] raw, string paramName, float val1, float val2)
    {
        string needle = paramName + "frgt";
        byte[] needleBytes = Encoding.ASCII.GetBytes(needle);
        byte[] adgt = Encoding.ASCII.GetBytes("adgt");

        int idx = -1;
        for (int i = 0; i <= raw.Length - needleBytes.Length; i++)
        {
            bool m = true;
            for (int j = 0; j < needleBytes.Length; j++)
            {
                if (raw[i + j] != needleBytes[j]) { m = false; break; }
            }
            if (m) { idx = i; break; }
        }

        if (idx >= 0)
        {
            int adgtIdx = -1;
            int limit = Math.Min(raw.Length - 4, idx + 350);
            for (int i = idx; i <= limit; i++)
            {
                if (raw[i] == adgt[0] && raw[i + 1] == adgt[1] && raw[i + 2] == adgt[2] && raw[i + 3] == adgt[3])
                {
                    adgtIdx = i;
                    break;
                }
            }

            if (adgtIdx >= 0 && adgtIdx + 24 <= raw.Length)
            {
                byte[] b1 = BitConverter.GetBytes(val1);
                byte[] b2 = BitConverter.GetBytes(val2);
                Array.Copy(b1, 0, raw, adgtIdx + 16, 4);
                Array.Copy(b2, 0, raw, adgtIdx + 20, 4);
            }
        }
    }

    private static string PadPathToLength(string path, int targetLen)
    {
        if (path.Length == targetLen) return path;

        if (path.Length > targetLen)
        {
            int lastSlash = path.LastIndexOf('\\');
            if (lastSlash > 0 && lastSlash < targetLen)
            {
                string dirPart = path.Substring(0, lastSlash + 1);
                int maxNameLen = targetLen - dirPart.Length;
                string namePart = path.Substring(lastSlash + 1);
                if (namePart.Length > maxNameLen) namePart = namePart.Substring(0, maxNameLen);
                return dirPart + namePart;
            }
            return path.Substring(0, targetLen);
        }

        int slash = path.LastIndexOf('\\');
        int pad = targetLen - path.Length;
        if (slash >= 0)
        {
            return path.Substring(0, slash) + new string('\\', pad + 1) + path.Substring(slash + 1);
        }
        return path + new string('\\', pad);
    }

    /// <summary>
    /// Executes tool_fast.exe or tool.exe to compile TIFF files in data\ into .bitmap tags in tags\.
    /// </summary>
    public static bool CompileBitmapsWithTool(string ekPath, string levelName, string scenarioType = "multi")
    {
        if (string.IsNullOrWhiteSpace(ekPath) || !Directory.Exists(ekPath)) return false;

        string toolExe = Path.Combine(ekPath, "tool_fast.exe");
        if (!File.Exists(toolExe)) toolExe = Path.Combine(ekPath, "tool.exe");
        if (!File.Exists(toolExe)) return false;

        string cleanLevel = SanitizeIdentifier(levelName);
        string relBitmDir = Path.Combine("levels", scenarioType, cleanLevel, "bitmaps");

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = toolExe,
                Arguments = $"bitmaps \"{relBitmDir}\"",
                WorkingDirectory = ekPath,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(45000);
                return proc.ExitCode == 0;
            }
        }
        catch
        {
            // Non-critical if running in test environment
        }

        return false;
    }

    public static List<CustomShaderResult> SetupCustomShadersForModel(
        string ekPath,
        string levelName,
        IEnumerable<MeshObject> meshes,
        string? sourceAssetDir,
        HaloShaderCatalog? catalog = null,
        Dictionary<string, CustomShaderProperties>? shaderPropertiesMap = null)
        => SetupCustomShadersForModel(ekPath, levelName, "multi", meshes, sourceAssetDir, catalog, shaderPropertiesMap);

    /// <summary>
    /// Processes imported geometry and textures:
    /// 1. Cleans and canonicalizes material identifiers (e.g. Material_icefieldsicefields_ground -> ground)
    /// 2. Converts source PNGs to 32-bit TIFFs in data\levels\<scenarioType>\<levelName>\bitmaps
    /// 3. Runs tool_fast.exe bitmaps to compile authentic .bitmap tags in tags\
    /// 4. Generates .shader tags referencing compiled bitmaps in tags\levels\<scenarioType>\<levelName>\shaders
    /// 5. Remaps mesh MaterialName on each mesh to "{cleanLevel} {baseMatName}" for seamless ASS export and compilation
    /// 6. Registers prefixes in tags\levels\shader_collections.txt
    /// 7. Adds entries into HaloShaderCatalog for UI immediate display
    /// </summary>
    public static List<CustomShaderResult> SetupCustomShadersForModel(
        string ekPath,
        string levelName,
        string scenarioType,
        IEnumerable<MeshObject> meshes,
        string? sourceAssetDir,
        HaloShaderCatalog? catalog = null,
        Dictionary<string, CustomShaderProperties>? shaderPropertiesMap = null)
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

        var meshList = meshes.ToList();

        // Search directory for matching textures
        var candidateFiles = new List<string>();
        var searchDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(sourceAssetDir) && Directory.Exists(sourceAssetDir))
            searchDirs.Add(sourceAssetDir);

        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (Directory.Exists(desktop))
            {
                string coagTex = Path.Combine(desktop, "coagulation", "Textures");
                if (Directory.Exists(coagTex)) searchDirs.Add(coagTex);
                string coagRoot = Path.Combine(desktop, "coagulation");
                if (Directory.Exists(coagRoot)) searchDirs.Add(coagRoot);
                string levelTex = Path.Combine(desktop, levelName, "Textures");
                if (Directory.Exists(levelTex)) searchDirs.Add(levelTex);
                string levelRoot = Path.Combine(desktop, levelName);
                if (Directory.Exists(levelRoot)) searchDirs.Add(levelRoot);
            }
        }
        catch { }

        foreach (var dir in searchDirs)
        {
            try
            {
                candidateFiles.AddRange(Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories));
            }
            catch { }
        }

        // Distinct raw materials
        var rawMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in meshList)
        {
            if (!string.IsNullOrWhiteSpace(m.MaterialName)) rawMaterials.Add(m.MaterialName);
            foreach (var slot in m.MaterialSlots)
            {
                if (!string.IsNullOrWhiteSpace(slot)) rawMaterials.Add(slot);
            }
        }

        // Map each raw material to a clean canonical name and source texture
        var rawToCleanMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var cleanToTexMap = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawMat in rawMaterials)
        {
            // Skip engine special materials (+sky, +portal, +seamsealer, etc.)
            if (rawMat.StartsWith("+") || rawMat.StartsWith("%+") || rawMat.Equals("sky", StringComparison.OrdinalIgnoreCase) || rawMat.EndsWith(" sky", StringComparison.OrdinalIgnoreCase) || rawMat.EndsWith(" sky%", StringComparison.OrdinalIgnoreCase))
                continue;

            string cleanId = CleanMaterialIdentifier(rawMat, cleanLevel);
            rawToCleanMap[rawMat] = cleanId;

            if (!cleanToTexMap.ContainsKey(cleanId))
            {
                // Find matching texture file:
                // 1. Check if any mesh has an explicit MaterialTextureMap for this material
                string? matchedTex = null;
                foreach (var m in meshList)
                {
                    if (m.MaterialTextureMap != null && m.MaterialTextureMap.Count > 0)
                    {
                        if (m.MaterialTextureMap.TryGetValue(rawMat, out var tex) && File.Exists(tex))
                        {
                            matchedTex = tex;
                            break;
                        }
                        foreach (var (slotKey, texVal) in m.MaterialTextureMap)
                        {
                            if (!string.IsNullOrEmpty(texVal) && File.Exists(texVal))
                            {
                                string cleanSlot = CleanMaterialIdentifier(slotKey, cleanLevel);
                                if (cleanSlot.Equals(cleanId, StringComparison.OrdinalIgnoreCase))
                                {
                                    matchedTex = texVal;
                                    break;
                                }
                            }
                        }
                        if (!string.IsNullOrEmpty(matchedTex)) break;
                    }
                }

                // 2. Check if any mesh with this material has an explicit SourceTexturePath
                if (string.IsNullOrEmpty(matchedTex))
                {
                    matchedTex = meshList
                        .Where(m => (m.MaterialName == rawMat || m.MaterialSlots.Contains(rawMat)) &&
                                    !string.IsNullOrEmpty(m.SourceTexturePath) &&
                                    File.Exists(m.SourceTexturePath))
                        .Select(m => m.SourceTexturePath)
                        .FirstOrDefault();
                }

                // 3. Direct candidate matching from asset directory (prioritize diffuse over alpha/bump/masks)
                if (string.IsNullOrEmpty(matchedTex) && candidateFiles.Count > 0)
                {
                    var diffuseCandidates = candidateFiles.Where(f =>
                    {
                        string fn = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        return !fn.Contains("alpha") && !fn.Contains("bump") && !fn.EndsWith(" cc") &&
                               !fn.Contains("mask") && !fn.Contains("cloud");
                    }).ToList();

                    // Try exact name match
                    matchedTex = diffuseCandidates.FirstOrDefault(f =>
                    {
                        string fn = Path.GetFileNameWithoutExtension(f).ToLowerInvariant().Replace(" ", "_");
                        return fn == cleanId || fn == $"coag_{cleanId}" || fn == $"ch_{cleanId}" || fn == $"blood_{cleanId}";
                    });

                    // Try contains
                    if (string.IsNullOrEmpty(matchedTex))
                    {
                        matchedTex = diffuseCandidates.FirstOrDefault(f =>
                        {
                            string fn = Path.GetFileNameWithoutExtension(f).ToLowerInvariant().Replace(" ", "_");
                            return fn.Contains(cleanId) || cleanId.Contains(fn) ||
                                   (cleanId == "ch_flat_graytech" && fn.Contains("graytech")) ||
                                   (cleanId == "light" && fn.Contains("light_bands")) ||
                                   (cleanId == "lights" && fn.Contains("lights")) ||
                                   (cleanId == "arrow" && fn.Contains("arrow"));
                        });
                    }

                    // Fallback to any candidate if still not found
                    if (string.IsNullOrEmpty(matchedTex))
                    {
                        matchedTex = candidateFiles.FirstOrDefault(f =>
                        {
                            string fn = Path.GetFileNameWithoutExtension(f).ToLowerInvariant().Replace(" ", "_");
                            return fn.Contains(cleanId);
                        });
                    }
                }

                // 4. Check data\levels\...\bitmaps directory for pre-existing source texture
                if (string.IsNullOrEmpty(matchedTex))
                {
                    foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" })
                    {
                        string p = Path.Combine(dataBitmapsDir, cleanId + ext);
                        if (File.Exists(p))
                        {
                            matchedTex = p;
                            break;
                        }
                    }
                }

                cleanToTexMap[cleanId] = matchedTex;
            }
        }

        // Convert textures to TIFFs
        bool anyTifCreated = false;
        foreach (var (cleanId, sourceTex) in cleanToTexMap)
        {
            string targetTif = Path.Combine(dataBitmapsDir, $"{cleanId}.tif");

            if (sourceTex != null && File.Exists(sourceTex))
            {
                if (!string.Equals(sourceTex, targetTif, StringComparison.OrdinalIgnoreCase))
                {
                    ConvertImageToTif(sourceTex, targetTif, upscaleToHalo3Standards: true);
                }
                anyTifCreated = true;

                // Also check for matching bump map
                string? bumpSource = null;
                string sourceDir = Path.GetDirectoryName(sourceTex) ?? "";
                string sourceBaseName = Path.GetFileNameWithoutExtension(sourceTex);
                var bumpCandidates = new[]
                {
                    Path.Combine(sourceDir, $"{sourceBaseName}_bump.png"),
                    Path.Combine(sourceDir, $"{sourceBaseName}_bump.PNG"),
                    Path.Combine(sourceDir, $"{sourceBaseName} bump.png"),
                    Path.Combine(sourceDir, $"{sourceBaseName} bump.PNG"),
                    Path.Combine(sourceDir, $"{cleanId}_bump.png"),
                    Path.Combine(sourceDir, $"{cleanId}_bump.PNG"),
                    Path.Combine(sourceDir, $"{cleanId} bump.png"),
                    Path.Combine(sourceDir, $"{cleanId} bump.PNG")
                };
                foreach (var bc in bumpCandidates)
                {
                    if (File.Exists(bc)) { bumpSource = bc; break; }
                }

                // Specific Coagulation known bump map names
                if (string.IsNullOrEmpty(bumpSource))
                {
                    if (cleanId.Equals("ground", StringComparison.OrdinalIgnoreCase))
                    {
                        string p = Path.Combine(sourceDir, "coag_landslide_bump.PNG");
                        if (File.Exists(p)) bumpSource = p;
                    }
                    else if (cleanId.Equals("coag_panel_i", StringComparison.OrdinalIgnoreCase))
                    {
                        string p = Path.Combine(sourceDir, "panel_i_bump.PNG");
                        if (File.Exists(p)) bumpSource = p;
                    }
                    else if (cleanId.Equals("panels", StringComparison.OrdinalIgnoreCase))
                    {
                        string p = Path.Combine(sourceDir, "ch_panel_thin_strips_bump.PNG");
                        if (File.Exists(p)) bumpSource = p;
                    }
                }

                if (!string.IsNullOrEmpty(bumpSource) && File.Exists(bumpSource))
                {
                    string targetBumpTif = Path.Combine(dataBitmapsDir, $"{cleanId}_bump.tif");
                    ConvertImageToTif(bumpSource, targetBumpTif, upscaleToHalo3Standards: true);
                }
            }
            else if (!File.Exists(targetTif))
            {
                CreateDefaultTexture(targetTif, cleanId);
                anyTifCreated = true;
            }
            else
            {
                // Existing .tif in directory
                anyTifCreated = true;
            }
        }

        // Run tool_fast bitmaps to compile .bitmap tags in H3EK
        if (anyTifCreated && !string.IsNullOrEmpty(ekPath) && Directory.Exists(ekPath))
        {
            CompileBitmapsWithTool(ekPath, cleanLevel, scenarioType);
        }

        // Generate .shader tags and register with catalog
        foreach (var (cleanId, sourceTex) in cleanToTexMap)
        {
            string targetTif = Path.Combine(dataBitmapsDir, $"{cleanId}.tif");
            string relBitm = Path.Combine("levels", scenarioType, cleanLevel, "bitmaps", cleanId);
            string shaderTag = Path.Combine(tagsShadersDir, $"{cleanId}.shader");
            string relShader = Path.Combine("levels", scenarioType, cleanLevel, "shaders", $"{cleanId}.shader");

            CustomShaderProperties? props = null;
            if (shaderPropertiesMap != null && shaderPropertiesMap.TryGetValue(cleanId, out var customP))
            {
                props = customP;
            }

            GenerateCustomShaderTag(shaderTag, cleanId, relBitm, ekPath, props);

            string fullMatName = $"{cleanLevel} {cleanId}";
            if (catalog != null)
            {
                catalog.RegisterCustomShader(fullMatName, relShader, cleanLevel, "Custom Level Shaders");
                catalog.RegisterCustomShader(cleanId, relShader, cleanLevel, "Custom Level Shaders");
            }

            results.Add(new CustomShaderResult(fullMatName, relShader, relBitm, shaderTag, targetTif));
        }

        // Reassign clean material names to meshes for seamless ASS export and compilation
        foreach (var m in meshList)
        {
            if (m.MaterialName.StartsWith("+sky", StringComparison.OrdinalIgnoreCase) || m.Name.StartsWith("+sky", StringComparison.OrdinalIgnoreCase) || m.MaterialName.Equals("sky", StringComparison.OrdinalIgnoreCase))
            {
                m.MaterialName = "+sky";
                if (!m.MaterialSlots.Contains("+sky"))
                {
                    m.MaterialSlots.Clear();
                    m.MaterialSlots.Add("+sky");
                }
                m.IsCollideable = true;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(m.MaterialName) && rawToCleanMap.TryGetValue(m.MaterialName, out var cleanId))
            {
                string orig = m.MaterialName;
                string leading = orig.StartsWith("+") ? "+" : "";
                string trailing = "";
                if (orig.Contains("%")) trailing += "%";
                if (orig.Contains("!")) trailing += "!";
                if (orig.Contains("'")) trailing += "'";
                m.MaterialName = $"{leading}{cleanLevel} {cleanId}{trailing}";
            }

            for (int s = 0; s < m.MaterialSlots.Count; s++)
            {
                if (m.MaterialSlots[s].StartsWith("+sky", StringComparison.OrdinalIgnoreCase) || m.MaterialSlots[s].Equals("sky", StringComparison.OrdinalIgnoreCase))
                {
                    m.MaterialSlots[s] = "+sky";
                    continue;
                }

                if (rawToCleanMap.TryGetValue(m.MaterialSlots[s], out var cleanSlotId))
                {
                    string orig = m.MaterialSlots[s];
                    string leading = orig.StartsWith("+") ? "+" : "";
                    string trailing = "";
                    if (orig.Contains("%")) trailing += "%";
                    if (orig.Contains("!")) trailing += "!";
                    if (orig.Contains("'")) trailing += "'";
                    m.MaterialSlots[s] = $"{leading}{cleanLevel} {cleanSlotId}{trailing}";
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Reduces raw, verbose, or prefixed material identifiers to concise canonical identifiers
    /// (e.g. Material_icefieldsicefields_ground -> ground, Material_icefieldsb40_bridgeramp -> ramp)
    /// </summary>
    public static string CleanMaterialIdentifier(string rawName, string levelName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return "ground";
        if (rawName.StartsWith("+sky", StringComparison.OrdinalIgnoreCase) || rawName.Equals("sky", StringComparison.OrdinalIgnoreCase))
            return "+sky";
        string s = rawName.Trim().ToLowerInvariant();

        // Strip leading special characters %, !, +, $, @, ~
        while (s.Length > 0 && (s[0] == '%' || s[0] == '!' || s[0] == '+' || s[0] == '$' || s[0] == '@' || s[0] == '~'))
        {
            s = s[1..];
        }

        // Strip trailing special characters
        while (s.Length > 0 && (s[^1] == '%' || s[^1] == '!' || s[^1] == '+' || s[^1] == '$' || s[^1] == '@' || s[^1] == '~'))
        {
            s = s[..^1];
        }

        // Strip 'material_' or 'mat_' prefix
        if (s.StartsWith("material_", StringComparison.OrdinalIgnoreCase)) s = s[9..];
        else if (s.StartsWith("mat_", StringComparison.OrdinalIgnoreCase)) s = s[4..];

        // Strip level prefix (e.g. 'icefields' or 'icefields_')
        string lvl = levelName.Trim().ToLowerInvariant();
        if (s.StartsWith(lvl + lvl + "_", StringComparison.OrdinalIgnoreCase))
            s = s[(lvl.Length * 2 + 1)..];
        else if (s.StartsWith(lvl + lvl, StringComparison.OrdinalIgnoreCase))
            s = s[(lvl.Length * 2)..];
        else if (s.StartsWith(lvl + "_", StringComparison.OrdinalIgnoreCase))
            s = s[(lvl.Length + 1)..];
        else if (s.StartsWith(lvl + " ", StringComparison.OrdinalIgnoreCase))
            s = s[(lvl.Length + 1)..];
        else if (s.StartsWith(lvl, StringComparison.OrdinalIgnoreCase) && s.Length > lvl.Length && (s[lvl.Length] == '_' || char.IsLetter(s[lvl.Length])))
            s = s[lvl.Length..].TrimStart('_');

        // Shorten specific noisy strings
        s = s.Replace("b40_", "")
             .Replace("beavercreek_", "")
             .Replace("sidewinder_", "")
             .Replace("generic_", "");

        s = Regex.Replace(s, @"[^a-z0-9_]+", "_").Trim('_');
        if (string.IsNullOrEmpty(s)) s = "ground";

        // Map to concise canonical names to guarantee <= 16 chars
        if (s.Contains("ground_ice") || s.Contains("ice_caves")) s = "ice";
        else if (s.Contains("ground")) s = "ground";
        else if (s.Contains("cliff")) s = "cliffs";
        else if (s.Contains("roof")) s = "roof";
        else if (s.Contains("bridge") || s.Contains("ramp")) s = "ramp";
        else if (s.Contains("plate_floor")) s = "floor";
        else if (s.Contains("panels")) s = "panels";
        else if (s.Contains("flat_generic") || s.Contains("metal")) s = "metal";
        else if (s.Contains("ladder")) s = "ladder";
        else if (s.Contains("lights_blue")) s = "light_blue";
        else if (s.Contains("lights_red")) s = "light_red";
        else if (s.Contains("stripes_blue") || s.Contains("stripes_red") || s.Contains("stripes")) s = "stripes";
        else if (s.Contains("symbol_blue")) s = "symbol_blue";
        else if (s.Contains("symbol_red")) s = "symbol_red";
        else if (s.Contains("lights")) s = "lights"; // Explicitly preserve plural 'lights' to match ASS shader requests
        else if (s.Contains("greenlight") || s.Contains("light")) s = "light";
        else if (s.Contains("arrow")) s = "arrow";
        else if (s.Contains("teleporter") || s.Contains("shield")) s = "shield";

        return s;
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
            SaveCleanTiff(bmp, targetTifPath);
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
