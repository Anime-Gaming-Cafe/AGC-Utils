using System.Data;
using AGC_Management.Services.DatabaseHandler;
using DisCatSharp;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AGC_Management.Commands;

public class ModerationSystemTasks
{
    public async Task StartRemovingWarnsPeriodically(DiscordClient discord)
    {
        if (DatabaseService.IsConnected())
            while (true)
            {
                await RemoveWarnsOlderThan7Days(discord);
                await Task.Delay(TimeSpan.FromMinutes(5));
            }

        discord.Logger.LogWarning("Database not connected.. Disable Periodically Expired Warn check.");
    }

    private async Task RemoveWarnsOlderThan7Days(DiscordClient discord)
    {
        discord.Logger.LogInformation("Pr�fe auf abgelaufene Warns");
        var warnlist = new List<dynamic>();
        int expireTime = (int)DateTimeOffset.UtcNow.AddSeconds(-604800).ToUnixTimeSeconds();
        string deleteQuery = $"DELETE FROM warns WHERE datum < '{expireTime}' AND perma = 'False'";
        string selectQuery = $"SELECT * FROM warns WHERE datum < '{expireTime}' AND perma = 'False'";

        await using (NpgsqlDataReader warnReader = DatabaseService.ExecuteQuery(selectQuery))
        {
            while (warnReader.Read())
            {
                var warn = new
                {
                    UserId = warnReader.GetInt64(0),
                    PunisherId = discord.CurrentUser.Id,
                    Datum = warnReader.GetInt32(2),
                    Description = warnReader.GetString(3) + " **[AUTO] EXPIRED WARN!**",
                    Perma = warnReader.GetBoolean(4),
                    CaseId = "EXPIRED-" + warnReader.GetString(5)
                };
                warnlist.Add(warn);
            }
        }

        foreach (var warn in warnlist)
        {
            string insertQuery = "INSERT INTO flags (userid, punisherid, datum, description, caseid) VALUES " +
                                 "(@UserId, @PunisherId, @Datum, @Description, @CaseId)";

            await using (NpgsqlConnection connection = new(DatabaseService.GetConnectionString()))
            {
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync();

                await using (NpgsqlCommand command = new(insertQuery, connection))
                {
                    command.Parameters.AddWithValue("@UserId", warn.UserId);
                    command.Parameters.AddWithValue("@PunisherId", warn.PunisherId);
                    command.Parameters.AddWithValue("@Datum", warn.Datum);
                    command.Parameters.AddWithValue("@Description", warn.Description);
                    command.Parameters.AddWithValue("@CaseId", warn.CaseId);

                    await command.ExecuteNonQueryAsync();
                }
            }

            // Create a new connection for the delete query
            await using (NpgsqlConnection connectionDelete = new(DatabaseService.GetConnectionString()))
            {
                if (connectionDelete.State != ConnectionState.Open)
                    await connectionDelete.OpenAsync();

                using (NpgsqlCommand commandDelete = new(deleteQuery, connectionDelete))
                {
                    int remWarn = await commandDelete.ExecuteNonQueryAsync();
                    discord.Logger.LogInformation($"{remWarn} Abgelaufene Verwarnungen in Flags verschoben.");
                }
            }
        }
    }
}