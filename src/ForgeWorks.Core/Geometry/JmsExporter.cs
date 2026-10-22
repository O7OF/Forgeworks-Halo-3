using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace ForgeWorks.Core.Geometry;

/// <summary>
/// Jointed Model Skeleton (.JMS) Exporter.
/// Supports versions 8290 (H2/H3 Tool), 8213, and 8200 (HCE).
/// </summary>
public sealed class JmsExporter
{
    public static void ExportToFile(
        string filepath,
        List<string> materials,
        MeshObject mesh,
        int version = 8290,
        string nodeName = "frame root",
        float scale = 1.0f)
    {
        var dir = Path.GetDirectoryName(filepath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var stream = File.Create(filepath);
        ExportToStream(stream, materials, mesh, version, nodeName, scale);
    }

    public static void ExportToStream(
        Stream destination,
        List<string> materials,
        MeshObject mesh,
        int version = 8290,
        string nodeName = "frame root",
        float scale = 1.0f)
    {
        using var writer = new StreamWriter(destination, leaveOpen: true);
        var inv = CultureInfo.InvariantCulture;

        if (materials == null || materials.Count == 0)
        {
            materials = new List<string> { "default" };
        }

        // Header: Version string
        writer.WriteLine(version.ToString());

        // Node checksum & Node count
        writer.WriteLine("35179"); // Structure node checksum
        writer.WriteLine("1");     // 1 root node for static structure

        // Node 0
        writer.WriteLine(nodeName);
        writer.WriteLine("-1 -1 -1"); // First child, next sibling, parent
        writer.WriteLine(string.Format(inv, "{0:F6} {1:F6} {2:F6}", 0.0f, 0.0f, 0.0f));
        writer.WriteLine(string.Format(inv, "{0:F6} {1:F6} {2:F6} {3:F6}", 0.0f, 0.0f, 0.0f, 1.0f));

        // Materials Table
        writer.WriteLine(materials.Count.ToString());
        foreach (var mat in materials)
        {
            writer.WriteLine($"{mat}\t<none>");
        }

        // Markers Table
        writer.WriteLine("0");

        // Vertex Table
        writer.WriteLine(mesh.Vertices.Count.ToString());
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            var v = mesh.Vertices[i];
            var uv = v.UVs.Count > 0 ? v.UVs[0] : Vector2.Zero;

            // Compute tangent and binormal from normal
            Vector3 tangent = Vector3.UnitX;
            if (MathF.Abs(Vector3.Dot(v.Normal, tangent)) > 0.9f)
            {
                tangent = Vector3.UnitY;
            }
            Vector3 binormal = Vector3.Normalize(Vector3.Cross(v.Normal, tangent));
            tangent = Vector3.Normalize(Vector3.Cross(binormal, v.Normal));

            // Node, Pos(X,Y,Z), Normal(I,J,K), Tangent(I,J,K), Binormal(I,J,K), UV(U,V)
            writer.WriteLine(string.Format(inv,
                "0\t{0:F6}\t{1:F6}\t{2:F6}\t{3:F6}\t{4:F6}\t{5:F6}\t{6:F6}\t{7:F6}\t{8:F6}\t{9:F6}\t{10:F6}\t{11:F6}\t{12:F6}\t{13:F6}\t0",
                v.Position.X * scale, v.Position.Y * scale, v.Position.Z * scale,
                v.Normal.X, v.Normal.Y, v.Normal.Z,
                tangent.X, tangent.Y, tangent.Z,
                binormal.X, binormal.Y, binormal.Z,
                uv.X, 1.0f - uv.Y)); // Flip V for Blam convention
        }

        // Triangles Table
        writer.WriteLine(mesh.Triangles.Count.ToString());
        foreach (var tri in mesh.Triangles)
        {
            int matIdx = Math.Clamp(tri.MaterialIndex, 0, materials.Count - 1);
            writer.WriteLine($"{matIdx}\t{tri.V0}\t{tri.V1}\t{tri.V2}");
        }
    }
}
