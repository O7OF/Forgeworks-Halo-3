using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ForgeWorks.Core.Geometry;

namespace ForgeWorks.Core.Procedural;

public enum TerrainAlgorithm
{
    MountainPassValley, // Canyon valley flanked by towering mountain ridges (Blood Gulch / Valhalla)
    CanyonGorge,        // Stepped slot canyons with sheer vertical dropoffs and plateaus
    RollingHills,       // Undulating grassy hills & rolling mounds with smooth swales
    CraterImpact,       // Circular impact crater with raised rim & central rebound peak
    MesaPlateau,        // Multi-tier flat-topped table mesas with sheer cliffs
    DuneSea,            // Wind-rippled desert barchan dunes with sharp crests
    IslandAtoll,        // Island mountain peaks surrounded by sunken water basin
    DomainWarped,       // Winding valleys & meanders (fBm + vector coordinate distortion)
    FbmPerlin,          // Fractal Brownian Motion standard Perlin / Simplex
    DiamondSquare,      // Recursive midpoint displacement fractal (Alpine peaks)
    VoronoiCellular,    // Voronoi / Worley cellular noise (Caldera, basalt terrace, cracked basin)
    RidgedMultifractal  // Razorback alpine spines & mountain crests
}

public enum CellularMode
{
    Caldera,        // Volcanic caldera / flat-topped mesa with steep falls
    BasaltTerrace,  // Stepped columnar hexagonal terraces
    CrackedBasin    // Cracked canyons along cell boundaries
}

public enum TerrainTopologyType
{
    Quads,       // Regular quad mesh (alternating dual-diagonal triangulation)
    Triangles,   // Isometric 60° equilateral diamond lattice
    Hexagons,    // Honeycomb hexagonal cells with center star triangulation
    Polygonal    // Jittered centroid dual polygonal Voronoi-style cells
}

public enum IslandMaskMode
{
    None,
    RadialIslandSinkEdges, // Circular island falling off into deep water
    RadialBasinRaiseEdges, // Ring of mountains enclosing central arena basin
    SquareBasinBowl,       // Box arena valley with raised perimeter
    InvertedCraterCenter   // Deep central crater depression
}

public sealed class TerrainConfig
{
    public TerrainAlgorithm Algorithm { get; set; } = TerrainAlgorithm.MountainPassValley;
    public CellularMode CellularStyle { get; set; } = CellularMode.Caldera;
    public TerrainTopologyType Topology { get; set; } = TerrainTopologyType.Quads;

    public int ResolutionX { get; set; } = 48;
    public int ResolutionY { get; set; } = 48;
    public float Width { get; set; } = 6000.0f;  // X span in units
    public float Length { get; set; } = 6000.0f; // Y span in units
    public float HeightScale { get; set; } = 800.0f;
    public float NoiseFrequency { get; set; } = 0.0005f;
    public int Octaves { get; set; } = 5;
    public float Lacunarity { get; set; } = 2.0f;
    public float Gain { get; set; } = 0.5f;
    public float WarpStrength { get; set; } = 400.0f;
    public float DiamondSquareRoughness { get; set; } = 0.55f;
    public int Seed { get; set; } = 1337;

    public int HydraulicErosionIterations { get; set; } = 5000;
    public int ThermalErosionIterations { get; set; } = 0;
    public float ReposeAngleDeg { get; set; } = 35.0f;

    public float CliffSlopeThreshold { get; set; } = 0.65f;
    public float PeakElevationThreshold { get; set; } = 0.75f;
    public bool SealSkirt { get; set; } = false;
    public float SkirtDepth { get; set; } = 150.0f;
    public string DefaultMaterial { get; set; } = "river riverworld_ground";

    // Realistic perimeter cliff walls & flattened plateau rim for skybox sealing
    public bool OuterCliffWalls { get; set; } = true;
    public float PerimeterCliffHeight { get; set; } = 600.0f;
    public bool SealAcrossTop { get; set; } = false;        // Automatically seals maps across top with +sky ceiling (0 open edges)
    public float CeilingHeight { get; set; } = 2500.0f;     // Skybox ceiling clearance above highest terrain peak
    public bool NoTiling { get; set; } = false;
    public float UvTileScale { get; set; } = 300.0f;

    // Extended Procedural Modifiers (v2.1)
    public int TerraceSteps { get; set; } = 0;              // 0 = disabled, 1..32 = stepped terraces
    public float TerraceSmoothness { get; set; } = 0.35f;   // 0.05 = razor cliff step, 1.0 = soft rolling
    public IslandMaskMode IslandMask { get; set; } = IslandMaskMode.None;
    public float IslandFalloffRadius { get; set; } = 0.75f; // Radius (0..1) where island water falloff begins
    public float PlateauLevel { get; set; } = 1.0f;         // 1.0 = unclipped, <1.0 = flat mesa table tops
    public float ValleyCarveDepth { get; set; } = 0.0f;     // Canyon/river valley depth carved across map
    public float ValleyCarveWidth { get; set; } = 1400.0f;  // Width of carved central canyon
    public bool VertexColorAO { get; set; } = true;         // Curvature & ambient occlusion in vertex colors
    public bool SlopeLayerWeighting { get; set; } = true;   // Automated slope-based multi-layer shader weights
}

public sealed class TerrainGenerator
{
    public static MeshObject GenerateTerrain(TerrainConfig config, string name = "bsp_terrain")
    {
        int resX = Math.Max(config.ResolutionX, 4);
        int resY = Math.Max(config.ResolutionY, 4);

        float dx = config.Width / (resX - 1);
        float dy = config.Length / (resY - 1);
        float startX = -config.Width * 0.5f;
        float startY = -config.Length * 0.5f;

        float[,] heightMap = new float[resX, resY];

        // 1. Evaluate Procedural Heightmap according to selected algorithm
        switch (config.Algorithm)
        {
            case TerrainAlgorithm.MountainPassValley:
                for (int x = 0; x < resX; x++)
                {
                    for (int y = 0; y < resY; y++)
                    {
                        float wx = startX + x * dx;
                        float wy = startY + y * dy;
                        float valleyY = MathF.Sin(wy * 0.0008f) * (config.Width * 0.18f);
                        float distNorm = MathF.Abs(wx - valleyY) / (config.Width * 0.5f);
                        float valley = MathF.Pow(Math.Clamp(distNorm, 0f, 1f), 1.7f);
                        float hills = EvalFbm(wx * config.NoiseFrequency, wy * config.NoiseFrequency, config.Octaves, config.Lacunarity, config.Gain);
                        heightMap[x, y] = (valley * 0.75f + hills * 0.25f) * config.HeightScale;
                    }
                }
                break;

            case TerrainAlgorithm.CanyonGorge:
                for (int x = 0; x < resX; x++)
                {
                    for (int y = 0; y < resY; y++)
                    {
                        float wx = startX + x * dx;
                        float wy = startY + y * dy;
                        float baseNoise = EvalDomainWarp(wx, wy, config.NoiseFrequency * 0.7f, config.WarpStrength, 4, 2f, 0.5f);
                        float steps = 5.0f;
                        float stepped = MathF.Floor(baseNoise * steps) / steps;
                        heightMap[x, y] = (stepped * 0.70f + baseNoise * 0.30f) * config.HeightScale;
                    }
                }
                break;

            case TerrainAlgorithm.RollingHills:
                for (int x = 0; x < resX; x++)
                {
                    for (int y = 0; y < resY; y++)
                    {
                        float wx = startX + x * dx;
                        float wy = startY + y * dy;
                        float h1 = SimplexNoise2D(wx * 0.00035f, wy * 0.00035f) * 0.60f;
                        float h2 = SimplexNoise2D(wx * 0.0008f + 14.2f, wy * 0.0008f + 38.6f) * 0.30f;
                        float h3 = SimplexNoise2D(wx * 0.0016f + 72.1f, wy * 0.0016f + 91.5f) * 0.10f;
                        heightMap[x, y] = (h1 + h2 + h3 + 0.5f) * config.HeightScale;
                    }
                }
                break;

            case TerrainAlgorithm.CraterImpact:
                for (int x = 0; x < resX; x++)
                {
                    for (int y = 0; y < resY; y++)
                    {
                        float wx = startX + x * dx;
                        float wy = startY + y * dy;
                        float r = MathF.Sqrt(wx * wx + wy * wy) / (MathF.Min(config.Width, config.Length) * 0.38f);
                        float c = 0f;
                        if (r < 0.25f) c = (0.25f - r) * 1.8f; // central rebound peak
                        else if (r < 1.0f) c = -MathF.Cos((r - 0.25f) / 0.75f * MathF.PI * 0.5f); // basin depression
                        else if (r < 1.45f) c = MathF.Sin((r - 1.0f) / 0.45f * MathF.PI) * 0.55f; // crater rim ridge
                        float fbm = EvalFbm(wx * 0.0006f, wy * 0.0006f, 4, 2f, 0.5f) * 0.20f;
                        heightMap[x, y] = (c + 0.65f + fbm) * config.HeightScale;
                    }
                }
                break;

            case TerrainAlgorithm.MesaPlateau:
                for (int x = 0; x < resX; x++)
                {
                    for (int y = 0; y < resY; y++)
                    {
                        float wx = startX + x * dx;
                        float wy = startY + y * dy;
                        float fbm = EvalFbm(wx * config.NoiseFrequency, wy * config.NoiseFrequency, 4, 2f, 0.5f);
                        float mesa = MathF.Tanh((fbm - 0.05f) * 7.0f) * 0.5f + 0.5f;
                        heightMap[x, y] = mesa * config.HeightScale;
                    }
                }
                break;

            case TerrainAlgorithm.DuneSea:
                for (int x = 0; x < resX; x++)
                {
                    for (int y = 0; y < resY; y++)
                    {
                        float wx = startX + x * dx;
                        float wy = startY + y * dy;
                        float angle = 0.55f;
                        float uD = (wx * MathF.Cos(angle) + wy * MathF.Sin(angle)) * 0.0007f;
                        float vD = (-wx * MathF.Sin(angle) + wy * MathF.Cos(angle)) * 0.00035f;
                        float barchan = MathF.Sin(uD * MathF.PI * 2.0f + SimplexNoise2D(uD * 2f, vD * 2f) * 1.3f);
                        barchan = barchan > 0 ? MathF.Pow(barchan, 0.75f) : -MathF.Pow(-barchan, 1.8f);
                        float ripple = SimplexNoise2D(wx * 0.003f, wy * 0.003f) * 0.06f;
                        heightMap[x, y] = (barchan * 0.5f + 0.5f + ripple) * config.HeightScale;
                    }
                }
                break;

            case TerrainAlgorithm.IslandAtoll:
                for (int x = 0; x < resX; x++)
                {
                    for (int y = 0; y < resY; y++)
                    {
                        float wx = startX + x * dx;
                        float wy = startY + y * dy;
                        float distNorm = MathF.Sqrt(wx * wx + wy * wy) / (MathF.Min(config.Width, config.Length) * 0.45f);
                        float falloff = Math.Clamp(1.0f - distNorm, 0f, 1f);
                        falloff = falloff * falloff * (3.0f - 2.0f * falloff);
                        float island = EvalDomainWarp(wx, wy, config.NoiseFrequency, config.WarpStrength, 4, 2f, 0.5f);
                        heightMap[x, y] = (island * 0.75f + 0.25f) * falloff * config.HeightScale;
                    }
                }
                break;

            case TerrainAlgorithm.DiamondSquare:
                heightMap = EvalDiamondSquare(resX, resY, config.DiamondSquareRoughness, config.Seed);
                for (int x = 0; x < resX; x++)
                    for (int y = 0; y < resY; y++)
                        heightMap[x, y] *= config.HeightScale;
                break;

            case TerrainAlgorithm.VoronoiCellular:
                for (int x = 0; x < resX; x++)
                {
                    for (int y = 0; y < resY; y++)
                    {
                        float wx = startX + x * dx;
                        float wy = startY + y * dy;
                        float h = EvalVoronoiCellular(wx, wy, config.NoiseFrequency, config.CellularStyle);
                        heightMap[x, y] = h * config.HeightScale;
                    }
                }
                break;

            case TerrainAlgorithm.RidgedMultifractal:
                for (int x = 0; x < resX; x++)
                {
                    for (int y = 0; y < resY; y++)
                    {
                        float wx = startX + x * dx;
                        float wy = startY + y * dy;
                        float h = EvalRidgedMultifractal(wx, wy, config.NoiseFrequency, config.Octaves, config.Lacunarity, config.Gain);
                        heightMap[x, y] = h * config.HeightScale;
                    }
                }
                break;

            case TerrainAlgorithm.FbmPerlin:
                for (int x = 0; x < resX; x++)
                {
                    for (int y = 0; y < resY; y++)
                    {
                        float wx = startX + x * dx;
                        float wy = startY + y * dy;
                        float h = EvalFbm(wx * config.NoiseFrequency, wy * config.NoiseFrequency, config.Octaves, config.Lacunarity, config.Gain);
                        heightMap[x, y] = h * config.HeightScale;
                    }
                }
                break;

            case TerrainAlgorithm.DomainWarped:
            default:
                for (int x = 0; x < resX; x++)
                {
                    for (int y = 0; y < resY; y++)
                    {
                        float wx = startX + x * dx;
                        float wy = startY + y * dy;
                        float h = EvalDomainWarp(wx, wy, config.NoiseFrequency, config.WarpStrength, config.Octaves, config.Lacunarity, config.Gain);
                        heightMap[x, y] = h * config.HeightScale;
                    }
                }
                break;
        }

        // 1.5. Extended Procedural Modifiers (Valley Carving, Island Mask, Terracing, Plateau)

        // Valley Riverbed / Canyon Wash Carving
        if (config.ValleyCarveDepth > 0f)
        {
            float valleyWidth = MathF.Max(config.ValleyCarveWidth, 100.0f);
            for (int x = 0; x < resX; x++)
            {
                for (int y = 0; y < resY; y++)
                {
                    float wx = startX + x * dx;
                    float wy = startY + y * dy;
                    float meander = MathF.Sin(wy * 0.0006f + (config.Seed * 0.1f)) * (config.Width * 0.22f);
                    float dist = MathF.Abs(wx - meander);
                    if (dist < valleyWidth)
                    {
                        float t = dist / valleyWidth;
                        float profile = t * t * (3.0f - 2.0f * t); // Smoothstep U-valley
                        heightMap[x, y] -= (1.0f - profile) * config.ValleyCarveDepth;
                    }
                }
            }
        }

        // Island / Basin Mask Falloff
        if (config.IslandMask != IslandMaskMode.None)
        {
            float halfX = (resX - 1) * 0.5f;
            float halfY = (resY - 1) * 0.5f;
            float sinkTarget = -config.HeightScale * 0.35f;

            for (int x = 0; x < resX; x++)
            {
                for (int y = 0; y < resY; y++)
                {
                    float nx = (x - halfX) / halfX;
                    float ny = (y - halfY) / halfY;
                    float r = MathF.Sqrt(nx * nx + ny * ny);

                    switch (config.IslandMask)
                    {
                        case IslandMaskMode.RadialIslandSinkEdges:
                            if (r >= config.IslandFalloffRadius)
                            {
                                float t = Math.Clamp((r - config.IslandFalloffRadius) / (1.414f - config.IslandFalloffRadius), 0f, 1f);
                                float s = t * t * (3.0f - 2.0f * t);
                                heightMap[x, y] = Lerp(heightMap[x, y], sinkTarget, s);
                            }
                            break;

                        case IslandMaskMode.RadialBasinRaiseEdges:
                            float basinRim = MathF.Max(config.HeightScale * 1.2f, 800.0f);
                            if (r >= 0.40f)
                            {
                                float t = Math.Clamp((r - 0.40f) / 0.60f, 0f, 1f);
                                float s = t * t * (3.0f - 2.0f * t);
                                heightMap[x, y] = Lerp(heightMap[x, y], heightMap[x, y] + basinRim, s);
                            }
                            break;

                        case IslandMaskMode.SquareBasinBowl:
                            float dBox = MathF.Max(MathF.Abs(nx), MathF.Abs(ny));
                            if (dBox >= 0.50f)
                            {
                                float t = Math.Clamp((dBox - 0.50f) / 0.50f, 0f, 1f);
                                float s = t * t * (3.0f - 2.0f * t);
                                heightMap[x, y] += s * config.HeightScale * 0.8f;
                            }
                            break;

                        case IslandMaskMode.InvertedCraterCenter:
                            if (r < 0.65f)
                            {
                                float t = 1.0f - (r / 0.65f);
                                float s = t * t * (3.0f - 2.0f * t);
                                heightMap[x, y] -= s * config.HeightScale * 0.7f;
                            }
                            break;
                    }
                }
            }
        }

        // Terracing / Step Bench Quantization (Sandtrap & Sierra 117 sedimentary rock strata)
        if (config.TerraceSteps > 0)
        {
            float curMin = float.MaxValue, curMax = float.MinValue;
            for (int x = 0; x < resX; x++)
                for (int y = 0; y < resY; y++)
                {
                    curMin = MathF.Min(curMin, heightMap[x, y]);
                    curMax = MathF.Max(curMax, heightMap[x, y]);
                }
            float curSpan = MathF.Max(curMax - curMin, 1.0f);
            float smoothness = Math.Clamp(config.TerraceSmoothness, 0.02f, 0.98f);

            for (int x = 0; x < resX; x++)
            {
                for (int y = 0; y < resY; y++)
                {
                    float normH = (heightMap[x, y] - curMin) / curSpan;
                    float stepVal = normH * config.TerraceSteps;
                    float stepInt = MathF.Floor(stepVal);
                    float frac = stepVal - stepInt;

                    // Smoothstep transition band between steps
                    float lower = 0.5f - smoothness * 0.5f;
                    float upper = 0.5f + smoothness * 0.5f;
                    float blend = Math.Clamp((frac - lower) / (upper - lower), 0f, 1f);
                    blend = blend * blend * (3.0f - 2.0f * blend);

                    float terracedNorm = (stepInt + blend) / config.TerraceSteps;
                    heightMap[x, y] = curMin + terracedNorm * curSpan;
                }
            }
        }

        // Plateau Mesa Table-Top Clamping
        if (config.PlateauLevel < 0.999f && config.PlateauLevel > 0.05f)
        {
            float curMin = float.MaxValue, curMax = float.MinValue;
            for (int x = 0; x < resX; x++)
                for (int y = 0; y < resY; y++)
                {
                    curMin = MathF.Min(curMin, heightMap[x, y]);
                    curMax = MathF.Max(curMax, heightMap[x, y]);
                }
            float plateauH = curMin + (curMax - curMin) * Math.Clamp(config.PlateauLevel, 0.1f, 0.98f);
            for (int x = 0; x < resX; x++)
            {
                for (int y = 0; y < resY; y++)
                {
                    if (heightMap[x, y] > plateauH)
                    {
                        heightMap[x, y] = plateauH;
                    }
                }
            }
        }

        // 2. Realistic Outer Perimeter Cliff Walls & Flattened Rim Plateau for Clean Skybox Sealing
        if (config.OuterCliffWalls)
        {
            float maxInteriorH = 0f;
            for (int x = 0; x < resX; x++)
                for (int y = 0; y < resY; y++)
                    maxInteriorH = MathF.Max(maxInteriorH, heightMap[x, y]);

            float targetRimH = MathF.Max(maxInteriorH, config.HeightScale * 0.9f) + config.PerimeterCliffHeight;

            for (int x = 0; x < resX; x++)
            {
                for (int y = 0; y < resY; y++)
                {
                    float nx = MathF.Abs(x - (resX - 1) * 0.5f) / ((resX - 1) * 0.5f);
                    float ny = MathF.Abs(y - (resY - 1) * 0.5f) / ((resY - 1) * 0.5f);
                    float d = MathF.Max(nx, ny); // Chebyshev box distance (reaches 1 at perimeter edges)

                    if (d >= 0.88f)
                    {
                        // Flattens out on top to make the sky seal look natural, seamless and realistic
                        heightMap[x, y] = targetRimH;
                    }
                    else if (d >= 0.65f)
                    {
                        // Steep cliff face rising up from interior to outer rim plateau
                        float t = (d - 0.65f) / (0.88f - 0.65f);
                        float s = t * t * (3.0f - 2.0f * t);
                        float wx = startX + x * dx;
                        float wy = startY + y * dy;
                        float rockNoise = SimplexNoise2D(wx * 0.002f, wy * 0.002f) * (config.HeightScale * 0.08f) * (1.0f - t);
                        heightMap[x, y] = Lerp(heightMap[x, y], targetRimH, s) + rockNoise;
                    }
                }
            }
        }

        // 3. Physical Geological Erosion Simulations
        if (config.ThermalErosionIterations > 0)
        {
            SimulateThermalErosion(heightMap, resX, resY, dx, dy, config.ReposeAngleDeg, config.ThermalErosionIterations);
        }

        if (config.HydraulicErosionIterations > 0)
        {
            SimulateHydraulicErosion(heightMap, resX, resY, dx, dy, config.HydraulicErosionIterations);
        }

        // 4. Construct Mesh Vertices
        var mesh = new MeshObject(name);
        mesh.MaterialName = !string.IsNullOrEmpty(config.DefaultMaterial) ? config.DefaultMaterial : "river riverworld_ground";
        mesh.IsCollideable = true;
        mesh.GridResolutionX = resX;
        mesh.GridResolutionY = resY;

        float minH = float.MaxValue;
        float maxH = float.MinValue;
        for (int x = 0; x < resX; x++)
        {
            for (int y = 0; y < resY; y++)
            {
                minH = MathF.Min(minH, heightMap[x, y]);
                maxH = MathF.Max(maxH, heightMap[x, y]);
            }
        }
        float heightRange = MathF.Max(maxH - minH, 1.0f);
        float tileScale = config.UvTileScale > 0 ? config.UvTileScale : 300.0f;
        bool noTiling = config.NoTiling || config.UvTileScale <= 0f;

        // 4. Construct Mesh according to selected topology
        switch (config.Topology)
        {
            case TerrainTopologyType.Triangles:
                BuildIsometricTrianglesMesh(mesh, heightMap, resX, resY, startX, startY, dx, dy, config.Width, config.Length, tileScale, noTiling);
                break;

            case TerrainTopologyType.Hexagons:
                BuildHoneycombHexagonsMesh(mesh, heightMap, resX, resY, startX, startY, dx, dy, config.Width, config.Length, tileScale, noTiling);
                break;

            case TerrainTopologyType.Polygonal:
                BuildPolygonalVoronoiMesh(mesh, heightMap, resX, resY, startX, startY, dx, dy, config.Width, config.Length, tileScale, noTiling, config.Seed);
                break;

            case TerrainTopologyType.Quads:
            default:
                BuildQuadsMesh(mesh, heightMap, resX, resY, startX, startY, dx, dy, config.Width, config.Length, tileScale, noTiling);
                break;
        }

        // 5. Seal perimeter skirt if explicitly requested
        if (config.SealSkirt)
        {
            float floorZ = minH - config.SkirtDepth;
            var perimeterIndices = new List<int>();

            for (int x = 0; x < resX; x++) perimeterIndices.Add(x);
            for (int y = 1; y < resY; y++) perimeterIndices.Add(y * resX + (resX - 1));
            for (int x = resX - 2; x >= 0; x--) perimeterIndices.Add((resY - 1) * resX + x);
            for (int y = resY - 2; y > 0; y--) perimeterIndices.Add(y * resX);

            int perimCount = perimeterIndices.Count;
            int floorBaseIdx = mesh.Vertices.Count;

            for (int i = 0; i < perimCount; i++)
            {
                var topPos = mesh.Vertices[perimeterIndices[i]].Position;
                var topUv = mesh.Vertices[perimeterIndices[i]].UVs.Count > 0 ? mesh.Vertices[perimeterIndices[i]].UVs[0] : Vector2.Zero;
                float drop = topPos.Z - floorZ;
                var floorVert = new Vertex(new Vector3(topPos.X, topPos.Y, floorZ), -Vector3.UnitZ);
                floorVert.UVs.Add(new Vector2(topUv.X, topUv.Y + drop / tileScale));
                mesh.Vertices.Add(floorVert);
            }

            for (int i = 0; i < perimCount; i++)
            {
                int next = (i + 1) % perimCount;
                int t0 = perimeterIndices[i];
                int t1 = perimeterIndices[next];
                int b0 = floorBaseIdx + i;
                int b1 = floorBaseIdx + next;

                mesh.Triangles.Add(new Triangle(t0, b0, t1));
                mesh.Triangles.Add(new Triangle(t1, b0, b1));
            }

            // Seal bottom cap of skirt with center centroid fan so bottom is 100% closed
            int floorCenterIdx = mesh.Vertices.Count;
            float cX = startX + config.Width * 0.5f;
            float cY = startY + config.Length * 0.5f;
            var floorCenter = new Vertex(new Vector3(cX, cY, floorZ), -Vector3.UnitZ);
            floorCenter.UVs.Add(new Vector2(0.5f, 0.5f));
            mesh.Vertices.Add(floorCenter);

            for (int i = 0; i < perimCount; i++)
            {
                int next = (i + 1) % perimCount;
                int b0 = floorBaseIdx + i;
                int b1 = floorBaseIdx + next;
                mesh.Triangles.Add(new Triangle(b0, b1, floorCenterIdx));
            }
        }

        // 6. Seal Map Across Top (eliminates all open edge errors across outer cliffs / skybox)
        if (config.SealAcrossTop)
        {
            SealTerrainAcrossTop(mesh, heightMap, resX, resY, startX, startY, dx, dy, config);
        }

        // 8. Recalculate Vertex Normals
        mesh.RecalculateNormals();

        // 9. Layered Blend Colors & Ambient Occlusion for Blam Terrain Shaders
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var v = mesh.Vertices[i];
            float elevationNorm = (v.Position.Z - minH) / heightRange;
            float slopeNorm = 1.0f - MathF.Abs(v.Normal.Z);

            float rockWeight = Math.Clamp((slopeNorm - config.CliffSlopeThreshold) * 4.0f, 0.0f, 1.0f);
            float peakWeight = Math.Clamp((elevationNorm - config.PeakElevationThreshold) * 4.0f, 0.0f, 1.0f);
            float baseWeight = Math.Max(0.0f, 1.0f - rockWeight - peakWeight);

            // Ambient Occlusion / Concavity shading based on normal orientation and crevice shadowing
            float ao = 1.0f;
            if (config.VertexColorAO)
            {
                ao = Math.Clamp(0.70f + 0.30f * v.Normal.Z, 0.55f, 1.0f);
            }

            if (config.SlopeLayerWeighting)
            {
                mesh.Vertices[i].Color = new Vector4(baseWeight * ao, rockWeight * ao, peakWeight * ao, 1.0f);
            }
            else
            {
                mesh.Vertices[i].Color = new Vector4(ao, ao, ao, 1.0f);
            }
        }

        return mesh;
    }

    /// <summary>
    /// Recalculates 1:1 world-space isometric terrain UVs across a mesh.
    /// Maps world coordinates directly to texture UV space, guaranteeing zero shearing or wavy contour banding.
    /// <summary>
    /// Recalculates 1:1 isometric terrain UVs across a mesh.
    /// When noTiling is true (or tileScale <= 0), maps normalized bounding box [0, 1] continuously across the entire canvas with zero repeating tiles.
    /// When noTiling is false, maps world coordinates directly to texture UV space.
    /// </summary>
    public static void RecalculateTerrainUVs(MeshObject mesh, float tileScale = 300.0f, bool noTiling = false)
    {
        if (mesh == null || mesh.Vertices.Count == 0) return;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var p = mesh.Vertices[i].Position;
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        float spanX = MathF.Max(maxX - minX, 1.0f);
        float spanY = MathF.Max(maxY - minY, 1.0f);

        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var p = mesh.Vertices[i].Position;
            var uv = (noTiling || tileScale <= 0f)
                ? new Vector2((p.X - minX) / spanX, (p.Y - minY) / spanY)
                : new Vector2(p.X / tileScale, p.Y / tileScale);

            mesh.Vertices[i].UVs.Clear();
            mesh.Vertices[i].UVs.Add(uv);
        }
    }

    // =========================================================================
    // MATHEMATICAL NOISE & ALGORITHMS (Ported from modules/terrain.py)
    // =========================================================================

    public static float EvalFbm(float x, float y, int octaves, float lacunarity, float gain)
    {
        float total = 0.0f;
        float freq = 1.0f;
        float amp = 1.0f;
        float maxAmp = 0.0f;

        for (int i = 0; i < octaves; i++)
        {
            total += SimplexNoise2D(x * freq, y * freq) * amp;
            maxAmp += amp;
            freq *= lacunarity;
            amp *= gain;
        }

        return total / MathF.Max(maxAmp, 0.0001f);
    }

    public static float EvalDomainWarp(float x, float y, float frequency, float warpStrength, int octaves, float lacunarity, float gain)
    {
        float qx = EvalFbm((x + 0.0f) * frequency, (y + 0.0f) * frequency, octaves, lacunarity, gain);
        float qy = EvalFbm((x + 5.2f) * frequency, (y + 1.3f) * frequency, octaves, lacunarity, gain);

        float rx = EvalFbm((x + warpStrength * qx + 1.7f) * frequency, (y + warpStrength * qy + 9.2f) * frequency, octaves, lacunarity, gain);
        float ry = EvalFbm((x + warpStrength * qx + 8.3f) * frequency, (y + warpStrength * qy + 2.8f) * frequency, octaves, lacunarity, gain);

        return EvalFbm((x + warpStrength * rx) * frequency, (y + warpStrength * ry) * frequency, octaves, lacunarity, gain);
    }

    /// <summary>
    /// Port of eval_diamond_square from modules/terrain.py:
    /// Recursive midpoint displacement fractal on a power-of-two grid, resampled to target resolution.
    /// </summary>
    public static float[,] EvalDiamondSquare(int resX, int resY, float roughness = 0.55f, int seed = 1337)
    {
        int maxDim = Math.Max(resX, resY);
        int n = 1;
        while (n < maxDim - 1) n *= 2;
        int size = n + 1;

        float[,] grid = new float[size, size];
        var rng = new Random(seed);

        float Rnd(float s) => ((float)rng.NextDouble() * 2.0f - 1.0f) * s;

        // Initialize 4 corners
        grid[0, 0] = Rnd(1.0f);
        grid[0, n] = Rnd(1.0f);
        grid[n, 0] = Rnd(1.0f);
        grid[n, n] = Rnd(1.0f);

        int step = n;
        float scale = 1.0f;

        while (step > 1)
        {
            int half = step / 2;

            // Diamond step
            for (int y = half; y < n; y += step)
            {
                for (int x = half; x < n; x += step)
                {
                    float avg = (grid[y - half, x - half] + grid[y - half, x + half] +
                                 grid[y + half, x - half] + grid[y + half, x + half]) * 0.25f;
                    grid[y, x] = avg + Rnd(scale);
                }
            }

            // Square step
            for (int y = 0; y < size; y += half)
            {
                int shift = ((y / half) % 2 == 1) ? 0 : half;
                for (int x = shift; x < size; x += step)
                {
                    float total = 0.0f;
                    int count = 0;
                    if (y >= half) { total += grid[y - half, x]; count++; }
                    if (y + half < size) { total += grid[y + half, x]; count++; }
                    if (x >= half) { total += grid[y, x - half]; count++; }
                    if (x + half < size) { total += grid[y, x + half]; count++; }
                    grid[y, x] = (total / count) + Rnd(scale);
                }
            }

            step /= 2;
            scale *= roughness;
        }

        // Resample grid to target resX, resY
        float[,] result = new float[resX, resY];
        for (int iy = 0; iy < resY; iy++)
        {
            float sy = (iy / (float)Math.Max(1, resY - 1)) * n;
            int y0 = (int)MathF.Floor(sy);
            int y1 = Math.Min(n, y0 + 1);
            float fy = sy - y0;

            for (int ix = 0; ix < resX; ix++)
            {
                float sx = (ix / (float)Math.Max(1, resX - 1)) * n;
                int x0 = (int)MathF.Floor(sx);
                int x1 = Math.Min(n, x0 + 1);
                float fx = sx - x0;

                float top = grid[y0, x0] * (1.0f - fx) + grid[y0, x1] * fx;
                float bot = grid[y1, x0] * (1.0f - fx) + grid[y1, x1] * fx;
                result[ix, iy] = (top * (1.0f - fy) + bot * fy) * 0.5f + 0.5f;
            }
        }

        return result;
    }

    /// <summary>
    /// Port of eval_voronoi_cellular from modules/terrain.py:
    /// Distance-to-feature-points for basalt terraces, calderas, and cracked basins.
    /// </summary>
    public static float EvalVoronoiCellular(float x, float y, float frequency, CellularMode mode)
    {
        float px = x * frequency;
        float py = y * frequency;

        int xi = (int)MathF.Floor(px);
        int yi = (int)MathF.Floor(py);

        float d1 = float.MaxValue;
        float d2 = float.MaxValue;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = xi + dx;
                int cy = yi + dy;

                int h = Hash(cx, cy);
                float fx = cx + ((h & 0xFF) / 255.0f);
                float fy = cy + (((h >> 8) & 0xFF) / 255.0f);

                float dist = MathF.Sqrt((px - fx) * (px - fx) + (py - fy) * (py - fy));
                if (dist < d1)
                {
                    d2 = d1;
                    d1 = dist;
                }
                else if (dist < d2)
                {
                    d2 = dist;
                }
            }
        }

        switch (mode)
        {
            case CellularMode.BasaltTerrace:
                float steps = 4.0f;
                float val = (d2 - d1) * 2.0f;
                return MathF.Floor(val * steps) / steps;

            case CellularMode.CrackedBasin:
                float edge = d2 - d1;
                return MathF.Min(1.0f, edge * 3.5f);

            case CellularMode.Caldera:
            default:
                float v = 1.0f - MathF.Min(1.0f, d1 * 1.5f);
                return v * v * (3.0f - 2.0f * v);
        }
    }

    /// <summary>
    /// Port of eval_ridged_multifractal from modules/terrain.py:
    /// Inverted absolute value noise producing razorback mountain crests and alpine spines.
    /// </summary>
    public static float EvalRidgedMultifractal(float x, float y, float frequency, int octaves, float lacunarity, float gain, float sharpness = 2.0f)
    {
        float total = 0.0f;
        float freq = frequency;
        float amp = 1.0f;
        float weight = 1.0f;

        for (int i = 0; i < octaves; i++)
        {
            float signal = 1.0f - MathF.Abs(SimplexNoise2D(x * freq, y * freq));
            signal = MathF.Pow(signal, sharpness);
            signal *= weight;
            weight = Math.Clamp(signal * 2.0f, 0.0f, 1.0f);

            total += signal * amp;
            freq *= lacunarity;
            amp *= gain;
        }

        return total * 0.45f;
    }

    /// <summary>
    /// Port of simulate_thermal_erosion from modules/terrain.py:
    /// Simulates gravity rockfall and weathering along steep slopes exceeding the angle of repose.
    /// </summary>
    public static void SimulateThermalErosion(float[,] grid, int resX, int resY, float dx, float dy, float reposeAngleDeg = 35.0f, int iterations = 8, float talusRate = 0.35f)
    {
        float tanRepose = MathF.Tan(reposeAngleDeg * MathF.PI / 180.0f);
        float talusLimitX = dx * tanRepose;
        float talusLimitY = dy * tanRepose;

        for (int it = 0; it < iterations; it++)
        {
            for (int y = 1; y < resY - 1; y++)
            {
                for (int x = 1; x < resX - 1; x++)
                {
                    float z = grid[x, y];
                    (int nx, int ny, float limit)[] neighbors =
                    {
                        (x + 1, y, talusLimitX),
                        (x - 1, y, talusLimitX),
                        (x, y + 1, talusLimitY),
                        (x, y - 1, talusLimitY)
                    };

                    foreach (var (nx, ny, limit) in neighbors)
                    {
                        float diff = z - grid[nx, ny];
                        if (diff > limit)
                        {
                            float excess = (diff - limit) * talusRate * 0.25f;
                            grid[x, y] -= excess;
                            grid[nx, ny] += excess;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Particle-based hydraulic erosion carving river tributaries and sediment basins.
    /// </summary>
    public static void SimulateHydraulicErosion(float[,] map, int resX, int resY, float dx, float dy, int iterations)
    {
        var rng = new Random(42);

        for (int i = 0; i < iterations; i++)
        {
            float px = (float)rng.NextDouble() * (resX - 2) + 1;
            float py = (float)rng.NextDouble() * (resY - 2) + 1;
            float dirX = 0;
            float dirY = 0;
            float speed = 1.0f;
            float water = 1.0f;
            float sediment = 0.0f;

            for (int step = 0; step < 30; step++)
            {
                int ix = (int)px;
                int iy = (int)py;

                float gx = (map[ix + 1, iy] - map[ix - 1, iy]) * 0.5f;
                float gy = (map[ix, iy + 1] - map[ix, iy - 1]) * 0.5f;

                dirX = (dirX * 0.5f) - (gx * 0.5f);
                dirY = (dirY * 0.5f) - (gy * 0.5f);

                float len = MathF.Sqrt(dirX * dirX + dirY * dirY);
                if (len < 0.001f) break;

                dirX /= len;
                dirY /= len;

                px += dirX;
                py += dirY;

                if (px < 1 || px >= resX - 2 || py < 1 || py >= resY - 2)
                    break;

                int nextX = (int)px;
                int nextY = (int)py;
                float heightDiff = map[nextX, nextY] - map[ix, iy];

                float capacity = MathF.Max(-heightDiff * speed * water * 0.1f, 0.01f);
                if (sediment > capacity)
                {
                    float deposit = (sediment - capacity) * 0.3f;
                    map[ix, iy] += deposit;
                    sediment -= deposit;
                }
                else
                {
                    float erode = MathF.Min((capacity - sediment) * 0.3f, -heightDiff);
                    map[ix, iy] -= erode;
                    sediment += erode;
                }

                speed = MathF.Sqrt(MathF.Max(speed * speed - heightDiff, 0.01f));
                water *= 0.95f;
            }
        }
    }

    // Fast 2D Simplex/Perlin-style value noise
    private static float SimplexNoise2D(float x, float y)
    {
        int xi = (int)MathF.Floor(x) & 255;
        int yi = (int)MathF.Floor(y) & 255;

        float xf = x - MathF.Floor(x);
        float yf = y - MathF.Floor(y);

        float u = Fade(xf);
        float v = Fade(yf);

        int aa = Hash(xi, yi);
        int ab = Hash(xi, yi + 1);
        int ba = Hash(xi + 1, yi);
        int bb = Hash(xi + 1, yi + 1);

        float x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
        float x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);

        return Lerp(x1, x2, v);
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static float Lerp(float a, float b, float t) => a + t * (b - a);
    private static int Hash(int x, int y) => ((x * 1619 + y * 31337) ^ (x * 37 + y * 13)) & 0xFFFF;

    private static float Grad(int hash, float x, float y)
    {
        int h = hash & 3;
        return h switch
        {
            0 => x + y,
            1 => -x + y,
            2 => x - y,
            _ => -x - y
        };
    }

    public static float SampleBilinearHeight(float[,] heightMap, float wx, float wy, float startX, float startY, float dx, float dy, int resX, int resY)
    {
        float gx = (wx - startX) / dx;
        float gy = (wy - startY) / dy;
        int x0 = Math.Clamp((int)MathF.Floor(gx), 0, resX - 1);
        int y0 = Math.Clamp((int)MathF.Floor(gy), 0, resY - 1);
        int x1 = Math.Clamp(x0 + 1, 0, resX - 1);
        int y1 = Math.Clamp(y0 + 1, 0, resY - 1);
        float fx = Math.Clamp(gx - x0, 0f, 1f);
        float fy = Math.Clamp(gy - y0, 0f, 1f);

        float h00 = heightMap[x0, y0];
        float h10 = heightMap[x1, y0];
        float h01 = heightMap[x0, y1];
        float h11 = heightMap[x1, y1];

        return (h00 * (1f - fx) + h10 * fx) * (1f - fy) + (h01 * (1f - fx) + h11 * fx) * fy;
    }

    private static void BuildQuadsMesh(MeshObject mesh, float[,] heightMap, int resX, int resY, float startX, float startY, float dx, float dy, float width, float length, float tileScale, bool noTiling)
    {
        float spanX = MathF.Max(width, 1.0f);
        float spanY = MathF.Max(length, 1.0f);

        for (int y = 0; y < resY; y++)
        {
            for (int x = 0; x < resX; x++)
            {
                float px = startX + x * dx;
                float py = startY + y * dy;
                float pz = heightMap[x, y];
                var pos = new Vector3(px, py, pz);
                var v = new Vertex(pos, Vector3.UnitZ);
                var uv = (noTiling || tileScale <= 0f)
                    ? new Vector2((px - startX) / spanX, (py - startY) / spanY)
                    : new Vector2(pos.X / tileScale, pos.Y / tileScale);
                v.UVs.Add(uv);
                mesh.Vertices.Add(v);
            }
        }

        for (int y = 0; y < resY - 1; y++)
        {
            for (int x = 0; x < resX - 1; x++)
            {
                int i0 = y * resX + x;
                int i1 = y * resX + (x + 1);
                int i2 = (y + 1) * resX + x;
                int i3 = (y + 1) * resX + (x + 1);

                if ((x + y) % 2 == 0)
                {
                    mesh.Triangles.Add(new Triangle(i0, i1, i3));
                    mesh.Triangles.Add(new Triangle(i0, i3, i2));
                }
                else
                {
                    mesh.Triangles.Add(new Triangle(i0, i1, i2));
                    mesh.Triangles.Add(new Triangle(i1, i3, i2));
                }
            }
        }
    }

    private static void BuildIsometricTrianglesMesh(MeshObject mesh, float[,] heightMap, int resX, int resY, float startX, float startY, float dx, float dy, float width, float length, float tileScale, bool noTiling)
    {
        float spanX = MathF.Max(width, 1.0f);
        float spanY = MathF.Max(length, 1.0f);

        for (int y = 0; y < resY; y++)
        {
            float rowOffset = (y % 2 == 1) ? 0.5f * dx : 0f;
            for (int x = 0; x < resX; x++)
            {
                float px = startX + x * dx + rowOffset;
                if (x == 0) px = startX;
                else if (x == resX - 1) px = startX + width;

                float py = startY + y * dy;
                float pz = SampleBilinearHeight(heightMap, px, py, startX, startY, dx, dy, resX, resY);
                var pos = new Vector3(px, py, pz);
                var v = new Vertex(pos, Vector3.UnitZ);
                var uv = (noTiling || tileScale <= 0f)
                    ? new Vector2((px - startX) / spanX, (py - startY) / spanY)
                    : new Vector2(pos.X / tileScale, pos.Y / tileScale);
                v.UVs.Add(uv);
                mesh.Vertices.Add(v);
            }
        }

        for (int y = 0; y < resY - 1; y++)
        {
            for (int x = 0; x < resX - 1; x++)
            {
                int i0 = y * resX + x;
                int i1 = y * resX + (x + 1);
                int i2 = (y + 1) * resX + x;
                int i3 = (y + 1) * resX + (x + 1);

                if (y % 2 == 0)
                {
                    mesh.Triangles.Add(new Triangle(i0, i1, i2));
                    mesh.Triangles.Add(new Triangle(i1, i3, i2));
                }
                else
                {
                    mesh.Triangles.Add(new Triangle(i0, i1, i3));
                    mesh.Triangles.Add(new Triangle(i0, i3, i2));
                }
            }
        }
    }

    private static void BuildHoneycombHexagonsMesh(MeshObject mesh, float[,] heightMap, int resX, int resY, float startX, float startY, float dx, float dy, float width, float length, float tileScale, bool noTiling)
    {
        float spanX = MathF.Max(width, 1.0f);
        float spanY = MathF.Max(length, 1.0f);

        var vertMap = new Dictionary<(int, int), int>();
        int GetOrCreateVertex(float px, float py)
        {
            px = Math.Clamp(px, startX, startX + width);
            py = Math.Clamp(py, startY, startY + length);

            var key = ((int)MathF.Round(px * 5.0f), (int)MathF.Round(py * 5.0f));
            if (vertMap.TryGetValue(key, out int existing)) return existing;

            float pz = SampleBilinearHeight(heightMap, px, py, startX, startY, dx, dy, resX, resY);
            var pos = new Vector3(px, py, pz);
            var v = new Vertex(pos, Vector3.UnitZ);
            var uv = (noTiling || tileScale <= 0f)
                ? new Vector2((px - startX) / spanX, (py - startY) / spanY)
                : new Vector2(pos.X / tileScale, pos.Y / tileScale);
            v.UVs.Add(uv);
            int idx = mesh.Vertices.Count;
            mesh.Vertices.Add(v);
            vertMap[key] = idx;
            return idx;
        }

        float hexRadius = dx * 0.70f;
        for (int y = 0; y < resY; y++)
        {
            float rowOffsetX = (y % 2 == 1) ? 0.5f * dx : 0f;
            for (int x = 0; x < resX; x++)
            {
                float cx = startX + x * dx + rowOffsetX;
                float cy = startY + y * dy;
                int centerIdx = GetOrCreateVertex(cx, cy);

                int[] ring = new int[6];
                for (int k = 0; k < 6; k++)
                {
                    float ang = (k * 60.0f + 30.0f) * MathF.PI / 180.0f;
                    float kx = cx + MathF.Cos(ang) * hexRadius;
                    float ky = cy + MathF.Sin(ang) * hexRadius;
                    ring[k] = GetOrCreateVertex(kx, ky);
                }

                for (int k = 0; k < 6; k++)
                {
                    int nxt = (k + 1) % 6;
                    if (ring[k] != ring[nxt] && ring[k] != centerIdx && ring[nxt] != centerIdx)
                    {
                        mesh.Triangles.Add(new Triangle(centerIdx, ring[k], ring[nxt]));
                    }
                }
            }
        }
    }

    private static void BuildPolygonalVoronoiMesh(MeshObject mesh, float[,] heightMap, int resX, int resY, float startX, float startY, float dx, float dy, float width, float length, float tileScale, bool noTiling, int seed)
    {
        float spanX = MathF.Max(width, 1.0f);
        float spanY = MathF.Max(length, 1.0f);

        var rng = new Random(seed);
        int[,] gridVerts = new int[resX, resY];

        for (int y = 0; y < resY; y++)
        {
            for (int x = 0; x < resX; x++)
            {
                float px = startX + x * dx;
                float py = startY + y * dy;

                if (x > 0 && x < resX - 1 && y > 0 && y < resY - 1)
                {
                    px += ((float)rng.NextDouble() - 0.5f) * 0.45f * dx;
                    py += ((float)rng.NextDouble() - 0.5f) * 0.45f * dy;
                }

                float pz = SampleBilinearHeight(heightMap, px, py, startX, startY, dx, dy, resX, resY);
                var pos = new Vector3(px, py, pz);
                var v = new Vertex(pos, Vector3.UnitZ);
                var uv = (noTiling || tileScale <= 0f)
                    ? new Vector2((px - startX) / spanX, (py - startY) / spanY)
                    : new Vector2(pos.X / tileScale, pos.Y / tileScale);
                v.UVs.Add(uv);
                gridVerts[x, y] = mesh.Vertices.Count;
                mesh.Vertices.Add(v);
            }
        }

        for (int y = 0; y < resY - 1; y++)
        {
            for (int x = 0; x < resX - 1; x++)
            {
                int i00 = gridVerts[x, y];
                int i10 = gridVerts[x + 1, y];
                int i11 = gridVerts[x + 1, y + 1];
                int i01 = gridVerts[x, y + 1];

                var p00 = mesh.Vertices[i00].Position;
                var p10 = mesh.Vertices[i10].Position;
                var p11 = mesh.Vertices[i11].Position;
                var p01 = mesh.Vertices[i01].Position;

                var cPos = (p00 + p10 + p11 + p01) * 0.25f;
                cPos.Z = SampleBilinearHeight(heightMap, cPos.X, cPos.Y, startX, startY, dx, dy, resX, resY);

                int cIdx = mesh.Vertices.Count;
                var cv = new Vertex(cPos, Vector3.UnitZ);
                var cuv = (noTiling || tileScale <= 0f)
                    ? new Vector2((cPos.X - startX) / spanX, (cPos.Y - startY) / spanY)
                    : new Vector2(cPos.X / tileScale, cPos.Y / tileScale);
                cv.UVs.Add(cuv);
                mesh.Vertices.Add(cv);

                mesh.Triangles.Add(new Triangle(cIdx, i00, i10));
                mesh.Triangles.Add(new Triangle(cIdx, i10, i11));
                mesh.Triangles.Add(new Triangle(cIdx, i11, i01));
                mesh.Triangles.Add(new Triangle(cIdx, i01, i00));
            }
        }
    }

    private static void SealTerrainAcrossTop(MeshObject mesh, float[,] heightMap, int resX, int resY, float startX, float startY, float dx, float dy, TerrainConfig config)
    {
        if (mesh == null || mesh.Vertices.Count == 0 || resX < 2 || resY < 2) return;

        // Ensure +sky material exists in MaterialSlots
        int skyMatIdx = mesh.MaterialSlots.IndexOf("+sky");
        if (skyMatIdx < 0)
        {
            skyMatIdx = mesh.MaterialSlots.Count;
            mesh.MaterialSlots.Add("+sky");
        }

        // Determine top rim height and ceiling elevation
        float maxH = float.MinValue;
        for (int x = 0; x < resX; x++)
            for (int y = 0; y < resY; y++)
                maxH = MathF.Max(maxH, heightMap[x, y]);

        float ceilingZ = maxH + MathF.Max(config.CeilingHeight, 1000.0f);
        float spanX = MathF.Max(config.Width, 1.0f);
        float spanY = MathF.Max(config.Length, 1.0f);

        if (config.Topology == TerrainTopologyType.Quads)
        {
            // For Quad topology, vertices are indexed as y * resX + x.
            // Create a ceiling grid where the boundary vertices reuse the exact perimeter vertices of the terrain,
            // and interior ceiling vertices are placed at ceilingZ facing downward (-Vector3.UnitZ).
            int[,] ceilingIndices = new int[resX, resY];

            for (int y = 0; y < resY; y++)
            {
                for (int x = 0; x < resX; x++)
                {
                    bool isBoundary = (x == 0 || x == resX - 1 || y == 0 || y == resY - 1);
                    if (isBoundary)
                    {
                        // Stitched 1:1 with terrain rim: exact same vertex index!
                        ceilingIndices[x, y] = y * resX + x;
                    }
                    else
                    {
                        float px = startX + x * dx;
                        float py = startY + y * dy;
                        // Arched dome toward center
                        float nx = MathF.Abs(x - (resX - 1) * 0.5f) / ((resX - 1) * 0.5f);
                        float ny = MathF.Abs(y - (resY - 1) * 0.5f) / ((resY - 1) * 0.5f);
                        float dist = MathF.Max(nx, ny);
                        float arch = MathF.Cos(dist * MathF.PI * 0.5f) * 500.0f;

                        var vCeil = new Vertex(new Vector3(px, py, ceilingZ + arch), -Vector3.UnitZ);
                        vCeil.UVs.Add(new Vector2((px - startX) / spanX, (py - startY) / spanY));
                        ceilingIndices[x, y] = mesh.Vertices.Count;
                        mesh.Vertices.Add(vCeil);
                    }
                }
            }

            // Add downward-facing ceiling quads (wound in reverse of terrain so boundary half-edges match 1:1)
            for (int y = 0; y < resY - 1; y++)
            {
                for (int x = 0; x < resX - 1; x++)
                {
                    int c0 = ceilingIndices[x, y];
                    int c1 = ceilingIndices[x + 1, y];
                    int c2 = ceilingIndices[x, y + 1];
                    int c3 = ceilingIndices[x + 1, y + 1];

                    // Downward-facing normals (-Z)
                    mesh.Triangles.Add(new Triangle(c0, c3, c1, skyMatIdx));
                    mesh.Triangles.Add(new Triangle(c0, c2, c3, skyMatIdx));
                }
            }
        }
        else
        {
            // For other topologies: trace outer perimeter loop and cap with downward-facing centroid fan
            var perimeterIndices = new List<int>();
            for (int x = 0; x < resX; x++) perimeterIndices.Add(x);
            for (int y = 1; y < resY; y++) perimeterIndices.Add(y * resX + (resX - 1));
            for (int x = resX - 2; x >= 0; x--) perimeterIndices.Add((resY - 1) * resX + x);
            for (int y = resY - 2; y > 0; y--) perimeterIndices.Add(y * resX);

            int perimCount = perimeterIndices.Count;
            int apexIdx = mesh.Vertices.Count;
            float cX = startX + config.Width * 0.5f;
            float cY = startY + config.Length * 0.5f;
            var apex = new Vertex(new Vector3(cX, cY, ceilingZ + 1000.0f), -Vector3.UnitZ);
            apex.UVs.Add(new Vector2(0.5f, 0.5f));
            mesh.Vertices.Add(apex);

            for (int i = 0; i < perimCount; i++)
            {
                int next = (i + 1) % perimCount;
                int p0 = perimeterIndices[i];
                int p1 = perimeterIndices[next];
                // Downward facing: p0 -> p1 -> apex
                mesh.Triangles.Add(new Triangle(p0, p1, apexIdx, skyMatIdx));
            }
        }
    }
}
