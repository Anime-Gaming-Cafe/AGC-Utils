#region

using AGC_Management.Services;

#endregion

namespace AGC_Management.Tasks;

public class TempVoiceTasks
{
    public async Task StartRemoveEmptyTempVoices(DiscordClient discord)
    {
        await Task.Delay(TimeSpan.FromSeconds(15));
        discord.Logger.LogInformation(
            "Datenbank verbunden. Starte automatische überprüfung auf leere TempVoices.");
        while (true)
        {
            await RemoveEmptyTempVoices(discord);
            await Task.Delay(TimeSpan.FromSeconds(90));
        }
    }

    private static async Task RemoveEmptyTempVoices(DiscordClient discord)
    {
        _ = Task.Run(async () =>
        {
            List<string> Query = new()
            {
                "channelid"
            };
            var all_channels =
                await DatabaseService.SelectDataFromTable("tempvoice", Query, null);
            foreach (var listed_channel in all_channels)
            {
                var channelid = listed_channel["channelid"].ToString();
                try
                {
                    var channel = await discord.TryGetChannelAsync(ulong.Parse(channelid));
                    if (channel == null)
                    {
                        Dictionary<string, (object value, string comparisonOperator)>
                            DeletewhereConditions = new()
                            {
                                { "channelid", (long.Parse(channelid), "=") }
                            };
                        //await channel.DeleteAsync();
                        //await DatabaseService.DeleteDataFromTable("tempvoice", DeletewhereConditions);
                        continue;
                    }

                    if (channel?.Users.Count == 0)
                    {
                        Dictionary<string, (object value, string comparisonOperator)>
                            DeletewhereConditions = new()
                            {
                                { "channelid", ((long)channel.Id, "=") }
                            };
                        await channel.DeleteAsync();
                        await DatabaseService.DeleteDataFromTable("tempvoice", DeletewhereConditions);
                    }
                }
                catch (Exception)
                {
                    Dictionary<string, (object value, string comparisonOperator)>
                        DeletewhereConditions = new()
                        {
                            { "channelid", (long.Parse(channelid), "=") }
                        };
                    //await channel.DeleteAsync();
                    //await DatabaseService.DeleteDataFromTable("tempvoice", DeletewhereConditions);
                }
            }
        });
    }
}