using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Atrium.Api.Attachments;
using Atrium.Api.Auth;
using Atrium.Api.Data;
using Atrium.Api.Knowledge;
using Atrium.Api.Notes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class ArtifactTextExtractionTests
{
    private const string OwnerPassword = "Atrium-Owner-Pass-2026!";

    private static async Task<AuthSessionResponse> SetupOwnerAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync(
            "/api/auth/setup",
            new SetupRequest("andras", "andras@atrium.local", "András", OwnerPassword));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var session = await res.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.NotNull(session);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return session;
    }

    private static byte[] BuildMinimalPptx(params string[] slideTexts)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 0; i < slideTexts.Length; i += 1)
            {
                var entry = zip.CreateEntry($"ppt/slides/slide{i + 1}.xml");
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(
                    $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                           xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                      <p:cSld><p:spTree><p:sp><p:txBody>
                        <a:p><a:r><a:t>{slideTexts[i]}</a:t></a:r></a:p>
                      </p:txBody></p:sp></p:spTree></p:cSld>
                    </p:sld>
                    """);
            }
        }

        return ms.ToArray();
    }

    private static byte[] BuildMinimalDocx(params string[] paragraphs)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            var body = string.Join(string.Empty, paragraphs.Select(p => $"<w:p><w:r><w:t>{p}</w:t></w:r></w:p>"));
            writer.Write(
                $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>{body}</w:body>
                </w:document>
                """);
        }

        return ms.ToArray();
    }

    [Fact]
    public void Extract_PptxDocxAndPlainText_ExtractsOrderedChunks()
    {
        var extractor = new DefaultArtifactTextExtractor();

        using var pptxStream = new MemoryStream(BuildMinimalPptx("Intro to Graphs", "Dijkstra Algorithm"));
        var pptxResult = extractor.Extract(
            pptxStream,
            "lecture3.pptx",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            CancellationToken.None);
        Assert.Equal(AttachmentExtractionStatuses.Completed, pptxResult.Status);
        Assert.Equal(2, pptxResult.PageCount);
        Assert.Equal(2, pptxResult.Chunks.Count);
        Assert.Equal("Slide 1", pptxResult.Chunks[0].PageLabel);
        Assert.Contains("Intro to Graphs", pptxResult.Chunks[0].Content, StringComparison.Ordinal);
        Assert.Equal("Slide 2", pptxResult.Chunks[1].PageLabel);
        Assert.Contains("Dijkstra Algorithm", pptxResult.Chunks[1].Content, StringComparison.Ordinal);

        using var docxStream = new MemoryStream(BuildMinimalDocx("Course Syllabus", "Grading rubric 50% exam"));
        var docxResult = extractor.Extract(
            docxStream,
            "syllabus.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            CancellationToken.None);
        Assert.Equal(AttachmentExtractionStatuses.Completed, docxResult.Status);
        Assert.Single(docxResult.Chunks);
        Assert.Contains("Grading rubric", docxResult.Chunks[0].Content, StringComparison.Ordinal);

        var longText = string.Join('\n', Enumerable.Repeat("Line of algorithm study notes for chunking.", 150));
        using var txtStream = new MemoryStream(Encoding.UTF8.GetBytes(longText));
        var txtResult = extractor.Extract(txtStream, "notes.md", "text/markdown", CancellationToken.None);
        Assert.Equal(AttachmentExtractionStatuses.Completed, txtResult.Status);
        Assert.True(txtResult.Chunks.Count >= 2);
    }

    [Fact]
    public async Task UploadCorruptPdf_Returns201_AndMarksStatusFailed_WhileValidTextExtracts()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var note = await (await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Study", null, "Body")))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);

        // Corrupt PDF must NOT fail upload
        using var badPdfForm = new MultipartFormDataContent();
        var badPdf = new ByteArrayContent("%PDF-1.4\nnot-a-valid-pdf-stream"u8.ToArray());
        badPdf.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        badPdfForm.Add(badPdf, "file", "broken.pdf");
        badPdfForm.Add(new StringContent(AttachmentKinds.Artifact), "kind");
        badPdfForm.Add(new StringContent(note.Id.ToString()), "noteId");

        var badRes = await client.PostAsync("/api/attachments", badPdfForm);
        Assert.Equal(HttpStatusCode.Created, badRes.StatusCode);
        var badDto = await badRes.Content.ReadFromJsonAsync<AttachmentDto>();
        Assert.NotNull(badDto);
        Assert.Equal(AttachmentExtractionStatuses.Failed, badDto.TextExtractionStatus);

        // Valid text artifact extracts immediately
        using var txtForm = new MultipartFormDataContent();
        var txt = new ByteArrayContent("Red-black trees guarantee O(log n) height."u8.ToArray());
        txt.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        txtForm.Add(txt, "file", "trees.txt");
        txtForm.Add(new StringContent(AttachmentKinds.Artifact), "kind");
        txtForm.Add(new StringContent(note.Id.ToString()), "noteId");

        var okRes = await client.PostAsync("/api/attachments", txtForm);
        Assert.Equal(HttpStatusCode.Created, okRes.StatusCode);
        var okDto = await okRes.Content.ReadFromJsonAsync<AttachmentDto>();
        Assert.NotNull(okDto);
        Assert.Equal(AttachmentExtractionStatuses.Completed, okDto.TextExtractionStatus);

        // Verify lazy backfill idempotency by resetting status to pending and re-extracting
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = await db.Attachments.FirstAsync(a => a.Id == okDto.Id);
        entity.TextExtractionStatus = AttachmentExtractionStatuses.Pending;
        db.AttachmentTextChunks.RemoveRange(await db.AttachmentTextChunks.Where(c => c.AttachmentId == okDto.Id).ToListAsync());
        await db.SaveChangesAsync();

        var textService = scope.ServiceProvider.GetRequiredService<ArtifactTextService>();
        await textService.EnsureExtractedAsync(okDto.Id, entity.UserId, CancellationToken.None);
        await textService.EnsureExtractedAsync(okDto.Id, entity.UserId, CancellationToken.None);

        var chunks = await db.AttachmentTextChunks.Where(c => c.AttachmentId == okDto.Id).ToListAsync();
        Assert.Single(chunks);
        Assert.Contains("Red-black trees", chunks[0].Content, StringComparison.Ordinal);
    }
}

