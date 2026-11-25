using Atrium.Api.Data.Entities;

namespace Atrium.Api.Auth;

public sealed record AuthStatusResponse(bool Initialized);

public sealed record SetupRequest(
    string Username,
    string Email,
    string DisplayName,
    string Password);

public sealed record LoginRequest(
    string Identifier,
    string Password);

public sealed record RegisterWithInviteRequest(
    string InviteCode,
    string Username,
    string Email,
    string DisplayName,
    string Password);

public sealed record AuthSessionResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    UserDto User,
    PreferencesDto Preferences);

public sealed record RefreshResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt);

public sealed record UserDto(
    Guid Id,
    string Username,
    string Email,
    string DisplayName,
    string Bio,
    string Timezone,
    string? AvatarDataUrl,
    string Role,
    DateTimeOffset PasswordUpdatedAt,
    DateTimeOffset CreatedAt);

public sealed record PreferencesDto(
    string Theme,
    string WeekStartsOn,
    string TimeFormat,
    bool RequireAgentApproval,
    bool MorningBrief,
    bool EventReminders);

public sealed record AccountMeResponse(
    UserDto User,
    PreferencesDto Preferences);

public sealed record UpdateProfileRequest(
    string DisplayName,
    string Username,
    string Email,
    string Bio,
    string Timezone,
    string? AvatarDataUrl);

public sealed record UpdatePreferencesRequest(
    string Theme,
    string WeekStartsOn,
    string TimeFormat,
    bool RequireAgentApproval,
    bool MorningBrief,
    bool EventReminders);

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);

public sealed record SessionDto(
    Guid Id,
    string Device,
    string Location,
    DateTimeOffset LastActiveAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent);

public sealed record CreateAgentTokenRequest(
    string Name,
    IReadOnlyList<string> Scopes);

public sealed record AgentTokenDto(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

public sealed record CreateAgentTokenResponse(
    AgentTokenDto Token,
    string Secret);

public sealed record AgentWhoAmIResponse(
    Guid UserId,
    string Username,
    string AuthType,
    IReadOnlyList<string> Scopes);

public sealed record CreateInviteRequest(
    string? Label,
    int? ExpiresInDays);

public sealed record InviteDto(
    Guid Id,
    string CodePrefix,
    string? Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt,
    DateTimeOffset? RevokedAt,
    string Status);

public sealed record CreateInviteResponse(
    InviteDto Invite,
    string Code,
    string InvitePath);

public sealed record HouseholdMemberDto(
    Guid Id,
    string Username,
    string DisplayName,
    string Email,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActiveAt,
    int ActiveSessionCount,
    int ActiveTokenCount);

public sealed record UpdateMemberStatusRequest(bool IsActive);

public static class AuthMappings
{
    public static UserDto ToUserDto(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserDto(
            user.Id,
            user.Username,
            user.Email,
            user.DisplayName,
            user.Bio,
            user.Timezone,
            user.AvatarDataUrl,
            user.Role,
            user.PasswordUpdatedAt,
            user.CreatedAt);
    }

    public static PreferencesDto ToPreferencesDto(UserPreferences prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        return new PreferencesDto(
            prefs.Theme,
            prefs.WeekStartsOn,
            prefs.TimeFormat,
            prefs.RequireAgentApproval,
            prefs.MorningBrief,
            prefs.EventReminders);
    }

    public static InviteDto ToInviteDto(Invite invite, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(invite);
        var status = invite.UsedAt is not null ? "used"
            : invite.RevokedAt is not null ? "revoked"
            : invite.ExpiresAt <= now ? "expired"
            : "pending";

        return new InviteDto(
            invite.Id,
            invite.CodePrefix,
            invite.Label,
            invite.CreatedAt,
            invite.ExpiresAt,
            invite.UsedAt,
            invite.RevokedAt,
            status);
    }

    public static AgentTokenDto ToAgentTokenDto(AgentToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var scopes = token.Scopes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new AgentTokenDto(
            token.Id,
            token.Name,
            token.Prefix,
            scopes,
            token.CreatedAt,
            token.LastUsedAt);
    }
}
