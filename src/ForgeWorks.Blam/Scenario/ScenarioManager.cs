using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.IO;

namespace ForgeWorks.Blam.Scenario;

public sealed record PlayerStartingLocation(
    Vector3 Position,
    float FacingAngle,
    short TeamDesignator = 0,
    short GameTypeFlags = 0
);

public sealed record NetgameEquipment(
    Vector3 Position,
    Quaternion Orientation,
    string ItemTagPath,
    short SpawnType = 0,
    short RespawnTime = 30
);

public sealed record SceneryInstance(
    Vector3 Position,
    Quaternion Orientation,
    string SceneryTagPath,
    string Name = "scenery"
);

public sealed record VehicleInstance(
    Vector3 Position,
    Quaternion Orientation,
    string VehicleTagPath
);

public sealed record WeaponInstance(
    Vector3 Position,
    Quaternion Orientation,
    string WeaponTagPath,
    short RoundsLoaded = 0,
    short RoundsTotal = 0
);

public sealed record TriggerVolume(
    string Name,
    Vector3 Position,
    Vector3 Extents,
    Quaternion Rotation,
    string VolumeType = "KILL" // KILL, OBJECTIVE, SAFE
);

public sealed class ScenarioData
{
    public string ScenarioName { get; set; } = "custom_map";
    public string StructureBspTagPath { get; set; } = string.Empty;
    public string SkyTagPath { get; set; } = @"levels\multi\chill\sky\chill_sky";
    public List<PlayerStartingLocation> Spawns { get; set; } = new();
    public List<NetgameEquipment> Equipment { get; set; } = new();
    public List<SceneryInstance> Scenery { get; set; } = new();
    public List<VehicleInstance> Vehicles { get; set; } = new();
    public List<WeaponInstance> Weapons { get; set; } = new();
    public List<TriggerVolume> Volumes { get; set; } = new();
}

public sealed class ScenarioManager
{
    public ScenarioData Current { get; private set; } = new();

    public void AddSpawn(Vector3 position, float facingAngle = 0.0f, short team = 0)
    {
        Current.Spawns.Add(new PlayerStartingLocation(position, facingAngle, team));
    }

    public void AddEquipment(Vector3 position, string itemTagPath, short respawnSeconds = 30)
    {
        Current.Equipment.Add(new NetgameEquipment(position, Quaternion.Identity, itemTagPath, 0, respawnSeconds));
    }

    public void AddScenery(Vector3 position, Quaternion orientation, string sceneryTagPath, string name = "scenery")
    {
        Current.Scenery.Add(new SceneryInstance(position, orientation, sceneryTagPath, name));
    }

    public void AddTriggerVolume(string name, Vector3 position, Vector3 extents, string volumeType = "KILL")
    {
        Current.Volumes.Add(new TriggerVolume(name, position, extents, Quaternion.Identity, volumeType));
    }

    public void SaveToJson(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    public void LoadFromJson(string filePath)
    {
        if (File.Exists(filePath))
        {
            var json = File.ReadAllText(filePath);
            var loaded = JsonSerializer.Deserialize<ScenarioData>(json);
            if (loaded != null)
            {
                Current = loaded;
            }
        }
    }
}
