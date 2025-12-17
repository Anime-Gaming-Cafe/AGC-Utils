#region

using AGC_Management;
using AGC_Management.Services;
using AGC_Management.TempVoice;
using AGC_Management.Utils;
using DisCatSharp.Exceptions;

#endregion

[EventHandler]
public class TempVCEventHandler : TempVoiceHelper
{
    [Event]
    private Task VoiceStateUpdated(DiscordClient sender, VoiceStateUpdateEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (e.Guild.Id != ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["ServerId"])) return;
                var sessionresult = new List<Dictionary<string, object>>();
                var usersession = new List<dynamic>();
                List<string> Query =
				[
					"userid", "channelname", "channelbitrate", "channellimit",
                    "blockedusers", "permitedusers", "locked", "hidden", "sessionskip", "channelmods"
                ];
                Dictionary<string, object> WhereCondiditons = new()
                {
                    { "userid", (long)e.User.Id }
                };
                sessionresult = await DatabaseService.SelectDataFromTable("tempvoicesession", Query, WhereCondiditons);
                if (sessionresult.Count == 0)
                {
                    var all_channels = await GetAllTempChannels();
                    if ((e.Before?.Channel != null && e.After?.Channel == null) ||
                        (e.Before?.Channel != null && e.After?.Channel != null))
                        if (all_channels.Contains((long)e.Before.Channel.Id))
                            if (e.Before.Channel.Users.Count == 0)
                                try
                                {
                                    Dictionary<string, (object value, string comparisonOperator)>
                                        DeletewhereConditions = new()
                                        {
                                            { "channelid", ((long)e.Before.Channel.Id, "=") }
                                        };

                                    await e.Before.Channel.DeleteAsync();
                                    await DatabaseService.DeleteDataFromTable("tempvoice", DeletewhereConditions);
                                }
                                catch (Exception)
                                {
                                    try
                                    {
                                        Dictionary<string, (object value, string comparisonOperator)>
                                            DeletewhereConditions = new()
                                            {
                                                { "channelid", ((long)e.Before.Channel.Id, "=") }
                                            };
                                    }
                                    catch (NullReferenceException)
                                    {
                                        //await DatabaseService.DeleteDataFromTable("tempvoice", DeletewhereConditions);
                                    }


                                    //await DatabaseService.DeleteDataFromTable("tempvoice", DeletewhereConditions);
                                }

                    if ((e.After?.Channel != null && e.Before?.Channel == null) ||
                        (e.Before?.Channel != null && e.After?.Channel != null))
                    {
						if (ulong.TryParse(GetVCConfig("Creation_Channel_ID"), out ulong creationChannelId))
							if (e.After.Channel.Id == creationChannelId)
							{
								var m = await e.Guild.GetMemberAsync(e.User.Id);

								var defaultVcName = GetVCConfig("Default_VC_Name") ?? $"{m.Username}'s Channel";
								defaultVcName = string.IsNullOrWhiteSpace(defaultVcName)
									? $"{m.Username}'s Channel"
									: defaultVcName;
								defaultVcName = defaultVcName.Replace("{username}", m.Username)
									.Replace("{userid}", m.Id.ToString())
									.Replace("{fullname}", m.UsernameWithDiscriminator);


								var voice = await e.After?.Guild.CreateVoiceChannelAsync
								(defaultVcName, e.After.Channel.Parent,
									96000, 0, qualityMode: VideoQualityMode.Full);
								var overwrites = voice.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();
								Dictionary<string, object> data = new()
								{
									{ "ownerid", (long)e.User.Id },
									{ "channelid", (long)voice.Id },
									{ "lastedited", (long)0 },
									{ "statuslastedited", (long)0 }
								};
								await DatabaseService.InsertDataIntoTable("tempvoice", data);
								try
								{
									await m.ModifyAsync(x => x.VoiceChannel = voice);
								}
								catch (Exception)
								{
									Dictionary<string, (object value, string comparisonOperator)>
										DeletewhereConditions = new()
										{
											{ "channelid", ((long)voice.Id, "=") }
										};
									await voice.DeleteAsync();
									await DatabaseService.DeleteDataFromTable("tempvoice", DeletewhereConditions);
									return;
								}

								overwrites = overwrites.Merge(m,
									Permissions.ManageChannels | Permissions.MoveMembers | Permissions.UseVoice |
									Permissions.AccessChannels, Permissions.None);
								await voice.ModifyPositionInCategoryAsync(e.After.Channel.Position + 1);
								await voice.ModifyAsync(async x => { x.PermissionOverwrites = overwrites; });
							}
					}
                }
                else if (sessionresult.Count == 1)
                {
                    var all_channels = await GetAllTempChannels();
                    if ((e.Before?.Channel != null && e.After?.Channel == null) ||
                        (e.Before?.Channel != null && e.After?.Channel != null))
                        if (all_channels.Contains((long)e.Before.Channel.Id))
                            if (e.Before.Channel.Users.Count == 0)
                                try
                                {
                                    Dictionary<string, (object value, string comparisonOperator)>
                                        DeletewhereConditions = new()
                                        {
                                            { "channelid", ((long)e.Before.Channel.Id, "=") }
                                        };

                                    await e.Before.Channel.DeleteAsync();
                                    await DatabaseService.DeleteDataFromTable("tempvoice", DeletewhereConditions);
                                }
                                catch (Exception)
                                {
                                    try
                                    {
                                        Dictionary<string, (object value, string comparisonOperator)>
                                            DeletewhereConditions = new()
                                            {
                                                { "channelid", ((long)e.Before.Channel.Id, "=") }
                                            };
                                    }
                                    catch (NullReferenceException)
                                    {
                                        //await DatabaseService.DeleteDataFromTable("tempvoice", DeletewhereConditions);
                                    }
                                }

                    if ((e.After?.Channel != null && e.Before?.Channel == null) ||
                        (e.Before?.Channel != null && e.After?.Channel != null))
                    {
						if (ulong.TryParse(GetVCConfig("Creation_Channel_ID"), out ulong creationChannelId))
							if (e.After.Channel.Id == creationChannelId)
							{
								var sessionskipActive = false;
								foreach (var item in sessionresult)
								{
									sessionskipActive = (bool)item["sessionskip"];
									break;
								}

								long userId = 0;
								var channelName = string.Empty;
								var channelBitrate = 0;
								var channelLimit = 0;
								var blockedusers = string.Empty;
								var permitedusers = string.Empty;
								var locked = false;
								var hidden = false;
								var channelMods = string.Empty;
								foreach (var item in sessionresult)
								{
									userId = (long)item["userid"];
									channelName = (string)item["channelname"];
									channelBitrate = (int)item["channelbitrate"];
									channelLimit = (int)item["channellimit"];
									blockedusers = item["blockedusers"] != null
										? (string)item["blockedusers"]
										: string.Empty;
									permitedusers = item["permitedusers"] != null
										? (string)item["permitedusers"]
										: string.Empty;
									locked = (bool)item["locked"];
									hidden = (bool)item["hidden"];
									channelMods = item["channelmods"] != null
										? (string)item["channelmods"]
										: string.Empty;
									break;
								}

								var blockeduserslist =
									blockedusers.Split([", "], StringSplitOptions.None).ToList();
								var permiteduserslist =
									permitedusers.Split([", "], StringSplitOptions.None).ToList();

								var m = await e.Guild.GetMemberAsync((ulong)userId);
								DiscordChannel voice;
								if (sessionskipActive)
								{
									var defaultVcName = GetVCConfig("Default_VC_Name") ?? $"{m.Username}'s Channel";
									defaultVcName = string.IsNullOrWhiteSpace(defaultVcName)
										? $"{m.Username}'s Channel"
										: defaultVcName;
									defaultVcName = defaultVcName.Replace("{username}", m.Username)
										.Replace("{userid}", m.Id.ToString())
										.Replace("{fullname}", m.UsernameWithDiscriminator);


									voice = await e.After?.Guild.CreateVoiceChannelAsync
									(defaultVcName, e.After.Channel.Parent,
										96000, 0, qualityMode: VideoQualityMode.Full);
									var overwrites2 = voice.PermissionOverwrites.Select(x => x.ConvertToBuilder())
										.ToList();
									Dictionary<string, object> data2 = new()
									{
										{ "ownerid", (long)e.User.Id },
										{ "channelid", (long)voice.Id },
										{ "lastedited", (long)0 },
										{ "statuslastedited", (long)0 }
									};
									await DatabaseService.InsertDataIntoTable("tempvoice", data2);
									try
									{
										await m.ModifyAsync(x => x.VoiceChannel = voice);
									}
									catch (Exception)
									{
										Dictionary<string, (object value, string comparisonOperator)>
											DeletewhereConditions = new()
											{
												{ "channelid", ((long)voice.Id, "=") }
											};
										await voice.DeleteAsync();
										await DatabaseService.DeleteDataFromTable("tempvoice", DeletewhereConditions);
										return;
									}

									overwrites2 = overwrites2.Merge(m,
										Permissions.ManageChannels | Permissions.MoveMembers | Permissions.UseVoice |
										Permissions.AccessChannels, Permissions.None);
									await voice.ModifyPositionInCategoryAsync(e.After.Channel.Position + 1);
									await voice.ModifyAsync(async x => { x.PermissionOverwrites = overwrites2; });

									// write sessionskip false to db
									var conn = CurrentApplication.ServiceProvider
										.GetRequiredService<NpgsqlDataSource>();
									await using var cmd = conn.CreateCommand(
										"UPDATE tempvoicesession SET sessionskip = @sessionskip WHERE userid = @userid");
									cmd.Parameters.AddWithValue("sessionskip", false);
									// execute command
									await cmd.ExecuteNonQueryAsync();
									return;
								}

								voice = await e.After?.Guild.CreateVoiceChannelAsync(channelName,
									e.After.Channel.Parent, channelBitrate, channelLimit,
									qualityMode: VideoQualityMode.Full);
								Dictionary<string, object> data = new()
								{
									{ "ownerid", (long)e.User.Id },
									{ "channelid", (long)voice.Id },
									{ "lastedited", (long)0 },
									{ "statuslastedited", (long)0 }
								};
								await DatabaseService.InsertDataIntoTable("tempvoice", data);
								try
								{
									await m.ModifyAsync(x => x.VoiceChannel = voice);
								}
								catch (Exception)
								{
									Dictionary<string, (object value, string comparisonOperator)>
										DeletewhereConditions = new()
										{
											{ "channelid", ((long)voice.Id, "=") }
										};
									await voice.DeleteAsync();
									await DatabaseService.DeleteDataFromTable("tempvoice", DeletewhereConditions);
									return;
								}

								var overwrites = voice.PermissionOverwrites.Select(x => x.ConvertToBuilder()).ToList();
								await voice.ModifyPositionInCategoryAsync(e.After.Channel.Position + 1);
								overwrites = overwrites.Merge(m,
									Permissions.ManageChannels | Permissions.MoveMembers | Permissions.UseVoice |
									Permissions.AccessChannels, Permissions.None);
								if (locked)
									overwrites = overwrites.Merge(voice.Guild.EveryoneRole, Permissions.None,
										Permissions.UseVoice);

								if (hidden)
									overwrites = overwrites.Merge(voice.Guild.EveryoneRole, Permissions.None,
										Permissions.AccessChannels);


								foreach (var user in blockeduserslist)
									if (ulong.TryParse(user, out var blockeduser))
										try
										{
											var userid = await e.After.Guild.GetMemberAsync(blockeduser);


											overwrites = overwrites.Merge(userid, Permissions.None,
												Permissions.UseVoice);
										}
										catch (NotFoundException)
										{
										}

								foreach (var user in permiteduserslist)
									if (ulong.TryParse(user, out var permiteduser))
										try
										{
											var userid = await e.After.Guild.GetMemberAsync(permiteduser);


											overwrites = overwrites.Merge(userid,
												Permissions.UseVoice | Permissions.AccessChannels,
												Permissions.None);
										}
										catch (NotFoundException)
										{
										}

								try
								{
									var conn = CurrentApplication.ServiceProvider
										.GetRequiredService<NpgsqlDataSource>();
									var sql =
										"UPDATE tempvoice SET channelmods = @mods WHERE channelid = @channelid";
									await using var command = conn.CreateCommand(sql);
									command.Parameters.AddWithValue("@mods", string.Join(", ", channelMods));
									command.Parameters.AddWithValue("@channelid", (long)voice.Id);
									await command.ExecuteNonQueryAsync();
								}
								catch (Exception)
								{
									// ignored, if it fails, it fails
								}


								await voice.ModifyAsync(x => x.PermissionOverwrites = overwrites);
							}
					}
                }
            }
            catch (NotFoundException)
            {
                // pass
            }
            catch (Exception ex)
            {
                await ErrorReporting.SendErrorToDev(sender, e.User, ex);
                Console.WriteLine(ex.Message);
            }
        });
        _ = Task.Run(async () =>
        {
            try
            {
                if (e.Before == null && e.After == null) return;
                if (e.Before == e.After) return;
                var beforeChannel = e.Before?.Channel;
                var afterChannel = e.After?.Channel;
                var allChannel = await GetAllChannelIDsFromDB();
                if (beforeChannel == null && afterChannel != null)
                    if (allChannel.Contains((long)afterChannel.Id))
                    {
                        var member = await e.Guild.GetMemberAsync(e.User.Id);
                        await afterChannel.SendMessageAsync(
                            $"<:vcjoin:1152229106289758388> {GetBetterUsernameWithID(member)}");
                        return;
                    }

                if (beforeChannel != null && afterChannel == null)
                    if (allChannel.Contains((long)beforeChannel.Id))
                    {
                        var member = await e.Guild.GetMemberAsync(e.User.Id);
                        await beforeChannel.SendMessageAsync(
                            $"<:vcleave:1152229103974502541> {GetBetterUsernameWithID(member)}");
                        return;
                    }

                if (beforeChannel != null && afterChannel != null)
                {
                    if (beforeChannel == afterChannel) return;
                    var member = await e.Guild.GetMemberAsync(e.User.Id);
                    if (allChannel.Contains((long)beforeChannel.Id))
                        await beforeChannel.SendMessageAsync(
                            $"<:vcleave:1152229103974502541> {GetBetterUsernameWithID(member)}");

                    if (allChannel.Contains((long)afterChannel.Id))
                        await afterChannel.SendMessageAsync(
                            $"<:vcjoin:1152229106289758388> {GetBetterUsernameWithID(member)}");
                }
            }
            catch (NotFoundException)
            {
                // ignored // Normal if user leaves vc before user get the vc
            }
            catch (Exception err)
            {
                await ErrorReporting.SendErrorToDev(sender, e.User, err);
            }
        });

        return Task.CompletedTask;
    }
}