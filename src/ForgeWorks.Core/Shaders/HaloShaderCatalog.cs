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
    bool IsVerified = false
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
        "Terrain",
        "Rock & Cliff",
        "UNSC & Metal",
        "Covenant",
        "Foliage & Bark",
        "Water",
        "Special"
    };

    public static HaloShaderCatalog LoadFromFiles(string blenderShadersFile, string? rawTagPathsFile = null)
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

        return catalog;
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
    };

    public static string DetermineCategory(string matName, string tagPath, string shaderType)
    {
        string lower = (matName + " " + tagPath + " " + shaderType).ToLowerInvariant();
        if (matName.StartsWith("+") || matName.StartsWith("%") || matName.StartsWith("$") || matName.StartsWith("@") || matName.StartsWith("~"))
            return "Special";
        if (lower.Contains("water") || lower.Contains("stream") || lower.Contains("puddle") || lower.Contains("ocean") || lower.Contains("swamp") || lower.Contains("waterfall") || (lower.Contains("river") && !lower.Contains("riverworld") && !lower.Contains("tree") && !lower.Contains("bark") && !lower.Contains("leaf") && !lower.Contains("rock") && !lower.Contains("ground")))
            return "Water";
        if (lower.Contains("bark") || lower.Contains("tree") || lower.Contains("leaf") || lower.Contains("leaves") || lower.Contains("pine") || lower.Contains("needle") || lower.Contains("foliage") || lower.Contains("bush") || lower.Contains("wood") || lower.Contains("fern") || lower.Contains("grass"))
            return "Foliage & Bark";
        if (lower.Contains("rock") || lower.Contains("cliff") || lower.Contains("stone") || lower.Contains("boulder") || lower.Contains("basalt") || lower.Contains("crag") || lower.Contains("gravel") || lower.Contains("rubble"))
            return "Rock & Cliff";
        if (lower.Contains("terrain") || lower.Contains("ground") || lower.Contains("sand") || lower.Contains("dirt") || lower.Contains("mud") || lower.Contains("earth") || lower.Contains("snow"))
            return "Terrain";
        if (lower.Contains("covenant") || lower.Contains("purple") || lower.Contains("hull") || lower.Contains("brute") || lower.Contains("forerunner") || lower.Contains("sentinel"))
            return "Covenant";
        if (lower.Contains("metal") || lower.Contains("concrete") || lower.Contains("steel") || lower.Contains("panel") || lower.Contains("wall") || lower.Contains("floor") || lower.Contains("girder") || lower.Contains("trim") || lower.Contains("base") || lower.Contains("bunker"))
            return "UNSC & Metal";
        return "General";
    }

    private static readonly HashSet<string> VerifiedLevelPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "river", "chill", "construct", "con", "guardian", "highground", "bunkerworld",
        "deadlock", "lockout", "salvation", "snowbound", "sb", "zanzibar", "zan", "cyber", "cyberdyne", "boxcanvas"
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
               lower.Contains("snowbound") || lower.Contains("zanzibar");
    }

    private void AddEntry(HaloShaderEntry rawEntry)
    {
        string category = DetermineCategory(rawEntry.MaterialName, rawEntry.TagPath, rawEntry.ShaderType);
        bool isVerified = rawEntry.IsVerified || CheckIsVerified(rawEntry.MaterialName, rawEntry.TagPath, rawEntry.LevelPrefix);
        var entry = rawEntry with { Category = category, IsVerified = isVerified };

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

            if (category == "Terrain" || entry.ShaderType.Contains("terrain", StringComparison.OrdinalIgnoreCase) ||
                entry.TagPath.EndsWith(".shader_terrain", StringComparison.OrdinalIgnoreCase))
            {
                _terrainShaders.Add(entry);
            }
        }
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
