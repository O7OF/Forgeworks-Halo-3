using System;
using System.Collections.Generic;
using System.Numerics;

namespace ForgeWorks.Core.Geometry;

public enum SmoothMode
{
    All,
    MountainTops,
    Cliffs,
    Floor
}

/// <summary>
/// Halo Geometry Smoother & Laplacian Elevation Filter.
/// Direct C# port and enhancement of the authentic 'modules/smoother.py'.
/// Smooths terrain elevation, mountain peaks, and cliff walls to eliminate jagged
/// triangular facets, harsh stepped terraces, and sawteeth while preserving airtight sealed boundaries.
/// </summary>
public static class GeometrySmoother
{
    /// <summary>
    /// Smooths terrain or world geometry mesh using Laplacian vertex relaxation.
    /// Preserves sealed boundary planes (min_x, max_x, min_y, max_y) to ensure the BSP remains 100% airtight.
    /// </summary>
    public static int SmoothTerrainMesh(
        MeshObject mesh,
        int passes = 3,
        float strength = 0.45f,
        SmoothMode smoothMode = SmoothMode.All,
        bool lockBoundaries = true)
    {
        if (mesh == null || mesh.Vertices.Count == 0 || mesh.Triangles.Count == 0)
            return 0;

        // 1. Build vertex adjacency graph from triangles
        var adj = new Dictionary<int, HashSet<int>>();
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            AddAdjacency(adj, tri.V0, tri.V1);
            AddAdjacency(adj, tri.V1, tri.V2);
            AddAdjacency(adj, tri.V2, tri.V0);
        }

        // 2. Identify ceiling vertices to prevent pulling horizontal +sky ceilings downwards
        var ceilingVerts = new HashSet<int>();
        float maxAllZ = float.MinValue;
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            if (mesh.Vertices[i].Position.Z > maxAllZ)
                maxAllZ = mesh.Vertices[i].Position.Z;
        }

        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            string matName = (tri.MaterialIndex >= 0 && tri.MaterialIndex < mesh.MaterialSlots.Count)
                ? mesh.MaterialSlots[tri.MaterialIndex]
                : mesh.MaterialName;

            if (matName.StartsWith("+sky", StringComparison.OrdinalIgnoreCase))
            {
                var p0 = mesh.Vertices[tri.V0].Position;
                var p1 = mesh.Vertices[tri.V1].Position;
                var p2 = mesh.Vertices[tri.V2].Position;
                var cross = Vector3.Cross(p1 - p0, p2 - p0);
                if (cross.LengthSquared() > 1e-6f)
                {
                    var normal = Vector3.Normalize(cross);
                    float triCenterZ = (p0.Z + p1.Z + p2.Z) / 3.0f;
                    // Downward ceiling normal (Z < -0.85) at ceiling elevation
                    if (normal.Z < -0.85f && MathF.Abs(triCenterZ - maxAllZ) < 15.0f)
                    {
                        ceilingVerts.Add(tri.V0);
                        ceilingVerts.Add(tri.V1);
                        ceilingVerts.Add(tri.V2);
                    }
                }
            }
        }

        // 3. Find bounding box of terrain vertices
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        var candidateVerts = new List<int>();
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            if (ceilingVerts.Contains(i)) continue;
            var pos = mesh.Vertices[i].Position;
            if (pos.X < minX) minX = pos.X;
            if (pos.X > maxX) maxX = pos.X;
            if (pos.Y < minY) minY = pos.Y;
            if (pos.Y > maxY) maxY = pos.Y;
            if (pos.Z < minZ) minZ = pos.Z;
            if (pos.Z > maxZ) maxZ = pos.Z;
            candidateVerts.Add(i);
        }

        if (candidateVerts.Count == 0) return 0;
        float zSpan = MathF.Max(1.0f, maxZ - minZ);

        // 4. Select target vertex subset according to SmoothMode
        var targetSet = new HashSet<int>();
        foreach (int i in candidateVerts)
        {
            var v = mesh.Vertices[i];
            float relZ = (v.Position.Z - minZ) / zSpan;
            float normalZ = Math.Clamp(v.Normal.Z, -1.0f, 1.0f);
            float slopeDeg = MathF.Acos(normalZ) * (180.0f / MathF.PI);

            switch (smoothMode)
            {
                case SmoothMode.MountainTops:
                    // Upper 38% elevation or steep ridges
                    if (relZ >= 0.38f || (relZ >= 0.25f && slopeDeg > 35.0f))
                        targetSet.Add(i);
                    break;
                case SmoothMode.Cliffs:
                    // Steep cliff walls (> 22 degrees slope)
                    if (slopeDeg >= 22.0f)
                        targetSet.Add(i);
                    break;
                case SmoothMode.Floor:
                    // Low valley floor and riverbed
                    if (relZ <= 0.35f && slopeDeg < 25.0f)
                        targetSet.Add(i);
                    break;
                default:
                    targetSet.Add(i);
                    break;
            }
        }

        if (targetSet.Count == 0) return 0;

        float boundMargin = 2.0f;

        // 5. Run Laplacian relaxation iterations
        for (int p = 0; p < passes; p++)
        {
            var newPositions = new Dictionary<int, Vector3>(targetSet.Count);

            foreach (int i in targetSet)
            {
                if (!adj.TryGetValue(i, out var neighbors) || neighbors.Count == 0)
                    continue;

                Vector3 sum = Vector3.Zero;
                int validNeighborCount = 0;
                foreach (int n in neighbors)
                {
                    if (ceilingVerts.Contains(n)) continue;
                    sum += mesh.Vertices[n].Position;
                    validNeighborCount++;
                }

                if (validNeighborCount == 0) continue;
                Vector3 avgPos = sum / (float)validNeighborCount;
                var currentPos = mesh.Vertices[i].Position;

                // Check if vertex lies on outer bounding box plane
                bool onMinX = MathF.Abs(currentPos.X - minX) < boundMargin;
                bool onMaxX = MathF.Abs(currentPos.X - maxX) < boundMargin;
                bool onMinY = MathF.Abs(currentPos.Y - minY) < boundMargin;
                bool onMaxY = MathF.Abs(currentPos.Y - maxY) < boundMargin;
                bool isBoundary = onMinX || onMaxX || onMinY || onMaxY;

                // Elevation relaxation: smoothly harmonizes Z towards neighbor average
                float weight = strength;
                if (smoothMode == SmoothMode.MountainTops)
                {
                    float relZ = (currentPos.Z - minZ) / zSpan;
                    weight = MathF.Min(0.85f, strength * (1.0f + relZ * 0.5f));
                }

                float targetZ = currentPos.Z + weight * (avgPos.Z - currentPos.Z);
                float targetX, targetY;

                // Planar constraint enforcement:
                // If vertex is on a bounding plane, lock its coordinate on that plane so the sealed box remains 100% airtight!
                if (isBoundary && lockBoundaries)
                {
                    targetX = onMinX ? minX : (onMaxX ? maxX : currentPos.X);
                    targetY = onMinY ? minY : (onMaxY ? maxY : currentPos.Y);
                }
                else
                {
                    // Tangential relaxation removes zigzag triangular pleats on steep walls
                    float tangentFactor = strength * 0.35f;
                    targetX = currentPos.X + tangentFactor * (avgPos.X - currentPos.X);
                    targetY = currentPos.Y + tangentFactor * (avgPos.Y - currentPos.Y);
                }

                newPositions[i] = new Vector3(targetX, targetY, targetZ);
            }

            foreach (var kvp in newPositions)
            {
                var v = mesh.Vertices[kvp.Key];
                v.Position = kvp.Value;
            }
        }

        // 6. Recalculate smooth normals across entire mesh
        mesh.RecalculateNormals();
        return targetSet.Count;
    }

    /// <summary>
    /// Smooths any general mesh object (props, structures, terrain) with boundary edge preservation.
    /// </summary>
    public static int SmoothGeneralMesh(MeshObject mesh, int passes = 2, float strength = 0.35f)
    {
        if (mesh == null || mesh.Vertices.Count == 0 || mesh.Triangles.Count == 0)
            return 0;

        // Find boundary edges
        var edgeCount = new Dictionary<(int, int), int>();
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            void AddE(int u, int v)
            {
                var k = u < v ? (u, v) : (v, u);
                edgeCount[k] = edgeCount.GetValueOrDefault(k, 0) + 1;
            }
            AddE(tri.V0, tri.V1);
            AddE(tri.V1, tri.V2);
            AddE(tri.V2, tri.V0);
        }

        var boundaryVerts = new HashSet<int>();
        foreach (var kvp in edgeCount)
        {
            if (kvp.Value == 1)
            {
                boundaryVerts.Add(kvp.Key.Item1);
                boundaryVerts.Add(kvp.Key.Item2);
            }
        }

        var adj = new Dictionary<int, HashSet<int>>();
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            AddAdjacency(adj, tri.V0, tri.V1);
            AddAdjacency(adj, tri.V1, tri.V2);
            AddAdjacency(adj, tri.V2, tri.V0);
        }

        int smoothed = 0;
        for (int p = 0; p < passes; p++)
        {
            var newPositions = new Dictionary<int, Vector3>();
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                // Lock boundary vertices to avoid unsealing edges
                if (boundaryVerts.Contains(i)) continue;
                if (!adj.TryGetValue(i, out var neighbors) || neighbors.Count == 0) continue;

                Vector3 sum = Vector3.Zero;
                foreach (int n in neighbors) sum += mesh.Vertices[n].Position;
                Vector3 avg = sum / neighbors.Count;

                newPositions[i] = Vector3.Lerp(mesh.Vertices[i].Position, avg, Math.Clamp(strength, 0.05f, 0.8f));
                smoothed++;
            }

            foreach (var kvp in newPositions)
            {
                mesh.Vertices[kvp.Key].Position = kvp.Value;
            }
        }

        mesh.RecalculateNormals();
        return smoothed;
    }

    private static void AddAdjacency(Dictionary<int, HashSet<int>> adj, int a, int b)
    {
        if (!adj.TryGetValue(a, out var setA))
        {
            setA = new HashSet<int>();
            adj[a] = setA;
        }
        setA.Add(b);

        if (!adj.TryGetValue(b, out var setB))
        {
            setB = new HashSet<int>();
            adj[b] = setB;
        }
        setB.Add(a);
    }
}
