using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Atrium.Api.Calendar;

public static partial class IcsParser
{
    private const int MaxIcsCharacters = 5_000_000;

    private static readonly string[] UtcDateTimeFormats =
    [
        "yyyyMMdd'T'HHmmss'Z'",
        "yyyyMMdd'T'HHmm'Z'",
    ];

    private static readonly string[] LocalDateTimeFormats =
    [
        "yyyyMMdd'T'HHmmss",
        "yyyyMMdd'T'HHmm",
    ];

    private static readonly string[] DateOnlyFormats =
    [
        "yyyyMMdd",
    ];

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "confirmed",
        "tentative",
        "cancelled",
    };

    [GeneratedRegex(
        @"^(?<sign>[+-])?P(?:(?<weeks>\d+)W|(?:(?<days>\d+)D)?(?:T(?:(?<hours>\d+)H)?(?:(?<minutes>\d+)M)?(?:(?<seconds>\d+)S)?)?)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationRegex();

    public static ParsedIcsCalendar Parse(string icsContent, string defaultTimezone = "UTC")
    {
        ArgumentNullException.ThrowIfNull(icsContent);

        if (icsContent.Length > MaxIcsCharacters)
        {
            throw new FormatException("Calendar file exceeds the 5 MB text limit.");
        }

        var normalized = icsContent.TrimStart('\uFEFF');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new FormatException("Calendar file is empty.");
        }

        var lines = UnfoldLines(normalized);
        var hasCalendarStart = false;
        var hasCalendarEnd = false;
        var componentStack = new Stack<string>();

        string? calendarName = null;
        string? calendarDescription = null;
        string? calendarTimezone = null;

        var events = new List<ParsedIcsEvent>();
        var warnings = new List<string>();
        VEventBuilder? currentEvent = null;
        var eventIndex = 0;

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            if (!TryParseContentLine(rawLine, out var propName, out var parameters, out var value))
            {
                continue;
            }

            if (propName.Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                var comp = value.Trim().ToUpperInvariant();
                if (comp == "VCALENDAR")
                {
                    hasCalendarStart = true;
                }
                else if (comp == "VEVENT" && componentStack.Count > 0 && componentStack.Peek() == "VCALENDAR")
                {
                    eventIndex++;
                    currentEvent = new VEventBuilder();
                }

                componentStack.Push(comp);
                continue;
            }

            if (propName.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                var comp = value.Trim().ToUpperInvariant();
                if (comp == "VEVENT" && currentEvent is not null && componentStack.Count > 0 && componentStack.Peek() == "VEVENT")
                {
                    var effectiveTz = !string.IsNullOrWhiteSpace(calendarTimezone)
                        ? calendarTimezone
                        : defaultTimezone;
                    var built = BuildEvent(currentEvent, effectiveTz, eventIndex, warnings);
                    if (built is not null)
                    {
                        events.Add(built);
                    }

                    currentEvent = null;
                }
                else if (comp == "VCALENDAR")
                {
                    hasCalendarEnd = true;
                }

                if (componentStack.Count > 0)
                {
                    componentStack.Pop();
                }

                continue;
            }

            if (componentStack.Count == 0)
            {
                continue;
            }

            var activeComponent = componentStack.Peek();
            if (activeComponent == "VCALENDAR")
            {
                switch (propName.ToUpperInvariant())
                {
                    case "X-WR-CALNAME":
                    case "NAME":
                        if (string.IsNullOrWhiteSpace(calendarName))
                        {
                            calendarName = UnescapeText(value).Trim();
                        }

                        break;
                    case "X-WR-CALDESC":
                    case "DESCRIPTION":
                        if (string.IsNullOrWhiteSpace(calendarDescription))
                        {
                            calendarDescription = UnescapeText(value).Trim();
                        }

                        break;
                    case "X-WR-TIMEZONE":
                        if (string.IsNullOrWhiteSpace(calendarTimezone))
                        {
                            calendarTimezone = UnescapeText(value).Trim();
                        }

                        break;
                }
            }
            else if (activeComponent == "VEVENT" && currentEvent is not null)
            {
                switch (propName.ToUpperInvariant())
                {
                    case "UID":
                        currentEvent.Uid = value.Trim();
                        break;
                    case "SUMMARY":
                        currentEvent.Summary = UnescapeText(value).Trim();
                        break;
                    case "DESCRIPTION":
                        currentEvent.Description = UnescapeText(value).Trim();
                        break;
                    case "LOCATION":
                        currentEvent.Location = UnescapeText(value).Trim();
                        break;
                    case "STATUS":
                        currentEvent.Status = value.Trim();
                        break;
                    case "RRULE":
                        currentEvent.RecurrenceRule = value.Trim();
                        break;
                    case "DURATION":
                        currentEvent.DurationRaw = value.Trim();
                        break;
                    case "DTSTART":
                        currentEvent.DtStartRaw = value.Trim();
                        currentEvent.DtStartParams = parameters;
                        break;
                    case "DTEND":
                        currentEvent.DtEndRaw = value.Trim();
                        currentEvent.DtEndParams = parameters;
                        break;
                    case "RECURRENCE-ID":
                        currentEvent.RecurrenceIdRaw = value.Trim();
                        currentEvent.RecurrenceIdParams = parameters;
                        break;
                    case "EXDATE":
                        currentEvent.ExDates.Add((value.Trim(), parameters));
                        break;
                }
            }
        }

        if (!hasCalendarStart || !hasCalendarEnd)
        {
            throw new FormatException("Input is not a valid iCalendar (.ics) file (missing BEGIN:VCALENDAR / END:VCALENDAR).");
        }

        return new ParsedIcsCalendar(
            string.IsNullOrWhiteSpace(calendarName) ? null : calendarName,
            string.IsNullOrWhiteSpace(calendarDescription) ? null : calendarDescription,
            string.IsNullOrWhiteSpace(calendarTimezone) ? null : calendarTimezone,
            events,
            warnings);
    }

    public static string Serialize(
        string calendarName,
        string? calendarDescription,
        string timezone,
        IEnumerable<CalendarEventDto> events,
        IReadOnlyDictionary<Guid, IReadOnlyList<DateTimeOffset>>? exceptionDatesByMasterId = null)
    {
        ArgumentNullException.ThrowIfNull(calendarName);
        ArgumentNullException.ThrowIfNull(events);

        var sb = new StringBuilder();
        AppendLine(sb, "BEGIN:VCALENDAR");
        AppendLine(sb, "VERSION:2.0");
        AppendLine(sb, "PRODID:-//Atrium//Calendar//EN");
        AppendLine(sb, "CALSCALE:GREGORIAN");
        AppendFoldedProperty(sb, "X-WR-CALNAME", EscapeText(calendarName));
        if (!string.IsNullOrWhiteSpace(calendarDescription))
        {
            AppendFoldedProperty(sb, "X-WR-CALDESC", EscapeText(calendarDescription));
        }

        if (!string.IsNullOrWhiteSpace(timezone))
        {
            AppendFoldedProperty(sb, "X-WR-TIMEZONE", EscapeText(timezone));
        }

        var dtStamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        foreach (var evt in events)
        {
            AppendLine(sb, "BEGIN:VEVENT");
            var uid = !string.IsNullOrWhiteSpace(evt.ExternalUid)
                ? evt.ExternalUid
                : $"{(evt.SeriesMasterId ?? evt.Id):D}@atrium.local";
            AppendFoldedProperty(sb, "UID", uid);
            AppendLine(sb, $"DTSTAMP:{dtStamp}");

            if (evt.IsRecurrenceException && evt.OccurrenceStartAt.HasValue)
            {
                var origUtc = evt.OccurrenceStartAt.Value.ToUniversalTime();
                if (evt.IsAllDay)
                {
                    AppendLine(sb, $"RECURRENCE-ID;VALUE=DATE:{origUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}");
                }
                else
                {
                    AppendLine(sb, $"RECURRENCE-ID:{origUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}");
                }
            }

            if (evt.IsAllDay)
            {
                var startUtc = evt.StartAt.ToUniversalTime();
                var endUtc = evt.EndAt.ToUniversalTime();
                if (endUtc <= startUtc)
                {
                    endUtc = startUtc.AddDays(1);
                }

                AppendLine(sb, $"DTSTART;VALUE=DATE:{startUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}");
                AppendLine(sb, $"DTEND;VALUE=DATE:{endUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}");
            }
            else
            {
                AppendLine(sb, $"DTSTART:{evt.StartAt.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}");
                AppendLine(sb, $"DTEND:{evt.EndAt.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}");
            }

            AppendFoldedProperty(sb, "SUMMARY", EscapeText(evt.Title));
            if (!string.IsNullOrWhiteSpace(evt.Location))
            {
                AppendFoldedProperty(sb, "LOCATION", EscapeText(evt.Location));
            }

            if (!string.IsNullOrWhiteSpace(evt.Description))
            {
                AppendFoldedProperty(sb, "DESCRIPTION", EscapeText(evt.Description));
            }

            if (!string.IsNullOrWhiteSpace(evt.Status) && ValidStatuses.Contains(evt.Status))
            {
                AppendLine(sb, $"STATUS:{evt.Status.ToUpperInvariant()}");
            }

            if (!evt.IsRecurrenceException && !string.IsNullOrWhiteSpace(evt.RecurrenceRule))
            {
                AppendFoldedProperty(sb, "RRULE", evt.RecurrenceRule);
            }

            if (!evt.IsRecurrenceException
                && exceptionDatesByMasterId is not null
                && exceptionDatesByMasterId.TryGetValue(evt.Id, out var exDates)
                && exDates.Count > 0)
            {
                foreach (var exDate in exDates.Order())
                {
                    var exUtc = exDate.ToUniversalTime();
                    if (evt.IsAllDay)
                    {
                        AppendLine(sb, $"EXDATE;VALUE=DATE:{exUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}");
                    }
                    else
                    {
                        AppendLine(sb, $"EXDATE:{exUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}");
                    }
                }
            }

            AppendLine(sb, "END:VEVENT");
        }

        AppendLine(sb, "END:VCALENDAR");
        return sb.ToString();
    }

    private static List<string> UnfoldLines(string content)
    {
        var result = new List<string>();
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && result.Count > 0)
            {
                result[^1] += line[1..];
            }
            else
            {
                result.Add(line);
            }
        }

        return result;
    }

    private static bool TryParseContentLine(
        string line,
        out string name,
        out Dictionary<string, string> parameters,
        out string value)
    {
        name = string.Empty;
        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        value = string.Empty;

        var inQuotes = false;
        var colonIndex = -1;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == ':' && !inQuotes)
            {
                colonIndex = i;
                break;
            }
        }

        if (colonIndex <= 0)
        {
            return false;
        }

        var left = line[..colonIndex];
        value = line[(colonIndex + 1)..];

        var segments = SplitRespectingQuotes(left, ';');
        if (segments.Count == 0 || string.IsNullOrWhiteSpace(segments[0]))
        {
            return false;
        }

        name = segments[0].Trim();
        for (var i = 1; i < segments.Count; i++)
        {
            var seg = segments[i];
            var eqIdx = seg.IndexOf('=', StringComparison.Ordinal);
            if (eqIdx <= 0)
            {
                continue;
            }

            var paramName = seg[..eqIdx].Trim();
            var paramValue = seg[(eqIdx + 1)..].Trim();
            if (paramValue.Length >= 2 && paramValue[0] == '"' && paramValue[^1] == '"')
            {
                paramValue = paramValue[1..^1];
            }

            parameters[paramName] = paramValue;
        }

        return true;
    }

    private static List<string> SplitRespectingQuotes(string text, char separator)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in text)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                current.Append(ch);
            }
            else if (ch == separator && !inQuotes)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        parts.Add(current.ToString());
        return parts;
    }

    private static ParsedIcsEvent? BuildEvent(
        VEventBuilder builder,
        string defaultTimezone,
        int eventIndex,
        List<string> warnings)
    {
        var label = !string.IsNullOrWhiteSpace(builder.Summary)
            ? builder.Summary
            : $"VEVENT #{eventIndex}";

        if (string.IsNullOrWhiteSpace(builder.DtStartRaw))
        {
            warnings.Add($"Skipped '{label}': missing DTSTART.");
            return null;
        }

        if (!TryParseIcsDateTime(
                builder.DtStartRaw,
                builder.DtStartParams,
                defaultTimezone,
                out var startAt,
                out var isAllDay,
                out var resolvedTimezone))
        {
            warnings.Add($"Skipped '{label}': could not parse DTSTART '{builder.DtStartRaw}'.");
            return null;
        }

        DateTimeOffset endAt;
        if (!string.IsNullOrWhiteSpace(builder.DtEndRaw))
        {
            if (!TryParseIcsDateTime(
                    builder.DtEndRaw,
                    builder.DtEndParams,
                    resolvedTimezone ?? defaultTimezone,
                    out endAt,
                    out _,
                    out _))
            {
                warnings.Add($"Skipped '{label}': could not parse DTEND '{builder.DtEndRaw}'.");
                return null;
            }
        }
        else if (!string.IsNullOrWhiteSpace(builder.DurationRaw) && TryParseDuration(builder.DurationRaw, out var duration))
        {
            endAt = startAt + duration;
        }
        else
        {
            endAt = isAllDay ? startAt.AddDays(1) : startAt;
        }

        if (endAt < startAt)
        {
            endAt = isAllDay ? startAt.AddDays(1) : startAt;
        }

        var status = !string.IsNullOrWhiteSpace(builder.Status) && ValidStatuses.Contains(builder.Status)
            ? builder.Status.ToLowerInvariant()
            : "confirmed";

        var title = string.IsNullOrWhiteSpace(builder.Summary) ? "(Untitled event)" : builder.Summary;
        if (title.Length > 200)
        {
            title = title[..200];
        }

        var location = string.IsNullOrWhiteSpace(builder.Location) ? null : builder.Location;
        if (location is not null && location.Length > 280)
        {
            location = location[..280];
        }

        var uid = string.IsNullOrWhiteSpace(builder.Uid) ? null : builder.Uid;
        if (uid is not null && uid.Length > 255)
        {
            uid = uid[..255];
        }

        var rrule = string.IsNullOrWhiteSpace(builder.RecurrenceRule) ? null : builder.RecurrenceRule;
        if (rrule is not null && rrule.Length > 255)
        {
            rrule = rrule[..255];
        }

        DateTimeOffset? recurrenceId = null;
        if (!string.IsNullOrWhiteSpace(builder.RecurrenceIdRaw)
            && TryParseIcsDateTime(
                builder.RecurrenceIdRaw,
                builder.RecurrenceIdParams,
                resolvedTimezone ?? defaultTimezone,
                out var parsedRecId,
                out _,
                out _))
        {
            recurrenceId = parsedRecId.ToUniversalTime();
        }

        List<DateTimeOffset>? exceptionDates = null;
        if (builder.ExDates.Count > 0)
        {
            exceptionDates = [];
            foreach (var (rawList, exParams) in builder.ExDates)
            {
                foreach (var token in rawList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (TryParseIcsDateTime(token, exParams, resolvedTimezone ?? defaultTimezone, out var exDt, out _, out _))
                    {
                        var exUtc = exDt.ToUniversalTime();
                        if (!exceptionDates.Contains(exUtc))
                        {
                            exceptionDates.Add(exUtc);
                        }
                    }
                }
            }
        }

        return new ParsedIcsEvent(
            uid,
            title,
            string.IsNullOrWhiteSpace(builder.Description) ? null : builder.Description,
            location,
            startAt.ToUniversalTime(),
            endAt.ToUniversalTime(),
            isAllDay,
            resolvedTimezone,
            status,
            rrule,
            recurrenceId,
            exceptionDates);
    }

    private static bool TryParseIcsDateTime(
        string raw,
        Dictionary<string, string>? parameters,
        string defaultTimezone,
        out DateTimeOffset result,
        out bool isAllDay,
        out string? resolvedTimezone)
    {
        result = default;
        isAllDay = false;
        resolvedTimezone = null;

        var valueParam = parameters is not null && parameters.TryGetValue("VALUE", out var vp)
            ? vp
            : null;
        var tzid = parameters is not null && parameters.TryGetValue("TZID", out var tz)
            ? tz
            : null;

        if (string.Equals(valueParam, "DATE", StringComparison.OrdinalIgnoreCase)
            || (raw.Length == 8 && !raw.Contains('T', StringComparison.OrdinalIgnoreCase)))
        {
            if (DateTime.TryParseExact(raw, DateOnlyFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            {
                isAllDay = true;
                resolvedTimezone = tzid ?? defaultTimezone;
                result = new DateTimeOffset(DateTime.SpecifyKind(dateOnly, DateTimeKind.Utc));
                return true;
            }

            return false;
        }

        if (raw.EndsWith('Z') || raw.EndsWith('z'))
        {
            if (DateTime.TryParseExact(
                    raw.ToUpperInvariant(),
                    UtcDateTimeFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var utcDt))
            {
                resolvedTimezone = "UTC";
                result = new DateTimeOffset(DateTime.SpecifyKind(utcDt, DateTimeKind.Utc));
                return true;
            }

            return false;
        }

        if (DateTime.TryParseExact(
                raw.ToUpperInvariant(),
                LocalDateTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localDt))
        {
            var tzName = !string.IsNullOrWhiteSpace(tzid) ? tzid : defaultTimezone;
            resolvedTimezone = tzName;
            var tzInfo = ResolveTimeZone(tzName);
            var unspecified = DateTime.SpecifyKind(localDt, DateTimeKind.Unspecified);
            var offset = tzInfo.GetUtcOffset(unspecified);
            result = new DateTimeOffset(unspecified, offset).ToUniversalTime();
            return true;
        }

        return false;
    }

    private static TimeZoneInfo ResolveTimeZone(string? tzId)
    {
        if (string.IsNullOrWhiteSpace(tzId) || tzId.Equals("UTC", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(tzId);
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

    private static bool TryParseDuration(string raw, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var match = DurationRegex().Match(raw.Trim());
        if (!match.Success)
        {
            return false;
        }

        var weeks = ParseGroupInt(match.Groups["weeks"]);
        var days = ParseGroupInt(match.Groups["days"]);
        var hours = ParseGroupInt(match.Groups["hours"]);
        var minutes = ParseGroupInt(match.Groups["minutes"]);
        var seconds = ParseGroupInt(match.Groups["seconds"]);

        var total = new TimeSpan((weeks * 7) + days, hours, minutes, seconds);
        if (match.Groups["sign"].Value == "-")
        {
            total = -total;
        }

        duration = total;
        return true;
    }

    private static int ParseGroupInt(Group group) =>
        group.Success && int.TryParse(group.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val)
            ? val
            : 0;

    private static string UnescapeText(string raw)
    {
        if (!raw.Contains('\\', StringComparison.Ordinal))
        {
            return raw;
        }

        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                var next = raw[i + 1];
                switch (next)
                {
                    case 'n':
                    case 'N':
                        sb.Append('\n');
                        i++;
                        continue;
                    case ',':
                    case ';':
                    case '\\':
                        sb.Append(next);
                        i++;
                        continue;
                }
            }

            sb.Append(raw[i]);
        }

        return sb.ToString();
    }

    private static string EscapeText(string raw)
    {
        return raw
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal);
    }

    private static void AppendLine(StringBuilder sb, string line)
    {
        sb.Append(line);
        sb.Append("\r\n");
    }

    private static void AppendFoldedProperty(StringBuilder sb, string propName, string value)
    {
        var fullLine = $"{propName}:{value}";
        const int maxChunk = 72;
        if (fullLine.Length <= maxChunk)
        {
            AppendLine(sb, fullLine);
            return;
        }

        var index = 0;
        var first = true;
        while (index < fullLine.Length)
        {
            var take = Math.Min(first ? maxChunk : maxChunk - 1, fullLine.Length - index);
            if (!first)
            {
                sb.Append(' ');
            }

            sb.Append(fullLine.AsSpan(index, take));
            sb.Append("\r\n");
            index += take;
            first = false;
        }
    }

    private sealed class VEventBuilder
    {
        public string? Uid { get; set; }
        public string? Summary { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
        public string? Status { get; set; }
        public string? RecurrenceRule { get; set; }
        public string? DurationRaw { get; set; }
        public string? DtStartRaw { get; set; }
        public Dictionary<string, string>? DtStartParams { get; set; }
        public string? DtEndRaw { get; set; }
        public Dictionary<string, string>? DtEndParams { get; set; }
        public string? RecurrenceIdRaw { get; set; }
        public Dictionary<string, string>? RecurrenceIdParams { get; set; }
        public List<(string Raw, Dictionary<string, string>? Params)> ExDates { get; } = [];
    }
}
