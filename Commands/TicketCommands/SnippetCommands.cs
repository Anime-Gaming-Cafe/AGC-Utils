#region

using System.Text;
using System.Text.RegularExpressions;
using AGC_Management.Attributes;

#endregion

namespace AGC_Management.Commands;

[Group("snippetmanager")]
public class SnippetManagerCommands : BaseCommandModule
{
    [GroupCommand]
    [TicketRequireStaffRole]
    public async Task SnippetManager_Help(CommandContext ctx)
    {
        await SnippetManager(ctx);
    }

    [Command("help")]
    [TicketRequireStaffRole]
    public async Task SnippetManager(CommandContext ctx)
    {
        var prefix = ctx.Prefix;
        var eb = new DiscordEmbedBuilder().WithTitle("Snippet Manager")
            .WithDescription("Verwaltung der Snippets")
            .WithColor(DiscordColor.Red)
            .WithFooter("AGC Support-System", ctx.Guild.IconUrl)
            .AddField(new DiscordEmbedField("Hinzufügen", $"`{prefix}snippetmanager add <name> <content>`"))
            .AddField(new DiscordEmbedField("Entfernen", $"`{prefix}snippetmanager remove <name>`"))
            .AddField(new DiscordEmbedField("Auflisten", $"`{prefix}snippetmanager list`"))
            .AddField(new DiscordEmbedField("Suchen", $"`{prefix}snippetmanager search <name>`"))
            .AddField(new DiscordEmbedField("Kürzelsuche", $"`{prefix}snippetmanager shortcutsearch <dein_text>`"))
            .AddField(new DiscordEmbedField("Hilfe", $"`{prefix}snippetmanager help`"))
            .Build();
        await ctx.RespondAsync(eb);
    }


    [Command("add")]
    [TicketRequireStaffRole]
    public async Task AddSnippet(CommandContext ctx, string name, [RemainingText] string content)
    {
        if (!await SnippetManagerHelper.CreateAsync(name, content))
        {
            var ebe = new DiscordEmbedBuilder()
                .WithDescription($"Snippet `{name}` existiert bereits!")
                .WithColor(DiscordColor.Red);
            await ctx.RespondAsync(ebe.Build());
            return;
        }

        var eb = new DiscordEmbedBuilder()
            .WithDescription($"Snippet `{name}` wurde hinzugfügt!")
            .WithFooter("AGC Support-System", ctx.Guild.IconUrl)
            .WithColor(DiscordColor.Green);
        await ctx.RespondAsync(eb);
    }


    [Command("remove")]
    [TicketRequireStaffRole]
    public async Task RemoveSnippet(CommandContext ctx, string name)
    {
        var removed = await SnippetManagerHelper.DeleteAsync(name);
        var eb = new DiscordEmbedBuilder();

        if (removed)
            eb.WithDescription($"Snippet `{name}` wurde erfolgreich entfernt!")
                .WithColor(DiscordColor.Green);
        else
            eb.WithDescription($"Snippet `{name}` existiert nicht!")
                .WithColor(DiscordColor.Red);

        eb.WithFooter("AGC Support-System", ctx.Guild.IconUrl);
        await ctx.RespondAsync(eb.Build());
    }


    [Command("list")]
    [TicketRequireStaffRole]
    public async Task ListSnippets(CommandContext ctx)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();


        await using var cmd = con.CreateCommand("SELECT snip_id FROM snippets");
        await using var reader = await cmd.ExecuteReaderAsync();

        var eb = new DiscordEmbedBuilder()
            .WithTitle("Snippets")
            .WithFooter("AGC Support-System", ctx.Guild.IconUrl)
            .WithColor(DiscordColor.Gold);

        var snippetFound = false;
        StringBuilder sb = new();

        while (await reader.ReadAsync())
        {
            sb.Append($"`{reader.GetString(0)}` ");
            snippetFound = true;
        }

        if (snippetFound)
        {
            eb.WithDescription(sb.ToString());
        }
        else
        {
            eb.WithDescription("Keine Snippets gefunden!");
            eb.WithColor(DiscordColor.Red);
        }

        await reader.CloseAsync();
        await ctx.RespondAsync(eb.Build());
    }


    [Command("search")]
    [TicketRequireStaffRole]
    public async Task SearchSnippets(CommandContext ctx, string snippet_id)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd = con.CreateCommand("SELECT snipped_text FROM snippets WHERE snip_id = @snippet_id");
        cmd.Parameters.AddWithValue("snippet_id", snippet_id);
        await using var reader = await cmd.ExecuteReaderAsync();
        var eb = new DiscordEmbedBuilder()
            .WithTitle("Snippet Suche")
            .WithColor(DiscordColor.Gold)
            .WithFooter("AGC Support-System", ctx.Guild.IconUrl);
        if (await reader.ReadAsync())
        {
            var snipped_text = reader.GetString(0);
            snipped_text = SnippetManagerHelper.FormatStringWithVariables(snipped_text);
            eb.WithDescription(snipped_text);
        }
        else
        {
            eb.WithDescription("Snippet nicht gefunden!");
        }

        await reader.CloseAsync();
        await ctx.RespondAsync(eb.Build());
    }

    [Command("shortcutsearch")]
    [TicketRequireStaffRole]
    public async Task ShortcutSnippets(CommandContext ctx)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();


        var words = Regex.Split(ctx.Message.Content, @"\W+");

        var eb = new DiscordEmbedBuilder()
            .WithTitle("Snippet Suche")
            .WithColor(DiscordColor.Gold)
            .WithFooter("AGC Support-System", ctx.Guild.IconUrl);

        var snippetFound = false;

        foreach (var word in words)
        {
            await using var cmd =
                con.CreateCommand("SELECT snip_id, snipped_text FROM snippets WHERE snipped_text ~* @word");

            cmd.Parameters.AddWithValue("word", $"\\m{word}\\M");
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                eb.AddField(new DiscordEmbedField(reader.GetString(0), reader.GetString(1)));
                snippetFound = true;
            }

            await reader.CloseAsync();
        }

        if (!snippetFound)
        {
            eb.WithDescription("Kein passendes Snippetkürzel gefunden!");
            eb.WithColor(DiscordColor.Red);
        }

        await ctx.RespondAsync(eb.Build());
    }
}