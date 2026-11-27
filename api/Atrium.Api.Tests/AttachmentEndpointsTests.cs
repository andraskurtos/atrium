using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Atrium.Api.Attachments;
using Atrium.Api.Auth;
using Atrium.Api.Notes;
using Atrium.Api.University;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class AttachmentEndpointsTests
{
    private const string OwnerPassword = "Atrium-Owner-Pass-2026!";
    private const string MemberPassword = "Atrium-Member-Pass-2026!";

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

    private static async Task<AuthSessionResponse> CreateInvitedMemberAsync(
        HttpClient ownerClient,
        HttpClient memberClient)
    {
        var inviteRes = await ownerClient.PostAsJsonAsync(
            "/api/account/invites",
            new CreateInviteRequest("Partner", 7));
        Assert.Equal(HttpStatusCode.Created, inviteRes.StatusCode);
        var invite = await inviteRes.Content.ReadFromJsonAsync<CreateInviteResponse>();
        Assert.NotNull(invite);

        var regRes = await memberClient.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterWithInviteRequest(invite.Code, "partner", "partner@atrium.local", "Partner", MemberPassword));
        Assert.Equal(HttpStatusCode.OK, regRes.StatusCode);
        var session = await regRes.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.NotNull(session);
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return session;
    }

    private static MultipartFormDataContent BuildUploadForm(
        byte[] bytes,
        string fileName,
        string contentType,
        string kind,
        Guid? noteId = null,
        Guid? courseId = null,
        Guid? itemId = null,
        Guid? eventId = null,
        DateTimeOffset? occurrenceStartAt = null)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent(kind), "kind");
        if (noteId.HasValue) form.Add(new StringContent(noteId.Value.ToString()), "noteId");
        if (courseId.HasValue) form.Add(new StringContent(courseId.Value.ToString()), "universityCourseId");
        if (itemId.HasValue) form.Add(new StringContent(itemId.Value.ToString()), "universityActivityItemId");
        if (eventId.HasValue) form.Add(new StringContent(eventId.Value.ToString()), "calendarEventId");
        if (occurrenceStartAt.HasValue) form.Add(new StringContent(occurrenceStartAt.Value.ToString("O")), "calendarOccurrenceStartAt");
        return form;
    }

    [Fact]
    public async Task UploadAndDownload_Artifact_OnCourseAndItem_StreamsWithAttachmentHeaders()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var course = await (await client.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("Algorithms", "CS301", "2026 Fall", null, null, null, "#c25438", null)))
            .Content.ReadFromJsonAsync<UniversityCourseDto>();
        Assert.NotNull(course);

        var pdfBytes = "%PDF-1.4\nSyllabus content\n"u8.ToArray();
        using var uploadForm = BuildUploadForm(pdfBytes, "syllabus.pdf", "application/pdf", AttachmentKinds.Artifact, courseId: course.Id);
        var uploadRes = await client.PostAsync("/api/attachments", uploadForm);
        Assert.Equal(HttpStatusCode.Created, uploadRes.StatusCode);

        var dto = await uploadRes.Content.ReadFromJsonAsync<AttachmentDto>();
        Assert.NotNull(dto);
        Assert.Equal("syllabus.pdf", dto.FileName);
        Assert.Equal(AttachmentKinds.Artifact, dto.Kind);
        Assert.Equal($"/api/attachments/{dto.Id}", dto.Url);

        var list = await client.GetFromJsonAsync<List<AttachmentDto>>($"/api/attachments?universityCourseId={course.Id}");
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal(dto.Id, list[0].Id);

        var dlRes = await client.GetAsync(dto.Url);
        Assert.Equal(HttpStatusCode.OK, dlRes.StatusCode);
        Assert.Equal("nosniff", dlRes.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("attachment", dlRes.Content.Headers.ContentDisposition?.DispositionType);
        var downloaded = await dlRes.Content.ReadAsByteArrayAsync();
        Assert.Equal(pdfBytes, downloaded);
    }

    [Fact]
    public async Task UploadInlineImage_HiddenFromArtifactList_StreamsInline()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var note = await (await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Visual Note", null, "Body")))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);

        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        using var uploadForm = BuildUploadForm(pngBytes, "diagram.png", "image/png", AttachmentKinds.InlineImage, noteId: note.Id);
        var uploadRes = await client.PostAsync("/api/attachments", uploadForm);
        Assert.Equal(HttpStatusCode.Created, uploadRes.StatusCode);
        var dto = await uploadRes.Content.ReadFromJsonAsync<AttachmentDto>();
        Assert.NotNull(dto);

        // Must NOT appear in visible artifact list
        var list = await client.GetFromJsonAsync<List<AttachmentDto>>($"/api/attachments?noteId={note.Id}");
        Assert.NotNull(list);
        Assert.Empty(list);

        // Must stream inline
        var imgRes = await client.GetAsync(dto.Url);
        Assert.Equal(HttpStatusCode.OK, imgRes.StatusCode);
        Assert.Equal("inline", imgRes.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", imgRes.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task UploadValidation_RejectsSvgInlineImage_BlockedExtensions_AndInvalidOwners()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var note = await (await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Note", null, "Body")))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);

        using var svgForm = BuildUploadForm(
            Encoding.UTF8.GetBytes("<svg></svg>"),
            "xss.svg",
            "image/svg+xml",
            AttachmentKinds.InlineImage,
            noteId: note.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/attachments", svgForm)).StatusCode);

        using var exeForm = BuildUploadForm(
            [1, 2, 3],
            "malware.exe",
            "application/octet-stream",
            AttachmentKinds.Artifact,
            noteId: note.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/attachments", exeForm)).StatusCode);

        using var noOwnerForm = BuildUploadForm(
            [1, 2, 3],
            "orphan.pdf",
            "application/pdf",
            AttachmentKinds.Artifact);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/attachments", noOwnerForm)).StatusCode);
    }

    [Fact]
    public async Task FilenameSanitization_StripsPathTraversalAndControlChars()
    {
        Assert.Equal("evilname.pdf", Atrium.Api.Endpoints.AttachmentEndpoints.SanitizeFileName("../../evil\"\r\n;name.pdf"));

        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var note = await (await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Note", null, "Body")))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);

        using var form = BuildUploadForm(
            "%PDF-1.4"u8.ToArray(),
            "../../evil;name.pdf",
            "application/pdf",
            AttachmentKinds.Artifact,
            noteId: note.Id);
        var res = await client.PostAsync("/api/attachments", form);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<AttachmentDto>();
        Assert.NotNull(dto);
        Assert.DoesNotContain("/", dto.FileName, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", dto.FileName, StringComparison.Ordinal);
        Assert.DoesNotContain("..", dto.FileName, StringComparison.Ordinal);
        Assert.Equal("evilname.pdf", dto.FileName);
    }

    [Fact]
    public async Task AuthAndAgentScopes_EnforcesUserIsolation_ModuleScopes_AndDeleteCleanup()
    {
        using var factory = new TestAppFactory();
        using var ownerClient = factory.CreateClient();
        using var memberClient = factory.CreateClient();
        await SetupOwnerAsync(ownerClient);
        await CreateInvitedMemberAsync(ownerClient, memberClient);

        var note = await (await ownerClient.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Public Note", null, "Body", AllowAgentRead: true)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);

        var course = await (await ownerClient.PostAsJsonAsync(
            "/api/university/courses",
            new CreateCourseRequest("Math", "M101", "2026 Fall", null, null, null, "#c25438", null)))
            .Content.ReadFromJsonAsync<UniversityCourseDto>();
        Assert.NotNull(course);

        using var noteForm = BuildUploadForm("hello"u8.ToArray(), "doc.txt", "text/plain", AttachmentKinds.Artifact, noteId: note.Id);
        var noteAtt = await (await ownerClient.PostAsync("/api/attachments", noteForm)).Content.ReadFromJsonAsync<AttachmentDto>();
        Assert.NotNull(noteAtt);

        using var courseForm = BuildUploadForm("syllabus"u8.ToArray(), "syl.pdf", "application/pdf", AttachmentKinds.Artifact, courseId: course.Id);
        var courseAtt = await (await ownerClient.PostAsync("/api/attachments", courseForm)).Content.ReadFromJsonAsync<AttachmentDto>();
        Assert.NotNull(courseAtt);

        // Other user cannot access
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync(noteAtt.Url)).StatusCode);

        // Agent PAT with notes:read only can read note attachment, NOT course attachment
        var patRes = await ownerClient.PostAsJsonAsync(
            "/api/account/tokens",
            new CreateAgentTokenRequest("Notes Only Agent", ["notes:read"]));
        Assert.Equal(HttpStatusCode.Created, patRes.StatusCode);
        var pat = await patRes.Content.ReadFromJsonAsync<CreateAgentTokenResponse>();
        Assert.NotNull(pat);

        using var agentClient = factory.CreateClient();
        agentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pat.Secret);
        Assert.Equal(HttpStatusCode.OK, (await agentClient.GetAsync(noteAtt.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agentClient.GetAsync(courseAtt.Url)).StatusCode);

        // Hiding note from agents hides its attachment too
        await ownerClient.PutAsJsonAsync($"/api/notes/{note.Id}", new UpdateNoteRequest(AllowAgentRead: false));
        Assert.Equal(HttpStatusCode.NotFound, (await agentClient.GetAsync(noteAtt.Url)).StatusCode);

        // Delete removes DB record and file
        var delRes = await ownerClient.DeleteAsync(noteAtt.Url);
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerClient.GetAsync(noteAtt.Url)).StatusCode);
    }
}
