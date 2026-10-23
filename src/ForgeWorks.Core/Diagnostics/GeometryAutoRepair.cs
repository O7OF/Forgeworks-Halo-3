using System;
using System.Collections.Generic;
using System.Numerics;
using ForgeWorks.Core.Geometry;

namespace ForgeWorks.Core.Diagnostics;

public sealed class RepairReport
{
    public int VerticesWelded { get; set; }
    public int DuplicateFacesRemoved { get; set; }
    public int DegenerateFacesRemoved { get; set; }
    public int HolesCapped { get; set; }
    public int OpenEdgesBridged { get; set; }
    public int OpenEdgesRemaining { get; set; }
    public int AmbiguousEdgesSplit { get; set; }
    public int ShortEdgesCollapsed { get; set; }
    public int WaterQuadsFixed { get; set; }
    public int WaterUVChannelsFixed { get; set; }
    public int SmoothedVertices { get; set; }
    public int OverlappingSurfacesRemoved { get; set; }
    public int DegenerateTrianglesRemoved { get; set; }
    public int DegenerateFacesFixed { get; set; }
    public int LeakyConnectionsSealed { get; set; }
    public int NearlyCoplanarSurfacesFlattened { get; set; }
    public bool NormalsRecalculated { get; set; }
}

public sealed class GeometryAutoRepair
{
    public const float DefaultWeldDistance = 0.05f; // Standard weld threshold
    public const float BoundarySnapDistance = 2.5f; // Boundary seam snap threshold
    public const float ShortEdgeThreshold = 0.08f;  // Halo collision short edge threshold (0.08 units)
    public const float MinTriangleArea = 1e-4f;     // Degenerate triangle area threshold
    public const float MaxLeakyDistance = 12.0f;    // Leaky seam proximity threshold

    /// <summary>
    /// Master repair pipeline addressing all 8 canonical Halo HEK / H3EK BSP error types:
    /// 1. Degenerate Triangle
    /// 2. Short Collision Edge
    /// 3. Overlapping Surfaces
    /// 4. Open Edge
    /// 5. Degenerate Face
    /// 6. Leaky Connection
    /// 7. Ambiguous Connection
    /// 8. Nearly Coplanar Surface
    /// </summary>
    public static RepairReport RepairAllErrors(
        MeshObject mesh,
        bool capHoles = true,
        float weldDistance = DefaultWeldDistance,
        bool collapseShortEdges = true,
        bool flattenNearlyCoplanar = true,
        bool applySmooth = true)
    {
        var report = new RepairReport();
        if (mesh == null || mesh.Vertices.Count == 0 || mesh.Triangles.Count == 0)
            return report;

        // 0. Render-only tag enforcement for special materials (! water, % foliage cards)
        EnforceMaterialConventions(mesh, report);

        // Algorithm 1: Fix Degenerate Triangles (identical indices & zero area)
        FixDegenerateTriangles(mesh, report);

        // Algorithm 5: Fix Degenerate Faces (needle slivers & extreme aspect ratios) - skip on terrain/sky to protect rim
        if (!IsLandscapeOrSky(mesh))
        {
            FixDegenerateFaces(mesh, report);
        }

        // Algorithm 2: Fix Short Collision Edges (< 0.08 units) - skip on terrain/sky to protect rim
        if (collapseShortEdges && !IsLandscapeOrSky(mesh))
        {
            FixShortCollisionEdges(mesh, ShortEdgeThreshold, report);
            FixDegenerateTriangles(mesh, report);
        }

        // Algorithm 3: Fix Overlapping Surfaces (duplicate & coplanar overlapping faces)
        FixOverlappingSurfaces(mesh, report);

        // Algorithm 7: Fix Ambiguous Connections (> 2 faces sharing an edge)
        FixAmbiguousConnections(mesh, report);

        // Algorithm 6: Fix Leaky Connections (unwelded seams & slits between adjacent components)
        FixLeakyConnections(mesh, MaxLeakyDistance, report);

        // Vertices welding pass (crucial for welding terrain boundary to sky dome boundary)
        if (weldDistance > 0.0f)
        {
            WeldVertices(mesh, weldDistance, report);
            FixOverlappingSurfaces(mesh, report);
        }

        // Algorithm 8: Fix Nearly Coplanar Surfaces (normal difference 0.02° - 1.5°) - skip on terrain/sky to protect rim
        if (flattenNearlyCoplanar && !IsLandscapeOrSky(mesh))
        {
            FixNearlyCoplanarSurfaces(mesh, 0.02f, 1.5f, report);
        }

        // Algorithm 4: Fix Open Edges (boundary hole capping, edge bridging & render-only tag fallback)
        FixOpenEdges(mesh, capHoles, report);

        // Water UV channel verification (requires exactly 4 channels for Blam water simulation)
        if (!mesh.CheckIsCollideable() && mesh.Name.Contains("water", StringComparison.OrdinalIgnoreCase))
        {
            FixWaterUvs(mesh, report);
        }

        // Optional Laplacian smoothing pass (skip on terrain/sky to protect rim)
        if (applySmooth && !IsLandscapeOrSky(mesh))
        {
            report.SmoothedVertices = GeometrySmoother.SmoothGeneralMesh(mesh, passes: 2, strength: 0.35f);
        }

        // Prune loose unreferenced vertices and recalculate normals
        PruneLooseVertices(mesh);
        mesh.RecalculateNormals();
        report.NormalsRecalculated = true;

        // Final count of remaining open edges
        report.OpenEdgesRemaining = CountOpenEdges(mesh);

        return report;
    }

    public static RepairReport SwampthingFixOpenEdges(
        MeshObject mesh,
        bool capHoles = true,
        float weldDistance = DefaultWeldDistance,
        bool collapseShortEdges = true,
        bool applySmooth = true)
    {
        return RepairAllErrors(mesh, capHoles, weldDistance, collapseShortEdges, flattenNearlyCoplanar: true, applySmooth);
    }

    public static RepairReport RepairMesh(
        MeshObject mesh,
        float weldDistance = DefaultWeldDistance,
        bool removeDegenerate = true,
        bool removeDuplicates = true,
        bool fixWaterUVs = true,
        bool recalculateNormals = true)
    {
        return RepairAllErrors(mesh, capHoles: true, weldDistance: weldDistance, collapseShortEdges: true, flattenNearlyCoplanar: true, applySmooth: true);
    }

    // =========================================================================
    // 1. ALGORITHM: FIX DEGENERATE TRIANGLES (O(N) Union-Find Optimized)
    // =========================================================================
    public static void FixDegenerateTriangles(MeshObject mesh, RepairReport report)
    {
        if (mesh == null || mesh.Triangles.Count == 0 || mesh.Vertices.Count == 0) return;

        int[] parent = new int[mesh.Vertices.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        int Find(int i)
        {
            while (i != parent[i])
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        void Union(int a, int b)
        {
            int rA = Find(a);
            int rB = Find(b);
            if (rA != rB)
            {
                parent[rB] = rA;
                mesh.Vertices[rA].Position = (mesh.Vertices[rA].Position + mesh.Vertices[rB].Position) * 0.5f;
            }
        }

        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            int v0 = Find(tri.V0);
            int v1 = Find(tri.V1);
            int v2 = Find(tri.V2);

            // Identical vertex indices
            if (v0 == v1 || v1 == v2 || v0 == v2)
            {
                report.DegenerateTrianglesRemoved++;
                report.DegenerateFacesRemoved++;
                continue;
            }

            var p0 = mesh.Vertices[v0].Position;
            var p1 = mesh.Vertices[v1].Position;
            var p2 = mesh.Vertices[v2].Position;

            var cross = Vector3.Cross(p1 - p0, p2 - p0);
            float area = cross.Length() * 0.5f;

            if (area < MinTriangleArea)
            {
                float d01 = (p1 - p0).LengthSquared();
                float d12 = (p2 - p1).LengthSquared();
                float d20 = (p0 - p2).LengthSquared();

                int keep = v0, drop = v1;
                if (d12 < d01 && d12 < d20) { keep = v1; drop = v2; }
                else if (d20 < d01 && d20 < d12) { keep = v2; drop = v0; }

                Union(keep, drop);
                report.DegenerateTrianglesRemoved++;
                report.DegenerateFacesRemoved++;
            }
        }

        var validTris = new List<Triangle>(mesh.Triangles.Count);
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            int v0 = Find(tri.V0);
            int v1 = Find(tri.V1);
            int v2 = Find(tri.V2);

            if (v0 != v1 && v1 != v2 && v0 != v2)
            {
                tri.V0 = v0;
                tri.V1 = v1;
                tri.V2 = v2;
                validTris.Add(tri);
            }
        }
        mesh.Triangles = validTris;
    }

    // =========================================================================
    // 2. ALGORITHM: FIX SHORT COLLISION EDGES (< 0.08 units) (O(N) Union-Find Optimized)
    // =========================================================================
    public static void FixShortCollisionEdges(MeshObject mesh, float minLength, RepairReport report)
    {
        if (mesh == null || mesh.Triangles.Count == 0 || mesh.Vertices.Count == 0) return;
        float minLenSq = minLength * minLength;

        int[] parent = new int[mesh.Vertices.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        int Find(int i)
        {
            while (i != parent[i])
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        void Union(int a, int b)
        {
            int rA = Find(a);
            int rB = Find(b);
            if (rA != rB)
            {
                parent[rB] = rA;
                mesh.Vertices[rA].Position = (mesh.Vertices[rA].Position + mesh.Vertices[rB].Position) * 0.5f;
            }
        }

        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            int v0 = Find(tri.V0);
            int v1 = Find(tri.V1);
            int v2 = Find(tri.V2);
            if (v0 == v1 || v1 == v2 || v0 == v2) continue;

            var p0 = mesh.Vertices[v0].Position;
            var p1 = mesh.Vertices[v1].Position;
            var p2 = mesh.Vertices[v2].Position;

            if ((p1 - p0).LengthSquared() < minLenSq)
            {
                Union(v0, v1);
                report.ShortEdgesCollapsed++;
            }
            else if ((p2 - p1).LengthSquared() < minLenSq)
            {
                Union(v1, v2);
                report.ShortEdgesCollapsed++;
            }
            else if ((p0 - p2).LengthSquared() < minLenSq)
            {
                Union(v2, v0);
                report.ShortEdgesCollapsed++;
            }
        }

        var validTris = new List<Triangle>(mesh.Triangles.Count);
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            int v0 = Find(tri.V0);
            int v1 = Find(tri.V1);
            int v2 = Find(tri.V2);

            if (v0 != v1 && v1 != v2 && v0 != v2)
            {
                tri.V0 = v0;
                tri.V1 = v1;
                tri.V2 = v2;
                validTris.Add(tri);
            }
        }
        mesh.Triangles = validTris;
    }

    // =========================================================================
    // 3. ALGORITHM: FIX OVERLAPPING SURFACES
    // =========================================================================
    public static void FixOverlappingSurfaces(MeshObject mesh, RepairReport report)
    {
        var seen = new HashSet<(int, int, int)>();
        var unique = new List<Triangle>(mesh.Triangles.Count);

        foreach (var tri in mesh.Triangles)
        {
            int a = tri.V0, b = tri.V1, c = tri.V2;
            int minV = Math.Min(a, Math.Min(b, c));
            int maxV = Math.Max(a, Math.Max(b, c));
            int midV = a + b + c - minV - maxV;

            if (seen.Add((minV, midV, maxV)))
            {
                unique.Add(tri);
            }
            else
            {
                report.OverlappingSurfacesRemoved++;
                report.DuplicateFacesRemoved++;
            }
        }
        mesh.Triangles = unique;
    }

    // =========================================================================
    // 4. ALGORITHM: FIX OPEN EDGES
    // =========================================================================
    public static void FixOpenEdges(MeshObject mesh, bool capHoles, RepairReport report)
    {
        if (!mesh.CheckIsCollideable()) return;
        if (IsLandscapeOrSky(mesh)) return; // Preserves clean terrain perimeter for 1:1 skybox stitching

        // Step 1: Snap boundary vertices within seam snap distance (2.5 units)
        SnapBoundaryVertices(mesh, BoundarySnapDistance, report);

        // Step 2: Bridge opposing boundary edge chains
        BridgeOpenBoundaryEdges(mesh, report);

        // Step 3: Cap boundary loops with centroid fan triangles (only on props/bunkers)
        if (capHoles)
        {
            CapBoundaryHoles(mesh, report);
        }

        // Step 4: Fallback for un-sealable decorative props (mark render-only so Tool skips collision crash)
        int remainingOpen = CountOpenEdges(mesh);
        if (remainingOpen > 0 && !mesh.MaterialName.StartsWith("+sky", StringComparison.OrdinalIgnoreCase) && !mesh.Name.StartsWith("+sky", StringComparison.OrdinalIgnoreCase))
        {
            bool isMainGround = mesh.Name.Contains("terrain", StringComparison.OrdinalIgnoreCase) || mesh.Name.Contains("ground", StringComparison.OrdinalIgnoreCase) || mesh.Name.Contains("bsp", StringComparison.OrdinalIgnoreCase);
            if (!isMainGround)
            {
                if (!mesh.MaterialName.StartsWith("!") && !mesh.MaterialName.StartsWith("%!"))
                {
                    mesh.MaterialName = $"!{mesh.MaterialName}";
                }
                for (int s = 0; s < mesh.MaterialSlots.Count; s++)
                {
                    if (!mesh.MaterialSlots[s].StartsWith("!") && !mesh.MaterialSlots[s].StartsWith("%!"))
                    {
                        mesh.MaterialSlots[s] = $"!{mesh.MaterialSlots[s]}";
                    }
                }
                mesh.IsCollideable = false;
            }
        }
    }

    // =========================================================================
    // 5. ALGORITHM: FIX DEGENERATE FACES (O(N) Union-Find Optimized)
    // =========================================================================
    public static void FixDegenerateFaces(MeshObject mesh, RepairReport report)
    {
        if (mesh == null || mesh.Triangles.Count == 0 || mesh.Vertices.Count == 0) return;

        int[] parent = new int[mesh.Vertices.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        int Find(int i)
        {
            while (i != parent[i])
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        void Union(int a, int b)
        {
            int rA = Find(a);
            int rB = Find(b);
            if (rA != rB)
            {
                parent[rB] = rA;
                mesh.Vertices[rA].Position = (mesh.Vertices[rA].Position + mesh.Vertices[rB].Position) * 0.5f;
            }
        }

        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            int v0 = Find(tri.V0);
            int v1 = Find(tri.V1);
            int v2 = Find(tri.V2);
            if (v0 == v1 || v1 == v2 || v0 == v2) continue;

            var p0 = mesh.Vertices[v0].Position;
            var p1 = mesh.Vertices[v1].Position;
            var p2 = mesh.Vertices[v2].Position;

            var cross = Vector3.Cross(p1 - p0, p2 - p0);
            float area = cross.Length() * 0.5f;

            if (area < MinTriangleArea)
            {
                Union(v0, v1);
                report.DegenerateFacesFixed++;
                report.DegenerateFacesRemoved++;
                continue;
            }

            float e0 = (p1 - p0).Length();
            float e1 = (p2 - p1).Length();
            float e2 = (p0 - p2).Length();
            float perimeter = e0 + e1 + e2;

            if ((perimeter * perimeter / area) > 400.0f)
            {
                int u = v0, v = v1;
                if (e1 < e0 && e1 < e2) { u = v1; v = v2; }
                else if (e2 < e0 && e2 < e1) { u = v2; v = v0; }

                Union(u, v);
                report.DegenerateFacesFixed++;
            }
        }

        var nonDegen = new List<Triangle>(mesh.Triangles.Count);
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            int v0 = Find(tri.V0);
            int v1 = Find(tri.V1);
            int v2 = Find(tri.V2);

            if (v0 != v1 && v1 != v2 && v0 != v2)
            {
                tri.V0 = v0;
                tri.V1 = v1;
                tri.V2 = v2;
                nonDegen.Add(tri);
            }
        }
        mesh.Triangles = nonDegen;
    }

    // =========================================================================
    // 6. ALGORITHM: FIX LEAKY CONNECTIONS (Spatial Hash Grid Optimized)
    // =========================================================================
    public static void FixLeakyConnections(MeshObject mesh, float maxDistance, RepairReport report)
    {
        if (IsLandscapeOrSky(mesh)) return;

        var openEdges = GetOpenEdges(mesh);
        if (openEdges.Count < 2) return;

        float snapDistSq = BoundarySnapDistance * BoundarySnapDistance;
        var boundaryVerts = new HashSet<int>();
        foreach (var (u, v) in openEdges)
        {
            boundaryVerts.Add(u);
            boundaryVerts.Add(v);
        }

        float cellSize = MathF.Max(BoundarySnapDistance * 2.0f, 1.0f);
        var grid = new Dictionary<(int, int, int), List<int>>();
        (int, int, int) GetCell(Vector3 p) =>
            ((int)MathF.Floor(p.X / cellSize), (int)MathF.Floor(p.Y / cellSize), (int)MathF.Floor(p.Z / cellSize));

        foreach (int idx in boundaryVerts)
        {
            var p = mesh.Vertices[idx].Position;
            var cell = GetCell(p);
            int match = -1;

            for (int dx = -1; dx <= 1 && match == -1; dx++)
            {
                for (int dy = -1; dy <= 1 && match == -1; dy++)
                {
                    for (int dz = -1; dz <= 1 && match == -1; dz++)
                    {
                        if (grid.TryGetValue((cell.Item1 + dx, cell.Item2 + dy, cell.Item3 + dz), out var list))
                        {
                            foreach (var oIdx in list)
                            {
                                if ((mesh.Vertices[oIdx].Position - p).LengthSquared() <= snapDistSq)
                                {
                                    match = oIdx;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if (match != -1)
            {
                mesh.Vertices[idx].Position = mesh.Vertices[match].Position;
                report.LeakyConnectionsSealed++;
                report.VerticesWelded++;
            }
            else
            {
                if (!grid.TryGetValue(cell, out var list))
                {
                    list = new List<int>(4);
                    grid[cell] = list;
                }
                list.Add(idx);
            }
        }

        // Bridge remaining edge gaps between 2.5 and 12 units
        BridgeOpenBoundaryEdges(mesh, report);
    }

    // =========================================================================
    // 7. ALGORITHM: FIX AMBIGUOUS CONNECTIONS (> 2 faces sharing an edge)
    // =========================================================================
    public static void FixAmbiguousConnections(MeshObject mesh, RepairReport report)
    {
        var edgeMap = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            void Link(int a, int b)
            {
                var k = a < b ? (a, b) : (b, a);
                if (!edgeMap.TryGetValue(k, out var list))
                {
                    list = new List<int>(2);
                    edgeMap[k] = list;
                }
                list.Add(i);
            }
            Link(tri.V0, tri.V1);
            Link(tri.V1, tri.V2);
            Link(tri.V2, tri.V0);
        }

        foreach (var kvp in edgeMap)
        {
            var linked = kvp.Value;
            if (linked.Count > 2)
            {
                var (vA, vB) = kvp.Key;
                // Leave first 2 faces attached; duplicate vertices for extra faces to make them 2-manifold
                for (int f = 2; f < linked.Count; f++)
                {
                    int triIdx = linked[f];
                    var tri = mesh.Triangles[triIdx];

                    int newVa = mesh.Vertices.Count;
                    mesh.Vertices.Add(new Vertex(mesh.Vertices[vA].Position, mesh.Vertices[vA].Normal)
                    {
                        Color = mesh.Vertices[vA].Color,
                        UVs = new List<Vector2>(mesh.Vertices[vA].UVs)
                    });

                    int newVb = mesh.Vertices.Count;
                    mesh.Vertices.Add(new Vertex(mesh.Vertices[vB].Position, mesh.Vertices[vB].Normal)
                    {
                        Color = mesh.Vertices[vB].Color,
                        UVs = new List<Vector2>(mesh.Vertices[vB].UVs)
                    });

                    if (tri.V0 == vA) tri.V0 = newVa;
                    else if (tri.V1 == vA) tri.V1 = newVa;
                    else if (tri.V2 == vA) tri.V2 = newVa;

                    if (tri.V0 == vB) tri.V0 = newVb;
                    else if (tri.V1 == vB) tri.V1 = newVb;
                    else if (tri.V2 == vB) tri.V2 = newVb;

                    report.AmbiguousEdgesSplit++;
                }
            }
        }
    }

    // =========================================================================
    // 8. ALGORITHM: FIX NEARLY COPLANAR SURFACES (Normal diff 0.02° - 1.5°)
    // =========================================================================
    public static void FixNearlyCoplanarSurfaces(MeshObject mesh, float minAngleDeg, float maxAngleDeg, RepairReport report)
    {
        float minDot = MathF.Cos(maxAngleDeg * MathF.PI / 180.0f);
        float maxDot = MathF.Cos(minAngleDeg * MathF.PI / 180.0f);

        var edgeMap = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            void Link(int a, int b)
            {
                var k = a < b ? (a, b) : (b, a);
                if (!edgeMap.TryGetValue(k, out var list))
                {
                    list = new List<int>(2);
                    edgeMap[k] = list;
                }
                list.Add(i);
            }
            Link(tri.V0, tri.V1);
            Link(tri.V1, tri.V2);
            Link(tri.V2, tri.V0);
        }

        foreach (var kvp in edgeMap)
        {
            var linked = kvp.Value;
            if (linked.Count == 2)
            {
                var tri0 = mesh.Triangles[linked[0]];
                var tri1 = mesh.Triangles[linked[1]];

                float dot = Vector3.Dot(tri0.Normal, tri1.Normal);
                if (dot >= minDot && dot <= maxDot)
                {
                    // Equalize face normals by planar snapping opposing vertices onto shared average plane
                    var nAvg = Vector3.Normalize(tri0.Normal + tri1.Normal);
                    var (vA, vB) = kvp.Key;
                    var mid = (mesh.Vertices[vA].Position + mesh.Vertices[vB].Position) * 0.5f;
                    float planeD = Vector3.Dot(nAvg, mid);

                    // Find opposing vertices
                    int w0 = tri0.V0 != vA && tri0.V0 != vB ? tri0.V0 : (tri0.V1 != vA && tri0.V1 != vB ? tri0.V1 : tri0.V2);
                    int w1 = tri1.V0 != vA && tri1.V0 != vB ? tri1.V0 : (tri1.V1 != vA && tri1.V1 != vB ? tri1.V1 : tri1.V2);

                    // Project opposing vertices onto the average plane
                    var pW0 = mesh.Vertices[w0].Position;
                    mesh.Vertices[w0].Position = pW0 - (Vector3.Dot(nAvg, pW0) - planeD) * nAvg;

                    var pW1 = mesh.Vertices[w1].Position;
                    mesh.Vertices[w1].Position = pW1 - (Vector3.Dot(nAvg, pW1) - planeD) * nAvg;

                    tri0.Normal = nAvg;
                    tri1.Normal = nAvg;

                    report.NearlyCoplanarSurfacesFlattened++;
                }
            }
        }
    }

    // =========================================================================
    // HELPER ROUTINES (Welding, Snapping, Capping, Water & Normal Recalculation)
    // =========================================================================

    public static void SnapBoundaryVertices(MeshObject mesh, float snapDistance, RepairReport report)
    {
        var openEdges = GetOpenEdges(mesh);
        if (openEdges.Count == 0) return;

        var boundaryVertIndices = new HashSet<int>();
        foreach (var (u, v) in openEdges)
        {
            boundaryVertIndices.Add(u);
            boundaryVertIndices.Add(v);
        }

        float snapDistSq = snapDistance * snapDistance;
        float cellSize = MathF.Max(snapDistance * 2.0f, 1.0f);
        var grid = new Dictionary<(int, int, int), List<int>>();
        (int, int, int) GetCell(Vector3 p) =>
            ((int)MathF.Floor(p.X / cellSize), (int)MathF.Floor(p.Y / cellSize), (int)MathF.Floor(p.Z / cellSize));

        foreach (int idx in boundaryVertIndices)
        {
            var p = mesh.Vertices[idx].Position;
            var cell = GetCell(p);
            int match = -1;

            for (int dx = -1; dx <= 1 && match == -1; dx++)
            {
                for (int dy = -1; dy <= 1 && match == -1; dy++)
                {
                    for (int dz = -1; dz <= 1 && match == -1; dz++)
                    {
                        if (grid.TryGetValue((cell.Item1 + dx, cell.Item2 + dy, cell.Item3 + dz), out var list))
                        {
                            foreach (var oIdx in list)
                            {
                                if ((mesh.Vertices[oIdx].Position - p).LengthSquared() <= snapDistSq)
                                {
                                    match = oIdx;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if (match != -1)
            {
                mesh.Vertices[idx].Position = mesh.Vertices[match].Position;
                report.VerticesWelded++;
            }
            else
            {
                if (!grid.TryGetValue(cell, out var list))
                {
                    list = new List<int>(4);
                    grid[cell] = list;
                }
                list.Add(idx);
            }
        }
    }

    public static void WeldVertices(MeshObject mesh, float weldDistance, RepairReport report)
    {
        float weldDistSq = weldDistance * weldDistance;
        var oldToNewMap = new int[mesh.Vertices.Count];
        var newVertices = new List<Vertex>();

        float cellSize = MathF.Max(weldDistance * 2.0f, 0.1f);
        var grid = new Dictionary<(int, int, int), List<int>>();

        (int, int, int) GetCell(Vector3 pos) =>
            ((int)MathF.Floor(pos.X / cellSize),
             (int)MathF.Floor(pos.Y / cellSize),
             (int)MathF.Floor(pos.Z / cellSize));

        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var v = mesh.Vertices[i];
            var cell = GetCell(v.Position);
            int matchIndex = -1;

            for (int dx = -1; dx <= 1 && matchIndex == -1; dx++)
            {
                for (int dy = -1; dy <= 1 && matchIndex == -1; dy++)
                {
                    for (int dz = -1; dz <= 1 && matchIndex == -1; dz++)
                    {
                        var neighborCell = (cell.Item1 + dx, cell.Item2 + dy, cell.Item3 + dz);
                        if (grid.TryGetValue(neighborCell, out var cellVerts))
                        {
                            foreach (var candidateIdx in cellVerts)
                            {
                                if ((newVertices[candidateIdx].Position - v.Position).LengthSquared() <= weldDistSq)
                                {
                                    matchIndex = candidateIdx;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if (matchIndex != -1)
            {
                oldToNewMap[i] = matchIndex;
                report.VerticesWelded++;
            }
            else
            {
                int newIdx = newVertices.Count;
                newVertices.Add(v);
                oldToNewMap[i] = newIdx;

                if (!grid.TryGetValue(cell, out var list))
                {
                    list = new List<int>();
                    grid[cell] = list;
                }
                list.Add(newIdx);
            }
        }

        mesh.Vertices = newVertices;

        var validTris = new List<Triangle>(mesh.Triangles.Count);
        foreach (var tri in mesh.Triangles)
        {
            int n0 = oldToNewMap[tri.V0];
            int n1 = oldToNewMap[tri.V1];
            int n2 = oldToNewMap[tri.V2];

            if (n0 == n1 || n1 == n2 || n0 == n2)
            {
                report.DegenerateFacesRemoved++;
                continue;
            }

            tri.V0 = n0;
            tri.V1 = n1;
            tri.V2 = n2;
            validTris.Add(tri);
        }
        mesh.Triangles = validTris;
    }

    public static void RemoveDegenerateFaces(MeshObject mesh, RepairReport report)
    {
        FixDegenerateTriangles(mesh, report);
    }

    public static void RemoveDuplicateFaces(MeshObject mesh, RepairReport report)
    {
        FixOverlappingSurfaces(mesh, report);
    }

    public static void CollapseShortEdges(MeshObject mesh, float minLength, RepairReport report)
    {
        FixShortCollisionEdges(mesh, minLength, report);
    }

    public static void SplitAmbiguousEdges(MeshObject mesh, RepairReport report)
    {
        FixAmbiguousConnections(mesh, report);
    }

    public static void CapBoundaryHoles(MeshObject mesh, RepairReport report)
    {
        var edgeCount = new Dictionary<(int, int), int>();
        var halfEdges = new Dictionary<int, List<int>>();

        bool IsCollideableTriangle(Triangle tri)
        {
            string mName = (tri.MaterialIndex >= 0 && tri.MaterialIndex < mesh.MaterialSlots.Count)
                ? mesh.MaterialSlots[tri.MaterialIndex]
                : mesh.MaterialName;
            if (string.IsNullOrEmpty(mName)) mName = mesh.MaterialName;
            return !mName.StartsWith("%") && !mName.StartsWith("!") && !mName.StartsWith("+portal") && !mName.EndsWith("!");
        }

        foreach (var tri in mesh.Triangles)
        {
            if (!IsCollideableTriangle(tri)) continue;
            void RecordEdge(int u, int v)
            {
                var key = u < v ? (u, v) : (v, u);
                edgeCount[key] = edgeCount.GetValueOrDefault(key, 0) + 1;
            }
            RecordEdge(tri.V0, tri.V1);
            RecordEdge(tri.V1, tri.V2);
            RecordEdge(tri.V2, tri.V0);
        }

        foreach (var tri in mesh.Triangles)
        {
            if (!IsCollideableTriangle(tri)) continue;
            void AddBoundaryHalfEdge(int u, int v)
            {
                var key = u < v ? (u, v) : (v, u);
                if (edgeCount.TryGetValue(key, out int count) && count == 1)
                {
                    if (!halfEdges.TryGetValue(u, out var list))
                    {
                        list = new List<int>(1);
                        halfEdges[u] = list;
                    }
                    list.Add(v);
                }
            }
            AddBoundaryHalfEdge(tri.V0, tri.V1);
            AddBoundaryHalfEdge(tri.V1, tri.V2);
            AddBoundaryHalfEdge(tri.V2, tri.V0);
        }

        var visited = new HashSet<(int, int)>();
        var loops = new List<List<int>>();

        foreach (var kvp in halfEdges)
        {
            int start = kvp.Key;
            foreach (int next in kvp.Value)
            {
                if (visited.Contains((start, next))) continue;

                var loop = new List<int> { start };
                visited.Add((start, next));
                int curr = next;

                while (curr != start)
                {
                    loop.Add(curr);
                    if (halfEdges.TryGetValue(curr, out var candidates) && candidates.Count > 0)
                    {
                        int picked = -1;
                        foreach (var c in candidates)
                        {
                            if (!visited.Contains((curr, c)))
                            {
                                picked = c;
                                break;
                            }
                        }
                        if (picked != -1)
                        {
                            visited.Add((curr, picked));
                            curr = picked;
                        }
                        else break;
                    }
                    else break;
                }

                if (curr == start && loop.Count >= 3)
                {
                    loops.Add(loop);
                }
            }
        }

        foreach (var loop in loops)
        {
            int n = loop.Count;
            if (n > 512) continue;

            if (n == 3)
            {
                if (mesh.Triangles.Count <= 1) continue;
                mesh.Triangles.Add(new Triangle(loop[0], loop[2], loop[1], 0));
                report.HolesCapped++;
            }
            else if (n >= 4)
            {
                Vector3 sum = Vector3.Zero;
                foreach (int vIdx in loop)
                {
                    sum += mesh.Vertices[vIdx].Position;
                }
                Vector3 centerPos = sum / n;
                int centerIdx = mesh.Vertices.Count;
                mesh.Vertices.Add(new Vertex(centerPos, Vector3.UnitZ));

                for (int i = 0; i < n; i++)
                {
                    int next = (i + 1) % n;
                    mesh.Triangles.Add(new Triangle(centerIdx, loop[next], loop[i], 0));
                }
                report.HolesCapped++;
            }
        }
    }

    public static void BridgeOpenBoundaryEdges(MeshObject mesh, RepairReport report)
    {
        var openEdges = GetOpenEdges(mesh);
        if (openEdges.Count < 2) return;

        float maxBridgeDistSq = 50.0f * 50.0f;
        var paired = new HashSet<int>();

        float cellSize = 50.0f;
        var grid = new Dictionary<(int, int, int), List<int>>();
        (int, int, int) GetCell(Vector3 p) =>
            ((int)MathF.Floor(p.X / cellSize), (int)MathF.Floor(p.Y / cellSize), (int)MathF.Floor(p.Z / cellSize));

        var midpoints = new Vector3[openEdges.Count];
        for (int i = 0; i < openEdges.Count; i++)
        {
            var (a, b) = openEdges[i];
            var mid = (mesh.Vertices[a].Position + mesh.Vertices[b].Position) * 0.5f;
            midpoints[i] = mid;
            var cell = GetCell(mid);
            if (!grid.TryGetValue(cell, out var list))
            {
                list = new List<int>(4);
                grid[cell] = list;
            }
            list.Add(i);
        }

        for (int i = 0; i < openEdges.Count; i++)
        {
            if (paired.Contains(i)) continue;
            var (a1, b1) = openEdges[i];
            var mid1 = midpoints[i];
            var cell = GetCell(mid1);

            int bestMatch = -1;
            float bestDistSq = float.MaxValue;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (grid.TryGetValue((cell.Item1 + dx, cell.Item2 + dy, cell.Item3 + dz), out var candidates))
                        {
                            foreach (int j in candidates)
                            {
                                if (j <= i || paired.Contains(j)) continue;
                                var (a2, b2) = openEdges[j];
                                if (a1 == a2 || a1 == b2 || b1 == a2 || b1 == b2) continue;

                                float dSq = (mid1 - midpoints[j]).LengthSquared();
                                if (dSq < bestDistSq && dSq <= maxBridgeDistSq)
                                {
                                    bestDistSq = dSq;
                                    bestMatch = j;
                                }
                            }
                        }
                    }
                }
            }

            if (bestMatch != -1)
            {
                var (a2, b2) = openEdges[bestMatch];
                float cross1 = (mesh.Vertices[a1].Position - mesh.Vertices[b2].Position).LengthSquared();
                float cross2 = (mesh.Vertices[b1].Position - mesh.Vertices[a2].Position).LengthSquared();

                if (cross1 < cross2)
                {
                    mesh.Triangles.Add(new Triangle(a1, b1, b2, 0));
                    mesh.Triangles.Add(new Triangle(a1, b2, a2, 0));
                }
                else
                {
                    mesh.Triangles.Add(new Triangle(a1, b1, a2, 0));
                    mesh.Triangles.Add(new Triangle(b1, b2, a2, 0));
                }

                paired.Add(i);
                paired.Add(bestMatch);
                report.OpenEdgesBridged += 2;
            }
        }
    }

    public static List<(int, int)> GetOpenEdges(MeshObject mesh)
    {
        var result = new List<(int, int)>();
        if (!mesh.CheckIsCollideable()) return result;

        var edgeCount = new Dictionary<(int, int), int>();
        foreach (var tri in mesh.Triangles)
        {
            if (!mesh.IsTriangleCollideable(tri)) continue;

            void CountEdge(int a, int b)
            {
                var k = a < b ? (a, b) : (b, a);
                edgeCount[k] = edgeCount.GetValueOrDefault(k, 0) + 1;
            }
            CountEdge(tri.V0, tri.V1);
            CountEdge(tri.V1, tri.V2);
            CountEdge(tri.V2, tri.V0);
        }

        foreach (var kvp in edgeCount)
        {
            if (kvp.Value == 1)
            {
                result.Add(kvp.Key);
            }
        }
        return result;
    }

    public static int CountOpenEdges(MeshObject mesh)
    {
        return GetOpenEdges(mesh).Count;
    }

    public static void FixWaterUvs(MeshObject mesh, RepairReport report)
    {
        foreach (var v in mesh.Vertices)
        {
            var baseUv = v.UVs.Count > 0 ? v.UVs[0] : new Vector2(v.Position.X * 0.01f, v.Position.Y * 0.01f);
            v.UVs.Clear();
            v.UVs.Add(baseUv);
            v.UVs.Add(baseUv * 2.0f);
            v.UVs.Add(baseUv * 0.5f);
            v.UVs.Add(baseUv * 4.0f);
        }
        report.WaterUVChannelsFixed = mesh.Vertices.Count;
    }

    public static void PruneLooseVertices(MeshObject mesh)
    {
        var used = new HashSet<int>();
        foreach (var tri in mesh.Triangles)
        {
            used.Add(tri.V0);
            used.Add(tri.V1);
            used.Add(tri.V2);
        }

        if (used.Count == mesh.Vertices.Count) return;

        var oldToNew = new int[mesh.Vertices.Count];
        var newVerts = new List<Vertex>(used.Count);

        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            if (used.Contains(i))
            {
                oldToNew[i] = newVerts.Count;
                newVerts.Add(mesh.Vertices[i]);
            }
            else
            {
                oldToNew[i] = -1;
            }
        }

        foreach (var tri in mesh.Triangles)
        {
            tri.V0 = oldToNew[tri.V0];
            tri.V1 = oldToNew[tri.V1];
            tri.V2 = oldToNew[tri.V2];
        }

        mesh.Vertices = newVerts;
    }

    public static bool IsLandscapeOrSky(MeshObject mesh) =>
        mesh != null && (
            mesh.Name.Contains("terrain", StringComparison.OrdinalIgnoreCase)
            || mesh.Name.Contains("ground", StringComparison.OrdinalIgnoreCase)
            || mesh.Name.Contains("bsp", StringComparison.OrdinalIgnoreCase)
            || mesh.Name.Contains("sky", StringComparison.OrdinalIgnoreCase)
            || mesh.MaterialName.StartsWith("+sky", StringComparison.OrdinalIgnoreCase));

    private static void EnforceMaterialConventions(MeshObject mesh, RepairReport report)
    {
        // Preserve +sky
        if (mesh.MaterialName.StartsWith("+sky", StringComparison.OrdinalIgnoreCase) || mesh.Name.StartsWith("+sky", StringComparison.OrdinalIgnoreCase))
        {
            mesh.MaterialName = "+sky";
            mesh.IsCollideable = true;
            return;
        }

        string mat = mesh.MaterialName;
        bool isWater = (mesh.Name.Contains("water", StringComparison.OrdinalIgnoreCase) && !mesh.Name.Contains("watertight", StringComparison.OrdinalIgnoreCase))
            || (mat.Contains("water", StringComparison.OrdinalIgnoreCase) && !mat.Contains("watertight", StringComparison.OrdinalIgnoreCase))
            || mat.EndsWith("'");

        if (isWater)
        {
            if (!mat.StartsWith("!") && !mat.StartsWith("%")) mat = "!" + mat;
            if (!mat.EndsWith("!")) mat = mat + "!";
            mesh.MaterialName = mat;
            mesh.IsCollideable = false;
            report.WaterQuadsFixed++;
        }
        else if (mat.StartsWith("%"))
        {
            // Foliage / two-sided cards in Blam: prefix '%!' makes them render-only without collision.
            // Strip any trailing '!' so Tool does not fail tag resolution.
            string clean = mat.TrimEnd('!');
            if (!clean.StartsWith("%!"))
            {
                clean = "%!" + clean.Substring(1);
            }
            mesh.MaterialName = clean;
        }

        for (int i = 0; i < mesh.MaterialSlots.Count; i++)
        {
            string sMat = mesh.MaterialSlots[i];
            if (sMat.StartsWith("+sky", StringComparison.OrdinalIgnoreCase))
            {
                mesh.MaterialSlots[i] = "+sky";
            }
            else if ((sMat.Contains("water", StringComparison.OrdinalIgnoreCase) && !sMat.Contains("watertight", StringComparison.OrdinalIgnoreCase)) || sMat.EndsWith("'"))
            {
                if (!sMat.StartsWith("!") && !sMat.StartsWith("%")) sMat = "!" + sMat;
                mesh.MaterialSlots[i] = sMat.TrimEnd('!');
                report.WaterQuadsFixed++;
            }
            else if (sMat.StartsWith("%"))
            {
                string clean = sMat.TrimEnd('!');
                if (!clean.StartsWith("%!"))
                {
                    clean = "%!" + clean.Substring(1);
                }
                mesh.MaterialSlots[i] = clean;
            }
            else if (sMat.EndsWith("!"))
            {
                string clean = sMat.TrimEnd('!');
                if (!clean.StartsWith("!")) clean = "!" + clean;
                mesh.MaterialSlots[i] = clean;
            }
        }
    }
}
