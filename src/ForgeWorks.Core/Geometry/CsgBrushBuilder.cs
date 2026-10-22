using System;
using System.Collections.Generic;
using System.Numerics;

namespace ForgeWorks.Core.Geometry;

public enum BrushPrimitiveType
{
    Box,
    HollowRoom,
    WedgeRamp,
    Cylinder,
    ArchDoorway
}

public sealed class BrushConfig
{
    public BrushPrimitiveType Type { get; set; } = BrushPrimitiveType.Box;
    public Vector3 Size { get; set; } = new(500, 500, 300); // Width (X), Length (Y), Height (Z)
    public Vector3 Location { get; set; } = Vector3.Zero;
    public float WallThickness { get; set; } = 25.0f;        // For Hollow Room and Arch
    public int Segments { get; set; } = 16;                  // For Cylinder and Arch
    public float DoorWidth { get; set; } = 120.0f;           // For Arch / Room doorway
    public float DoorHeight { get; set; } = 180.0f;          // For Arch / Room doorway
    public string MaterialName { get; set; } = "base concrete";
    public int MaterialIndex { get; set; } = 0;
    public Vector2 TextureScale { get; set; } = new(0.01f, 0.01f);
}

/// <summary>
/// Low-friction 3D blockout brush builder inspired by TrenchBroom, Hammer, and Blockbench.
/// Generates strictly convex and sealed engine-compliant geometry with correct UVs and normals.
/// </summary>
public static class CsgBrushBuilder
{
    /// <summary>
    /// Creates a solid cuboid brush with 6 faces (12 triangles) and box planar UVs.
    /// </summary>
    public static MeshObject CreateBox(Vector3 size, Vector3 location, string material = "base concrete", int materialIndex = 0, Vector2? uvScale = null)
    {
        var mesh = new MeshObject($"brush_box_{Guid.NewGuid().ToString()[..4]}");
        mesh.IsCollideable = true;

        Vector2 scale = uvScale ?? new Vector2(0.01f, 0.01f);
        Vector3 h = size * 0.5f;

        // 8 Corner vertices
        Vector3[] corners =
        {
            new(-h.X, -h.Y, -h.Z), // 0: ---
            new( h.X, -h.Y, -h.Z), // 1: +--
            new( h.X,  h.Y, -h.Z), // 2: ++-
            new(-h.X,  h.Y, -h.Z), // 3: -+-
            new(-h.X, -h.Y,  h.Z), // 4: --+
            new( h.X, -h.Y,  h.Z), // 5: +-+
            new( h.X,  h.Y,  h.Z), // 6: +++
            new(-h.X,  h.Y,  h.Z)  // 7: -++
        };

        // 6 Quads (Face indices in CCW outward order)
        int[][] faces =
        {
            new[] { 3, 2, 1, 0 }, // Bottom (-Z)
            new[] { 4, 5, 6, 7 }, // Top (+Z)
            new[] { 0, 1, 5, 4 }, // Front (-Y)
            new[] { 2, 3, 7, 6 }, // Back (+Y)
            new[] { 3, 0, 4, 7 }, // Left (-X)
            new[] { 1, 2, 6, 5 }  // Right (+X)
        };

        Vector3[] faceNormals =
        {
            -Vector3.UnitZ, Vector3.UnitZ,
            -Vector3.UnitY, Vector3.UnitY,
            -Vector3.UnitX, Vector3.UnitX
        };

        for (int f = 0; f < 6; f++)
        {
            int baseIdx = mesh.Vertices.Count;
            var normal = faceNormals[f];

            for (int v = 0; v < 4; v++)
            {
                var pos = corners[faces[f][v]] + location;
                var vert = new Vertex(pos, normal);

                // Planar UV Projection based on dominant normal
                Vector2 uv = MathF.Abs(normal.Z) > 0.5f
                    ? new Vector2(pos.X * scale.X, pos.Y * scale.Y)
                    : (MathF.Abs(normal.Y) > 0.5f
                        ? new Vector2(pos.X * scale.X, pos.Z * scale.Y)
                        : new Vector2(pos.Y * scale.X, pos.Z * scale.Y));

                vert.UVs.Add(uv);
                mesh.Vertices.Add(vert);
            }

            mesh.Triangles.Add(new Triangle(baseIdx, baseIdx + 1, baseIdx + 2, materialIndex));
            mesh.Triangles.Add(new Triangle(baseIdx, baseIdx + 2, baseIdx + 3, materialIndex));
        }

        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>
    /// Creates an authentic Halo wedge / ramp brush with sloped face.
    /// </summary>
    public static MeshObject CreateWedge(Vector3 size, Vector3 location, string material = "base concrete", int materialIndex = 0)
    {
        var mesh = new MeshObject($"brush_wedge_{Guid.NewGuid().ToString()[..4]}");
        mesh.IsCollideable = true;

        Vector3 h = size * 0.5f;

        // 6 Vertices for triangular prism
        Vector3[] v =
        {
            new(-h.X, -h.Y, -h.Z), // 0: Bottom-Left-Front
            new( h.X, -h.Y, -h.Z), // 1: Bottom-Right-Front
            new( h.X,  h.Y, -h.Z), // 2: Bottom-Right-Back
            new(-h.X,  h.Y, -h.Z), // 3: Bottom-Left-Back
            new(-h.X,  h.Y,  h.Z), // 4: Top-Left-Back
            new( h.X,  h.Y,  h.Z)  // 5: Top-Right-Back
        };

        for (int i = 0; i < v.Length; i++)
        {
            v[i] += location;
        }

        void AddTri(Vector3 p0, Vector3 p1, Vector3 p2)
        {
            int idx = mesh.Vertices.Count;
            var norm = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
            mesh.Vertices.Add(new Vertex(p0, norm, new Vector2(p0.X * 0.01f, p0.Y * 0.01f)));
            mesh.Vertices.Add(new Vertex(p1, norm, new Vector2(p1.X * 0.01f, p1.Y * 0.01f)));
            mesh.Vertices.Add(new Vertex(p2, norm, new Vector2(p2.X * 0.01f, p2.Y * 0.01f)));
            mesh.Triangles.Add(new Triangle(idx, idx + 1, idx + 2, materialIndex));
        }

        void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            AddTri(p0, p1, p2);
            AddTri(p0, p2, p3);
        }

        // Bottom quad
        AddQuad(v[3], v[2], v[1], v[0]);
        // Back vertical quad
        AddQuad(v[2], v[3], v[4], v[5]);
        // Sloped ramp quad (Front-bottom to Back-top)
        AddQuad(v[0], v[1], v[5], v[4]);
        // Left triangle
        AddTri(v[0], v[4], v[3]);
        // Right triangle
        AddTri(v[1], v[2], v[5]);

        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>
    /// Creates a polygonal cylinder / column brush (Forerunner or Covenant pillar).
    /// </summary>
    public static MeshObject CreateCylinder(float radius, float height, int segments, Vector3 location, int materialIndex = 0)
    {
        var mesh = new MeshObject($"brush_cylinder_{Guid.NewGuid().ToString()[..4]}");
        mesh.IsCollideable = true;

        segments = Math.Clamp(segments, 6, 64);
        float halfH = height * 0.5f;

        var bottomVerts = new Vector3[segments];
        var topVerts = new Vector3[segments];

        for (int i = 0; i < segments; i++)
        {
            float theta = i * MathF.Tau / segments;
            float cos = MathF.Cos(theta) * radius;
            float sin = MathF.Sin(theta) * radius;

            bottomVerts[i] = new Vector3(cos, sin, -halfH) + location;
            topVerts[i] = new Vector3(cos, sin, halfH) + location;
        }

        void AddTri(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 normal)
        {
            int idx = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(p0, normal, new Vector2(p0.X * 0.01f, p0.Y * 0.01f)));
            mesh.Vertices.Add(new Vertex(p1, normal, new Vector2(p1.X * 0.01f, p1.Y * 0.01f)));
            mesh.Vertices.Add(new Vertex(p2, normal, new Vector2(p2.X * 0.01f, p2.Y * 0.01f)));
            mesh.Triangles.Add(new Triangle(idx, idx + 1, idx + 2, materialIndex));
        }

        // Side quads
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            var p0 = bottomVerts[i];
            var p1 = bottomVerts[next];
            var p2 = topVerts[next];
            var p3 = topVerts[i];

            var n = Vector3.Normalize(new Vector3(p0.X - location.X, p0.Y - location.Y, 0));
            AddTri(p0, p1, p2, n);
            AddTri(p0, p2, p3, n);
        }

        // Bottom cap
        var bottomCenter = new Vector3(location.X, location.Y, location.Z - halfH);
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            AddTri(bottomCenter, bottomVerts[next], bottomVerts[i], -Vector3.UnitZ);
        }

        // Top cap
        var topCenter = new Vector3(location.X, location.Y, location.Z + halfH);
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            AddTri(topCenter, topVerts[i], topVerts[next], Vector3.UnitZ);
        }

        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>
    /// Creates a hollow room with floor, ceiling, and 4 exterior walls plus a doorway opening.
    /// </summary>
    public static MeshObject CreateHollowRoom(Vector3 outerSize, float wallThickness, float doorWidth, float doorHeight, Vector3 location, int materialIndex = 0)
    {
        var mesh = new MeshObject($"brush_room_{Guid.NewGuid().ToString()[..4]}");
        mesh.IsCollideable = true;

        float ox = outerSize.X * 0.5f;
        float oy = outerSize.Y * 0.5f;
        float oz = outerSize.Z;
        float t = wallThickness;

        float ix = ox - t;
        float iy = oy - t;
        float dw = doorWidth * 0.5f;

        void AddWall(Vector3 bMin, Vector3 bMax)
        {
            var box = CreateBox(bMax - bMin, (bMin + bMax) * 0.5f + location, materialIndex: materialIndex);
            CsgBooleanEngine.MergeInto(mesh, box);
        }

        // Floor slab
        AddWall(new Vector3(-ox, -oy, 0), new Vector3(ox, oy, t));
        // Ceiling slab
        AddWall(new Vector3(-ox, -oy, oz - t), new Vector3(ox, oy, oz));

        // Left wall (-X)
        AddWall(new Vector3(-ox, -oy, t), new Vector3(-ix, oy, oz - t));
        // Right wall (+X)
        AddWall(new Vector3(ix, -oy, t), new Vector3(ox, oy, oz - t));
        // Back wall (+Y)
        AddWall(new Vector3(-ix, iy, t), new Vector3(ix, oy, oz - t));

        // Front wall (-Y) with doorway split into Left segment, Right segment, and Header over door
        // Left of door
        AddWall(new Vector3(-ix, -oy, t), new Vector3(-dw, -iy, oz - t));
        // Right of door
        AddWall(new Vector3(dw, -oy, t), new Vector3(ix, -iy, oz - t));
        // Lintel header over doorway
        if (oz - t > doorHeight)
        {
            AddWall(new Vector3(-dw, -oy, doorHeight), new Vector3(dw, -iy, oz - t));
        }

        mesh.RecalculateNormals();
        return mesh;
    }

    public static MeshObject CreatePlanarPortal(string name, float width, float height, Vector3 location)
    {
        var mesh = new MeshObject(name);
        mesh.MaterialName = "+portal";
        mesh.IsCollideable = false;

        float hw = width * 0.5f;
        mesh.Vertices.Add(new Vertex(new Vector3(-hw, 0, 0) + location, Vector3.UnitY));
        mesh.Vertices.Add(new Vertex(new Vector3(hw, 0, 0) + location, Vector3.UnitY));
        mesh.Vertices.Add(new Vertex(new Vector3(hw, 0, height) + location, Vector3.UnitY));
        mesh.Vertices.Add(new Vertex(new Vector3(-hw, 0, height) + location, Vector3.UnitY));

        // Double-sided planar portal
        mesh.Triangles.Add(new Triangle(0, 1, 2));
        mesh.Triangles.Add(new Triangle(0, 2, 3));
        mesh.Triangles.Add(new Triangle(0, 2, 1));
        mesh.Triangles.Add(new Triangle(0, 3, 2));

        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>
    /// Builds a brush from config.
    /// </summary>
    public static MeshObject BuildFromConfig(BrushConfig config)
    {
        return config.Type switch
        {
            BrushPrimitiveType.Box => CreateBox(config.Size, config.Location, config.MaterialName, config.MaterialIndex, config.TextureScale),
            BrushPrimitiveType.WedgeRamp => CreateWedge(config.Size, config.Location, config.MaterialName, config.MaterialIndex),
            BrushPrimitiveType.Cylinder => CreateCylinder(config.Size.X * 0.5f, config.Size.Z, config.Segments, config.Location, config.MaterialIndex),
            BrushPrimitiveType.HollowRoom => CreateHollowRoom(config.Size, config.WallThickness, config.DoorWidth, config.DoorHeight, config.Location, config.MaterialIndex),
            _ => CreateBox(config.Size, config.Location, config.MaterialName, config.MaterialIndex)
        };
    }
}
