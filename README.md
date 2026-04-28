# Discord Azure DevOps Bot

Production-ready Discord bot in **C# / .NET 8** that resolves Azure DevOps references in chat and provides structured team workflow commands.

## What It Does

- Detects work item references (`#1234`) and pull request references (`!5678`) in Discord messages.
- Fetches Azure DevOps metadata in read-only mode.
- Responds with concise, non-spammy summaries.
- Supports team slash commands:
  - `/review`
  - `/workstart`
  - `/assigntask`

## Architecture

- `Program.cs`: host, DI, configuration, logging, HttpClient policy.
- `Services/DiscordBotService.cs`: Discord events, command handling, message composition.
- `Services/AzureDevOpsService.cs`: read-only Azure DevOps REST integration + cache.
- `Services/MessageParser.cs`: pattern detection and dedupe.
- `Models/*`: options + domain models.
- `Utils/RegexPatterns.cs`: compiled regex patterns.

## Prerequisites

- .NET SDK 8.0+
- Discord bot token
- Azure DevOps PAT with minimum read scopes

## Configuration

Secrets are **not committed**. Use one of these approaches:

1. Local file-based config (recommended for local dev)
2. Environment variables (recommended for container/platform deployments)

### Local Config (Windows/Linux/macOS)

1. Copy template:
   - `cp appsettings.example.json appsettings.json` (Linux/macOS)
   - `Copy-Item appsettings.example.json appsettings.json` (PowerShell)
2. Fill values:
   - `Discord:Token`
   - `Discord:GuildId` (optional but recommended for fast slash-command propagation)
   - `AzureDevOps:Organization`
   - `AzureDevOps:Project`
   - `AzureDevOps:PersonalAccessToken`

## Build and Run

### Windows (PowerShell)

```powershell
cd C:\discord-chatbot
dotnet restore
dotnet build .\discord-chatbot.slnx
dotnet run --project .\discord-chatbot.csproj
```

### Linux/macOS (bash/zsh)

```bash
cd /path/to/discord-chatbot
dotnet restore
dotnet build ./discord-chatbot.slnx
dotnet run --project ./discord-chatbot.csproj
```

## Docker

### 1. Prepare environment file

```bash
cp .env.example .env
```

Set your values in `.env`.

### 2. Start with Docker Compose

```bash
docker compose up -d --build
```

### 3. View logs

```bash
docker compose logs -f discord-azure-bot
```

### 4. Stop

```bash
docker compose down
```

## Environment Variables (for Docker / CI / PaaS)

- `DISCORD_TOKEN`
- `DISCORD_GUILD_ID` (optional)
- `AZDO_ORGANIZATION`
- `AZDO_PROJECT`
- `AZDO_PAT`
- `LOG_LEVEL` (optional, default: `Information`)

## Azure DevOps Access (Read-only)

Required PAT scopes:
- **Work Items**: Read
- **Code**: Read

No write scopes are required.

## Discord Notes

- Enable **Message Content Intent** in Developer Portal for message-content parsing.
- Slash command updates are fastest when `Discord:GuildId` is set.

## Security

- `appsettings.json` is gitignored.
- `.env` is gitignored.
- Rotate tokens immediately if they were ever exposed.

## License

MIT. See [LICENSE](./LICENSE).
