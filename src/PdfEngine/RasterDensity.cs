using System.Buffers.Binary;
using System.IO;

namespace Almarfeld.PdfEngine;
internal static class RasterDensity
{
    // Change only encoder density metadata, never image pixels or compression quality.
    public static void Set(Stream stream, RasterFormat format, int dpi)
    {
        if (format == RasterFormat.Jpeg)
        {
            stream.Position = 2;
            while (stream.Position + 4 < stream.Length)
            {
                if (stream.ReadByte() != 255) break; var marker = stream.ReadByte();
                if (marker == 218) break;
                var length = stream.ReadByte() * 256 + stream.ReadByte(); var start = stream.Position;
                if (length < 2 || start + length - 2 > stream.Length) break;
                if (marker == 224)
                {
                    var signature = new byte[5]; stream.ReadExactly(signature);
                    if (signature.SequenceEqual(new byte[] { 74, 70, 73, 70, 0 }))
                    {
                        stream.Position = start + 7; stream.WriteByte(1);
                        stream.WriteByte((byte)(dpi >> 8)); stream.WriteByte((byte)dpi); stream.WriteByte((byte)(dpi >> 8)); stream.WriteByte((byte)dpi); return;
                    }
                }
                stream.Position = start + length - 2;
            }
        }
        else
        {
            stream.Position = 8; var header = new byte[8];
            while (stream.Position + 12 <= stream.Length)
            {
                stream.ReadExactly(header); var length = BinaryPrimitives.ReadUInt32BigEndian(header); var start = stream.Position;
                if (start + length + 4 > stream.Length) break;
                if (length == 9 && header.AsSpan(4).SequenceEqual("pHYs"u8))
                {
                    var chunk = new byte[13]; "pHYs"u8.CopyTo(chunk); var units = (uint)Math.Round(dpi / .0254);
                    BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(4), units); BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8), units); chunk[12] = 1;
                    stream.Write(chunk, 4, 9); uint crc = uint.MaxValue;
                    foreach (var b in chunk) { crc ^= b; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
                    var checksum = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(checksum, ~crc); stream.Write(checksum); return;
                }
                stream.Position = start + length + 4;
            }
        }
        throw new PdfOperationException("Raster", "The image encoder did not provide supported resolution metadata; no output was saved.");
    }
}
