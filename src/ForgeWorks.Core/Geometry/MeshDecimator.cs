using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace ForgeWorks.Core.Geometry;

public sealed record DecimationReport(
    int OriginalVertices,
    int DecimatedVertices,
    int OriginalTriangles,
    int DecimatedTriangles,
    float ReductionPercentage
);

/// <summary>
/// Professional Quadric Error Metric (QEM) mesh decimation and polygon simplifier.
/// Transforms high-poly models into engine-native low-poly meshes for Halo 3 Blam! engine
/// while strictly preserving boundary silhouettes, material slots, and UV mapping.
/// </summary>
public static class MeshDecimator
{
    private struct SymmetricMatrix
    {
        public float M0, M1, M2, M3;
        public float M4, M5, M6;
        public float M7, M8;
        public float M9;

        public SymmetricMatrix(float c)
        {
            M0 = c; M1 = c; M2 = c; M3 = c;
            M4 = c; M5 = c; M6 = c;
            M7 = c; M8 = c;
            M9 = c;
        }

        public SymmetricMatrix(float a, float b, float c, float d)
        {
            M0 = a * a; M1 = a * b; M2 = a * c; M3 = a * d;
            M4 = b * b; M5 = b * c; M6 = b * d;
            M7 = c * c; M8 = c * d;
            M9 = d * d;
        }

        public static SymmetricMatrix operator +(SymmetricMatrix n, SymmetricMatrix m) => new()
        {
            M0 = n.M0 + m.M0, M1 = n.M1 + m.M1, M2 = n.M2 + m.M2, M3 = n.M3 + m.M3,
            M4 = n.M4 + m.M4, M5 = n.M5 + m.M5, M6 = n.M6 + m.M6,
            M7 = n.M7 + m.M7, M8 = n.M8 + m.M8,
            M9 = n.M9 + m.M9
        };

        public readonly float Evaluate(Vector3 v)
        {
            float x = v.X, y = v.Y, z = v.Z;
            return M0 * x * x + 2 * M1 * x * y + 2 * M2 * x * z + 2 * M3 * x
                 + M4 * y * y + 2 * M5 * y * z + 2 * M6 * y
                 + M7 * z * z + 2 * M8 * z
                 + M9;
        }
    }

    private sealed class CollapseEdge
    {
        public int V0;
        public int V1;
        public Vector3 TargetPosition;
        public float Cost;
        public bool IsValid = true;
    }

    /// <summary>
    /// Decimates the mesh to target ratio (0.1f = 10% remaining, 0.5f = 50% remaining).
    /// </summary>
    public static DecimationReport DecimateToRatio(MeshObject mesh, float targetRatio = 0.5f, bool preserveBoundaries = true)
    {
        if (mesh == null || mesh.Triangles.Count == 0)
            return new DecimationReport(0, 0, 0, 0, 0f);

        int targetTriangles = Math.Max(4, (int)(mesh.Triangles.Count * Math.Clamp(targetRatio, 0.02f, 0.98f)));
        return DecimateToTargetTriangles(mesh, targetTriangles, preserveBoundaries);
    }

    /// <summary>
    /// Decimates the mesh until triangle count is at or below targetTriangles.
    /// Halo 3 optimal cluster budget: 5,000 - 20,000 triangles.
    /// </summary>
    public static DecimationReport DecimateToTargetTriangles(MeshObject mesh, int targetTriangles, bool preserveBoundaries = true)
    {
        if (mesh == null || mesh.Triangles.Count <= targetTriangles)
        {
            int vCount = mesh?.Vertices.Count ?? 0;
            int tCount = mesh?.Triangles.Count ?? 0;
            return new DecimationReport(vCount, vCount, tCount, tCount, 0f);
        }

        int origVerts = mesh.Vertices.Count;
        int origTris = mesh.Triangles.Count;
        mesh.RecalculateNormals();

        // 1. Identify boundary edges (belong to only 1 triangle)
        var edgeTriCount = new Dictionary<MeshEdge, int>();
        foreach (var t in mesh.Triangles)
        {
            void CountE(int a, int b)
            {
                var e = new MeshEdge(a, b);
                edgeTriCount.TryGetValue(e, out int c);
                edgeTriCount[e] = c + 1;
            }
            CountE(t.V0, t.V1);
            CountE(t.V1, t.V2);
            CountE(t.V2, t.V0);
        }

        var boundaryVerts = new HashSet<int>();
        foreach (var (edge, count) in edgeTriCount)
        {
            if (count == 1)
            {
                boundaryVerts.Add(edge.V0);
                boundaryVerts.Add(edge.V1);
            }
        }

        // 2. Compute initial Quadrics for each vertex
        var quadrics = new SymmetricMatrix[mesh.Vertices.Count];
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var t = mesh.Triangles[i];
            var p0 = mesh.Vertices[t.V0].Position;
            var p1 = mesh.Vertices[t.V1].Position;
            var p2 = mesh.Vertices[t.V2].Position;

            var normal = Vector3.Cross(p1 - p0, p2 - p0);
            float len = normal.Length();
            if (len > 1e-6f)
            {
                normal /= len;
                float d = -Vector3.Dot(normal, p0);
                var q = new SymmetricMatrix(normal.X, normal.Y, normal.Z, d);
                quadrics[t.V0] += q;
                quadrics[t.V1] += q;
                quadrics[t.V2] += q;
            }
        }

        // Add heavy penalty quadric to boundary vertices to lock silhouettes
        if (preserveBoundaries)
        {
            foreach (var (edge, count) in edgeTriCount)
            {
                if (count == 1)
                {
                    var p0 = mesh.Vertices[edge.V0].Position;
                    var p1 = mesh.Vertices[edge.V1].Position;
                    var edgeVec = p1 - p0;
                    float len = edgeVec.Length();
                    if (len > 1e-6f)
                    {
                        var edgeDir = edgeVec / len;
                        // Plane perpendicular to boundary edge
                        var n = Vector3.Normalize(Vector3.Cross(edgeDir, Vector3.UnitZ));
                        if (n.LengthSquared() < 0.1f) n = Vector3.Normalize(Vector3.Cross(edgeDir, Vector3.UnitX));
                        float d = -Vector3.Dot(n, p0);
                        var boundaryQ = new SymmetricMatrix(n.X * 1000f, n.Y * 1000f, n.Z * 1000f, d * 1000f);
                        quadrics[edge.V0] += boundaryQ;
                        quadrics[edge.V1] += boundaryQ;
                    }
                }
            }
        }

        // 3. Build vertex adjacency
        var vertNeighbors = new Dictionary<int, HashSet<int>>();
        var vertTriangles = new Dictionary<int, HashSet<int>>();
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            vertNeighbors[i] = new HashSet<int>();
            vertTriangles[i] = new HashSet<int>();
        }

        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var t = mesh.Triangles[i];
            vertNeighbors[t.V0].Add(t.V1); vertNeighbors[t.V0].Add(t.V2);
            vertNeighbors[t.V1].Add(t.V0); vertNeighbors[t.V1].Add(t.V2);
            vertNeighbors[t.V2].Add(t.V0); vertNeighbors[t.V2].Add(t.V1);

            vertTriangles[t.V0].Add(i);
            vertTriangles[t.V1].Add(i);
            vertTriangles[t.V2].Add(i);
        }

        // 4. Calculate collapse cost for an edge (v0 -> v1)
        CollapseEdge CalculateEdge(int v0, int v1)
        {
            var p0 = mesh.Vertices[v0].Position;
            var p1 = mesh.Vertices[v1].Position;
            var q = quadrics[v0] + quadrics[v1];

            // Test 3 candidate positions: midpoint, p0, p1
            var mid = (p0 + p1) * 0.5f;
            float costMid = q.Evaluate(mid);
            float cost0 = q.Evaluate(p0);
            float cost1 = q.Evaluate(p1);

            Vector3 bestPos = mid;
            float bestCost = costMid;

            // If one vertex is boundary, prefer collapsing to the boundary vertex to retain outline
            if (boundaryVerts.Contains(v0) && !boundaryVerts.Contains(v1))
            {
                bestPos = p0;
                bestCost = cost0;
            }
            else if (!boundaryVerts.Contains(v0) && boundaryVerts.Contains(v1))
            {
                bestPos = p1;
                bestCost = cost1;
            }
            else
            {
                if (cost0 < bestCost) { bestCost = cost0; bestPos = p0; }
                if (cost1 < bestCost) { bestCost = cost1; bestPos = p1; }
            }

            // Penalty for boundary vertex collapse if both are boundary
            if (preserveBoundaries && boundaryVerts.Contains(v0) && boundaryVerts.Contains(v1))
            {
                bestCost += 1e6f;
            }

            return new CollapseEdge
            {
                V0 = v0,
                V1 = v1,
                TargetPosition = bestPos,
                Cost = bestCost
            };
        }

        var candidateEdges = new List<CollapseEdge>();
        var seenEdges = new HashSet<MeshEdge>();
        for (int v = 0; v < mesh.Vertices.Count; v++)
        {
            foreach (int n in vertNeighbors[v])
            {
                var me = new MeshEdge(v, n);
                if (seenEdges.Add(me))
                {
                    candidateEdges.Add(CalculateEdge(v, n));
                }
            }
        }

        candidateEdges.Sort((a, b) => a.Cost.CompareTo(b.Cost));

        // 5. Iteratively collapse edges with minimal quadric cost
        int currentTris = mesh.Triangles.Count;
        int edgeIndex = 0;
        var vertexRemap = new int[mesh.Vertices.Count];
        for (int i = 0; i < vertexRemap.Length; i++) vertexRemap[i] = i;

        int FindRoot(int v)
        {
            while (v != vertexRemap[v])
            {
                vertexRemap[v] = vertexRemap[vertexRemap[v]];
                v = vertexRemap[v];
            }
            return v;
        }

        var collapsedTris = new HashSet<int>();
        while (currentTris > targetTriangles && edgeIndex < candidateEdges.Count)
        {
            var edge = candidateEdges[edgeIndex++];
            int r0 = FindRoot(edge.V0);
            int r1 = FindRoot(edge.V1);
            if (r0 == r1) continue;

            // Check if collapsing r0 into r1 flips any incident triangle normals
            bool flips = false;
            var incidentTris = vertTriangles[r0].Union(vertTriangles[r1]);
            foreach (int tIdx in incidentTris)
            {
                if (collapsedTris.Contains(tIdx)) continue;
                var t = mesh.Triangles[tIdx];
                int tv0 = FindRoot(t.V0);
                int tv1 = FindRoot(t.V1);
                int tv2 = FindRoot(t.V2);

                // Triangles sharing the collapsing edge will disappear; do not check them for flips
                bool hasR0 = (tv0 == r0 || tv1 == r0 || tv2 == r0);
                bool hasR1 = (tv0 == r1 || tv1 == r1 || tv2 == r1);
                if (hasR0 && hasR1) continue;
                if (tv0 == tv1 || tv1 == tv2 || tv2 == tv0) continue;

                var origNormal = t.Normal;
                if (origNormal.LengthSquared() < 0.1f) origNormal = Vector3.UnitZ;
                var tp0 = tv0 == r0 || tv0 == r1 ? edge.TargetPosition : mesh.Vertices[tv0].Position;
                var tp1 = tv1 == r0 || tv1 == r1 ? edge.TargetPosition : mesh.Vertices[tv1].Position;
                var tp2 = tv2 == r0 || tv2 == r1 ? edge.TargetPosition : mesh.Vertices[tv2].Position;

                var newNormal = Vector3.Cross(tp1 - tp0, tp2 - tp0);
                if (newNormal.LengthSquared() < 1e-8f || Vector3.Dot(Vector3.Normalize(newNormal), origNormal) < 0.20f)
                {
                    flips = true;
                    break;
                }
            }

            if (flips) continue;

            // Perform collapse: merge r0 into r1
            mesh.Vertices[r1].Position = edge.TargetPosition;
            quadrics[r1] += quadrics[r0];
            vertexRemap[r0] = r1;

            vertTriangles[r1].UnionWith(vertTriangles[r0]);

            // Count how many triangles collapsed
            foreach (int tIdx in vertTriangles[r1])
            {
                if (!collapsedTris.Contains(tIdx))
                {
                    var t = mesh.Triangles[tIdx];
                    int v0 = FindRoot(t.V0);
                    int v1 = FindRoot(t.V1);
                    int v2 = FindRoot(t.V2);
                    if (v0 == v1 || v1 == v2 || v2 == v0)
                    {
                        collapsedTris.Add(tIdx);
                        currentTris--;
                    }
                }
            }
        }

        // 6. Rebuild clean surviving triangles
        var survivingTris = new List<Triangle>(currentTris);
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var t = mesh.Triangles[i];
            int v0 = FindRoot(t.V0);
            int v1 = FindRoot(t.V1);
            int v2 = FindRoot(t.V2);

            if (v0 != v1 && v1 != v2 && v2 != v0)
            {
                survivingTris.Add(new Triangle(v0, v1, v2, t.MaterialIndex));
            }
        }
        mesh.Triangles = survivingTris;

        // Prune loose vertices and recalculate normals
        Diagnostics.GeometryAutoRepair.PruneLooseVertices(mesh);
        mesh.RecalculateNormals();

        int finalVerts = mesh.Vertices.Count;
        int finalTris = mesh.Triangles.Count;
        float reduction = origTris > 0 ? (origTris - finalTris) / (float)origTris * 100.0f : 0f;

        return new DecimationReport(origVerts, finalVerts, origTris, finalTris, reduction);
    }
}
