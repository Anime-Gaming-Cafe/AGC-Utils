#region

using System.Collections.Concurrent;
using AGC_Management.Utils;
using DisCatSharp.Exceptions;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     Keeps the member cache of the target guild populated.
///     Discord only ships a handful of members in GUILD_CREATE (large_threshold, 250 at most) and
///     DisCatSharp wipes <c>guild.Members</c> on every GUILD_CREATE before writing the payload into it.
///     READY additionally swaps out all guild objects. So the cache has to be rebuilt after every
///     (re)connect, not just once on startup.
/// </summary>
public static class MemberCacheService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);
    private static readonly ConcurrentDictionary<ulong, byte> PendingCompletions = new();

    public static ulong TargetGuildId => ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["ServerId"]);

    /// <summary>
    ///     Safety net in case a refresh is missed. The actual trigger is GuildAvailable.
    /// </summary>
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

    /// <summary>
    ///     Loads all members of the guild into the cache. Only ever runs once at a time, additional
    ///     triggers while a download is in flight are dropped.
    /// </summary>
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

            // READY replaces every cached guild object, so the globals have to point at the live
            // instance again - otherwise everything reading .Members off them sees a dead cache.
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

    /// <summary>
    ///     Fills in a cache entry that DisCatSharp created from a bare DiscordUser.
    ///     When a GUILD_MEMBER_UPDATE arrives for a member that is not cached yet, its handler builds
    ///     the entry with the <c>DiscordMember(DiscordUser)</c> constructor and then copies only a
    ///     subset of the payload onto it - JoinedAt, PremiumSince and the voice flags stay unset even
    ///     though the gateway sent them. Refetching replaces the entry with a complete one.
    /// </summary>
    public static async Task EnsureCompleteAsync(DiscordGuild guild, DiscordMember? member)
    {
        // A real member always has a join date, so this doubles as the "entry is incomplete" check.
        if (member is null || member.IsBot || member.JoinedAt != default) return;
        if (!PendingCompletions.TryAdd(member.Id, 0)) return;

        try
        {
            await guild.GetMemberAsync(member.Id, true);
            CurrentApplication.Logger.Debug($"Completed partial cache entry for member {member.Id}.");
        }
        catch (NotFoundException)
        {
            // Member left between the update and the fetch, nothing to complete.
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Failed to complete cache entry for member {MemberId}", member.Id);
        }
        finally
        {
            PendingCompletions.TryRemove(member.Id, out _);
        }
    }

    /// <summary>
    ///     Requests all members over the gateway (OP 8). Considerably cheaper than the REST pagination
    ///     since the chunks arrive over the existing socket and do not consume a rate limit.
    /// </summary>
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

            // Discord sends 1000 members per chunk, a few seconds per chunk is plenty.
            var timeout = TimeSpan.FromSeconds(Math.Clamp(expected / 1000 * 5, 60, 600));
            return await Task.WhenAny(completion.Task, Task.Delay(timeout)) == completion.Task;
        }
        finally
        {
            client.GuildMembersChunked -= OnChunk;
        }
    }
}
