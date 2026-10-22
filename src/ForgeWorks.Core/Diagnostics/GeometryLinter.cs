using System;
using System.Collections.Generic;
using System.Numerics;
using ForgeWorks.Core.Geometry;

namespace ForgeWorks.Core.Diagnostics;

public enum LintSeverity
{
    Info,
    Warning,
    Error
}

public sealed record LintIssue(
    LintSeverity Severity,
    string Category,
    string Description,
    Vector3 Position,
    int ElementIndex = -1,
    Vector3? SegmentA = null,
    Vector3? SegmentB = null
);

public sealed class GeometryLinter
{
    public const float MinEdgeLength = 0.08f;
    public const float MinEdgeLengthSq = MinEdgeLength * MinEdgeLength;
    public const float MinTriangleArea = 1e-4f;
    public const float BoundsLimit = 150000.0f;

    public static List<LintIssue> LintScene(IEnumerable<MeshObject> meshes, MeshObject? selectedMeshOnly = null, List<string>? materialNames = null)
    {
        var allIssues = new List<LintIssue>();
        var targets = selectedMeshOnly != null ? new[] { selectedMeshOnly } : meshes;
        foreach (var m in targets)
        {
            allIssues.AddRange(LintMesh(m, materialNames));
        }

        // When evaluating the entire scene, edges stitched across adjacent meshes (e.g. terrain perimeter welded to skybox rim)
        // are closed manifold connections in Blam BSP compilation.
        if (selectedMeshOnly == null)
        {
            var openIssues = allIssues.FindAll(i => i.Category == "Open Edge" && i.SegmentA.HasValue && i.SegmentB.HasValue);
            if (openIssues.Count > 0)
            {
                var stitchedIssues = new HashSet<LintIssue>();
                for (int i = 0; i < openIssues.Count; i++)
                {
                    var a = openIssues[i];
                    var a0 = a.SegmentA!.Value;
                    var a1 = a.SegmentB!.Value;

                    for (int j = i + 1; j < openIssues.Count; j++)
                    {
                        var b = openIssues[j];
                        var b0 = b.SegmentA!.Value;
                        var b1 = b.SegmentB!.Value;

                        // Check if edge (a0, a1) matches opposing edge (b1, b0) within 0.15 units
                        if ((Vector3.DistanceSquared(a0, b1) < 0.05f && Vector3.DistanceSquared(a1, b0) < 0.05f) ||
                            (Vector3.DistanceSquared(a0, b0) < 0.05f && Vector3.DistanceSquared(a1, b1) < 0.05f))
                        {
                            stitchedIssues.Add(a);
                            stitchedIssues.Add(b);
                            break;
                        }
                    }
                }

                if (stitchedIssues.Count > 0)
                {
                    allIssues.RemoveAll(i => stitchedIssues.Contains(i));
                }
            }
        }

        return allIssues;
    }

    public static List<LintIssue> LintMesh(MeshObject mesh, List<string>? materialNames = null)
    {
        var issues = new List<LintIssue>();
        if (mesh == null || mesh.Vertices.Count == 0 || mesh.Triangles.Count == 0)
        {
            return issues;
        }

        mesh.RecalculateNormals();

        var edgeMap = new Dictionary<(int, int), List<int>>();
        void AddEdge(int vA, int vB, int triIdx)
        {
            var key = vA < vB ? (vA, vB) : (vB, vA);
            if (!edgeMap.TryGetValue(key, out var list))
            {
                list = new List<int>(2);
                edgeMap[key] = list;
            }
            list.Add(triIdx);
        }

        var seenFaces = new Dictionary<(int, int, int), int>();

        // 1. Degenerate Triangles, Degenerate Faces, Short Collision Edges, Overlapping Surfaces
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];

            // 1a. Degenerate Triangle: identical indices
            if (tri.V0 == tri.V1 || tri.V1 == tri.V2 || tri.V0 == tri.V2)
            {
                var p = mesh.Vertices[tri.V0].Position;
                issues.Add(new LintIssue(
                    LintSeverity.Error,
                    "Degenerate Triangle",
                    $"Triangle #{i} on '{mesh.Name}' has duplicate vertex indices ({tri.V0}, {tri.V1}, {tri.V2})",
                    p,
                    i));
                continue;
            }

            var p0 = mesh.Vertices[tri.V0].Position;
            var p1 = mesh.Vertices[tri.V1].Position;
            var p2 = mesh.Vertices[tri.V2].Position;
            Vector3 center = (p0 + p1 + p2) / 3.0f;

            // 1b. Degenerate Triangle: zero surface area
            Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
            float area = cross.Length() * 0.5f;
            if (area < MinTriangleArea)
            {
                issues.Add(new LintIssue(
                    LintSeverity.Error,
                    "Degenerate Triangle",
                    $"Triangle #{i} on '{mesh.Name}' has near-zero surface area ({area:E3} < {MinTriangleArea})",
                    center,
                    i));
            }

            // 1c. Degenerate Face: needle sliver or collinear vertices
            float e0 = (p1 - p0).Length();
            float e1 = (p2 - p1).Length();
            float e2 = (p0 - p2).Length();
            float perimeter = e0 + e1 + e2;
            if (area > 1e-6f && (perimeter * perimeter / area) > 400.0f)
            {
                issues.Add(new LintIssue(
                    LintSeverity.Error,
                    "Degenerate Face",
                    $"Triangle #{i} on '{mesh.Name}' is an extreme needle sliver (perimeter²/area = {(perimeter * perimeter / area):F1} > 400)",
                    center,
                    i));
            }

            bool isCollideable = mesh.IsTriangleCollideable(tri);

            if (isCollideable)
            {
                if (e0 < MinEdgeLength)
                {
                    issues.Add(new LintIssue(
                        LintSeverity.Warning,
                        "Short Collision Edge",
                        $"Triangle #{i} on '{mesh.Name}' contains short edge (V0-V1: {e0:F3} < {MinEdgeLength} units)",
                        (p0 + p1) * 0.5f, i, p0, p1));
                }
                if (e1 < MinEdgeLength)
                {
                    issues.Add(new LintIssue(
                        LintSeverity.Warning,
                        "Short Collision Edge",
                        $"Triangle #{i} on '{mesh.Name}' contains short edge (V1-V2: {e1:F3} < {MinEdgeLength} units)",
                        (p1 + p2) * 0.5f, i, p1, p2));
                }
                if (e2 < MinEdgeLength)
                {
                    issues.Add(new LintIssue(
                        LintSeverity.Warning,
                        "Short Collision Edge",
                        $"Triangle #{i} on '{mesh.Name}' contains short edge (V2-V0: {e2:F3} < {MinEdgeLength} units)",
                        (p2 + p0) * 0.5f, i, p2, p0));
                }

                // 1e. Overlapping Surfaces: coincident face check
                int a = tri.V0, b = tri.V1, c = tri.V2;
                int minV = Math.Min(a, Math.Min(b, c));
                int maxV = Math.Max(a, Math.Max(b, c));
                int midV = a + b + c - minV - maxV;
                var key = (minV, midV, maxV);

                if (seenFaces.TryGetValue(key, out int existingTri))
                {
                    issues.Add(new LintIssue(
                        LintSeverity.Error,
                        "Overlapping Surfaces",
                        $"Triangle #{i} overlaps identical surface polygon with Triangle #{existingTri} on '{mesh.Name}'",
                        center, i));
                }
                else
                {
                    seenFaces[key] = i;
                }

                AddEdge(tri.V0, tri.V1, i);
                AddEdge(tri.V1, tri.V2, i);
                AddEdge(tri.V2, tri.V0, i);
            }
        }

        // 2. Vertex Bounds Check
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var pos = mesh.Vertices[i].Position;
            if (MathF.Abs(pos.X) > BoundsLimit || MathF.Abs(pos.Y) > BoundsLimit || MathF.Abs(pos.Z) > BoundsLimit)
            {
                issues.Add(new LintIssue(
                    LintSeverity.Error,
                    "Out of Bounds",
                    $"Vertex #{i} on '{mesh.Name}' at ({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1}) exceeds the ±150,000 JMS bounding volume limit",
                    pos,
                    i));
            }
        }

        // 3. Open Edges, Ambiguous Connections & Leaky Connections (Collideable BSP)
        var openEdgeList = new List<((int vA, int vB), Vector3 midPoint)>();

        if (mesh.CheckIsCollideable())
        {
            foreach (var kvp in edgeMap)
            {
                var (vA, vB) = kvp.Key;
                var linkedTris = kvp.Value;
                Vector3 midPoint = (mesh.Vertices[vA].Position + mesh.Vertices[vB].Position) * 0.5f;

                if (linkedTris.Count == 1)
                {
                    openEdgeList.Add((kvp.Key, midPoint));
                    issues.Add(new LintIssue(
                        LintSeverity.Error,
                        "Open Edge",
                        $"Edge between vertices {vA} and {vB} on '{mesh.Name}' is open (linked to only 1 face). Halo BSP requires sealed-world geometry.",
                        midPoint, -1, mesh.Vertices[vA].Position, mesh.Vertices[vB].Position));
                }
                else if (linkedTris.Count > 2)
                {
                    issues.Add(new LintIssue(
                        LintSeverity.Error,
                        "Ambiguous Connection",
                        $"Edge between vertices {vA} and {vB} on '{mesh.Name}' is shared by {linkedTris.Count} faces (> 2). Causes Tool 'Couldn't update edge'.",
                        midPoint, -1, mesh.Vertices[vA].Position, mesh.Vertices[vB].Position));
                }
            }

            // 3b. Leaky Connection Detection (open edges in close proximity forming unsealed slits)
            for (int i = 0; i < openEdgeList.Count; i++)
            {
                var (edgeA, midA) = openEdgeList[i];
                for (int j = i + 1; j < openEdgeList.Count; j++)
                {
                    var (edgeB, midB) = openEdgeList[j];
                    if (edgeA.vA == edgeB.vA || edgeA.vA == edgeB.vB || edgeA.vB == edgeB.vA || edgeA.vB == edgeB.vB)
                        continue;

                    float distSq = (midA - midB).LengthSquared();
                    if (distSq > 0.0025f && distSq <= 144.0f) // 0.05 < distance <= 12.0 units
                    {
                        issues.Add(new LintIssue(
                            LintSeverity.Error,
                            "Leaky Connection",
                            $"Unwelded seam gap ({MathF.Sqrt(distSq):F2} units) between edges ({edgeA.vA}-{edgeA.vB}) and ({edgeB.vA}-{edgeB.vB}) on '{mesh.Name}'. Causes lightmap and collision leaks.",
                            (midA + midB) * 0.5f, -1, midA, midB));
                        break;
                    }
                }
            }
        }

        // 4. Nearly Coplanar Surface (Causes phantom BSP in Tool structure)
        const float MinCoplanarDeg = 0.02f;
        const float MaxCoplanarDeg = 1.5f;
        float minDot = MathF.Cos(MaxCoplanarDeg * MathF.PI / 180.0f);
        float maxDot = MathF.Cos(MinCoplanarDeg * MathF.PI / 180.0f);

        foreach (var kvp in edgeMap)
        {
            var linkedTris = kvp.Value;
            if (linkedTris.Count == 2)
            {
                var norm0 = mesh.Triangles[linkedTris[0]].Normal;
                var norm1 = mesh.Triangles[linkedTris[1]].Normal;

                float dot = Vector3.Dot(norm0, norm1);
                if (dot >= minDot && dot <= maxDot)
                {
                    float angleDeg = MathF.Acos(Math.Clamp(dot, -1.0f, 1.0f)) * (180.0f / MathF.PI);
                    var (vA, vB) = kvp.Key;
                    Vector3 midPoint = (mesh.Vertices[vA].Position + mesh.Vertices[vB].Position) * 0.5f;
                    issues.Add(new LintIssue(
                        LintSeverity.Warning,
                        "Nearly Coplanar Surface",
                        $"Faces #{linkedTris[0]} and #{linkedTris[1]} differ by {angleDeg:F2}° (nearly coplanar: 0.02° - 1.5°). Warning triggers Phantom BSP in Tool.",
                        midPoint, -1, mesh.Vertices[vA].Position, mesh.Vertices[vB].Position));
                }
            }
        }

        // 5. Water UV Compliance
        bool isWater = mesh.Name.Contains("water", StringComparison.OrdinalIgnoreCase) ||
                       mesh.Name.StartsWith("!water", StringComparison.OrdinalIgnoreCase);
        if (isWater)
        {
            int uvCount = mesh.Vertices.Count > 0 ? mesh.Vertices[0].UVs.Count : 0;
            if (uvCount != 4)
            {
                issues.Add(new LintIssue(
                    LintSeverity.Error,
                    "Water UV Mismatch",
                    $"Water mesh '{mesh.Name}' has {uvCount} UV layers. Halo engine requires exactly 4 UV layers (UV_0, UV_1, UV_2, UV_3) for water simulation.",
                    Vector3.Zero));
            }
        }

        // 6. Material Name Linter
        if (materialNames != null)
        {
            for (int i = 0; i < materialNames.Count; i++)
            {
                string mat = materialNames[i];
                if (string.IsNullOrWhiteSpace(mat))
                {
                    issues.Add(new LintIssue(
                        LintSeverity.Error,
                        "Invalid Material Name",
                        $"Material slot #{i} has an empty or null name.",
                        Vector3.Zero));
                    continue;
                }

                if (mat.Length > 32)
                {
                    issues.Add(new LintIssue(
                        LintSeverity.Warning,
                        "Material Name Too Long",
                        $"Material '{mat}' exceeds 32 characters ({mat.Length}). May truncate in legacy Blam engine buffers.",
                        Vector3.Zero));
                }

                bool hasSpecialSymbol = mat.StartsWith("+") || mat.StartsWith("!") || mat.StartsWith("%") ||
                                       mat.StartsWith("~") || mat.StartsWith("*") || mat.StartsWith("@") || mat.StartsWith("$");
                if (!hasSpecialSymbol && mat.ToLowerInvariant() != mat)
                {
                    issues.Add(new LintIssue(
                        LintSeverity.Info,
                        "Material Capitalization",
                        $"Material '{mat}' contains uppercase characters. Blam convention recommends lowercase to match tag directory paths.",
                        Vector3.Zero));
                }
            }
        }

        return issues;
    }
}
