namespace PilotCapture.Infrastructure.Files;

internal static class JpegMetadataReader
{
    private static readonly HashSet<byte> StartOfFrameMarkers =
    [
        0xC0, 0xC1, 0xC2, 0xC3,
        0xC5, 0xC6, 0xC7,
        0xC9, 0xCA, 0xCB,
        0xCD, 0xCE, 0xCF
    ];

    public static (int Width, int Height)? ReadPixelDimensions(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("JPEG metadata requires a readable, seekable stream.", nameof(stream));

        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            if (stream.ReadByte() != 0xFF || stream.ReadByte() != 0xD8)
                return null;

            while (stream.Position < stream.Length)
            {
                var prefix = stream.ReadByte();
                if (prefix < 0)
                    return null;
                if (prefix != 0xFF)
                    continue;

                int marker;
                do
                {
                    marker = stream.ReadByte();
                } while (marker == 0xFF);

                if (marker < 0 || marker == 0x00 || marker == 0xD9 || marker == 0xDA)
                    return null;
                if (marker == 0xD8 || marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
                    continue;

                var lengthHigh = stream.ReadByte();
                var lengthLow = stream.ReadByte();
                if (lengthHigh < 0 || lengthLow < 0)
                    return null;
                var segmentLength = (lengthHigh << 8) | lengthLow;
                if (segmentLength < 2 || segmentLength - 2 > stream.Length - stream.Position)
                    return null;

                if (StartOfFrameMarkers.Contains((byte)marker))
                {
                    if (segmentLength < 7)
                        return null;
                    _ = stream.ReadByte(); // Sample precision
                    var height = ReadUInt16BigEndian(stream);
                    var width = ReadUInt16BigEndian(stream);
                    return width > 0 && height > 0 ? (width, height) : null;
                }

                stream.Seek(segmentLength - 2, SeekOrigin.Current);
            }

            return null;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static int ReadUInt16BigEndian(Stream stream)
    {
        var high = stream.ReadByte();
        var low = stream.ReadByte();
        return high < 0 || low < 0 ? 0 : (high << 8) | low;
    }
}
