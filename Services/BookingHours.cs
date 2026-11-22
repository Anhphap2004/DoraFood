using System;
using System.Globalization;

namespace DoraFood.Services;

public static class BookingHours
{
    private static readonly TimeSpan LunchStart = new(10, 0, 0);
    private static readonly TimeSpan LunchEnd = new(14, 0, 0);
    private static readonly TimeSpan DinnerStart = new(16, 0, 0);
    private static readonly TimeSpan DinnerEnd = new(22, 0, 0);

    public static string ArrivalWindowsText { get; } =
        $"{Format(LunchStart)} đến trước {Format(LunchEnd)} hoặc "
        + $"{Format(DinnerStart)} đến trước {Format(DinnerEnd)}";

    public static DateTime GetSuggestedArrival(DateTime earliestLocalArrival)
    {
        var time = earliestLocalArrival.TimeOfDay;
        if (time < LunchStart)
            return earliestLocalArrival.Date.Add(LunchStart);

        if (time >= LunchEnd && time < DinnerStart)
            return earliestLocalArrival.Date.Add(DinnerStart);

        if (time >= DinnerEnd)
            return earliestLocalArrival.Date.AddDays(1).Add(LunchStart);

        return earliestLocalArrival;
    }

    public static DateTime? GetSessionEnd(DateTime localArrival)
    {
        var time = localArrival.TimeOfDay;
        if (time >= LunchStart && time < LunchEnd)
            return localArrival.Date.Add(LunchEnd);

        if (time >= DinnerStart && time < DinnerEnd)
            return localArrival.Date.Add(DinnerEnd);

        return null;
    }

    private static string Format(TimeSpan time) =>
        time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
}
