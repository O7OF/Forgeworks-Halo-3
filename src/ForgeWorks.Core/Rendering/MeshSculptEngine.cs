using System;
using System.Collections.Generic;
using System.Numerics;
using ForgeWorks.Core.Geometry;
using ForgeWorks.Core.Procedural;

namespace ForgeWorks.Core.Rendering;

public enum SculptBrushMode
{
    Raise,
    Lower,
    Rise,
    Smooth,
    Erode,
    ThermalErode,
    Terrace,
    Flatten,
    Pinch,
    Inflate,
    NoiseJitter
}

public sealed record Ray(Vector3 Origin, Vector3 Direction);

public sealed class MeshSculptEngine
{
    public static Ray ScreenPointToRay(
        Vector2 screenPoint,
        float viewportWidth,
        float viewportHeight,
        BlamCamera camera)
    {
        // Convert screen coordinates to Normalized Device Coordinates (NDC)
        float x = (2.0f * screenPoint.X) / viewportWidth - 1.0f;
        float y = 1.0f - (2.0f * screenPoint.Y) / viewportHeight;

        // Invert view-projection matrix
        var vp = camera.ViewMatrix * camera.ProjectionMatrix;
        if (!Matrix4x4.Invert(vp, out var invVp))
        {
            return new Ray(camera.Position, camera.Forward);
        }

        var nearVec = Vector4.Transform(new Vector4(x, y, -1.0f, 1.0f), invVp);
        var farVec = Vector4.Transform(new Vector4(x, y, 1.0f, 1.0f), invVp);

        var nearPoint = new Vector3(nearVec.X, nearVec.Y, nearVec.Z) / nearVec.W;
        var farPoint = new Vector3(farVec.X, farVec.Y, farVec.Z) / farVec.W;

        var dir = Vector3.Normalize(farPoint - nearPoint);
        return new Ray(nearPoint, dir);
    }

    public static bool RayIntersectsMesh(
        Ray ray,
        MeshObject mesh,
        out Vector3 hitPoint,
        out Vector3 hitNormal,
        out int hitTriIndex)
    {
        hitPoint = Vector3.Zero;
        hitNormal = Vector3.UnitZ;
        hitTriIndex = -1;
        float closestDist = float.MaxValue;

        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            var v0 = mesh.Vertices[tri.V0].Position;
            var v1 = mesh.Vertices[tri.V1].Position;
            var v2 = mesh.Vertices[tri.V2].Position;

            if (RayIntersectsTriangle(ray, v0, v1, v2, out float t, out var hit))
            {
                if (t > 0.0f && t < closestDist)
                {
                    closestDist = t;
                    hitPoint = hit;
                    hitNormal = tri.Normal;
                    hitTriIndex = i;
                }
            }
        }

        return hitTriIndex != -1;
    }

    private static bool RayIntersectsTriangle(
        Ray ray,
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        out float distance,
        out Vector3 hitPoint)
    {
        distance = 0f;
        hitPoint = Vector3.Zero;

        // Möller–Trumbore ray-triangle intersection algorithm
        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var h = Vector3.Cross(ray.Direction, edge2);
        float a = Vector3.Dot(edge1, h);

        if (MathF.Abs(a) < 1e-6f)
            return false;

        float f = 1.0f / a;
        var s = ray.Origin - v0;
        float u = f * Vector3.Dot(s, h);

        if (u < 0.0f || u > 1.0f)
            return false;

        var q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(ray.Direction, q);

        if (v < 0.0f || u + v > 1.0f)
            return false;

        float t = f * Vector3.Dot(edge2, q);
        if (t > 1e-4f)
        {
            distance = t;
            hitPoint = ray.Origin + ray.Direction * t;
            return true;
        }

        return false;
    }

    public static void ApplySculpt(
        MeshObject mesh,
        Vector3 center,
        float radius,
        float strength,
        SculptBrushMode mode,
        float tileScale = 300.0f)
    {
        float radSq = radius * radius;
        var modifiedVerts = new HashSet<int>();

        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var v = mesh.Vertices[i];
            float distSq = (v.Position - center).LengthSquared();
            if (distSq > radSq) continue;

            float dist = MathF.Sqrt(distSq);
            float normDist = dist / radius;
            float falloff = MathF.Exp(-normDist * normDist * 3.0f); // Gaussian falloff

            switch (mode)
            {
                case SculptBrushMode.Raise:
                    v.Position += Vector3.UnitZ * (strength * falloff);
                    break;
                case SculptBrushMode.Lower:
                    v.Position -= Vector3.UnitZ * (strength * falloff);
                    break;
                case SculptBrushMode.Rise:
                    // Plateau extrusion with smooth dome falloff
                    float domeProfile = MathF.Cos(normDist * MathF.PI * 0.5f);
                    v.Position += Vector3.UnitZ * (strength * 1.5f * MathF.Max(0f, domeProfile));
                    break;
                case SculptBrushMode.Terrace:
                    // Quantize elevation to step increments
                    const float terraceStep = 80.0f;
                    float steppedZ = MathF.Round(v.Position.Z / terraceStep) * terraceStep;
                    v.Position = new Vector3(v.Position.X, v.Position.Y, MathHelperLerp(v.Position.Z, steppedZ, falloff * Math.Clamp(strength * 0.08f, 0.05f, 0.8f)));
                    break;
                case SculptBrushMode.Flatten:
                    float targetZ = center.Z;
                    v.Position = new Vector3(v.Position.X, v.Position.Y, MathHelperLerp(v.Position.Z, targetZ, falloff * 0.5f));
                    break;
                case SculptBrushMode.Pinch:
                    var dirToCenter = center - v.Position;
                    v.Position += dirToCenter * (strength * falloff * 0.1f);
                    break;
                case SculptBrushMode.Inflate:
                    v.Position += v.Normal * (strength * falloff);
                    break;
                case SculptBrushMode.NoiseJitter:
                    float nx = MathF.Sin(v.Position.X * 0.05f + v.Position.Y * 0.03f);
                    float ny = MathF.Cos(v.Position.Y * 0.05f + v.Position.Z * 0.03f);
                    float nz = MathF.Sin(v.Position.X * 0.03f + v.Position.Z * 0.05f);
                    v.Position += new Vector3(nx, ny, nz) * (strength * falloff * 0.5f);
                    break;
            }

            modifiedVerts.Add(i);
        }

        if (mode == SculptBrushMode.Smooth && modifiedVerts.Count > 0)
        {
            SmoothVertices(mesh, modifiedVerts, strength);
        }
        else if (mode == SculptBrushMode.Erode && modifiedVerts.Count > 0)
        {
            ErodeVertices(mesh, modifiedVerts, strength);
        }
        else if (mode == SculptBrushMode.ThermalErode && modifiedVerts.Count > 0)
        {
            ThermalErodeVertices(mesh, modifiedVerts, strength);
        }

        mesh.RecalculateNormals();

        // 1:1 Seamless World-Space UV Remapping on sculpted vertices
        float scale = tileScale > 0 ? tileScale : 300.0f;
        foreach (int idx in modifiedVerts)
        {
            RemapVertexUv(mesh.Vertices[idx], scale);
        }
    }

    public static void ApplyPaint(
        MeshObject mesh,
        Vector3 center,
        float radius,
        float strength,
        int targetLayerIndex) // 0: Base, 1: Rock (R), 2: Alpine (G), 3: Dirt (B)
    {
        float radSq = radius * radius;
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var v = mesh.Vertices[i];
            float distSq = (v.Position - center).LengthSquared();
            if (distSq > radSq) continue;

            float dist = MathF.Sqrt(distSq);
            float falloff = MathF.Exp(-(dist / radius) * (dist / radius) * 3.0f) * strength;

            var col = v.Color;
            float r = col.X;
            float g = col.Y;
            float b = col.Z;

            switch (targetLayerIndex)
            {
                case 1: // Rock Layer (R)
                    r = Math.Clamp(r + falloff, 0f, 1f);
                    break;
                case 2: // Alpine Layer (G)
                    g = Math.Clamp(g + falloff, 0f, 1f);
                    break;
                case 3: // Dirt Layer (B)
                    b = Math.Clamp(b + falloff, 0f, 1f);
                    break;
                case 0: // Base Layer (Erases R, G, B)
                    r = Math.Clamp(r - falloff, 0f, 1f);
                    g = Math.Clamp(g - falloff, 0f, 1f);
                    b = Math.Clamp(b - falloff, 0f, 1f);
                    break;
            }

            v.Color = new Vector4(r, g, b, 1.0f);
        }
    }

    public static int PaintTrianglesMaterial(
        MeshObject mesh,
        Vector3 center,
        float radius,
        int targetMaterialIndex,
        float tileScale = 300.0f,
        bool noTiling = false)
    {
        float radSq = radius * radius;
        int painted = 0;
        mesh.GetBounds(out var min, out var max);

        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            var p0 = mesh.Vertices[tri.V0].Position;
            var p1 = mesh.Vertices[tri.V1].Position;
            var p2 = mesh.Vertices[tri.V2].Position;
            var triCenter = (p0 + p1 + p2) / 3.0f;
            if ((triCenter - center).LengthSquared() <= radSq)
            {
                tri.MaterialIndex = targetMaterialIndex;
                painted++;

                // Ensure 1:1 isometric world UVs on all painted vertices
                RemapVertexUv(mesh.Vertices[tri.V0], tileScale, min, max, noTiling);
                RemapVertexUv(mesh.Vertices[tri.V1], tileScale, min, max, noTiling);
                RemapVertexUv(mesh.Vertices[tri.V2], tileScale, min, max, noTiling);
            }
        }

        return painted;
    }

    public static void RemapVertexUv(Vertex v, float tileScale, Vector3 min, Vector3 max, bool noTiling = true)
    {
        Vector2 uv;
        if (noTiling || tileScale <= 0f)
        {
            float spanX = MathF.Max(max.X - min.X, 1.0f);
            float spanY = MathF.Max(max.Y - min.Y, 1.0f);
            uv = new Vector2((v.Position.X - min.X) / spanX, (v.Position.Y - min.Y) / spanY);
        }
        else
        {
            uv = new Vector2(v.Position.X / tileScale, v.Position.Y / tileScale);
        }

        if (v.UVs.Count == 0) v.UVs.Add(uv);
        else v.UVs[0] = uv;
    }

    public static void RemapVertexUv(Vertex v, float tileScale)
    {
        var uv = tileScale > 0f ? new Vector2(v.Position.X / tileScale, v.Position.Y / tileScale) : Vector2.Zero;
        if (v.UVs.Count == 0) v.UVs.Add(uv);
        else v.UVs[0] = uv;
    }

    public static void EnsureCorrectUvScaling(MeshObject mesh, float tileScale = 300.0f, bool noTiling = true)
    {
        TerrainGenerator.RecalculateTerrainUVs(mesh, tileScale, noTiling);
    }

    private static void SmoothVertices(MeshObject mesh, HashSet<int> vertIndices, float strength)
    {
        // Build adjacency
        var adj = new Dictionary<int, List<int>>();
        foreach (var tri in mesh.Triangles)
        {
            AddAdj(adj, tri.V0, tri.V1);
            AddAdj(adj, tri.V1, tri.V2);
            AddAdj(adj, tri.V2, tri.V0);
        }

        foreach (var idx in vertIndices)
        {
            if (adj.TryGetValue(idx, out var neighbors) && neighbors.Count > 0)
            {
                Vector3 avg = Vector3.Zero;
                foreach (var n in neighbors)
                {
                    avg += mesh.Vertices[n].Position;
                }
                avg /= neighbors.Count;
                mesh.Vertices[idx].Position = Vector3.Lerp(mesh.Vertices[idx].Position, avg, Math.Clamp(strength * 0.2f, 0f, 0.5f));
            }
        }
    }

    private static void ErodeVertices(MeshObject mesh, HashSet<int> vertIndices, float strength)
    {
        // Hydraulic sediment transport: Carves steep ridges and deposits into valleys
        var adj = new Dictionary<int, List<int>>();
        foreach (var tri in mesh.Triangles)
        {
            AddAdj(adj, tri.V0, tri.V1);
            AddAdj(adj, tri.V1, tri.V2);
            AddAdj(adj, tri.V2, tri.V0);
        }

        float carveFactor = Math.Clamp(strength * 0.12f, 0.02f, 0.45f);
        foreach (var idx in vertIndices)
        {
            if (adj.TryGetValue(idx, out var neighbors) && neighbors.Count > 0)
            {
                float curZ = mesh.Vertices[idx].Position.Z;
                float neighborAvgZ = 0f;
                float minZ = float.MaxValue;
                foreach (var n in neighbors)
                {
                    float nz = mesh.Vertices[n].Position.Z;
                    neighborAvgZ += nz;
                    if (nz < minZ) minZ = nz;
                }
                neighborAvgZ /= neighbors.Count;

                if (curZ > neighborAvgZ)
                {
                    // Crest/ridge: erode downward towards downslope drainage
                    float slopeDiff = curZ - minZ;
                    mesh.Vertices[idx].Position -= Vector3.UnitZ * (slopeDiff * carveFactor * 0.5f);
                }
                else
                {
                    // Gully/basin: accumulate alluvial deposit
                    float basinDiff = neighborAvgZ - curZ;
                    mesh.Vertices[idx].Position += Vector3.UnitZ * (basinDiff * carveFactor * 0.25f);
                }
            }
        }
    }

    private static void ThermalErodeVertices(MeshObject mesh, HashSet<int> vertIndices, float strength)
    {
        // Thermal weathering / talus relaxation: slumps overhangs steeper than angle of repose (~35 deg)
        var adj = new Dictionary<int, List<int>>();
        foreach (var tri in mesh.Triangles)
        {
            AddAdj(adj, tri.V0, tri.V1);
            AddAdj(adj, tri.V1, tri.V2);
            AddAdj(adj, tri.V2, tri.V0);
        }

        const float talusAngleTangent = 0.70f; // ~35 degrees max slope
        float slumpRate = Math.Clamp(strength * 0.15f, 0.05f, 0.5f);

        foreach (var idx in vertIndices)
        {
            if (adj.TryGetValue(idx, out var neighbors) && neighbors.Count > 0)
            {
                var curPos = mesh.Vertices[idx].Position;
                foreach (var n in neighbors)
                {
                    var nPos = mesh.Vertices[n].Position;
                    float horizDist = MathF.Sqrt(MathF.Max(1.0f, (curPos.X - nPos.X) * (curPos.X - nPos.X) + (curPos.Y - nPos.Y) * (curPos.Y - nPos.Y)));
                    float maxStableDeltaZ = horizDist * talusAngleTangent;

                    float deltaZ = curPos.Z - nPos.Z;
                    if (deltaZ > maxStableDeltaZ)
                    {
                        // Oversteepened: slump scree material down
                        float excess = deltaZ - maxStableDeltaZ;
                        mesh.Vertices[idx].Position -= Vector3.UnitZ * (excess * slumpRate * 0.5f);
                        mesh.Vertices[n].Position += Vector3.UnitZ * (excess * slumpRate * 0.5f);
                    }
                }
            }
        }
    }

    private static void AddAdj(Dictionary<int, List<int>> adj, int a, int b)
    {
        if (!adj.TryGetValue(a, out var list))
        {
            list = new List<int>();
            adj[a] = list;
        }
        if (!list.Contains(b)) list.Add(b);

        if (!adj.TryGetValue(b, out var listB))
        {
            listB = new List<int>();
            adj[b] = listB;
        }
        if (!listB.Contains(a)) listB.Add(a);
    }

    private static float MathHelperLerp(float a, float b, float t) => a + t * (b - a);
}
