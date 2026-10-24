using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeWorks.Core.Geometry;

namespace ForgeWorks.Core.Project;

public sealed class ProjectVertexData
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public float Nx { get; set; }
    public float Ny { get; set; }
    public float Nz { get; set; }

    public float Cr { get; set; } = 1f;
    public float Cg { get; set; } = 1f;
    public float Cb { get; set; } = 1f;
    public float Ca { get; set; } = 1f;

    public List<float[]> UVs { get; set; } = new();

    public static ProjectVertexData FromVertex(Vertex v)
    {
        var data = new ProjectVertexData
        {
            X = v.Position.X,
            Y = v.Position.Y,
            Z = v.Position.Z,
            Nx = v.Normal.X,
            Ny = v.Normal.Y,
            Nz = v.Normal.Z,
            Cr = v.Color.X,
            Cg = v.Color.Y,
            Cb = v.Color.Z,
            Ca = v.Color.W
        };
        foreach (var uv in v.UVs)
        {
            data.UVs.Add(new[] { uv.X, uv.Y });
        }
        return data;
    }

    public Vertex ToVertex()
    {
        var v = new Vertex(new Vector3(X, Y, Z), new Vector3(Nx, Ny, Nz))
        {
            Color = new Vector4(Cr, Cg, Cb, Ca)
        };
        foreach (var uvArr in UVs)
        {
            if (uvArr.Length >= 2)
            {
                v.UVs.Add(new Vector2(uvArr[0], uvArr[1]));
            }
        }
        return v;
    }
}

public sealed class ProjectTriangleData
{
    public int V0 { get; set; }
    public int V1 { get; set; }
    public int V2 { get; set; }
    public int MaterialIndex { get; set; }
    public float Nx { get; set; }
    public float Ny { get; set; }
    public float Nz { get; set; }

    public static ProjectTriangleData FromTriangle(Triangle t) => new()
    {
        V0 = t.V0,
        V1 = t.V1,
        V2 = t.V2,
        MaterialIndex = t.MaterialIndex,
        Nx = t.Normal.X,
        Ny = t.Normal.Y,
        Nz = t.Normal.Z
    };

    public Triangle ToTriangle() => new(V0, V1, V2, MaterialIndex)
    {
        Normal = new Vector3(Nx, Ny, Nz)
    };
}

public sealed class ProjectMeshData
{
    public string Name { get; set; } = "unnamed_object";
    public string MaterialName { get; set; } = "default";
    public List<string> MaterialSlots { get; set; } = new();
    public Dictionary<string, string> MaterialTextureMap { get; set; } = new();
    public string? SourceTexturePath { get; set; }
    public bool IsCollideable { get; set; } = true;
    public float[] TransformMatrix { get; set; } = new float[16];

    public List<ProjectVertexData> Vertices { get; set; } = new();
    public List<ProjectTriangleData> Triangles { get; set; } = new();

    public static ProjectMeshData FromMesh(MeshObject mesh)
    {
        var data = new ProjectMeshData
        {
            Name = mesh.Name,
            MaterialName = mesh.MaterialName,
            MaterialSlots = new List<string>(mesh.MaterialSlots),
            SourceTexturePath = mesh.SourceTexturePath,
            IsCollideable = mesh.IsCollideable
        };

        foreach (var (k, v) in mesh.MaterialTextureMap)
        {
            data.MaterialTextureMap[k] = v;
        }

        // 4x4 matrix row-major
        var m = mesh.Transform;
        data.TransformMatrix = new[]
        {
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44
        };

        foreach (var v in mesh.Vertices) data.Vertices.Add(ProjectVertexData.FromVertex(v));
        foreach (var t in mesh.Triangles) data.Triangles.Add(ProjectTriangleData.FromTriangle(t));

        return data;
    }

    public MeshObject ToMesh()
    {
        var mesh = new MeshObject(Name)
        {
            MaterialName = MaterialName,
            MaterialSlots = new List<string>(MaterialSlots),
            SourceTexturePath = SourceTexturePath,
            IsCollideable = IsCollideable
        };

        foreach (var (k, v) in MaterialTextureMap)
        {
            mesh.MaterialTextureMap[k] = v;
        }

        if (TransformMatrix is { Length: 16 })
        {
            mesh.Transform = new Matrix4x4(
                TransformMatrix[0], TransformMatrix[1], TransformMatrix[2], TransformMatrix[3],
                TransformMatrix[4], TransformMatrix[5], TransformMatrix[6], TransformMatrix[7],
                TransformMatrix[8], TransformMatrix[9], TransformMatrix[10], TransformMatrix[11],
                TransformMatrix[12], TransformMatrix[13], TransformMatrix[14], TransformMatrix[15]
            );
        }

        foreach (var vd in Vertices) mesh.Vertices.Add(vd.ToVertex());
        foreach (var td in Triangles) mesh.Triangles.Add(td.ToTriangle());

        return mesh;
    }
}

public sealed class ProjectSpawnData
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float FacingAngle { get; set; }
    public short TeamDesignator { get; set; }
    public short GameTypeFlags { get; set; }
}

public sealed class ProjectEquipmentData
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Qx { get; set; }
    public float Qy { get; set; }
    public float Qz { get; set; }
    public float Qw { get; set; } = 1f;
    public string ItemTagPath { get; set; } = "";
    public short SpawnType { get; set; }
    public short RespawnTime { get; set; } = 30;
}

public sealed class ProjectVolumeData
{
    public string Name { get; set; } = "volume";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float ExtX { get; set; } = 200f;
    public float ExtY { get; set; } = 200f;
    public float ExtZ { get; set; } = 200f;
    public float Qx { get; set; }
    public float Qy { get; set; }
    public float Qz { get; set; }
    public float Qw { get; set; } = 1f;
    public string VolumeType { get; set; } = "KILL";
}

public sealed class ProjectMarkerData
{
    public string Name { get; set; } = "marker";
    public int NodeIndex { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Qx { get; set; }
    public float Qy { get; set; }
    public float Qz { get; set; }
    public float Qw { get; set; } = 1f;
    public float Radius { get; set; } = 2.0f;
}

public sealed class ProjectTerrainSettings
{
    public int ResX { get; set; } = 48;
    public int ResY { get; set; } = 48;
    public float Width { get; set; } = 6000.0f;
    public float Length { get; set; } = 6000.0f;
    public float HeightScale { get; set; } = 800.0f;
    public int Octaves { get; set; } = 5;
    public float WarpStrength { get; set; } = 300.0f;
    public int ErosionDrops { get; set; } = 1000;
    public int ThermalErosionIterations { get; set; } = 0;
    public int AlgorithmIndex { get; set; } = 0;
    public int CellularStyleIndex { get; set; } = 0;
    public bool OuterCliffWalls { get; set; } = true;
    public float PerimeterCliffHeight { get; set; } = 600.0f;
    public bool SealAcrossTop { get; set; } = true;
    public float UvTileScale { get; set; } = 300.0f;
}

/// <summary>
/// Native project file format (.blam) for ForgeWorks / HaloForge Studio.
/// Preserves full level geometry, applied material names (1:1 Blam tags), multi-material slots,
/// textures, Sapien scenario entities, markers, and procedural settings.
/// </summary>
public sealed class ForgeWorksProject
{
    public const string ProjectExtension = ".blam";
    public const string ProjectMagic = "FORGEWORKS_BLAM_PROJECT";
    public const int CurrentVersion = 1;

    public string Magic { get; set; } = ProjectMagic;
    public int Version { get; set; } = CurrentVersion;
    public string ProjectName { get; set; } = "custom_canvas";
    public string TargetGame { get; set; } = "Halo 3 (H3EK)";
    public string EkPath { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

    public List<ProjectMeshData> Meshes { get; set; } = new();
    public List<ProjectSpawnData> Spawns { get; set; } = new();
    public List<ProjectEquipmentData> Equipment { get; set; } = new();
    public List<ProjectVolumeData> Volumes { get; set; } = new();
    public List<ProjectMarkerData> Markers { get; set; } = new();
    public ProjectTerrainSettings TerrainSettings { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public void Save(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        LastModifiedAt = DateTime.UtcNow;
        string json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public static ForgeWorksProject Load(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"ForgeWorks project file not found: {filePath}");

        string json = File.ReadAllText(filePath);
        var proj = JsonSerializer.Deserialize<ForgeWorksProject>(json, JsonOptions);
        if (proj == null)
            throw new InvalidDataException($"Failed to deserialize ForgeWorks project file: {filePath}");

        if (proj.Magic != ProjectMagic)
            throw new InvalidDataException($"Unrecognized project magic header: '{proj.Magic}'. Expected '{ProjectMagic}'.");

        return proj;
    }
}
