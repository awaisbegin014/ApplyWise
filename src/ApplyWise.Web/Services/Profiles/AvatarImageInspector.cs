using System.Buffers.Binary;

namespace ApplyWise.Web.Services.Profiles;

public sealed record AvatarImageInfo(string ContentType, int Width, int Height);

public static class AvatarImageInspector
{
    private const int MaximumDimension = 4_096;
    private const long MaximumPixels = 8_000_000;

    public static AvatarImageInfo? Inspect(ReadOnlySpan<byte> bytes)
    {
        var candidate = InspectPng(bytes) ?? InspectJpeg(bytes) ?? InspectWebP(bytes);
        if (candidate is null
            || candidate.Width <= 0
            || candidate.Height <= 0
            || candidate.Width > MaximumDimension
            || candidate.Height > MaximumDimension
            || (long)candidate.Width * candidate.Height > MaximumPixels)
        {
            return null;
        }

        return candidate;
    }

    private static AvatarImageInfo? InspectPng(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.Length < 24
            || !bytes.StartsWith(signature)
            || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
        return new AvatarImageInfo("image/png", width, height);
    }

    private static AvatarImageInfo? InspectJpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8) return null;
        var index = 2;
        while (index + 4 <= bytes.Length)
        {
            while (index < bytes.Length && bytes[index] == 0xFF) index++;
            if (index >= bytes.Length) break;
            var marker = bytes[index++];
            if (marker is 0x01 or >= 0xD0 and <= 0xD9) continue;
            if (index + 2 > bytes.Length) return null;
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index, 2));
            if (segmentLength < 2 || index + segmentLength > bytes.Length) return null;
            if (IsStartOfFrame(marker) && segmentLength >= 7)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index + 5, 2));
                return new AvatarImageInfo("image/jpeg", width, height);
            }

            index += segmentLength;
        }

        return null;
    }

    private static AvatarImageInfo? InspectWebP(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 20
            || !bytes[..4].SequenceEqual("RIFF"u8)
            || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return null;
        }

        var index = 12;
        while (index + 8 <= bytes.Length)
        {
            var chunkType = bytes.Slice(index, 4);
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(index + 4, 4));
            var dataIndex = index + 8;
            if (chunkSize > int.MaxValue || dataIndex + (long)chunkSize > bytes.Length) return null;
            var data = bytes.Slice(dataIndex, (int)chunkSize);

            if (chunkType.SequenceEqual("VP8X"u8) && data.Length >= 10)
            {
                return new AvatarImageInfo(
                    "image/webp",
                    1 + ReadUInt24LittleEndian(data.Slice(4, 3)),
                    1 + ReadUInt24LittleEndian(data.Slice(7, 3)));
            }
            if (chunkType.SequenceEqual("VP8L"u8) && data.Length >= 5 && data[0] == 0x2F)
            {
                var width = 1 + data[1] + ((data[2] & 0x3F) << 8);
                var height = 1 + ((data[2] & 0xC0) >> 6) + (data[3] << 2) + ((data[4] & 0x0F) << 10);
                return new AvatarImageInfo("image/webp", width, height);
            }
            if (chunkType.SequenceEqual("VP8 "u8)
                && data.Length >= 10
                && data.Slice(3, 3).SequenceEqual(new byte[] { 0x9D, 0x01, 0x2A }))
            {
                var width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6, 2)) & 0x3FFF;
                var height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2)) & 0x3FFF;
                return new AvatarImageInfo("image/webp", width, height);
            }

            index = dataIndex + (int)chunkSize + ((int)chunkSize & 1);
        }

        return null;
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xC3
            or >= 0xC5 and <= 0xC7
            or >= 0xC9 and <= 0xCB
            or >= 0xCD and <= 0xCF;

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes) =>
        bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
}
