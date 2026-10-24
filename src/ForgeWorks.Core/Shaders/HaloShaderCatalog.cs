using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ForgeWorks.Core.Shaders;

public sealed record HaloShaderEntry(
    string MaterialName,
    string TagPath,
    string ShaderType,
    string LevelPrefix,
    string Category = "General",
    bool IsVerified = false,
    string? TexturePath = null,
    bool IsTerrainShader = false
);

public sealed class HaloShaderCatalog
{
    private readonly Dictionary<string, HaloShaderEntry> _byMaterialName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<HaloShaderEntry>> _byLevelPrefix = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<HaloShaderEntry>> _byCategory = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<HaloShaderEntry> _allShaders = new();
    private readonly List<HaloShaderEntry> _terrainShaders = new();

    public IReadOnlyList<HaloShaderEntry> AllShaders => _allShaders;
    public IReadOnlyList<HaloShaderEntry> TerrainShaders => _terrainShaders;
    public IReadOnlyDictionary<string, List<HaloShaderEntry>> ShadersByCategory => _byCategory;
    public int Count => _allShaders.Count;

    public static readonly string[] Categories = new[]
    {
        "All",
        "Floors",
        "Walls",
        "Panels",
        "Lights",
        "Snow (Snowbound)",
        "Grass (Riverworld)",
        "Desert (Shrine)",
        "Terrain",
        "Rock & Cliff",
        "Metal & Structure",
        "Covenant & Forerunner",
        "Foliage & Nature",
        "Water & Glass",
        "Special (+Sky)"
    };

    public static HaloShaderCatalog LoadFromFiles(string blenderShadersFile, string? rawTagPathsFile = null, string? ekPath = null)
    {
        var catalog = new HaloShaderCatalog();

        if (File.Exists(blenderShadersFile))
        {
            catalog.ParseBlenderShadersFile(blenderShadersFile);
        }

        if (!string.IsNullOrEmpty(rawTagPathsFile) && File.Exists(rawTagPathsFile))
        {
            catalog.ParseRawTagPathsFile(rawTagPathsFile);
        }

        if (!string.IsNullOrEmpty(ekPath) && Directory.Exists(ekPath))
        {
            catalog.LoadFromEditingKit(ekPath);
        }

        return catalog;
    }

    /// <summary>
    /// Scans H3EK tags and data directories to load all authentic Halo 3 shaders and textures.
    /// Indexes terrain folders from every map (multiplayer, solo, dlc) and filters out
    /// non-environment shaders (holograms, cortana, vfx) that do not render on terrain.
    /// </summary>
    public void LoadFromEditingKit(string ekPath)
    {
        if (string.IsNullOrWhiteSpace(ekPath) || !Directory.Exists(ekPath)) return;

        string tagsDir = Path.Combine(ekPath, "tags");
        string dataDir = Path.Combine(ekPath, "data");
        if (!Directory.Exists(tagsDir)) return;

        // 1. Parse shader_collections.txt to map prefixes <-> directories
        var prefixToDir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dirToPrefix = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string collectionsPath = Path.Combine(tagsDir, "levels", "shader_collections.txt");
        if (File.Exists(collectionsPath))
        {
            foreach (var rawLine in File.ReadAllLines(collectionsPath))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(";")) continue;

                var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string prefix = parts[0].Trim();
                    string relDir = parts[1].Trim().TrimEnd('\\', '/').ToLowerInvariant();
                    prefixToDir[prefix] = relDir;
                    dirToPrefix.TryAdd(relDir, prefix);
                    // Also map directory with trailing /shaders or without
                    if (relDir.EndsWith("shaders"))
                    {
                        string parent = Path.GetDirectoryName(relDir)?.ToLowerInvariant() ?? "";
                        if (!string.IsNullOrEmpty(parent)) dirToPrefix.TryAdd(parent, prefix);
                    }
                    else
                    {
                        dirToPrefix.TryAdd($"{relDir}\\shaders", prefix);
                    }
                }
            }
        }

        // 2. Scan tags\levels (and tags\scenarios\shaders) for all valid shaders
        var searchRoots = new List<string>();
        string levelsTags = Path.Combine(tagsDir, "levels");
        if (Directory.Exists(levelsTags)) searchRoots.Add(levelsTags);
        string scenariosTags = Path.Combine(tagsDir, "scenarios", "shaders");
        if (Directory.Exists(scenariosTags)) searchRoots.Add(scenariosTags);

        var shaderExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".shader",
            ".shader_terrain",
            ".shader_foliage",
            ".shader_water",
            ".shader_custom"
        };

        foreach (var root in searchRoots)
        {
            try
            {
                var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    string ext = Path.GetExtension(file);
                    if (!shaderExtensions.Contains(ext)) continue;

                    string relTagPath = Path.GetRelativePath(tagsDir, file);
                    string lowerPath = relTagPath.ToLowerInvariant();

                    // Filter out shaders that do not render as environment geometry (holograms, VFX, UI)
                    if (!IsEnvironmentRenderable(lowerPath, ext))
                        continue;

                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);
                    string relDir = Path.GetDirectoryName(relTagPath)?.ToLowerInvariant() ?? "";

                    // Check if shader is in a terrain folder or has terrain shader type
                    bool isTerrainFolder = lowerPath.Contains(@"\terrain\") || lowerPath.Contains(@"/terrain/") ||
                                           lowerPath.Contains(@"\nature\") || lowerPath.Contains(@"/nature/") ||
                                           relDir.EndsWith("terrain") || ext.Equals(".shader_terrain", StringComparison.OrdinalIgnoreCase);

                    // Determine prefix from shader_collections.txt or directory path
                    string prefix = "";
                    if (dirToPrefix.TryGetValue(relDir, out var mappedPrefix))
                    {
                        prefix = mappedPrefix;
                    }
                    else
                    {
                        // Check if part of levels\multi\<level> or levels\dlc\<level> or levels\solo\<level>
                        var parts = relDir.Split(new[] { '\\', '/' });
                        int levelsIdx = Array.FindIndex(parts, p => p.Equals("levels", StringComparison.OrdinalIgnoreCase));
                        if (levelsIdx >= 0 && levelsIdx + 2 < parts.Length)
                        {
                            string mapFolder = parts[levelsIdx + 2];
                            if (LevelPrefixAliases.TryGetValue(mapFolder, out var alias))
                            {
                                prefix = alias;
                            }
                            else
                            {
                                prefix = mapFolder;
                            }
                        }
                        else if (parts.Length > 0)
                        {
                            prefix = parts[^1];
                        }
                    }

                    string matName = !string.IsNullOrEmpty(prefix)
                        ? $"{prefix} {fileNameWithoutExt}"
                        : fileNameWithoutExt;

                    // Check if corresponding texture exists in data\
                    string? matchingTexture = null;
                    if (Directory.Exists(dataDir))
                    {
                        string candidateTif = Path.Combine(dataDir, relDir.Replace("shaders", "bitmaps"), $"{fileNameWithoutExt}.tif");
                        if (File.Exists(candidateTif))
                        {
                            matchingTexture = candidateTif;
                        }
                        else
                        {
                            // Check direct bitmaps folder
                            string directTif = Path.Combine(dataDir, relDir, $"{fileNameWithoutExt}.tif");
                            if (File.Exists(directTif)) matchingTexture = directTif;
                        }
                    }

                    string category = isTerrainFolder ? "Terrain" : DetermineCategory(matName, relTagPath, ext);
                    bool isVerified = true; // Actual H3EK tag files on disk are verified working retail shaders!

                    var entry = new HaloShaderEntry(
                        MaterialName: matName,
                        TagPath: relTagPath,
                        ShaderType: ext,
                        LevelPrefix: prefix,
                        Category: category,
                        IsVerified: isVerified,
                        TexturePath: matchingTexture,
                        IsTerrainShader: isTerrainFolder
                    );

                    AddEntry(entry);

                    // Also add alias without prefix if not already present
                    if (!string.IsNullOrEmpty(prefix) && !_byMaterialName.ContainsKey(fileNameWithoutExt))
                    {
                        _byMaterialName[fileNameWithoutExt] = entry;
                    }
                }
            }
            catch
            {
                // Directory enumeration fallback
            }
        }
    }

    /// <summary>
    /// Validates whether a shader tag renders as opaque/solid environment geometry in-game.
    /// Excludes holograms, Cortana VFX, UI screens, particles, null/invisibles, and decals.
    /// </summary>
    public static bool IsEnvironmentRenderable(string lowerTagPath, string extension)
    {
        // 1. Reject non-environment shader extensions
        if (extension.Equals(".shader_halogram", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".shader_cortana", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".shader_decal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 2. Reject VFX, UI, cinematic, and particles folders
        if (lowerTagPath.Contains(@"\fx\") || lowerTagPath.Contains(@"/fx/") ||
            lowerTagPath.Contains(@"\effects\") || lowerTagPath.Contains(@"/effects/") ||
            lowerTagPath.Contains(@"\particles\") || lowerTagPath.Contains(@"/particles/") ||
            lowerTagPath.Contains(@"\ui\") || lowerTagPath.Contains(@"/ui/") ||
            lowerTagPath.Contains(@"\cinematics\") || lowerTagPath.Contains(@"/cinematics/") ||
            lowerTagPath.Contains(@"\null_object\") || lowerTagPath.Contains(@"/null_object/"))
        {
            return false;
        }

        // 3. Reject invisible, clip, and utility shaders
        string fn = Path.GetFileNameWithoutExtension(lowerTagPath);
        if (fn.Equals("invisible", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("player_clip", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("bullet_clip", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("player_physics", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("seamsealer", StringComparison.OrdinalIgnoreCase) ||
            fn.Equals("invalid", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public void ParseBlenderShadersFile(string filePath)
    {
        using var reader = new StreamReader(filePath);
        string? line;
        string currentLevel = "";

        // Regex for table line: "material_name | tag_path | type"
        var tableRegex = new Regex(@"^\s*([A-Za-z0-9_'\+\%\!\~\*\s]+?)\s*\|\s*([A-Za-z0-9_\\/\.]+?)\s*\|\s*([A-Za-z0-9_\.]+)", RegexOptions.Compiled);
        var levelRegex = new Regex(@"LEVEL\s*/\s*FOLDER:\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        while ((line = reader.ReadLine()) != null)
        {
            var levelMatch = levelRegex.Match(line);
            if (levelMatch.Success)
            {
                currentLevel = levelMatch.Groups[1].Value.ToLowerInvariant();
                continue;
            }

            var match = tableRegex.Match(line);
            if (match.Success)
            {
                string matName = match.Groups[1].Value.Trim();
                string tagPath = match.Groups[2].Value.Trim();
                string type = match.Groups[3].Value.Trim();

                if (!IsEnvironmentRenderable(tagPath.ToLowerInvariant(), type))
                    continue;

                // Extract prefix from material name (e.g. "boxcanvas blue_panel_light" -> "boxcanvas")
                int spaceIdx = matName.IndexOf(' ');
                string prefix = spaceIdx > 0 ? matName[..spaceIdx] : currentLevel;

                AddEntry(new HaloShaderEntry(matName, tagPath, type, prefix));
            }
        }
    }

    public void ParseRawTagPathsFile(string filePath)
    {
        using var reader = new StreamReader(filePath);
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || !trimmed.Contains("shaders"))
                continue;

            string ext = Path.GetExtension(trimmed);
            if (!IsEnvironmentRenderable(trimmed.ToLowerInvariant(), ext))
                continue;

            // e.g. "levels\dlc\armory\shaders\armory_cliff"
            string fileName = Path.GetFileName(trimmed);
            string matName = fileName;

            // Try to deduce level prefix if in levels\multi\<level> or levels\<category>\<level>
            string prefix = "";
            var parts = trimmed.Split(new[] { '\\', '/' });
            int shaderIdx = Array.FindIndex(parts, p => p.Equals("shaders", StringComparison.OrdinalIgnoreCase));
            if (shaderIdx > 0)
            {
                prefix = parts[shaderIdx - 1];
                if (LevelPrefixAliases.TryGetValue(prefix, out var alias))
                {
                    prefix = alias;
                }
                matName = $"{prefix} {fileName}";
            }

            if (!_byMaterialName.ContainsKey(matName))
            {
                AddEntry(new HaloShaderEntry(matName, trimmed, ".shader", prefix));
            }
        }
    }

    private static readonly Dictionary<string, string> LevelPrefixAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["riverworld"] = "river",
        ["deadlock"] = "dead",
        ["salvation"] = "sal",
        ["zanzibar"] = "zan",
        ["pump_haus"] = "ph",
        ["snowbound"] = "sb",
        ["isolation"] = "iso",
        ["construct"] = "con",
        ["cyberdyne"] = "cyber",
        ["shared"] = "mpshare",
        ["boxcanvas"] = "boxcanvas",
        ["chill"] = "chill",
        ["shrine"] = "shr",
        ["sandtrap"] = "shr",
        ["shr"] = "shr",
        ["guardian"] = "guardian",
        ["highground"] = "highground",
        ["bunkerworld"] = "bunkerworld",
        ["icefields"] = "icefields",
        ["docks"] = "docks",
        ["ghosttown"] = "ghosttown",
        ["armory"] = "armory",
        ["avalanche"] = "s3dava",
        ["lockout"] = "s3dlock",
        ["powerhouse"] = "s3dpwhs",
        ["waterfall"] = "s3dwtf",
        ["reactor"] = "s3dreactor"
    };

    public static string DetermineCategory(string matName, string tagPath, string shaderType)
    {
        string lower = (matName + " " + tagPath + " " + shaderType).ToLowerInvariant();
        if (matName.StartsWith("+") || matName.StartsWith("%") || matName.StartsWith("$") || matName.StartsWith("@") || matName.StartsWith("~") || lower.Contains("sky") || lower.Contains("portal"))
            return "Special (+Sky)";

        // 1. Specific architectural elements FIRST (so floors, walls, panels, and lights are cleanly organized)
        if (lower.Contains("light") || lower.Contains("glow") || lower.Contains("illum") || lower.Contains("lamp") || lower.Contains("beacon") || lower.Contains("emissive") || lower.Contains("neon") || lower.Contains("strip_warm") || lower.Contains("blue_panel_light"))
            return "Lights";
        if (lower.Contains("floor") || lower.Contains("rubber") || lower.Contains("walkway") || lower.Contains("pavement") || lower.Contains("deck") || lower.Contains("runway") || lower.Contains("catwalk") || lower.Contains("tile"))
            return "Floors";
        if (lower.Contains("wall") || lower.Contains("brick") || lower.Contains("barrier") || lower.Contains("divider") || lower.Contains("pillar") || lower.Contains("column") || lower.Contains("post") || lower.Contains("bulkhead") || lower.Contains("door"))
            return "Walls";
        if (lower.Contains("panel") || lower.Contains("trim") || lower.Contains("grate") || lower.Contains("plate") || lower.Contains("scallop") || lower.Contains("laminate") || lower.Contains("hatch") || lower.Contains("vent") || lower.Contains("techstrip"))
            return "Panels";

        // 2. Dedicated Halo 3 environment archetypes (Snowbound, Riverworld, Shrine)
        if (lower.Contains("snow") || lower.Contains("glacier") || lower.Contains("chill_snow") || lower.Contains("snowbound") || lower.Contains("ice_crust") || lower.Contains("icefield") || lower.Contains("icecave") || lower.StartsWith("chill ") || lower.StartsWith("sb "))
            return "Snow (Snowbound)";
        if (lower.Contains("shrine") || lower.Contains("sandtrap") || lower.Contains("sand") || lower.Contains("dune") || lower.Contains("desert") || lower.Contains("stone_pyramid") || lower.Contains("cust_stone") || lower.Contains("cust_copper") || lower.Contains("copper_plate") || lower.StartsWith("shr "))
            return "Desert (Shrine)";
        if (lower.Contains("riverworld_ground") || lower.Contains("riverworld_grass") || lower.Contains("riverworld_dirt") || lower.Contains("grass") || lower.Contains("dirt") || lower.Contains("mud") || lower.StartsWith("river "))
            return "Grass (Riverworld)";

        // 3. Natural environments
        if (lower.Contains("water") || lower.Contains("stream") || lower.Contains("puddle") || lower.Contains("ocean") || lower.Contains("swamp") || lower.Contains("waterfall") || lower.Contains("glass") || lower.Contains("window"))
            return "Water & Glass";
        if (lower.Contains("bark") || lower.Contains("tree") || lower.Contains("leaf") || lower.Contains("leaves") || lower.Contains("pine") || lower.Contains("needle") || lower.Contains("foliage") || lower.Contains("bush") || lower.Contains("wood") || lower.Contains("fern"))
            return "Foliage & Nature";
        if (lower.Contains("rock") || lower.Contains("cliff") || lower.Contains("stone") || lower.Contains("boulder") || lower.Contains("basalt") || lower.Contains("crag") || lower.Contains("gravel") || lower.Contains("rubble"))
            return "Rock & Cliff";
        if (lower.Contains("terrain") || lower.Contains("ground") || lower.Contains("earth") || lower.Contains("ice") || shaderType.Contains("terrain", StringComparison.OrdinalIgnoreCase))
            return "Terrain";

        // 4. Sci-Fi / factions
        if (lower.Contains("covenant") || lower.Contains("cov_") || lower.Contains("purple") || lower.Contains("hull") || lower.Contains("brute") || lower.Contains("forerunner") || lower.Contains("sentinel") || lower.Contains("plasma"))
            return "Covenant & Forerunner";
        if (lower.Contains("metal") || lower.Contains("concrete") || lower.Contains("steel") || lower.Contains("girder") || lower.Contains("base") || lower.Contains("bunker") || lower.Contains("unsc"))
            return "Metal & Structure";

        return "Terrain";
    }

    private static readonly HashSet<string> VerifiedLevelPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "river", "chill", "construct", "con", "guardian", "highground", "bunkerworld",
        "deadlock", "lockout", "salvation", "snowbound", "sb", "zanzibar", "zan", "cyber", "cyberdyne", "boxcanvas", "icefields",
        "shrine", "shr"
    };

    public static bool CheckIsVerified(string matName, string tagPath, string levelPrefix)
    {
        if (matName.StartsWith("+sky") || matName.StartsWith("!river") || matName.StartsWith("%river"))
            return true;
        if (VerifiedLevelPrefixes.Contains(levelPrefix))
            return true;
        string lower = (matName + " " + tagPath).ToLowerInvariant();
        return lower.Contains("riverworld") || lower.Contains("chill") || lower.Contains("construct") ||
               lower.Contains("guardian") || lower.Contains("highground") || lower.Contains("bunkerworld") ||
               lower.Contains("snowbound") || lower.Contains("zanzibar") || lower.Contains("icefields") ||
               lower.Contains("shrine") || lower.Contains("sandtrap");
    }

    private void AddEntry(HaloShaderEntry rawEntry)
    {
        string category = rawEntry.Category != "General" && rawEntry.Category != "Custom"
            ? rawEntry.Category
            : DetermineCategory(rawEntry.MaterialName, rawEntry.TagPath, rawEntry.ShaderType);

        bool isVerified = rawEntry.IsVerified || CheckIsVerified(rawEntry.MaterialName, rawEntry.TagPath, rawEntry.LevelPrefix);
        bool isTerrain = rawEntry.IsTerrainShader || category == "Terrain" ||
                         rawEntry.ShaderType.Contains("terrain", StringComparison.OrdinalIgnoreCase) ||
                         rawEntry.TagPath.EndsWith(".shader_terrain", StringComparison.OrdinalIgnoreCase) ||
                         rawEntry.TagPath.Contains(@"\terrain\", StringComparison.OrdinalIgnoreCase);

        var entry = rawEntry with { Category = category, IsVerified = isVerified, IsTerrainShader = isTerrain };

        if (_byMaterialName.TryAdd(entry.MaterialName, entry))
        {
            _allShaders.Add(entry);

            if (!_byLevelPrefix.TryGetValue(entry.LevelPrefix, out var list))
            {
                list = new List<HaloShaderEntry>();
                _byLevelPrefix[entry.LevelPrefix] = list;
            }
            list.Add(entry);

            if (!_byCategory.TryGetValue(category, out var catList))
            {
                catList = new List<HaloShaderEntry>();
                _byCategory[category] = catList;
            }
            catList.Add(entry);

            if (isTerrain)
            {
                _terrainShaders.Add(entry);
            }
        }
    }

    public void RegisterCustomShader(string materialName, string tagPath, string levelPrefix = "", string category = "Custom", string? texturePath = null)
    {
        AddEntry(new HaloShaderEntry(materialName, tagPath, ".shader", levelPrefix, category, IsVerified: true, TexturePath: texturePath));
    }

    public bool IsValidShader(string materialName)
    {
        if (string.IsNullOrWhiteSpace(materialName))
            return false;

        string cleanName = StripSemanticPrefixes(materialName);
        return _byMaterialName.ContainsKey(cleanName) ||
               cleanName.Equals("default", StringComparison.OrdinalIgnoreCase) ||
               cleanName.Equals("sky", StringComparison.OrdinalIgnoreCase);
    }

    public bool TryGetShader(string materialName, out HaloShaderEntry? entry)
    {
        string cleanName = StripSemanticPrefixes(materialName);
        return _byMaterialName.TryGetValue(cleanName, out entry);
    }

    public IEnumerable<HaloShaderEntry> Search(string query, int maxResults = 50, bool verifiedOnly = false)
    {
        int found = 0;
        foreach (var entry in _allShaders)
        {
            if (verifiedOnly && !entry.IsVerified)
                continue;

            if (string.IsNullOrWhiteSpace(query) ||
                entry.MaterialName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.TagPath.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                yield return entry;
                if (++found >= maxResults)
                    yield break;
            }
        }
    }

    public static string StripSemanticPrefixes(string materialName)
    {
        string name = materialName.Trim();
        // Remove trailing apostrophe (water symbol)
        if (name.EndsWith("'"))
            name = name[..^1];

        // Remove leading symbols: !, %, ~, *, +, @, $
        while (name.Length > 0 && "+!%~*@$#".Contains(name[0]))
        {
            name = name[1..];
        }

        return name.Trim();
    }
}
