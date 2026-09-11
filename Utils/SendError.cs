#region

#endregion

namespace AGC_Management.Utils;

public static class ErrorReporting
{
    public static async Task SendErrorToDev(DiscordClient client, DiscordUser? user,
        Exception exception)
    {
        var botOwner = await client.GetUserAsync(GlobalProperties.BotOwnerId);
        var embed2 = new DiscordEmbedBuilder();
        embed2.WithTitle("Fehler aufgetreten!");
        embed2.WithDescription($"Es ist ein Fehler aufgetreten. \n\n" +
                               $"__Fehlermeldung:__\n" +
                               $"```{exception.Message}```\n" +
                               $"__Stacktrace:__\n" +
                               $"```{exception.StackTrace}```\n" +
                               $"__User:__\n" +
                               $"``{user.UsernameWithDiscriminator}`` - ``{user.Id}``\n");
        embed2.WithColor(DiscordColor.Red);
        try
        {
            //await botOwner.SendMessageAsync(embed2);
        }
        catch (Exception)
        {
            // ignored
        }

        try
        {
            var errortrackingguildid = GlobalProperties.AGCGuild.Id;
            var errortrackingguild = await client.GetGuildAsync(errortrackingguildid);
            var errortrackingchannel = errortrackingguild.GetChannel(GlobalProperties.ErrorTrackingChannelId);
            await errortrackingchannel.SendMessageAsync(embed2);
        }
        catch (Exception)
        {
        }

        CurrentApplication.DiscordClient.Logger.LogError(
            $"Exception occured: {exception.GetType()}: {exception.Message}");
        CurrentApplication.DiscordClient.Logger.LogError($"Stacktrace: {exception.StackTrace}");
    }

    public static async Task SendErrorToDev(DiscordClient client, Exception exception)
    {
        var botOwner = await client.GetUserAsync(GlobalProperties.BotOwnerId);
        var embed2 = new DiscordEmbedBuilder();
        embed2.WithTitle("Fehler aufgetreten!");
        embed2.WithDescription($"Es ist ein Fehler aufgetreten. \n\n" +
                               $"__Fehlermeldung:__\n" +
                               $"```{exception.Message}```\n" +
                               $"__Stacktrace:__\n" +
                               $"```{exception.StackTrace}```\n");
        embed2.WithColor(DiscordColor.Red);
        try
        {
            //await botOwner.SendMessageAsync(embed2);
        }
        catch (Exception)
        {
            // ignored
        }

        try
        {
            var errortrackingguildid = GlobalProperties.AGCGuild.Id;
            var errortrackingguild = await client.GetGuildAsync(errortrackingguildid);
            var errortrackingchannel = errortrackingguild.GetChannel(GlobalProperties.ErrorTrackingChannelId);
            await errortrackingchannel.SendMessageAsync(embed2);
        }
        catch (Exception)
        {
            await SendErrorToDev(client, exception);
        }

        CurrentApplication.DiscordClient.Logger.LogError(
            $"Exception occured: {exception.GetType()}: {exception.Message}");
        CurrentApplication.DiscordClient.Logger.LogError($"Stacktrace: {exception.StackTrace}");
    }
}