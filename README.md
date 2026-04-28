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

- `#1234` -> Work Item lookup
- `!5678` -> Pull Request lookup

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

Authorization behavior (applies only to `/assigntask`):
- Layer 1 (Discord native): restrict command access by role/user from Discord server command permissions
- Layer 2 (bot enforcement): optional allowlists in configuration
  - `AllowedRoleIdsCsv`
  - `AllowedUserIdsCsv`
- If both allowlists are empty, bot-side restriction is disabled and Discord native permissions control access
- If any allowlist is populated, requester must match allowed user IDs or role IDs

#### `/reportbug`
Creates a structured bug report and posts it to a dedicated bug-report channel.

Slash command metadata options:
- `title` (required): short summary
- `area` (required): frontend/backend/locale_translations/devops
- `environment` (required): development/qas/production
- `platform` (required): windows/osx/ios/android
- `browser` (optional): chrome/firefox/safari/opera/brave/other
- `version` (required): app/build version
- `instance` (required): customer/instance identifier
- `check_dedupe` (optional, default `true`): pre-check duplicates before modal opens
- `attachment` (optional): single evidence file
- `tag` (optional): mention target (for example `@frontend`)
- `note` (optional): extra context

Modal inputs (multiline):
- Steps to Reproduce (required)
- Expected Behavior (required)
- Actual Behavior (required)

Duplicate-detection behavior:
- Pre-modal dedupe: metadata-level check; if duplicate found, modal does not open
- Post-modal dedupe: full-content check (includes steps/expected/actual)
- Exact and fuzzy matching are both used

---

## ReportBug Settings

`Discord:BugReporting` options:
- `ChannelId`: target bug-report channel id
- `DedupeSimilarityThreshold`: fuzzy similarity threshold (default `0.60`)
- `DedupeMaxCandidates`: max recent records checked for fuzzy dedupe (default `300`)

Quick guidance:
- Lower threshold => more aggressive duplicate detection (more false positives)
- Higher threshold => stricter matching (more false negatives)

Current reportbug constraints:
- Modal fields are constrained by Discord embed limits when rendered:
  - each embed field value max `1024` chars
  - long content is auto-truncated with `…(truncated)`
- Slash command attachment supports **one file** per bug report in current implementation
- Discord slash commands do not support multi-file upload in one option

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
    "GuildId": null,
    "BugReporting": {
      "ChannelId": null,
      "DedupeSimilarityThreshold": 0.60,
      "DedupeMaxCandidates": 300
    },
    "CommandAuthorization": {
      "AssignTask": {
        "AllowedRoleIdsCsv": "",
        "AllowedUserIdsCsv": "",
        "UnauthorizedMessage": "you are not authorized to assign tasks to others."
      }
    }
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
| Bug report channel id | `Discord__BugReporting__ChannelId` |
| Bug dedupe threshold | `Discord__BugReporting__DedupeSimilarityThreshold` |
| Bug dedupe max candidates | `Discord__BugReporting__DedupeMaxCandidates` |
| AssignTask allowed role IDs CSV (optional) | `Discord__CommandAuthorization__AssignTask__AllowedRoleIdsCsv` |
| AssignTask allowed user IDs CSV (optional) | `Discord__CommandAuthorization__AssignTask__AllowedUserIdsCsv` |
| AssignTask unauthorized message (optional) | `Discord__CommandAuthorization__AssignTask__UnauthorizedMessage` |
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
export BUGREPORT_CHANNEL_ID="<bug-report-channel-id>"
export BUGREPORT_DEDUPE_THRESHOLD="0.60"
export BUGREPORT_DEDUPE_MAX_CANDIDATES="300"
export ASSIGNTASK_ALLOWED_ROLE_IDS="<role-id-1,role-id-2>"
export ASSIGNTASK_ALLOWED_USER_IDS="<user-id-1,user-id-2>"
export ASSIGNTASK_UNAUTHORIZED_MESSAGE="you are not authorized to assign tasks to others."
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
$env:BUGREPORT_CHANNEL_ID="<bug-report-channel-id>"
$env:BUGREPORT_DEDUPE_THRESHOLD="0.60"
$env:BUGREPORT_DEDUPE_MAX_CANDIDATES="300"
$env:ASSIGNTASK_ALLOWED_ROLE_IDS="<role-id-1,role-id-2>"
$env:ASSIGNTASK_ALLOWED_USER_IDS="<user-id-1,user-id-2>"
$env:ASSIGNTASK_UNAUTHORIZED_MESSAGE="you are not authorized to assign tasks to others."
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

