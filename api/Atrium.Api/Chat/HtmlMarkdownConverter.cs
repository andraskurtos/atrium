using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Atrium.Api.Chat;

public static partial class HtmlMarkdownConverter
{
    [GeneratedRegex(@"<title[^>]*>(?<t>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"<(script|style|noscript|svg|template|iframe)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex NoiseBlockRegex();

    [GeneratedRegex(@"<(nav|footer|aside)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ChromeBlockRegex();

    [GeneratedRegex(@"<(main|article)\b[^>]*>(?<inner>.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex MainBlockRegex();

    [GeneratedRegex(@"<body\b[^>]*>(?<inner>.*?)</body>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex BodyBlockRegex();

    [GeneratedRegex(@"<table\b[^>]*>(?<inner>.*?)</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TableRegex();

    [GeneratedRegex(@"<tr\b[^>]*>(?<inner>.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TableRowRegex();

    [GeneratedRegex(@"<(th|td)\b[^>]*>(?<inner>.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TableCellRegex();

    [GeneratedRegex(@"<h(?<lvl>[1-6])\b[^>]*>(?<inner>.*?)</h\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"<a\b[^>]*href\s*=\s*(?:""(?<href>[^""]*)""|'(?<href>[^']*)'|(?<href>[^\s>]+))[^>]*>(?<inner>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"<li\b[^>]*>(?<inner>.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrRegex();

    [GeneratedRegex(@"</?(p|div|section|article|header|ul|ol|dl|dt|dd|blockquote|pre|hr)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockBreakRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex AnyTagRegex();

    [GeneratedRegex(@"[ \t]+", RegexOptions.CultureInvariant)]
    private static partial Regex HorizontalSpaceRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultiBlankLineRegex();

    public static (string? Title, string Markdown) Convert(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return (null, string.Empty);
        }

        string? title = null;
        var titleMatch = TitleRegex().Match(html);
        if (titleMatch.Success)
        {
            title = CleanInlineText(titleMatch.Groups["t"].Value);
            if (string.IsNullOrWhiteSpace(title)) title = null;
        }

        var working = CommentRegex().Replace(html, string.Empty);
        working = NoiseBlockRegex().Replace(working, string.Empty);

        var mainMatch = MainBlockRegex().Match(working);
        if (mainMatch.Success && mainMatch.Groups["inner"].Value.Length > 80)
        {
            working = mainMatch.Groups["inner"].Value;
        }
        else
        {
            var bodyMatch = BodyBlockRegex().Match(working);
            if (bodyMatch.Success)
            {
                working = bodyMatch.Groups["inner"].Value;
            }
        }

        working = ChromeBlockRegex().Replace(working, string.Empty);

        // Convert tables into GFM Markdown tables before stripping remaining tags
        working = TableRegex().Replace(working, m => "\n\n" + ConvertTable(m.Groups["inner"].Value) + "\n\n");

        // Headings
        working = HeadingRegex().Replace(working, m =>
        {
            var level = int.TryParse(m.Groups["lvl"].Value, out var l) ? Math.Clamp(l, 1, 6) : 2;
            var text = CleanInlineText(m.Groups["inner"].Value);
            return string.IsNullOrWhiteSpace(text) ? string.Empty : $"\n\n{new string('#', level)} {text}\n\n";
        });

        // List items
        working = ListItemRegex().Replace(working, m =>
        {
            var text = CleanInlineText(m.Groups["inner"].Value);
            return string.IsNullOrWhiteSpace(text) ? string.Empty : $"\n- {text}";
        });

        // Links (keep readable text; include absolute http(s) URL if distinct)
        working = LinkRegex().Replace(working, m =>
        {
            var href = WebUtility.HtmlDecode(m.Groups["href"].Value).Trim();
            var label = CleanInlineText(m.Groups["inner"].Value);
            if (string.IsNullOrWhiteSpace(label)) return string.Empty;
            if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return $"[{label}]({href})";
            }

            return label;
        });

        working = BrRegex().Replace(working, "\n");
        working = BlockBreakRegex().Replace(working, "\n\n");
        working = AnyTagRegex().Replace(working, string.Empty);
        working = WebUtility.HtmlDecode(working);

        var lines = working.Split('\n');
        var sb = new StringBuilder();
        foreach (var rawLine in lines)
        {
            var line = HorizontalSpaceRegex().Replace(rawLine, " ").Trim();
            sb.AppendLine(line);
        }

        var normalized = MultiBlankLineRegex().Replace(sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), "\n\n").Trim();
        return (title, normalized);
    }

    private static string ConvertTable(string tableInnerHtml)
    {
        var rows = new List<List<string>>();
        foreach (Match rowMatch in TableRowRegex().Matches(tableInnerHtml))
        {
            var cells = new List<string>();
            foreach (Match cellMatch in TableCellRegex().Matches(rowMatch.Groups["inner"].Value))
            {
                var text = CleanInlineText(cellMatch.Groups["inner"].Value).Replace("|", "\\|", StringComparison.Ordinal);
                cells.Add(text);
            }

            if (cells.Count > 0 && cells.Any(c => !string.IsNullOrWhiteSpace(c)))
            {
                rows.Add(cells);
            }
        }

        if (rows.Count == 0) return string.Empty;

        var colCount = rows.Max(r => r.Count);
        var sb = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            sb.Append('|');
            for (var c = 0; c < colCount; c++)
            {
                var cell = c < row.Count ? row[c] : string.Empty;
                sb.Append(' ').Append(cell).Append(" |");
            }

            sb.AppendLine();
            if (i == 0)
            {
                sb.Append('|');
                for (var c = 0; c < colCount; c++)
                {
                    sb.Append(" --- |");
                }

                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string CleanInlineText(string htmlFragment)
    {
        var noTags = AnyTagRegex().Replace(htmlFragment, " ");
        var decoded = WebUtility.HtmlDecode(noTags);
        return HorizontalSpaceRegex().Replace(decoded.Replace('\r', ' ').Replace('\n', ' '), " ").Trim();
    }
}

