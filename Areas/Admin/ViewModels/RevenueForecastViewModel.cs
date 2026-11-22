using System;
using System.Collections.Generic;

namespace DoraFood.Areas.Admin.ViewModels;

public sealed record RevenueForecastRow(
    DateTime Date,
    decimal? PredictedNetRevenue,
    int SampleCount);

public sealed record RevenueForecastViewModel(
    DateTime? HistoryFrom,
    DateTime HistoryTo,
    IReadOnlyList<RevenueForecastRow> Days,
    decimal? PredictedTotal);