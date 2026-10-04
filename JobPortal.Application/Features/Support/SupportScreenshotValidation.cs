using System.Buffers.Binary;
using JobPortal.Application.Common.Exceptions;

namespace JobPortal.Application.Features.Support;

// Follows the existing profile-photo signature/structure validation without trusting upload metadata.
public static class SupportScreenshotValidation
{
    public const int MaximumBytes = 5 * 1024 * 1024;

    public static async Task<(byte[] Content, string Extension)> ReadAsync(
        SupportScreenshotUpload upload, CancellationToken cancellationToken = default)
    {
        if (upload.Length is <= 0 or > MaximumBytes)
            throw new BadRequestException("Screenshot must be between 1 byte and 5 MB.", "invalid_screenshot");
        var extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp")) throw InvalidImage();
        using var buffer = new MemoryStream();
        var block = new byte[81920];
        int count;
        while ((count = await upload.Content.ReadAsync(block, cancellationToken)) > 0)
        {
            if (buffer.Length + count > MaximumBytes)
                throw new BadRequestException("Screenshot must not exceed 5 MB.", "invalid_screenshot");
            buffer.Write(block, 0, count);
        }
        var bytes = buffer.ToArray();
        var detected = Detect(bytes);
        if (bytes.Length != upload.Length || detected is null ||
            !string.Equals(upload.ContentType, detected, StringComparison.OrdinalIgnoreCase) ||
            detected != ContentType(extension)) throw InvalidImage();
        return (bytes, detected == "image/jpeg" ? ".jpg" : extension);
    }

    public static string ContentType(string extension) => extension switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => throw InvalidImage()
    };

    private static BadRequestException InvalidImage() =>
        new("Screenshot must be a valid JPG, PNG or WebP image matching its file type.", "invalid_screenshot");

    private static string? Detect(ReadOnlySpan<byte> bytes)
    {
        if (IsPng(bytes)) return "image/png";
        if (IsJpeg(bytes)) return "image/jpeg";
        if (IsWebP(bytes)) return "image/webp";
        return null;
    }

    private static bool IsPng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 57 || bytes[0] != 137 || !bytes.Slice(1, 7).SequenceEqual("PNG\r\n\x1a\n"u8)) return false;
        var offset = 8;
        var imageData = false;
        while (offset + 12 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            if (length > MaximumBytes || offset + 12L + length > bytes.Length) return false;
            var type = bytes.Slice(offset + 4, 4);
            if (offset == 8)
            {
                if (!type.SequenceEqual("IHDR"u8) || length != 13) return false;
                var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset + 8, 4));
                var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset + 12, 4));
                if (width is 0 or > 16384 || height is 0 or > 16384) return false;
            }
            if (type.SequenceEqual("IDAT"u8) && length > 0) imageData = true;
            if (type.SequenceEqual("IEND"u8)) return imageData && length == 0 && offset + 12 == bytes.Length;
            offset += (int)length + 12;
        }
        return false;
    }

    private static bool IsJpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[^2] != 0xff || bytes[^1] != 0xd9) return false;
        var offset = 2;
        var hasFrame = false;
        while (offset + 4 <= bytes.Length - 2)
        {
            if (bytes[offset++] != 0xff) return false;
            while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
            if (offset >= bytes.Length) return false;
            var marker = bytes[offset++];
            if (marker is 0x01 or >= 0xd0 and <= 0xd7) continue;
            if (offset + 2 > bytes.Length) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (length < 2 || offset + length > bytes.Length - 2) return false;
            if (marker == 0xda) return hasFrame && length >= 6;
            if (marker is >= 0xc0 and <= 0xc3 or >= 0xc5 and <= 0xc7 or >= 0xc9 and <= 0xcb or >= 0xcd and <= 0xcf)
            {
                if (length < 8) return false;
                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2));
                if (width is 0 or > 16384 || height is 0 or > 16384) return false;
                hasFrame = true;
            }
            offset += length;
        }
        return false;
    }

    private static bool IsWebP(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 25 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes.Slice(8, 4).SequenceEqual("WEBP"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)) + 8L != bytes.Length) return false;
        var offset = 12;
        var imageData = false;
        while (offset + 8 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4));
            if (length > MaximumBytes || offset + 8L + length + (length & 1) > bytes.Length) return false;
            var type = bytes.Slice(offset, 4);
            var data = bytes.Slice(offset + 8, (int)length);
            if (type.SequenceEqual("VP8 "u8))
                imageData |= data.Length >= 10 && data[3] == 0x9d && data[4] == 1 && data[5] == 0x2a;
            if (type.SequenceEqual("VP8L"u8)) imageData |= data.Length >= 5 && data[0] == 0x2f;
            offset += 8 + (int)length + (int)(length & 1);
        }
        return imageData && offset == bytes.Length;
    }
}
