using System;
using System.IO;
using System.Text;

namespace ForgeWorks.Blam.Tags;

public sealed record TagBlockPointer(int Count, uint DataOffset);

public sealed record TagReference(string GroupMagic, string TagPath);

public sealed class BlamTagReader : IDisposable
{
    private readonly BinaryReader _reader;
    private readonly Stream _stream;

    public string GroupMagic { get; private set; } = string.Empty;
    public uint EngineChecksum { get; private set; }
    public uint HeaderSize { get; private set; }
    public uint RootOffset { get; private set; }

    public BlamTagReader(Stream stream)
    {
        _stream = stream;
        _reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        ReadHeader();
    }

    public static BlamTagReader Open(string filePath)
    {
        var stream = File.OpenRead(filePath);
        return new BlamTagReader(stream);
    }

    private void ReadHeader()
    {
        if (_stream.Length < 0x40)
            return;

        _stream.Seek(0x24, SeekOrigin.Begin);
        
        // 4-character reversed tag group (e.g. 'rncs' -> 'scnr')
        byte[] magicBytes = _reader.ReadBytes(4);
        Array.Reverse(magicBytes);
        GroupMagic = Encoding.ASCII.GetString(magicBytes);

        EngineChecksum = _reader.ReadUInt32();

        _stream.Seek(0x30, SeekOrigin.Begin);
        HeaderSize = _reader.ReadUInt32();
        RootOffset = 0x40; // Base position for post-header root struct
    }

    public TagBlockPointer ReadReflexive(long streamPosition)
    {
        _stream.Seek(streamPosition, SeekOrigin.Begin);
        int count = _reader.ReadInt32();
        uint offset = _reader.ReadUInt32();
        return new TagBlockPointer(count, offset);
    }

    public TagReference ReadTagReference(long streamPosition)
    {
        _stream.Seek(streamPosition, SeekOrigin.Begin);
        byte[] groupBytes = _reader.ReadBytes(4);
        Array.Reverse(groupBytes);
        string group = Encoding.ASCII.GetString(groupBytes);

        uint pathOffset = _reader.ReadUInt32();
        int pathLength = _reader.ReadInt32();

        if (pathOffset == 0 || pathLength <= 0 || pathOffset >= _stream.Length)
            return new TagReference(group, string.Empty);

        long prevPos = _stream.Position;
        _stream.Seek(pathOffset, SeekOrigin.Begin);
        int readLen = Math.Min(pathLength - 1, (int)(_stream.Length - pathOffset));
        byte[] pathBytes = _reader.ReadBytes(Math.Max(0, readLen));
        _stream.Seek(prevPos, SeekOrigin.Begin);

        return new TagReference(group, Encoding.UTF8.GetString(pathBytes));
    }

    public void Dispose()
    {
        _reader.Dispose();
        _stream.Dispose();
    }
}
