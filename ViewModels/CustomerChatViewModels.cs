using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace DoraFood.ViewModels;

public sealed class CustomerChatViewModel
{
    public long ConversationId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public long? ReservationId { get; init; }

    public string? ReservationCode { get; init; }

    public bool CanSend => Status == "OPEN";
}

// Dùng chung cho form gửi tin của khách hàng và Admin.
public sealed class SendChatMessageInput : IValidatableObject
{
    public const long MaxImageBytes = 5 * 1024 * 1024;

    [StringLength(
        2000,
        ErrorMessage = "Tin nhắn tối đa 2.000 ký tự.")]
    public string? Body { get; set; }

    // File ảnh được gửi từ trình duyệt.
    public IFormFile? Image { get; set; }

    // Giữ nguyên mã này khi gửi lại cùng một tin bị lỗi mạng.
    public Guid ClientMessageId { get; set; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        var hasText = !string.IsNullOrWhiteSpace(Body);
        var hasImage = Image is { Length: > 0 };

        if (!hasText && !hasImage)
        {
            yield return new ValidationResult(
                "Anh hãy nhập tin nhắn hoặc chọn một ảnh.",
                new[] { nameof(Body), nameof(Image) });
        }

        if (Image != null)
        {
            if (Image.Length == 0)
            {
                yield return new ValidationResult(
                    "File ảnh rỗng. Anh hãy chọn ảnh khác.",
                    new[] { nameof(Image) });
            }
            else if (Image.Length > MaxImageBytes)
            {
                yield return new ValidationResult(
                    "Ảnh tối đa 5 MB.",
                    new[] { nameof(Image) });
            }
        }

        if (ClientMessageId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Thiếu mã gửi tin nhắn. Anh hãy tải lại trang.",
                new[] { nameof(ClientMessageId) });
        }
    }
}