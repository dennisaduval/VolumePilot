using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace PilotCapture.Infrastructure.Persistence;

/// <summary>Reads only the original exposure time from the JPEG EXIF header.</summary>
internal static class JpegCaptureTime
{
    public static long? Read(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.ReadByte() != 0xff || file.ReadByte() != 0xd8) return null;
            // Limit header work. Stop at compressed image data.
            while (file.Position < Math.Min(file.Length, 1024 * 1024) && file.ReadByte() == 0xff)
            {
                int marker;
                do { marker = file.ReadByte(); } while (marker == 0xff);
                if (marker is < 0 or 0xda or 0xd9) break;
                var high = file.ReadByte();
                var low = file.ReadByte();
                if (high < 0 || low < 0) break;
                var length = (high << 8) + low - 2;
                if (length < 0 || length > file.Length - file.Position) break;
                if (marker != 0xe1) { file.Seek(length, SeekOrigin.Current); continue; }
                var bytes = new byte[length];
                file.ReadExactly(bytes);
                if (bytes.AsSpan().StartsWith("Exif\0\0"u8) && ReadExif(bytes[6..]) is { } ticks) return ticks;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    private static long? ReadExif(byte[] tiff)
    {
        if (tiff.Length < 8) return null;
        var little = tiff.AsSpan().StartsWith("II"u8);
        if (!little && !tiff.AsSpan().StartsWith("MM"u8)) return null;
        ushort U16(int i) => little ? BinaryPrimitives.ReadUInt16LittleEndian(tiff.AsSpan(i)) : BinaryPrimitives.ReadUInt16BigEndian(tiff.AsSpan(i));
        uint U32(int i) => little ? BinaryPrimitives.ReadUInt32LittleEndian(tiff.AsSpan(i)) : BinaryPrimitives.ReadUInt32BigEndian(tiff.AsSpan(i));
        int Find(uint ifd, ushort tag)
        {
            if (ifd > tiff.Length - 2) return -1;
            var start = (int)ifd + 2;
            var count = U16((int)ifd);
            if (count > (tiff.Length - start) / 12) return -1;
            for (var i = start; i < start + count * 12; i += 12)
                if (U16(i) == tag) return i;
            return -1;
        }
        if (U16(2) != 42) return null;
        var pointer = Find(U32(4), 0x8769);
        if (pointer < 0 || U16(pointer + 2) != 4 || U32(pointer + 4) != 1) return null;
        var original = Find(U32(pointer + 8), 0x9003);
        if (original < 0 || U16(original + 2) != 2 || U32(original + 4) != 20) return null;
        var offset = U32(original + 8);
        if (offset > tiff.Length - 20 || tiff[(int)offset + 19] != 0) return null;
        var value = Encoding.ASCII.GetString(tiff, (int)offset, 19);
        return DateTime.TryParseExact(value, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var time) ? time.Ticks : null;
    }
}
