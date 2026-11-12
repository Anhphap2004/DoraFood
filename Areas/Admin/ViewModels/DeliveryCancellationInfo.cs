using System;

namespace DoraFood.Areas.Admin.ViewModels;

// Thông tin hủy đọc từ AuditLogs để hiển thị trong trang chi tiết.
public sealed record DeliveryCancellationInfo(string ActorName, string Reason, DateTime CreatedAtUtc);
