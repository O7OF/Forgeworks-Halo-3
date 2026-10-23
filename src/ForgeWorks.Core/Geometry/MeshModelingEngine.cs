using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

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
}
