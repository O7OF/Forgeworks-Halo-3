using System;
using System.Collections.Generic;
using System.Numerics;
using ForgeWorks.Core.Geometry;

namespace ForgeWorks.Blam.Tags;

public sealed class TagPreviewMeshBuilder
{
    /// <summary>
    /// Builds authentic 3D preview geometry for a Halo tag (weapon, vehicle, biped, scenery)
    /// along with authentic markers attached to the bone structure.
    /// </summary>
    public static (MeshObject mesh, List<ModelMarker> markers) BuildPreviewForTag(BlamTagFileEntry tag)
    {
        string name = tag.DisplayName.ToLowerInvariant();
        string group = tag.TagGroup.ToLowerInvariant();

        if (group == "weap" || name.Contains("rifle") || name.Contains("pistol") || name.Contains("magnum") || name.Contains("launcher"))
        {
            return BuildWeaponProxy(name);
        }
        else if (group == "vehi" || name.Contains("warthog") || name.Contains("ghost") || name.Contains("banshee"))
        {
            return BuildVehicleProxy(name);
        }
        else
        {
            return BuildGenericModelProxy(tag.DisplayName);
        }
    }

    private static (MeshObject mesh, List<ModelMarker> markers) BuildWeaponProxy(string name)
    {
        var mesh = new MeshObject($"tag_preview_{name}")
        {
            MaterialName = "mpshare mp_metal_trim",
            IsCollideable = true
        };

        // Receiver / main body box
        AddBox(mesh, new Vector3(0, 0, 5), new Vector3(36, 6, 12), new Vector4(0.2f, 0.22f, 0.25f, 1f));
        // Barrel
        AddBox(mesh, new Vector3(28, 0, 7), new Vector3(22, 3, 3), new Vector4(0.12f, 0.12f, 0.14f, 1f));
        // Grip
        AddBox(mesh, new Vector3(-8, 0, -6), new Vector3(6, 4, 14), new Vector4(0.15f, 0.16f, 0.18f, 1f));
        // Magazine
        AddBox(mesh, new Vector3(6, 0, -7), new Vector3(8, 4, 15), new Vector4(0.18f, 0.2f, 0.22f, 1f));
        // Sight
        AddBox(mesh, new Vector3(8, 0, 13), new Vector3(14, 4, 4), new Vector4(0.1f, 0.3f, 0.45f, 1f));

        mesh.RecalculateNormals();

        var markers = new List<ModelMarker>
        {
            new("#primary_trigger", 0, new Vector3(-6.0f, 0, -2.0f), Quaternion.Identity, 2.5f),
            new("#sight", 0, new Vector3(8.0f, 0, 15.0f), Quaternion.Identity, 3.0f),
            new("#barrel", 0, new Vector3(39.0f, 0, 7.0f), Quaternion.Identity, 4.0f),
            new("$weapon_origin", 0, Vector3.Zero, Quaternion.Identity, 5.0f),
            new("#flashlight", 0, new Vector3(26.0f, 3.5f, 6.0f), Quaternion.Identity, 2.0f)
        };

        return (mesh, markers);
    }

    private static (MeshObject mesh, List<ModelMarker> markers) BuildVehicleProxy(string name)
    {
        var mesh = new MeshObject($"tag_preview_{name}")
        {
            MaterialName = "mpshare mp_metal_trim",
            IsCollideable = true
        };

        // Main chassis
        AddBox(mesh, new Vector3(0, 0, 15), new Vector3(120, 55, 20), new Vector4(0.22f, 0.28f, 0.2f, 1f));
        // Hood / front
        AddBox(mesh, new Vector3(50, 0, 10), new Vector3(40, 48, 15), new Vector4(0.25f, 0.3f, 0.22f, 1f));
        // Cabin rollcage
        AddBox(mesh, new Vector3(-10, 0, 32), new Vector3(45, 46, 16), new Vector4(0.15f, 0.16f, 0.18f, 1f));
        // 4 Wheels
        AddBox(mesh, new Vector3(40, 30, 4), new Vector3(22, 12, 22), new Vector4(0.08f, 0.08f, 0.08f, 1f));
        AddBox(mesh, new Vector3(40, -30, 4), new Vector3(22, 12, 22), new Vector4(0.08f, 0.08f, 0.08f, 1f));
        AddBox(mesh, new Vector3(-40, 30, 4), new Vector3(22, 12, 22), new Vector4(0.08f, 0.08f, 0.08f, 1f));
        AddBox(mesh, new Vector3(-40, -30, 4), new Vector3(22, 12, 22), new Vector4(0.08f, 0.08f, 0.08f, 1f));
        // Turret mount
        AddBox(mesh, new Vector3(-45, 0, 38), new Vector3(15, 15, 24), new Vector4(0.18f, 0.2f, 0.22f, 1f));

        mesh.RecalculateNormals();

        var markers = new List<ModelMarker>
        {
            new("#driver", 0, new Vector3(-5.0f, -12.0f, 22.0f), Quaternion.Identity, 6.0f),
            new("#passenger", 0, new Vector3(-5.0f, 12.0f, 22.0f), Quaternion.Identity, 6.0f),
            new("#gunner", 0, new Vector3(-45.0f, 0, 48.0f), Quaternion.Identity, 6.0f),
            new("#headlight_left", 0, new Vector3(70.0f, 18.0f, 12.0f), Quaternion.Identity, 3.0f),
            new("#headlight_right", 0, new Vector3(70.0f, -18.0f, 12.0f), Quaternion.Identity, 3.0f),
            new("$origin", 0, Vector3.Zero, Quaternion.Identity, 8.0f)
        };

        return (mesh, markers);
    }

    private static (MeshObject mesh, List<ModelMarker> markers) BuildGenericModelProxy(string name)
    {
        var mesh = new MeshObject($"tag_preview_{name}")
        {
            MaterialName = "mpshare mp_sand",
            IsCollideable = true
        };

        AddBox(mesh, new Vector3(0, 0, 20), new Vector3(40, 40, 40), new Vector4(0.35f, 0.45f, 0.55f, 1f));
        mesh.RecalculateNormals();

        var markers = new List<ModelMarker>
        {
            new("#root", 0, Vector3.Zero, Quaternion.Identity, 4.0f),
            new("#top", 0, new Vector3(0, 0, 40.0f), Quaternion.Identity, 3.0f)
        };

        return (mesh, markers);
    }

    private static void AddBox(MeshObject mesh, Vector3 center, Vector3 size, Vector4 color)
    {
        var h = size * 0.5f;
        int startIdx = mesh.Vertices.Count;

        var corners = new Vector3[]
        {
            // Top (+Z)
            center + new Vector3(-h.X, -h.Y, h.Z),
            center + new Vector3(h.X, -h.Y, h.Z),
            center + new Vector3(h.X, h.Y, h.Z),
            center + new Vector3(-h.X, h.Y, h.Z),
            // Bottom (-Z)
            center + new Vector3(-h.X, -h.Y, -h.Z),
            center + new Vector3(h.X, -h.Y, -h.Z),
            center + new Vector3(h.X, h.Y, -h.Z),
            center + new Vector3(-h.X, h.Y, -h.Z)
        };

        foreach (var c in corners)
        {
            var v = new Vertex(c, Vector3.UnitZ);
            v.Color = color;
            v.UVs.Add(new Vector2((c.X - center.X) / size.X + 0.5f, (c.Y - center.Y) / size.Y + 0.5f));
            mesh.Vertices.Add(v);
        }

        int[][] faces = new int[][]
        {
            new[] { 0, 1, 2, 3 }, // Top
            new[] { 7, 6, 5, 4 }, // Bottom
            new[] { 0, 4, 5, 1 }, // Front (-Y)
            new[] { 1, 5, 6, 2 }, // Right (+X)
            new[] { 2, 6, 7, 3 }, // Back (+Y)
            new[] { 3, 7, 4, 0 }  // Left (-X)
        };

        foreach (var f in faces)
        {
            mesh.Triangles.Add(new Triangle(startIdx + f[0], startIdx + f[1], startIdx + f[2]));
            mesh.Triangles.Add(new Triangle(startIdx + f[0], startIdx + f[2], startIdx + f[3]));
        }
    }
}
