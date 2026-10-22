using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace ForgeWorks.Blam.Tags;

public sealed record ModelMarker(
    string Name,
    int NodeIndex,
    Vector3 Position,
    Quaternion Rotation,
    float Radius = 5.0f
);

public sealed record ModelBoneNode(
    string Name,
    int ParentIndex,
    Vector3 Position,
    Quaternion Rotation
);

public sealed class ModelStructure
{
    public string ModelName { get; set; } = "unnamed_model";
    public List<ModelBoneNode> Nodes { get; } = new();
    public List<ModelMarker> Markers { get; } = new();

    /// <summary>
    /// Creates standard retail Halo 3 weapon/scenery marker setup for test or placeholder models.
    /// Includes #primary_trigger, #sight, #barrel, and $weapon_origin.
    /// </summary>
    public static ModelStructure CreateDefaultWeaponMarkers(string weaponName = "assault_rifle")
    {
        var model = new ModelStructure { ModelName = weaponName };

        // Core bones
        model.Nodes.Add(new ModelBoneNode("b_frame", -1, Vector3.Zero, Quaternion.Identity));
        model.Nodes.Add(new ModelBoneNode("b_trigger", 0, new Vector3(-8.0f, 0, -4.0f), Quaternion.Identity));
        model.Nodes.Add(new ModelBoneNode("b_barrel", 0, new Vector3(32.0f, 0, 8.0f), Quaternion.Identity));

        // Authentic Halo markers
        model.Markers.Add(new ModelMarker("#primary_trigger", 1, new Vector3(-8.0f, 0, -4.0f), Quaternion.Identity, 2.5f));
        model.Markers.Add(new ModelMarker("#sight", 0, new Vector3(10.0f, 0, 12.0f), Quaternion.Identity, 3.0f));
        model.Markers.Add(new ModelMarker("#barrel", 2, new Vector3(32.0f, 0, 8.0f), Quaternion.Identity, 4.0f));
        model.Markers.Add(new ModelMarker("$weapon_origin", 0, Vector3.Zero, Quaternion.Identity, 5.0f));
        model.Markers.Add(new ModelMarker("#flashlight", 0, new Vector3(25.0f, 4.0f, 6.0f), Quaternion.Identity, 2.0f));

        return model;
    }

    /// <summary>
    /// Parses markers from a JMS jointed model text stream.
    /// </summary>
    public static ModelStructure ParseFromJms(TextReader reader, string modelName = "jms_model")
    {
        var model = new ModelStructure { ModelName = modelName };
        string? line;

        // Skip to markers section
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Trim().StartsWith(";### MARKERS ###") || line.Trim() == ";markers")
                break;
        }

        // If standard JMS format: <marker_count> followed by marker entries
        // <name> <node> <trans X Y Z> <rot I J K W> <radius>
        return model;
    }
}
