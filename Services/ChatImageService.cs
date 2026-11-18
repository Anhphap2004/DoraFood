using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace DoraFood.Services;

public sealed class ChatImageService
{
    private const long MaxUploadBytes = 5 * 1024 * 1024;

    private const long MaxPixels = 20_000_000;

    private const int MaxDimension = 12_000;

    private const int OutputDimension = 1600;

    private const long MaxOutputBytes = 10 * 1024 * 1024;

    public async Task<PreparedChatImage> PrepareAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
        {
            throw new ChatException(
                400,
                "File ảnh rỗng. Anh hãy chọn ảnh khác.");
        }

        if (file.Length > MaxUploadBytes)
        {
            throw new ChatException(
                400,
                "Ảnh tối đa 5 MB.");
        }

        // Đọc file và kiểm tra cả dung lượng thực tế.
        using var original = new MemoryStream();

        await using (var source = file.OpenReadStream())
        {
            var buffer = new byte[81920];

            while (true)
            {
                var read = await source.ReadAsync(
                    buffer.AsMemory(),
                    cancellationToken);

                if (read == 0)
                {
                    break;
                }

                if (original.Length + read > MaxUploadBytes)
                {
                    throw new ChatException(
                        400,
                        "Ảnh tối đa 5 MB.");
                }

                await original.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
            }
        }

        if (original.Length == 0)
        {
            throw new ChatException(
                400,
                "Không đọc được dữ liệu ảnh.");
        }

        // Mã nhận diện nội dung file gốc.
        // Sau này dùng để kiểm tra khi gửi lại cùng một tin.
        var sourceHash = Convert.ToHexString(
            SHA256.HashData(
                original.GetBuffer().AsSpan(
                    0,
                    checked((int)original.Length))));

        original.Position = 0;

        try
        {
            var options = new DecoderOptions
            {
                // Ảnh động được lấy khung đầu tiên.
                MaxFrames = 1,

                // Giữ thông tin hướng ảnh để xoay đúng chiều.
                SkipMetadata = false
            };

            // Kiểm tra thông tin trước khi đọc toàn bộ điểm ảnh.
            var info = await Image.IdentifyAsync(
                options,
                original,
                cancellationToken);

            var formatName = info.Metadata
                .DecodedImageFormat?.Name;

            if (formatName != "JPEG"
                && formatName != "PNG"
                && formatName != "WEBP")
            {
                throw new ChatException(
                    400,
                    "Chỉ hỗ trợ ảnh JPG, PNG hoặc WebP.");
            }

            if (info.Width <= 0
                || info.Height <= 0
                || info.Width > MaxDimension
                || info.Height > MaxDimension
                || (long)info.Width * info.Height > MaxPixels)
            {
                throw new ChatException(
                    400,
                    "Kích thước ảnh quá lớn. Anh hãy chọn ảnh nhỏ hơn.");
            }

            original.Position = 0;

            // Đọc và giải mã ảnh thật.
            using var image = await Image.LoadAsync(
                options,
                original,
                cancellationToken);

            image.Mutate(operation => operation.AutoOrient());

            // Thu nhỏ ảnh lớn, giữ nguyên tỷ lệ.
            // Ảnh nhỏ hơn giới hạn không bị phóng to.
            if (image.Width > OutputDimension
                || image.Height > OutputDimension)
            {
                image.Mutate(operation => operation.Resize(
                    new ResizeOptions
                    {
                        Size = new Size(
                            OutputDimension,
                            OutputDimension),

                        Mode = ResizeMode.Max
                    }));
            }

            using var output = new MemoryStream();

            // Lưu lại từ ảnh đã giải mã.
            // Bỏ metadata khỏi file xuất.
            await image.SaveAsync(
                output,
                new PngEncoder
                {
                    SkipMetadata = true
                },
                cancellationToken);

            if (output.Length > MaxOutputBytes)
            {
                throw new ChatException(
                    400,
                    "Ảnh sau xử lý vẫn quá lớn. Anh hãy chọn ảnh nhỏ hơn.");
            }

            // Tên gốc chỉ dùng để hiển thị, không dùng làm đường dẫn lưu.
            var originalName = Path.GetFileName(
                file.FileName.Replace('\\', '/'));

            if (string.IsNullOrWhiteSpace(originalName))
            {
                originalName = "anh-chat";
            }

            if (originalName.Length > 150)
            {
                originalName = originalName[..150];
            }

            return new PreparedChatImage(
                output.ToArray(),
                originalName,
                sourceHash);
        }
        catch (UnknownImageFormatException)
        {
            throw new ChatException(
                400,
                "File không phải ảnh JPG, PNG hoặc WebP hợp lệ.");
        }
        catch (InvalidImageContentException)
        {
            throw new ChatException(
                400,
                "Ảnh bị lỗi hoặc không đọc được. Anh hãy chọn ảnh khác.");
        }
    }
}

// Kết quả sau khi kiểm tra và xử lý.
// Chưa lưu file hoặc tạo bản ghi trong SQL ở bước này.
public sealed record PreparedChatImage(
    byte[] Bytes,
    string OriginalName,
    string SourceHash)
{
    public string MimeType => "image/png";

    public string Extension => ".png";

    public long SizeBytes => Bytes.LongLength;
}