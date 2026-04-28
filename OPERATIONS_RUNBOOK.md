# Discord-Azure DevOps Bot Operations Runbook

## 1. Purpose
This runbook describes how to deploy and operate the Discord bot in a Docker-based infrastructure, including network requirements, security controls, persistence, and production validation.

---

## 2. Service Overview
- Runtime: `.NET 8` container
- Deployment model: Docker Compose service
- Traffic model: outbound-only
- Integrations:
  - Discord API/Gateway
  - Azure DevOps REST API

Core capabilities:
- Chat reference resolution for `#workItem` and `!pullRequest`
- Slash commands:
  - `/review`
  - `/workstart`
  - `/assigntask`
  - `/reportbug`

---

## 3. Prerequisites

### 3.1 Platform
- Docker Engine
- Docker Compose plugin
- Host clock synchronization (NTP)

### 3.2 Required Credentials
Provided by application owner:
- Discord bot token
- Discord guild/server ID (recommended)
- Azure DevOps organization name
- Azure DevOps project name
- Azure DevOps PAT

Minimum Azure DevOps PAT scopes:
- Work Items: Read
- Code: Read

### 3.3 Discord Prerequisites
- Bot invited to target server
- Bot permissions in target channels:
  - View Channels
  - Read Message History
  - Send Messages
  - Embed Links
- **Message Content Intent enabled** in Discord Developer Portal

---

## 4. Network & Firewall Requirements

### 4.1 Outbound Access
Allow outbound `443/TCP` to:
- `discord.com`
- `*.discord.com`
- `dev.azure.com`

If corporate proxy or TLS inspection is used, ensure container can complete TLS handshakes for these endpoints.

### 4.2 Inbound Access
- None required
- Do not expose service ports

### 4.3 DNS
- Container DNS resolution must work for Discord and Azure endpoints

---

## 5. Required Files
From repository root:
- `Dockerfile`
- `docker-compose.yml`
- `.env.example`
- (reference) `appsettings.example.json`

Runtime generated persistence file:
- `/app/data/bug-reports-store.json`

---

## 6. Configuration

### 6.1 Create Runtime Environment File
Create `.env` from `.env.example` and set values.

Required:
- `DISCORD_TOKEN`
- `DISCORD_GUILD_ID` (recommended)
- `BUGREPORT_CHANNEL_ID` (if `/reportbug` enabled)
- `AZDO_ORGANIZATION`
- `AZDO_PROJECT`
- `AZDO_PAT`

Optional:
- `BUGREPORT_DEDUPE_THRESHOLD` (default: `0.60`)
- `BUGREPORT_DEDUPE_MAX_CANDIDATES` (default: `300`)
- `ASSIGNTASK_ALLOWED_ROLE_IDS`
- `ASSIGNTASK_ALLOWED_USER_IDS`
- `ASSIGNTASK_UNAUTHORIZED_MESSAGE`
- `LOG_LEVEL` (default: `Information`)

### 6.2 File Protection
- Restrict `.env` read access to infrastructure operators only
- Do not place plaintext secrets in source control

---

## 7. Recommended Compose Configuration
Use this baseline and align to internal standards:

```yaml
services:
  discord-azure-bot:
    build:
      context: .
      dockerfile: Dockerfile
    container_name: discord-azure-bot
    restart: unless-stopped
    environment:
      Discord__Token: ${DISCORD_TOKEN}
      Discord__GuildId: ${DISCORD_GUILD_ID}
      Discord__BugReporting__ChannelId: ${BUGREPORT_CHANNEL_ID}
      Discord__BugReporting__DedupeSimilarityThreshold: ${BUGREPORT_DEDUPE_THRESHOLD:-0.60}
      Discord__BugReporting__DedupeMaxCandidates: ${BUGREPORT_DEDUPE_MAX_CANDIDATES:-300}
      Discord__CommandAuthorization__AssignTask__AllowedRoleIdsCsv: ${ASSIGNTASK_ALLOWED_ROLE_IDS:-}
      Discord__CommandAuthorization__AssignTask__AllowedUserIdsCsv: ${ASSIGNTASK_ALLOWED_USER_IDS:-}
      Discord__CommandAuthorization__AssignTask__UnauthorizedMessage: ${ASSIGNTASK_UNAUTHORIZED_MESSAGE:-you are not authorized to assign tasks to others.}
      AzureDevOps__Organization: ${AZDO_ORGANIZATION}
      AzureDevOps__Project: ${AZDO_PROJECT}
      AzureDevOps__PersonalAccessToken: ${AZDO_PAT}
      Logging__LogLevel__Default: ${LOG_LEVEL:-Information}
      Logging__LogLevel__Microsoft: Warning
      Logging__LogLevel__Microsoft.Hosting.Lifetime: Information
    volumes:
      - ./bot-data:/app/data
```

---

## 8. Deployment Procedure

### Step 1: Prepare Host Directory
- Pull latest repository revision
- Verify target path ownership/permissions

### Step 2: Create `.env`
- Copy `.env.example` to `.env`
- Populate runtime values

### Step 3: Validate Egress Connectivity
From host or utility container:
- `curl -I https://discord.com`
- `curl -I https://dev.azure.com`

### Step 4: Start Service
```bash
docker compose up -d --build
```

### Step 5: Verify Runtime
```bash
docker compose ps
docker compose logs -f discord-azure-bot
```

Expected logs:
- Host started
- Discord gateway connected
- Bot ready and command registration completed

---

## 9. Functional Validation Checklist
In Discord test channel:

1. Chat resolver
- Post message with `#<workitemId>` and `!<prId>`
- Verify compact bot response

2. `/review`
- Validate formatting and PR/work item enrichment

3. `/workstart`
- Validate title/type/priority/area enrichment

4. `/assigntask`
- Validate allowed and denied users/roles

5. `/reportbug`
- Validate metadata intake
- Validate modal for steps/expected/actual
- Validate dedupe behavior (pre-modal and post-modal)
- Validate evidence attachment posting

---

## 10. Persistence Validation
- Trigger at least one `/reportbug`
- Confirm `./bot-data/bug-reports-store.json` exists on host
- Restart service
- Re-test dedupe; confirm previous report is still considered

---

## 11. `/reportbug` Specific Notes

### 11.1 Dedupe Tuning
- `BUGREPORT_DEDUPE_THRESHOLD`
  - lower value => more aggressive duplicate detection
  - higher value => stricter matching
- `BUGREPORT_DEDUPE_MAX_CANDIDATES`
  - number of recent candidate records checked for fuzzy duplicate matching

### 11.2 Current Practical Limits
- Modal text eventually rendered into embed fields
- Discord embed field value max is `1024` characters
- Long text is truncated with `�(truncated)`
- Slash command attachment currently supports one evidence file per report in this implementation

---

## 12. Operations & Security
- Rotate Discord token and Azure PAT according to policy
- Centralize container logs into observability platform
- Alert on restart loops and repeated API failures
- Keep `.env` and host volume data access restricted

---

## 13. Troubleshooting

### Bot starts but does not answer
- Verify token validity
- Verify Message Content Intent
- Verify channel permissions

### Slash commands not visible
- Ensure `DISCORD_GUILD_ID` is configured
- Wait for command propagation if global fallback used

### Azure references fail
- Validate PAT scopes and PAT owner access
- Validate outbound access to `dev.azure.com`

### Dedupe not working as expected
- Verify `BUGREPORT_CHANNEL_ID` is correct
- Verify `/app/data` volume mounted and writable
- Validate dedupe threshold tuning

---

## 14. Acceptance Criteria
Deployment accepted when:
- Container remains healthy and restart policy is active
- Outbound connectivity to Discord and Azure DevOps verified
- No inbound port exposure
- All credentials provided via runtime environment
- Dedupe persistence verified across restart
- All command and resolver smoke tests pass
