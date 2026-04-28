using discord_chatbot.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace discord_chatbot.Services;

public interface IAzureDevOpsService
{
    Task<WorkItem?> GetWorkItemAsync(int id, CancellationToken cancellationToken);

    Task<PullRequest?> GetPullRequestAsync(int id, CancellationToken cancellationToken);
}

public sealed class AzureDevOpsService : IAzureDevOpsService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(7);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly AzureDevOpsOptions _options;
    private readonly ILogger<AzureDevOpsService> _logger;

    public AzureDevOpsService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<AzureDevOpsOptions> options,
        ILogger<AzureDevOpsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public Task<WorkItem?> GetWorkItemAsync(int id, CancellationToken cancellationToken)
    {
        return GetOrCreateAsync($"ado:wi:{id}", () => FetchWorkItemAsync(id, cancellationToken), cancellationToken);
    }

    public Task<PullRequest?> GetPullRequestAsync(int id, CancellationToken cancellationToken)
    {
        return GetOrCreateAsync($"ado:pr:{id}", () => FetchPullRequestAsync(id, cancellationToken), cancellationToken);
    }

    private async Task<T?> GetOrCreateAsync<T>(string cacheKey, Func<Task<T?>> valueFactory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_cache.TryGetValue(cacheKey, out CacheItem<T>? cachedItem) && cachedItem is not null)
        {
            return cachedItem.HasValue ? cachedItem.Value : default;
        }

        var value = await valueFactory();
        _cache.Set(cacheKey, new CacheItem<T>(value), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl
        });

        return value;
    }

    private async Task<WorkItem?> FetchWorkItemAsync(int id, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("AzureDevOps");
        var requestUri = $"{Uri.EscapeDataString(_options.Project)}/_apis/wit/workitems/{id}?api-version=7.0";

        _logger.LogInformation("Requesting Work Item {WorkItemId} from Azure DevOps", id);

        using var response = await client.GetAsync(requestUri, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Work Item {WorkItemId} was not found", id);
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("fields", out var fields))
        {
            throw new InvalidOperationException($"Work item {id} response did not contain 'fields'.");
        }

        var title = TryGetString(fields, "System.Title") ?? "(untitled work item)";
        var workItemType = TryGetString(fields, "System.WorkItemType") ?? "Unknown";
        var priority = TryGetNumberOrString(fields, "Microsoft.VSTS.Common.Priority") ?? "N/A";
        var timeEstimation = TryGetNumberOrString(fields, "Microsoft.VSTS.Scheduling.OriginalEstimate")
            ?? TryGetNumberOrString(fields, "Microsoft.VSTS.Scheduling.RemainingWork")
            ?? TryGetNumberOrString(fields, "Microsoft.VSTS.Scheduling.Effort")
            ?? "N/A";
        var area = TryGetString(fields, "System.AreaPath") ?? "N/A";
        var assignee = TryGetIdentityDisplayName(fields, "System.AssignedTo");

        var url = $"https://dev.azure.com/{_options.Organization}/{_options.Project}/_workitems/edit/{id}";
        return new WorkItem(id, title, url, workItemType, priority, timeEstimation, area, assignee);
    }

    private async Task<PullRequest?> FetchPullRequestAsync(int id, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("AzureDevOps");
        var requestUri = $"{Uri.EscapeDataString(_options.Project)}/_apis/git/pullrequests/{id}?api-version=7.1";

        _logger.LogInformation("Requesting Pull Request {PullRequestId} from Azure DevOps", id);

        using var response = await client.GetAsync(requestUri, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Pull Request {PullRequestId} was not found", id);
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var title = document.RootElement.GetProperty("title").GetString() ?? "(untitled pull request)";
        var repositoryName = document.RootElement
            .GetProperty("repository")
            .GetProperty("name")
            .GetString() ?? "(unknown-repository)";

        var escapedRepoName = Uri.EscapeDataString(repositoryName);
        var url = $"https://dev.azure.com/{_options.Organization}/{_options.Project}/_git/{escapedRepoName}/pullrequest/{id}";
        return new PullRequest(id, title, repositoryName, url);
    }

    private sealed record CacheItem<T>(T? Value)
    {
        public bool HasValue { get; } = Value is not null;
    }

    private static string? TryGetString(JsonElement fields, string fieldName)
    {
        if (!fields.TryGetProperty(fieldName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static string? TryGetNumberOrString(JsonElement fields, string fieldName)
    {
        if (!fields.TryGetProperty(fieldName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out var intValue)
                ? intValue.ToString()
                : property.TryGetDouble(out var doubleValue)
                    ? doubleValue.ToString("0.##")
                    : property.ToString(),
            JsonValueKind.String => property.GetString(),
            _ => property.ToString()
        };
    }

    private static string? TryGetIdentityDisplayName(JsonElement fields, string fieldName)
    {
        if (!fields.TryGetProperty(fieldName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        if (property.ValueKind == JsonValueKind.Object)
        {
            if (property.TryGetProperty("displayName", out var displayName) && displayName.ValueKind == JsonValueKind.String)
            {
                return displayName.GetString();
            }

            if (property.TryGetProperty("uniqueName", out var uniqueName) && uniqueName.ValueKind == JsonValueKind.String)
            {
                return uniqueName.GetString();
            }
        }

        return null;
    }
}
