# Discord Azure DevOps Bot

A production-grade **.NET 8** Discord bot for Azure DevOps engineering workflows.

It combines two operating modes:

1. **Reference Resolver** in normal chat messages (`#workItem`, `!pullRequest`)
2. **Structured Slash Commands** for repeatable team communication

---

## Highlights

- Read-only Azure DevOps integration (work items + pull requests)
- Compact, non-spam Discord responses
- Multi-repository PR resolution by PR ID at project scope
- In-memory caching and resilient HTTP retry policy
- Duplicate-event protection to avoid repeated bot messages
- Command-based workflow messaging with consistent formatting

---

## Supported Workflows

### 1) Chat Reference Resolution
When a user posts a message containing Azure DevOps references, the bot enriches it automatically.

- `#1234` ? Work Item lookup
- `!5678` ? Pull Request lookup

Behavior:
- Handles multiple references in one message
- Deduplicates repeated IDs in the same message
- Sends a single compact bot response per source message

### 2) Slash Commands

#### `/review`
Creates a structured review request.

Options:
- `task` (required): work item ID
- `prs` (required): PR IDs (comma/space separated)
- `reviewers` (required): mentions/names/roles
- `note` (optional): context

Formatting:
- Singular/plural-aware labels (`Reviewer`/`Reviewers`, `PR`/`PRs`)
- Repository shown in PR heading
- Leading task/repo prefixes in PR titles cleaned for readability

#### `/workstart`
Publishes a standardized “work started” announcement.

Options:
- `task` (required): work item ID
- `note` (optional): context

Includes:
- announcer
- task link + italicized title
- type, priority, estimation, area

#### `/assigntask`
Publishes a task assignment message.

Options:
- `task` (required): work item ID
- `assignee` (optional): person/role
- `note` (optional): context

Assignee behavior:
- If `assignee` is provided, that value is used
- Otherwise, assignee is pulled from the Azure DevOps task
- If neither exists, assignee line is omitted

---

## Architecture

- `Program.cs` - host bootstrap, DI, config, logging, HTTP resilience
- `Services/DiscordBotService.cs` - Discord gateway/events, commands, message composition
- `Services/AzureDevOpsService.cs` - Azure DevOps REST client + caching
- `Services/MessageParser.cs` - high-performance reference parsing
- `Models/*` - options and domain records
- `Utils/RegexPatterns.cs` - compiled regex patterns
- `tests/discord-chatbot.Tests/*` - parser unit tests

---

## Requirements

- .NET SDK 8+
- Discord bot token
- Azure DevOps PAT

Official setup references:
- Discord app/bot foundation: [Discord Bots & Companion Apps](https://docs.discord.com/developers/bots)
- OAuth scopes and bot permissions: [OAuth2 and Permissions](https://docs.discord.com/developers/platform/oauth2-and-permissions)
- Permission model details: [Discord Permissions Reference](https://docs.discord.com/developers/topics/permissions)
- Azure DevOps PAT lifecycle: [Use personal access tokens (Microsoft Learn)](https://learn.microsoft.com/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate?view=azure-devops-2022)

Azure DevOps PAT minimum scopes:
- **Work Items: Read**
- **Code: Read**

---

## Configuration

Use either `appsettings.json` or environment variables.

### `appsettings.json` shape

```json
{
  "Discord": {
    "Token": "YOUR_DISCORD_BOT_TOKEN",
    "GuildId": null
  },
  "AzureDevOps": {
    "Organization": "your-org",
    "Project": "your-project",
    "PersonalAccessToken": "YOUR_PAT"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

### Environment Variable Mapping

| Setting | Environment Variable |
|---|---|
| Discord token | `Discord__Token` |
| Discord guild id (optional) | `Discord__GuildId` |
| ADO organization | `AzureDevOps__Organization` |
| ADO project | `AzureDevOps__Project` |
| ADO PAT | `AzureDevOps__PersonalAccessToken` |
| Log level | `Logging__LogLevel__Default` |

---

## Build and Run

```bash
dotnet restore
dotnet build ./discord-chatbot.slnx
dotnet run --project ./discord-chatbot.csproj
```

Run tests:

```bash
dotnet test ./tests/discord-chatbot.Tests/discord-chatbot.Tests.csproj
```

---

## Discord Setup Checklist

1. Create a Discord application and bot user:
   - [Discord Bots & Companion Apps](https://docs.discord.com/developers/bots)
   - [OAuth2 and Permissions](https://docs.discord.com/developers/platform/oauth2-and-permissions)
2. Enable **Message Content Intent**
   - Intent behavior reference: [Application Resource Flags](https://docs.discord.com/developers/resources/application)
3. Invite bot with permissions:
   - View Channels
   - Read Message History
   - Send Messages
   - Embed Links
   - Invite/permission flow reference: [OAuth2 and Permissions](https://docs.discord.com/developers/platform/oauth2-and-permissions)
4. (Recommended) set `Discord:GuildId` for fast command propagation

Azure DevOps token guidance:
- Create and scope PATs: [Use personal access tokens](https://learn.microsoft.com/azure/devops/organizations/accounts/use-personal-access-tokens-to-authenticate?view=azure-devops-2022)
- Organizational PAT policy controls: [Manage PATs with policies](https://learn.microsoft.com/en-us/azure/devops/organizations/accounts/manage-pats-with-policies-for-administrators?view=azure-devops)

---

## Docker Deployment

Docker references:
- [Docker Compose Overview](https://docs.docker.com/compose/)
- [Compose Environment Variables](https://docs.docker.com/compose/environment-variables/)

### Option A: `.env` + Docker Compose

1. Create `.env` from template
2. Fill values
3. Start

```bash
docker compose up -d --build
```

Logs:

```bash
docker compose logs -f discord-azure-bot
```

Stop:

```bash
docker compose down
```

### Option B: Explicit shell export (no `.env` file)

**Linux/macOS (bash/zsh):**

```bash
export DISCORD_TOKEN="<token>"
export DISCORD_GUILD_ID="<guild-id>"
export AZDO_ORGANIZATION="<org>"
export AZDO_PROJECT="<project>"
export AZDO_PAT="<pat>"
export LOG_LEVEL="Information"
docker compose up -d --build
```

**Windows PowerShell:**

```powershell
$env:DISCORD_TOKEN="<token>"
$env:DISCORD_GUILD_ID="<guild-id>"
$env:AZDO_ORGANIZATION="<org>"
$env:AZDO_PROJECT="<project>"
$env:AZDO_PAT="<pat>"
$env:LOG_LEVEL="Information"
docker compose up -d --build
```

---

## Operations Notes

- Uses retry policy for transient HTTP failures and rate-limit responses
- Uses in-memory cache for ADO entity lookups (TTL-based)
- Prevents duplicate processing of the same Discord message event
- Keeps command and resolver outputs concise for channel readability

---

## License

MIT License. See [LICENSE](./LICENSE).

