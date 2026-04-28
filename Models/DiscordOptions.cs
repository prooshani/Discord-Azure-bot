namespace discord_chatbot.Models;

public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    public string Token { get; init; } = string.Empty;

    public ulong? GuildId { get; init; }

    public CommandAuthorizationOptions CommandAuthorization { get; init; } = new();
}

public sealed class CommandAuthorizationOptions
{
    public AssignTaskAuthorizationOptions AssignTask { get; init; } = new();
}

public sealed class AssignTaskAuthorizationOptions
{
    public string AllowedRoleIdsCsv { get; init; } = string.Empty;

    public string AllowedUserIdsCsv { get; init; } = string.Empty;

    public string UnauthorizedMessage { get; init; } = "you are not authorized to assign tasks to others.";
}
