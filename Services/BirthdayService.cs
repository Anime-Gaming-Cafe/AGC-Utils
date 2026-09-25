#region

using System.Globalization;
using System.Text.RegularExpressions;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Services;

public sealed record BirthdayEntry(ulong UserId, string Date, bool Ping);

public static class BirthdayService
{
    public const string Section = "Birthday";

    public const string SetButtonId = "birthday:set";
    public const string PingButtonId = "birthday:ping";
    public const string ConfirmButtonPrefix = "birthday:confirm:";
    public const string CancelButtonId = "birthday:cancel";

    private const string PanelThumbnail = "https://cdn.discordapp.com/emojis/921052951525589003.png?size=96";

    private static readonly CultureInfo German = new("de-DE");
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    private static readonly TimeZoneInfo Berlin = ResolveBerlin();

    private static TimeZoneInfo ResolveBerlin()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch
        {
            return TimeZoneInfo.Local;
        }
    }

    public const string DisabledMessage = "Das Geburtstagssystem ist aktuell deaktiviert.";

    public static Task<bool> IsEnabledAsync()
    {
        return RuntimeSettings.GetBoolAsync(Section, "Enabled", false);
    }

    public static async Task<bool> RejectIfDisabledAsync(DiscordInteraction interaction)
    {
        if (await IsEnabledAsync()) return false;
        await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(DisabledMessage).AsEphemeral());
        return true;
    }

    public static DateOnly TodayBerlin()
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, Berlin));
    }

    public static bool TryParseDate(string? input, out string date)
    {
        date = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var parts = input.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 2 or > 3) return false;
        if (!int.TryParse(parts[0], out var day) || !int.TryParse(parts[1], out var month)) return false;
        if (month is < 1 or > 12) return false;
        if (day < 1 || day > DateTime.DaysInMonth(2000, month)) return false;

        date = $"{day:00}.{month:00}";
        return true;
    }

    public static string Humanize(string date)
    {
        var parts = date.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var day) || !int.TryParse(parts[1], out var month) ||
            month is < 1 or > 12)
            return date;

        return $"{day}. {German.DateTimeFormat.GetMonthName(month)}";
    }

    public static string ToDate(DateOnly day)
    {
        return $"{day.Day:00}.{day.Month:00}";
    }

    public static int MonthOf(string date)
    {
        var parts = date.Split('.');
        return parts.Length >= 2 && int.TryParse(parts[1], out var month) ? month : 0;
    }

    #region Database

    private static NpgsqlDataSource Db()
    {
        return CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
    }

    public static async Task<BirthdayEntry?> GetAsync(ulong userId)
    {
        await using var cmd = Db().CreateCommand("SELECT datum, ping FROM birthdays WHERE user_id = @id");
        cmd.Parameters.AddWithValue("id", (long)userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new BirthdayEntry(userId, reader.GetString(0), reader.GetBoolean(1));
    }

    public static async Task<bool> InsertIfMissingAsync(ulong userId, string date)
    {
        await using var cmd = Db().CreateCommand(
            "INSERT INTO birthdays (user_id, datum, ping) VALUES (@id, @date, true) ON CONFLICT (user_id) DO NOTHING");
        cmd.Parameters.AddWithValue("id", (long)userId);
        cmd.Parameters.AddWithValue("date", date);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public static async Task UpsertAsync(ulong userId, string date)
    {
        await using var cmd = Db().CreateCommand(
            "INSERT INTO birthdays (user_id, datum, ping) VALUES (@id, @date, true) " +
            "ON CONFLICT (user_id) DO UPDATE SET datum = EXCLUDED.datum");
        cmd.Parameters.AddWithValue("id", (long)userId);
        cmd.Parameters.AddWithValue("date", date);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<bool> DeleteAsync(ulong userId)
    {
        await using var cmd = Db().CreateCommand("DELETE FROM birthdays WHERE user_id = @id");
        cmd.Parameters.AddWithValue("id", (long)userId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public static async Task SetPingAsync(ulong userId, bool ping)
    {
        await using var cmd = Db().CreateCommand("UPDATE birthdays SET ping = @ping WHERE user_id = @id");
        cmd.Parameters.AddWithValue("id", (long)userId);
        cmd.Parameters.AddWithValue("ping", ping);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<List<string>> GetAllDatesAsync()
    {
        List<string> dates = [];
        await using var cmd = Db().CreateCommand("SELECT datum FROM birthdays");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) dates.Add(reader.GetString(0));
        return dates;
    }

    public static async Task<int> CountAsync()
    {
        await using var cmd = Db().CreateCommand("SELECT COUNT(*) FROM birthdays");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public static async Task<List<BirthdayEntry>> GetForDayAsync(DateOnly day)
    {
        List<BirthdayEntry> entries = [];
        if (day.Month == 2 && day.Day == 29 && !DateTime.IsLeapYear(day.Year)) return entries;

        await using var cmd = Db().CreateCommand("SELECT user_id, datum, ping FROM birthdays WHERE datum = @date");
        cmd.Parameters.AddWithValue("date", ToDate(day));
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            entries.Add(new BirthdayEntry((ulong)reader.GetInt64(0), reader.GetString(1), reader.GetBoolean(2)));
        return entries;
    }

    #endregion

    #region Roles

    public static async Task<DiscordRole?> GetRoleAsync()
    {
        var raw = await RuntimeSettings.GetAsync(Section, "RoleId");
        if (!ulong.TryParse(raw, out var id) || id == 0) return null;
        return CurrentApplication.TargetGuild?.GetRole(id);
    }

    public static async Task GrantRoleIfTodayAsync(ulong userId, string date)
    {
        if (date != ToDate(TodayBerlin())) return;
        if (!await IsEnabledAsync()) return;

        var role = await GetRoleAsync();
        var guild = CurrentApplication.TargetGuild;
        if (role is null || guild is null || !guild.Members.TryGetValue(userId, out var member)) return;

        try
        {
            if (member.Roles.All(r => r.Id != role.Id)) await member.GrantRoleAsync(role);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Birthday: Rolle konnte {User} nicht gegeben werden", userId);
        }
    }

    public static async Task RevokeRoleAsync(ulong userId)
    {
        var role = await GetRoleAsync();
        var guild = CurrentApplication.TargetGuild;
        if (role is null || guild is null || !guild.Members.TryGetValue(userId, out var member)) return;

        try
        {
            if (member.Roles.Any(r => r.Id == role.Id)) await member.RevokeRoleAsync(role);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Birthday: Rolle konnte {User} nicht entzogen werden", userId);
        }
    }

    #endregion

    #region Panel

    public static async Task<(DiscordEmbed embed, DiscordComponent[] buttons)> BuildPanelAsync(DiscordUser user,
        string? notice = null)
    {
        var entry = await GetAsync(user.Id);
        var embed = new DiscordEmbedBuilder()
            .WithTitle("Konfiguration Geburtstagssystem")
            .WithColor(BotConfig.GetEmbedColor())
            .WithThumbnail(PanelThumbnail);

        if (entry is null)
        {
            embed.WithDescription("Dein Geburtstag: **Nicht festgelegt**\n" +
                                  "Ping aktiviert: **Nicht festgelegt**\n\n" +
                                  "**Dein Geburtstag ist nicht eingerichtet! Bitte klicke unten auf `Geburtstag festlegen`.**");
            embed.WithFooter(
                $"Gültig für {user.UsernameWithGlobalName}\nDu kannst deinen Geburtstag nur 1x selbst setzen! Um ihn zu ändern, musst du ein Ticket öffnen.",
                user.AvatarUrl);
        }
        else
        {
            embed.WithDescription($"Dein Geburtstag: **{Humanize(entry.Date)}**\n" +
                                  $"Ping aktiviert: **{(entry.Ping ? "Ja" : "Nein")}**");
            embed.WithFooter(
                $"Gültig für {user.UsernameWithGlobalName}\nUm deinen Geburtstag zu ändern, musst du ein Ticket öffnen.",
                user.AvatarUrl);
        }

        if (!string.IsNullOrEmpty(notice)) embed.WithDescription($"{notice}\n\n{embed.Description}");

        DiscordComponent[] buttons =
        [
            new DiscordButtonComponent(ButtonStyle.Primary, SetButtonId, "Geburtstag festlegen", entry is not null),
            new DiscordButtonComponent(ButtonStyle.Secondary, PingButtonId,
                entry is { Ping: false } ? "Geburtstagsping aktivieren" : "Geburtstagsping deaktivieren",
                entry is null)
        ];

        return (embed.Build(), buttons);
    }

    public static (DiscordEmbed embed, DiscordComponent[] buttons) BuildConfirm(DiscordUser user, string date)
    {
        var embed = new DiscordEmbedBuilder()
            .WithTitle("Bestätige Eingabe")
            .WithDescription($"Geburtstag festgelegt auf: **{Humanize(date)}** *({date})*\nBitte bestätige deine Eingabe.\n\n" +
                             "Danach kannst du deinen Geburtstag nicht mehr selbst ändern.")
            .WithColor(BotConfig.GetEmbedColor())
            .WithFooter($"Gültig für {user.UsernameWithGlobalName}", user.AvatarUrl);

        DiscordComponent[] buttons =
        [
            new DiscordButtonComponent(ButtonStyle.Success, ConfirmButtonPrefix + date, "Bestätigen"),
            new DiscordButtonComponent(ButtonStyle.Danger, CancelButtonId, "Abbruch")
        ];

        return (embed.Build(), buttons);
    }

    #endregion

    #region Daily run

    public static async Task<bool> RunDailyAsync(bool force = false)
    {
        if (!await RunLock.WaitAsync(0)) return false;
        try
        {
            if (!await IsEnabledAsync()) return false;

            var today = TodayBerlin();
            var todayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!force && await RuntimeSettings.GetAsync(Section, "LastRunDate") == todayKey) return false;

            var guild = CurrentApplication.TargetGuild;
            if (guild is null) return false;

            var channelRaw = await RuntimeSettings.GetAsync(Section, "ChannelId");
            if (!ulong.TryParse(channelRaw, out var channelId) || channelId == 0) return false;
            var channel = guild.GetChannel(channelId);
            if (channel is null)
            {
                CurrentApplication.Logger.Warning("Birthday: Channel {Channel} nicht gefunden", channelId);
                return false;
            }

            await RuntimeSettings.SetAsync(Section, "LastRunDate", todayKey);
            CurrentApplication.Logger.Information("Birthday: Tageslauf für {Day}", todayKey);

            await PurgeAsync(channel, await RuntimeSettings.GetIntAsync(Section, "PurgeLimit", 90));
            await channel.SendMessageAsync("Heutige Geburtstage:");

            var role = await GetRoleAsync();
            if (role is not null)
                foreach (var holder in guild.Members.Values.Where(m => m.Roles.Any(r => r.Id == role.Id)).ToList())
                    try
                    {
                        await holder.RevokeRoleAsync(role);
                    }
                    catch (Exception e)
                    {
                        CurrentApplication.Logger.Warning(e, "Birthday: Rolle konnte {User} nicht entzogen werden",
                            holder.Id);
                    }

            var entries = await GetForDayAsync(today);
            var present = entries.Where(e => guild.Members.ContainsKey(e.UserId)).ToList();
            var humanized = Humanize(ToDate(today));
            var messages = await GetMessagesAsync();
            var emoji = await GetReactionEmojiAsync();
            var thumbnail = await RuntimeSettings.GetAsync(Section, "ThumbnailUrl");
            var image = await RuntimeSettings.GetAsync(Section, "ImageUrl");

            foreach (var entry in present)
                try
                {
                    var member = guild.Members[entry.UserId];
                    if (role is not null) await member.GrantRoleAsync(role);
                    if (!entry.Ping) continue;

                    var text = messages[Random.Shared.Next(messages.Count)]
                        .Replace("{user}", member.DisplayName)
                        .Replace("{role}", role?.Mention ?? "Geburtstagsrolle");
                    var embed = new DiscordEmbedBuilder()
                        .WithTitle($"{member.UsernameWithGlobalName} hat Geburtstag")
                        .WithDescription(text)
                        .WithColor(BotConfig.GetEmbedColor())
                        .WithFooter($"Wünscht {member.DisplayName} alles Gute! | Geburtstag: {humanized}",
                            member.AvatarUrl);
                    if (Uri.IsWellFormedUriString(thumbnail, UriKind.Absolute)) embed.WithThumbnail(thumbnail);
                    if (Uri.IsWellFormedUriString(image, UriKind.Absolute)) embed.WithImageUrl(image);

                    var message = await channel.SendMessageAsync(new DiscordMessageBuilder()
                        .WithContent(member.Mention)
                        .AddEmbed(embed)
                        .WithAllowedMention(new UserMention(member.Id)));
                    if (emoji is not null) await message.CreateReactionAsync(emoji);
                }
                catch (Exception e)
                {
                    CurrentApplication.Logger.Warning(e, "Birthday: Gratulation für {User} fehlgeschlagen",
                        entry.UserId);
                }

            var count = present.Count;
            var has = count == 1 ? "hat" : "haben";
            var memberWord = count == 1 ? "Mitglied" : "Mitglieder";
            var total = await CountAsync();
            var overview = new DiscordEmbedBuilder()
                .WithTitle("Übersicht Geburtstage")
                .WithDescription($"Heute, den **{humanized}** {has} **{count} {memberWord}** Geburtstag")
                .WithColor(BotConfig.GetEmbedColor())
                .WithFooter(
                    "Beachte: Es werden nur die einzelnen Mitglieder in diesem Channel angezeigt, die den Ping im Bot nicht deaktiviert haben.\n" +
                    "Die Anzahl widerspiegelt jedoch alle eingetragenen Mitglieder, die heute Geburtstag haben.\n" +
                    "Möchtest du auch deinen Geburtstag eintragen? Dann führe /birthdaysetup aus.\n" +
                    $"Aktuell sind {total} Mitglieder im System eingetragen, wovon heute {count} {memberWord} Geburtstag {has}.");
            await channel.SendMessageAsync(overview);
            return true;
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "Birthday: Tageslauf fehlgeschlagen");
            await ErrorReporting.SendErrorToDev(CurrentApplication.DiscordClient, e);
            return false;
        }
        finally
        {
            RunLock.Release();
        }
    }

    private static async Task<List<string>> GetMessagesAsync()
    {
        var raw = await RuntimeSettings.GetAsync(Section, "Messages") ?? string.Empty;
        var messages = Regex.Split(raw.Replace("\r\n", "\n"), @"\n\s*\n")
            .Select(m => m.Trim())
            .Where(m => m.Length > 0)
            .ToList();
        if (messages.Count == 0) messages.Add("Alles Gute zum Geburtstag, {user}!\nDu hast für heute die Rolle {role}");
        return messages;
    }

    private static async Task<DiscordEmoji?> GetReactionEmojiAsync()
    {
        var raw = await RuntimeSettings.GetAsync(Section, "ReactionEmojiId");
        if (!ulong.TryParse(raw, out var id) || id == 0) return null;
        try
        {
            return DiscordEmoji.FromGuildEmote(CurrentApplication.DiscordClient, id);
        }
        catch
        {
            return null;
        }
    }

    private static async Task PurgeAsync(DiscordChannel channel, int limit)
    {
        if (limit <= 0) return;
        try
        {
            var messages = await channel.GetMessagesAsync(Math.Min(limit, 100));
            var cutoff = DateTimeOffset.UtcNow.AddDays(-13);
            var recent = messages.Where(m => m.CreationTimestamp > cutoff).ToList();
            var old = messages.Where(m => m.CreationTimestamp <= cutoff).ToList();

            if (recent.Count > 1) await channel.DeleteMessagesAsync(recent);
            else if (recent.Count == 1) await recent[0].DeleteAsync();

            foreach (var message in old)
                try
                {
                    await message.DeleteAsync();
                }
                catch
                {
                }
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Birthday: Channel konnte nicht geleert werden");
        }
    }

    #endregion
}
