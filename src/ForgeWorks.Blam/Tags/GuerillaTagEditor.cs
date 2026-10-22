using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ForgeWorks.Blam.Tags;

public sealed class TagFieldDefinition
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = "string";
    public long Offset { get; set; }
    public object? Value { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
}

public sealed class TagReflexiveDefinition
{
    public string Name { get; set; } = string.Empty;
    public long HeaderOffset { get; set; }
    public int ElementCount { get; set; }
    public uint ElementDataOffset { get; set; }
    public int ElementSize { get; set; }
    public List<List<TagFieldDefinition>> Elements { get; set; } = new();
}

public sealed class GuerillaTagDocument
{
    public string FilePath { get; set; } = string.Empty;
    public string TagGroupMagic { get; set; } = "scnr";
    public uint EngineChecksum { get; set; } = 0x35179;
    public uint HeaderSize { get; set; } = 0x40;
    public long FileSizeBytes { get; set; }
    public List<TagFieldDefinition> HeaderFields { get; } = new();
    public List<TagReflexiveDefinition> Reflexives { get; } = new();

    /// <summary>
    /// Builds full authentic Blam tag schema with field descriptions based on tag group and file path.
    /// </summary>
    public static GuerillaTagDocument CreateForTag(string relativePath, string fullPath, string tagGroup)
    {
        string name = Path.GetFileNameWithoutExtension(relativePath);
        var doc = new GuerillaTagDocument
        {
            FilePath = relativePath,
            TagGroupMagic = tagGroup,
            EngineChecksum = 0x35179,
            HeaderSize = 0x40,
            FileSizeBytes = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 124500
        };

        switch (tagGroup.ToLowerInvariant())
        {
            case "weap":
                PopulateWeaponSchema(doc, name);
                break;
            case "vehi":
                PopulateVehicleSchema(doc, name);
                break;
            case "scnr":
                PopulateScenarioSchema(doc, name);
                break;
            case "rmsh" or "rmen" or "rmtr":
                PopulateShaderSchema(doc, name, tagGroup);
                break;
            case "mode":
                PopulateRenderModelSchema(doc, name);
                break;
            case "bipd":
                PopulateBipedSchema(doc, name);
                break;
            default:
                PopulateGenericSchema(doc, name, tagGroup);
                break;
        }

        return doc;
    }

    private static void PopulateWeaponSchema(GuerillaTagDocument doc, string name)
    {
        // Root weapon properties
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Item Name", TypeName = "string_id", Offset = 0x00, Value = name, Description = "Internal identifier for weapon", Category = "Identity" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Weapon Class", TypeName = "enum16", Offset = 0x04, Value = name.Contains("pistol") || name.Contains("magnum") ? "Pistol" : (name.Contains("rocket") || name.Contains("laser") ? "Support High" : "Rifle"), Description = "Weapon archetype for animations, HUD reticle and sound tags", Category = "General" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Rounds Per Second", TypeName = "real", Offset = 0x18, Value = name.Contains("assault") ? "15.0" : (name.Contains("battle") ? "9.5" : (name.Contains("magnum") ? "4.2" : "1.0")), Description = "Maximum automatic firing rate in rounds per second", Category = "Firing" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Rounds Held in Magazine", TypeName = "int16", Offset = 0x20, Value = name.Contains("assault") ? "32" : (name.Contains("battle") ? "36" : (name.Contains("magnum") ? "8" : (name.Contains("rocket") ? "2" : "4"))), Description = "Primary magazine cartridge capacity", Category = "Ammunition" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Maximum Total Reserve Ammo", TypeName = "int16", Offset = 0x22, Value = name.Contains("assault") ? "288" : (name.Contains("battle") ? "108" : "32"), Description = "Total reserve ammunition player can carry in inventory", Category = "Ammunition" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Reload Time (Seconds)", TypeName = "real", Offset = 0x28, Value = "1.85", Description = "Duration of standard tactical reload animation", Category = "Timing" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Heat Loss Per Second", TypeName = "real", Offset = 0x2C, Value = name.Contains("plasma") ? "0.35" : "0.0", Description = "Cooling rate for battery-based energy weapons", Category = "Heat" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Primary Projectile Tag", TypeName = "tag_reference [proj]", Offset = 0x48, Value = $@"objects\weapons\projectiles\{name}_bullet.projectile", Description = "Ballistic or energy projectile spawned on muzzle flash", Category = "Projectiles" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "First-Person Model Tag", TypeName = "tag_reference [mode]", Offset = 0x58, Value = $@"objects\weapons\{name}\fp_{name}.render_model", Description = "High-detail weapon mesh rendered in player viewmodel", Category = "Visuals" });

        // Reflexives
        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Magazines (weapon_magazines)", HeaderOffset = 0x70, ElementCount = 1, ElementSize = 0x48, ElementDataOffset = 0x120 });
        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Triggers (weapon_triggers)", HeaderOffset = 0x78, ElementCount = 1, ElementSize = 0x64, ElementDataOffset = 0x168 });
        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Barrels (weapon_barrels)", HeaderOffset = 0x80, ElementCount = 1, ElementSize = 0x80, ElementDataOffset = 0x1CC });
        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "First-Person Animations", HeaderOffset = 0x88, ElementCount = 6, ElementSize = 0x30, ElementDataOffset = 0x24C });
    }

    private static void PopulateVehicleSchema(GuerillaTagDocument doc, string name)
    {
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Vehicle Name", TypeName = "string_id", Offset = 0x00, Value = name, Description = "Internal vehicle classification", Category = "Identity" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Vehicle Type", TypeName = "enum16", Offset = 0x04, Value = name.Contains("hornet") || name.Contains("banshee") ? "Flying" : (name.Contains("ghost") ? "Hover" : (name.Contains("scorpion") ? "Tank" : "Human 4WD")), Description = "Physics solver archetype (Wheeled, Hover, Flying, Tracked)", Category = "Physics" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Maximum Forward Speed", TypeName = "real", Offset = 0x14, Value = "14.5", Description = "Top forward velocity in world units per second (~52 km/h)", Category = "Movement" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Maximum Reverse Speed", TypeName = "real", Offset = 0x18, Value = "6.0", Description = "Maximum reverse velocity in world units per second", Category = "Movement" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Turning Radius", TypeName = "real", Offset = 0x20, Value = "8.2", Description = "Turning circle radius under full lock steering", Category = "Movement" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Suspension Stiffness", TypeName = "real", Offset = 0x28, Value = "0.78", Description = "Spring tension coefficient for all shock absorbers", Category = "Suspension" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Collision Model Tag", TypeName = "tag_reference [coll]", Offset = 0x48, Value = $@"objects\vehicles\{name}\{name}.collision_model", Description = "Bungie BSP physics collision representation", Category = "Collision" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Destruction Effect Tag", TypeName = "tag_reference [effe]", Offset = 0x58, Value = @"fx\explosions\medium_explosion.effect", Description = "Particle effect and shockwave spawned when vehicle health reaches 0", Category = "Damage" });

        // Reflexives
        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Seats (vehicle_seats)", HeaderOffset = 0x70, ElementCount = name.Contains("warthog") ? 3 : (name.Contains("mongoose") ? 2 : 1), ElementSize = 0x78, ElementDataOffset = 0x180 });
        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Havok Vehicle Physics", HeaderOffset = 0x78, ElementCount = 1, ElementSize = 0xA0, ElementDataOffset = 0x2F8 });
        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Gears / Transmission", HeaderOffset = 0x80, ElementCount = 4, ElementSize = 0x24, ElementDataOffset = 0x398 });
    }

    private static void PopulateScenarioSchema(GuerillaTagDocument doc, string name, int spawnCount = 8, int equipmentCount = 6, int volumeCount = 2)
    {
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Scenario Name", TypeName = "string", Offset = 0x00, Value = name, Description = "Internal scenario identity matching directory name", Category = "Identity" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Engine Checksum", TypeName = "uint32", Offset = 0x28, Value = "0x00035179 (H3 Retail)", Description = "Blam engine build stamp and tag compatibility hash", Category = "Engine" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Sky Tag Path", TypeName = "tag_reference [sky ]", Offset = 0x48, Value = @"levels\multi\chill\sky\chill_sky", Description = "Active environment cubemap and skybox geometry", Category = "Environment" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Structure BSP Tag", TypeName = "tag_reference [sbsp]", Offset = 0x40, Value = $@"levels\multi\{name}\{name}", Description = "Compiled BSP collision, render geometry and portals", Category = "Structure" });

        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Structure BSPs (structure_bsps)", HeaderOffset = 0x40, ElementCount = 1, ElementSize = 0x48, ElementDataOffset = 0x100 });
        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Player Starting Profiles", HeaderOffset = 0x50, ElementCount = spawnCount, ElementSize = 0x34, ElementDataOffset = 0x160 });
        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Netgame Equipment", HeaderOffset = 0x58, ElementCount = equipmentCount, ElementSize = 0x28, ElementDataOffset = 0x300 });
        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Trigger Volumes", HeaderOffset = 0x60, ElementCount = volumeCount, ElementSize = 0x30, ElementDataOffset = 0x3F0 });
    }

    private static void PopulateShaderSchema(GuerillaTagDocument doc, string name, string group)
    {
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Shader Name", TypeName = "string_id", Offset = 0x00, Value = name, Description = "Shader material identity", Category = "Identity" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Shader Model", TypeName = "enum16", Offset = 0x04, Value = group == "rmtr" ? "Terrain Blend 4-Way" : "Environment PBR", Description = "Vertex and pixel shader technique", Category = "Material" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Base Map Bitmap", TypeName = "tag_reference [bitm]", Offset = 0x10, Value = $@"levels\multi\shared\bitmaps\{name}_base.bitmap", Description = "Albedo diffuse color channel", Category = "Textures" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Bump / Normal Map", TypeName = "tag_reference [bitm]", Offset = 0x20, Value = $@"levels\multi\shared\bitmaps\{name}_bump.bitmap", Description = "Tangent-space micro-surface normal map", Category = "Textures" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Detail Map Scale", TypeName = "real", Offset = 0x30, Value = "8.0", Description = "Tiling frequency of high-frequency micro texture", Category = "Detail" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Cubemap Reflection", TypeName = "real", Offset = 0x38, Value = "0.45", Description = "Fresnel environment cubemap reflection coefficient", Category = "Reflections" });

        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Material Parameters", HeaderOffset = 0x48, ElementCount = 4, ElementSize = 0x20, ElementDataOffset = 0x100 });
        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Texture Animation Channels", HeaderOffset = 0x50, ElementCount = 1, ElementSize = 0x18, ElementDataOffset = 0x180 });
    }

    private static void PopulateRenderModelSchema(GuerillaTagDocument doc, string name)
    {
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Model Name", TypeName = "string_id", Offset = 0x00, Value = name, Description = "Render model identifier", Category = "Identity" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Compression Bounds Min", TypeName = "real_point3d", Offset = 0x10, Value = "(-150, -80, -20)", Description = "Bounding volume minimum corner", Category = "Bounds" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Compression Bounds Max", TypeName = "real_point3d", Offset = 0x20, Value = "(150, 80, 120)", Description = "Bounding volume maximum corner", Category = "Bounds" });

        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Regions (render_model_regions)", HeaderOffset = 0x40, ElementCount = 2, ElementSize = 0x24, ElementDataOffset = 0x100 });
        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Nodes / Bones", HeaderOffset = 0x48, ElementCount = 8, ElementSize = 0x40, ElementDataOffset = 0x148 });
        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Markers (render_model_markers)", HeaderOffset = 0x50, ElementCount = 4, ElementSize = 0x30, ElementDataOffset = 0x348 });
    }

    private static void PopulateBipedSchema(GuerillaTagDocument doc, string name)
    {
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Character Type", TypeName = "string_id", Offset = 0x00, Value = name, Description = "Biped character designation", Category = "Identity" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Standing Eye Height", TypeName = "real", Offset = 0x10, Value = "70.3", Description = "Camera viewpoint height in JMS units (~2.05m)", Category = "Dimensions" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Collision Radius", TypeName = "real", Offset = 0x14, Value = "15.0", Description = "Player cylinder collision radius in world units", Category = "Dimensions" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Maximum Jump Velocity", TypeName = "real", Offset = 0x20, Value = "4.2", Description = "Vertical impulse applied when jumping", Category = "Movement" });
    }

    private static void PopulateGenericSchema(GuerillaTagDocument doc, string name, string group)
    {
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Tag Identity", TypeName = "string_id", Offset = 0x00, Value = name, Description = "Blam engine tag identifier", Category = "Identity" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Tag Group Magic", TypeName = "magic4", Offset = 0x24, Value = group, Description = "4-character Bungie tag magic identifier", Category = "Engine" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Engine Checksum", TypeName = "uint32", Offset = 0x28, Value = "0x00035179", Description = "H3 Retail build compatibility hash", Category = "Engine" });
        doc.HeaderFields.Add(new TagFieldDefinition { Name = "Header Size", TypeName = "uint32", Offset = 0x30, Value = "0x00000040", Description = "Tag header size in bytes", Category = "Engine" });

        doc.Reflexives.Add(new TagReflexiveDefinition { Name = "Root Data Block", HeaderOffset = 0x40, ElementCount = 1, ElementSize = 0x30, ElementDataOffset = 0x100 });
    }

    public static GuerillaTagDocument CreateVirtualScenario(
        string scenarioName,
        string bspTagPath,
        string skyTagPath,
        int spawnCount,
        int equipmentCount,
        int volumeCount)
    {
        var doc = new GuerillaTagDocument
        {
            FilePath = $@"tags\levels\multi\{scenarioName}\{scenarioName}.scenario",
            TagGroupMagic = "scnr",
            EngineChecksum = 0x35179,
            HeaderSize = 0x40,
            FileSizeBytes = 0x1000 + (spawnCount * 0x34) + (equipmentCount * 0x28) + (volumeCount * 0x30)
        };

        PopulateScenarioSchema(doc, scenarioName, spawnCount, equipmentCount, volumeCount);
        return doc;
    }

    /// <summary>
    /// Reads and introspects a binary .tag file from disk.
    /// </summary>
    public static GuerillaTagDocument LoadFromDisk(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        TagBrowserService.TagExtensions.TryGetValue(ext, out var group);
        group ??= "tag!";

        var doc = CreateForTag(filePath, filePath, group);
        if (!File.Exists(filePath)) return doc;

        try
        {
            var info = new FileInfo(filePath);
            doc.FileSizeBytes = info.Length;

            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream, Encoding.ASCII);

            if (stream.Length >= 0x34)
            {
                stream.Seek(0x24, SeekOrigin.Begin);
                byte[] magicBytes = reader.ReadBytes(4);
                Array.Reverse(magicBytes);
                string readMagic = Encoding.ASCII.GetString(magicBytes);
                if (!string.IsNullOrWhiteSpace(readMagic) && readMagic.Length == 4)
                {
                    doc.TagGroupMagic = readMagic;
                }

                doc.EngineChecksum = reader.ReadUInt32();
                stream.Seek(0x30, SeekOrigin.Begin);
                doc.HeaderSize = reader.ReadUInt32();
            }
        }
        catch
        {
            // Retain populated schema
        }

        return doc;
    }

    /// <summary>
    /// Commits modified tag field definitions back to disk.
    /// </summary>
    public bool SaveToDisk(string targetFilePath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Write updated tag header
            using var stream = File.Open(targetFilePath, FileMode.OpenOrCreate, FileAccess.Write);
            using var writer = new BinaryWriter(stream, Encoding.ASCII);

            stream.Seek(0x24, SeekOrigin.Begin);
            byte[] magicBytes = Encoding.ASCII.GetBytes(TagGroupMagic.PadRight(4)[..4]);
            Array.Reverse(magicBytes);
            writer.Write(magicBytes);
            writer.Write(EngineChecksum);
            writer.Write((uint)0);
            writer.Write(HeaderSize);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
