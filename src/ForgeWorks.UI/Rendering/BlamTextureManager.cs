using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Silk.NET.OpenGL;

namespace ForgeWorks.UI.Rendering;

/// <summary>
/// High-fidelity Blam! Engine / Sapien texture manager.
/// Loads, caches, and binds authentic textures for Halo 3 shaders, custom model textures,
/// and procedural PBR surfaces for real-time viewport material preview and painting.
/// </summary>
public sealed class BlamTextureManager : IDisposable
{
    private readonly Dictionary<string, uint> _cache = new(StringComparer.OrdinalIgnoreCase);
    private uint _terrainGrassTex;
    private uint _terrainRockTex;
    private uint _terrainSnowTex;
    private uint _terrainMudTex;
    private uint _defaultGridTex;
    private bool _initialized;

    public void Initialize(GL gl)
    {
        if (_initialized) return;

        _defaultGridTex = CreateCheckerboardTexture(gl, 256, 32);
        _terrainGrassTex = CreateGrassTexture(gl, 256);
        _terrainRockTex = CreateRockTexture(gl, 256);
        _terrainSnowTex = CreateSnowTexture(gl, 256);
        _terrainMudTex = CreateMudTexture(gl, 256);

        _cache["default"] = _defaultGridTex;
        _cache["grass"] = _terrainGrassTex;
        _cache["rock"] = _terrainRockTex;
        _cache["snow"] = _terrainSnowTex;
        _cache["mud"] = _terrainMudTex;

        _initialized = true;
    }

    public (uint grass, uint rock, uint snow, uint mud) GetTerrainTextures(GL gl)
    {
        if (!_initialized) Initialize(gl);
        return (_terrainGrassTex, _terrainRockTex, _terrainSnowTex, _terrainMudTex);
    }

    public uint GetOrCreateMaterialTexture(
        GL gl,
        string? materialName,
        string? ekPath = null,
        string? activeLevelName = null,
        string? modelDirectory = null,
        string? explicitTexturePath = null)
    {
        if (!_initialized) Initialize(gl);
        if (string.IsNullOrWhiteSpace(materialName)) return _defaultGridTex;

        if (!string.IsNullOrEmpty(explicitTexturePath) && File.Exists(explicitTexturePath))
        {
            if (_cache.TryGetValue(explicitTexturePath, out uint cachedExplicit) && cachedExplicit != 0)
            {
                return cachedExplicit;
            }

            uint explicitGlTex = LoadTextureFromFile(gl, explicitTexturePath);
            if (explicitGlTex != 0)
            {
                _cache[explicitTexturePath] = explicitGlTex;
                return explicitGlTex;
            }
        }

        string cleanName = CleanMaterialName(materialName);
        if (_cache.TryGetValue(cleanName, out uint cachedId) && cachedId != 0)
        {
            return cachedId;
        }

        // 1. Search for custom texture files on disk (Model dir, Level Bitmaps, EK Data)
        string? diskFile = FindTextureFileOnDisk(cleanName, materialName, modelDirectory, ekPath, activeLevelName);
        if (!string.IsNullOrEmpty(diskFile) && File.Exists(diskFile))
        {
            uint diskTex = LoadTextureFromFile(gl, diskFile);
            if (diskTex != 0)
            {
                _cache[cleanName] = diskTex;
                return diskTex;
            }
        }

        // 2. Fall back to authentic Blam procedural texture corresponding to shader category
        uint proceduralTex = GenerateProceduralTextureForMaterial(gl, cleanName);
        _cache[cleanName] = proceduralTex;
        return proceduralTex;
    }

    private static string CleanMaterialName(string mat)
    {
        var clean = mat.Trim();
        if (clean.StartsWith("!") || clean.StartsWith("%!")) clean = clean.TrimStart('!', '%');
        if (clean.EndsWith("!") || clean.EndsWith("$")) clean = clean.TrimEnd('!', '$');
        int space = clean.IndexOf(' ');
        if (space > 0 && space < clean.Length - 1)
        {
            // E.g. "river riverworld_ground" -> "riverworld_ground" or keep both
            return clean.ToLowerInvariant();
        }
        return clean.ToLowerInvariant();
    }

    private static string? FindTextureFileOnDisk(
        string cleanName,
        string rawMaterialName,
        string? modelDirectory,
        string? ekPath,
        string? activeLevelName)
    {
        var extensions = new[] { ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".dds", ".tga" };

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { cleanName };
        if (!string.IsNullOrWhiteSpace(rawMaterialName))
        {
            candidates.Add(rawMaterialName.Trim());
            string cleanRaw = rawMaterialName.Trim().ToLowerInvariant();
            if (cleanRaw.StartsWith("material_", StringComparison.OrdinalIgnoreCase)) cleanRaw = cleanRaw[9..];
            if (cleanRaw.StartsWith("mat_", StringComparison.OrdinalIgnoreCase)) cleanRaw = cleanRaw[4..];
            candidates.Add(cleanRaw);
            candidates.Add(cleanRaw.Replace('_', ' '));

            int firstUs = cleanRaw.IndexOf('_');
            if (firstUs > 0 && firstUs < cleanRaw.Length - 1)
            {
                string sub = cleanRaw[(firstUs + 1)..];
                candidates.Add(sub);
                candidates.Add(sub.Replace('_', ' '));
            }
        }
        candidates.Add(cleanName.Replace('_', ' '));

        int usIdx = cleanName.IndexOf('_');
        if (usIdx > 0 && usIdx < cleanName.Length - 1)
        {
            string sub = cleanName[(usIdx + 1)..];
            candidates.Add(sub);
            candidates.Add(sub.Replace('_', ' '));
        }

        // Search directory of imported model (e.g. Ice Fields/, Scenery/, textures/, etc.)
        if (!string.IsNullOrWhiteSpace(modelDirectory) && Directory.Exists(modelDirectory))
        {
            var searchDirs = new List<string>
            {
                modelDirectory,
                Path.Combine(modelDirectory, "Scenery"),
                Path.Combine(modelDirectory, "scenery"),
                Path.Combine(modelDirectory, "textures"),
                Path.Combine(modelDirectory, "bitmaps"),
                Path.Combine(modelDirectory, "Textures"),
                Path.Combine(modelDirectory, "Bitmaps")
            };

            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;

                foreach (var cand in candidates)
                {
                    foreach (var ext in extensions)
                    {
                        string p1 = Path.Combine(dir, cand + ext);
                        if (File.Exists(p1)) return p1;
                    }
                }
            }
        }

        // Search H3EK data/levels/<type>/<level>/bitmaps/
        if (!string.IsNullOrWhiteSpace(ekPath) && !string.IsNullOrWhiteSpace(activeLevelName) && Directory.Exists(ekPath))
        {
            string ekBitmaps = Path.Combine(ekPath, "data", "levels", "multi", activeLevelName, "bitmaps");
            if (Directory.Exists(ekBitmaps))
            {
                foreach (var cand in candidates)
                {
                    foreach (var ext in extensions)
                    {
                        string p = Path.Combine(ekBitmaps, cand + ext);
                        if (File.Exists(p)) return p;
                    }
                }
            }
        }

        // Global Desktop fallback for Ice Fields
        string desktopIceFields = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Ice Fields");
        if (Directory.Exists(desktopIceFields))
        {
            var searchDirs = new[] { desktopIceFields, Path.Combine(desktopIceFields, "Scenery") };
            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var cand in candidates)
                {
                    foreach (var ext in extensions)
                    {
                        string p = Path.Combine(dir, cand + ext);
                        if (File.Exists(p)) return p;
                    }
                }
            }
        }

        return null;
    }

    private uint GenerateProceduralTextureForMaterial(GL gl, string mat)
    {
        if (mat.Contains("water") || mat.Contains("ocean") || mat.Contains("swamp"))
            return CreateWaterTexture(gl, 256);

        if (mat.Contains("needle") || mat.Contains("leaf") || mat.Contains("leaves") || mat.Contains("foliage") || mat.Contains("fern") || mat.Contains("bush"))
            return CreateFoliageTexture(gl, 256);

        if (mat.Contains("bark") || mat.Contains("wood") || mat.Contains("trunk") || mat.Contains("log") || mat.Contains("shingle") || mat.Contains("roof") || mat.Contains("plank"))
            return CreateWoodTexture(gl, 256);

        if (mat.Contains("rock") || mat.Contains("cliff") || mat.Contains("basalt") || mat.Contains("boulder") || mat.Contains("stone") || mat.Contains("crag"))
            return _terrainRockTex;

        if (mat.Contains("covenant") || mat.Contains("purple") || mat.Contains("hull") || mat.Contains("plasma") || mat.Contains("elite"))
            return CreateCovenantTexture(gl, 256);

        if (mat.Contains("sand") || mat.Contains("dune") || mat.Contains("desert"))
            return CreateSandTexture(gl, 256);

        if (mat.Contains("mud") || mat.Contains("dirt") || mat.Contains("swamp_ground"))
            return _terrainMudTex;

        if (mat.Contains("metal") || mat.Contains("steel") || mat.Contains("trim") || mat.Contains("plate") || mat.Contains("unsc") || mat.Contains("panel") || mat.Contains("girder"))
            return CreateMetalTexture(gl, 256);

        if (mat.Contains("concrete") || mat.Contains("floor") || mat.Contains("wall") || mat.Contains("structure") || mat.Contains("bunker"))
            return CreateConcreteTexture(gl, 256);

        if (mat.Contains("grass") || mat.Contains("ground") || mat.Contains("terrain"))
            return _terrainGrassTex;

        return _defaultGridTex;
    }

    public static unsafe uint LoadTextureFromFile(GL gl, string filePath)
    {
        if (!File.Exists(filePath)) return 0;
        try
        {
            using var img = new Bitmap(filePath);
            int w = img.Width;
            int h = img.Height;

            // Lock bitmap bits for fast 32bpp ARGB extraction
            var data = img.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                byte[] rgba = new byte[w * h * 4];
                byte* src = (byte*)data.Scan0;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int srcIdx = y * data.Stride + x * 4;
                        int dstIdx = (y * w + x) * 4;
                        rgba[dstIdx + 0] = src[srcIdx + 2]; // R
                        rgba[dstIdx + 1] = src[srcIdx + 1]; // G
                        rgba[dstIdx + 2] = src[srcIdx + 0]; // B
                        rgba[dstIdx + 3] = src[srcIdx + 3]; // A
                    }
                }

                return CreateGlTexture(gl, w, h, rgba);
            }
            finally
            {
                img.UnlockBits(data);
            }
        }
        catch
        {
            return 0;
        }
    }

    public static unsafe uint CreateGlTexture(GL gl, int width, int height, byte[] rgbaPixels, bool repeat = true)
    {
        uint texId = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texId);

        fixed (byte* p = rgbaPixels)
        {
            gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba,
                (uint)width,
                (uint)height,
                0,
                Silk.NET.OpenGL.PixelFormat.Rgba,
                PixelType.UnsignedByte,
                p);
        }

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

        var wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)wrap);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)wrap);

        gl.GenerateMipmap(TextureTarget.Texture2D);
        gl.BindTexture(TextureTarget.Texture2D, 0);

        return texId;
    }

    // =========================================================================
    // PROCEDURAL HIGH-RESOLUTION BLAM SHADER PALETTES (256x256)
    // =========================================================================

    public static uint CreateCheckerboardTexture(GL gl, int size, int checkSize)
    {
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int check = ((x / checkSize) + (y / checkSize)) % 2;
                bool border = (x % checkSize == 0) || (y % checkSize == 0);
                byte c = border ? (byte)70 : (check == 0 ? (byte)185 : (byte)140);
                int idx = (y * size + x) * 4;
                pixels[idx + 0] = c;
                pixels[idx + 1] = c;
                pixels[idx + 2] = c;
                pixels[idx + 3] = 255;
            }
        }
        return CreateGlTexture(gl, size, size, pixels);
    }

    public static uint CreateGrassTexture(GL gl, int size)
    {
        var pixels = new byte[size * size * 4];
        var rnd = new Random(117);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float noise = (float)rnd.NextDouble();
                float blade = MathF.Sin(x * 0.4f + y * 0.2f) * 0.15f;
                byte r = (byte)Math.Clamp(55 + (int)(noise * 30 + blade * 25), 0, 255);
                byte g = (byte)Math.Clamp(115 + (int)(noise * 45 + blade * 40), 0, 255);
                byte b = (byte)Math.Clamp(40 + (int)(noise * 25 + blade * 20), 0, 255);
                int idx = (y * size + x) * 4;
                pixels[idx + 0] = r;
                pixels[idx + 1] = g;
                pixels[idx + 2] = b;
                pixels[idx + 3] = 255;
            }
        }
        return CreateGlTexture(gl, size, size, pixels);
    }

    public static uint CreateRockTexture(GL gl, int size)
    {
        var pixels = new byte[size * size * 4];
        var rnd = new Random(343);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float n1 = (float)rnd.NextDouble();
                float striation = MathF.Cos((x + y * 0.5f) * 0.15f) * 0.2f;
                byte val = (byte)Math.Clamp(85 + (int)(n1 * 40 + striation * 35), 0, 255);
                int idx = (y * size + x) * 4;
                pixels[idx + 0] = (byte)(val - 5);
                pixels[idx + 1] = (byte)(val - 2);
                pixels[idx + 2] = val;
                pixels[idx + 3] = 255;
            }
        }
        return CreateGlTexture(gl, size, size, pixels);
    }

    public static uint CreateSnowTexture(GL gl, int size)
    {
        var pixels = new byte[size * size * 4];
        var rnd = new Random(7);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float sparkle = rnd.NextDouble() > 0.96 ? 25f : 0f;
                float drift = MathF.Sin(x * 0.05f) * 8f;
                byte b = (byte)Math.Clamp(235 + (int)drift + (int)sparkle, 200, 255);
                byte g = (byte)Math.Clamp(228 + (int)drift, 190, 250);
                byte r = (byte)Math.Clamp(222 + (int)drift, 185, 245);
                int idx = (y * size + x) * 4;
                pixels[idx + 0] = r;
                pixels[idx + 1] = g;
                pixels[idx + 2] = b;
                pixels[idx + 3] = 255;
            }
        }
        return CreateGlTexture(gl, size, size, pixels);
    }

    public static uint CreateMudTexture(GL gl, int size)
    {
        var pixels = new byte[size * size * 4];
        var rnd = new Random(42);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float n = (float)rnd.NextDouble();
                byte r = (byte)Math.Clamp(75 + (int)(n * 35), 0, 255);
                byte g = (byte)Math.Clamp(55 + (int)(n * 25), 0, 255);
                byte b = (byte)Math.Clamp(38 + (int)(n * 18), 0, 255);
                int idx = (y * size + x) * 4;
                pixels[idx + 0] = r;
                pixels[idx + 1] = g;
                pixels[idx + 2] = b;
                pixels[idx + 3] = 255;
            }
        }
        return CreateGlTexture(gl, size, size, pixels);
    }

    public static uint CreateMetalTexture(GL gl, int size)
    {
        var pixels = new byte[size * size * 4];
        var rnd = new Random(2552);
        int panel = size / 2;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int px = x % panel;
                int py = y % panel;
                bool seam = px == 0 || py == 0 || px == panel - 1 || py == panel - 1;
                bool rivet = (px == 6 || px == panel - 7) && (py == 6 || py == panel - 7);
                float noise = (float)rnd.NextDouble() * 12f;

                byte baseVal = seam ? (byte)50 : (rivet ? (byte)210 : (byte)(130 + (int)noise));
                int idx = (y * size + x) * 4;
                pixels[idx + 0] = (byte)(baseVal - 6);
                pixels[idx + 1] = (byte)(baseVal - 2);
                pixels[idx + 2] = (byte)(baseVal + 8);
                pixels[idx + 3] = 255;
            }
        }
        return CreateGlTexture(gl, size, size, pixels);
    }

    public static uint CreateCovenantTexture(GL gl, int size)
    {
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float hex = MathF.Sin(x * 0.15f) * MathF.Cos(y * 0.15f);
                byte r = (byte)Math.Clamp(105 + (int)(hex * 45), 0, 255);
                byte g = (byte)Math.Clamp(50 + (int)(hex * 25), 0, 255);
                byte b = (byte)Math.Clamp(165 + (int)(hex * 55), 0, 255);
                int idx = (y * size + x) * 4;
                pixels[idx + 0] = r;
                pixels[idx + 1] = g;
                pixels[idx + 2] = b;
                pixels[idx + 3] = 255;
            }
        }
        return CreateGlTexture(gl, size, size, pixels);
    }

    public static uint CreateWoodTexture(GL gl, int size)
    {
        var pixels = new byte[size * size * 4];
        var rnd = new Random(101);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float grain = MathF.Sin(x * 0.08f + (float)rnd.NextDouble() * 0.5f) * 20f;
                int plank = (y / (size / 4));
                bool plankSeam = (y % (size / 4) == 0);

                byte r = plankSeam ? (byte)50 : (byte)Math.Clamp(130 + (int)grain, 0, 255);
                byte g = plankSeam ? (byte)35 : (byte)Math.Clamp(85 + (int)(grain * 0.7f), 0, 255);
                byte b = plankSeam ? (byte)20 : (byte)Math.Clamp(50 + (int)(grain * 0.4f), 0, 255);

                int idx = (y * size + x) * 4;
                pixels[idx + 0] = r;
                pixels[idx + 1] = g;
                pixels[idx + 2] = b;
                pixels[idx + 3] = 255;
            }
        }
        return CreateGlTexture(gl, size, size, pixels);
    }

    public static uint CreateFoliageTexture(GL gl, int size)
    {
        var pixels = new byte[size * size * 4];
        var rnd = new Random(888);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float leaf = MathF.Sin(x * 0.2f) * MathF.Sin(y * 0.2f);
                float vein = MathF.Abs(MathF.Cos((x + y) * 0.15f)) > 0.85f ? 35f : 0f;
                byte r = (byte)Math.Clamp(40 + (int)(leaf * 30 + vein * 0.5f), 0, 255);
                byte g = (byte)Math.Clamp(125 + (int)(leaf * 45 + vein), 0, 255);
                byte b = (byte)Math.Clamp(35 + (int)(leaf * 20), 0, 255);
                int idx = (y * size + x) * 4;
                pixels[idx + 0] = r;
                pixels[idx + 1] = g;
                pixels[idx + 2] = b;
                pixels[idx + 3] = 255;
            }
        }
        return CreateGlTexture(gl, size, size, pixels);
    }

    public static uint CreateConcreteTexture(GL gl, int size)
    {
        var pixels = new byte[size * size * 4];
        var rnd = new Random(501);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float speckle = (float)rnd.NextDouble() * 30f;
                byte val = (byte)Math.Clamp(140 + (int)speckle, 0, 255);
                int idx = (y * size + x) * 4;
                pixels[idx + 0] = (byte)(val - 2);
                pixels[idx + 1] = val;
                pixels[idx + 2] = (byte)(val + 4);
                pixels[idx + 3] = 255;
            }
        }
        return CreateGlTexture(gl, size, size, pixels);
    }

    public static uint CreateSandTexture(GL gl, int size)
    {
        var pixels = new byte[size * size * 4];
        var rnd = new Random(312);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float ripple = MathF.Sin(x * 0.1f + y * 0.05f) * 15f;
                float grain = (float)rnd.NextDouble() * 15f;
                byte r = (byte)Math.Clamp(195 + (int)ripple + (int)grain, 0, 255);
                byte g = (byte)Math.Clamp(175 + (int)(ripple * 0.8f) + (int)grain, 0, 255);
                byte b = (byte)Math.Clamp(130 + (int)(ripple * 0.6f), 0, 255);
                int idx = (y * size + x) * 4;
                pixels[idx + 0] = r;
                pixels[idx + 1] = g;
                pixels[idx + 2] = b;
                pixels[idx + 3] = 255;
            }
        }
        return CreateGlTexture(gl, size, size, pixels);
    }

    public static uint CreateWaterTexture(GL gl, int size)
    {
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float wave = (MathF.Sin(x * 0.12f) + MathF.Cos(y * 0.12f)) * 0.5f;
                byte r = (byte)Math.Clamp(40 + (int)(wave * 30), 0, 255);
                byte g = (byte)Math.Clamp(130 + (int)(wave * 40), 0, 255);
                byte b = (byte)Math.Clamp(210 + (int)(wave * 35), 0, 255);
                int idx = (y * size + x) * 4;
                pixels[idx + 0] = r;
                pixels[idx + 1] = g;
                pixels[idx + 2] = b;
                pixels[idx + 3] = 180; // Translucent
            }
        }
        return CreateGlTexture(gl, size, size, pixels);
    }

    public void Dispose()
    {
        _cache.Clear();
        _initialized = false;
    }
}
