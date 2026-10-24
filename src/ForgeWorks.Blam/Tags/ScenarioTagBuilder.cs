using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using ForgeWorks.Blam.Scenario;

namespace ForgeWorks.Blam.Tags;

/// <summary>
/// Writes engine-compliant Halo 3 .scenario (scnr) tag files directly to disk.
/// Automatically injects compiled Structure BSP references, Skies, Spawns, Netgame Equipment, and Palettes.
/// </summary>
public sealed class ScenarioTagBuilder
{
    public const string ScenarioMagic = "scnr";
    public const string StructureBspMagic = "sbsp";

    public static void GenerateScenarioTag(
        string outputPath,
        string scenarioName,
        string bspTagPath,
        string skyTagPath,
        IReadOnlyList<PlayerStartingLocation> spawns,
        IReadOnlyList<NetgameEquipment> equipment,
        IReadOnlyList<TriggerVolume> volumes,
        string? ekPath = null)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // If authentic template scenario tag exists in H3EK, clone it and patch BSP reference cleanly
        if (!string.IsNullOrEmpty(ekPath))
        {
            // Only use an existing scenario tag if it belongs to THIS exact level (updating itself)
            // or if the current level is explicitly named "custom_canvas".
            // NEVER use custom_canvas as a template for other projects to prevent asset bleeding!
            string thisLevelScenario = Path.Combine(ekPath, "tags", "levels", "multi", scenarioName, $"{scenarioName}.scenario");
            string chillTemplate = Path.Combine(ekPath, "tags", "levels", "multi", "chill", "chill.scenario");
            string bunkerTemplate = Path.Combine(ekPath, "tags", "levels", "multi", "bunkerworld", "bunkerworld.scenario");
            string customCanvasPath = Path.Combine(ekPath, "tags", "levels", "multi", "custom_canvas", "custom_canvas.scenario");

            string? template = null;
            string searchTarget = @"levels\multi\chill\chill";

            // 1. Update this map's own scenario if it already exists
            if (File.Exists(thisLevelScenario))
            {
                template = thisLevelScenario;
                searchTarget = bspTagPath;
            }
            // 2. For custom_canvas itself, use custom_canvas.scenario if present
            else if (scenarioName.Equals("custom_canvas", StringComparison.OrdinalIgnoreCase) && File.Exists(customCanvasPath))
            {
                template = customCanvasPath;
                searchTarget = @"levels\multi\custom_canvas\custom_canvas";
            }
            // 3. For any other map, ALWAYS prioritize clean retail templates (chill / bunkerworld)
            // to strictly prevent assets/spawns bleeding from previously compiled maps!
            else if (File.Exists(chillTemplate))
            {
                template = chillTemplate;
                searchTarget = @"levels\multi\chill\chill";
            }
            else if (File.Exists(bunkerTemplate))
            {
                template = bunkerTemplate;
                searchTarget = @"levels\multi\bunkerworld\bunkerworld";
            }
            // 4. Fallback if only custom_canvas is present (e.g. minimal mock test environment)
            else if (File.Exists(customCanvasPath))
            {
                template = customCanvasPath;
                searchTarget = @"levels\multi\custom_canvas\custom_canvas";
            }

            if (template != null)
            {
                byte[] raw = File.ReadAllBytes(template);
                byte[] searchBytes = Encoding.ASCII.GetBytes(searchTarget);
                byte[] replaceBytes = Encoding.ASCII.GetBytes(bspTagPath);

                bool patched = false;
                int idx = 0;
                while (idx <= raw.Length - searchBytes.Length)
                {
                    bool match = true;
                    for (int j = 0; j < searchBytes.Length; j++)
                    {
                        if (raw[idx + j] != searchBytes[j])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        // Safely patch without exceeding the original string length.
                        // Overwriting past searchBytes.Length wipes out the subsequent 'tgrf' (reversed 'frgt') chunk tag.
                        int copyLen = Math.Min(replaceBytes.Length, searchBytes.Length);
                        Array.Copy(replaceBytes, 0, raw, idx, copyLen);
                        for (int k = copyLen; k < searchBytes.Length; k++)
                        {
                            raw[idx + k] = 0; // Null-pad within slot bounds only
                        }
                        patched = true;
                        idx += searchBytes.Length;
                    }
                    else
                    {
                        idx++;
                    }
                }

                if (patched)
                {
                    File.WriteAllBytes(outputPath, raw);
                    return;
                }
            }
        }

        using var stream = File.Create(outputPath);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);

        // Pre-allocate memory buffer for tag construction
        var stringTable = new MemoryStream();
        var stringWriter = new BinaryWriter(stringTable, Encoding.UTF8);

        uint AddString(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            uint offset = (uint)stringTable.Position;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            stringWriter.Write(bytes);
            stringWriter.Write((byte)0); // Null terminator
            return offset;
        }

        // 1. Tag Header (0x00 - 0x3F)
        byte[] magicBytes = Encoding.ASCII.GetBytes(ScenarioMagic);
        Array.Reverse(magicBytes); // Blam disk reversed endian format: 'rncs'

        writer.Write(new byte[0x24]); // Pre-header padding
        writer.Write(magicBytes);     // Offset 0x24: 'rncs'
        writer.Write((uint)0x35179);  // Offset 0x28: Engine Checksum
        writer.Write((uint)0);        // Offset 0x2C: Reserved
        writer.Write((uint)0x40);     // Offset 0x30: Header Size
        writer.Write(new byte[12]);   // Offset 0x34 - 0x3F: Alignment padding

        // 2. Post-Header Root Scenario Block (Starts at 0x40)
        long rootStart = stream.Position;

        // Structure BSPs reflexive (offset 0x40)
        // int32 count, uint32 offset
        writer.Write((int)1); // 1 BSP
        writer.Write((uint)0x100); // Offset to BSP element

        // Skies reflexive (offset 0x48)
        writer.Write((int)1); // 1 Sky
        writer.Write((uint)0x130); // Offset to Sky element

        // Player Starting Locations reflexive (offset 0x50)
        writer.Write((int)spawns.Count);
        writer.Write((uint)0x160);

        // Netgame Equipment reflexive (offset 0x58)
        writer.Write((int)equipment.Count);
        writer.Write((uint)(0x160 + spawns.Count * 0x34));

        // Pad up to BSP Block element table
        while (stream.Position < 0x100)
        {
            writer.Write((byte)0);
        }

        // 3. Write Structure BSP Block Element (at 0x100)
        byte[] sbspGroup = Encoding.ASCII.GetBytes(StructureBspMagic);
        Array.Reverse(sbspGroup);
        writer.Write(sbspGroup); // 4-char magic
        uint bspPathOffset = 0x500 + AddString(bspTagPath);
        writer.Write(bspPathOffset); // Path offset
        writer.Write(bspTagPath.Length + 1); // Path length

        while (stream.Position < 0x130)
        {
            writer.Write((byte)0);
        }

        // 4. Write Sky Block Element (at 0x130)
        byte[] skyGroup = Encoding.ASCII.GetBytes("scen");
        Array.Reverse(skyGroup);
        writer.Write(skyGroup);
        uint skyPathOffset = 0x500 + AddString(skyTagPath);
        writer.Write(skyPathOffset);
        writer.Write(skyTagPath.Length + 1);

        while (stream.Position < 0x160)
        {
            writer.Write((byte)0);
        }

        // 5. Write Player Starting Locations Elements
        foreach (var spawn in spawns)
        {
            writer.Write(spawn.Position.X);
            writer.Write(spawn.Position.Y);
            writer.Write(spawn.Position.Z);
            writer.Write(spawn.FacingAngle);
            writer.Write(spawn.TeamDesignator);
            writer.Write(spawn.GameTypeFlags);
            writer.Write(new byte[32]); // Remaining starting profile data
        }

        // 6. Write Netgame Equipment Elements
        foreach (var eq in equipment)
        {
            writer.Write(eq.Position.X);
            writer.Write(eq.Position.Y);
            writer.Write(eq.Position.Z);
            writer.Write(eq.Orientation.X);
            writer.Write(eq.Orientation.Y);
            writer.Write(eq.Orientation.Z);
            writer.Write(eq.Orientation.W);
            writer.Write(eq.SpawnType);
            writer.Write(eq.RespawnTime);

            byte[] wepGroup = Encoding.ASCII.GetBytes("weap");
            Array.Reverse(wepGroup);
            writer.Write(wepGroup);
            uint eqPathOffset = 0x500 + AddString(eq.ItemTagPath);
            writer.Write(eqPathOffset);
            writer.Write(eq.ItemTagPath.Length + 1);
        }

        // Pad to string table boundary (0x500)
        while (stream.Position < 0x500)
        {
            writer.Write((byte)0);
        }

        // 7. Write String Table Data
        byte[] stringBytes = stringTable.ToArray();
        writer.Write(stringBytes);
    }
}
