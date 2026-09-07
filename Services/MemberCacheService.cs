#region

using AGC_Management.Utils;

#endregion

namespace AGC_Management.Services;

public static class MemberCacheService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);

    public static ulong TargetGuildId => ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["ServerId"]);

    public static Task StartPeriodicRefresh(DiscordClient client, ulong guildId)
    {
        return Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(RefreshInterval);
                await RefreshAsync(client, guildId, "periodic refresh");
            }
        });
    }

    public static async Task RefreshAsync(DiscordClient client, ulong guildId, string reason)
    {
        if (!await Gate.WaitAsync(TimeSpan.Zero))
        {
            CurrentApplication.Logger.Debug($"Member download already running, skipping trigger '{reason}'.");
            return;
        }

        try
        {
            var guild = client.Guilds.TryGetValue(guildId, out var cached)
                ? cached
                : await client.GetGuildAsync(guildId);

            // READY ersetzt alle gecachten Guild-Objekte. Die Globals müssen deshalb auf die
            // aktuelle Instanz zeigen, sonst lesen alle .Members-Zugriffe darauf einen toten Cache.
            GlobalProperties.AGCGuild = guild;
            CurrentApplication.TargetGuild = guild;

            var expected = guild.MemberCount ?? 0;
            CurrentApplication.Logger.Information(
                $"Starting member download ({expected} members expected, trigger: {reason})...");

            if (!await RequestViaGatewayAsync(client, guild, expected))
            {
                CurrentApplication.Logger.Warning(
                    "Gateway member chunking did not complete in time, falling back to REST.");
                await guild.GetAllMembersAsync();
            }

            CurrentApplication.Logger.Information(
                $"Member download complete: cache now holds {guild.Members.Count} members.");
        }
        catch (Exception ex)
        {
            CurrentApplication.Logger.Error(ex, "Member download failed");
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<bool> RequestViaGatewayAsync(DiscordClient client, DiscordGuild guild, int expected)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = 0;

        Task OnChunk(DiscordClient sender, GuildMembersChunkEventArgs e)
        {
            if (e.Nonce != nonce) return Task.CompletedTask;

            var total = Interlocked.Add(ref received, e.Members.Count);
            CurrentApplication.Logger.Information(
                $"Member download progress: chunk {e.ChunkIndex + 1}/{e.ChunkCount}, {total}/{expected} members.");

            if (e.ChunkIndex + 1 >= e.ChunkCount) completion.TrySetResult(true);
            return Task.CompletedTask;
        }

        client.GuildMembersChunked += OnChunk;
        try
        {
            await guild.RequestMembersAsync("", 0, true, nonce: nonce);

            var timeout = TimeSpan.FromSeconds(Math.Clamp(expected / 1000 * 5, 60, 600));
            return await Task.WhenAny(completion.Task, Task.Delay(timeout)) == completion.Task;
        }
        finally
        {
            client.GuildMembersChunked -= OnChunk;
        }
    }
}
