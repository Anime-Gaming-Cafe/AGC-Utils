#region

using AGC_Management.Entities.Autoposts;
using AGC_Management.Enums.Autoposts;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Services;

public static class AutopostSeedData
{
    private const ulong MainChannelId = 750365462524854331;
    private const string LegacyColor = "2F84A2";

    public static List<Autopost> Build()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var ticket = new Autopost
        {
            AutopostId = ToolSet.GenerateCaseID(),
            Name = "Ticket-Reminder",
            ChannelId = MainChannelId,
            TriggerType = AutopostTriggerType.Messages,
            Threshold = 2500,
            Steps =
            [
                Step(new AutopostVariant
                {
                    Embed = new AutopostEmbed
                    {
                        AuthorName = "・REMINDER - TICKETSYSTEM・",
                        AuthorIconUrl = "{guildicon}",
                        ThumbnailUrl = "{guildicon}",
                        Color = LegacyColor,
                        Description =
                            "Ihr habt Fragen, Probleme oder einen Report bezüglich unseres Servers? Scheut euch nicht uns zu kontaktieren! " +
                            "Um schnellstmöglich Hilfe zu erhalten, schreibt bitte ein Ticket über unser Ticketsystem <#826083443489636372>. " +
                            "Wir freuen uns über jeden Report und jede Anmerkung, um euch einen möglichst angenehmen Aufenthalt im Anime & Gaming Café zu bieten."
                    },
                    Buttons =
                    [
                        new AutopostButton
                        {
                            Label = "Zum Support",
                            Url = "https://discord.com/channels/750365461945778209/826083443489636372/975104971332788234"
                        }
                    ]
                })
            ]
        };

        var socials = new Autopost
        {
            AutopostId = ToolSet.GenerateCaseID(),
            Name = "Socials",
            ChannelId = MainChannelId,
            TriggerType = AutopostTriggerType.AfterAutopost,
            ParentAutopostId = ticket.AutopostId,
            ParentDelaySeconds = 300,
            Steps =
            [
                Step(new AutopostVariant
                {
                    Embed = new AutopostEmbed
                    {
                        AuthorName = "・REMINDER - SOCIALS・",
                        AuthorIconUrl = "{guildicon}",
                        Color = LegacyColor,
                        Description = "Wir sind auch auf Sozialen Medien und im Web vertreten. Schaut doch rein!"
                    },
                    Buttons =
                    [
                        new AutopostButton { Label = "Website", Url = "https://animegamingcafe.de/", Emoji = "910164710521962526" },
                        new AutopostButton { Label = "Vote für AGC", Url = "https://top.gg/servers/750365461945778209/vote", Emoji = "910167295043702784" },
                        new AutopostButton { Label = "Patreon", Url = "https://www.patreon.com/animegamingcafe/membership", Emoji = "1057333836637294632" },
                        new AutopostButton { Label = "Instagram", Url = "https://www.instagram.com/animegamingcafe/", Emoji = "910164710102544404" }
                    ]
                })
            ]
        };

        var newcomers = new Autopost
        {
            AutopostId = ToolSet.GenerateCaseID(),
            Name = "Neulinge",
            ChannelId = MainChannelId,
            TriggerType = AutopostTriggerType.Joins,
            Threshold = 75,
            SubtractLeaves = true,
            Steps =
            [
                Step(new AutopostVariant
                {
                    Embed = new AutopostEmbed
                    {
                        Title = "Hey ihr neuen! <:AGC_02Hi:796339958272884736>",
                        Color = "",
                        Description =
                            "An all unsere Neuankömmlinge! Ihr habt die Möglichkeit euch bei <#752701273043763221> einige Extrarollen zu geben! " +
                            "Vorbeischauen lohnt sich! (Einen Blick in die <#750365462235316250> zu werfen schadet auch nie)"
                    }
                })
            ]
        };

        foreach (var autopost in new[] { ticket, socials, newcomers })
        {
            autopost.CreatedAt = now;
            autopost.UpdatedAt = now;
            autopost.CounterSince = now;
        }

        return [ticket, socials, newcomers];
    }

    private static AutopostStep Step(AutopostVariant variant)
    {
        return new AutopostStep { Variants = [variant] };
    }
}
