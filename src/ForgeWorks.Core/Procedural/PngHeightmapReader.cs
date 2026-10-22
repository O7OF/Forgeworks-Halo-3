using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace ForgeWorks.Core.Procedural;

/// <summary>
/// Lightweight, zero-dependency PNG heightmap reader.
/// Decodes 8-bit Grayscale, RGB, and RGBA PNG noise textures from the noise/ folder.
/// </summary>
public static class PngHeightmapReader
{
    private static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static float[,] LoadHeightmapFromPng(string filePath, int targetResX, int targetResY)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Noise texture not found: {filePath}");
        }

        using var stream = File.OpenRead(filePath);
        return LoadHeightmapFromStream(stream, targetResX, targetResY);
    }

    public static float[,] LoadHeightmapFromStream(Stream stream, int targetResX, int targetResY)
    {
        using var reader = new BinaryReader(stream);

        // Check PNG signature
        byte[] sig = reader.ReadBytes(8);
        if (sig.Length < 8)
            throw new InvalidDataException("Invalid PNG signature");

        for (int i = 0; i < 8; i++)
        {
            if (sig[i] != PngHeader[i])
                throw new InvalidDataException("Invalid PNG header");
        }

        int width = 0;
        int height = 0;
        int bitDepth = 0;
        int colorType = 0;
        using var idatStream = new MemoryStream();

        while (stream.Position < stream.Length)
        {
            uint chunkLength = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
            string chunkType = new string(reader.ReadChars(4));
            byte[] chunkData = reader.ReadBytes((int)chunkLength);
            reader.ReadBytes(4); // CRC

            if (chunkType == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(chunkData.AsSpan(0, 4));
                height = BinaryPrimitives.ReadInt32BigEndian(chunkData.AsSpan(4, 4));
                bitDepth = chunkData[8];
                colorType = chunkData[9];
            }
            else if (chunkType == "IDAT")
            {
                idatStream.Write(chunkData, 0, chunkData.Length);
            }
            else if (chunkType == "IEND")
            {
                break;
            }
        }

        if (width <= 0 || height <= 0 || bitDepth != 8)
            throw new InvalidDataException($"Unsupported PNG dimensions or bit depth: {width}x{height}, bitDepth={bitDepth}");

        int bytesPerPixel = colorType switch
        {
            0 => 1, // Grayscale
            2 => 3, // RGB
            4 => 2, // Grayscale + Alpha
            6 => 4, // RGBA
            _ => throw new InvalidDataException($"Unsupported PNG color type: {colorType}")
        };

        // Decompress IDAT zlib payload
        byte[] idatBytes = idatStream.ToArray();
        using var rawCompressed = new MemoryStream(idatBytes);

        // Skip 2-byte zlib header (CMF/FLG) if present
        if (idatBytes.Length > 2 && (idatBytes[0] == 0x78 || idatBytes[0] == 0x58))
        {
            rawCompressed.Seek(2, SeekOrigin.Begin);
        }

        using var deflate = new DeflateStream(rawCompressed, CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        deflate.CopyTo(decompressed);
        byte[] rawPixels = decompressed.ToArray();

        int stride = width * bytesPerPixel;
        var reconstructed = new byte[height, stride];

        int srcOffset = 0;
        byte[] prevRow = new byte[stride];
        byte[] currRow = new byte[stride];

        for (int y = 0; y < height; y++)
        {
            if (srcOffset >= rawPixels.Length) break;
            byte filterType = rawPixels[srcOffset++];

            for (int x = 0; x < stride; x++)
            {
                byte raw = (srcOffset < rawPixels.Length) ? rawPixels[srcOffset++] : (byte)0;
                byte a = (x >= bytesPerPixel) ? currRow[x - bytesPerPixel] : (byte)0;
                byte b = prevRow[x];
                byte c = (x >= bytesPerPixel) ? prevRow[x - bytesPerPixel] : (byte)0;

                byte val = filterType switch
                {
                    0 => raw,                                     // None
                    1 => (byte)(raw + a),                         // Sub
                    2 => (byte)(raw + b),                         // Up
                    3 => (byte)(raw + (a + b) / 2),               // Average
                    4 => (byte)(raw + PaethPredictor(a, b, c)),   // Paeth
                    _ => raw
                };
                currRow[x] = val;
                reconstructed[y, x] = val;
            }
            Array.Copy(currRow, prevRow, stride);
        }

        // Convert to 2D float height values [0..1]
        var sourceHeights = new float[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pxOffset = x * bytesPerPixel;
                float lum;
                if (bytesPerPixel == 1)
                {
                    lum = reconstructed[y, pxOffset] / 255.0f;
                }
                else
                {
                    float r = reconstructed[y, pxOffset] / 255.0f;
                    float g = reconstructed[y, pxOffset + 1] / 255.0f;
                    float b = reconstructed[y, pxOffset + 2] / 255.0f;
                    lum = r * 0.299f + g * 0.587f + b * 0.114f;
                }
                // (x, y) coordinates with y flipped so top is positive Y
                sourceHeights[x, height - 1 - y] = lum;
            }
        }

        // Resample with bilinear interpolation to targetResX, targetResY
        var result = new float[targetResX, targetResY];
        for (int ty = 0; ty < targetResY; ty++)
        {
            float sy = (ty / (float)Math.Max(1, targetResY - 1)) * (height - 1);
            int y0 = (int)MathF.Floor(sy);
            int y1 = Math.Min(height - 1, y0 + 1);
            float fy = sy - y0;

            for (int tx = 0; tx < targetResX; tx++)
            {
                float sx = (tx / (float)Math.Max(1, targetResX - 1)) * (width - 1);
                int x0 = (int)MathF.Floor(sx);
                int x1 = Math.Min(width - 1, x0 + 1);
                float fx = sx - x0;

                float h00 = sourceHeights[x0, y0];
                float h10 = sourceHeights[x1, y0];
                float h01 = sourceHeights[x0, y1];
                float h11 = sourceHeights[x1, y1];

                float top = h00 * (1.0f - fx) + h10 * fx;
                float bot = h01 * (1.0f - fx) + h11 * fx;
                result[tx, ty] = top * (1.0f - fy) + bot * fy;
            }
        }

        return result;
    }

    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }
}
