#region

using System.Text;
using AGC_Management.Entities;
using AGC_Management.Entities.Leveling;
using AGC_Management.Entities.Web;
using AGC_Management.Enums.LevelSystem;

#endregion

namespace AGC_Management.Utils;

public static class LevelUtils
{
    private static readonly ulong levelguildid = ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["ServerId"]);

    public static List<WebLeaderboardData> _leaderboardData = new();
    public static List<WebLeaderboardData> cachedLeaderboardData = new();
    public static string? CacheDate = "";
    public static bool LeaderboardDataLoaded;

    public static async Task RunLeaderboardUpdate()
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        while (true)
        {
            CurrentApplication.Logger.Information("Updating leaderboard data...");
            await RetrieveLeaderboardData();
            await LoadLeaderboardData();
            LeaderboardDataLoaded = true;
            CacheDate = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            CurrentApplication.Logger.Information("Updated leaderboard data.");
            await Task.Delay(TimeSpan.FromMinutes(15));
        }
    }


    public static async Task RetrieveLeaderboardData()
    {
        var leaderboardData = new List<WebLeaderboardData>();
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        var cmd = db.CreateCommand(
            "SELECT userid, current_xp, current_level FROM levelingdata ORDER BY current_xp DESC");
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                var percent = 0;

                var userId = reader.GetInt64(0);
                var xp = reader.GetInt32(1);
                var level = reader.GetInt32(2);

                var xpForLevel = XpForLevel(level);
                var xpForNextLevel = XpForLevel(level + 1);
                var xpForThisLevel = xpForNextLevel - xpForLevel;
                var xpForThisLevelPercent = xp / xpForThisLevel * 100;
                percent = xpForThisLevelPercent;

                leaderboardData.Add(new WebLeaderboardData
                {
                    UserId = (ulong)userId, Experience = Converter.FormatWithCommas(xp), Level = level,
                    ProgressInPercent = percent
                });
            }
        else
            leaderboardData.Add(new WebLeaderboardData { UserId = 0, Experience = "0", Level = 0 });

        await reader.CloseAsync();

        _leaderboardData = leaderboardData;
        await Task.CompletedTask;
    }


    private static async Task LoadLeaderboardData()
    {
        var leaderboard = _leaderboardData;
        var tempLeaderboardData = new HashSet<WebLeaderboardData>();
        var tasks = leaderboard.Select(async (x, i) =>
        {
            var isOnServer = await ToolSet.IsUserOnServer(x.UserId);
            var isCached = await ToolSet.IsUserInCache(x.UserId);
            string avatarUrl, username;

            if (isOnServer)
            {
                var user = await CurrentApplication.DiscordClient.GetUserAsync(x.UserId);
                avatarUrl = user.AvatarUrl;
                username = user.Username;
            }
            else if (isCached)
            {
                var user = CurrentApplication.DiscordClient.UserCache[x.UserId];
                avatarUrl = user.AvatarUrl;
                username = user.Username;
            }
            else
            {
                var fallbackUser = ToolSet.GetFallbackUser(x.UserId);
                avatarUrl = fallbackUser.Avatar;
                username = fallbackUser.UserName;
            }

            return new WebLeaderboardData
            {
                Avatar = avatarUrl,
                UserId = x.UserId,
                Username = username,
                Level = x.Level,
                Experience = x.Experience,
                Rank = i + 1,
                ProgressInPercent = x.ProgressInPercent
            };
        });

        foreach (var task in tasks)
        {
            var result = await task;
            tempLeaderboardData.Add(result);
        }

        cachedLeaderboardData = tempLeaderboardData.ToList();

        await Task.CompletedTask;
    }

    /// <summary>
    ///     Calculates the experience points required to reach a given level.
    /// </summary>
    /// <param name="lvl">The level to calculate the experience points for.</param>
    /// <returns>The experience points required to reach the given level.</returns>
    public static int XpForLevel(int lvl)
    {
        if (lvl <= 0) return 0;

        var alvl = lvl - 1;
        return (int)(5 / 6.0 * (151 * alvl + 33 * Math.Pow(alvl, 2) + 2 * Math.Pow(alvl, 3)) + 100);
    }

    public static int XpForNextLevel(int lvl)
    {
        lvl += 1;
        var alvl = lvl;
        return (int)(5 / 6.0 * (151 * alvl + 33 * Math.Pow(alvl, 2) + 2 * Math.Pow(alvl, 3)) + 100);
    }

    public static int XpToFinishLevel(int xp)
    {
        var level = LevelAtXp(xp);
        var xpForNextLevel = XpForLevel(level + 1);
        return xpForNextLevel - xp;
    }

    /// <summary>
    ///     Updates the level roles for a member.
    /// </summary>
    /// <param name="member">The DiscordMember whose level roles need to be updated.</param>
    public static async Task UpdateLevelRoles(DiscordMember? member)
    {
        // Check if member is null
        if (member == null) return;

        if (member.IsBot) return;

        if (member.IsPending is true) return;


        try
        {
            var rank = await GetRank(member.Id);
            var level = rank[member.Id].Level;
            var rewards = await GetLevelRewards();


            var currentRoles = new HashSet<ulong>(member.Roles.Select(role => role.Id));

            foreach (var reward in rewards)
                if (currentRoles.Contains(reward.RoleId) && level < reward.Level)
                    await member.RevokeRoleAsync(CurrentApplication.TargetGuild.GetRole(reward.RoleId));
                else if (!currentRoles.Contains(reward.RoleId) && level >= reward.Level)
                    await member.GrantRoleAsync(CurrentApplication.TargetGuild.GetRole(reward.RoleId));
        }
        catch (Exception e)
        {
            await ErrorReporting.SendErrorToDev(CurrentApplication.DiscordClient, member, e);
        }
    }

    public static async Task<bool> IsRewardLevel(int level)
    {
        var rewards = await GetLevelRewards();
        foreach (var reward in rewards)
            if (reward.Level == level)
                return true;

        return false;
    }

    public static async Task<bool> IsLevelingEnabled(XpRewardType type)
    {
        if (type == XpRewardType.Message)
        {
            var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
            var cmd = db.CreateCommand("SELECT text_active FROM levelingsettings WHERE guildid = @guildid");
            cmd.Parameters.AddWithValue("@guildid", (long)levelguildid);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (reader.HasRows)
                while (await reader.ReadAsync())
                {
                    var messageLevelingEnabled = reader.GetBoolean(0);
                    return messageLevelingEnabled;
                }
            else
                return false;
        }

        if (type == XpRewardType.Voice)
        {
            var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
            var cmd = db.CreateCommand("SELECT vc_active FROM levelingsettings WHERE guildid = @guildid");
            cmd.Parameters.AddWithValue("@guildid", (long)levelguildid);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (reader.HasRows)
                while (await reader.ReadAsync())
                {
                    var vcLevelingEnabled = reader.GetBoolean(0);
                    return vcLevelingEnabled;
                }
            else
                return false;
        }

        return false;
    }

    public static async Task<string> GetLevelMultiplier(XpRewardType type)
    {
        try
        {
            // Check for active timed multiplier first
            var timedMultiplier = await GetActiveTimedMultiplier(type);
            if (timedMultiplier != null)
            {
                var remainingSeconds = timedMultiplier.ExpiryTimestamp - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (remainingSeconds > 0)
                {
                    var remainingTime = TimeSpan.FromSeconds(remainingSeconds);
                    var timeStr = remainingTime.TotalHours >= 1 
                        ? $"{(int)remainingTime.TotalHours}h {remainingTime.Minutes}m"
                        : $"{remainingTime.Minutes}m {remainingTime.Seconds}s";
                    return $"{timedMultiplier.Multiplier} (Timed: {timeStr} remaining)";
                }
            }
        }
        catch (Exception ex)
        {
            CurrentApplication.Logger.Warning(ex, "Error getting timed multiplier info for {Type}", type);
        }
        
        if (type == XpRewardType.Message)
        {
            var multiplier = await GetMessageXpMultiplier();
            if (multiplier == 0) return "Deaktiviert";

            return multiplier.ToString();
        }

        if (type == XpRewardType.Voice)
        {
            var multiplier = await GetVcXpMultiplier();
            if (multiplier == 0) return "Deaktiviert";

            return multiplier.ToString();
        }

        return "Deaktiviert";
    }


    public static string GetLeveltypeString(XpRewardType type)
    {
        switch (type)
        {
            case XpRewardType.Message:
                return "text";
            case XpRewardType.Voice:
                return "vc";
            default:
                return "Unbekannt";
        }
    }

    public static async Task SetMultiplier(XpRewardType rewardType, float multiplicator)
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        if (multiplicator == 0)
        {
            // var {type}_active 
            // eg. vc_active or text_active
            var cmd = db.CreateCommand(
                $"UPDATE levelingsettings SET {GetLeveltypeString(rewardType)}_active = @active WHERE guildid = @guildid");
            cmd.Parameters.AddWithValue("@active", false);
            cmd.Parameters.AddWithValue("@guildid", (long)levelguildid);
            await cmd.ExecuteNonQueryAsync();
            return;
        }

        // set multi and type_active
        var cmd2 = db.CreateCommand(
            $"UPDATE levelingsettings SET {GetLeveltypeString(rewardType)}_multi = @multi, {GetLeveltypeString(rewardType)}_active = @active WHERE guildid = @guildid");
        cmd2.Parameters.AddWithValue("@multi", multiplicator);
        cmd2.Parameters.AddWithValue("@active", true);
        cmd2.Parameters.AddWithValue("@guildid", (long)levelguildid);
        await cmd2.ExecuteNonQueryAsync();
    }

    public static float GetFloatFromMultiplicatorItem(MultiplicatorItem multiplicatorItem)
    {
        if (multiplicatorItem == MultiplicatorItem.Disabled) return 0;

        if (multiplicatorItem == MultiplicatorItem.Quarter) return 0.25f;

        if (multiplicatorItem == MultiplicatorItem.Half) return 0.5f;

        if (multiplicatorItem == MultiplicatorItem.One) return 1.0f;

        if (multiplicatorItem == MultiplicatorItem.OneAndHalf) return 1.5f;

        if (multiplicatorItem == MultiplicatorItem.Two) return 2.0f;

        if (multiplicatorItem == MultiplicatorItem.Three) return 3.0f;

        if (multiplicatorItem == MultiplicatorItem.Four) return 4.0f;

        if (multiplicatorItem == MultiplicatorItem.Five) return 5.0f;

        return 0;
    }

    public static long ConvertTimeUnitToSeconds(long duration, TimeUnit timeUnit)
    {
        return timeUnit switch
        {
            TimeUnit.Minutes => duration * 60,
            TimeUnit.Hours => duration * 3600,
            TimeUnit.Days => duration * 86400,
            TimeUnit.Weeks => duration * 604800,
            TimeUnit.Months => duration * 2629746, // 30.44 days average
            _ => throw new ArgumentException($"Unsupported time unit: {timeUnit}")
        };
    }

    public static async Task AddOverrideRole(ulong roleId, float multiplicator)
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd =
            db.CreateCommand(
                "INSERT INTO level_multiplicatoroverrideroles (roleid, multiplicator) VALUES (@roleid, @multiplicator)");
        cmd.Parameters.AddWithValue("@roleid", (long)roleId);
        cmd.Parameters.AddWithValue("@multiplicator", multiplicator);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task RemoveOverrideRole(ulong roleId)
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd =
            db.CreateCommand("DELETE FROM level_multiplicatoroverrideroles WHERE roleid = @roleid");
        cmd.Parameters.AddWithValue("@roleid", (long)roleId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<bool> IsOverrideRole(ulong roleId)
    {
        var overrides = await GetMultiplicatorOverrides();
        foreach (var overrideRole in overrides)
            if (overrideRole.RoleId == roleId)
                return true;

        return false;
    }

    public static async Task<bool> IsBlacklistedChannel(ulong channelId)
    {
        var blockedChannels = await BlockedChannels();
        foreach (var blockedChannel in blockedChannels)
            if (blockedChannel == channelId)
                return true;

        return false;
    }

    public static async Task<bool> AddBlacklistedChannel(ulong channelId)
    {
        var blockedChannels = await BlockedChannels();
        if (blockedChannels.Contains(channelId)) return false;

        blockedChannels.Add(channelId);
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd =
            db.CreateCommand("INSERT INTO level_excludedchannels (channelid) VALUES (@channelid)");
        cmd.Parameters.AddWithValue("@channelid", (long)channelId);
        await cmd.ExecuteNonQueryAsync();

        return true;
    }

    public static async Task<bool> RemoveBlacklistedChannel(ulong channelId)
    {
        var blockedChannels = await BlockedChannels();
        if (!blockedChannels.Contains(channelId)) return false;

        blockedChannels.Remove(channelId);
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd = db.CreateCommand("DELETE FROM level_excludedchannels WHERE channelid = @channelid");
        cmd.Parameters.AddWithValue("@channelid", (long)channelId);
        await cmd.ExecuteNonQueryAsync();

        return true;
    }

    public static async Task<bool> IsRewardRole(ulong roleId)
    {
        var rewards = await GetLevelRewards();
        foreach (var reward in rewards)
            if (reward.RoleId == roleId)
                return true;

        return false;
    }

    public static async Task AddRewardRole(ulong roleId, int level)
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd =
            db.CreateCommand("INSERT INTO level_rewards (roleid, level) VALUES (@roleid, @level)");
        cmd.Parameters.AddWithValue("@roleid", (long)roleId);
        cmd.Parameters.AddWithValue("@level", level);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task RemoveRewardRole(ulong roleId, int level)
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd =
            db.CreateCommand("DELETE FROM level_rewards WHERE roleid = @roleid AND level = @level");
        cmd.Parameters.AddWithValue("@roleid", (long)roleId);
        cmd.Parameters.AddWithValue("@level", level);
        await cmd.ExecuteNonQueryAsync();
    }


    /// <summary>
    ///     Transfers XP from a source user to a destination user.
    /// </summary>
    /// <param name="sourceUserId">The ID of the source user.</param>
    /// <param name="destinationUserId">The ID of the destination user.</param>
    public static async Task TransferXp(ulong sourceUserId, ulong destinationUserId)
    {
        var sourceRank = await GetRank(sourceUserId);
        var destinationRank = await GetRank(destinationUserId);
        var sourceXp = sourceRank[sourceUserId].Xp;
        var destinationXp = destinationRank[destinationUserId].Xp;
        var newXp = sourceXp + destinationXp;
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd = db.CreateCommand("UPDATE levelingdata SET current_xp = @xp WHERE userid = @id");
        cmd.Parameters.AddWithValue("@xp", newXp);
        cmd.Parameters.AddWithValue("@id", (long)destinationUserId);
        await cmd.ExecuteNonQueryAsync();


        await using var cmd2 = db.CreateCommand("UPDATE levelingdata SET current_xp = @xp WHERE userid = @id");
        cmd2.Parameters.AddWithValue("@xp", 0);
        cmd2.Parameters.AddWithValue("@id", (long)sourceUserId);
        await cmd2.ExecuteNonQueryAsync();

        await RecalculateAndUpdate(destinationUserId);
        await RecalculateAndUpdate(sourceUserId);
    }

    private static async Task RecalculateAndUpdate(ulong userId)
    {
        await RecalculateUserLevel(userId);

        // if user is not in guild, return
        if (CurrentApplication.TargetGuild.Members.Values.FirstOrDefault(x => x.Id == userId) == null) return;

        await UpdateLevelRoles(await CurrentApplication.TargetGuild.GetMemberAsync(userId));
    }


    /// <summary>
    ///     Calculates the minimum and maximum experience points (XP) required for a given level.
    /// </summary>
    /// <param name="lvl">The level.</param>
    /// <returns>A dictionary containing the minimum and maximum XP values.</returns>
    public static Dictionary<int, int> MinAndMaxXpForThisLevel(int lvl)
    {
        var min = 0;
        var max = 0;
        if (lvl <= 0)
        {
            if (lvl == 0) return new Dictionary<int, int> { { 0, 100 } };

            return new Dictionary<int, int> { { min, max } };
        }

        var alvl = lvl - 1;
        min = (int)(5 / 6.0 * (151 * alvl + 33 * Math.Pow(alvl, 2) + 2 * Math.Pow(alvl, 3)) + 100);
        max = (int)(5 / 6.0 * (151 * lvl + 33 * Math.Pow(lvl, 2) + 2 * Math.Pow(lvl, 3)) + 100);
        return new Dictionary<int, int> { { min, max } };
    }


    /// <summary>
    ///     Calculates the level based on the total experience points.
    /// </summary>
    /// <param name="totalXp">The total experience points.</param>
    /// <returns>The level corresponding to the total experience points.</returns>
    public static int LevelAtXp(int totalXp)
    {
        var level = 0;
        var xpForNextLevel = 100;

        while (totalXp >= xpForNextLevel)
        {
            totalXp -= xpForNextLevel;
            level++;
            xpForNextLevel = 5 * level * level + 50 * level + 100;
        }

        return level;
    }

    /// <summary>
    ///     Calculates the amount of experience points needed to reach the next level.
    /// </summary>
    /// <param name="xp">The current amount of experience points.</param>
    /// <returns>The amount of experience points needed to reach the next level.</returns>
    public static int XpUntilNextLevel(int xp)
    {
        var currentLevel = LevelAtXp(xp);
        var xpForNextLevel = XpForLevel(currentLevel + 1);
        return xpForNextLevel - xp;
    }

    /// <summary>
    ///     Method to retrieve the ranking of a user based on their XP in the leveling data table.
    /// </summary>
    /// <param name="userid">The unique identifier of the user.</param>
    /// <returns>The rank of the user. Returns 0 if the user is not found.</returns>
    public static async Task<int> GetUserRankAsync(ulong userid)
    {
        var rank = 0;
        await AddUserToDbIfNot(userid);
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd =
            db.CreateCommand("SELECT userid, current_xp FROM levelingdata ORDER BY current_xp DESC");
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                rank++;
                if (reader.GetInt64(0) == (long)userid) break;
            }


        return rank;
    }

    public static async Task<int> GetXp(ulong userId)
    {
        var rank = await GetRank(userId);
        return rank[userId].Xp;
    }

    /// <summary>
    ///     Recalculates the user level based on their experience points (xp) and updates the database.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    public static async Task RecalculateUserLevel(ulong userId)
    {
        var rank = await GetRank(userId);
        var xp = rank[userId].Xp;
        var level = LevelAtXp(xp);
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd =
            db.CreateCommand("UPDATE levelingdata SET current_level = @level WHERE userid = @id");
        cmd.Parameters.AddWithValue("@level", level);
        cmd.Parameters.AddWithValue("@id", (long)userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task RecalculateAllUserLevels()
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = db.CreateCommand("SELECT userid, current_xp FROM levelingdata");
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
        {
            CurrentApplication.Logger.Information("Recalculating all user levels...");
            while (await reader.ReadAsync())
            {
                var userId = reader.GetInt64(0);
                var xp = reader.GetInt32(1);
                var level = LevelAtXp(xp);
                await using var cmd2 =
                    db.CreateCommand("UPDATE levelingdata SET current_level = @level WHERE userid = @id");
                cmd2.Parameters.AddWithValue("@level", level);
                cmd2.Parameters.AddWithValue("@id", userId);
                try
                {
                    var guildmembers = CurrentApplication.TargetGuild.Members;
                    var member = guildmembers.Values.FirstOrDefault(x => x.Id == (ulong)userId);
                    if (member != null) await UpdateLevelRoles(member);
                }
                catch (Exception)
                {
                    // ignored
                }

                await cmd2.ExecuteNonQueryAsync();
            }

            await using var cmd3 = db.CreateCommand("UPDATE levelingsettings SET lastrecalc = @timestamp");
            cmd3.Parameters.AddWithValue("@timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            await cmd3.ExecuteNonQueryAsync();
            CurrentApplication.Logger.Information("Recalculated all user levels.");
        }
    }

    /// <summary>
    ///     Retrieves the rank data for a specified user.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    /// <returns>A dictionary containing the rank data, where the key is the user ID and the value is the RankData object.</returns>
    public static async Task<Dictionary<ulong, RankData>> GetRank(ulong userId)
    {
        var rank = new Dictionary<ulong, RankData>();
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        var cmd = db.CreateCommand("SELECT current_xp, current_level FROM levelingdata WHERE userid = @id");
        cmd.Parameters.AddWithValue("@id", (long)userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                var xp = reader.GetInt32(0);
                var level = reader.GetInt32(1);
                rank[userId] = new RankData { Level = level, Xp = xp };
            }
        else
            rank[userId] = new RankData { Level = 0, Xp = 0 };

        await reader.CloseAsync();
        return rank;
    }

    public static async Task<int> GetUserLevel(ulong userId)
    {
        var rank = await GetRank(userId);
        return rank[userId].Level;
    }

    public static async Task<int> GetUserXp(ulong userId)
    {
        var rank = await GetRank(userId);
        return rank[userId].Xp;
    }

    public static async Task<int> GetUserLevelPercent(ulong userId)
    {
        var rank = await GetRank(userId);
        var xp = rank[userId].Xp;
        var level = rank[userId].Level;
        var xpForLevel = XpForLevel(level);
        var xpForNextLevel = XpForLevel(level + 1);
        var xpForThisLevel = xpForNextLevel - xpForLevel;
        var xpForThisLevelPercent = xp / xpForThisLevel * 100;
        return xpForThisLevelPercent;
    }


    // leaderboard data for the leaderboard command
    public static async Task<List<LeaderboardData>> FetchLeaderboardData()
    {
        var leaderboardData = new List<LeaderboardData>();
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        var cmd = db.CreateCommand(
            "SELECT userid, current_xp, current_level FROM levelingdata ORDER BY current_xp DESC");
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                var userId = reader.GetInt64(0);
                var xp = reader.GetInt32(1);
                var level = reader.GetInt32(2);
                leaderboardData.Add(new LeaderboardData { UserId = (ulong)userId, XP = xp, Level = level });
            }
        else
            leaderboardData.Add(new LeaderboardData { UserId = 0, XP = 0, Level = 0 });

        await reader.CloseAsync();
        return leaderboardData;
    }

    public static int GetUserRank(ulong invokingUserId, List<LeaderboardData> leaderboardData)
    {
        for (var i = 0; i < leaderboardData.Count; i++)
            if (leaderboardData[i].UserId == invokingUserId)
                return i + 1;

        return -1;
    }

    public static int GetBaseXp(XpRewardType type)
    {
        var rng = new Random();
        switch (type)
        {
            case XpRewardType.Message:
                return rng.Next(15, 26);
            case XpRewardType.Voice:
                return rng.Next(2, 6);
            default:
                return 0;
        }
    }

    public static async Task<bool> isVcLevelingEnabled()
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd = db.CreateCommand("SELECT vc_active FROM levelingsettings WHERE guildid = @guildid");
        cmd.Parameters.AddWithValue("@guildid", (long)levelguildid);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                var vcLevelingEnabled = reader.GetBoolean(0);
                return vcLevelingEnabled;
            }
        else
            return false;


        return false;
    }

    public static async Task<bool> isMessageLevelingEnabled()
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd =
            db.CreateCommand("SELECT text_active FROM levelingsettings WHERE guildid = @guildid");
        cmd.Parameters.AddWithValue("@guildid", (long)levelguildid);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                var messageLevelingEnabled = reader.GetBoolean(0);
                return messageLevelingEnabled;
            }
        else
            return false;


        return false;
    }

    public static async Task<float> GetVcXpMultiplier()
    {
        try
        {
            // Check for active timed multiplier first
            var timedMultiplier = await GetActiveTimedMultiplier(XpRewardType.Voice);
            if (timedMultiplier != null)
            {
                return timedMultiplier.Multiplier;
            }
        }
        catch (Exception ex)
        {
            CurrentApplication.Logger.Warning(ex, "Error getting timed voice multiplier, falling back to base multiplier");
        }
        
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd = db.CreateCommand("SELECT vc_multi FROM levelingsettings WHERE guildid = @guildid");
        cmd.Parameters.AddWithValue("@guildid", (long)levelguildid);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                var vcXpMultiplier = reader.GetFloat(0);
                return vcXpMultiplier;
            }
        else
            return 1;


        return 1;
    }

    public static async Task<float> GetMessageXpMultiplier()
    {
        try
        {
            // Check for active timed multiplier first
            var timedMultiplier = await GetActiveTimedMultiplier(XpRewardType.Message);
            if (timedMultiplier != null)
            {
                return timedMultiplier.Multiplier;
            }
        }
        catch (Exception ex)
        {
            CurrentApplication.Logger.Warning(ex, "Error getting timed message multiplier, falling back to base multiplier");
        }
        
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd = db.CreateCommand("SELECT text_multi FROM levelingsettings WHERE guildid = @guildid");
        cmd.Parameters.AddWithValue("@guildid", (long)levelguildid);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                var messageXpMultiplier = reader.GetFloat(0);
                return messageXpMultiplier;
            }
        else
            return 1;


        return 1;
    }

    public static async Task<string> GetLevelUpMessage()
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd =
            db.CreateCommand("SELECT levelupmessage FROM levelingsettings WHERE guildid = @guildid");
        cmd.Parameters.AddWithValue("@guildid", (long)levelguildid);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                var levelUpMessage = reader.GetString(0);
                return levelUpMessage;
            }
        else
            return "Congratulations {user}! You just advanced to level {level}!";


        return "Congratulations {user}! You just advanced to level {level}!";
    }

    public static async Task<string> GetLevelUpRewardMessage()
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd =
            db.CreateCommand("SELECT levelupmessagereward FROM levelingsettings WHERE guildid = @guildid");
        cmd.Parameters.AddWithValue("@guildid", (long)levelguildid);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                var levelUpRewardMessage = reader.GetString(0);
                return levelUpRewardMessage;
            }
        else
            return "Congratulations {username}! You just advanced to level {level} and received {rolename}!";


        return "Congratulations {username}! You just advanced to level {level} and received {rolename}!";
    }

    public static async Task<ulong> GetLevelUpChannelId()
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd =
            db.CreateCommand("SELECT levelupchannelid FROM levelingsettings WHERE guildid = @guildid");
        cmd.Parameters.AddWithValue("@guildid", (long)levelguildid);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                var levelUpChannelId = reader.GetInt64(0);
                return (ulong)levelUpChannelId;
            }
        else
            return 0;


        return 0;
    }

    public static async Task<List<Reward>> GetLevelRewards()
    {
        var rewards = new List<Reward>();
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();


        var cmd = db.CreateCommand("SELECT level, roleid FROM level_rewards ORDER BY level ASC");
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                var level = reader.GetInt32(0);
                var roleId = reader.GetInt64(1);
                rewards.Add(new Reward { Level = level, RoleId = (ulong)roleId });
            }

        await reader.CloseAsync();

        return rewards;
    }

    public static async Task<bool> ToggleLevelUpPing(ulong userId)
    {
        await AddUserToDbIfNot(await CurrentApplication.TargetGuild.GetMemberAsync(userId));
        var pingEnabled = await IsLevelUpPingEnabled(userId);
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd =
            db.CreateCommand("UPDATE levelingdata SET pingactive = @pingenabled WHERE userid = @userid");
        cmd.Parameters.AddWithValue("@pingenabled", !pingEnabled);
        cmd.Parameters.AddWithValue("@userid", (long)userId);
        await cmd.ExecuteNonQueryAsync();

        return !pingEnabled;
    }

    public static async Task<bool> IsLevelUpPingEnabled(ulong userId)
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await AddUserToDbIfNot(userId);
        await using var cmd = db.CreateCommand("SELECT pingactive FROM levelingdata WHERE userid = @userid");
        cmd.Parameters.AddWithValue("@userid", (long)userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
                return reader.GetBoolean(0);
        else
            return false;


        return false;
    }

    public static async Task<List<MultiplicatorOverrides>> GetMultiplicatorOverrides()
    {
        var overrides = new List<MultiplicatorOverrides>();
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();


        var cmd = db.CreateCommand("SELECT roleid, multiplicator FROM level_multiplicatoroverrideroles");
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                var roleId = reader.GetInt64(0);
                var multiplier = reader.GetFloat(1);
                overrides.Add(new MultiplicatorOverrides { RoleId = (ulong)roleId, Multiplicator = multiplier });
            }

        await reader.CloseAsync();

        return overrides;
    }

    public static async Task<bool> IsLevelingActive(XpRewardType type)
    {
        if (type == XpRewardType.Message) return await isMessageLevelingEnabled();

        if (type == XpRewardType.Voice) return await isVcLevelingEnabled();

        return false;
    }

    public static async Task<List<ulong>> BlockedChannels()
    {
        var blockedChannels = new List<ulong>();
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();


        var cmd = db.CreateCommand("SELECT channelid FROM level_excludedchannels");
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                var channelId = reader.GetInt64(0);
                blockedChannels.Add((ulong)channelId);
            }

        await reader.CloseAsync();

        return blockedChannels;
    }

    public static async Task<List<ulong>> BlockedRoles()
    {
        var blockedRoles = new List<ulong>();
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();


        var cmd = db.CreateCommand("SELECT roleid FROM level_excludedroles");
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                var roleId = reader.GetInt64(0);
                blockedRoles.Add((ulong)roleId);
            }

        await reader.CloseAsync();

        return blockedRoles;
    }

    public static async Task<bool> IsChannelBlocked(ulong channelId)
    {
        var blockedChannels = await BlockedChannels();
        if (blockedChannels.Contains(channelId)) return true;

        return false;
    }

    public static async Task<bool> UserHasBlockedRole(DiscordMember member)
    {
        var blockedRoles = await BlockedRoles();
        foreach (var role in member.Roles)
            if (blockedRoles.Contains(role.Id))
                return true;

        return false;
    }

    public static async Task GiveXP(DiscordUser user, float xp, XpRewardType type)
    {
        _ = Task.Run(async () =>
        {
            if (!await IsLevelingActive(type)) return;

            if (await UserHasBlockedRole(await user.ConvertToMember(CurrentApplication.TargetGuild))) return;

            await AddUserToDbIfNot(user.Id);
            var typeString = type == XpRewardType.Message ? "last_text_reward" : "last_vc_reward";
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var cooldowndb = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
            await using var cooldowncmd =
                cooldowndb.CreateCommand(
                    $"SELECT {typeString} FROM levelingdata WHERE userid = @id AND {typeString} > @cooldown");
            cooldowncmd.Parameters.AddWithValue("@id", (long)user.Id);
            cooldowncmd.Parameters.AddWithValue("@cooldown", now - 60);
            await using var cooldownreader = await cooldowncmd.ExecuteReaderAsync();
            // cooldown is 60 seconds
            if (cooldownreader.HasRows)
            {
                CurrentApplication.Logger.Debug("Cooldown is active for " + user.Username +
                                                $" RewardType: {type.ToString()}");
                return;
            }

            await cooldownreader.CloseAsync();

            var rank = await GetRank(user.Id);
            var currentXp = rank[user.Id].Xp;
            var currentLevel = rank[user.Id].Level;
            var xpMultiplier = 1f;
            if (type == XpRewardType.Message)
                xpMultiplier = await GetMessageXpMultiplier();
            else if (type == XpRewardType.Voice) xpMultiplier = await GetVcXpMultiplier();

            var xpToGive = (int)(xpMultiplier * xp);
            var multiplicatorOverrides = await GetMultiplicatorOverrides();
            var member = await user.ConvertToMember(CurrentApplication.TargetGuild);
            foreach (var multiplicatorOverride in multiplicatorOverrides)
                if (member.Roles.Any(role => role.Id == multiplicatorOverride.RoleId))
                    xpToGive = (int)(xpToGive * multiplicatorOverride.Multiplicator);

            var newXp = currentXp + xpToGive;
            var newLevel = LevelAtXp(newXp);
            var current_timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var rewardTypeString = type == XpRewardType.Message ? "last_text_reward" : "last_vc_reward";
            var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

            await using var cmd =
                db.CreateCommand(
                    "UPDATE levelingdata SET current_xp = @xp, current_level = @level, " + rewardTypeString +
                    " = @timestamp WHERE userid = @id");
            cmd.Parameters.AddWithValue("@xp", newXp);
            cmd.Parameters.AddWithValue("@level", newLevel);
            cmd.Parameters.AddWithValue("@id", (long)user.Id);
            cmd.Parameters.AddWithValue("@timestamp", current_timestamp);
            await cmd.ExecuteNonQueryAsync();

            CurrentApplication.Logger.Debug("Gave " + xpToGive + " xp to " + user.Username);
            if (newLevel > currentLevel) await SendLevelUpMessageAndReward(user, newLevel);
        });
        await Task.CompletedTask;
    }

    public static async Task AddUserToDbIfNot(DiscordUser user)
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = db.CreateCommand(
            "INSERT INTO levelingdata (userid, current_xp, current_level) VALUES (@id, @xp, @level) ON CONFLICT (userid) DO NOTHING");
        cmd.Parameters.AddWithValue("@id", (long)user.Id);
        cmd.Parameters.AddWithValue("@xp", 0);
        cmd.Parameters.AddWithValue("@level", 0);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task AddUserToDbIfNot(ulong userid)
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = db.CreateCommand(
            "INSERT INTO levelingdata (userid, current_xp, current_level) VALUES (@id, @xp, @level) ON CONFLICT (userid) DO NOTHING");
        cmd.Parameters.AddWithValue("@id", (long)userid);
        cmd.Parameters.AddWithValue("@xp", 0);
        cmd.Parameters.AddWithValue("@level", 0);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SendLevelUpMessageAndReward(DiscordUser user, int level)
    {
        // Fetch all necessary data at once
        var levelUpMessage = await GetLevelUpMessage();
        var pingEnabled = await UserHasPingEnabled(user.Id);
        var isReward = await IsLevelRewarded(level);
        var rewardMessage = await GetLevelUpRewardMessage();
        var reward = await GetRewardForaLevel(level);
        var member = await user.ConvertToMember(CurrentApplication.TargetGuild);

        // Use StringBuilder for string concatenation
        var messageBuilder = new StringBuilder();

        if (isReward)
        {
            var role = CurrentApplication.TargetGuild.GetRole(reward[true]);
            if (!member.Roles.Contains(role))
                try
                {
                    await member.GrantRoleAsync(role);
                }
                catch (Exception e)
                {
                    CurrentApplication.Logger.Error(e.Message);
                }

            messageBuilder.Append(await MessageFormatter.FormatLevelUpMessage(rewardMessage, true, user, role));
        }
        else
        {
            messageBuilder.Append(await MessageFormatter.FormatLevelUpMessage(levelUpMessage, false, user));
        }

        var channel = CurrentApplication.TargetGuild.GetChannel(await GetLevelUpChannelId());
        var messagebuilder = new DiscordMessageBuilder();
        messagebuilder.WithContent(messageBuilder.ToString());
        if (!pingEnabled) messagebuilder.WithAllowedMentions(Mentions.None);

        await channel.SendMessageAsync(messagebuilder);
    }

    private static async Task<bool> IsLevelRewarded(int level)
    {
        var rewards = await GetLevelRewards();
        var reward = rewards.FirstOrDefault(r => r.Level == level);
        if (reward != null) return true;

        return false;
    }

    public static async Task<bool> UserHasPingEnabled(ulong userId)
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd = db.CreateCommand("SELECT pingactive FROM levelingdata WHERE userid = @id");
        cmd.Parameters.AddWithValue("@id", (long)userId);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (reader.HasRows)
            while (await reader.ReadAsync())
                return reader.GetBoolean(0);


        return false;
    }

    private static async Task<Dictionary<bool, ulong>> GetRewardForaLevel(int level)
    {
        var rewards = await GetLevelRewards();
        var reward = rewards.FirstOrDefault(r => r.Level == level);
        if (reward != null) return new Dictionary<bool, ulong> { { true, reward.RoleId } };

        return new Dictionary<bool, ulong> { { false, 0 } };
    }


    // restore all roles for a user that match the level or below
    public static async Task RestoreRoles(DiscordMember user)
    {
        var rank = await GetRank(user.Id);
        var level = rank[user.Id].Level;
        var rewards = await GetLevelRewards();
        var rolesToRestore = rewards.Where(r => r.Level <= level).ToList();
        foreach (var roleToRestore in rolesToRestore)
        {
            var role = CurrentApplication.TargetGuild.GetRole(roleToRestore.RoleId);
            try
            {
                await user.GrantRoleAsync(role);
            }
            catch (Exception e)
            {
                CurrentApplication.Logger.Error(e.Message);
            }
        }
    }

    public static async Task<bool> IsLevelUpMessageEnabled()
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd =
            db.CreateCommand("SELECT levelupchannelid FROM levelingsettings WHERE guildid = @guildid");
        cmd.Parameters.AddWithValue("@guildid", (long)CurrentApplication.TargetGuild.Id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
            while (await reader.ReadAsync())
            {
                var levelUpChannelId = reader.GetInt64(0);
                if (levelUpChannelId == 0) return false;

                return true;
            }
        else
            return false;

        return false;
    }

    public static async Task SetXp(DiscordUser user, int xp)
    {
        await AddUserToDbIfNot(user.Id);
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = db.CreateCommand("UPDATE levelingdata SET current_xp = @xp WHERE userid = @id");
        cmd.Parameters.AddWithValue("@xp", xp);
        cmd.Parameters.AddWithValue("@id", (long)user.Id);
        await cmd.ExecuteNonQueryAsync();
        await RecalculateAndUpdate(user.Id);
    }

    public static async Task AddXp(DiscordUser user, int xp)
    {
        await AddUserToDbIfNot(user.Id);
        var rank = await GetRank(user.Id);
        var currentXp = rank[user.Id].Xp;
        var newXp = currentXp + xp;
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = db.CreateCommand("UPDATE levelingdata SET current_xp = @xp WHERE userid = @id");
        cmd.Parameters.AddWithValue("@xp", newXp);
        cmd.Parameters.AddWithValue("@id", (long)user.Id);
        await RecalculateAndUpdate(user.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task RemoveXp(DiscordUser user, int xp)
    {
        await AddUserToDbIfNot(user.Id);
        var rank = await GetRank(user.Id);
        var currentXp = rank[user.Id].Xp;
        var newXp = currentXp - xp;
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = db.CreateCommand("UPDATE levelingdata SET current_xp = @xp WHERE userid = @id");
        cmd.Parameters.AddWithValue("@xp", newXp);
        cmd.Parameters.AddWithValue("@id", (long)user.Id);
        await RecalculateAndUpdate(user.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<int> GetLevel(ulong userId)
    {
        var rank = await GetRank(userId);
        return rank[userId].Level;
    }

    public static async Task SetLevel(DiscordUser user, int level)
    {
        await AddUserToDbIfNot(user.Id);
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        var xp = XpForLevel(level);
        await using var cmd =
            db.CreateCommand("UPDATE levelingdata SET current_level = @level, current_xp = @xp WHERE userid = @id");
        cmd.Parameters.AddWithValue("@level", level);
        cmd.Parameters.AddWithValue("@id", (long)user.Id);
        cmd.Parameters.AddWithValue("@xp", xp);
        await cmd.ExecuteNonQueryAsync();

        await RecalculateAndUpdate(user.Id);
    }

    public static async Task AddLevel(DiscordUser user, int level)
    {
        await AddUserToDbIfNot(user.Id);
        var rank = await GetRank(user.Id);
        var currentLevel = rank[user.Id].Level;
        var newLevel = currentLevel + level;
        var xp = XpForLevel(newLevel);
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd =
            db.CreateCommand("UPDATE levelingdata SET current_level = @level, current_xp = @xp WHERE userid = @id");
        cmd.Parameters.AddWithValue("@level", newLevel);
        cmd.Parameters.AddWithValue("@id", (long)user.Id);
        cmd.Parameters.AddWithValue("@xp", xp);
        await RecalculateAndUpdate(user.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task RemoveLevel(DiscordUser user, int level)
    {
        await AddUserToDbIfNot(user.Id);
        var rank = await GetRank(user.Id);
        var currentLevel = rank[user.Id].Level;
        var newLevel = currentLevel - level;
        var xp = XpForLevel(newLevel);
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd =
            db.CreateCommand("UPDATE levelingdata SET current_level = @level, current_xp = @xp WHERE userid = @id");
        cmd.Parameters.AddWithValue("@level", newLevel);
        cmd.Parameters.AddWithValue("@id", (long)user.Id);
        cmd.Parameters.AddWithValue("@xp", xp);
        await RecalculateAndUpdate(user.Id);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Sets a timed multiplier for the specified XP reward type
    /// </summary>
    /// <param name="rewardType">The type of XP reward (Message or Voice)</param>
    /// <param name="multiplier">The multiplier value to set</param>
    /// <param name="durationInSeconds">Duration in seconds for how long the multiplier should be active</param>
    /// <param name="resetValue">The value to reset to after the multiplier expires</param>
    public static async Task SetTimedMultiplier(XpRewardType rewardType, float multiplier, long durationInSeconds, float resetValue)
    {
        if (multiplier <= 0)
            throw new ArgumentException("Multiplier must be greater than 0", nameof(multiplier));
        if (durationInSeconds <= 0)
            throw new ArgumentException("Duration must be greater than 0", nameof(durationInSeconds));
        if (resetValue < 0)
            throw new ArgumentException("Reset value cannot be negative", nameof(resetValue));
            
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var expiryTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + durationInSeconds;
        
        // Remove any existing timed multiplier for this type
        await RemoveTimedMultiplier(rewardType);
        
        // Insert new timed multiplier
        await using var cmd = db.CreateCommand(
            "INSERT INTO level_timedmultipliers (guildid, type, multiplier, expiry_timestamp, reset_value) VALUES (@guildid, @type, @multiplier, @expiry, @reset)");
        cmd.Parameters.AddWithValue("@guildid", (long)levelguildid);
        cmd.Parameters.AddWithValue("@type", (int)rewardType);
        cmd.Parameters.AddWithValue("@multiplier", multiplier);
        cmd.Parameters.AddWithValue("@expiry", expiryTimestamp);
        cmd.Parameters.AddWithValue("@reset", resetValue);
        await cmd.ExecuteNonQueryAsync();
        
        CurrentApplication.Logger.Information($"Set timed multiplier for {rewardType}: {multiplier}x for {durationInSeconds}s, reset to {resetValue}x");
    }

    /// <summary>
    /// Removes a timed multiplier for the specified XP reward type
    /// </summary>
    /// <param name="rewardType">The type of XP reward (Message or Voice)</param>
    public static async Task RemoveTimedMultiplier(XpRewardType rewardType)
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = db.CreateCommand(
            "DELETE FROM level_timedmultipliers WHERE guildid = @guildid AND type = @type");
        cmd.Parameters.AddWithValue("@guildid", (long)levelguildid);
        cmd.Parameters.AddWithValue("@type", (int)rewardType);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Gets the active timed multiplier for the specified XP reward type, if any
    /// </summary>
    /// <param name="rewardType">The type of XP reward (Message or Voice)</param>
    /// <returns>The timed multiplier if active, null otherwise</returns>
    public static async Task<TimedMultiplier?> GetActiveTimedMultiplier(XpRewardType rewardType)
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        await using var cmd = db.CreateCommand(
            "SELECT multiplier, expiry_timestamp, reset_value FROM level_timedmultipliers WHERE guildid = @guildid AND type = @type AND expiry_timestamp > @current");
        cmd.Parameters.AddWithValue("@guildid", (long)levelguildid);
        cmd.Parameters.AddWithValue("@type", (int)rewardType);
        cmd.Parameters.AddWithValue("@current", currentTimestamp);
        
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
        {
            while (await reader.ReadAsync())
            {
                return new TimedMultiplier
                {
                    GuildId = levelguildid,
                    Type = rewardType,
                    Multiplier = reader.GetFloat(0),
                    ExpiryTimestamp = reader.GetInt64(1),
                    ResetValue = reader.GetFloat(2)
                };
            }
        }
        
        await reader.CloseAsync();
        return null;
    }

    /// <summary>
    /// Cleans up expired timed multipliers and resets the base multipliers to their reset values
    /// </summary>
    public static async Task CleanupExpiredTimedMultipliers()
    {
        try
        {
            var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
            var currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            // Get expired multipliers before deleting them
            var expiredMultipliers = new List<TimedMultiplier>();
            await using var selectCmd = db.CreateCommand(
                "SELECT type, reset_value FROM level_timedmultipliers WHERE guildid = @guildid AND expiry_timestamp <= @current");
            selectCmd.Parameters.AddWithValue("@guildid", (long)levelguildid);
            selectCmd.Parameters.AddWithValue("@current", currentTimestamp);
            
            await using var reader = await selectCmd.ExecuteReaderAsync();
            if (reader.HasRows)
            {
                while (await reader.ReadAsync())
                {
                    var type = (XpRewardType)reader.GetInt32(0);
                    var resetValue = reader.GetFloat(1);
                    expiredMultipliers.Add(new TimedMultiplier { Type = type, ResetValue = resetValue });
                }
            }
            await reader.CloseAsync();
            
            // Reset base multipliers to their reset values
            foreach (var expired in expiredMultipliers)
            {
                await SetMultiplier(expired.Type, expired.ResetValue);
                CurrentApplication.Logger.Information($"Timed multiplier for {expired.Type} expired and reset to {expired.ResetValue}x");
            }
            
            // Delete expired multipliers
            if (expiredMultipliers.Count > 0)
            {
                await using var deleteCmd = db.CreateCommand(
                    "DELETE FROM level_timedmultipliers WHERE guildid = @guildid AND expiry_timestamp <= @current");
                deleteCmd.Parameters.AddWithValue("@guildid", (long)levelguildid);
                deleteCmd.Parameters.AddWithValue("@current", currentTimestamp);
                var deletedCount = await deleteCmd.ExecuteNonQueryAsync();
                CurrentApplication.Logger.Information($"Cleaned up {deletedCount} expired timed multipliers");
            }
        }
        catch (Exception ex)
        {
            CurrentApplication.Logger.Error(ex, "Error during timed multiplier cleanup");
            throw;
        }
    }
}