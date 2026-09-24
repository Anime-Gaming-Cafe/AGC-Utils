#region

using AGC_Management.Entities.Autoposts;
using AGC_Management.Enums.Autoposts;
using AGC_Management.Services;

#endregion

namespace AGC_Management.Utils;

/// <summary>
///     The one place that knows the autopost condition types. A new type is an enum value, a case in
///     <see cref="IsMetAsync" /> and <see cref="Summarize" />, its fields on <see cref="AutopostConditionParams" />
///     and a form block in the detail page.
/// </summary>
public static class AutopostConditionEvaluator
{
    public record Result(bool Passed, string? BlockedBy);

    public static readonly (AutopostConditionType Type, string Label)[] Types =
    [
        (AutopostConditionType.ApplicationPhase, "Bewerbungsphase offen"),
        (AutopostConditionType.TimeWindow, "Uhrzeit und Wochentage"),
        (AutopostConditionType.DateRange, "Zeitraum (Datum)"),
        (AutopostConditionType.ChannelActivity, "Aktivität im Zielkanal"),
        (AutopostConditionType.Cooldown, "Mindestabstand zum letzten Post"),
        (AutopostConditionType.LastMessageNotAutopost, "Letzte Nachricht ist kein Autopost")
    ];

    public static async Task<Result> EvaluateAsync(Autopost autopost)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var condition in autopost.Conditions)
        {
            var met = await IsMetAsync(autopost, condition, now);
            if (met == condition.Negate) return new Result(false, Summarize(condition));
        }

        return new Result(true, null);
    }

    private static async Task<bool> IsMetAsync(Autopost autopost, AutopostCondition condition, DateTimeOffset now)
    {
        var p = condition.Params;
        var unix = now.ToUnixTimeSeconds();

        switch (condition.Type)
        {
            case AutopostConditionType.ApplicationPhase:
            {
                var positions = await TeamApplicationService.GetPositionsAsync(true);
                if (!string.IsNullOrEmpty(p.PositionId))
                    positions = positions.Where(pos => pos.PositionId == p.PositionId).ToList();

                foreach (var position in positions)
                    if ((await TeamApplicationService.ResolveOpeningAsync(position)).CanApply)
                        return true;

                return false;
            }
            case AutopostConditionType.TimeWindow:
            {
                var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
                var weekday = ((int)local.DayOfWeek + 6) % 7 + 1;
                if (p.Weekdays.Count > 0 && !p.Weekdays.Contains(weekday)) return false;

                if (!TimeOnly.TryParse(p.From, out var from) || !TimeOnly.TryParse(p.To, out var to)) return true;

                var time = TimeOnly.FromDateTime(local.DateTime);
                return from <= to ? time >= from && time < to : time >= from || time < to;
            }
            case AutopostConditionType.DateRange:
                return (p.DateFrom <= 0 || unix >= p.DateFrom) && (p.DateTo <= 0 || unix <= p.DateTo);
            case AutopostConditionType.ChannelActivity:
                return await AutopostService.CountMessagesAsync([autopost.ChannelId],
                    unix - Math.Max(p.WindowMinutes, 1) * 60L) >= p.MinMessages;
            case AutopostConditionType.Cooldown:
                return unix - autopost.LastPostedAt >= Math.Max(p.Minutes, 0) * 60L;
            case AutopostConditionType.LastMessageNotAutopost:
                return !await LastMessageIsAutopostAsync(autopost.ChannelId);
            default:
                return true;
        }
    }

    private static async Task<bool> LastMessageIsAutopostAsync(ulong channelId)
    {
        var guild = CurrentApplication.TargetGuild;
        if (guild == null || !guild.Channels.TryGetValue(channelId, out var channel)) return false;

        var last = (await channel.GetMessagesAsync(1)).FirstOrDefault();
        if (last == null) return false;

        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand("SELECT EXISTS (SELECT 1 FROM autoposts WHERE @id = ANY(last_message_ids))");
        cmd.Parameters.AddWithValue("id", (long)last.Id);
        return await cmd.ExecuteScalarAsync() is true;
    }

    public static string Summarize(AutopostCondition condition,
        IReadOnlyDictionary<string, string>? positionNames = null)
    {
        var p = condition.Params;
        var not = condition.Negate;

        return condition.Type switch
        {
            AutopostConditionType.ApplicationPhase =>
                $"Bewerbungsphase {(string.IsNullOrEmpty(p.PositionId) ? "(beliebige Position)" : $"\"{PositionName(p.PositionId, positionNames)}\"")} {(not ? "geschlossen" : "offen")}",
            AutopostConditionType.TimeWindow =>
                $"{(not ? "Nicht " : "")}{p.From} bis {p.To} Uhr, {WeekdayText(p.Weekdays)}",
            AutopostConditionType.DateRange =>
                $"{(not ? "Außerhalb" : "Innerhalb")} {DateText(p.DateFrom)} bis {DateText(p.DateTo)}",
            AutopostConditionType.ChannelActivity =>
                $"{(not ? "Weniger als" : "Mindestens")} {p.MinMessages} Nachrichten in {p.WindowMinutes} Min",
            AutopostConditionType.Cooldown =>
                $"{(not ? "Höchstens" : "Mindestens")} {p.Minutes} Min seit dem letzten Post",
            AutopostConditionType.LastMessageNotAutopost =>
                not ? "Letzte Nachricht ist ein Autopost" : "Letzte Nachricht ist kein Autopost",
            _ => condition.Type.ToString()
        };
    }

    private static string PositionName(string positionId, IReadOnlyDictionary<string, string>? names)
    {
        return names != null && names.TryGetValue(positionId, out var name) ? name : positionId;
    }

    private static string DateText(long unix)
    {
        return unix <= 0
            ? "offen"
            : DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
    }

    private static readonly string[] WeekdayNames = ["Mo", "Di", "Mi", "Do", "Fr", "Sa", "So"];

    private static string WeekdayText(List<int> weekdays)
    {
        if (weekdays.Count == 0 || weekdays.Count == 7) return "jeden Tag";
        return string.Join(", ", weekdays.Where(d => d is >= 1 and <= 7).Order().Select(d => WeekdayNames[d - 1]));
    }

    public static string WeekdayName(int weekday)
    {
        return WeekdayNames[weekday - 1];
    }
}
