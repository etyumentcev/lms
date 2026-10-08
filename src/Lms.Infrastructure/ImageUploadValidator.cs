using System.Buffers.Binary;
using System.IO.Compression;
using Lms.Application;

namespace Lms.Infrastructure;

// Structural format checks, not image forensics. No original bytes are transformed.
public static class ImageUploadValidator
{
    public static void Validate(byte[] bytes, string mediaType)
    {
        if (mediaType is not ("image/jpeg" or "image/png")) throw new UseCaseException("UnsupportedAttachmentType");
        if (!(mediaType == "image/png" ? Png(bytes) : Jpeg(bytes))) throw new UseCaseException("MalformedAttachment");
    }

    private static bool Dimensions(uint width, uint height) => width > 0 && height > 0 && (ulong)width * height <= 60_000_000;
    private static bool Png(ReadOnlySpan<byte> b)
    {
        if (b.Length < 57 || !b[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
        var position = 8; var header = false; var data = false;
        uint width = 0, height = 0; var bitsPerPixel = 0; var interlaced = false;
        using var compressed = new MemoryStream();
        while (position <= b.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(b[position..]);
            if (length > b.Length - position - 12) return false;
            var count = (int)length;
            var type = b.Slice(position + 4, 4);
            var chunk = b.Slice(position + 8, count);
            uint crc = 0xffffffff;
            foreach (var value in b.Slice(position + 4, count + 4))
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0);
            }
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(b[(position + 8 + count)..])) return false;
            if (!header)
            {
                if (!type.SequenceEqual("IHDR"u8) || count != 13 ||
                    !Dimensions(BinaryPrimitives.ReadUInt32BigEndian(chunk), BinaryPrimitives.ReadUInt32BigEndian(chunk[4..]))) return false;
                width = BinaryPrimitives.ReadUInt32BigEndian(chunk); height = BinaryPrimitives.ReadUInt32BigEndian(chunk[4..]);
                var depth = chunk[8]; var color = chunk[9];
                var allowedDepth = color switch { 0 => depth is 1 or 2 or 4 or 8 or 16, 3 => depth is 1 or 2 or 4 or 8,
                    2 or 4 or 6 => depth is 8 or 16, _ => false };
                if (!allowedDepth || chunk[10] != 0 || chunk[11] != 0 || chunk[12] > 1) return false;
                bitsPerPixel = depth * (color switch { 2 => 3, 4 => 2, 6 => 4, _ => 1 });
                interlaced = chunk[12] == 1;
                header = true;
            }
            else if (type.SequenceEqual("IHDR"u8)) return false;
            if (type.SequenceEqual("IDAT"u8) && count > 0) { data = true; compressed.Write(chunk); }
            position += count + 12;
            if (type.SequenceEqual("IEND"u8)) return count == 0 && data && position == b.Length && PngPixels(compressed, width, height, bitsPerPixel, interlaced);
        }
        return false;
    }

    private static bool PngPixels(MemoryStream compressed, uint width, uint height, int bits, bool interlaced)
    {
        compressed.Position = 0;
        try
        {
            using var decoded = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            var buffer = new byte[8192];
            (int X, int Y, int Dx, int Dy)[] passes = interlaced
                ? [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)]
                : [(0, 0, 1, 1)];
            foreach (var pass in passes)
            {
                var columns = Math.Max(0, ((long)width - pass.X + pass.Dx - 1) / pass.Dx);
                var rows = Math.Max(0, ((long)height - pass.Y + pass.Dy - 1) / pass.Dy);
                if (columns == 0) continue;
                for (var row = 0L; row < rows; row++)
                {
                    if (decoded.ReadByte() is < 0 or > 4) return false;
                    var remaining = (columns * bits + 7) / 8;
                    while (remaining > 0)
                    {
                        var read = decoded.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
                        if (read == 0) return false;
                        remaining -= read;
                    }
                }
            }
            return decoded.ReadByte() == -1;
        }
        catch (InvalidDataException) { return false; }
    }

    private static bool Jpeg(ReadOnlySpan<byte> b)
    {
        if (b.Length < 12 || b[0] != 0xff || b[1] != 0xd8) return false;
        var position = 2; var dimensions = false; var scan = false;
        while (position < b.Length)
        {
            if (b[position++] != 0xff) return false;
            while (position < b.Length && b[position] == 0xff) position++;
            if (position >= b.Length) return false;
            var marker = b[position++];
            if (marker == 0xd9) return dimensions && scan && position == b.Length;
            if (marker is 0 or 0xd8 || position > b.Length - 2) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(b[position..]);
            if (length < 2 || length > b.Length - position) return false;
            if (marker is 0xc0 or 0xc1 or 0xc2)
            {
                if (length < 11 || b[position + 2] != 8 || b[position + 7] is < 1 or > 4 || length != 8 + 3 * b[position + 7] || !Dimensions(BinaryPrimitives.ReadUInt16BigEndian(b[(position + 5)..]),
                    BinaryPrimitives.ReadUInt16BigEndian(b[(position + 3)..]))) return false;
                dimensions = true;
            }
            if (marker == 0xda && (!dimensions || length < 8 || b[position + 2] is < 1 or > 4 || length != 6 + 2 * b[position + 2])) return false;
            position += length;
            if (marker != 0xda) continue;
            var scanStart = position;
            scan = true;
            while (position < b.Length)
            {
                if (b[position] != 0xff) { position++; continue; }
                if (position + 1 >= b.Length) return false;
                if (b[position + 1] == 0 || b[position + 1] is >= 0xd0 and <= 0xd7) { position += 2; continue; }
                break;
            }
            if (position == scanStart) return false;
        }
        return false;
    }
}
