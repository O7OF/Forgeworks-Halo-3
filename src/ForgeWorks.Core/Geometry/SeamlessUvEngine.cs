using System;
using System.Collections.Generic;
using System.Numerics;

namespace ForgeWorks.Core.Geometry;

public enum UvProjectionMode
{
    TriplanarSeamless = 0,
    BoxSeamless = 1,
    PlanarXy = 2,
    PlanarXz = 3,
    PlanarYz = 4,
    FaceNormal = 5
}

/// <summary>
/// Professional seamless texturing and UV mapping/remapping engine for Halo Blam! geometry.
/// Eliminates texture tearing, stretching along cliff walls, and seam discontinuities.
/// Supports continuous world-space triplanar mapping, box mapping, planar mapping,
/// and selective face UV remapping with scale, offset, and rotation.
/// </summary>
public static class SeamlessUvEngine
{
    /// <summary>
    /// Applies 1:1 seamless triplanar world-space UV projection to the entire mesh.
    /// Automatically detects dominant plane for each face (XY floor, YZ X-wall, XZ Y-wall)
    /// and splits vertices along UV seams if requested so texture coordinates never distort.
    /// </summary>
    public static void ApplySeamlessUvs(MeshObject mesh, float tileScale = 300.0f, bool splitUvSeams = false)
    {
        if (mesh == null || mesh.Vertices.Count == 0 || mesh.Triangles.Count == 0) return;
        if (tileScale <= 0f) tileScale = 300.0f;

        mesh.RecalculateNormals();

        if (splitUvSeams)
        {
            ApplySeamSplitTriplanarUvs(mesh, tileScale);
        }
        else
        {
            ApplyContinuousTriplanarUvs(mesh, tileScale);
        }
    }

    /// <summary>
    /// Continuous triplanar unwrap without duplicating vertices.
    /// Uses dominant normal axis per vertex to determine projection plane.
    /// </summary>
    public static void ApplyContinuousTriplanarUvs(MeshObject mesh, float tileScale = 300.0f)
    {
        if (mesh == null || mesh.Vertices.Count == 0) return;
        float invScale = tileScale > 0f ? 1.0f / tileScale : 1.0f / 300.0f;

        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var v = mesh.Vertices[i];
            var p = v.Position;
            var n = v.Normal;

            float nx = MathF.Abs(n.X);
            float ny = MathF.Abs(n.Y);
            float nz = MathF.Abs(n.Z);

            Vector2 uv;
            if (nz >= nx && nz >= ny)
            {
                // Dominant Z (Horizontal ground, floor, ceiling)
                uv = new Vector2(p.X * invScale, p.Y * invScale);
            }
            else if (nx >= ny)
            {
                // Dominant X (Vertical X-facing wall / cliff)
                uv = new Vector2(p.Y * invScale, p.Z * invScale);
            }
            else
            {
                // Dominant Y (Vertical Y-facing wall / cliff)
                uv = new Vector2(p.X * invScale, p.Z * invScale);
            }

            if (v.UVs.Count == 0) v.UVs.Add(uv);
            else v.UVs[0] = uv;
        }
    }

    /// <summary>
    /// Seam-aware triplanar unwrap. When a vertex is shared between faces with different
    /// dominant projection planes (e.g. floor meets cliff), duplicates the vertex along the seam
    /// so neither face experiences texture stretching or UV pinching.
    /// </summary>
    public static void ApplySeamSplitTriplanarUvs(MeshObject mesh, float tileScale = 300.0f)
    {
        if (mesh == null || mesh.Vertices.Count == 0 || mesh.Triangles.Count == 0) return;
        float invScale = tileScale > 0f ? 1.0f / tileScale : 1.0f / 300.0f;

        // 1. Determine dominant projection plane for each triangle (0=XY, 1=YZ, 2=XZ)
        int[] triPlane = new int[mesh.Triangles.Count];
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var n = mesh.Triangles[i].Normal;
            float nx = MathF.Abs(n.X);
            float ny = MathF.Abs(n.Y);
            float nz = MathF.Abs(n.Z);

            if (nz >= nx && nz >= ny) triPlane[i] = 0; // XY
            else if (nx >= ny) triPlane[i] = 1;        // YZ
            else triPlane[i] = 2;                      // XZ
        }

        // 2. Map (oldVertexIndex, planeIndex) -> newVertexIndex
        var vertPlaneMap = new Dictionary<(int vIdx, int plane), int>();
        var newVerts = new List<Vertex>();

        int GetOrCreateVertexForPlane(int oldVIdx, int plane)
        {
            var key = (oldVIdx, plane);
            if (vertPlaneMap.TryGetValue(key, out int existing)) return existing;

            var oldV = mesh.Vertices[oldVIdx];
            var p = oldV.Position;
            Vector2 uv = plane switch
            {
                0 => new Vector2(p.X * invScale, p.Y * invScale),
                1 => new Vector2(p.Y * invScale, p.Z * invScale),
                2 => new Vector2(p.X * invScale, p.Z * invScale),
                _ => new Vector2(p.X * invScale, p.Y * invScale)
            };

            var newV = new Vertex(p, oldV.Normal, uv)
            {
                Color = oldV.Color
            };
            int newIdx = newVerts.Count;
            newVerts.Add(newV);
            vertPlaneMap[key] = newIdx;
            return newIdx;
        }

        // 3. Remap all triangles to use plane-specific vertices
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            int plane = triPlane[i];

            tri.V0 = GetOrCreateVertexForPlane(tri.V0, plane);
            tri.V1 = GetOrCreateVertexForPlane(tri.V1, plane);
            tri.V2 = GetOrCreateVertexForPlane(tri.V2, plane);
        }

        mesh.Vertices = newVerts;
    }

    /// <summary>
    /// Remaps UV coordinates for a specific subset of faces (e.g. selected in Face Mode).
    /// Supports scale, offset, and rotation so artists can align textures on specific walls or floors.
    /// </summary>
    public static void RemapSelectedFacesUvs(
        MeshObject mesh,
        IEnumerable<int> faceIndices,
        UvProjectionMode mode,
        float tileScale = 300.0f,
        float offsetU = 0.0f,
        float offsetV = 0.0f,
        float rotationDeg = 0.0f)
    {
        if (mesh == null) return;
        var faceSet = new HashSet<int>(faceIndices);
        if (faceSet.Count == 0) return;

        float invScale = tileScale > 0f ? 1.0f / tileScale : 1.0f / 300.0f;
        float rotRad = rotationDeg * MathF.PI / 180.0f;
        float cos = MathF.Cos(rotRad);
        float sin = MathF.Sin(rotRad);

        Vector2 TransformUv(Vector2 raw)
        {
            float u = raw.X * invScale;
            float v = raw.Y * invScale;
            if (rotationDeg != 0f)
            {
                float ru = u * cos - v * sin;
                float rv = u * sin + v * cos;
                u = ru;
                v = rv;
            }
            return new Vector2(u + offsetU, v + offsetV);
        }

        // To prevent altering neighboring unselected faces that share vertices, duplicate vertices for selected faces
        var vertRemap = new Dictionary<int, int>();

        int GetUniqueVertex(int oldIdx)
        {
            if (vertRemap.TryGetValue(oldIdx, out int existing)) return existing;
            var oldV = mesh.Vertices[oldIdx];
            var newV = new Vertex(oldV.Position, oldV.Normal)
            {
                Color = oldV.Color
            };
            foreach (var uv in oldV.UVs) newV.UVs.Add(uv);

            int newIdx = mesh.Vertices.Count;
            mesh.Vertices.Add(newV);
            vertRemap[oldIdx] = newIdx;
            return newIdx;
        }

        foreach (int fIdx in faceSet)
        {
            if (fIdx < 0 || fIdx >= mesh.Triangles.Count) continue;
            var tri = mesh.Triangles[fIdx];

            // Duplicate vertices for this triangle so edits don't bleed into unselected neighbors
            tri.V0 = GetUniqueVertex(tri.V0);
            tri.V1 = GetUniqueVertex(tri.V1);
            tri.V2 = GetUniqueVertex(tri.V2);

            var n = tri.Normal;
            float nx = MathF.Abs(n.X);
            float ny = MathF.Abs(n.Y);
            float nz = MathF.Abs(n.Z);

            void UpdateVertUv(int vIdx)
            {
                var p = mesh.Vertices[vIdx].Position;
                Vector2 raw = mode switch
                {
                    UvProjectionMode.PlanarXy => new Vector2(p.X, p.Y),
                    UvProjectionMode.PlanarXz => new Vector2(p.X, p.Z),
                    UvProjectionMode.PlanarYz => new Vector2(p.Y, p.Z),
                    UvProjectionMode.FaceNormal => ProjectOntoFaceNormalPlane(p, n),
                    _ => (nz >= nx && nz >= ny) ? new Vector2(p.X, p.Y) :
                         (nx >= ny) ? new Vector2(p.Y, p.Z) : new Vector2(p.X, p.Z)
                };

                var finalUv = TransformUv(raw);
                if (mesh.Vertices[vIdx].UVs.Count == 0) mesh.Vertices[vIdx].UVs.Add(finalUv);
                else mesh.Vertices[vIdx].UVs[0] = finalUv;
            }

            UpdateVertUv(tri.V0);
            UpdateVertUv(tri.V1);
            UpdateVertUv(tri.V2);
        }
    }

    private static Vector2 ProjectOntoFaceNormalPlane(Vector3 p, Vector3 normal)
    {
        if (normal.LengthSquared() < 1e-4f) normal = Vector3.UnitZ;
        normal = Vector3.Normalize(normal);

        // Find tangent vector perpendicular to normal
        Vector3 tangent;
        if (MathF.Abs(normal.Z) < 0.90f)
            tangent = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitZ));
        else
            tangent = Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitX));

        Vector3 bitangent = Vector3.Normalize(Vector3.Cross(normal, tangent));

        return new Vector2(Vector3.Dot(p, tangent), Vector3.Dot(p, bitangent));
    }
}
