using Discord;
using Discord.WebSocket;
using discord_chatbot.Models;
using discord_chatbot.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using System.Net.Http.Headers;
using System.Text;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services
    .AddOptions<DiscordOptions>()
    .Bind(builder.Configuration.GetSection(DiscordOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Token), "Discord token is required.")
    .ValidateOnStart();

builder.Services
    .AddOptions<AzureDevOpsOptions>()
    .Bind(builder.Configuration.GetSection(AzureDevOpsOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Organization), "Azure DevOps organization is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Project), "Azure DevOps project is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.PersonalAccessToken), "Azure DevOps PAT is required.")
    .ValidateOnStart();

builder.Services.AddSingleton(_ => new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
    AlwaysDownloadUsers = false,
    LogGatewayIntentWarnings = false
}));

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IMessageParser, MessageParser>();
builder.Services.AddSingleton<IAzureDevOpsService, AzureDevOpsService>();
builder.Services.AddHostedService<DiscordBotService>();

builder.Services.AddHttpClient("AzureDevOps", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<AzureDevOpsOptions>>().Value;
    client.BaseAddress = new Uri($"https://dev.azure.com/{options.Organization}/");
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

    var patBytes = Encoding.ASCII.GetBytes($":{options.PersonalAccessToken}");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(patBytes));
})
.AddPolicyHandler(GetRetryPolicy());

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    options.SingleLine = true;
});

await builder.Build().RunAsync();

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(response => (int)response.StatusCode == 429)
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
}
