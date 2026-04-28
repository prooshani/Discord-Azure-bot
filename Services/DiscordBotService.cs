using Discord;
using Discord.WebSocket;
using discord_chatbot.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.RegularExpressions;

namespace discord_chatbot.Services;

public sealed class DiscordBotService : BackgroundService
{
    private readonly DiscordSocketClient _discordClient;
    private readonly IMessageParser _messageParser;
    private readonly IAzureDevOpsService _azureDevOpsService;
    private readonly IMemoryCache _memoryCache;
    private readonly DiscordOptions _discordOptions;
    private readonly ILogger<DiscordBotService> _logger;
    private static readonly TimeSpan ProcessedMessageTtl = TimeSpan.FromMinutes(10);

    public DiscordBotService(
        DiscordSocketClient discordClient,
        IMessageParser messageParser,
        IAzureDevOpsService azureDevOpsService,
        IMemoryCache memoryCache,
        IOptions<DiscordOptions> discordOptions,
        ILogger<DiscordBotService> logger)
    {
        _discordClient = discordClient;
        _messageParser = messageParser;
        _azureDevOpsService = azureDevOpsService;
        _memoryCache = memoryCache;
        _discordOptions = discordOptions.Value;
        _logger = logger;

        _discordClient.Log += OnDiscordLogAsync;
        _discordClient.MessageReceived += OnMessageReceivedAsync;
        _discordClient.SlashCommandExecuted += OnSlashCommandExecutedAsync;
        _discordClient.Ready += OnReadyAsync;
        _discordClient.Disconnected += OnDisconnectedAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Discord bot service");

        await _discordClient.LoginAsync(TokenType.Bot, _discordOptions.Token);
        await _discordClient.StartAsync();

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Discord bot service");

        _discordClient.MessageReceived -= OnMessageReceivedAsync;
        _discordClient.SlashCommandExecuted -= OnSlashCommandExecutedAsync;
        _discordClient.Ready -= OnReadyAsync;
        _discordClient.Disconnected -= OnDisconnectedAsync;
        _discordClient.Log -= OnDiscordLogAsync;

        await _discordClient.StopAsync();
        await _discordClient.LogoutAsync();

        await base.StopAsync(cancellationToken);
    }

    private async Task OnReadyAsync()
    {
        _logger.LogInformation("Discord bot connected as {Username} ({UserId})", _discordClient.CurrentUser.Username, _discordClient.CurrentUser.Id);
        await RegisterCommandsAsync();
    }

    private Task OnDiscordLogAsync(LogMessage message)
    {
        _logger.Log(
            MapSeverity(message.Severity),
            message.Exception,
            "Discord.Net [{Severity}] {Source}: {Message}",
            message.Severity,
            message.Source,
            message.Message);
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is null)
        {
            _logger.LogWarning("Discord gateway disconnected without exception details.");
        }
        else
        {
            _logger.LogError(exception, "Discord gateway disconnected.");
        }

        return Task.CompletedTask;
    }

    private static LogLevel MapSeverity(LogSeverity severity)
    {
        return severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Debug,
            _ => LogLevel.Information
        };
    }

    private async Task OnMessageReceivedAsync(SocketMessage rawMessage)
    {
        if (rawMessage is not SocketUserMessage message)
        {
            return;
        }

        if (message.Source != MessageSource.User)
        {
            return;
        }

        var processedKey = $"discord:processed:{message.Id}";
        if (_memoryCache.TryGetValue(processedKey, out _))
        {
            _logger.LogDebug("Message {MessageId} already processed. Skipping duplicate event", message.Id);
            return;
        }
        _memoryCache.Set(processedKey, true, ProcessedMessageTtl);

        _logger.LogInformation("Message received {MessageId} in channel {ChannelId}", message.Id, message.Channel.Id);

        var references = _messageParser.Parse(message.Content);
        if (!references.HasReferences)
        {
            _logger.LogDebug("Message {MessageId} has no Azure DevOps references", message.Id);
            return;
        }

        _logger.LogInformation(
            "Message {MessageId} references WorkItems={WorkItemIds} PullRequests={PullRequestIds}",
            message.Id,
            string.Join(',', references.WorkItemIds),
            string.Join(',', references.PullRequestIds));

        var workItemTasks = references.WorkItemIds
            .Select(FetchWorkItemSafeAsync)
            .ToArray();

        var pullRequestTasks = references.PullRequestIds
            .Select(FetchPullRequestSafeAsync)
            .ToArray();

        await Task.WhenAll(workItemTasks.Cast<Task>().Concat(pullRequestTasks));

        var lines = new List<string>();

        foreach (var workItem in workItemTasks.Select(task => task.Result).Where(item => item is not null))
        {
            lines.Add($"[#{workItem!.Id}]({workItem.Url}) {workItem.Title}");
        }

        foreach (var pullRequest in pullRequestTasks.Select(task => task.Result).Where(item => item is not null))
        {
            lines.Add($"[!{pullRequest!.Id}]({pullRequest.Url}) {pullRequest.Title} ({pullRequest.RepositoryName})");
        }

        if (lines.Count == 0)
        {
            _logger.LogInformation("Message {MessageId} had references, but no retrievable entities", message.Id);
            return;
        }

        if (lines.Count > 12)
        {
            lines = lines.Take(12).ToList();
            lines.Add("...additional references omitted");
        }

        await message.Channel.SendMessageAsync(
            text: string.Join(Environment.NewLine, lines),
            allowedMentions: AllowedMentions.None);

        _logger.LogInformation("Sent compact response with {LineCount} lines for message {MessageId}", lines.Count, message.Id);
    }

    private async Task OnSlashCommandExecutedAsync(SocketSlashCommand command)
    {
        if (string.Equals(command.CommandName, "review", StringComparison.Ordinal))
        {
            await HandleReviewCommandAsync(command);
            return;
        }

        if (string.Equals(command.CommandName, "workstart", StringComparison.Ordinal) ||
            string.Equals(command.CommandName, "startingwork", StringComparison.Ordinal))
        {
            await HandleStartingWorkCommandAsync(command);
            return;
        }

        if (string.Equals(command.CommandName, "assigntask", StringComparison.Ordinal))
        {
            await HandleAssignTaskCommandAsync(command);
        }
    }

    private async Task HandleReviewCommandAsync(SocketSlashCommand command)
    {
        try
        {
            var taskId = GetRequiredLongOption(command, "task");
            var prsRaw = GetRequiredStringOption(command, "prs");
            var reviewers = GetRequiredStringOption(command, "reviewers");
            var note = GetOptionalStringOption(command, "note");

            var pullRequestIds = ExtractDistinctIds(prsRaw).ToArray();
            if (pullRequestIds.Length == 0)
            {
                await command.RespondAsync("No valid PR IDs were provided in `prs`.", ephemeral: true);
                return;
            }
            var reviewerCount = CountReviewers(reviewers);

            await command.DeferAsync();

            var workItemTask = FetchWorkItemSafeAsync((int)taskId);
            var prTasks = pullRequestIds.Select(FetchPullRequestSafeAsync).ToArray();
            await Task.WhenAll(prTasks.Cast<Task>().Append(workItemTask));

            var workItem = await workItemTask;
            var pullRequests = prTasks.Select(task => task.Result).Where(pr => pr is not null).Cast<PullRequest>().ToList();

            var lines = new List<string>();
            lines.Add("🔎 **Review Request**");
            lines.Add($"**From:** {command.User.Mention}");

            if (workItem is null)
            {
                lines.Add($"**Task:** #{taskId}");
            }
            else
            {
                lines.Add($"**Task:** [#{workItem.Id}]({workItem.Url}) *{workItem.Title}*");
            }

            var reviewerLabel = reviewerCount > 1 ? "Reviewers" : "Reviewer";
            lines.Add($"**{reviewerLabel}:** {reviewers.Trim()}");
            var prLabel = pullRequestIds.Length > 1 ? "PRs" : "PR";
            lines.Add($"**{prLabel}:**");

            if (pullRequests.Count == 0)
            {
                lines.Add($"- {string.Join(", ", pullRequestIds.Select(id => $"!{id}"))}");
            }
            else
            {
                foreach (var pr in pullRequests)
                {
                    var cleanedTitle = CleanPullRequestTitle(pr.Title, (int)taskId);
                    lines.Add($"- {ToRepositoryLabel(pr.RepositoryName)}: [!{pr.Id}]({pr.Url}) {cleanedTitle}");
                }
            }

            if (!string.IsNullOrWhiteSpace(note))
            {
                lines.Add($"**Note:** {note.Trim()}");
            }

            await command.FollowupAsync(
                text: string.Join(Environment.NewLine, lines),
                allowedMentions: new AllowedMentions(AllowedMentionTypes.Users | AllowedMentionTypes.Roles));

            _logger.LogInformation(
                "Generated /review message by {UserId} for Task {TaskId} with {PullRequestCount} PRs",
                command.User.Id,
                taskId,
                pullRequestIds.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process /review command");

            if (!command.HasResponded)
            {
                await command.RespondAsync("Failed to process `/review` command.", ephemeral: true);
            }
            else
            {
                await command.FollowupAsync("Failed to process `/review` command.", ephemeral: true);
            }
        }
    }

    private async Task HandleStartingWorkCommandAsync(SocketSlashCommand command)
    {
        try
        {
            var taskId = GetRequiredLongOption(command, "task");
            var note = GetOptionalStringOption(command, "note");

            await command.DeferAsync();
            var workItem = await FetchWorkItemSafeAsync((int)taskId);

            var lines = new List<string>();
            lines.Add("🚀 **Work Start**");
            lines.Add($"**By:** {command.User.Mention}");

            if (workItem is null)
            {
                lines.Add($"**Task:** #{taskId}");
            }
            else
            {
                lines.Add($"**Task:** [#{workItem.Id}]({workItem.Url}) *{workItem.Title}*");
                lines.Add(
                    $"**Type:** {workItem.WorkItemType} | **Priority:** {workItem.Priority} | **Estimation:** {workItem.TimeEstimation} | **Area:** {TrimArea(workItem.Area)}");
            }

            if (!string.IsNullOrWhiteSpace(note))
            {
                lines.Add($"**Note:** {note.Trim()}");
            }

            await command.FollowupAsync(
                text: string.Join(Environment.NewLine, lines),
                allowedMentions: new AllowedMentions(AllowedMentionTypes.Users | AllowedMentionTypes.Roles));

            _logger.LogInformation("Generated /workstart message by {UserId} for Task {TaskId}", command.User.Id, taskId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process /workstart command");

            if (!command.HasResponded)
            {
                await command.RespondAsync("Failed to process `/workstart` command.", ephemeral: true);
            }
            else
            {
                await command.FollowupAsync("Failed to process `/workstart` command.", ephemeral: true);
            }
        }
    }

    private async Task HandleAssignTaskCommandAsync(SocketSlashCommand command)
    {
        try
        {
            var taskId = GetRequiredLongOption(command, "task");
            var assigneeInput = GetOptionalStringOption(command, "assignee");
            var note = GetOptionalStringOption(command, "note");

            await command.DeferAsync();
            var workItem = await FetchWorkItemSafeAsync((int)taskId);
            var resolvedAssignee = !string.IsNullOrWhiteSpace(assigneeInput)
                ? assigneeInput.Trim()
                : workItem?.Assignee?.Trim();

            var lines = new List<string>();
            lines.Add("📌 **Task Assignment**");
            lines.Add($"**From:** {command.User.Mention}");
            if (!string.IsNullOrWhiteSpace(resolvedAssignee))
            {
                lines.Add($"**Assignee:** {resolvedAssignee}");
            }

            if (workItem is null)
            {
                lines.Add($"**Task:** #{taskId}");
            }
            else
            {
                lines.Add($"**Task:** [#{workItem.Id}]({workItem.Url}) *{workItem.Title}*");
                lines.Add($"**Type:** {workItem.WorkItemType} | **Priority:** {workItem.Priority} | **Area:** {TrimArea(workItem.Area)}");
            }

            if (!string.IsNullOrWhiteSpace(note))
            {
                lines.Add($"**Note:** {note.Trim()}");
            }

            await command.FollowupAsync(
                text: string.Join(Environment.NewLine, lines),
                allowedMentions: new AllowedMentions(AllowedMentionTypes.Users | AllowedMentionTypes.Roles));

            _logger.LogInformation("Generated /assigntask message by {UserId} for Task {TaskId}", command.User.Id, taskId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process /assigntask command");

            if (!command.HasResponded)
            {
                await command.RespondAsync("Failed to process `/assigntask` command.", ephemeral: true);
            }
            else
            {
                await command.FollowupAsync("Failed to process `/assigntask` command.", ephemeral: true);
            }
        }
    }

    private async Task RegisterCommandsAsync()
    {
        var reviewCommand = new SlashCommandBuilder()
            .WithName("review")
            .WithDescription("Create a structured Azure DevOps review request message.")
            .AddOption("task", ApplicationCommandOptionType.Integer, "Task work item ID (e.g. 6176).", isRequired: true)
            .AddOption("prs", ApplicationCommandOptionType.String, "PR IDs, comma or space separated (e.g. 6049, 6050).", isRequired: true)
            .AddOption("reviewers", ApplicationCommandOptionType.String, "Reviewers (mentions, names, roles).", isRequired: true)
            .AddOption("note", ApplicationCommandOptionType.String, "Optional short context note.", isRequired: false)
            .Build();

        var startingWorkCommand = new SlashCommandBuilder()
            .WithName("workstart")
            .WithDescription("Announce that work on a task has started.")
            .AddOption("task", ApplicationCommandOptionType.Integer, "Task work item ID (e.g. 6254).", isRequired: true)
            .AddOption("note", ApplicationCommandOptionType.String, "Optional short context note.", isRequired: false)
            .Build();

        var assignTaskCommand = new SlashCommandBuilder()
            .WithName("assigntask")
            .WithDescription("Create a structured task assignment message.")
            .AddOption("task", ApplicationCommandOptionType.Integer, "Task work item ID (e.g. 6291).", isRequired: true)
            .AddOption("assignee", ApplicationCommandOptionType.String, "Person or role to assign the task to. Optional, defaults to task assignee.", isRequired: false)
            .AddOption("note", ApplicationCommandOptionType.String, "Optional short context note.", isRequired: false)
            .Build();

        var commands = new ApplicationCommandProperties[] { reviewCommand, startingWorkCommand, assignTaskCommand };

        if (_discordOptions.GuildId.HasValue)
        {
            var guild = _discordClient.GetGuild(_discordOptions.GuildId.Value);
            if (guild is not null)
            {
                await guild.BulkOverwriteApplicationCommandAsync(commands);
                await _discordClient.BulkOverwriteGlobalApplicationCommandsAsync(Array.Empty<ApplicationCommandProperties>());
                _logger.LogInformation("Registered commands for guild {GuildId}", _discordOptions.GuildId.Value);
                return;
            }

            _logger.LogWarning(
                "Guild {GuildId} not found in current cache. Falling back to global command registration.",
                _discordOptions.GuildId.Value);
        }

        await _discordClient.BulkOverwriteGlobalApplicationCommandsAsync(commands);
        _logger.LogInformation("Registered global commands /review and /workstart");
    }

    private static long GetRequiredLongOption(SocketSlashCommand command, string optionName)
    {
        var option = command.Data.Options.FirstOrDefault(x => string.Equals(x.Name, optionName, StringComparison.Ordinal));
        if (option?.Value is long value)
        {
            return value;
        }

        throw new InvalidOperationException($"Missing required integer option '{optionName}'.");
    }

    private static string GetRequiredStringOption(SocketSlashCommand command, string optionName)
    {
        var option = command.Data.Options.FirstOrDefault(x => string.Equals(x.Name, optionName, StringComparison.Ordinal));
        if (option?.Value is string value && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException($"Missing required string option '{optionName}'.");
    }

    private static string? GetOptionalStringOption(SocketSlashCommand command, string optionName)
    {
        var option = command.Data.Options.FirstOrDefault(x => string.Equals(x.Name, optionName, StringComparison.Ordinal));
        return option?.Value as string;
    }

    private static IEnumerable<int> ExtractDistinctIds(string input)
    {
        return Regex.Matches(input, @"\d+")
            .Select(match => int.TryParse(match.Value, out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct();
    }

    private static int CountReviewers(string reviewers)
    {
        if (string.IsNullOrWhiteSpace(reviewers))
        {
            return 0;
        }

        var mentionCount = Regex.Matches(reviewers, @"<@!?\d+>|<@&\d+>").Count;
        if (mentionCount > 0)
        {
            return mentionCount;
        }

        var parts = reviewers
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return Math.Max(1, parts.Length);
    }

    private static string CleanPullRequestTitle(string title, int taskId)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "(untitled pull request)";
        }

        var cleaned = title.Trim();
        cleaned = Regex.Replace(cleaned, @"^(?:\s*#\d+\s*)+", string.Empty, RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"^\s*[-:]+\s*", string.Empty, RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"^\s*#"+ taskId + @"\s*", string.Empty, RegexOptions.CultureInvariant);
        cleaned = Regex.Replace(cleaned, @"^\s*[A-Za-z0-9._-]{2,30}\s*:\s*", string.Empty, RegexOptions.CultureInvariant);

        return string.IsNullOrWhiteSpace(cleaned) ? title : cleaned.Trim();
    }

    private static string ToRepositoryLabel(string repositoryName)
    {
        if (string.IsNullOrWhiteSpace(repositoryName))
        {
            return "Repository";
        }

        var tail = repositoryName.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? repositoryName;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tail.Replace('-', ' ').Replace('_', ' '));
    }

    private static string TrimArea(string areaPath)
    {
        if (string.IsNullOrWhiteSpace(areaPath))
        {
            return "N/A";
        }

        var split = areaPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return split.LastOrDefault() ?? areaPath;
    }

    private async Task<WorkItem?> FetchWorkItemSafeAsync(int id)
    {
        try
        {
            return await _azureDevOpsService.GetWorkItemAsync(id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve Work Item {WorkItemId}", id);
            return null;
        }
    }

    private async Task<PullRequest?> FetchPullRequestSafeAsync(int id)
    {
        try
        {
            return await _azureDevOpsService.GetPullRequestAsync(id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve Pull Request {PullRequestId}", id);
            return null;
        }
    }
}
