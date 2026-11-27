using System.Security.Cryptography;
using System.Text;

namespace Atrium.Api.Notes;

public sealed record ParsedNoteFrontmatter(
    string? Title,
    IReadOnlyList<string> Tags,
    string Body);

public static class NoteFrontmatter
{
    public const int MaxTagLength = 40;
    public const int MaxTagsPerNote = 30;

    public static ParsedNoteFrontmatter Parse(string? rawContent)
    {
        var content = rawContent ?? string.Empty;
        if (content.Length == 0)
        {
            return new ParsedNoteFrontmatter(null, [], string.Empty);
        }

        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        if (lines.Length < 3 || lines[0].Trim() != "---")
        {
            return new ParsedNoteFrontmatter(null, [], content);
        }

        var closingIndex = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed is "---" or "...")
            {
                closingIndex = i;
                break;
            }
        }

        if (closingIndex < 0)
        {
            return new ParsedNoteFrontmatter(null, [], content);
        }

        string? title = null;
        var tags = new List<string>();
        var tagSet = new HashSet<string>(StringComparer.Ordinal);
        var inTagsList = false;

        for (var i = 1; i < closingIndex; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (inTagsList)
            {
                if (trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    AddTag(trimmed[2..], tags, tagSet);
                    continue;
                }

                if (trimmed.Length == 0)
                {
                    continue;
                }

                inTagsList = false;
            }

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim().ToLowerInvariant();
            var rawValue = line[(colon + 1)..].Trim();

            if (key == "title")
            {
                var unquoted = UnquoteYamlScalar(rawValue);
                if (!string.IsNullOrWhiteSpace(unquoted))
                {
                    title = unquoted;
                }
            }
            else if (key == "tags")
            {
                if (rawValue.Length == 0)
                {
                    inTagsList = true;
                }
                else if (rawValue.StartsWith('[') && rawValue.EndsWith(']'))
                {
                    var inner = rawValue[1..^1];
                    foreach (var part in inner.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        AddTag(part, tags, tagSet);
                    }
                }
                else
                {
                    foreach (var part in rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        AddTag(part, tags, tagSet);
                    }
                }
            }
        }

        var body = closingIndex + 1 < lines.Length
            ? string.Join('\n', lines[(closingIndex + 1)..])
            : string.Empty;

        return new ParsedNoteFrontmatter(title, tags, body);
    }

    public static string? NormalizeTag(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = UnquoteYamlScalar(raw.Trim()).Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].Trim();
        }

        if (trimmed.Length == 0 || trimmed.Length > MaxTagLength)
        {
            return null;
        }

        var lower = trimmed.ToLowerInvariant();
        foreach (var ch in lower)
        {
            var valid = (ch >= 'a' && ch <= 'z')
                || (ch >= '0' && ch <= '9')
                || ch is '-' or '_' or '/';
            if (!valid)
            {
                return null;
            }
        }

        return lower;
    }

    public static string ComputeContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }

    public static string BuildExcerpt(string body, int maxLength = 160, string? highlightTerm = null)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var source = body;
        var hasLeadingEllipsis = false;
        if (!string.IsNullOrWhiteSpace(highlightTerm))
        {
            var idx = body.IndexOf(highlightTerm, StringComparison.OrdinalIgnoreCase);
            if (idx > 48)
            {
                var start = Math.Max(0, idx - 40);
                source = body[start..];
                hasLeadingEllipsis = true;
            }
        }

        var sb = new StringBuilder(Math.Min(source.Length, maxLength + 16));
        if (hasLeadingEllipsis)
        {
            sb.Append('…');
        }

        var lastWasSpace = true;

        foreach (var ch in source)
        {
            if (ch is '#' or '*' or '_' or '>' or '`' or '[' or ']')
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }

            if (sb.Length >= maxLength)
            {
                break;
            }
        }

        var result = sb.ToString().Trim();
        if (result.Length >= maxLength)
        {
            return result.TrimEnd() + "…";
        }

        return result;
    }

    private static void AddTag(string raw, List<string> tags, HashSet<string> tagSet)
    {
        if (tags.Count >= MaxTagsPerNote)
        {
            return;
        }

        var normalized = NormalizeTag(raw);
        if (normalized is not null && tagSet.Add(normalized))
        {
            tags.Add(normalized);
        }
    }

    private static string UnquoteYamlScalar(string value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            {
                return value[1..^1].Trim();
            }
        }

        return value;
    }
}

