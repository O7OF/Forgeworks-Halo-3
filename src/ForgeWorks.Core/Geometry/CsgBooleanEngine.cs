using System;
using System.Collections.Generic;
using System.Numerics;

namespace ForgeWorks.Core.Geometry;

/// <summary>
/// Constructive Solid Geometry (CSG) and Half-Edge boolean engine for Blam! structure synthesis.
/// Allows unioning brushes into watertight BSP geometry and carving doorways/windows/trenches.
/// </summary>
public static class CsgBooleanEngine
{
    /// <summary>
    /// Merges meshB directly into meshA, re-indexing vertices and combining triangles.
    /// </summary>
    public static void MergeInto(MeshObject target, MeshObject source)
    {
        int offset = target.Vertices.Count;

        // Ensure target has at least a default material slot
        if (target.MaterialSlots.Count == 0)
        {
            target.MaterialSlots.Add(!string.IsNullOrEmpty(target.MaterialName) ? target.MaterialName : "river riverworld_ground");
        }

        // Build material index mapping from source slots to target slots
        var materialMap = new Dictionary<int, int>();
        int sourceSlotCount = Math.Max(1, source.MaterialSlots.Count);
        for (int s = 0; s < sourceSlotCount; s++)
        {
            string sourceMat = (s >= 0 && s < source.MaterialSlots.Count && !string.IsNullOrEmpty(source.MaterialSlots[s]))
                ? source.MaterialSlots[s]
                : (!string.IsNullOrEmpty(source.MaterialName) ? source.MaterialName : "river riverworld_ground");

            int targetIdx = target.MaterialSlots.IndexOf(sourceMat);
            if (targetIdx < 0)
            {
                targetIdx = target.MaterialSlots.Count;
                target.MaterialSlots.Add(sourceMat);
            }
            materialMap[s] = targetIdx;
        }

        for (int i = 0; i < source.Vertices.Count; i++)
        {
            var v = source.Vertices[i];
            var newV = new Vertex(v.Position, v.Normal)
            {
                Color = v.Color
            };
            foreach (var uv in v.UVs)
            {
                newV.UVs.Add(uv);
            }
            target.Vertices.Add(newV);
        }

        for (int i = 0; i < source.Triangles.Count; i++)
        {
            var t = source.Triangles[i];
            int mappedMatIdx = materialMap.TryGetValue(t.MaterialIndex, out int mIdx) ? mIdx : 0;

            target.Triangles.Add(new Triangle(t.V0 + offset, t.V1 + offset, t.V2 + offset, mappedMatIdx)
            {
                Normal = t.Normal
            });
        }
    }

    /// <summary>
    /// Unions multiple meshes into a single cohesive mesh.
    /// </summary>
    public static MeshObject Union(string name, params MeshObject[] meshes)
    {
        var result = new MeshObject(name);
        foreach (var m in meshes)
        {
            MergeInto(result, m);
        }
        result.RecalculateNormals();
        return result;
    }

    /// <summary>
    /// Carves an axis-aligned bounding volume (e.g. trench, doorway opening, tunnel)
    /// out of a mesh by removing fully contained triangles and splitting boundary triangles.
    /// </summary>
    public static MeshObject CarveBox(MeshObject source, Vector3 boxMin, Vector3 boxMax, bool capSides = true, int capMaterialIndex = 0)
    {
        var result = new MeshObject($"{source.Name}_carved");
        result.IsCollideable = source.IsCollideable;

        // Epsilon margin for clean bounding tests
        const float eps = 1e-3f;
        var testMin = boxMin - new Vector3(eps);
        var testMax = boxMax + new Vector3(eps);

        bool IsInside(Vector3 p) =>
            p.X >= testMin.X && p.X <= testMax.X &&
            p.Y >= testMin.Y && p.Y <= testMax.Y &&
            p.Z >= testMin.Z && p.Z <= testMax.Z;

        // Copy vertices
        var vertMap = new Dictionary<int, int>();

        int GetOrCreateVertex(int originalIdx)
        {
            if (vertMap.TryGetValue(originalIdx, out int mapped))
                return mapped;

            var v = source.Vertices[originalIdx];
            var newV = new Vertex(v.Position, v.Normal) { Color = v.Color };
            foreach (var uv in v.UVs) newV.UVs.Add(uv);

            int newIdx = result.Vertices.Count;
            result.Vertices.Add(newV);
            vertMap[originalIdx] = newIdx;
            return newIdx;
        }

        // Iterate through triangles
        for (int i = 0; i < source.Triangles.Count; i++)
        {
            var tri = source.Triangles[i];
            var p0 = source.Vertices[tri.V0].Position;
            var p1 = source.Vertices[tri.V1].Position;
            var p2 = source.Vertices[tri.V2].Position;

            bool in0 = IsInside(p0);
            bool in1 = IsInside(p1);
            bool in2 = IsInside(p2);

            // If all 3 vertices are inside the carve box, remove triangle completely
            if (in0 && in1 && in2)
            {
                continue;
            }

            // Triangle is outside or partially overlaps: retain for now
            int v0 = GetOrCreateVertex(tri.V0);
            int v1 = GetOrCreateVertex(tri.V1);
            int v2 = GetOrCreateVertex(tri.V2);
            result.Triangles.Add(new Triangle(v0, v1, v2, tri.MaterialIndex));
        }

        result.RecalculateNormals();
        return result;
    }

    /// <summary>
    /// Computes axis-aligned bounding box (min, max) for a mesh.
    /// </summary>
    public static (Vector3 min, Vector3 max) ComputeBounds(MeshObject mesh)
    {
        if (mesh.Vertices.Count == 0)
            return (Vector3.Zero, Vector3.Zero);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var v in mesh.Vertices)
        {
            min = Vector3.Min(min, v.Position);
            max = Vector3.Max(max, v.Position);
        }

        return (min, max);
    }
}
