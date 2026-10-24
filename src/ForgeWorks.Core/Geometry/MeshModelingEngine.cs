using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ForgeWorks.Core.Diagnostics;

namespace ForgeWorks.Core.Geometry;

public enum SubElementSelectionMode
{
    Object = 0,
    Vertex = 1,
    Edge = 2,
    Face = 3
}

public readonly record struct MeshEdge(int V0, int V1)
{
    public int Min => Math.Min(V0, V1);
    public int Max => Math.Max(V0, V1);

    public bool Equals(MeshEdge other) => Min == other.Min && Max == other.Max;
    public override int GetHashCode() => HashCode.Combine(Min, Max);
}

public sealed record ModelOptimizationReport(
    int OriginalVertices,
    int OptimizedVertices,
    int OriginalTriangles,
    int OptimizedTriangles,
    int EdgesDissolved,
    int CoplanarFacesMerged
)
{
    public float VertexReductionPercent => OriginalVertices > 0
        ? ((OriginalVertices - OptimizedVertices) / (float)OriginalVertices) * 100.0f
        : 0f;

    public float TriangleReductionPercent => OriginalTriangles > 0
        ? ((OriginalTriangles - OptimizedTriangles) / (float)OriginalTriangles) * 100.0f
        : 0f;
}

/// <summary>
/// Professional DCC modeling suite for Halo 3 Blam! engine level design (Blockbench, Blender & Godot style).
/// Implements sub-element face/edge/vertex selection, extrusion, insetting, edge loop cuts,
/// edge/face dissolution, and normal flipping.
/// </summary>
public static class MeshModelingEngine
{
    /// <summary>
    /// Extrudes the selected triangles along their average normal by distance.
    /// Creates watertight bridge quads along the boundary perimeter of the selected faces.
    /// Returns the new triangle indices corresponding to the extruded cap.
    /// </summary>
    public static List<int> ExtrudeFaces(MeshObject mesh, IEnumerable<int> faceIndices, float distance)
    {
        var faceSet = new HashSet<int>(faceIndices.Where(i => i >= 0 && i < mesh.Triangles.Count));
        if (faceSet.Count == 0) return new List<int>();

        // 1. Calculate average normal of selected faces
        Vector3 avgNormal = Vector3.Zero;
        foreach (int fIdx in faceSet)
        {
            avgNormal += mesh.Triangles[fIdx].Normal;
        }
        if (avgNormal.LengthSquared() > 1e-6f)
            avgNormal = Vector3.Normalize(avgNormal);
        else
            avgNormal = Vector3.UnitZ;

        Vector3 offset = avgNormal * distance;

        // 2. Identify boundary edges of the selected face island.
        // A boundary edge belongs to exactly ONE triangle in the selection set.
        var edgeCount = new Dictionary<MeshEdge, (int vA, int vB, int count)>();
        foreach (int fIdx in faceSet)
        {
            var t = mesh.Triangles[fIdx];
            AddDirectedEdge(edgeCount, t.V0, t.V1);
            AddDirectedEdge(edgeCount, t.V1, t.V2);
            AddDirectedEdge(edgeCount, t.V2, t.V0);
        }

        var boundaryEdges = new List<(int vA, int vB)>();
        foreach (var kvp in edgeCount)
        {
            if (kvp.Value.count == 1)
            {
                boundaryEdges.Add((kvp.Value.vA, kvp.Value.vB));
            }
        }

        // 3. Collect unique vertices used by the selected faces and duplicate them for the cap
        var vertexMap = new Dictionary<int, int>(); // Old index -> New cap index
        foreach (int fIdx in faceSet)
        {
            var t = mesh.Triangles[fIdx];
            if (!vertexMap.ContainsKey(t.V0)) vertexMap[t.V0] = DuplicateVertex(mesh, t.V0, offset);
            if (!vertexMap.ContainsKey(t.V1)) vertexMap[t.V1] = DuplicateVertex(mesh, t.V1, offset);
            if (!vertexMap.ContainsKey(t.V2)) vertexMap[t.V2] = DuplicateVertex(mesh, t.V2, offset);
        }

        // 4. Update selected triangles to use the newly extruded vertices (moving the cap outward)
        var newFaceIndices = new List<int>();
        foreach (int fIdx in faceSet)
        {
            var t = mesh.Triangles[fIdx];
            t.V0 = vertexMap[t.V0];
            t.V1 = vertexMap[t.V1];
            t.V2 = vertexMap[t.V2];
            newFaceIndices.Add(fIdx);
        }

        // 5. Build perimeter skirt quads along each boundary edge (vA -> vB)
        // vA_old -> vB_old -> vB_new -> vA_new
        foreach (var (vA, vB) in boundaryEdges)
        {
            int vA_new = vertexMap[vA];
            int vB_new = vertexMap[vB];

            int matIdx = mesh.Triangles[faceSet.First()].MaterialIndex;
            // Quad split into 2 triangles CCW
            mesh.Triangles.Add(new Triangle(vA, vB, vB_new, matIdx));
            mesh.Triangles.Add(new Triangle(vA, vB_new, vA_new, matIdx));
        }

        mesh.RecalculateNormals();
        return newFaceIndices;
    }

    private static void AddDirectedEdge(Dictionary<MeshEdge, (int vA, int vB, int count)> dict, int vA, int vB)
    {
        var key = new MeshEdge(vA, vB);
        if (dict.TryGetValue(key, out var val))
        {
            dict[key] = (val.vA, val.vB, val.count + 1);
        }
        else
        {
            dict[key] = (vA, vB, 1);
        }
    }

    private static int DuplicateVertex(MeshObject mesh, int oldIdx, Vector3 offset)
    {
        var old = mesh.Vertices[oldIdx];
        var nv = new Vertex(old.Position + offset, old.Normal)
        {
            Color = old.Color
        };
        foreach (var uv in old.UVs) nv.UVs.Add(uv);

        int newIdx = mesh.Vertices.Count;
        mesh.Vertices.Add(nv);
        return newIdx;
    }

    /// <summary>
    /// Insets the selected faces inward toward their centroid, creating an outer border quad frame.
    /// </summary>
    public static List<int> InsetFaces(MeshObject mesh, IEnumerable<int> faceIndices, float insetAmount)
    {
        var faceSet = new HashSet<int>(faceIndices.Where(i => i >= 0 && i < mesh.Triangles.Count));
        if (faceSet.Count == 0) return new List<int>();

        var newCapFaces = new List<int>();

        foreach (int fIdx in faceSet)
        {
            var t = mesh.Triangles[fIdx];
            int oldV0 = t.V0;
            int oldV1 = t.V1;
            int oldV2 = t.V2;

            var p0 = mesh.Vertices[oldV0].Position;
            var p1 = mesh.Vertices[oldV1].Position;
            var p2 = mesh.Vertices[oldV2].Position;
            var centroid = (p0 + p1 + p2) / 3.0f;

            // Inset factor (0..1)
            float maxDist = Math.Max(Vector3.Distance(p0, centroid), Math.Max(Vector3.Distance(p1, centroid), Vector3.Distance(p2, centroid)));
            float factor = maxDist > 1e-4f ? Math.Clamp(1.0f - (insetAmount / maxDist), 0.05f, 0.95f) : 0.8f;

            var in0 = Vector3.Lerp(centroid, p0, factor);
            var in1 = Vector3.Lerp(centroid, p1, factor);
            var in2 = Vector3.Lerp(centroid, p2, factor);

            int idxIn0 = DuplicateVertex(mesh, oldV0, in0 - p0);
            int idxIn1 = DuplicateVertex(mesh, oldV1, in1 - p1);
            int idxIn2 = DuplicateVertex(mesh, oldV2, in2 - p2);

            // Update original face to be inner cap
            t.V0 = idxIn0;
            t.V1 = idxIn1;
            t.V2 = idxIn2;
            newCapFaces.Add(fIdx);

            // Add 3 border quads (6 triangles)
            int mat = t.MaterialIndex;
            // Edge 0-1
            mesh.Triangles.Add(new Triangle(oldV0, oldV1, idxIn1, mat));
            mesh.Triangles.Add(new Triangle(oldV0, idxIn1, idxIn0, mat));
            // Edge 1-2
            mesh.Triangles.Add(new Triangle(oldV1, oldV2, idxIn2, mat));
            mesh.Triangles.Add(new Triangle(oldV1, idxIn2, idxIn1, mat));
            // Edge 2-0
            mesh.Triangles.Add(new Triangle(oldV2, oldV0, idxIn0, mat));
            mesh.Triangles.Add(new Triangle(oldV2, idxIn0, idxIn2, mat));
        }

        mesh.RecalculateNormals();
        return newCapFaces;
    }

    /// <summary>
    /// Performs an edge loop cut slicing the mesh with an axis-aligned plane through slicePoint.
    /// Triangles crossing the plane are subdivided cleanly with newly created edge loop vertices.
    /// </summary>
    public static int LoopCutMesh(MeshObject mesh, Vector3 slicePoint, Vector3 planeNormal)
    {
        if (planeNormal.LengthSquared() < 1e-6f) planeNormal = Vector3.UnitZ;
        planeNormal = Vector3.Normalize(planeNormal);
        float planeD = -Vector3.Dot(planeNormal, slicePoint);

        float DistanceToPlane(Vector3 p) => Vector3.Dot(planeNormal, p) + planeD;

        var newTris = new List<Triangle>();
        var removeTris = new HashSet<int>();
        var edgeSplitMap = new Dictionary<MeshEdge, int>();

        int GetOrCreateSplitVertex(int vA, int vB)
        {
            var key = new MeshEdge(vA, vB);
            if (edgeSplitMap.TryGetValue(key, out int splitIdx)) return splitIdx;

            var pA = mesh.Vertices[vA].Position;
            var pB = mesh.Vertices[vB].Position;
            float dA = DistanceToPlane(pA);
            float dB = DistanceToPlane(pB);
            float t = Math.Clamp(dA / (dA - dB), 0.001f, 0.999f);

            var pos = Vector3.Lerp(pA, pB, t);
            var normal = Vector3.Normalize(Vector3.Lerp(mesh.Vertices[vA].Normal, mesh.Vertices[vB].Normal, t));
            var uvA = mesh.Vertices[vA].UVs.Count > 0 ? mesh.Vertices[vA].UVs[0] : Vector2.Zero;
            var uvB = mesh.Vertices[vB].UVs.Count > 0 ? mesh.Vertices[vB].UVs[0] : Vector2.Zero;
            var uv = Vector2.Lerp(uvA, uvB, t);

            var v = new Vertex(pos, normal, uv)
            {
                Color = Vector4.Lerp(mesh.Vertices[vA].Color, mesh.Vertices[vB].Color, t)
            };

            int idx = mesh.Vertices.Count;
            mesh.Vertices.Add(v);
            edgeSplitMap[key] = idx;
            return idx;
        }

        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            float d0 = DistanceToPlane(mesh.Vertices[tri.V0].Position);
            float d1 = DistanceToPlane(mesh.Vertices[tri.V1].Position);
            float d2 = DistanceToPlane(mesh.Vertices[tri.V2].Position);

            bool s0 = d0 > 1e-4f;
            bool s1 = d1 > 1e-4f;
            bool s2 = d2 > 1e-4f;

            // If all 3 vertices are on same side of plane, no cut needed for this triangle
            if ((s0 && s1 && s2) || (!s0 && !s1 && !s2))
                continue;

            removeTris.Add(i);

            // Reorder so that v0 is the isolated vertex on one side
            int v0 = tri.V0, v1 = tri.V1, v2 = tri.V2;
            if (s1 != s0 && s1 != s2)
            {
                v0 = tri.V1; v1 = tri.V2; v2 = tri.V0;
            }
            else if (s2 != s0 && s2 != s1)
            {
                v0 = tri.V2; v1 = tri.V0; v2 = tri.V1;
            }

            int split01 = GetOrCreateSplitVertex(v0, v1);
            int split02 = GetOrCreateSplitVertex(v0, v2);

            // 1 triangle on v0 side, 2 triangles forming quad on v1-v2 side
            newTris.Add(new Triangle(v0, split01, split02, tri.MaterialIndex));
            newTris.Add(new Triangle(split01, v1, v2, tri.MaterialIndex));
            newTris.Add(new Triangle(split01, v2, split02, tri.MaterialIndex));
        }

        if (removeTris.Count == 0) return 0;

        // Rebuild triangle list
        var surviving = new List<Triangle>(mesh.Triangles.Count);
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            if (!removeTris.Contains(i)) surviving.Add(mesh.Triangles[i]);
        }
        surviving.AddRange(newTris);
        mesh.Triangles = surviving;

        mesh.RecalculateNormals();
        return edgeSplitMap.Count;
    }

    /// <summary>
    /// Dissolves the specified edge by collapsing/welding the two vertices into one.
    /// </summary>
    public static bool DissolveEdge(MeshObject mesh, MeshEdge edge)
    {
        if (edge.V0 < 0 || edge.V0 >= mesh.Vertices.Count || edge.V1 < 0 || edge.V1 >= mesh.Vertices.Count)
            return false;

        int keepIdx = edge.V0;
        int removeIdx = edge.V1;

        // Position welded at midpoint
        mesh.Vertices[keepIdx].Position = (mesh.Vertices[keepIdx].Position + mesh.Vertices[removeIdx].Position) * 0.5f;

        var surviving = new List<Triangle>();
        foreach (var t in mesh.Triangles)
        {
            int v0 = t.V0 == removeIdx ? keepIdx : t.V0;
            int v1 = t.V1 == removeIdx ? keepIdx : t.V1;
            int v2 = t.V2 == removeIdx ? keepIdx : t.V2;

            // Discard degenerate triangles that collapsed onto the edge
            if (v0 != v1 && v1 != v2 && v2 != v0)
            {
                surviving.Add(new Triangle(v0, v1, v2, t.MaterialIndex));
            }
        }

        mesh.Triangles = surviving;
        mesh.RecalculateNormals();
        return true;
    }

    /// <summary>
    /// Deletes the specified faces and cleans up degenerate geometry.
    /// </summary>
    public static int DeleteFaces(MeshObject mesh, IEnumerable<int> faceIndices)
    {
        var toRemove = new HashSet<int>(faceIndices);
        if (toRemove.Count == 0) return 0;

        var surviving = new List<Triangle>();
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            if (!toRemove.Contains(i)) surviving.Add(mesh.Triangles[i]);
        }

        int removed = mesh.Triangles.Count - surviving.Count;
        mesh.Triangles = surviving;
        mesh.RecalculateNormals();
        return removed;
    }

    /// <summary>
    /// Inverts the triangle winding order (V0, V2, V1) and normals for selected faces.
    /// </summary>
    public static void FlipNormals(MeshObject mesh, IEnumerable<int> faceIndices)
    {
        foreach (int idx in faceIndices)
        {
            if (idx >= 0 && idx < mesh.Triangles.Count)
            {
                var t = mesh.Triangles[idx];
                int tmp = t.V1;
                t.V1 = t.V2;
                t.V2 = tmp;
                t.Normal = -t.Normal;
            }
        }
    }

    /// <summary>
    /// Subdivides selected faces (1 triangle -> 4 triangles) by splitting all 3 edges at midpoints.
    /// </summary>
    public static int SubdivideFaces(MeshObject mesh, IEnumerable<int> faceIndices)
    {
        var selected = new HashSet<int>(faceIndices.Where(i => i >= 0 && i < mesh.Triangles.Count));
        if (selected.Count == 0) return 0;

        var edgeMidpoints = new Dictionary<MeshEdge, int>();

        int GetMidpoint(int vA, int vB)
        {
            var key = new MeshEdge(vA, vB);
            if (edgeMidpoints.TryGetValue(key, out int mid)) return mid;

            var pA = mesh.Vertices[vA];
            var pB = mesh.Vertices[vB];
            var midPos = (pA.Position + pB.Position) * 0.5f;
            var midNorm = Vector3.Normalize(pA.Normal + pB.Normal);
            var uvA = pA.UVs.Count > 0 ? pA.UVs[0] : Vector2.Zero;
            var uvB = pB.UVs.Count > 0 ? pB.UVs[0] : Vector2.Zero;
            var midUv = (uvA + uvB) * 0.5f;

            var v = new Vertex(midPos, midNorm, midUv)
            {
                Color = (pA.Color + pB.Color) * 0.5f
            };

            int idx = mesh.Vertices.Count;
            mesh.Vertices.Add(v);
            edgeMidpoints[key] = idx;
            return idx;
        }

        var surviving = new List<Triangle>();
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var t = mesh.Triangles[i];
            if (!selected.Contains(i))
            {
                surviving.Add(t);
                continue;
            }

            int m01 = GetMidpoint(t.V0, t.V1);
            int m12 = GetMidpoint(t.V1, t.V2);
            int m20 = GetMidpoint(t.V2, t.V0);

            // 4 sub-triangles
            surviving.Add(new Triangle(t.V0, m01, m20, t.MaterialIndex));
            surviving.Add(new Triangle(m01, t.V1, m12, t.MaterialIndex));
            surviving.Add(new Triangle(m20, m12, t.V2, t.MaterialIndex));
            surviving.Add(new Triangle(m01, m12, m20, t.MaterialIndex));
        }

        mesh.Triangles = surviving;
        mesh.RecalculateNormals();
        return selected.Count * 3;
    }

    /// <summary>
    /// Combines multiple mesh objects into a single clean unified mesh,
    /// consolidating material slots, remapping triangle indices, and welding
    /// coincident boundary seam vertices.
    /// </summary>
    public static MeshObject CleanMergeMeshes(IEnumerable<MeshObject> meshes, string mergedName = "merged_mesh", float weldDist = 0.05f)
    {
        var meshList = meshes.Where(m => m != null && m.Vertices.Count > 0 && m.Triangles.Count > 0).ToList();
        if (meshList.Count == 0) return new MeshObject(mergedName);
        if (meshList.Count == 1) return meshList[0];

        var merged = new MeshObject(mergedName);
        var materialMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int AddOrGetMaterial(string mat)
        {
            if (string.IsNullOrWhiteSpace(mat)) mat = "river riverworld_ground";
            if (!materialMap.TryGetValue(mat, out int idx))
            {
                idx = merged.MaterialSlots.Count;
                merged.MaterialSlots.Add(mat);
                materialMap[mat] = idx;
            }
            return idx;
        }

        merged.MaterialName = meshList[0].MaterialName;
        AddOrGetMaterial(merged.MaterialName);

        foreach (var m in meshList)
        {
            uint baseIdx = (uint)merged.Vertices.Count;

            // Copy vertices with UVs and colors
            foreach (var v in m.Vertices)
            {
                var newV = new Vertex(v.Position, v.Normal)
                {
                    Color = v.Color,
                    UVs = new List<Vector2>(v.UVs)
                };
                merged.Vertices.Add(newV);
            }

            // Remap triangle material indices
            foreach (var tri in m.Triangles)
            {
                string mat = (tri.MaterialIndex >= 0 && tri.MaterialIndex < m.MaterialSlots.Count)
                    ? m.MaterialSlots[tri.MaterialIndex]
                    : m.MaterialName;

                int mergedMatIdx = AddOrGetMaterial(mat);
                merged.Triangles.Add(new Triangle(
                    (int)(baseIdx + tri.V0),
                    (int)(baseIdx + tri.V1),
                    (int)(baseIdx + tri.V2),
                    mergedMatIdx)
                {
                    Normal = tri.Normal
                });
            }
        }

        // Weld coincident boundary seams across joined sub-meshes
        if (weldDist > 0)
        {
            GeometryAutoRepair.WeldVertices(merged, weldDist, new RepairReport());
            GeometryAutoRepair.PruneLooseVertices(merged);
        }

        merged.RecalculateNormals();
        return merged;
    }

    /// <summary>
    /// Mathematically dissolves coplanar interior edges without altering curved geometry
    /// or boundary silhouettes. Collapses flat interior vertices where all incident triangles
    /// lie on the same plane (within maxAngleDeg) and share the same material.
    /// </summary>
    public static int DissolveCoplanarEdges(MeshObject mesh, float maxAngleDeg = 0.5f)
    {
        if (mesh == null || mesh.Triangles.Count == 0 || mesh.Vertices.Count == 0) return 0;

        float minDot = MathF.Cos(maxAngleDeg * MathF.PI / 180.0f);
        int totalDissolved = 0;

        // Perform multiple passes until no further interior coplanar vertices can be collapsed
        for (int pass = 0; pass < 8; pass++)
        {
            // 1. Identify boundary vertices (never collapse boundary perimeter vertices)
            var edgeCount = new Dictionary<MeshEdge, int>();
            foreach (var tri in mesh.Triangles)
            {
                void AddE(int a, int b)
                {
                    var e = new MeshEdge(a, b);
                    edgeCount.TryGetValue(e, out int c);
                    edgeCount[e] = c + 1;
                }
                AddE(tri.V0, tri.V1);
                AddE(tri.V1, tri.V2);
                AddE(tri.V2, tri.V0);
            }

            var boundaryVerts = new HashSet<int>();
            foreach (var (edge, count) in edgeCount)
            {
                if (count == 1)
                {
                    boundaryVerts.Add(edge.V0);
                    boundaryVerts.Add(edge.V1);
                }
            }

            // 2. Build vertex to incident triangles adjacency
            var vertTris = new Dictionary<int, List<int>>();
            for (int tIdx = 0; tIdx < mesh.Triangles.Count; tIdx++)
            {
                var t = mesh.Triangles[tIdx];
                void Link(int v)
                {
                    if (!vertTris.TryGetValue(v, out var list))
                    {
                        list = new List<int>(6);
                        vertTris[v] = list;
                    }
                    list.Add(tIdx);
                }
                Link(t.V0);
                Link(t.V1);
                Link(t.V2);
            }

            int passDissolved = 0;
            var collapsed = new HashSet<int>();

            // 3. Find candidates for coplanar dissolution
            foreach (var (vIdx, incident) in vertTris)
            {
                if (boundaryVerts.Contains(vIdx) || collapsed.Contains(vIdx) || incident.Count < 3)
                    continue;

                // Ensure all incident triangles share the same material
                int firstMat = mesh.Triangles[incident[0]].MaterialIndex;
                bool sameMat = incident.All(ti => mesh.Triangles[ti].MaterialIndex == firstMat);
                if (!sameMat) continue;

                // Calculate average normal of incident ring
                var avgNorm = Vector3.Zero;
                foreach (int ti in incident)
                {
                    avgNorm += mesh.Triangles[ti].Normal;
                }
                if (avgNorm.LengthSquared() < 1e-4f) continue;
                avgNorm = Vector3.Normalize(avgNorm);

                // Check that every single incident triangle is coplanar with avgNorm
                bool isCoplanar = incident.All(ti => Vector3.Dot(mesh.Triangles[ti].Normal, avgNorm) >= minDot);
                if (!isCoplanar) continue;

                // Find valid neighbor to collapse into
                var neighbors = new HashSet<int>();
                foreach (int ti in incident)
                {
                    var t = mesh.Triangles[ti];
                    if (t.V0 != vIdx) neighbors.Add(t.V0);
                    if (t.V1 != vIdx) neighbors.Add(t.V1);
                    if (t.V2 != vIdx) neighbors.Add(t.V2);
                }

                int bestTarget = -1;
                float bestDist = float.MaxValue;
                var vPos = mesh.Vertices[vIdx].Position;

                foreach (int nIdx in neighbors)
                {
                    if (collapsed.Contains(nIdx)) continue;

                    // Verify that collapsing vIdx -> nIdx preserves normal direction for surviving faces
                    bool valid = true;
                    var nPos = mesh.Vertices[nIdx].Position;

                    foreach (int ti in incident)
                    {
                        var t = mesh.Triangles[ti];
                        if (t.V0 == nIdx || t.V1 == nIdx || t.V2 == nIdx)
                            continue; // This face collapses to zero area (will be removed cleanly)

                        var p0 = t.V0 == vIdx ? nPos : mesh.Vertices[t.V0].Position;
                        var p1 = t.V1 == vIdx ? nPos : mesh.Vertices[t.V1].Position;
                        var p2 = t.V2 == vIdx ? nPos : mesh.Vertices[t.V2].Position;

                        var newCross = Vector3.Cross(p1 - p0, p2 - p0);
                        if (newCross.LengthSquared() < 1e-6f || Vector3.Dot(Vector3.Normalize(newCross), avgNorm) < 0.5f)
                        {
                            valid = false;
                            break;
                        }
                    }

                    if (valid)
                    {
                        float d = (nPos - vPos).LengthSquared();
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestTarget = nIdx;
                        }
                    }
                }

                if (bestTarget != -1)
                {
                    // Collapse vIdx into bestTarget
                    collapsed.Add(vIdx);
                    collapsed.Add(bestTarget);

                    // Remap in triangles
                    for (int i = 0; i < mesh.Triangles.Count; i++)
                    {
                        var t = mesh.Triangles[i];
                        int v0 = t.V0 == vIdx ? bestTarget : t.V0;
                        int v1 = t.V1 == vIdx ? bestTarget : t.V1;
                        int v2 = t.V2 == vIdx ? bestTarget : t.V2;
                        if (v0 != t.V0 || v1 != t.V1 || v2 != t.V2)
                        {
                            mesh.Triangles[i] = new Triangle(v0, v1, v2, t.MaterialIndex) { Normal = t.Normal };
                        }
                    }

                    passDissolved++;
                }
            }

            // Remove collapsed zero-area triangles
            var survivingTris = new List<Triangle>(mesh.Triangles.Count);
            foreach (var t in mesh.Triangles)
            {
                if (t.V0 != t.V1 && t.V1 != t.V2 && t.V0 != t.V2)
                {
                    survivingTris.Add(t);
                }
            }
            mesh.Triangles = survivingTris;

            totalDissolved += passDissolved;
            if (passDissolved == 0) break;
        }

        GeometryAutoRepair.PruneLooseVertices(mesh);
        mesh.RecalculateNormals();
        return totalDissolved;
    }

    /// <summary>
    /// Dissolves redundant collinear vertices along straight boundary or interior seams.
    /// Eliminates vertices that sit along a straight line (180 deg) without changing shape.
    /// </summary>
    public static int DissolveCollinearVertices(MeshObject mesh, float maxAngleDeg = 0.5f)
    {
        if (mesh == null || mesh.Triangles.Count == 0 || mesh.Vertices.Count == 0) return 0;

        float maxDot = -MathF.Cos(maxAngleDeg * MathF.PI / 180.0f); // ~ -0.99996
        int dissolved = 0;

        var edgeCount = new Dictionary<MeshEdge, int>();
        foreach (var tri in mesh.Triangles)
        {
            void AddE(int a, int b)
            {
                var e = new MeshEdge(a, b);
                edgeCount.TryGetValue(e, out int c);
                edgeCount[e] = c + 1;
            }
            AddE(tri.V0, tri.V1);
            AddE(tri.V1, tri.V2);
            AddE(tri.V2, tri.V0);
        }

        // Adjacency for boundary edges
        var boundaryAdj = new Dictionary<int, List<int>>();
        foreach (var (edge, count) in edgeCount)
        {
            if (count == 1)
            {
                if (!boundaryAdj.TryGetValue(edge.V0, out var l0)) { l0 = new List<int>(2); boundaryAdj[edge.V0] = l0; }
                l0.Add(edge.V1);
                if (!boundaryAdj.TryGetValue(edge.V1, out var l1)) { l1 = new List<int>(2); boundaryAdj[edge.V1] = l1; }
                l1.Add(edge.V0);
            }
        }

        var toCollapse = new Dictionary<int, int>();
        foreach (var (vIdx, neighbors) in boundaryAdj)
        {
            if (neighbors.Count == 2 && !toCollapse.ContainsKey(neighbors[0]) && !toCollapse.ContainsKey(neighbors[1]))
            {
                var p = mesh.Vertices[vIdx].Position;
                var p0 = mesh.Vertices[neighbors[0]].Position;
                var p1 = mesh.Vertices[neighbors[1]].Position;

                var d0 = Vector3.Normalize(p0 - p);
                var d1 = Vector3.Normalize(p1 - p);

                if (Vector3.Dot(d0, d1) <= maxDot) // Collinear (180 degrees)
                {
                    toCollapse[vIdx] = neighbors[0];
                    dissolved++;
                }
            }
        }

        if (toCollapse.Count > 0)
        {
            var validTris = new List<Triangle>(mesh.Triangles.Count);
            foreach (var t in mesh.Triangles)
            {
                int v0 = toCollapse.TryGetValue(t.V0, out int r0) ? r0 : t.V0;
                int v1 = toCollapse.TryGetValue(t.V1, out int r1) ? r1 : t.V1;
                int v2 = toCollapse.TryGetValue(t.V2, out int r2) ? r2 : t.V2;

                if (v0 != v1 && v1 != v2 && v0 != v2)
                {
                    validTris.Add(new Triangle(v0, v1, v2, t.MaterialIndex));
                }
            }
            mesh.Triangles = validTris;
            GeometryAutoRepair.PruneLooseVertices(mesh);
            mesh.RecalculateNormals();
        }

        return dissolved;
    }

    /// <summary>
    /// Master optimization suite for lowering vertex/triangle counts and dissolving redundant cuts:
    /// 1. Pre-welds coincident vertices.
    /// 2. Iteratively dissolves coplanar interior edges.
    /// 3. Dissolves straight-line collinear boundary vertices.
    /// 4. Removes degenerate faces and prunes unreferenced vertices.
    /// </summary>
    public static ModelOptimizationReport OptimizeAndDissolveModel(MeshObject mesh, float maxAngleDeg = 0.5f, float weldDist = 0.05f)
    {
        if (mesh == null) return new ModelOptimizationReport(0, 0, 0, 0, 0, 0);

        int origVerts = mesh.Vertices.Count;
        int origTris = mesh.Triangles.Count;

        // Pass 1: Weld coincident vertices
        if (weldDist > 0)
        {
            GeometryAutoRepair.WeldVertices(mesh, weldDist, new RepairReport());
        }

        // Pass 2: Planar edge decimation
        int dissolvedEdges = DissolveCoplanarEdges(mesh, maxAngleDeg);

        // Pass 3: Collinear boundary vertex simplification
        int dissolvedCollinear = DissolveCollinearVertices(mesh, maxAngleDeg);

        // Pass 4: Clean up zero-area degenerate triangles
        GeometryAutoRepair.FixDegenerateTriangles(mesh, new RepairReport());
        GeometryAutoRepair.PruneLooseVertices(mesh);
        mesh.RecalculateNormals();

        int finalVerts = mesh.Vertices.Count;
        int finalTris = mesh.Triangles.Count;

        return new ModelOptimizationReport(
            origVerts,
            finalVerts,
            origTris,
            finalTris,
            dissolvedEdges + dissolvedCollinear,
            origTris - finalTris
        );
    }

    /// <summary>
    /// Auto-fits UV coordinates to geometry using distortion-free 1:1 triplanar world mapping.
    /// Eliminates texture stretching along vertical cliff faces, walls, and horizontal ground planes.
    /// </summary>
    public static void AutoFitTriplanarUVs(MeshObject mesh, float tileScale = 300.0f)
    {
        if (mesh == null || mesh.Vertices.Count == 0) return;
        if (tileScale <= 0f) tileScale = 300.0f;

        mesh.RecalculateNormals();

        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var v = mesh.Vertices[i];
            var uv = ForgeWorks.FSharp.Math3D.calculateTriplanarUV(v.Position, v.Normal, tileScale);

            if (mesh.Vertices[i].UVs.Count == 0)
            {
                mesh.Vertices[i].UVs.Add(uv);
            }
            else
            {
                mesh.Vertices[i].UVs[0] = uv;
            }
        }
    }

    /// <summary>
    /// Computes the collective bounding box of all meshes in the scene and scales them uniformly
    /// so the map fits within the target grid extent (corner to corner), centering the map at (0, 0)
    /// and placing its ground base at Z = 0. Uses high-performance F# Math3D for vector computation.
    /// </summary>
    public static float FitSceneToGrid(
        IEnumerable<MeshObject> meshes,
        float targetGridExtent = 7000.0f,
        bool centerAtOrigin = true,
        bool alignBaseToZero = true,
        bool autoFitUvs = true)
    {
        var meshList = meshes.ToList();
        if (meshList.Count == 0) return 1.0f;

        var points = meshList.SelectMany(m => m.Vertices.Select(v => v.Position));
        var bounds = ForgeWorks.FSharp.Math3D.BoundingBox3D.FromPoints(points);

        if (bounds.IsEmpty || bounds.MaxHorizontalSpan <= 1e-4f) return 1.0f;

        var fit = ForgeWorks.FSharp.Math3D.calculateGridFit(bounds, targetGridExtent, centerAtOrigin, alignBaseToZero);

        foreach (var m in meshList)
        {
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                var v = m.Vertices[i];
                var newPos = fit.TransformPoint(v.Position);

                m.Vertices[i] = new Vertex(newPos, v.Normal, v.UVs.Count > 0 ? v.UVs[0] : null)
                {
                    Color = v.Color
                };

                for (int u = 1; u < v.UVs.Count; u++)
                {
                    m.Vertices[i].UVs.Add(v.UVs[u]);
                }
            }

            m.RecalculateNormals();

            if (autoFitUvs)
            {
                AutoFitTriplanarUVs(m, 300.0f);
            }
        }

        return fit.Scale;
    }
}
