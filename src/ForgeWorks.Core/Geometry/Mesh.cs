using System;
using System.Collections.Generic;
using System.Numerics;

namespace ForgeWorks.Core.Geometry;

public sealed class Vertex
{
    public Vector3 Position { get; set; }
    public Vector3 Normal { get; set; }
    public Vector4 Color { get; set; } = new(1f, 1f, 1f, 1f); // Terrain_Layers (R, G, B, A weights)
    public List<Vector2> UVs { get; set; } = new();

    public Vertex() { }

    public Vertex(Vector3 position, Vector3 normal, Vector2? uv = null)
    {
        Position = position;
        Normal = normal;
        if (uv.HasValue)
        {
            UVs.Add(uv.Value);
        }
    }
}

public sealed class Triangle
{
    public int V0 { get; set; }
    public int V1 { get; set; }
    public int V2 { get; set; }
    public int MaterialIndex { get; set; }
    public Vector3 Normal { get; set; }

    public Triangle(int v0, int v1, int v2, int materialIndex = 0)
    {
        V0 = v0;
        V1 = v1;
        V2 = v2;
        MaterialIndex = materialIndex;
    }
}

public sealed class MeshObject
{
    public string Name { get; set; } = "unnamed_object";
    public string MaterialName { get; set; } = "default";
    public List<string> MaterialSlots { get; set; } = new();
    public List<Vertex> Vertices { get; set; } = new();
    public List<Triangle> Triangles { get; set; } = new();
    public bool IsCollideable { get; set; } = true;
    public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;
    public int GridResolutionX { get; set; }
    public int GridResolutionY { get; set; }

    public MeshObject() { }

    public MeshObject(string name)
    {
        Name = name;
    }

    public bool CheckIsCollideable()
    {
        if (MaterialName.StartsWith("!") || MaterialName.StartsWith("%!") || MaterialName.EndsWith("!") || MaterialName.StartsWith("+portal") || MaterialName.StartsWith("+unused") || MaterialName.StartsWith("+fog") || MaterialName.EndsWith("$") || Name.EndsWith("!"))
            return false;
        return IsCollideable;
    }

    public bool IsTriangleCollideable(Triangle tri)
    {
        if (!CheckIsCollideable()) return false;
        string? mName = null;
        if (tri.MaterialIndex >= 0 && tri.MaterialIndex < MaterialSlots.Count)
        {
            mName = MaterialSlots[tri.MaterialIndex];
        }
        if (string.IsNullOrEmpty(mName))
        {
            mName = MaterialName;
        }
        return IsMaterialCollideable(mName);
    }

    public static bool IsMaterialCollideable(string? materialName)
    {
        if (string.IsNullOrWhiteSpace(materialName)) return true;
        var m = materialName.Trim();
        if (m.StartsWith("!") || m.StartsWith("%!") || m.EndsWith("!") || m.EndsWith("$"))
            return false;
        if (m.StartsWith("+portal", StringComparison.OrdinalIgnoreCase) ||
            m.StartsWith("+unused", StringComparison.OrdinalIgnoreCase) ||
            m.StartsWith("+fog", StringComparison.OrdinalIgnoreCase))
            return false;
        if (m.StartsWith("%") && (m.Contains("leaf", StringComparison.OrdinalIgnoreCase) || m.Contains("foliage", StringComparison.OrdinalIgnoreCase) || m.Contains("bush", StringComparison.OrdinalIgnoreCase) || m.Contains("grass", StringComparison.OrdinalIgnoreCase)))
            return false;
        return true;
    }

    public void RecalculateNormals()
    {
        for (int i = 0; i < Triangles.Count; i++)
        {
            var tri = Triangles[i];
            var p0 = Vertices[tri.V0].Position;
            var p1 = Vertices[tri.V1].Position;
            var p2 = Vertices[tri.V2].Position;

            var normal = Vector3.Cross(p1 - p0, p2 - p0);
            float len = normal.Length();
            if (len > 1e-6f)
            {
                tri.Normal = normal / len;
            }
            else
            {
                tri.Normal = Vector3.UnitZ;
            }
        }

        // Compute vertex normals by accumulating face normals
        var vertNormals = new Vector3[Vertices.Count];
        for (int i = 0; i < Triangles.Count; i++)
        {
            var tri = Triangles[i];
            vertNormals[tri.V0] += tri.Normal;
            vertNormals[tri.V1] += tri.Normal;
            vertNormals[tri.V2] += tri.Normal;
        }

        for (int i = 0; i < Vertices.Count; i++)
        {
            float len = vertNormals[i].Length();
            if (len > 1e-6f)
            {
                Vertices[i].Normal = vertNormals[i] / len;
            }
        }
    }

    public void GetBounds(out Vector3 min, out Vector3 max)
    {
        if (Vertices == null || Vertices.Count == 0)
        {
            min = Vector3.Zero;
            max = Vector3.Zero;
            return;
        }

        min = new Vector3(float.MaxValue);
        max = new Vector3(float.MinValue);

        for (int i = 0; i < Vertices.Count; i++)
        {
            var p = Vertices[i].Position;
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
    }
}
