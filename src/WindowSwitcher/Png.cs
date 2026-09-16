using System.IO.Compression;

namespace WindowSwitcher;

/// <summary>Minimal PNG writer for the diagnostic dump (RGBA, filter none).</summary>
internal static class Png
{
    static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Writes premultiplied top-down BGRA as a straight-alpha RGBA PNG.</summary>
    public static void Write(string path, CapturedImage image)
    {
        int w = image.Width, h = image.Height;
        byte[] px = image.Pixels;
        var raw = new byte[(w * 4 + 1) * h];
        int o = 0;
        for (int y = 0; y < h; y++)
        {
            raw[o++] = 0;
            for (int x = 0; x < w; x++)
            {
                int s = (y * w + x) * 4;
                int a = px[s + 3];
                int b = px[s], g = px[s + 1], r = px[s + 2];
                if (a != 0 && a != 255)
                {
                    r = Math.Min(255, r * 255 / a);
                    g = Math.Min(255, g * 255 / a);
                    b = Math.Min(255, b * 255 / a);
                }
                raw[o++] = (byte)r;
                raw[o++] = (byte)g;
                raw[o++] = (byte)b;
                raw[o++] = (byte)a;
            }
        }

        var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            z.Write(raw);

        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)w);
        WriteBigEndian(header, 4, (uint)h);
        header[8] = 8;  // bit depth
        header[9] = 6;  // RGBA
        WriteChunk(file, "IHDR"u8, header);
        WriteChunk(file, "IDAT"u8, compressed.ToArray());
        WriteChunk(file, "IEND"u8, []);
    }

    static void WriteChunk(Stream s, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> buf = stackalloc byte[4];
        WriteBigEndian(buf, 0, (uint)data.Length);
        s.Write(buf);
        s.Write(type);
        s.Write(data);
        uint crc = Crc(0xFFFFFFFF, type);
        crc = Crc(crc, data) ^ 0xFFFFFFFF;
        WriteBigEndian(buf, 0, crc);
        s.Write(buf);
    }

    static void WriteBigEndian(Span<byte> b, int at, uint v)
    {
        b[at] = (byte)(v >> 24);
        b[at + 1] = (byte)(v >> 16);
        b[at + 2] = (byte)(v >> 8);
        b[at + 3] = (byte)v;
    }

    static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte d in data)
            crc = CrcTable[(crc ^ d) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
