namespace discord_chatbot.Models;

public sealed record WorkItem(
    int Id,
    string Title,
    string Url,
    string WorkItemType,
    string Priority,
    string TimeEstimation,
    string Area,
    string? Assignee);
