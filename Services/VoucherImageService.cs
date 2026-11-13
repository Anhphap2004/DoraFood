using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DoraFood.Services;

public sealed class VoucherImageService
{
    private const long MaxSize = 5 * 1024 * 1024;

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<VoucherImageService> _logger;

    public VoucherImageService(
        IWebHostEnvironment environment,
        ILogger<VoucherImageService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<VoucherImageUpload> UploadAsync(
        IFormFile file,
        long uploadedBy,
        string altText,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0 || file.Length > MaxSize)
        {
            throw new ValidationException(
                "Ảnh phải có dữ liệu và không vượt quá 5 MB.");
        }

        using var buffer = new MemoryStream();

        await using (var source = file.OpenReadStream())
        {
            var chunk = new byte[81920];
            int bytesRead;

            while ((bytesRead = await source.ReadAsync(
                chunk.AsMemory(),
                cancellationToken)) > 0)
            {
                if (buffer.Length + bytesRead > MaxSize)
                {
                    throw new ValidationException(
                        "Ảnh quảng cáo tối đa 5 MB.");
                }

                await buffer.WriteAsync(
                    chunk.AsMemory(0, bytesRead),
                    cancellationToken);
            }
        }

        var format = DetectFormat(
            buffer.GetBuffer(),
            checked((int)buffer.Length));

        if (!format.HasValue)
        {
            throw new ValidationException(
                "File không được nhận diện là ảnh JPG, PNG hoặc WebP.");
        }

        var webRoot = _environment.WebRootPath;

        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(
                _environment.ContentRootPath,
                "wwwroot");
        }

        var directory = Path.Combine(
            webRoot,
            "uploads",
            "vouchers");

        Directory.CreateDirectory(directory);

        // Tự sinh tên file, không dùng tên khách gửi làm đường dẫn.
        var fileName = $"{Guid.NewGuid():N}{format.Value.Extension}";
        var fullPath = Path.Combine(directory, fileName);

        buffer.Position = 0;

        try
        {
            await using var destination = new FileStream(
                fullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);

            await buffer.CopyToAsync(
                destination,
                cancellationToken);
        }
        catch
        {
            DeleteFile(fullPath);
            throw;
        }

        var originalName = Path.GetFileName(
            file.FileName.Replace('\\', '/'));

        if (string.IsNullOrWhiteSpace(originalName))
        {
            originalName = fileName;
        }

        if (originalName.Length > 255)
        {
            originalName = originalName[..255];
        }

        var media = new MediaFile
        {
            Url = $"~/uploads/vouchers/{fileName}",
            OriginalName = originalName,
            MimeType = format.Value.MimeType,
            SizeBytes = buffer.Length,
            AltText = altText,
            UploadedBy = uploadedBy,
            CreatedAtUtc = DateTime.UtcNow
        };

        return new VoucherImageUpload(media, fullPath);
    }

    public void DeleteFile(string fullPath)
    {
        try
        {
            File.Delete(fullPath);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(
                exception,
                "Không dọn được file ảnh voucher {Path}.",
                fullPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(
                exception,
                "Không có quyền dọn file ảnh voucher {Path}.",
                fullPath);
        }
    }

    private static (string Extension, string MimeType)? DetectFormat(
        byte[] data,
        int length)
    {
        var header = data.AsSpan(0, length);

        if (header.Length >= 8
            && header[..8].SequenceEqual(
                new byte[]
                {
                    0x89, 0x50, 0x4E, 0x47,
                    0x0D, 0x0A, 0x1A, 0x0A
                }))
        {
            return (".png", "image/png");
        }

        if (header.Length >= 3
            && header[0] == 0xFF
            && header[1] == 0xD8
            && header[2] == 0xFF)
        {
            return (".jpg", "image/jpeg");
        }

        if (header.Length >= 12
            && header[..4].SequenceEqual("RIFF"u8)
            && header.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return (".webp", "image/webp");
        }

        return null;
    }
}

public sealed record VoucherImageUpload(
    MediaFile Media,
    string FullPath);