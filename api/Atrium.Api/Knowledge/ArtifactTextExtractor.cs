using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Atrium.Api.Attachments;
using UglyToad.PdfPig;

namespace Atrium.Api.Knowledge;

public sealed record ExtractedTextChunk(
    int ChunkIndex,
    string PageLabel,
    string Content);

public sealed record ArtifactExtractionResult(
    string Status,
    int? PageCount,
    IReadOnlyList<ExtractedTextChunk> Chunks);

public interface IArtifactTextExtractor
{
    ArtifactExtractionResult Extract(Stream stream, string fileName, string contentType, CancellationToken ct);
}

public sealed partial class DefaultArtifactTextExtractor : IArtifactTextExtractor
{
    public const int MaxTotalChars = 500_000;
    public const int MaxChunkChars = 4_000;
    public const int MaxPdfPages = 200;

    private static readonly HashSet<string> PlainTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".yaml", ".yml", ".xml", ".log",
    };

    [GeneratedRegex(@"^ppt/slides/slide(\d+)\.xml$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PptxSlidePathRegex();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultiNewlineRegex();

    public ArtifactExtractionResult Extract(
        Stream stream,
        string fileName,
        string contentType,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ct.ThrowIfCancellationRequested();

        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        var mime = (contentType ?? string.Empty).Trim().ToLowerInvariant();

        try
        {
            if (ext == ".pdf" || mime == "application/pdf")
            {
                return ExtractPdf(stream, ct);
            }

            if (ext == ".pptx" || mime == "application/vnd.openxmlformats-officedocument.presentationml.presentation")
            {
                return ExtractPptx(stream, ct);
            }

            if (ext == ".docx" || mime == "application/vnd.openxmlformats-officedocument.wordprocessingml.document")
            {
                return ExtractDocx(stream, ct);
            }

            if (PlainTextExtensions.Contains(ext) || mime.StartsWith("text/", StringComparison.Ordinal))
            {
                return ExtractPlainText(stream, ct);
            }

            return new ArtifactExtractionResult(AttachmentExtractionStatuses.Unsupported, null, []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new ArtifactExtractionResult(AttachmentExtractionStatuses.Failed, null, []);
        }
    }

    private static ArtifactExtractionResult ExtractPdf(Stream stream, CancellationToken ct)
    {
        using var document = PdfDocument.Open(stream);
        var totalPages = document.NumberOfPages;
        var chunks = new List<ExtractedTextChunk>();
        var totalChars = 0;

        for (var pageNum = 1; pageNum <= Math.Min(totalPages, MaxPdfPages); pageNum += 1)
        {
            ct.ThrowIfCancellationRequested();
            if (totalChars >= MaxTotalChars) break;

            var page = document.GetPage(pageNum);
            var text = NormalizeText(page.Text);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var label = $"Page {pageNum}";
            AppendChunked(chunks, label, text, ref totalChars);
        }

        if (chunks.Count == 0)
        {
            return new ArtifactExtractionResult(AttachmentExtractionStatuses.Empty, totalPages, []);
        }

        return new ArtifactExtractionResult(AttachmentExtractionStatuses.Completed, totalPages, chunks);
    }

    private static ArtifactExtractionResult ExtractPptx(Stream stream, CancellationToken ct)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var slideEntries = zip.Entries
            .Select(e => (Entry: e, Match: PptxSlidePathRegex().Match(e.FullName)))
            .Where(x => x.Match.Success)
            .Select(x => (x.Entry, SlideNum: int.Parse(x.Match.Groups[1].Value, CultureInfo.InvariantCulture)))
            .OrderBy(x => x.SlideNum)
            .ToList();

        var chunks = new List<ExtractedTextChunk>();
        var totalChars = 0;
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        foreach (var (entry, slideNum) in slideEntries)
        {
            ct.ThrowIfCancellationRequested();
            if (totalChars >= MaxTotalChars) break;

            using var entryStream = entry.Open();
            var xdoc = XDocument.Load(entryStream);
            var lines = xdoc.Descendants(drawingNs + "p")
                .Select(p => string.Concat(p.Descendants(drawingNs + "t").Select(t => t.Value)).Trim())
                .Where(line => line.Length > 0);

            var text = NormalizeText(string.Join('\n', lines));
            if (string.IsNullOrWhiteSpace(text)) continue;

            AppendChunked(chunks, $"Slide {slideNum}", text, ref totalChars);
        }

        var pageCount = slideEntries.Count > 0 ? slideEntries.Count : (int?)null;
        if (chunks.Count == 0)
        {
            return new ArtifactExtractionResult(AttachmentExtractionStatuses.Empty, pageCount, []);
        }

        return new ArtifactExtractionResult(AttachmentExtractionStatuses.Completed, pageCount, chunks);
    }

    private static ArtifactExtractionResult ExtractDocx(Stream stream, CancellationToken ct)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var docEntry = zip.GetEntry("word/document.xml");
        if (docEntry is null)
        {
            return new ArtifactExtractionResult(AttachmentExtractionStatuses.Empty, null, []);
        }

        ct.ThrowIfCancellationRequested();
        using var entryStream = docEntry.Open();
        var xdoc = XDocument.Load(entryStream);
        XNamespace wordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var paragraphs = xdoc.Descendants(wordNs + "p")
            .Select(p => string.Concat(p.Descendants(wordNs + "t").Select(t => t.Value)).Trim())
            .Where(line => line.Length > 0);

        var text = NormalizeText(string.Join("\n\n", paragraphs));
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ArtifactExtractionResult(AttachmentExtractionStatuses.Empty, null, []);
        }

        var chunks = new List<ExtractedTextChunk>();
        var totalChars = 0;
        AppendChunked(chunks, "Section", text, ref totalChars, numberParts: true);
        return new ArtifactExtractionResult(AttachmentExtractionStatuses.Completed, chunks.Count, chunks);
    }

    private static ArtifactExtractionResult ExtractPlainText(Stream stream, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var buffer = new char[MaxTotalChars];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        var text = NormalizeText(new string(buffer, 0, read));
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ArtifactExtractionResult(AttachmentExtractionStatuses.Empty, null, []);
        }

        var chunks = new List<ExtractedTextChunk>();
        var totalChars = 0;
        AppendChunked(chunks, "Part", text, ref totalChars, numberParts: true);
        return new ArtifactExtractionResult(AttachmentExtractionStatuses.Completed, chunks.Count, chunks);
    }

    private static string NormalizeText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var unified = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var noNulls = unified.Replace("\0", string.Empty, StringComparison.Ordinal);
        var collapsedSpaces = MultiSpaceRegex().Replace(noNulls, " ");
        return MultiNewlineRegex().Replace(collapsedSpaces, "\n\n").Trim();
    }

    private static void AppendChunked(
        List<ExtractedTextChunk> target,
        string baseLabel,
        string text,
        ref int totalChars,
        bool numberParts = false)
    {
        var remainingBudget = MaxTotalChars - totalChars;
        if (remainingBudget <= 0) return;

        var bounded = text.Length > remainingBudget ? text[..remainingBudget] : text;
        var offset = 0;
        var part = 1;

        while (offset < bounded.Length)
        {
            var len = Math.Min(MaxChunkChars, bounded.Length - offset);
            if (offset + len < bounded.Length)
            {
                var breakPos = bounded.LastIndexOf('\n', offset + len - 1, Math.Min(len, 600));
                if (breakPos <= offset)
                {
                    breakPos = bounded.LastIndexOf(' ', offset + len - 1, Math.Min(len, 200));
                }

                if (breakPos > offset)
                {
                    len = breakPos - offset;
                }
            }

            var slice = bounded.Substring(offset, len).Trim();
            offset += len;

            if (slice.Length == 0) continue;

            var label = numberParts
                ? $"{baseLabel} {part}"
                : (part == 1 ? baseLabel : $"{baseLabel} (cont. {part})");
            if (label.Length > 64) label = label[..64];

            target.Add(new ExtractedTextChunk(target.Count, label, slice));
            totalChars += slice.Length;
            part += 1;
        }
    }
}

