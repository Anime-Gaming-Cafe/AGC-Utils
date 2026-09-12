# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

One .NET 10 ASP.NET Core project (`AGC Management.csproj`) that runs **two things in a single process**:

- a Discord bot built on **DisCatSharp** (CommandsNext prefix commands + application/slash commands + event handlers)
- an optional **Blazor Server** dashboard ("WebUI") for the AGC Discord team

There is no second project and no test project (CI runs `dotnet test`, but it finds nothing).

## Commands

```bash
dotnet restore
dotnet build                                   # or --configuration Release, as CI does
dotnet run                                     # needs a config.ini in the working directory
dotnet publish -c Release -r linux-x64 --self-contained false -o out
docker build -t agc-utils .                    # multi-stage; also pulls DiscordChatExporter CLI
docker compose up                              # bot + apache serving ticket transcripts
```

Health endpoints when the WebUI is on: `/health/live`, `/health/ready` (readiness = `DiscordBotService.IsReady`).

## Configuration

`config.ini` in the working directory is the **primary** config — not `appsettings.json` (that only carries ASP.NET log levels). It is gitignored; `exampleconfig.ini` is the template with every section the code reads (`MainConfig`, `ServerConfig`, `WebUI`, `DatabaseCfg`, `TicketConfig`, `TempVC`, `ImageStore`, …).

- Read it via `BotConfig.GetConfig()["Section"]["Key"]`. Every call re-parses the file from disk, and a missing key throws — most call sites wrap it in `try/catch` with a fallback. Follow that pattern.
- `MainConfig.DebugMode = true` switches to `Discord_API_Token_DEB` and the `DatabaseCfgDBG` section, and makes most permission checks return `true`.
- Settings that must be changeable at runtime live in the `botsettings` **table**, not the ini — use `RuntimeSettings.GetAsync/SetAsync` (30 s in-memory TTL). Defaults are seeded in `DatabaseService.InitBotSettings`.

## Architecture

`Program.cs` builds the `WebApplication`, registers services, initializes the DB schema, then starts the bot as a hosted service and the WebUI as a fire-and-forget task.

**Two global statics are the ambient context everywhere** — `CurrentApplication` (DiscordClient, TargetGuild, Logger, ServiceProvider, VersionString) and `GlobalProperties` (role ids and other config-derived constants, read once at first access). Services are usually resolved ad hoc via `CurrentApplication.ServiceProvider.GetRequiredService<T>()` rather than constructor injection.

**`Services/DiscordBotService.cs` (`IHostedService`) owns the bot.** It configures the `DiscordClient`, wires up error/logging handlers, and — importantly — does all registration by **assembly scan**:

- `commands.RegisterCommands(Assembly.GetExecutingAssembly())` → any `BaseCommandModule` subclass with `[Command]` methods
- `appCommands.RegisterGlobalCommands(...)` → any `ApplicationCommandsModule` subclass with `[SlashCommand]` / `[SlashCommandGroup]`
- `discord.RegisterEventHandlers(...)` → any class marked `[EventHandler]` with `[Event]` methods

So new commands and listeners are picked up just by existing in the right shape; there is no registry file to update. The one exception is **background loops**, which must be added by hand to `DiscordBotService.StartTasks` (see `Tasks/`).

### Database

Raw **Npgsql**, no EF Core, no migration tool. Get a connection with
`CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>()` and `con.CreateCommand(sql)` with named parameters.

The schema is code, in `Services/DatabaseService.cs`:

- `InitializeAndUpdateDatabaseTables()` — a dict of `CREATE TABLE IF NOT EXISTS` statements, run on every startup
- `UpdateTables()` — a dict of `ALTER TABLE … ADD COLUMN IF NOT EXISTS` / `CREATE UNIQUE INDEX IF NOT EXISTS` statements, the de-facto migration list

**Adding a column means editing both**: the `CREATE TABLE` (for fresh databases) *and* an `ADD COLUMN IF NOT EXISTS` entry (for existing ones). `DatabaseService` also has generic `InsertDataIntoTable` / `SelectDataFromTable` / `DeleteDataFromTable` helpers, but most feature code writes SQL directly.

### Permission checks

Check attributes live in `Utils/AttributeHelper.cs`, namespace `AGC_Management.Attributes`. CommandsNext and application commands need *different* base types:

- prefix commands → `CheckBaseAttribute` (`RequireStaffRole`, `RequireTeamCat`, `RequireDatabase`, `RequireBotOwner`, `TicketRequireStaffRole`, `RequireOpenTicket`, …)
- slash commands → `ApplicationCommandCheckBaseAttribute` (`ApplicationCommandRequireModerationTeam`)

Failed slash-command checks are swallowed and answered with an ephemeral error embed in `Discord_SlashCommandErrored`.

### Web dashboard

Discord OAuth2 (`Discord.OAuth2.AspNetCore`) → cookie auth. `AuthUtils.ResolveAccessLevelAsync` maps the user's guild roles to an `Enums/Web/AccessLevel` (`Helpers/AuthUtils.cs`'s `RoleMappings`, config-key-driven); `Helpers/DashboardRoleClaimsTransformation.cs` (an `IClaimsTransformation`, 20s TTL cache per user) keeps the `ClaimTypes.Role` claim fresh so role changes take effect without re-login. Razor pages gate with `@attribute [Authorize(Policy = DashboardPolicies.XYZ)]` and `<AuthorizeView Policy="@DashboardPolicies.XYZ">` — the 7 access combinations live once as named policies in `Helpers/DashboardPolicies.cs`/`Program.cs`'s `AddAuthorization`, built from `Enums/Web/AccessLevels.AtLeast(...)` plus one explicit union for the non-hierarchical event-management branch. Blazor Server; `_Host` is the fallback page. Toasts and the "unsaved changes" bar are homegrown (`Services/ToastService.cs`, `Services/UnsavedChangesTracker.cs`, `Pages/SharedPages/ToastHost.razor`, `Pages/SharedPages/UnsavedChangesBar.razor`), no BlazorBootstrap. The WebUI runs only when `[WebUI] Active = true`; `Program.RunAspAsync` also rewrites `Request.Host`/`Scheme` from `DashboardURL`/`UseHttps` so the OAuth `redirect_uri` is correct behind a proxy.

If you work on the Web UI, stick to `.claude/rules/ui-anti-slop.md`!

### Member cache

Discord only ships a subset of members and DisCatSharp clears `guild.Members` on every `GUILD_CREATE`, so `MemberCacheService` re-downloads the whole guild after each (re)connect and **re-points `CurrentApplication.TargetGuild` / `GlobalProperties.AGCGuild` at the live guild object**. Read members through those globals; don't cache a `DiscordGuild` reference of your own.

### Feature subsystems

`Commands/`, `Eventlistener/`, `Tasks/`, and `Utils/` are each split by feature, and a feature usually spans all of them (e.g. levelsystem = `Commands/Levelsystem` + `Eventlistener/Levelsystem` + `Tasks/Levelsystem` + `Utils/LevelUtils.cs`). Notable ones: moderation (warns/bans/flags with case ids), ticket system (transcripts exported via the bundled `tools/exporter/DiscordChatExporter.Cli`), temp voice channels, level system (SkiaSharp rank cards), booster colors, application system, and **extra permissions** — condition-driven role grants where `ExtraPermissionService.ResolveAsync` is the single decision point (manual override beats automatic evaluation; `Untouched` means "leave the role alone").

## Conventions

- **User-facing strings are German; code and comments are English.** Keep new comments sparse and only where the logic is non-obvious. Avoid comments in the code.
- File-scoped namespaces; usings wrapped in `#region` blocks at the top of files; common usings are in `Utils/GlobalUsings.cs`.
- Discord ids are `ulong` in C# and `BIGINT` in Postgres — cast with `(long)id` when parameterizing.
- Errors reach the dev via `ErrorReporting.SendErrorToDev` (posts to `ErrorTrackingChannelId`). There is no Sentry; DisCatSharp only pulls the package in transitively. Logging is Serilog (`CurrentApplication.Logger`) to console and `logs/`.

## Release / deploy

`.github/workflows/release-application.yml` (manual dispatch) tags, publishes zips, and pushes a GHCR image; the version comes from the `GIT_TAG_VERSION` build arg baked into `AssemblyInformationalVersion` and surfaced as `CurrentApplication.VersionString`. The Dockerfile manually copies `blazor.server.js` out of the NuGet cache (the publish target misses it for this project shape) — don't remove that step. `k8s/` holds the kustomize manifests; `datamigration.md` documents the baremetal → Kubernetes data move.
