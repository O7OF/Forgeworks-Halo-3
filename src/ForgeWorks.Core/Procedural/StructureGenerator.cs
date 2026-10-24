using System;
using System.Collections.Generic;
using System.Numerics;
using ForgeWorks.Core.Geometry;

namespace ForgeWorks.Core.Procedural;

public sealed class StructureGenerator
{
    /// <summary>
    /// Generates an authentic Halo Blood Gulch fortress bunker with ramps and parapets.
    /// </summary>
    public static MeshObject GenerateBunker(
        string name = "h3_base_bunker",
        float radiusOut = 140.0f,
        float radiusIn = 118.0f,
        float height = 85.0f,
        float rampWidth = 35.0f,
        float coverHeight = 22.0f,
        Vector3 location = default)
    {
        var mesh = new MeshObject(name);
        mesh.IsCollideable = true;

        float hw = radiusOut * 0.85f;
        float hl = radiusOut * 1.05f;
        float hwIn = radiusIn * 0.82f;
        float hlIn = radiusIn * 0.98f;
        float dw = 30.0f; // Door half-width

        // Exterior ring (Z = 0)
        var extFloor = new Vector3[]
        {
            new(hw, hl, 0), new(dw, hl, 0), new(-dw, hl, 0), new(-hw, hl, 0),
            new(-hw, -hl, 0), new(-dw, -hl, 0), new(dw, -hl, 0), new(hw, -hl, 0)
        };

        // Exterior roof (Z = height)
        var extRoof = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            extRoof[i] = new Vector3(extFloor[i].X, extFloor[i].Y, height);
        }

        // Add exterior vertices
        int baseExtIdx = mesh.Vertices.Count;
        for (int i = 0; i < 8; i++)
        {
            mesh.Vertices.Add(new Vertex(extFloor[i] + location, Vector3.UnitZ));
        }
        for (int i = 0; i < 8; i++)
        {
            mesh.Vertices.Add(new Vertex(extRoof[i] + location, Vector3.UnitZ));
        }

        // Exterior wall quads
        for (int i = 0; i < 8; i++)
        {
            int next = (i + 1) % 8;
            // Skip entrance doorway openings on +Y and -Y
            if (i == 1 || i == 5) continue;

            int f0 = baseExtIdx + i;
            int f1 = baseExtIdx + next;
            int r0 = baseExtIdx + 8 + i;
            int r1 = baseExtIdx + 8 + next;

            mesh.Triangles.Add(new Triangle(f0, r0, f1));
            mesh.Triangles.Add(new Triangle(f1, r0, r1));
        }

        // Roof slab
        for (int i = 1; i < 7; i++)
        {
            mesh.Triangles.Add(new Triangle(baseExtIdx + 8, baseExtIdx + 8 + i, baseExtIdx + 8 + i + 1));
        }

        // Ingress Ramp (from ground to roof)
        float rampLength = 120.0f;
        int rampBaseIdx = mesh.Vertices.Count;
        mesh.Vertices.Add(new Vertex(new Vector3(-rampWidth * 0.5f, -hl - rampLength, 0) + location, Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(rampWidth * 0.5f, -hl - rampLength, 0) + location, Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(-rampWidth * 0.5f, -hl, height) + location, Vector3.UnitZ));
        mesh.Vertices.Add(new Vertex(new Vector3(rampWidth * 0.5f, -hl, height) + location, Vector3.UnitZ));

        mesh.Triangles.Add(new Triangle(rampBaseIdx, rampBaseIdx + 2, rampBaseIdx + 1));
        mesh.Triangles.Add(new Triangle(rampBaseIdx + 1, rampBaseIdx + 2, rampBaseIdx + 3));

        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>
    /// Generates a sealed water volume box with the mandatory 4 UV sets.
    /// </summary>
    public static MeshObject GenerateWaterVolume(
        string name = "!water_volume'",
        float width = 2000.0f,
        float length = 2000.0f,
        float depth = 80.0f,
        Vector3 surfaceLocation = default)
    {
        var mesh = new MeshObject(name);
        mesh.IsCollideable = false; // Render-only transparent water sheet

        float hw = width * 0.5f;
        float hl = length * 0.5f;

        // 8 box vertices
        var positions = new Vector3[]
        {
            // Top surface (Z = 0)
            new(-hw, -hl, 0),
            new(hw, -hl, 0),
            new(hw, hl, 0),
            new(-hw, hl, 0),
            // Bottom surface (Z = -depth)
            new(-hw, -hl, -depth),
            new(hw, -hl, -depth),
            new(hw, hl, -depth),
            new(-hw, hl, -depth),
        };

        foreach (var p in positions)
        {
            var v = new Vertex(p + surfaceLocation, Vector3.UnitZ);
            var baseUv = new Vector2((p.X + hw) / width * 4.0f, (p.Y + hl) / length * 4.0f);

            // Mandatory 4 UV channels for Blam water shaders
            v.UVs.Add(baseUv);
            v.UVs.Add(baseUv * 2.0f);
            v.UVs.Add(baseUv * 0.5f);
            v.UVs.Add(baseUv * 3.5f);

            mesh.Vertices.Add(v);
        }

        // Top face
        mesh.Triangles.Add(new Triangle(0, 1, 2));
        mesh.Triangles.Add(new Triangle(0, 2, 3));

        // Bottom face
        mesh.Triangles.Add(new Triangle(4, 6, 5));
        mesh.Triangles.Add(new Triangle(4, 7, 6));

        // Sides
        mesh.Triangles.Add(new Triangle(0, 4, 1));
        mesh.Triangles.Add(new Triangle(1, 4, 5));

        mesh.Triangles.Add(new Triangle(1, 5, 2));
        mesh.Triangles.Add(new Triangle(2, 5, 6));

        mesh.Triangles.Add(new Triangle(2, 6, 3));
        mesh.Triangles.Add(new Triangle(3, 6, 7));

        mesh.Triangles.Add(new Triangle(3, 7, 0));
        mesh.Triangles.Add(new Triangle(0, 7, 4));

        mesh.RecalculateNormals();
        // Enforce true vertical normals on top water surface for uniform specular reflection
        for (int i = 0; i < 4; i++)
        {
            mesh.Vertices[i].Normal = Vector3.UnitZ;
        }
        return mesh;
    }

    /// <summary>
    /// Generates a bounding sky enclosure dome/box with inverted normals and +sky material.
    /// </summary>
    public static MeshObject GenerateSkyEnclosure(
        string name = "+sky_enclosure",
        float width = 12000.0f,
        float length = 12000.0f,
        float height = 4000.0f,
        Vector3 center = default)
    {
        var mesh = new MeshObject(name);
        mesh.IsCollideable = true; // +sky is collideable sealed envelope

        float hw = width * 0.5f;
        float hl = length * 0.5f;

        var corners = new Vector3[]
        {
            // Ceiling (Z = height)
            new(-hw, -hl, height),
            new(hw, -hl, height),
            new(hw, hl, height),
            new(-hw, hl, height),
            // Base ring (Z = 0)
            new(-hw, -hl, 0),
            new(hw, -hl, 0),
            new(hw, hl, 0),
            new(-hw, hl, 0)
        };

        foreach (var c in corners)
        {
            var v = new Vertex(c + center, Vector3.Zero);
            v.UVs.Add(new Vector2((c.X + hw) / width, (c.Y + hl) / length));
            mesh.Vertices.Add(v);
        }

        // Ceiling facing downward (normals pointed inward toward playable space)
        mesh.Triangles.Add(new Triangle(0, 2, 1));
        mesh.Triangles.Add(new Triangle(0, 3, 2));

        // Inward facing walls
        mesh.Triangles.Add(new Triangle(0, 1, 4));
        mesh.Triangles.Add(new Triangle(1, 5, 4));

        mesh.Triangles.Add(new Triangle(1, 2, 5));
        mesh.Triangles.Add(new Triangle(2, 6, 5));

        mesh.Triangles.Add(new Triangle(2, 3, 6));
        mesh.Triangles.Add(new Triangle(3, 7, 6));

        mesh.Triangles.Add(new Triangle(3, 0, 7));
        mesh.Triangles.Add(new Triangle(0, 4, 7));

        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>
    /// Generates a sealed water volume box fitting 1:1 with the horizontal bounds of the target mesh.
    /// Uses mandatory 4 UV sets for Blam water shaders, and non-collideable render-only convention.
    /// </summary>
    public static MeshObject GenerateWaterVolumeForMesh(
        MeshObject targetMesh,
        string materialName = "!river riverworld_water_rough'",
        float? surfaceZ = null,
        float depth = 40.0f)
    {
        var (min, max) = CsgBooleanEngine.ComputeBounds(targetMesh);
        float width = MathF.Max(max.X - min.X, 100.0f);
        float length = MathF.Max(max.Y - min.Y, 100.0f);
        float heightRange = max.Z - min.Z;

        // Default surface Z: lake/river basin depression (22% elevation)
        float sZ = surfaceZ ?? (min.Z + heightRange * 0.22f);
        Vector3 center = new Vector3((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, sZ);

        string mat = materialName;
        if (!mat.StartsWith("!") && !mat.StartsWith("%")) mat = $"!{mat}";
        if (!mat.EndsWith("'")) mat = $"{mat}'";

        var water = GenerateWaterVolume(mat, width, length, depth, center);
        water.MaterialName = mat;
        water.IsCollideable = false;
        return water;
    }

    /// <summary>
    /// Generates a 100% sealed watertight sky dome envelope by grabbing the outer perimeter edges
    /// of the terrain/level geometry directly and building a sky box from the edges straight upwards to a ceiling.
    /// Bottom rim vertices match the terrain perimeter edges 1:1 with +sky material, sealing the map with zero boundary leaks or open edges.
    /// </summary>
    public static MeshObject GenerateWatertightSkyDome(MeshObject levelMesh, float ceilingMargin = 8000.0f, float floorMargin = 500.0f, float horizontalPadding = 500.0f)
    {
        var (min, max) = CsgBooleanEngine.ComputeBounds(levelMesh);
        float maxZ = max.Z + ceilingMargin;
        float centerX = (min.X + max.X) * 0.5f;
        float centerY = (min.Y + max.Y) * 0.5f;

        // Find boundary edges (edges shared by exactly 1 triangle)
        var edgeCount = new Dictionary<(int, int), int>();
        var halfEdges = new Dictionary<int, List<int>>();

        foreach (var tri in levelMesh.Triangles)
        {
            void AddEdge(int u, int v)
            {
                var k = u < v ? (u, v) : (v, u);
                edgeCount[k] = edgeCount.GetValueOrDefault(k, 0) + 1;
            }
            AddEdge(tri.V0, tri.V1);
            AddEdge(tri.V1, tri.V2);
            AddEdge(tri.V2, tri.V0);
        }

        foreach (var tri in levelMesh.Triangles)
        {
            void AddHalf(int u, int v)
            {
                var k = u < v ? (u, v) : (v, u);
                if (edgeCount.TryGetValue(k, out int c) && c == 1)
                {
                    if (!halfEdges.TryGetValue(u, out var l))
                    {
                        l = new List<int>(1);
                        halfEdges[u] = l;
                    }
                    l.Add(v);
                }
            }
            AddHalf(tri.V0, tri.V1);
            AddHalf(tri.V1, tri.V2);
            AddHalf(tri.V2, tri.V0);
        }

        // Trace boundary loop(s)
        var visited = new HashSet<(int, int)>();
        var boundaryLoops = new List<List<int>>();

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
                        foreach (var cand in candidates)
                        {
                            if (!visited.Contains((curr, cand)))
                            {
                                picked = cand;
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
                    boundaryLoops.Add(loop);
                }
            }
        }

        // Pick the outermost boundary loop by maximum enclosed 2D area (never an interior hole)
        List<int>? perimeter = null;
        if (boundaryLoops.Count > 0)
        {
            float maxArea = -1f;
            foreach (var loop in boundaryLoops)
            {
                float area = 0f;
                int count = loop.Count;
                for (int i = 0; i < count; i++)
                {
                    var p0 = levelMesh.Vertices[loop[i]].Position;
                    var p1 = levelMesh.Vertices[loop[(i + 1) % count]].Position;
                    area += (p0.X * p1.Y - p1.X * p0.Y);
                }
                float absArea = MathF.Abs(area);
                if (absArea > maxArea)
                {
                    maxArea = absArea;
                    perimeter = loop;
                }
            }
        }

        var sky = new MeshObject("+sky_watertight_envelope");
        sky.MaterialName = "+sky";
        sky.MaterialSlots.Add("+sky");
        sky.IsCollideable = true;

        bool isTerrainOrSurface = perimeter != null && perimeter.Count >= 3
            && !levelMesh.Name.StartsWith("brush_box", StringComparison.OrdinalIgnoreCase)
            && !levelMesh.Name.StartsWith("brush_cube", StringComparison.OrdinalIgnoreCase);

        if (isTerrainOrSurface)
        {
            int N = perimeter!.Count;

            // Ensure CCW winding in XY plane around (centerX, centerY)
            float signedArea = 0f;
            for (int i = 0; i < N; i++)
            {
                var p0 = levelMesh.Vertices[perimeter[i]].Position;
                var p1 = levelMesh.Vertices[perimeter[(i + 1) % N]].Position;
                signedArea += (p0.X * p1.Y - p1.X * p0.Y);
            }
            if (signedArea < 0f)
            {
                perimeter.Reverse();
            }

            float spanX = MathF.Max(max.X - min.X, 1.0f);
            float spanY = MathF.Max(max.Y - min.Y, 1.0f);

            var botIndices = new int[N];
            var midIndices = new int[N];
            var topIndices = new int[N];

            // 1. Build Multi-Tiered Arched Dome:
            // Tier 0: Rim vertices matching terrain perimeter edges 1:1 (stitched seamlessly)
            // Tier 1: Arched mid-wall expanding slightly outward for cathedral scale
            // Tier 2: Upper dome curving inward toward center apex
            for (int i = 0; i < N; i++)
            {
                var p = levelMesh.Vertices[perimeter[i]].Position;
                var uv = new Vector2((p.X - min.X) / spanX, (p.Y - min.Y) / spanY);

                // Tier 0: Exact match to terrain outer rim
                botIndices[i] = sky.Vertices.Count;
                var vBot = new Vertex(p, Vector3.UnitZ);
                vBot.UVs.Add(uv);
                sky.Vertices.Add(vBot);

                // Tier 1: Mid shoulder (elevated wall)
                float zMid = p.Z + (maxZ - p.Z) * 0.40f;
                float xMid = p.X;
                float yMid = p.Y;
                midIndices[i] = sky.Vertices.Count;
                var vMid = new Vertex(new Vector3(xMid, yMid, zMid), -Vector3.UnitZ);
                vMid.UVs.Add(uv);
                sky.Vertices.Add(vMid);

                // Tier 2: Upper dome curving inward to ceiling level
                float zTop = maxZ;
                float xTop = centerX + (p.X - centerX) * 0.55f;
                float yTop = centerY + (p.Y - centerY) * 0.55f;
                topIndices[i] = sky.Vertices.Count;
                var vTop = new Vertex(new Vector3(xTop, yTop, zTop), -Vector3.UnitZ);
                vTop.UVs.Add(uv);
                sky.Vertices.Add(vTop);
            }

            // 2. Build Wall Quads from Tier 0 -> Tier 1 and Tier 1 -> Tier 2
            for (int i = 0; i < N; i++)
            {
                int next = (i + 1) % N;
                int b0 = botIndices[i];
                int b1 = botIndices[next];
                int m0 = midIndices[i];
                int m1 = midIndices[next];
                int t0 = topIndices[i];
                int t1 = topIndices[next];

                // Tier 0 to Tier 1
                sky.Triangles.Add(new Triangle(b0, m0, b1, 0));
                sky.Triangles.Add(new Triangle(m0, m1, b1, 0));

                // Tier 1 to Tier 2
                sky.Triangles.Add(new Triangle(m0, t0, m1, 0));
                sky.Triangles.Add(new Triangle(t0, t1, m1, 0));
            }

            // 3. Cap high dome apex at maxZ with downward-facing centroid fan
            int ceilingCenterIdx = sky.Vertices.Count;
            var vCenter = new Vertex(new Vector3(centerX, centerY, maxZ), -Vector3.UnitZ);
            vCenter.UVs.Add(new Vector2(0.5f, 0.5f));
            sky.Vertices.Add(vCenter);

            for (int i = 0; i < N; i++)
            {
                int next = (i + 1) % N;
                // Reverse winding order so normal faces inward toward play area
                sky.Triangles.Add(new Triangle(ceilingCenterIdx, topIndices[next], topIndices[i], 0));
            }

            sky.RecalculateNormals();
            return sky;
        }

        // Fallback: If mesh has no open boundary edges (e.g. closed bunker or convex hull)
        return GenerateBoundingSkyBox(min, max, ceilingMargin, floorMargin, horizontalPadding);
    }

    /// <summary>
    /// Seals an entire multi-mesh scene watertight (+sky).
    /// Computes overall scene bounds across all level meshes, selects ground perimeter or builds inward-facing skybox envelope.
    /// </summary>
    public static MeshObject GenerateWatertightSkyDomeForScene(
        IEnumerable<MeshObject> meshes,
        float ceilingMargin = 8000.0f,
        float floorMargin = 500.0f,
        float horizontalPadding = 500.0f)
    {
        var meshList = new List<MeshObject>();
        foreach (var m in meshes)
        {
            if (m != null && m.Vertices.Count > 0)
                meshList.Add(m);
        }

        if (meshList.Count == 0)
        {
            return GenerateBoundingSkyBox(new Vector3(-5000, -5000, -500), new Vector3(5000, 5000, 5000), ceilingMargin, floorMargin, horizontalPadding);
        }

        Vector3 globalMin = new Vector3(float.MaxValue);
        Vector3 globalMax = new Vector3(float.MinValue);
        MeshObject? bestGroundMesh = null;
        float maxFootprint = -1f;

        foreach (var m in meshList)
        {
            var (bMin, bMax) = CsgBooleanEngine.ComputeBounds(m);
            globalMin = Vector3.Min(globalMin, bMin);
            globalMax = Vector3.Max(globalMax, bMax);

            float footprint = (bMax.X - bMin.X) * (bMax.Y - bMin.Y);
            if (footprint > maxFootprint)
            {
                maxFootprint = footprint;
                bestGroundMesh = m;
            }
        }

        if (bestGroundMesh != null && bestGroundMesh.Triangles.Count > 0)
        {
            var candidateSky = GenerateWatertightSkyDome(bestGroundMesh, ceilingMargin, floorMargin, horizontalPadding);
            var (skyMin, skyMax) = CsgBooleanEngine.ComputeBounds(candidateSky);
            if (skyMin.X <= globalMin.X && skyMin.Y <= globalMin.Y && skyMax.X >= globalMax.X && skyMax.Y >= globalMax.Y && skyMax.Z >= globalMax.Z)
            {
                return candidateSky;
            }
        }

        return GenerateBoundingSkyBox(globalMin, globalMax, ceilingMargin, floorMargin, horizontalPadding);
    }

    private static MeshObject GenerateBoundingSkyBox(Vector3 min, Vector3 max, float ceilingMargin, float floorMargin, float horizontalPadding)
    {
        float minX = min.X - horizontalPadding;
        float maxX = max.X + horizontalPadding;
        float minY = min.Y - horizontalPadding;
        float maxY = max.Y + horizontalPadding;
        float minZ = min.Z - floorMargin;
        float maxZ = max.Z + ceilingMargin;

        var mesh = new MeshObject("+sky_watertight_envelope");
        mesh.MaterialName = "+sky";
        mesh.IsCollideable = true;

        var corners = new Vector3[]
        {
            new(minX, minY, maxZ), // 0
            new(maxX, minY, maxZ), // 1
            new(maxX, maxY, maxZ), // 2
            new(minX, maxY, maxZ), // 3
            new(minX, minY, minZ), // 4
            new(maxX, minY, minZ), // 5
            new(maxX, maxY, minZ), // 6
            new(minX, maxY, minZ)  // 7
        };

        float spanX = MathF.Max(maxX - minX, 1.0f);
        float spanY = MathF.Max(maxY - minY, 1.0f);

        for (int i = 0; i < corners.Length; i++)
        {
            var c = corners[i];
            var v = new Vertex(c, Vector3.Zero);
            v.UVs.Add(new Vector2((c.X - minX) / spanX, (c.Y - minY) / spanY));
            mesh.Vertices.Add(v);
        }

        // Ceiling facing down
        mesh.Triangles.Add(new Triangle(0, 2, 1));
        mesh.Triangles.Add(new Triangle(0, 3, 2));

        // Floor facing up
        mesh.Triangles.Add(new Triangle(4, 5, 6));
        mesh.Triangles.Add(new Triangle(4, 6, 7));

        // Inward walls
        mesh.Triangles.Add(new Triangle(0, 1, 5));
        mesh.Triangles.Add(new Triangle(0, 5, 4));

        mesh.Triangles.Add(new Triangle(1, 2, 6));
        mesh.Triangles.Add(new Triangle(1, 6, 5));

        mesh.Triangles.Add(new Triangle(2, 3, 7));
        mesh.Triangles.Add(new Triangle(2, 7, 6));

        mesh.Triangles.Add(new Triangle(3, 0, 4));
        mesh.Triangles.Add(new Triangle(3, 4, 7));

        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>
    /// Generates an authentic UNSC corridor / airlock hallway segment with floor, ceiling, and doorway arches.
    /// </summary>
    public static MeshObject GenerateHallwayAirlock(
        string name = "h3_unsc_corridor",
        float width = 80.0f,
        float height = 75.0f,
        float length = 200.0f,
        Vector3 location = default)
    {
        var mesh = new MeshObject(name);
        mesh.IsCollideable = true;

        float hw = width * 0.5f;
        float hl = length * 0.5f;

        // 8 box vertices
        var corners = new Vector3[]
        {
            new(-hw, -hl, 0), new(hw, -hl, 0), new(hw, hl, 0), new(-hw, hl, 0),
            new(-hw, -hl, height), new(hw, -hl, height), new(hw, hl, height), new(-hw, hl, height)
        };

        foreach (var c in corners)
        {
            mesh.Vertices.Add(new Vertex(c + location, Vector3.UnitZ));
        }

        // Floor (facing up +Z)
        mesh.Triangles.Add(new Triangle(0, 1, 2));
        mesh.Triangles.Add(new Triangle(0, 2, 3));

        // Ceiling (facing down -Z into corridor)
        mesh.Triangles.Add(new Triangle(4, 6, 5));
        mesh.Triangles.Add(new Triangle(4, 7, 6));

        // Left wall (-X, facing inward +X)
        mesh.Triangles.Add(new Triangle(0, 3, 7));
        mesh.Triangles.Add(new Triangle(0, 7, 4));

        // Right wall (+X, facing inward -X)
        mesh.Triangles.Add(new Triangle(1, 5, 6));
        mesh.Triangles.Add(new Triangle(1, 6, 2));

        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>
    /// Generates an airtight rock arch bridge spanning a canyon chokepoint with an integrated planar portal.
    /// </summary>
    public static (MeshObject arch, MeshObject portal) GenerateChokepointArchAndPortal(
        float archWidth = 240.0f,
        float archHeight = 140.0f,
        float wallThickness = 60.0f,
        float canyonSpan = 1600.0f,
        Vector3 location = default)
    {
        var arch = HaloRockGenerator.GenerateRock(
            RockArchetype.NaturalArch,
            new Vector3(canyonSpan * 0.5f, wallThickness, archHeight),
            seed: 42,
            materialName: "dead deadlock_cliff_rock",
            location: location);
        arch.Name = "h3_chokepoint_arch";

        var portal = CsgBrushBuilder.CreatePlanarPortal("h3_chokepoint_portal", archWidth * 0.95f, archHeight, location);
        return (arch, portal);
    }
}

