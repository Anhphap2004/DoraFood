using System;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed record CancellationReviewViewModel(
    long RequestId,
    string Version,
    string Code,
    int ReturnPage,
    string CustomerName,
    string Reason,
    DateTime RequestedAtUtc);