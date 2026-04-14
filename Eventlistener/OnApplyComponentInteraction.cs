#region

#endregion

namespace AGC_Management.Eventlistener;

[EventHandler]
public sealed class OnApplyComponentInteraction : BaseCommandModule
{
    [Event]
    public Task ComponentInteractionCreated(DiscordClient client, ComponentInteractionCreateEventArgs args)
    {
        _ = Task.Run(async () =>
            {
                var cid = args.Interaction.Data.CustomId;
                var values = args.Interaction.Data.Values;
                if (cid != "applypanelselector") return;
                var randomid = new Random();
                var ncid = randomid.Next(100000, 999999).ToString();
                var pos = $"{values[0][..1].ToUpper()}{values[0][1..]}";
                var applicable = await IsApplicable(pos.ToLower());
                Console.WriteLine(applicable);
                if (!applicable)
                {
                    var embed = new DiscordEmbedBuilder();
                    embed.WithTitle("Bewerbung");
                    embed.WithDescription($"Die Position ``{pos}`` ist aktuell nicht bewerbbar.");
                    embed.WithColor(DiscordColor.Red);
                    await args.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder().AddEmbed(embed).AsEphemeral());
                    return;
                }

                bool useHttps;
                try
                {
                    useHttps = bool.Parse(BotConfig.GetConfig()["WebUI"]["UseHttps"]);
                }
                catch
                {
                    useHttps = false;
                }

                string dashboardUrl;
                try
                {
                    dashboardUrl = BotConfig.GetConfig()["WebUI"]["DashboardURL"];
                }
                catch
                {
                    dashboardUrl = "localhost";
                }

                var url = $"{(useHttps ? "https" : "http")}://{dashboardUrl}/apply/{pos.ToLower()}";
                var _embed = new DiscordEmbedBuilder();
                _embed.WithTitle("Bewerbung");
                _embed.WithDescription($"[Klicke hier um dich für die Position ``{pos}`` zu bewerben]({url})");
                _embed.WithColor(DiscordColor.Green);
                await args.Interaction.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder().AddEmbed(_embed).AsEphemeral());
            }
        );

        return Task.CompletedTask;
    }


    private static async Task<bool> IsApplicable(string Position)
    {
        var db = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        await using var command =
            db.CreateCommand("SELECT applicable FROM applicationcategories WHERE positionid = @positionname");
        command.Parameters.AddWithValue("positionname", Position);
        await using var reader = await command.ExecuteReaderAsync();
        if (reader.HasRows)
        {
            await reader.ReadAsync();
            return reader.GetBoolean(0);
        }

        return false;
    }
}