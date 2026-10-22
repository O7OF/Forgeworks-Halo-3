using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ForgeWorks.Core.Geometry;

public enum SceneNodeType
{
    Root,
    LevelRoot,      // b_levelroot (Parent of all level geometry)
    PortalsRoot,    // z_portals (Parent of all portal geometry)
    EntityGroup,    // Sapien Entities (Spawns, Equipment, Volumes)
    MarkerGroup,    // Model / Scenario Markers
    Mesh,           // Actual geometry mesh (terrain, bunker, brush)
    Portal,         // Portal mesh (+portal_*)
    Spawn,          // Player starting location
    Equipment,      // Netgame weapon/equipment
    Volume,         // Trigger/Kill volume
    Marker          // Model marker (#primary_trigger, $weapon_origin, etc.)
}

public sealed class SceneNode : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Node";
    public SceneNodeType Type { get; set; } = SceneNodeType.Mesh;

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VisibilityIcon));
                OnPropertyChanged(nameof(DisplayOpacity));
            }
        }
    }

    public double DisplayOpacity => IsVisible ? 1.0 : 0.4;
    public string VisibilityIcon => IsVisible ? "👁️" : "🕶️";

    public bool IsLocked { get; set; } = false;
    public Vector3 Position { get; set; } = Vector3.Zero;
    public float PosX { get => Position.X; set => Position = Position with { X = value }; }
    public float PosY { get => Position.Y; set => Position = Position with { Y = value }; }
    public float PosZ { get => Position.Z; set => Position = Position with { Z = value }; }
    public Vector3 Rotation { get; set; } = Vector3.Zero;
    public Vector3 Scale { get; set; } = Vector3.One;
    public MeshObject? Mesh { get; set; }
    public object? TagPayload { get; set; }

    public ObservableCollection<SceneNode> Children { get; } = new();

    public string Icon => Type switch
    {
        SceneNodeType.Root => "🌐",
        SceneNodeType.LevelRoot => "🏛️",
        SceneNodeType.PortalsRoot => "🚪",
        SceneNodeType.EntityGroup => "🎭",
        SceneNodeType.MarkerGroup => "📍",
        SceneNodeType.Mesh => "🔲",
        SceneNodeType.Portal => "🚪",
        SceneNodeType.Spawn => "🚩",
        SceneNodeType.Equipment => "🔫",
        SceneNodeType.Volume => "📦",
        SceneNodeType.Marker => "🎯",
        _ => "📄"
    };

    public string StatsSummary
    {
        get
        {
            if (Mesh != null)
                return $"{Mesh.Vertices.Count}v | {Mesh.Triangles.Count}t";
            if (Children.Count > 0)
                return $"{Children.Count} items";
            return "";
        }
    }
}

/// <summary>
/// Scene Graph container managing the Halo engine hierarchy:
/// b_levelroot (Terrain, Bunkers, CSG Brushes)
/// z_portals (Cluster Portals)
/// Sapien Entities (Spawns, Equipment, Volumes)
/// Markers (Model and Scenario Markers)
/// </summary>
public sealed class SceneContainer
{
    public SceneNode Root { get; }
    public SceneNode LevelRoot { get; }
    public SceneNode PortalsRoot { get; }
    public SceneNode EntitiesRoot { get; }
    public SceneNode MarkersRoot { get; }

    public ObservableCollection<SceneNode> TopLevelNodes { get; } = new();

    public SceneContainer()
    {
        Root = new SceneNode { Name = "Scene Root", Type = SceneNodeType.Root };

        LevelRoot = new SceneNode { Name = "b_levelroot", Type = SceneNodeType.LevelRoot };
        PortalsRoot = new SceneNode { Name = "z_portals", Type = SceneNodeType.PortalsRoot };
        EntitiesRoot = new SceneNode { Name = "Sapien Entities", Type = SceneNodeType.EntityGroup };
        MarkersRoot = new SceneNode { Name = "Markers", Type = SceneNodeType.MarkerGroup };

        Root.Children.Add(LevelRoot);
        Root.Children.Add(PortalsRoot);
        Root.Children.Add(EntitiesRoot);
        Root.Children.Add(MarkersRoot);

        TopLevelNodes.Add(Root);
    }

    public bool HasAnyGeometryNodes => LevelRoot.Children.Count > 0 || PortalsRoot.Children.Count > 0;

    /// <summary>
    /// Adds a geometry mesh as a child of b_levelroot.
    /// </summary>
    public SceneNode AddGeometryMesh(MeshObject mesh)
    {
        var node = new SceneNode
        {
            Name = mesh.Name,
            Type = SceneNodeType.Mesh,
            Mesh = mesh
        };
        LevelRoot.Children.Add(node);
        return node;
    }

    /// <summary>
    /// Adds a portal mesh as a child of z_portals.
    /// </summary>
    public SceneNode AddPortalMesh(MeshObject portalMesh)
    {
        var node = new SceneNode
        {
            Name = portalMesh.Name,
            Type = SceneNodeType.Portal,
            Mesh = portalMesh
        };
        PortalsRoot.Children.Add(node);
        return node;
    }

    /// <summary>
    /// Collects all active visible geometry meshes under b_levelroot.
    /// </summary>
    public System.Collections.Generic.List<MeshObject> GetLevelMeshes()
    {
        var meshes = new System.Collections.Generic.List<MeshObject>();
        if (!LevelRoot.IsVisible) return meshes;

        foreach (var child in LevelRoot.Children)
        {
            if (child.IsVisible && child.Mesh != null)
            {
                meshes.Add(child.Mesh);
            }
        }
        return meshes;
    }

    /// <summary>
    /// Collects all active visible portal meshes under z_portals.
    /// </summary>
    public System.Collections.Generic.List<MeshObject> GetPortalMeshes()
    {
        var portals = new System.Collections.Generic.List<MeshObject>();
        if (!PortalsRoot.IsVisible) return portals;

        foreach (var child in PortalsRoot.Children)
        {
            if (child.IsVisible && child.Mesh != null)
            {
                portals.Add(child.Mesh);
            }
        }
        return portals;
    }

    /// <summary>
    /// Clears and resets geometry under b_levelroot.
    /// </summary>
    public void ClearGeometry()
    {
        LevelRoot.Children.Clear();
        PortalsRoot.Children.Clear();
    }

    /// <summary>
    /// Removes a node from anywhere in the scene hierarchy.
    /// </summary>
    public bool RemoveNode(SceneNode target)
    {
        if (target == null) return false;
        return RemoveRecursive(Root, target);
    }

    private static bool RemoveRecursive(SceneNode current, SceneNode target)
    {
        if (current.Children.Remove(target)) return true;
        foreach (var c in current.Children)
        {
            if (RemoveRecursive(c, target)) return true;
        }
        return false;
    }
}
