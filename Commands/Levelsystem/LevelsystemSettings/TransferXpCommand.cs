#region

using AGC_Management.Attributes;
using AGC_Management.Utils;
using DisCatSharp.ApplicationCommands.Attributes;
using DisCatSharp.ApplicationCommands.Context;
using DisCatSharp.Interactivity.Extensions;

#endregion

namespace AGC_Management.Commands.Levelsystem;

public partial class LevelSystemSettings
{
    [RequireTeamCat]
    [ACRequireStaffRole]
    [SlashCommand("transferxp", "Transferiere XP von einem Nutzer zu einem anderen", (long)Permissions.Administrator)]
    public static async Task TransferXp(InteractionContext ctx,
        [Option("sourceuser", "Der Nutzer von dem die XP abgezogen werden sollen.")]
        DiscordUser sourceuser,
        [Option("destinationuser", "Der Nutzer zu dem die XP hinzugefügt werden sollen.")]
        DiscordUser destinationuser)
    {
        await ctx.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().WithContent(
                "<a:loading_agc:1084157150747697203> Transfer wird vorbereitet..."));
        try
        {
            if (CurrentApplication.TargetGuild.Id == 750365461945778209)
            {
                var role = ctx.Guild.GetRole(ulong.Parse("1066374671131480105"));
                if (ctx.Member.Roles.Contains(role))
                {
                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                        "<:attention:1085333468688433232> **Fehler!** Du kannst keine XP transferieren, wenn du den Eventler bist!"));
                    return;
                }
            }
        }
        catch (Exception)
        {
        }


        if (sourceuser.IsBot)
        {
            await ctx.EditResponseAsync(
                new DiscordWebhookBuilder().WithContent(
                    "<:attention:1085333468688433232> **Fehler!** Der Quell-Nutzer ist ein Bot!"));
            return;
        }

        if (destinationuser.IsBot)
        {
            await ctx.EditResponseAsync(
                new DiscordWebhookBuilder().WithContent(
                    "<:attention:1085333468688433232> **Fehler!** Der Ziel-Nutzer ist ein Bot!"));
            return;
        }

        if (sourceuser.Id == destinationuser.Id)
        {
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                "<:attention:1085333468688433232> **Fehler!** Die Nutzer müssen unterschiedlich sein!"));
            return;
        }

        await LevelUtils.AddUserToDbIfNot(sourceuser);
        await LevelUtils.AddUserToDbIfNot(destinationuser);

        var embed = new DiscordEmbedBuilder();
        embed.WithTitle("XP Transfer");
        embed.WithDescription(
            $"Möchtest du den Transfer von ``{sourceuser.Username}`` <a:ani_arrow:1197137691347787877> ``{destinationuser.Username}`` wirklich durchführen?\n" +
            $"```diff\n" +
            $"- {sourceuser.Username}: {Converter.FormatWithCommas(await LevelUtils.GetXp(sourceuser.Id))} -> 0 XP \n" +
            $"+ {destinationuser.Username}: {Converter.FormatWithCommas(await LevelUtils.GetXp(destinationuser.Id))} -> {Converter.FormatWithCommas(await LevelUtils.GetXp(sourceuser.Id) + await LevelUtils.GetXp(destinationuser.Id))} XP```");
        embed.WithColor(BotConfig.GetEmbedColor());
        embed.WithFooter($"Dieser Vorgang kann nicht rückgängig gemacht werden! | {ctx.User.Username}",
            ctx.User.AvatarUrl);
        var msg = await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed.Build())
            .AddComponents(new DiscordButtonComponent(ButtonStyle.Primary, "transferconfirm", "Ja"),
                new DiscordButtonComponent(ButtonStyle.Danger, "transfercancel", "Nein")));
        var result = await msg.WaitForButtonAsync(ctx.User, TimeSpan.FromSeconds(120));
        if (result.TimedOut)
        {
            var errorMsg = new DiscordEmbedBuilder().WithTitle("Fehler")
                .WithDescription(
                    $"<:attention:1085333468688433232> **Fehler!** Der Transfer von ``{sourceuser.Username}`` <a:ani_arrow:1197137691347787877> ``{destinationuser.Username}`` wurde abgebrochen, da du nicht rechtzeitig geantwortet hast!")
                .WithColor(DiscordColor.Orange).WithFooter($"{ctx.User.Username}", ctx.User.AvatarUrl);
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(errorMsg.Build()));
            return;
        }

        if (result.Result.Id == "transferconfirm")
        {
            await ctx.EditResponseAsync(
                new DiscordWebhookBuilder().WithContent(
                    "<a:loading_agc:1084157150747697203> XP werden transferiert..."));
            var oldxp = await LevelUtils.GetXp(sourceuser.Id);
            var destinationoldxp = await LevelUtils.GetXp(destinationuser.Id);
            await LevelUtils.TransferXp(sourceuser.Id, destinationuser.Id);

            var successMsg = new DiscordEmbedBuilder().WithTitle("Erfolg")
                .WithDescription(
                    $"<:success:1085333481820790944> **Erfolgreich!** Der Transfer von ``{sourceuser.Username}`` <a:ani_arrow:1197137691347787877> ``{destinationuser.Username}`` wurde durchgeführt! \n" +
                    $"```diff\n" +
                    $"- {sourceuser.Username}: {Converter.FormatWithCommas(oldxp)} -> 0 XP \n" +
                    $"+ {destinationuser.Username}: {Converter.FormatWithCommas(destinationoldxp)} -> {Converter.FormatWithCommas(destinationoldxp + await LevelUtils.GetXp(destinationuser.Id))} XP```")
                .WithColor(DiscordColor.Green).WithFooter($"{ctx.User.Username}", ctx.User.AvatarUrl);
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(successMsg.Build()));
            await LoggingUtils.LogXpTransfer(sourceuser.Id, destinationuser.Id, oldxp, ctx.User.Id);
        }
        else if (result.Result.Id == "transfercancel")
        {
            var cancelMsg = new DiscordEmbedBuilder().WithTitle("Abgebrochen")
                .WithDescription(
                    $"<:attention:1085333468688433232> **Fehler!** Der Transfer von ``{sourceuser.Username}`` <a:ani_arrow:1197137691347787877> ``{destinationuser.Username}`` wurde abgebrochen!")
                .WithColor(DiscordColor.Red).WithFooter($"{ctx.User.Username}", ctx.User.AvatarUrl);
            await ctx.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(cancelMsg.Build()));
        }
    }
}