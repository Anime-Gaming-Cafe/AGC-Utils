#region

using System.Text;
using AGC_Management.Components;
using AGC_Management.Enums;

#endregion

namespace AGC_Management.Managers;

public class NotificationManager
{
    public static async Task<List<DiscordMember?>> GetSubscribedStaffs(DiscordChannel channel)
    {
        var cid = (long)channel.Id;
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand("SELECT user_id FROM subscriptions WHERE channel_id = @cid");
        cmd.Parameters.AddWithValue("cid", cid);
        await using var reader = await cmd.ExecuteReaderAsync();
        var L = new List<DiscordMember>();
        if (reader.HasRows)
        {
            while (await reader.ReadAsync())
            {
                var uid = (ulong)reader.GetInt64(0);
                var user = await CurrentApplication.DiscordClient.GetUserAsync(uid);
                var member = await channel.Guild.GetMemberAsync(uid);
                if (member == null)
                {
                    await SetMode(NotificationMode.Disabled, channel.Id, uid);
                    continue;
                }

                L.Add(member);
            }

            return L;
        }

        return L;
    }

    public static async Task SetMode(NotificationMode mode, ulong channel_id, ulong user_id)
    {
        var cid = (long)channel_id;
        var uid = (long)user_id;
        await RemoveMode(channel_id, user_id);
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd =
            con.CreateCommand("INSERT INTO subscriptions (user_id, channel_id, mode) VALUES (@uid, @cid, @mode)");
        cmd.Parameters.AddWithValue("mode", (int)mode);
        cmd.Parameters.AddWithValue("uid", uid);
        cmd.Parameters.AddWithValue("cid", cid);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task RemoveMode(ulong channel_id, ulong user_id)
    {
        var cid = (long)channel_id;
        var uid = (long)user_id;
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd =
            con.CreateCommand("DELETE FROM subscriptions WHERE user_id = @uid AND channel_id = @cid");
        cmd.Parameters.AddWithValue("uid", uid);
        cmd.Parameters.AddWithValue("cid", cid);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task ClearMode(ulong channel_id)
    {
        var cid = (long)channel_id;
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var cmd = con.CreateCommand("DELETE FROM subscriptions WHERE channel_id = @cid");
        cmd.Parameters.AddWithValue("cid", cid);
        await cmd.ExecuteNonQueryAsync();
    }

    public static string GetModeString(NotificationMode mode)
    {
		return mode switch
		{
			NotificationMode.Disabled => "Deaktiviert",
			NotificationMode.OnceMention => "Einmalig Erwähnen",
			NotificationMode.OnceDM => "Einmalig DM",
			NotificationMode.OnceBoth => "Einmalig Beides",
			NotificationMode.AlwaysMention => "Immer Erwähnen",
			NotificationMode.AlwaysDM => "Immer DM",
			NotificationMode.AlwaysBoth => "Immer Beides",
			_ => "Unbekannt",
		};
	}

    public static async Task<NotificationMode> GetCurrentMode(ulong channel_id, ulong user_id)
    {
        var cid = (long)channel_id;
        var uid = (long)user_id;
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd =
            con.CreateCommand("SELECT mode FROM subscriptions WHERE user_id = @uid AND channel_id = @cid");
        cmd.Parameters.AddWithValue("uid", uid);
        cmd.Parameters.AddWithValue("cid", cid);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (reader.HasRows)
        {
            await reader.ReadAsync();
            return (NotificationMode)reader.GetInt32(0);
        }

        return NotificationMode.Disabled;
    }

    private static List<DiscordActionRowComponent> GetPhase1Row(NotificationMode mode)
    {
        if (mode == NotificationMode.Disabled) return TicketComponents.GetNotificationManagerButtons();

        return TicketComponents.GetNotificationManagerButtonsEnabledNotify();
    }

    public static async Task RenderNotificationManager(DiscordInteraction interaction)
    {
        var customid = interaction.Data.CustomId;
        var mode = await GetCurrentMode(interaction.Channel.Id, interaction.User.Id);
        var enabled = mode != NotificationMode.Disabled ? "✅" : "❌";
        var modestr = GetModeString(mode);

        var content = new StringBuilder();
        content.Append($"**Benachrichtigungen für <#{interaction.Channel.Id}> / <@{interaction.User.Id}>**");
        content.Append("\n\n");
        content.Append($"**Aktiv:** {enabled}\n");
        content.Append($"**Gesetzter Modus:** {modestr}");
        var rows = GetPhase1Row(mode);
        var irb = new DiscordInteractionResponseBuilder();
        irb.AddEmbed(new DiscordEmbedBuilder().WithColor(DiscordColor.Blurple).WithDescription(content.ToString()));
        irb.AddComponents(rows).AsEphemeral();
        await interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, irb);
    }

    public static async Task RenderNotificationManagerWithUpdate(DiscordInteraction interaction)
    {
        var customid = interaction.Data.CustomId;
        var mode = await GetCurrentMode(interaction.Channel.Id, interaction.User.Id);
        var enabled = mode != NotificationMode.Disabled ? "✅" : "❌";
        var modestr = GetModeString(mode);

        var content = new StringBuilder();
        content.Append($"**Benachrichtigungen für <#{interaction.Channel.Id}> / <@{interaction.User.Id}>**");
        content.Append("\n\n");
        content.Append($"**Aktiv:** {enabled}\n");
        content.Append($"**Gesetzter Modus:** {modestr}");
        var rows = GetPhase1Row(mode);
        var irb = new DiscordInteractionResponseBuilder();
        irb.AddEmbed(new DiscordEmbedBuilder().WithColor(DiscordColor.Blurple).WithDescription(content.ToString()));
        irb.AddComponents(rows).AsEphemeral();
        await interaction.CreateResponseAsync(InteractionResponseType.UpdateMessage, irb);
    }


    public static async Task ChangeMode(DiscordInteraction interaction)
    {
        var customid = interaction.Data.CustomId;
        if (customid == "disable_notification")
        {
            await RemoveMode(interaction.Channel.Id, interaction.User.Id);
            await RenderNotificationManagerWithUpdate(interaction);
        }
        else if (customid == "enable_noti_mode1")
        {
            await SetMode(NotificationMode.OnceMention, interaction.Channel.Id, interaction.User.Id);
            await RenderNotificationManagerWithUpdate(interaction);
        }
        else if (customid == "enable_noti_mode2")
        {
            await SetMode(NotificationMode.OnceDM, interaction.Channel.Id, interaction.User.Id);
            await RenderNotificationManagerWithUpdate(interaction);
        }
        else if (customid == "enable_noti_mode3")
        {
            await SetMode(NotificationMode.OnceBoth, interaction.Channel.Id, interaction.User.Id);
            await RenderNotificationManagerWithUpdate(interaction);
        }
        else if (customid == "enable_noti_mode4")
        {
            await SetMode(NotificationMode.AlwaysMention, interaction.Channel.Id, interaction.User.Id);
            await RenderNotificationManagerWithUpdate(interaction);
        }
        else if (customid == "enable_noti_mode5")
        {
            await SetMode(NotificationMode.AlwaysDM, interaction.Channel.Id, interaction.User.Id);
            await RenderNotificationManagerWithUpdate(interaction);
        }
        else if (customid == "enable_noti_mode6")
        {
            await SetMode(NotificationMode.AlwaysBoth, interaction.Channel.Id, interaction.User.Id);
            await RenderNotificationManagerWithUpdate(interaction);
        }
    }
}