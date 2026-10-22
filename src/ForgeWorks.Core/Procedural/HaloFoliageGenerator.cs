using System;
using System.Collections.Generic;
using System.Numerics;
using ForgeWorks.Core.Geometry;

namespace ForgeWorks.Core.Procedural;

public enum FoliageArchetype
{
    ConiferPine,
    AlpineFir,
    Broadleaf,
    Palm,
    Acacia,
    Bush
}

public sealed class HaloFoliageGenerator
{
    public const string DefaultBarkShader = "river riverworld_tree_bark";
    public const string DefaultFoliageShader = "%!river riverworld_tree_leafa";

    public static MeshObject GenerateTree(
        FoliageArchetype archetype = FoliageArchetype.ConiferPine,
        float height = 140.0f,
        float radius = 45.0f,
        string barkShader = DefaultBarkShader,
        string foliageShader = DefaultFoliageShader,
        Vector3 location = default,
        float rotationZ = 0.0f)
    {
        // Enforce %! prefix (two-sided render-only in Blam) on foliage card material
        // Blam material symbols are strictly leading prefixes, never trailing suffixes.
        string cleanFoliage = foliageShader.TrimEnd('!');
        if (!cleanFoliage.StartsWith("%!"))
        {
            if (cleanFoliage.StartsWith("%"))
                cleanFoliage = "%!" + cleanFoliage.Substring(1);
            else if (cleanFoliage.StartsWith("!"))
                cleanFoliage = "%!" + cleanFoliage.Substring(1);
            else
                cleanFoliage = "%!" + cleanFoliage;
        }
        foliageShader = cleanFoliage;

        var mesh = new MeshObject($"h3_tree_{archetype.ToString().ToLowerInvariant()}");
        mesh.MaterialName = (archetype == FoliageArchetype.Bush) ? foliageShader : barkShader;
        mesh.MaterialSlots.Add(barkShader);    // Slot 0: Trunk bark (solid sealed cylinder)
        mesh.MaterialSlots.Add(foliageShader); // Slot 1: Foliage leaf cards (%! = 2-sided render-only, no open edge collision)
        mesh.IsCollideable = (archetype != FoliageArchetype.Bush);

        switch (archetype)
        {
            case FoliageArchetype.Palm:
                BuildPalm(mesh, height, radius);
                break;
            case FoliageArchetype.Acacia:
                BuildAcacia(mesh, height, radius);
                break;
            case FoliageArchetype.Bush:
                BuildBush(mesh, height * 0.3f, radius * 0.7f);
                break;
            case FoliageArchetype.Broadleaf:
                BuildBroadleaf(mesh, height, radius);
                break;
            case FoliageArchetype.AlpineFir:
            case FoliageArchetype.ConiferPine:
            default:
                BuildConifer(mesh, height, radius);
                break;
        }

        // Apply rotationZ and location offset, and calculate Wind_Weights vertex colors
        float cos = MathF.Cos(rotationZ);
        float sin = MathF.Sin(rotationZ);
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var p = mesh.Vertices[i].Position;
            float rx = p.X * cos - p.Y * sin;
            float ry = p.X * sin + p.Y * cos;
            mesh.Vertices[i].Position = new Vector3(rx, ry, p.Z) + location;

            // Wind_Weights: sway increases non-linearly with height
            float zNorm = Math.Clamp(p.Z / MathF.Max(1.0f, height), 0.0f, 1.0f);
            float sway = MathF.Pow(zNorm, 1.8f);
            mesh.Vertices[i].Color = new Vector4(sway, sway, sway, 1.0f);
        }

        mesh.RecalculateNormals();
        return mesh;
    }

    private static void BuildConifer(MeshObject mesh, float height, float radius)
    {
        // 1. Solid sealed 6-sided trunk
        const int sides = 6;
        float rBase = radius * 0.16f;
        float rTop = radius * 0.04f;
        float hTrunk = height * 0.85f;

        var baseVerts = new int[sides];
        var topVerts = new int[sides];

        float barkTileV = hTrunk / 30.0f;
        for (int i = 0; i < sides; i++)
        {
            float ang = (i / (float)sides) * MathF.PI * 2.0f;
            baseVerts[i] = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(MathF.Cos(ang) * rBase, MathF.Sin(ang) * rBase, 0.0f), Vector3.UnitZ, new Vector2(i / (float)sides, 0.0f)));

            topVerts[i] = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(MathF.Cos(ang) * rTop, MathF.Sin(ang) * rTop, hTrunk), Vector3.UnitZ, new Vector2(i / (float)sides, barkTileV)));
        }

        // Trunk sides (bark: MaterialIndex = 0) - Outward facing normals (base[i] -> base[nxt] -> top[i])
        for (int i = 0; i < sides; i++)
        {
            int nxt = (i + 1) % sides;
            mesh.Triangles.Add(new Triangle(baseVerts[i], baseVerts[nxt], topVerts[i], materialIndex: 0));
            mesh.Triangles.Add(new Triangle(baseVerts[nxt], topVerts[nxt], topVerts[i], materialIndex: 0));
        }

        // Base bottom cap (-Z normal)
        int bCenter = mesh.Vertices.Count;
        mesh.Vertices.Add(new Vertex(Vector3.Zero, -Vector3.UnitZ, Vector2.Zero));
        for (int i = 0; i < sides; i++)
        {
            int nxt = (i + 1) % sides;
            mesh.Triangles.Add(new Triangle(bCenter, baseVerts[nxt], baseVerts[i], materialIndex: 0));
        }

        // Trunk top cap (+Z normal)
        int tCenter = mesh.Vertices.Count;
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, hTrunk), Vector3.UnitZ, Vector2.One));
        for (int i = 0; i < sides; i++)
        {
            int nxt = (i + 1) % sides;
            mesh.Triangles.Add(new Triangle(tCenter, topVerts[i], topVerts[nxt], materialIndex: 0));
        }

        // 2. Layered conical foliage skirt cards (MaterialIndex = 1, % foliage with !)
        const int numTiers = 5;
        for (int t = 0; t < numTiers; t++)
        {
            float tFrac = t / (float)(numTiers - 1);
            float zBot = height * (0.20f + tFrac * 0.55f);
            float zTop = zBot + height * 0.28f;
            float rTier = radius * (1.0f - tFrac * 0.40f);

            for (int p = 0; p < 3; p++)
            {
                float ang = p * (MathF.PI / 3.0f) + (t * 0.45f);
                float dx = MathF.Cos(ang) * rTier;
                float dy = MathF.Sin(ang) * rTier;

                int v0 = mesh.Vertices.Count;
                mesh.Vertices.Add(new Vertex(new Vector3(-dx, -dy, zBot), Vector3.UnitZ, new Vector2(0, 0)));
                int v1 = mesh.Vertices.Count;
                mesh.Vertices.Add(new Vertex(new Vector3(dx, dy, zBot), Vector3.UnitZ, new Vector2(1, 0)));
                int v2 = mesh.Vertices.Count;
                mesh.Vertices.Add(new Vertex(new Vector3(dx * 0.6f, dy * 0.6f, zTop), Vector3.UnitZ, new Vector2(1, 1)));
                int v3 = mesh.Vertices.Count;
                mesh.Vertices.Add(new Vertex(new Vector3(-dx * 0.6f, -dy * 0.6f, zTop), Vector3.UnitZ, new Vector2(0, 1)));

                mesh.Triangles.Add(new Triangle(v0, v1, v2, materialIndex: 1));
                mesh.Triangles.Add(new Triangle(v0, v2, v3, materialIndex: 1));
            }
        }
    }

    private static void BuildBroadleaf(MeshObject mesh, float height, float radius)
    {
        // Sealed trunk
        const int sides = 6;
        float rBase = radius * 0.14f;
        float rTop = radius * 0.05f;
        float hTrunk = height * 0.70f;

        var baseVerts = new int[sides];
        var topVerts = new int[sides];
        float barkTileV = hTrunk / 30.0f;
        for (int i = 0; i < sides; i++)
        {
            float ang = (i / (float)sides) * MathF.PI * 2.0f;
            baseVerts[i] = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(MathF.Cos(ang) * rBase, MathF.Sin(ang) * rBase, 0.0f), Vector3.UnitZ, new Vector2(i / (float)sides, 0.0f)));
            topVerts[i] = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(MathF.Cos(ang) * rTop, MathF.Sin(ang) * rTop, hTrunk), Vector3.UnitZ, new Vector2(i / (float)sides, barkTileV)));
        }

        // Outward facing trunk walls
        for (int i = 0; i < sides; i++)
        {
            int nxt = (i + 1) % sides;
            mesh.Triangles.Add(new Triangle(baseVerts[i], baseVerts[nxt], topVerts[i], materialIndex: 0));
            mesh.Triangles.Add(new Triangle(baseVerts[nxt], topVerts[nxt], topVerts[i], materialIndex: 0));
        }

        int bCenter = mesh.Vertices.Count;
        mesh.Vertices.Add(new Vertex(Vector3.Zero, -Vector3.UnitZ, Vector2.Zero));
        for (int i = 0; i < sides; i++)
        {
            int nxt = (i + 1) % sides;
            mesh.Triangles.Add(new Triangle(bCenter, baseVerts[nxt], baseVerts[i], materialIndex: 0));
        }

        int tCenter = mesh.Vertices.Count;
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, hTrunk), Vector3.UnitZ, Vector2.One));
        for (int i = 0; i < sides; i++)
        {
            int nxt = (i + 1) % sides;
            mesh.Triangles.Add(new Triangle(tCenter, topVerts[i], topVerts[nxt], materialIndex: 0));
        }

        // Crossed foliage canopy cards
        const int numCards = 6;
        float canopyZ = height * 0.55f;
        for (int c = 0; c < numCards; c++)
        {
            float ang = (c / (float)numCards) * MathF.PI;
            float dx = MathF.Cos(ang) * radius * 0.8f;
            float dy = MathF.Sin(ang) * radius * 0.8f;

            int v0 = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(-dx, -dy, canopyZ), Vector3.UnitZ, new Vector2(0, 0)));
            int v1 = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(dx, dy, canopyZ), Vector3.UnitZ, new Vector2(1, 0)));
            int v2 = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(dx * 0.7f, dy * 0.7f, height), Vector3.UnitZ, new Vector2(1, 1)));
            int v3 = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(-dx * 0.7f, -dy * 0.7f, height), Vector3.UnitZ, new Vector2(0, 1)));

            mesh.Triangles.Add(new Triangle(v0, v1, v2, materialIndex: 1));
            mesh.Triangles.Add(new Triangle(v0, v2, v3, materialIndex: 1));
        }
    }

    private static void BuildPalm(MeshObject mesh, float height, float radius)
    {
        // Segmented leaning curved trunk
        const int segs = 6;
        float rBase = radius * 0.12f;
        var prevRing = new int[6];
        var firstRing = new int[6];

        for (int s = 0; s <= segs; s++)
        {
            float t = s / (float)segs;
            float z = t * height;
            float lean = MathF.Sin(t * MathF.PI * 0.5f) * (height * 0.22f);
            float r = rBase * (1.0f - t * 0.5f);

            var currRing = new int[6];
            for (int i = 0; i < 6; i++)
            {
                float ang = (i / 6.0f) * MathF.PI * 2.0f;
                float vx = lean + MathF.Cos(ang) * r;
                float vy = MathF.Sin(ang) * r;

                currRing[i] = mesh.Vertices.Count;
                mesh.Vertices.Add(new Vertex(new Vector3(vx, vy, z), Vector3.UnitZ, new Vector2(i / 6.0f, z / 30.0f)));
            }

            if (s == 0)
            {
                Array.Copy(currRing, firstRing, 6);
            }
            else
            {
                // Outward facing quad winding: prevRing[i] -> prevRing[nxt] -> currRing[i]
                for (int i = 0; i < 6; i++)
                {
                    int nxt = (i + 1) % 6;
                    mesh.Triangles.Add(new Triangle(prevRing[i], prevRing[nxt], currRing[i], materialIndex: 0));
                    mesh.Triangles.Add(new Triangle(prevRing[nxt], currRing[nxt], currRing[i], materialIndex: 0));
                }
            }
            Array.Copy(currRing, prevRing, 6);
        }

        // Cap bottom of trunk
        int bCenter = mesh.Vertices.Count;
        mesh.Vertices.Add(new Vertex(Vector3.Zero, -Vector3.UnitZ, Vector2.Zero));
        for (int i = 0; i < 6; i++)
        {
            int nxt = (i + 1) % 6;
            mesh.Triangles.Add(new Triangle(bCenter, firstRing[nxt], firstRing[i], materialIndex: 0));
        }

        // Cap top of trunk
        int tCenter = mesh.Vertices.Count;
        float finalLean = MathF.Sin(MathF.PI * 0.5f) * (height * 0.22f);
        Vector3 crownPos = new Vector3(finalLean, 0.0f, height);
        mesh.Vertices.Add(new Vertex(crownPos, Vector3.UnitZ, Vector2.One));
        for (int i = 0; i < 6; i++)
        {
            int nxt = (i + 1) % 6;
            mesh.Triangles.Add(new Triangle(tCenter, prevRing[i], prevRing[nxt], materialIndex: 0));
        }

        // 8 Drooping palm frond leaf cards
        const int numFronds = 8;
        for (int i = 0; i < numFronds; i++)
        {
            float ang = (i / (float)numFronds) * MathF.PI * 2.0f;
            float dx = MathF.Cos(ang) * radius * 0.9f;
            float dy = MathF.Sin(ang) * radius * 0.9f;
            float tipZ = crownPos.Z - height * 0.15f;

            int v0 = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(crownPos + new Vector3(-dy * 0.15f, dx * 0.15f, 0), Vector3.UnitZ, new Vector2(0, 0)));
            int v1 = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(crownPos + new Vector3(dy * 0.15f, -dx * 0.15f, 0), Vector3.UnitZ, new Vector2(1, 0)));
            int v2 = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(crownPos.X + dx, crownPos.Y + dy, tipZ), Vector3.UnitZ, new Vector2(1, 1)));
            int v3 = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(crownPos.X + dx * 0.9f, crownPos.Y + dy * 0.9f, tipZ + 10.0f), Vector3.UnitZ, new Vector2(0, 1)));

            mesh.Triangles.Add(new Triangle(v0, v1, v2, materialIndex: 1));
            mesh.Triangles.Add(new Triangle(v0, v2, v3, materialIndex: 1));
        }
    }

    private static void BuildAcacia(MeshObject mesh, float height, float radius)
    {
        // Forked acacia trunk (capped base and top)
        const int sides = 6;
        float rBase = radius * 0.15f;
        float hFork = height * 0.45f;

        var baseVerts = new int[sides];
        var forkVerts = new int[sides];
        float barkTileV = hFork / 30.0f;
        for (int i = 0; i < sides; i++)
        {
            float ang = (i / (float)sides) * MathF.PI * 2.0f;
            baseVerts[i] = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(MathF.Cos(ang) * rBase, MathF.Sin(ang) * rBase, 0.0f), Vector3.UnitZ, new Vector2(i / (float)sides, 0.0f)));
            forkVerts[i] = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(MathF.Cos(ang) * rBase * 0.7f, MathF.Sin(ang) * rBase * 0.7f, hFork), Vector3.UnitZ, new Vector2(i / (float)sides, barkTileV)));
        }

        // Outward facing trunk walls
        for (int i = 0; i < sides; i++)
        {
            int nxt = (i + 1) % sides;
            mesh.Triangles.Add(new Triangle(baseVerts[i], baseVerts[nxt], forkVerts[i], materialIndex: 0));
            mesh.Triangles.Add(new Triangle(baseVerts[nxt], forkVerts[nxt], forkVerts[i], materialIndex: 0));
        }

        int bCenter = mesh.Vertices.Count;
        mesh.Vertices.Add(new Vertex(Vector3.Zero, -Vector3.UnitZ));
        for (int i = 0; i < sides; i++)
        {
            int nxt = (i + 1) % sides;
            mesh.Triangles.Add(new Triangle(bCenter, baseVerts[nxt], baseVerts[i], materialIndex: 0));
        }

        int tCenter = mesh.Vertices.Count;
        mesh.Vertices.Add(new Vertex(new Vector3(0, 0, hFork), Vector3.UnitZ));
        for (int i = 0; i < sides; i++)
        {
            int nxt = (i + 1) % sides;
            mesh.Triangles.Add(new Triangle(tCenter, forkVerts[i], forkVerts[nxt], materialIndex: 0));
        }

        // Flat umbrella acacia foliage disks
        const int numPads = 5;
        for (int p = 0; p < numPads; p++)
        {
            float ang = (p / (float)numPads) * MathF.PI * 2.0f;
            float cx = MathF.Cos(ang) * (radius * 0.6f);
            float cy = MathF.Sin(ang) * (radius * 0.6f);
            float cz = height * (0.85f + (p % 2) * 0.10f);
            float rDisk = radius * 0.55f;

            for (int c = 0; c < 3; c++)
            {
                float cAng = c * (MathF.PI / 3.0f);
                float cdx = MathF.Cos(cAng) * rDisk;
                float cdy = MathF.Sin(cAng) * rDisk;

                int v0 = mesh.Vertices.Count;
                mesh.Vertices.Add(new Vertex(new Vector3(cx - cdx, cy - cdy, cz - 10.0f), Vector3.UnitZ, new Vector2(0, 0)));
                int v1 = mesh.Vertices.Count;
                mesh.Vertices.Add(new Vertex(new Vector3(cx + cdx, cy + cdy, cz - 10.0f), Vector3.UnitZ, new Vector2(1, 0)));
                int v2 = mesh.Vertices.Count;
                mesh.Vertices.Add(new Vertex(new Vector3(cx + cdx * 0.8f, cy + cdy * 0.8f, cz + 15.0f), Vector3.UnitZ, new Vector2(1, 1)));
                int v3 = mesh.Vertices.Count;
                mesh.Vertices.Add(new Vertex(new Vector3(cx - cdx * 0.8f, cy - cdy * 0.8f, cz + 15.0f), Vector3.UnitZ, new Vector2(0, 1)));

                mesh.Triangles.Add(new Triangle(v0, v1, v2, materialIndex: 1));
                mesh.Triangles.Add(new Triangle(v0, v2, v3, materialIndex: 1));
            }
        }
    }

    private static void BuildBush(MeshObject mesh, float height, float radius)
    {
        // Low ground clover / fern cluster cards
        const int numCards = 4;
        for (int c = 0; c < numCards; c++)
        {
            float ang = (c / (float)numCards) * MathF.PI;
            float dx = MathF.Cos(ang) * radius;
            float dy = MathF.Sin(ang) * radius;

            int v0 = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(-dx, -dy, 0.0f), Vector3.UnitZ, new Vector2(0, 0)));
            int v1 = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(dx, dy, 0.0f), Vector3.UnitZ, new Vector2(1, 0)));
            int v2 = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(dx * 0.7f, dy * 0.7f, height), Vector3.UnitZ, new Vector2(1, 1)));
            int v3 = mesh.Vertices.Count;
            mesh.Vertices.Add(new Vertex(new Vector3(-dx * 0.7f, -dy * 0.7f, height), Vector3.UnitZ, new Vector2(0, 1)));

            mesh.Triangles.Add(new Triangle(v0, v1, v2, materialIndex: 1));
            mesh.Triangles.Add(new Triangle(v0, v2, v3, materialIndex: 1));
        }
    }

    public static List<MeshObject> ScatterFoliageOnTerrain(
        MeshObject terrain,
        int count = 24,
        int seed = 1337,
        FoliageArchetype archetype = FoliageArchetype.ConiferPine,
        string barkShader = DefaultBarkShader,
        string foliageShader = DefaultFoliageShader,
        IReadOnlyList<Vector3>? occupiedPositions = null,
        List<Vector3>? outPositions = null,
        float minSeparation = 180.0f)
    {
        var trees = new List<MeshObject>();
        if (terrain == null || terrain.Vertices.Count == 0) return trees;

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
                rx += (float)(rng.NextDouble() - 0.5) * 60.0f;
                ry += (float)(rng.NextDouble() - 0.5) * 60.0f;
            }

            rz = SampleTerrainHeight(terrain, rx, ry);
            var locPos = new Vector3(rx, ry, rz);
            placedLocs.Add(locPos);
            outPositions?.Add(locPos);

            float scale = 0.65f + (float)rng.NextDouble() * 0.70f;
            float height = (archetype == FoliageArchetype.Bush ? 40.0f : 140.0f) * scale;
            float radius = (archetype == FoliageArchetype.Bush ? 30.0f : 45.0f) * scale;
            float rotZ = (float)rng.NextDouble() * MathF.PI * 2.0f;

            var tree = GenerateTree(archetype, height, radius, barkShader, foliageShader, locPos, rotZ);
            tree.Name = $"h3_foliage_{i + 1}";
            trees.Add(tree);
        }

        return trees;
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
}
