using discord_chatbot.Utils;

namespace discord_chatbot.Services;

public interface IMessageParser
{
    ParsedMessageReferences Parse(string messageContent);
}

public sealed class MessageParser : IMessageParser
{
    public ParsedMessageReferences Parse(string messageContent)
    {
        if (string.IsNullOrWhiteSpace(messageContent))
        {
            return ParsedMessageReferences.Empty;
        }

        var workItemIds = RegexPatterns.WorkItemPattern.Matches(messageContent)
            .Select(match => int.TryParse(match.Groups[1].Value, out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        var pullRequestIds = RegexPatterns.PullRequestPattern.Matches(messageContent)
            .Select(match => int.TryParse(match.Groups[1].Value, out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        return new ParsedMessageReferences(workItemIds, pullRequestIds);
    }
}

public sealed record ParsedMessageReferences(IReadOnlySet<int> WorkItemIds, IReadOnlySet<int> PullRequestIds)
{
    public static ParsedMessageReferences Empty { get; } = new ParsedMessageReferences(new HashSet<int>(), new HashSet<int>());

    public bool HasReferences => WorkItemIds.Count > 0 || PullRequestIds.Count > 0;
}
