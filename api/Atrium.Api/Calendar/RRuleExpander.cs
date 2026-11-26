using System.Globalization;

namespace Atrium.Api.Calendar;

public sealed record ParsedRRule(
    string Frequency,
    int Interval,
    int? Count,
    DateTimeOffset? Until,
    IReadOnlyList<DayOfWeek> ByDays,
    int? ByMonthDay);

public static class RRuleExpander
{
    private const int MaxCandidateIterations = 50_000;

    private static readonly HashSet<string> SupportedFrequencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "DAILY",
        "WEEKLY",
        "MONTHLY",
        "YEARLY",
    };

    private static readonly string[] UntilFormats =
    [
        "yyyyMMdd'T'HHmmss'Z'",
        "yyyyMMdd'T'HHmm'Z'",
        "yyyyMMdd",
    ];

    public static bool TryParse(string? rrule, out ParsedRRule? parsed, out string? error)
    {
        parsed = null;
        error = null;

        if (string.IsNullOrWhiteSpace(rrule))
        {
            error = "Recurrence rule cannot be empty.";
            return false;
        }

        var trimmed = rrule.Trim();
        if (trimmed.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[6..].Trim();
        }

        string? freq = null;
        var interval = 1;
        int? count = null;
        DateTimeOffset? until = null;
        var byDays = new List<DayOfWeek>();
        var hasPositionalByDay = false;
        int? byMonthDay = null;

        foreach (var rawPart in trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = rawPart.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0 || eq == rawPart.Length - 1)
            {
                error = $"Invalid recurrence rule segment '{rawPart}'.";
                return false;
            }

            var key = rawPart[..eq].Trim().ToUpperInvariant();
            var value = rawPart[(eq + 1)..].Trim();

            switch (key)
            {
                case "FREQ":
                    if (!SupportedFrequencies.Contains(value))
                    {
                        error = "FREQ must be DAILY, WEEKLY, MONTHLY, or YEARLY.";
                        return false;
                    }

                    freq = value.ToUpperInvariant();
                    break;

                case "INTERVAL":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out interval)
                        || interval < 1
                        || interval > 1000)
                    {
                        error = "INTERVAL must be an integer between 1 and 1000.";
                        return false;
                    }

                    break;

                case "COUNT":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount)
                        || parsedCount < 1
                        || parsedCount > 5000)
                    {
                        error = "COUNT must be an integer between 1 and 5000.";
                        return false;
                    }

                    count = parsedCount;
                    break;

                case "UNTIL":
                    if (!TryParseUntil(value, out var parsedUntil))
                    {
                        error = $"Invalid UNTIL timestamp '{value}'.";
                        return false;
                    }

                    until = parsedUntil;
                    break;

                case "BYDAY":
                    foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!TryParseWeekday(token, out var dayOfWeek, out var isPositional))
                        {
                            error = $"Invalid BYDAY value '{token}'.";
                            return false;
                        }

                        if (isPositional)
                        {
                            hasPositionalByDay = true;
                        }

                        if (!byDays.Contains(dayOfWeek))
                        {
                            byDays.Add(dayOfWeek);
                        }
                    }

                    break;

                case "BYMONTHDAY":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dayNum)
                        || dayNum < 1
                        || dayNum > 31)
                    {
                        error = "BYMONTHDAY must be between 1 and 31.";
                        return false;
                    }

                    byMonthDay = dayNum;
                    break;

                case "WKST":
                    // Accepted standard RFC 5545 parameter (e.g. WKST=MO)
                    break;

                default:
                    error = $"Unsupported recurrence rule clause '{key}'.";
                    return false;
            }
        }

        if (freq is null)
        {
            error = "Recurrence rule must specify FREQ (DAILY, WEEKLY, MONTHLY, or YEARLY).";
            return false;
        }

        if (hasPositionalByDay)
        {
            error = "Positional BYDAY values (e.g. 2TU) are not supported.";
            return false;
        }

        if (byDays.Count > 0 && freq != "WEEKLY")
        {
            error = "BYDAY is only supported for WEEKLY recurrence rules.";
            return false;
        }

        if (byMonthDay.HasValue && freq != "MONTHLY")
        {
            error = "BYMONTHDAY is only supported for MONTHLY recurrence rules.";
            return false;
        }

        parsed = new ParsedRRule(freq, interval, count, until, byDays, byMonthDay);
        return true;
    }

    public static IReadOnlyList<(DateTimeOffset StartAt, DateTimeOffset EndAt)> ExpandOccurrences(
        DateTimeOffset masterStartAt,
        DateTimeOffset masterEndAt,
        bool isAllDay,
        string? timezone,
        string rrule,
        DateTimeOffset rangeFrom,
        DateTimeOffset rangeTo,
        int maxOccurrences = 500)
    {
        if (!TryParse(rrule, out var rule, out _) || rule is null)
        {
            return [];
        }

        var utcStart = masterStartAt.ToUniversalTime();
        var utcEnd = masterEndAt.ToUniversalTime();
        var duration = utcEnd >= utcStart ? utcEnd - utcStart : TimeSpan.Zero;
        var fromUtc = rangeFrom.ToUniversalTime();
        var toUtc = rangeTo.ToUniversalTime();
        var tzInfo = isAllDay ? TimeZoneInfo.Utc : ResolveTimeZone(timezone);

        var localStart = isAllDay
            ? utcStart.UtcDateTime
            : TimeZoneInfo.ConvertTimeFromUtc(utcStart.UtcDateTime, tzInfo);
        var startDate = DateOnly.FromDateTime(localStart);
        var wallTime = TimeOnly.FromDateTime(localStart);

        var results = new List<(DateTimeOffset StartAt, DateTimeOffset EndAt)>();
        var totalGenerated = 0;
        var iterations = 0;

        IEnumerable<DateOnly> candidateDates = rule.Frequency switch
        {
            "DAILY" => GenerateDailyDates(startDate, rule.Interval),
            "WEEKLY" => GenerateWeeklyDates(startDate, rule.Interval, rule.ByDays),
            "MONTHLY" => GenerateMonthlyDates(startDate, rule.Interval, rule.ByMonthDay),
            "YEARLY" => GenerateYearlyDates(startDate, rule.Interval),
            _ => [],
        };

        foreach (var date in candidateDates)
        {
            iterations++;
            if (iterations > MaxCandidateIterations)
            {
                break;
            }

            var occStart = ToUtcInstant(date, wallTime, isAllDay, tzInfo);
            if (occStart < utcStart)
            {
                continue;
            }

            if (rule.Until.HasValue && occStart > rule.Until.Value)
            {
                break;
            }

            totalGenerated++;
            if (rule.Count.HasValue && totalGenerated > rule.Count.Value)
            {
                break;
            }

            if (occStart >= toUtc)
            {
                break;
            }

            var occEnd = occStart + duration;
            var overlapsWindow = occEnd > fromUtc || (occStart == occEnd && occStart >= fromUtc);
            if (overlapsWindow)
            {
                results.Add((occStart, occEnd));
                if (results.Count >= maxOccurrences)
                {
                    break;
                }
            }
        }

        return results;
    }

    private static IEnumerable<DateOnly> GenerateDailyDates(DateOnly start, int interval)
    {
        var current = start;
        while (current.Year <= 9998)
        {
            yield return current;
            if (current > DateOnly.MaxValue.AddDays(-interval))
            {
                yield break;
            }

            current = current.AddDays(interval);
        }
    }

    private static IEnumerable<DateOnly> GenerateWeeklyDates(
        DateOnly start,
        int interval,
        IReadOnlyList<DayOfWeek> byDays)
    {
        var targetDays = byDays.Count > 0 ? byDays : [start.DayOfWeek];
        var mondayOffset = ((int)start.DayOfWeek + 6) % 7;
        var weekMonday = start.AddDays(-mondayOffset);
        var orderedOffsets = targetDays
            .Select(d => ((int)d + 6) % 7)
            .Distinct()
            .Order()
            .ToArray();

        while (weekMonday.Year <= 9998)
        {
            foreach (var offset in orderedOffsets)
            {
                if (weekMonday > DateOnly.MaxValue.AddDays(-offset))
                {
                    yield break;
                }

                var candidate = weekMonday.AddDays(offset);
                if (candidate >= start)
                {
                    yield return candidate;
                }
            }

            var stepDays = interval * 7;
            if (weekMonday > DateOnly.MaxValue.AddDays(-stepDays))
            {
                yield break;
            }

            weekMonday = weekMonday.AddDays(stepDays);
        }
    }

    private static IEnumerable<DateOnly> GenerateMonthlyDates(
        DateOnly start,
        int interval,
        int? byMonthDay)
    {
        var targetDay = byMonthDay ?? start.Day;
        var monthCursor = new DateOnly(start.Year, start.Month, 1);

        while (monthCursor.Year <= 9998)
        {
            var daysInMonth = DateTime.DaysInMonth(monthCursor.Year, monthCursor.Month);
            if (targetDay <= daysInMonth)
            {
                var candidate = new DateOnly(monthCursor.Year, monthCursor.Month, targetDay);
                if (candidate >= start)
                {
                    yield return candidate;
                }
            }

            var totalMonths = (monthCursor.Year * 12) + (monthCursor.Month - 1) + interval;
            var nextYear = totalMonths / 12;
            var nextMonth = (totalMonths % 12) + 1;
            if (nextYear > 9998)
            {
                yield break;
            }

            monthCursor = new DateOnly(nextYear, nextMonth, 1);
        }
    }

    private static IEnumerable<DateOnly> GenerateYearlyDates(DateOnly start, int interval)
    {
        var year = start.Year;
        while (year <= 9998)
        {
            var daysInMonth = DateTime.DaysInMonth(year, start.Month);
            if (start.Day <= daysInMonth)
            {
                yield return new DateOnly(year, start.Month, start.Day);
            }

            year += interval;
        }
    }

    private static DateTimeOffset ToUtcInstant(
        DateOnly date,
        TimeOnly wallTime,
        bool isAllDay,
        TimeZoneInfo tzInfo)
    {
        if (isAllDay)
        {
            var utcMidnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            return new DateTimeOffset(utcMidnight, TimeSpan.Zero);
        }

        var unspecified = date.ToDateTime(wallTime, DateTimeKind.Unspecified);
        if (tzInfo.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        var offset = tzInfo.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }

    private static TimeZoneInfo ResolveTimeZone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone) || timezone.Equals("UTC", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static bool TryParseUntil(string raw, out DateTimeOffset until)
    {
        until = default;
        var upper = raw.ToUpperInvariant();
        if (upper.Length == 8 && DateTime.TryParseExact(upper, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            until = new DateTimeOffset(dateOnly.Year, dateOnly.Month, dateOnly.Day, 23, 59, 59, TimeSpan.Zero);
            return true;
        }

        if (DateTime.TryParseExact(
                upper,
                UntilFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt))
        {
            until = new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
            return true;
        }

        return false;
    }

    private static bool TryParseWeekday(string raw, out DayOfWeek day, out bool isPositional)
    {
        day = default;
        isPositional = false;

        var token = raw.Trim().ToUpperInvariant();
        if (token.Length < 2)
        {
            return false;
        }

        var dayCode = token[^2..];
        if (token.Length > 2)
        {
            var prefix = token[..^2];
            if (!int.TryParse(prefix, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ordinal)
                || ordinal == 0
                || ordinal < -53
                || ordinal > 53)
            {
                return false;
            }

            isPositional = true;
        }

        switch (dayCode)
        {
            case "MO":
                day = DayOfWeek.Monday;
                return true;
            case "TU":
                day = DayOfWeek.Tuesday;
                return true;
            case "WE":
                day = DayOfWeek.Wednesday;
                return true;
            case "TH":
                day = DayOfWeek.Thursday;
                return true;
            case "FR":
                day = DayOfWeek.Friday;
                return true;
            case "SA":
                day = DayOfWeek.Saturday;
                return true;
            case "SU":
                day = DayOfWeek.Sunday;
                return true;
            default:
                day = default;
                return false;
        }
    }
}
