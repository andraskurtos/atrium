using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Atrium.Api.Auth;
using Atrium.Api.Notes;
using Xunit;

namespace Atrium.Api.Tests;

public sealed class NoteEndpointsTests
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

    [Fact]
    public async Task Folders_CreateNestRenameMoveRejectCyclesAndSoftDeleteSubtree()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var parent = await (await client.PostAsJsonAsync(
            "/api/notes/folders",
            new CreateNoteFolderRequest("University")))
            .Content.ReadFromJsonAsync<NoteFolderDto>();
        Assert.NotNull(parent);

        var child = await (await client.PostAsJsonAsync(
            "/api/notes/folders",
            new CreateNoteFolderRequest("Algorithms", parent.Id)))
            .Content.ReadFromJsonAsync<NoteFolderDto>();
        Assert.NotNull(child);

        // Moving a folder into itself or its own descendant is rejected with 400
        var selfMove = await client.PutAsJsonAsync(
            $"/api/notes/folders/{parent.Id}",
            new UpdateNoteFolderRequest(ParentFolderId: parent.Id));
        Assert.Equal(HttpStatusCode.BadRequest, selfMove.StatusCode);

        var cycleMove = await client.PutAsJsonAsync(
            $"/api/notes/folders/{parent.Id}",
            new UpdateNoteFolderRequest(ParentFolderId: child.Id));
        Assert.Equal(HttpStatusCode.BadRequest, cycleMove.StatusCode);

        // Add a note inside child folder
        var note = await (await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Graphs", child.Id, "---\ntags: [cs, graphs]\n---\n\nBFS and DFS")))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(note);

        // Soft-delete parent folder -> deletes parent, child, and child's note & tags
        var delRes = await client.DeleteAsync($"/api/notes/folders/{parent.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        var tree = await client.GetFromJsonAsync<NotesTreeResponse>("/api/notes/tree");
        Assert.NotNull(tree);
        Assert.Empty(tree.Folders);
        Assert.Empty(tree.Notes);
        Assert.Empty(tree.Tags);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/notes/{note.Id}")).StatusCode);
    }

    [Fact]
    public async Task Notes_CrudFrontmatterTags_TitlePrecedence_AndVersionConflict()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        const string initialContent = """
            ---
            title: Linear Algebra Notes
            tags:
              - #Uni
              - Math-101
              - math-101
            author: Andras
            ---

            Eigenvalues and eigenvectors.
            """;

        var created = await (await client.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest(Content: initialContent)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(created);
        Assert.Equal("Linear Algebra Notes", created.Title);
        Assert.Equal(["math-101", "uni"], created.Tags);
        Assert.Equal(1, created.Version);
        Assert.Equal(64, created.ContentHash.Length);

        // Source-mode save (Title omitted) updates Note.Title from YAML title:
        const string sourceUpdatedContent = """
            ---
            title: Linear Algebra II
            tags: [uni, exam_prep]
            author: Andras
            ---

            SVD decomposition.
            """;

        var update1Res = await client.PutAsJsonAsync(
            $"/api/notes/{created.Id}",
            new UpdateNoteRequest(Content: sourceUpdatedContent, ExpectedVersion: 1));
        Assert.Equal(HttpStatusCode.OK, update1Res.StatusCode);
        var updated1 = await update1Res.Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(updated1);
        Assert.Equal("Linear Algebra II", updated1.Title);
        Assert.Equal(["exam_prep", "uni"], updated1.Tags);
        Assert.Equal(2, updated1.Version);
        Assert.NotEqual(created.ContentHash, updated1.ContentHash);

        // Stale ExpectedVersion returns 409 Conflict with current server note
        var conflictRes = await client.PutAsJsonAsync(
            $"/api/notes/{created.Id}",
            new UpdateNoteRequest(Content: "Stale overwrite", ExpectedVersion: 1));
        Assert.Equal(HttpStatusCode.Conflict, conflictRes.StatusCode);
        var conflict = await conflictRes.Content.ReadFromJsonAsync<NoteConflictResponse>();
        Assert.NotNull(conflict);
        Assert.Equal(2, conflict.CurrentNote.Version);
        Assert.Equal("Linear Algebra II", conflict.CurrentNote.Title);

        // Explicit Title wins over YAML title
        var renameRes = await client.PutAsJsonAsync(
            $"/api/notes/{created.Id}",
            new UpdateNoteRequest(Title: "Explicit Title", Content: sourceUpdatedContent, ExpectedVersion: 2));
        Assert.Equal(HttpStatusCode.OK, renameRes.StatusCode);
        var renamed = await renameRes.Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(renamed);
        Assert.Equal("Explicit Title", renamed.Title);
        Assert.Equal(3, renamed.Version);

        // Delete note
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/notes/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/notes/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Search_FiltersByQueryTagAndFolderSubtree()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        await SetupOwnerAsync(client);

        var parent = await (await client.PostAsJsonAsync(
            "/api/notes/folders",
            new CreateNoteFolderRequest("Projects")))
            .Content.ReadFromJsonAsync<NoteFolderDto>();
        Assert.NotNull(parent);

        var sub = await (await client.PostAsJsonAsync(
            "/api/notes/folders",
            new CreateNoteFolderRequest("Atrium", parent.Id)))
            .Content.ReadFromJsonAsync<NoteFolderDto>();
        Assert.NotNull(sub);

        await client.PostAsJsonAsync("/api/notes", new CreateNoteRequest("Root Note", null, "Unfiled thoughts"));
        await client.PostAsJsonAsync("/api/notes", new CreateNoteRequest("Parent Note", parent.Id, "---\ntags: [planning]\n---\nRoadmap"));
        await client.PostAsJsonAsync("/api/notes", new CreateNoteRequest("Deep Note", sub.Id, "---\ntags: [sync, planning]\n---\nDesktop sync protocol"));

        var rootOnly = await client.GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes?folderId=root");
        Assert.NotNull(rootOnly);
        Assert.Single(rootOnly);
        Assert.Equal("Root Note", rootOnly[0].Title);

        var recursive = await client.GetFromJsonAsync<List<NoteSummaryDto>>($"/api/notes?folderId={parent.Id}&includeSubfolders=true");
        Assert.NotNull(recursive);
        Assert.Equal(2, recursive.Count);

        var directOnly = await client.GetFromJsonAsync<List<NoteSummaryDto>>($"/api/notes?folderId={parent.Id}&includeSubfolders=false");
        Assert.NotNull(directOnly);
        Assert.Single(directOnly);
        Assert.Equal("Parent Note", directOnly[0].Title);

        var byTagAndQuery = await client.GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes?tag=planning&q=protocol");
        Assert.NotNull(byTagAndQuery);
        Assert.Single(byTagAndQuery);
        Assert.Equal("Deep Note", byTagAndQuery[0].Title);

        // searchIn=titles matches title only, not body
        var titlesOnly = await client.GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes?q=protocol&searchIn=titles");
        Assert.NotNull(titlesOnly);
        Assert.Empty(titlesOnly);

        // searchIn=content matches body only, not title
        var contentOnly = await client.GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes?q=protocol&searchIn=content");
        Assert.NotNull(contentOnly);
        Assert.Single(contentOnly);
        Assert.Equal("Deep Note", contentOnly[0].Title);

        // searchIn=tags matches tags only
        var tagsOnly = await client.GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes?q=sync&searchIn=tags");
        Assert.NotNull(tagsOnly);
        Assert.Single(tagsOnly);
        Assert.Equal("Deep Note", tagsOnly[0].Title);

        // Invalid searchIn returns 400
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/notes?q=sync&searchIn=invalid")).StatusCode);
    }

    [Fact]
    public async Task UserIsolation_AndAgentReadVisibilityInheritance()
    {
        using var factory = new TestAppFactory();
        using var ownerClient = factory.CreateClient();
        using var memberClient = factory.CreateClient();
        await SetupOwnerAsync(ownerClient);
        await CreateInvitedMemberAsync(ownerClient, memberClient);

        // Grandparent folder hidden from agents -> child folder & its notes inherit hidden status!
        var privateRoot = await (await ownerClient.PostAsJsonAsync(
            "/api/notes/folders",
            new CreateNoteFolderRequest("Private Journal", AllowAgentRead: false)))
            .Content.ReadFromJsonAsync<NoteFolderDto>();
        Assert.NotNull(privateRoot);

        var nestedUnderPrivate = await (await ownerClient.PostAsJsonAsync(
            "/api/notes/folders",
            new CreateNoteFolderRequest("2026", privateRoot.Id, AllowAgentRead: true)))
            .Content.ReadFromJsonAsync<NoteFolderDto>();
        Assert.NotNull(nestedUnderPrivate);
        Assert.False(nestedUnderPrivate.EffectiveAllowAgentRead);

        var inheritedHiddenNote = await (await ownerClient.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Secret Diary", nestedUnderPrivate.Id, "---\ntags: [secret]\n---\nPrivate", AllowAgentRead: true)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(inheritedHiddenNote);
        Assert.False(inheritedHiddenNote.EffectiveAllowAgentRead);

        var directHiddenNote = await (await ownerClient.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Hidden Root", null, "---\ntags: [hidden]\n---\nPrivate", AllowAgentRead: false)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(directHiddenNote);

        var publicNote = await (await ownerClient.PostAsJsonAsync(
            "/api/notes",
            new CreateNoteRequest("Study Guide", null, "---\ntags: [study]\n---\nPublic knowledge", AllowAgentRead: true)))
            .Content.ReadFromJsonAsync<NoteDetailDto>();
        Assert.NotNull(publicNote);

        // Member cannot access or mutate owner's notes/folders
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync($"/api/notes/{publicNote.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.DeleteAsync($"/api/notes/{publicNote.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.DeleteAsync($"/api/notes/folders/{privateRoot.Id}")).StatusCode);

        // PAT with notes:read sees ONLY publicNote, never inheritedHiddenNote or directHiddenNote
        var patRead = await (await ownerClient.PostAsJsonAsync(
            "/api/account/tokens",
            new CreateAgentTokenRequest("Notes Agent", ["notes:read"])))
            .Content.ReadFromJsonAsync<CreateAgentTokenResponse>();
        Assert.NotNull(patRead);

        using var patClient = factory.CreateClient();
        patClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", patRead.Secret);

        var patTree = await patClient.GetFromJsonAsync<NotesTreeResponse>("/api/notes/tree");
        Assert.NotNull(patTree);
        Assert.Empty(patTree.Folders);
        Assert.Single(patTree.Notes);
        Assert.Equal("Study Guide", patTree.Notes[0].Title);
        Assert.Single(patTree.Tags);
        Assert.Equal("study", patTree.Tags[0].Tag);

        var patSearch = await patClient.GetFromJsonAsync<List<NoteSummaryDto>>("/api/notes?q=Private");
        Assert.NotNull(patSearch);
        Assert.Empty(patSearch);

        Assert.Equal(HttpStatusCode.OK, (await patClient.GetAsync($"/api/notes/{publicNote.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await patClient.GetAsync($"/api/notes/{inheritedHiddenNote.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await patClient.GetAsync($"/api/notes/{directHiddenNote.Id}")).StatusCode);

        // PAT cannot write
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await patClient.PostAsJsonAsync("/api/notes", new CreateNoteRequest("Blocked"))).StatusCode);

        // PAT without notes:read gets 403
        var patNoScope = await (await ownerClient.PostAsJsonAsync(
            "/api/account/tokens",
            new CreateAgentTokenRequest("Calendar Agent", ["calendar:read"])))
            .Content.ReadFromJsonAsync<CreateAgentTokenResponse>();
        Assert.NotNull(patNoScope);

        using var patNoScopeClient = factory.CreateClient();
        patNoScopeClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", patNoScope.Secret);
        Assert.Equal(HttpStatusCode.Forbidden, (await patNoScopeClient.GetAsync("/api/notes/tree")).StatusCode);
    }
}

