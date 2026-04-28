namespace discord_chatbot.Models;

public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    public string Token { get; init; } = string.Empty;

    public ulong? GuildId { get; init; }
}
