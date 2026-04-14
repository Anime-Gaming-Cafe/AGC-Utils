#region

using AGC_Management.Attributes;
using AGC_Management.Services;

#endregion

namespace AGC_Management.Commands.Moderation;

public sealed class RemoveVorstellungsCooldownCommand : BaseCommandModule
{
    [Command("removevcooldown")]
    [RequireDatabase]
    [RequireStaffRole]
    [RequireTeamCat]
    public async Task VCooldown(CommandContext ctx, DiscordUser user)
    {
		await using NpgsqlConnection conn = new(DatabaseService.GetConnectionString());
		await conn.OpenAsync();
		var sql = "DELETE FROM vorstellungscooldown WHERE user_id = @userid";
		await using NpgsqlCommand command = new(sql, conn);
		command.Parameters.AddWithValue("@userid", (long)user.Id);

		var affected = await command.ExecuteNonQueryAsync();

		var ue = new DiscordEmbedBuilder()
			.WithTitle("Cooldown Entfernt").WithDescription(
				$"{user.UsernameWithDiscriminator} kann nun wieder eine Vorstellung posten.")
			.WithColor(BotConfig.GetEmbedColor()).Build();
		await ctx.RespondAsync(ue);
	}
}