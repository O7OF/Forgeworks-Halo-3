using System;
using System.Collections.Generic;
using System.Numerics;
using ForgeWorks.Core.Geometry;

namespace ForgeWorks.Core.Procedural;

public enum RockArchetype
{
    FacetedBoulder,
    MonolithCrag,
    StratifiedSlab,
    BasaltColumn,
    ErosionWedge,
    RubbleBlock,
    NaturalArch
}

public sealed class HaloRockGenerator
{
    public const string DefaultRockShader = "river riverworld_rock";

    public static MeshObject GenerateRock(
        RockArchetype archetype = RockArchetype.FacetedBoulder,
        Vector3? size = null,
        int seed = 1337,
        string materialName = DefaultRockShader,
        Vector3 location = default,
        float rotationZ = 0.0f)
    {
        var rng = new Random(seed);
        Vector3 dims = size ?? new Vector3(80.0f, 60.0f, 50.0f);
        float sx = dims.X;
        float sy = dims.Y;
        float sz = dims.Z;

        var mesh = new MeshObject($"h3_rock_{archetype.ToString().ToLowerInvariant()}");
        mesh.MaterialName = materialName;
        mesh.IsCollideable = true;

        switch (archetype)
        {
            case RockArchetype.BasaltColumn:
                GenerateBasaltColumn(mesh, sx, sy, sz, rng);
                break;
            case RockArchetype.MonolithCrag:
                GenerateMonolithCrag(mesh, sx, sy, sz, rng);
                break;
            case RockArchetype.StratifiedSlab:
                GenerateStratifiedSlab(mesh, sx, sy, sz, rng);
                break;
            case RockArchetype.RubbleBlock:
            case RockArchetype.ErosionWedge:
                GenerateRubbleBlock(mesh, sx, sy, sz, rng, isWedge: archetype == RockArchetype.ErosionWedge);
                break;
            case RockArchetype.NaturalArch:
                GenerateNaturalArch(mesh, sx, sy, sz, rng);
                break;
            case RockArchetype.FacetedBoulder:
            default:
                GenerateFacetedBoulder(mesh, sx, sy, sz, rng);
                break;
        }

        // Apply rotationZ and location offset
        float cos = MathF.Cos(rotationZ);
        float sin = MathF.Sin(rotationZ);
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var p = mesh.Vertices[i].Position;
            float rx = p.X * cos - p.Y * sin;
            float ry = p.X * sin + p.Y * cos;
            mesh.Vertices[i].Position = new Vector3(rx, ry, p.Z) + location;
        }

        FixWindingOutward(mesh);
        mesh.RecalculateNormals();

        // 1:1 Distortion-free Triplanar UV mapping matching rock shader tile scale
        const float rockTileScale = 100.0f;
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var p = mesh.Vertices[i].Position;
            var n = mesh.Vertices[i].Normal;
            float absX = MathF.Abs(n.X);
            float absY = MathF.Abs(n.Y);
            float absZ = MathF.Abs(n.Z);

            Vector2 uv;
            if (absZ >= absX && absZ >= absY)
                uv = new Vector2(p.X / rockTileScale, p.Y / rockTileScale);
            else if (absX >= absY)
                uv = new Vector2(p.Y / rockTileScale, p.Z / rockTileScale);
            else
                uv = new Vector2(p.X / rockTileScale, p.Z / rockTileScale);

            mesh.Vertices[i].UVs.Clear();
            mesh.Vertices[i].UVs.Add(uv);
        }

        return mesh;
    }

    /// <summary>
    /// Enforces outward winding order on all triangles so rock surfaces are never front-face culled in Halo 3 / Sapien.
    /// If face normal points inward toward centroid, the triangle's winding order is reversed.
    /// </summary>
    public static void FixWindingOutward(MeshObject mesh)
    {
        if (mesh.Vertices.Count == 0 || mesh.Triangles.Count == 0) return;

        Vector3 centroid = Vector3.Zero;
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            centroid += mesh.Vertices[i].Position;
        }
        centroid /= mesh.Vertices.Count;

        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var tri = mesh.Triangles[i];
            var v0 = mesh.Vertices[tri.V0].Position;
            var v1 = mesh.Vertices[tri.V1].Position;
            var v2 = mesh.Vertices[tri.V2].Position;

            Vector3 faceCenter = (v0 + v1 + v2) / 3.0f;
            Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0);
            if (faceNormal.LengthSquared() < 1e-8f) continue;
            faceNormal = Vector3.Normalize(faceNormal);

            Vector3 outwardDir = faceCenter - centroid;
            // If normal points towards centroid, flip triangle winding
            if (Vector3.Dot(faceNormal, outwardDir) < 0f)
            {
                mesh.Triangles[i] = new Triangle(tri.V0, tri.V2, tri.V1)
                {
                    MaterialIndex = tri.MaterialIndex,
                    Normal = -faceNormal
                };
            }
        }
    }

    private static void GenerateFacetedBoulder(MeshObject mesh, float sx, float sy, float sz, Random rng)
    {
        // 2-subdivision icosphere with noise displacement and flattened base
        BuildIcosphere(mesh, subdivisions: 2, radius: 1.0f);
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var p = mesh.Vertices[i].Position;
            float n = PseudoNoise(p.X * 0.05f, p.Y * 0.05f, p.Z * 0.05f, rng) * 0.22f;
            float x = p.X * sx * 0.5f * (1.0f + n);
            float y = p.Y * sy * 0.5f * (1.0f + n);
            float z = p.Z * sz * 0.5f * (1.0f + n);
            if (z < -sz * 0.15f) z = -sz * 0.15f; // Flatten base for clean terrain ground contact
            mesh.Vertices[i].Position = new Vector3(x, y, z);
        }
    }

    private static void GenerateMonolithCrag(MeshObject mesh, float sx, float sy, float sz, Random rng)
    {
        BuildIcosphere(mesh, subdivisions: 2, radius: 1.0f);
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var p = mesh.Vertices[i].Position;
            float n = PseudoNoise(p.X * 0.08f, p.Y * 0.08f, p.Z * 0.08f, rng) * 0.28f;
            float x = p.X * sx * 0.55f * (1.0f + n);
            float y = p.Y * sy * 0.55f * (1.0f + n);
            float z = MathF.Max(0.0f, (p.Z + 1.0f) * 0.5f) * sz * 1.8f * (1.0f + n);
            mesh.Vertices[i].Position = new Vector3(x, y, z);
        }
    }

    private static void GenerateStratifiedSlab(MeshObject mesh, float sx, float sy, float sz, Random rng)
    {
        // Stratified horizontal sedimentary slab with layered displacement
        BuildBox(mesh, sx * 1.2f, sy * 1.0f, sz * 0.45f);
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var p = mesh.Vertices[i].Position;
            float strata = MathF.Sin(p.Z * 0.35f) * (sz * 0.12f);
            float n = PseudoNoise(p.X * 0.04f, p.Y * 0.04f, p.Z * 0.04f, rng) * (sx * 0.08f);
            mesh.Vertices[i].Position = new Vector3(p.X + strata + n, p.Y + n * 0.5f, MathF.Max(0.0f, p.Z));
        }
    }

    private static void GenerateBasaltColumn(MeshObject mesh, float sx, float sy, float sz, Random rng)
    {
        // 6-sided hexagonal volcanic basalt column with beveled top and solid base
        const int sides = 6;
        float h = sz * 1.4f;
        float rTop = sx * 0.5f;
        float rBot = sx * 0.55f;

        var botVerts = new int[sides];
        var topVerts = new int[sides];

        for (int i = 0; i < sides; i++)
        {
            float ang = (i / (float)sides) * MathF.PI * 2.0f;
            float jx = (float)(rng.NextDouble() - 0.5) * sx * 0.08f;
            float jy = (float)(rng.NextDouble() - 0.5) * sy * 0.08f;

            float bx = MathF.Cos(ang) * rBot + jx;
            float by = MathF.Sin(ang) * rBot + jy;
            float tx = MathF.Cos(ang) * rTop + jx;
            float ty = MathF.Sin(ang) * rTop + jy;
            float tz = h + (float)(rng.NextDouble() - 0.5) * sz * 0.1f;

            botVerts[i] = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(bx, by, 0.0f), Vector3.UnitZ));

            topVerts[i] = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(tx, ty, tz), Vector3.UnitZ));
        }

        // 6 Side quads (as 12 triangles)
        for (int i = 0; i < sides; i++)
        {
            int nxt = (i + 1) % sides;
            int b0 = botVerts[i];
            int b1 = botVerts[nxt];
            int t0 = topVerts[i];
            int t1 = topVerts[nxt];

            mesh.Triangles.Add(new Triangle(b0, t0, b1));
            mesh.Triangles.Add(new Triangle(b1, t0, t1));
        }

        // Bottom cap (facing down)
        int centerBot = mesh.Vertices.Count;
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, 0), -Vector3.UnitZ));
        for (int i = 0; i < sides; i++)
        {
            int nxt = (i + 1) % sides;
            mesh.Triangles.Add(new Triangle(centerBot, botVerts[nxt], botVerts[i]));
        }

        // Top cap (facing up)
        int centerTop = mesh.Vertices.Count;
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, h), Vector3.UnitZ));
        for (int i = 0; i < sides; i++)
        {
            int nxt = (i + 1) % sides;
            mesh.Triangles.Add(new Triangle(centerTop, topVerts[i], topVerts[nxt]));
        }
    }

    private static void GenerateRubbleBlock(MeshObject mesh, float sx, float sy, float sz, Random rng, bool isWedge)
    {
        BuildBox(mesh, sx, sy, sz);
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var p = mesh.Vertices[i].Position;
            float jitterX = (float)(rng.NextDouble() - 0.5) * sx * 0.15f;
            float jitterY = (float)(rng.NextDouble() - 0.5) * sy * 0.15f;
            float z = p.Z;
            if (isWedge && p.X > 0)
            {
                z *= 0.35f; // Wedge taper
            }
            mesh.Vertices[i].Position = new Vector3(p.X + jitterX, p.Y + jitterY, MathF.Max(0.0f, z));
        }
    }

    private static void GenerateNaturalArch(MeshObject mesh, float sx, float sy, float sz, Random rng)
    {
        // Curved natural rock bridge with hollow canyon tunnel passage
        int nu = 12;
        int nv = 6;
        float halfSpan = MathF.Max(120.0f, sx) * 0.5f;
        float halfDepth = MathF.Max(60.0f, sy) * 0.5f;
        float h = MathF.Max(80.0f, sz);
        float tunnelW = halfSpan * 0.7f;
        float tunnelH = h * 0.65f;

        var topGrid = new int[nv, nu];
        var botGrid = new int[nv, nu];

        for (int iv = 0; iv < nv; iv++)
        {
            float vt = (iv / (float)(nv - 1)) * 2.0f - 1.0f;
            float y = vt * halfDepth;
            float yFac = 1.0f - 0.25f * (vt * vt);

            for (int iu = 0; iu < nu; iu++)
            {
                float ut = (iu / (float)(nu - 1)) * 2.0f - 1.0f;
                float x = ut * halfSpan;
                float archCurve = MathF.Cos(ut * (MathF.PI * 0.45f));
                float zTop = h * archCurve * yFac + 20.0f;

                float distC = MathF.Abs(x);
                float zBot = 0.0f;
                if (distC < tunnelW)
                {
                    float tRatio = distC / tunnelW;
                    zBot = tunnelH * MathF.Pow(MathF.Cos(tRatio * (MathF.PI * 0.5f)), 1.3f) * yFac;
                }

                topGrid[iv, iu] = mesh.Vertices.Count;
                mesh.Vertices.Add(new Vertex(new Vector3(x, y, zTop), Vector3.UnitZ));

                botGrid[iv, iu] = mesh.Vertices.Count;
                mesh.Vertices.Add(new Vertex(new Vector3(x, y, zBot), -Vector3.UnitZ));
            }
        }

        // Stitch top surface quads
        for (int iv = 0; iv < nv - 1; iv++)
        {
            for (int iu = 0; iu < nu - 1; iu++)
            {
                int t00 = topGrid[iv, iu];
                int t01 = topGrid[iv, iu + 1];
                int t10 = topGrid[iv + 1, iu];
                int t11 = topGrid[iv + 1, iu + 1];

                mesh.Triangles.Add(new Triangle(t00, t10, t01));
                mesh.Triangles.Add(new Triangle(t01, t10, t11));

                int b00 = botGrid[iv, iu];
                int b01 = botGrid[iv, iu + 1];
                int b10 = botGrid[iv + 1, iu];
                int b11 = botGrid[iv + 1, iu + 1];

                // Underside tunnel faces downward
                mesh.Triangles.Add(new Triangle(b00, b01, b10));
                mesh.Triangles.Add(new Triangle(b01, b11, b10));
            }
        }

        // Side walls (connecting top and bot grids to seal watertight)
        for (int iv = 0; iv < nv - 1; iv++)
        {
            // -X side
            mesh.Triangles.Add(new Triangle(topGrid[iv, 0], botGrid[iv, 0], topGrid[iv + 1, 0]));
            mesh.Triangles.Add(new Triangle(topGrid[iv + 1, 0], botGrid[iv, 0], botGrid[iv + 1, 0]));

            // +X side
            mesh.Triangles.Add(new Triangle(topGrid[iv, nu - 1], topGrid[iv + 1, nu - 1], botGrid[iv, nu - 1]));
            mesh.Triangles.Add(new Triangle(topGrid[iv + 1, nu - 1], botGrid[iv + 1, nu - 1], botGrid[iv, nu - 1]));
        }

        for (int iu = 0; iu < nu - 1; iu++)
        {
            // -Y side
            mesh.Triangles.Add(new Triangle(topGrid[0, iu], topGrid[0, iu + 1], botGrid[0, iu]));
            mesh.Triangles.Add(new Triangle(topGrid[0, iu + 1], botGrid[0, iu + 1], botGrid[0, iu]));

            // +Y side
            mesh.Triangles.Add(new Triangle(topGrid[nv - 1, iu], botGrid[nv - 1, iu], topGrid[nv - 1, iu + 1]));
            mesh.Triangles.Add(new Triangle(topGrid[nv - 1, iu + 1], botGrid[nv - 1, iu], botGrid[nv - 1, iu + 1]));
        }
    }

    public static List<MeshObject> ScatterRocksOnTerrain(
        MeshObject terrain,
        int count = 16,
        int seed = 1337,
        RockArchetype archetype = RockArchetype.FacetedBoulder,
        string materialName = DefaultRockShader,
        IReadOnlyList<Vector3>? occupiedPositions = null,
        List<Vector3>? outPositions = null,
        float minSeparation = 180.0f)
    {
        var rocks = new List<MeshObject>();
        if (terrain == null || terrain.Vertices.Count == 0) return rocks;

        var rng = new Random(seed);
        var (min, max) = CsgBooleanEngine.ComputeBounds(terrain);
        float width = max.X - min.X;
        float length = max.Y - min.Y;
        float minSepSq = minSeparation * minSeparation;

        var placedLocs = new List<Vector3>();
        if (occupiedPositions != null)
        {
            placedLocs.AddRange(occupiedPositions);
        }

        for (int i = 0; i < count; i++)
        {
            float rx = 0f, ry = 0f, rz = 0f;
            bool foundSpot = false;

            // Up to 35 attempts to find a spot that does not collide with existing rocks or trees
            for (int attempt = 0; attempt < 35; attempt++)
            {
                rx = min.X + width * (0.12f + (float)rng.NextDouble() * 0.76f);
                ry = min.Y + length * (0.12f + (float)rng.NextDouble() * 0.76f);

                bool tooClose = false;
                foreach (var loc in placedLocs)
                {
                    float dx = loc.X - rx;
                    float dy = loc.Y - ry;
                    if (dx * dx + dy * dy < minSepSq)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    foundSpot = true;
                    break;
                }
            }

            if (!foundSpot && placedLocs.Count > 0)
            {
                // Jitter position if tight
                rx += (float)(rng.NextDouble() - 0.5) * 50.0f;
                ry += (float)(rng.NextDouble() - 0.5) * 50.0f;
            }

            rz = SampleTerrainHeight(terrain, rx, ry);
            var locPos = new Vector3(rx, ry, rz);
            placedLocs.Add(locPos);
            outPositions?.Add(locPos);

            float scale = 0.5f + (float)rng.NextDouble() * 1.0f;
            var size = new Vector3(80.0f * scale, 60.0f * scale, 50.0f * scale);
            float rotZ = (float)rng.NextDouble() * MathF.PI * 2.0f;

            var rock = GenerateRock(archetype, size, seed + i * 37, materialName, locPos, rotZ);
            rock.Name = $"h3_rock_{i + 1}";
            rocks.Add(rock);
        }

        return rocks;
    }

    private static float SampleTerrainHeight(MeshObject terrain, float x, float y)
    {
        float bestDistSq = float.MaxValue;
        float bestZ = 0.0f;

        for (int i = 0; i < terrain.Vertices.Count; i += 2)
        {
            var p = terrain.Vertices[i].Position;
            float dx = p.X - x;
            float dy = p.Y - y;
            float dSq = dx * dx + dy * dy;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestZ = p.Z;
            }
        }
        return bestZ;
    }

    private static void BuildBox(MeshObject mesh, float sx, float sy, float sz)
    {
        float hw = sx * 0.5f, hl = sy * 0.5f;
        var corners = new Vector3[]
        {
            new(-hw, -hl, sz), new(hw, -hl, sz), new(hw, hl, sz), new(-hw, hl, sz),
            new(-hw, -hl, 0),  new(hw, -hl, 0),  new(hw, hl, 0),  new(-hw, hl, 0)
        };
        for (int i = 0; i < 8; i++) mesh.Vertices.Add(new Vertex(corners[i], Vector3.UnitZ));

        // 6 faces (12 triangles)
        mesh.Triangles.Add(new Triangle(0, 1, 2)); mesh.Triangles.Add(new Triangle(0, 2, 3)); // Top
        mesh.Triangles.Add(new Triangle(4, 6, 5)); mesh.Triangles.Add(new Triangle(4, 7, 6)); // Bot
        mesh.Triangles.Add(new Triangle(0, 4, 1)); mesh.Triangles.Add(new Triangle(1, 4, 5)); // -Y
        mesh.Triangles.Add(new Triangle(1, 5, 2)); mesh.Triangles.Add(new Triangle(2, 5, 6)); // +X
        mesh.Triangles.Add(new Triangle(2, 6, 3)); mesh.Triangles.Add(new Triangle(3, 6, 7)); // +Y
        mesh.Triangles.Add(new Triangle(3, 7, 0)); mesh.Triangles.Add(new Triangle(0, 7, 4)); // -X
    }

    private static void BuildIcosphere(MeshObject mesh, int subdivisions, float radius)
    {
        float t = (1.0f + MathF.Sqrt(5.0f)) / 2.0f;
        var baseVerts = new List<Vector3>
        {
            Vector3.Normalize(new(-1,  t,  0)) * radius, Vector3.Normalize(new( 1,  t,  0)) * radius,
            Vector3.Normalize(new(-1, -t,  0)) * radius, Vector3.Normalize(new( 1, -t,  0)) * radius,
            Vector3.Normalize(new( 0, -1,  t)) * radius, Vector3.Normalize(new( 0,  1,  t)) * radius,
            Vector3.Normalize(new( 0, -1, -t)) * radius, Vector3.Normalize(new( 0,  1, -t)) * radius,
            Vector3.Normalize(new( t,  0, -1)) * radius, Vector3.Normalize(new( t,  0,  1)) * radius,
            Vector3.Normalize(new(-t,  0, -1)) * radius, Vector3.Normalize(new(-t,  0,  1)) * radius,
        };

        var faces = new List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11),
            (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9),
            (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1)
        };

        var midpointCache = new Dictionary<(int, int), int>();
        int GetMidpoint(int p1, int p2)
        {
            var key = p1 < p2 ? (p1, p2) : (p2, p1);
            if (midpointCache.TryGetValue(key, out int ret)) return ret;
            var middle = Vector3.Normalize((baseVerts[p1] + baseVerts[p2]) * 0.5f) * radius;
            int idx = baseVerts.Count;
            baseVerts.Add(middle);
            midpointCache[key] = idx;
            return idx;
        }

        for (int s = 0; s < subdivisions; s++)
        {
            var newFaces = new List<(int, int, int)>(faces.Count * 4);
            foreach (var (v1, v2, v3) in faces)
            {
                int a = GetMidpoint(v1, v2);
                int b = GetMidpoint(v2, v3);
                int c = GetMidpoint(v3, v1);
                newFaces.Add((v1, a, c));
                newFaces.Add((v2, b, a));
                newFaces.Add((v3, c, b));
                newFaces.Add((a, b, c));
            }
            faces = newFaces;
        }

        foreach (var v in baseVerts) mesh.Vertices.Add(new Vertex(v, Vector3.Normalize(v)));
        foreach (var (a, b, c) in faces) mesh.Triangles.Add(new Triangle(a, b, c));
    }

    private static float PseudoNoise(float x, float y, float z, Random rng)
    {
        float s = MathF.Sin(x * 12.9898f + y * 78.233f + z * 37.719f);
        return (s * 43758.5453f) - MathF.Floor(s * 43758.5453f);
    }
}
