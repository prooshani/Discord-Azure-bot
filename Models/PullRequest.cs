namespace discord_chatbot.Models;

public sealed record PullRequest(int Id, string Title, string RepositoryName, string Url);
