namespace discord_chatbot.Models;

public sealed class AzureDevOpsOptions
{
    public const string SectionName = "AzureDevOps";

    public string Organization { get; init; } = string.Empty;

    public string Project { get; init; } = string.Empty;

    public string PersonalAccessToken { get; init; } = string.Empty;
}
