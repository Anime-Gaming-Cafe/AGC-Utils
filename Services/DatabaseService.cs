#region

#endregion

namespace AGC_Management.Services;

public static class DatabaseService
{
    public static string GetConnectionString()
    {
        var dbConfigSection = GlobalProperties.DebugMode ? "DatabaseCfgDBG" : "DatabaseCfg";
        var DbHost = BotConfig.GetConfig()[dbConfigSection]["Database_Host"];
        var DbUser = BotConfig.GetConfig()[dbConfigSection]["Database_User"];
        var DbPass = BotConfig.GetConfig()[dbConfigSection]["Database_Password"];
        var DbName = BotConfig.GetConfig()[dbConfigSection]["Database"];
        return $"Host={DbHost};Username={DbUser};Password={DbPass};Database={DbName};Maximum Pool Size=10;";
    }


    // Read DBContent
    public static NpgsqlDataReader ExecuteQuery(string sql)
    {
        try
        {
            var dbConnection = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
            using var cmd = dbConnection.CreateCommand(sql);
            return cmd.ExecuteReader();
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred while executing the database query: " + ex.Message);
            throw;
        }
    }

    public static async Task InsertDataIntoTable(string tableName, Dictionary<string, object> columnValuePairs)
    {
        var insertQuery = $"INSERT INTO {tableName} ({string.Join(", ", columnValuePairs.Keys)}) " +
                          $"VALUES ({string.Join(", ", columnValuePairs.Keys.Select(k => $"@{k}"))})";

        var connection = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var command = connection.CreateCommand(insertQuery);
        foreach (var kvp in columnValuePairs)
        {
            NpgsqlParameter parameter = new($"@{kvp.Key}", kvp.Value);
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync();
    }

    public static async Task<List<Dictionary<string, object>>> SelectDataFromTable(string tableName,
        List<string> columns, Dictionary<string, object> whereConditions)
    {
        string selectQuery;
        if (columns.Contains("*"))
        {
            selectQuery = $"SELECT * FROM \"{tableName}\"";
        }
        else
        {
            var columnNames = string.Join(", ", columns.Select(c => $"\"{c}\""));
            selectQuery = $"SELECT {columnNames} FROM \"{tableName}\"";
        }

        if (whereConditions != null && whereConditions.Count > 0)
        {
            var whereClause = string.Join(" AND ", whereConditions.Select(c => $"\"{c.Key}\" = @{c.Key}"));
            selectQuery += $" WHERE {whereClause}";
        }

        List<Dictionary<string, object>> results = new();

        var connection = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var command = connection.CreateCommand(selectQuery);
        if (whereConditions != null && whereConditions.Count > 0)
            foreach (var condition in whereConditions)
            {
                NpgsqlParameter parameter = new($"@{condition.Key}", condition.Value);
                command.Parameters.Add(parameter);
            }

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Dictionary<string, object> row = new();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                var columnValue = reader.GetValue(i);

                row[columnName] = columnValue;
            }

            results.Add(row);
        }

        return results;
    }

    public static async Task<int> DeleteDataFromTable(string tableName,
        Dictionary<string, (object value, string comparisonOperator)> whereConditions, string logicalOperator = "AND")
    {
        var deleteQuery = $"DELETE FROM \"{tableName}\"";

        if (whereConditions != null && whereConditions.Count > 0)
        {
            var whereClause = string.Join($" {logicalOperator} ",
                whereConditions.Select(c => $"\"{c.Key}\" {c.Value.comparisonOperator} @{c.Key}"));
            deleteQuery += $" WHERE {whereClause}";
        }

        int rowsAffected;

        var connection = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var command = connection.CreateCommand(deleteQuery);
        if (whereConditions != null && whereConditions.Count > 0)
            foreach (var condition in whereConditions)
            {
                NpgsqlParameter parameter = new($"@{condition.Key}", condition.Value.value);
                command.Parameters.Add(parameter);
            }

        rowsAffected = await command.ExecuteNonQueryAsync();

        return rowsAffected;
    }


    public static async Task InitializeAndUpdateDatabaseTables()
    {
        var conn = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        CurrentApplication.Logger.Information("Initializing database tables...");

        var tableCommands = new Dictionary<string, string>
        {
            {
                "userrankcardsettings",
                "CREATE TABLE IF NOT EXISTS userrankcardsettings (userid BIGINT, imagedata TEXT, barcolor TEXT DEFAULT '#9f00ff', textfont TEXT DEFAULT 'Verdana', boxalpha INTEGER DEFAULT 150, UNIQUE (userid))"
            },
            {
                "bewerbungen",
                "CREATE TABLE IF NOT EXISTS bewerbungen (bewerbungsid TEXT, userid BIGINT, positionname TEXT, status INTEGER DEFAULT 0, timestamp BIGINT, bewerbungstext TEXT, seenby BIGINT[] DEFAULT '{}')"
            },
            {
                "metrics_messages",
                "CREATE TABLE IF NOT EXISTS metrics_messages (userid BIGINT, messageid BIGINT, channelid BIGINT, timestamp BIGINT)"
            },
            {
                "metrics_voice",
                "CREATE TABLE IF NOT EXISTS metrics_voice (userid BIGINT, channelid BIGINT, timestamp BIGINT, voicestate INTEGER DEFAULT 0)"
            },
            {
                "metrics_activity",
                "CREATE TABLE IF NOT EXISTS metrics_activity (userid BIGINT, activityname TEXT, activityid BIGINT, timestamp BIGINT)"
            },
            {
                "metrics_activitymap",
                "CREATE TABLE IF NOT EXISTS metrics_activitymap (activityname TEXT, activityid BIGINT)"
            },
            {
                "idx_metrics_activitymap_userid",
                "CREATE INDEX IF NOT EXISTS idx_metrics_activitymap_activityid ON metrics_activitymap (activityid)"
            },
            {
                "idx_metrics_activity_userid",
                "CREATE INDEX IF NOT EXISTS idx_metrics_activity_userid ON metrics_activity (userid)"
            },
            {
                "idx_metrics_voice_userid",
                "CREATE INDEX IF NOT EXISTS idx_metrics_voice_userid ON metrics_voice (userid)"
            },
            {
                "idx_metrics_messages_userid",
                "CREATE INDEX IF NOT EXISTS idx_metrics_messages_userid ON metrics_messages (userid)"
            },

            {
                "pollsystem",
                "CREATE TABLE IF NOT EXISTS pollsystem (id TEXT, name TEXT, text TEXT, channelid BIGINT, messageid BIGINT, isexpiring BOOLEAN DEFAULT false, expirydate BIGINT DEFAULT 0, dmcreatoronfinish BOOLEAN DEFAULT false, isanonymous BOOLEAN DEFAULT false, ismultiplechoice BOOLEAN DEFAULT false, creatorid BIGINT, options JSONB)"
            },
            {
                "cmdexec",
                "CREATE TABLE IF NOT EXISTS cmdexec (commandname TEXT, commandcontent TEXT, userid BIGINT , timestamp BIGINT)"
            },

            {
                "pollvotes",
                "CREATE TABLE IF NOT EXISTS pollvotes (pollid TEXT, optionindex INTEGER, userid BIGINT)"
            },

            {
                "xptransferlogs",
                "CREATE TABLE IF NOT EXISTS xptransferlogs (sourceuserid BIGINT, destinationuserid BIGINT, executorid BIGINT, amount INTEGER, timestamp BIGINT)"
            },
            {
                "banlogs",
                "CREATE TABLE IF NOT EXISTS banlogs (userid BIGINT, executorid BIGINT, reason TEXT, timestamp BIGINT)"
            },
            {
                "dashboardlogins",
                "CREATE TABLE IF NOT EXISTS dashboardlogins (userid TEXT, useragent TEXT, ip TEXT, timestamp BIGINT)"
            },
            {
                "userrankcardunallowedimagelog",
                "CREATE TABLE IF NOT EXISTS userrankcardunallowedimagelog (userid BIGINT, imagedata TEXT, timestamp BIGINT,  blockreason TEXT)"
            },
            {
                "cachetable",
                "CREATE TABLE IF NOT EXISTS cachetable (cachetype TEXT, content jsonb)"
            },
            {
                "applicationcategories",
                "CREATE TABLE IF NOT EXISTS applicationcategories (positionname TEXT, positionid TEXT, applicable BOOLEAN DEFAULT false)"
            },
            { "reasonmap", "CREATE TABLE IF NOT EXISTS reasonmap (key TEXT, text TEXT)" },
            {
                "levelingdata",
                "CREATE TABLE IF NOT EXISTS levelingdata (userid BIGINT, current_xp INTEGER, current_level INTEGER, last_text_reward BIGINT DEFAULT 0, last_vc_reward BIGINT DEFAULT 0, pingactive BOOLEAN DEFAULT true, UNIQUE (userid))"
            },
            {
                "levelingsettings",
                "CREATE TABLE IF NOT EXISTS levelingsettings (guildid BIGINT, text_active BOOLEAN DEFAULT false, vc_active BOOLEAN DEFAULT FALSE, text_multi FLOAT DEFAULT 1.0, vc_multi FLOAT DEFAULT 1.0, levelupchannelid BIGINT, levelupmessage TEXT, levelupmessagereward TEXT, retainroles BOOLEAN DEFAULT true, lastrecalc BIGINT DEFAULT 0)"
            },
            { "level_rewards", "CREATE TABLE IF NOT EXISTS level_rewards (level INTEGER, roleid BIGINT)" },
            {
                "level_multiplicatoroverrideroles",
                "CREATE TABLE IF NOT EXISTS level_multiplicatoroverrideroles (roleid BIGINT, multiplicator FLOAT)"
            },
            { "level_excludedchannels", "CREATE TABLE IF NOT EXISTS level_excludedchannels (channelid BIGINT)" },
            { "level_excludedroles", "CREATE TABLE IF NOT EXISTS level_excludedroles (roleid BIGINT)" },
            { "banreasons", "CREATE TABLE IF NOT EXISTS banreasons (reason TEXT, custom_id VARCHAR)" },
            {
                "flags",
                "CREATE TABLE IF NOT EXISTS flags (userid BIGINT, punisherid BIGINT, datum BIGINT, description VARCHAR, caseid VARCHAR)"
            },
            {
                "tempvoice",
                "CREATE TABLE IF NOT EXISTS tempvoice (channelid BIGINT, ownerid BIGINT, lastedited BIGINT, laststatusedited BIGINT, channelmods VARCHAR)"
            },
            {
                "tempvoicesession",
                "CREATE TABLE IF NOT EXISTS tempvoicesession (userid BIGINT, channelname VARCHAR, channelbitrate INTEGER, channellimit INTEGER, blockedusers VARCHAR, permitedusers VARCHAR, locked BOOLEAN, hidden BOOLEAN, sessionskip BOOLEAN, channelmods VARCHAR)"
            },
            { "vorstellungscooldown", "CREATE TABLE IF NOT EXISTS vorstellungscooldown (user_id BIGINT, time BIGINT)" },
            { "warnreasons", "CREATE TABLE IF NOT EXISTS warnreasons (reason TEXT, custom_id VARCHAR)" },
            {
                "warns",
                "CREATE TABLE IF NOT EXISTS warns (userid BIGINT, punisherid BIGINT, datum BIGINT, description VARCHAR, perma BOOLEAN, caseid VARCHAR)"
            }
        };
        var progressBar = new ConsoleProgressBar(tableCommands.Count);

        foreach (var kvp in tableCommands)
        {
            var tableName = kvp.Key;
            var createTableCommand = kvp.Value;

            await using var cmdCreate = conn.CreateCommand(createTableCommand);
            await cmdCreate.ExecuteNonQueryAsync();
            //CurrentApplication.Logger.Debug($"Table {tableName} initialized or updated.");
            progressBar.Increment();
            await Task.Delay(10);
        }


        CurrentApplication.Logger.Information("Database tables initialized.");
        await UpdateTables();
    }

    private static async Task UpdateTables()
    {
        var conn = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        CurrentApplication.Logger.Information("Updating database tables...");


        var columnUpdates = new Dictionary<string, Dictionary<string, string>>
        {
            {
                "cachetable",
                new Dictionary<string, string>
                {
                    {
                        "cachetype_unique",
                        "CREATE UNIQUE INDEX IF NOT EXISTS idx_cachetable_cachetype ON cachetable (cachetype)"
                    }
                }
            },
            {
                "pollsystem",
                new Dictionary<string, string>
                {
                    { "id", "ALTER TABLE pollsystem ADD COLUMN IF NOT EXISTS id TEXT" },
                    { "name", "ALTER TABLE pollsystem ADD COLUMN IF NOT EXISTS name TEXT" },
                    { "text", "ALTER TABLE pollsystem ADD COLUMN IF NOT EXISTS text TEXT" },
                    { "channelid", "ALTER TABLE pollsystem ADD COLUMN IF NOT EXISTS channelid BIGINT" },
                    { "messageid", "ALTER TABLE pollsystem ADD COLUMN IF NOT EXISTS messageid BIGINT" },
                    {
                        "isexpiring", "ALTER TABLE pollsystem ADD COLUMN IF NOT EXISTS isexpiring BOOLEAN DEFAULT false"
                    },
                    { "expirydate", "ALTER TABLE pollsystem ADD COLUMN IF NOT EXISTS expirydate BIGINT DEFAULT 0" },
                    {
                        "dmcreatoronfinish",
                        "ALTER TABLE pollsystem ADD COLUMN IF NOT EXISTS dmcreatoronfinish BOOLEAN DEFAULT false"
                    },
                    {
                        "isanonymous",
                        "ALTER TABLE pollsystem ADD COLUMN IF NOT EXISTS isanonymous BOOLEAN DEFAULT false"
                    },
                    {
                        "ismultiplechoice",
                        "ALTER TABLE pollsystem ADD COLUMN IF NOT EXISTS ismultiplechoice BOOLEAN DEFAULT false"
                    },
                    { "creatorid", "ALTER TABLE pollsystem ADD COLUMN IF NOT EXISTS creatorid BIGINT" },
                    { "options", "ALTER TABLE pollsystem ADD COLUMN IF NOT EXISTS options JSONB" }
                }
            },
            {
                "tempvoicesession_unique",
                new Dictionary<string, string>
                {
                    {
                        "tempvoicesession_unique",
                        "CREATE UNIQUE INDEX IF NOT EXISTS idx_tempvoicesession_userid ON tempvoicesession (userid)"
                    }
                }
            },
            {
                "userrankcardsettings",
                new Dictionary<string, string>
                {
                    {
                        "barcolor",
                        "ALTER TABLE userrankcardsettings ADD COLUMN IF NOT EXISTS barcolor TEXT DEFAULT '#9f00ff'"
                    },
                    {
                        "textfont",
                        "ALTER TABLE userrankcardsettings ADD COLUMN IF NOT EXISTS textfont TEXT DEFAULT 'Verdana'"
                    },
                    { "imagedata", "ALTER TABLE userrankcardsettings ADD COLUMN IF NOT EXISTS imagedata TEXT" },
                    {
                        "boxalpha",
                        "ALTER TABLE userrankcardsettings ADD COLUMN IF NOT EXISTS boxalpha INTEGER DEFAULT 150"
                    }
                }
            },
            {
                "applicationcategories",
                new Dictionary<string, string>
                {
                    { "positionname", "ALTER TABLE applicationcategories ADD COLUMN IF NOT EXISTS positionname TEXT" },
                    { "positionid", "ALTER TABLE applicationcategories ADD COLUMN IF NOT EXISTS positionid TEXT" },
                    {
                        "applicable",
                        "ALTER TABLE applicationcategories ADD COLUMN IF NOT EXISTS applicable BOOLEAN DEFAULT false"
                    }
                }
            },
            {
                "reasonmap",
                new Dictionary<string, string>
                {
                    { "key", "ALTER TABLE reasonmap ADD COLUMN IF NOT EXISTS key TEXT" },
                    { "text", "ALTER TABLE reasonmap ADD COLUMN IF NOT EXISTS text TEXT" }
                }
            },

            {
                "banreasons",
                new Dictionary<string, string>
                {
                    { "reason", "ALTER TABLE banreasons ADD COLUMN IF NOT EXISTS reason TEXT" },
                    { "custom_id", "ALTER TABLE banreasons ADD COLUMN IF NOT EXISTS custom_id VARCHAR" }
                }
            },
            {
                "flags",
                new Dictionary<string, string>
                {
                    { "userid", "ALTER TABLE flags ADD COLUMN IF NOT EXISTS userid BIGINT" },
                    { "punisherid", "ALTER TABLE flags ADD COLUMN IF NOT EXISTS punisherid BIGINT" },
                    { "datum", "ALTER TABLE flags ADD COLUMN IF NOT EXISTS datum BIGINT" },
                    { "description", "ALTER TABLE flags ADD COLUMN IF NOT EXISTS description VARCHAR" },
                    { "caseid", "ALTER TABLE flags ADD COLUMN IF NOT EXISTS caseid VARCHAR" }
                }
            },
            {
                "levelingdata",
                new Dictionary<string, string>
                {
                    { "userid", "ALTER TABLE levelingdata ADD COLUMN IF NOT EXISTS userid BIGINT" },
                    { "current_xp", "ALTER TABLE levelingdata ADD COLUMN IF NOT EXISTS current_xp INTEGER" },
                    { "current_level", "ALTER TABLE levelingdata ADD COLUMN IF NOT EXISTS current_level INTEGER" },
                    {
                        "last_text_reward",
                        "ALTER TABLE levelingdata ADD COLUMN IF NOT EXISTS last_text_reward BIGINT DEFAULT 0"
                    },
                    {
                        "last_vc_reward",
                        "ALTER TABLE levelingdata ADD COLUMN IF NOT EXISTS last_vc_reward BIGINT DEFAULT 0"
                    },
                    {
                        "pingactive",
                        "ALTER TABLE levelingdata ADD COLUMN IF NOT EXISTS pingactive BOOLEAN DEFAULT true"
                    },
                    {
                        "unique_index",
                        "CREATE UNIQUE INDEX IF NOT EXISTS idx_levelingdata_userid ON levelingdata (userid)"
                    }
                }
            },
            {
                "levelingsettings",
                new Dictionary<string, string>
                {
                    { "guildid", "ALTER TABLE levelingsettings ADD COLUMN IF NOT EXISTS guildid BIGINT" },
                    {
                        "text_active",
                        "ALTER TABLE levelingsettings ADD COLUMN IF NOT EXISTS text_active BOOLEAN DEFAULT false"
                    },
                    {
                        "vc_active",
                        "ALTER TABLE levelingsettings ADD COLUMN IF NOT EXISTS vc_active BOOLEAN DEFAULT false"
                    },
                    {
                        "text_multi",
                        "ALTER TABLE levelingsettings ADD COLUMN IF NOT EXISTS text_multi FLOAT DEFAULT 1.0"
                    },
                    { "vc_multi", "ALTER TABLE levelingsettings ADD COLUMN IF NOT EXISTS vc_multi FLOAT DEFAULT 1.0" },
                    {
                        "levelupchannelid",
                        "ALTER TABLE levelingsettings ADD COLUMN IF NOT EXISTS levelupchannelid BIGINT"
                    },
                    { "levelupmessage", "ALTER TABLE levelingsettings ADD COLUMN IF NOT EXISTS levelupmessage TEXT" },
                    {
                        "levelupmessagereward",
                        "ALTER TABLE levelingsettings ADD COLUMN IF NOT EXISTS levelupmessagereward TEXT"
                    },
                    {
                        "retainroles",
                        "ALTER TABLE levelingsettings ADD COLUMN IF NOT EXISTS retainroles BOOLEAN DEFAULT true"
                    },
                    {
                        "lastrecalc",
                        "ALTER TABLE levelingsettings ADD COLUMN IF NOT EXISTS lastrecalc BIGINT DEFAULT 0"
                    }
                }
            },

            {
                "level_excludedchannels", new Dictionary<string, string>
                {
                    { "channelid", "ALTER TABLE level_excludedchannels ADD COLUMN IF NOT EXISTS channelid BIGINT" }
                }
            },

            {
                "level_excludedroles", new Dictionary<string, string>
                {
                    { "roleid", "ALTER TABLE level_excludedroles ADD COLUMN IF NOT EXISTS roleid BIGINT" }
                }
            },

            {
                "level_multiplicatoroverrideroles", new Dictionary<string, string>
                {
                    { "roleid", "ALTER TABLE level_multiplicatoroverrideroles ADD COLUMN IF NOT EXISTS roleid BIGINT" },
                    {
                        "multiplicator",
                        "ALTER TABLE level_multiplicatoroverrideroles ADD COLUMN IF NOT EXISTS multiplicator FLOAT"
                    }
                }
            },

            {
                "level_rewards", new Dictionary<string, string>
                {
                    { "level", "ALTER TABLE level_rewards ADD COLUMN IF NOT EXISTS level INTEGER" },
                    { "roleid", "ALTER TABLE level_rewards ADD COLUMN IF NOT EXISTS roleid BIGINT" }
                }
            },

            {
                "tempvoice",
                new Dictionary<string, string>
                {
                    { "channelid", "ALTER TABLE tempvoice ADD COLUMN IF NOT EXISTS channelid BIGINT" },
                    { "ownerid", "ALTER TABLE tempvoice ADD COLUMN IF NOT EXISTS ownerid BIGINT" },
                    { "lastedited", "ALTER TABLE tempvoice ADD COLUMN IF NOT EXISTS lastedited BIGINT" },
                    { "laststatusedited", "ALTER TABLE tempvoice ADD COLUMN IF NOT EXISTS laststatusedited BIGINT" },
                    { "channelmods", "ALTER TABLE tempvoice ADD COLUMN IF NOT EXISTS channelmods VARCHAR" }
                }
            },
            {
                "tempvoicesession",
                new Dictionary<string, string>
                {
                    { "userid", "ALTER TABLE tempvoicesession ADD COLUMN IF NOT EXISTS userid BIGINT" },
                    { "channelname", "ALTER TABLE tempvoicesession ADD COLUMN IF NOT EXISTS channelname VARCHAR" },
                    {
                        "channelbitrate", "ALTER TABLE tempvoicesession ADD COLUMN IF NOT EXISTS channelbitrate INTEGER"
                    },
                    { "channellimit", "ALTER TABLE tempvoicesession ADD COLUMN IF NOT EXISTS channellimit INTEGER" },
                    { "blockedusers", "ALTER TABLE tempvoicesession ADD COLUMN IF NOT EXISTS blockedusers VARCHAR" },
                    { "permitedusers", "ALTER TABLE tempvoicesession ADD COLUMN IF NOT EXISTS permitedusers VARCHAR" },
                    { "locked", "ALTER TABLE tempvoicesession ADD COLUMN IF NOT EXISTS locked BOOLEAN" },
                    { "hidden", "ALTER TABLE tempvoicesession ADD COLUMN IF NOT EXISTS hidden BOOLEAN" },
                    { "sessionskip", "ALTER TABLE tempvoicesession ADD COLUMN IF NOT EXISTS sessionskip BOOLEAN" },
                    { "channelmods", "ALTER TABLE tempvoicesession ADD COLUMN IF NOT EXISTS channelmods VARCHAR" }
                }
            },
            {
                "vorstellungscooldown",
                new Dictionary<string, string>
                {
                    { "user_id", "ALTER TABLE vorstellungscooldown ADD COLUMN IF NOT EXISTS user_id BIGINT" },
                    { "time", "ALTER TABLE vorstellungscooldown ADD COLUMN IF NOT EXISTS time BIGINT" }
                }
            },
            {
                "warnreasons",
                new Dictionary<string, string>
                {
                    { "reason", "ALTER TABLE warnreasons ADD COLUMN IF NOT EXISTS reason TEXT" },
                    { "custom_id", "ALTER TABLE warnreasons ADD COLUMN IF NOT EXISTS custom_id VARCHAR" }
                }
            },
            {
                "warns",
                new Dictionary<string, string>
                {
                    { "userid", "ALTER TABLE warns ADD COLUMN IF NOT EXISTS userid BIGINT" },
                    { "punisherid", "ALTER TABLE warns ADD COLUMN IF NOT EXISTS punisherid BIGINT" },
                    { "datum", "ALTER TABLE warns ADD COLUMN IF NOT EXISTS datum BIGINT" },
                    { "description", "ALTER TABLE warns ADD COLUMN IF NOT EXISTS description VARCHAR" },
                    { "perma", "ALTER TABLE warns ADD COLUMN IF NOT EXISTS perma BOOLEAN" },
                    { "caseid", "ALTER TABLE warns ADD COLUMN IF NOT EXISTS caseid VARCHAR" }
                }
            }
        };


        var commandCount = 0;
        foreach (var tableKvp in columnUpdates)
        foreach (var columnKvp in tableKvp.Value)
            commandCount++;

        var progressBar = new ConsoleProgressBar(commandCount);


        foreach (var tableKvp in columnUpdates)
        {
            var tableName = tableKvp.Key;
            foreach (var columnKvp in tableKvp.Value)
            {
                var columnName = columnKvp.Key;
                var alterColumnCommand = columnKvp.Value;

                await using var cmdAlter = conn.CreateCommand(alterColumnCommand);
                await cmdAlter.ExecuteNonQueryAsync();
                progressBar.Increment();
                await Task.Delay(10);
            }
        }

        await InitLeveling();
        CurrentApplication.Logger.Information("Database tables updated.");
    }


    private static async Task InitLeveling()
    {
        var targetGuildId = ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["ServerId"]);
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using var cmd = con.CreateCommand($"SELECT * FROM levelingsettings WHERE guildid = '{targetGuildId}'");
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = await reader.ReadAsync();
        await reader.CloseAsync();
        if (!result)
        {
            await using var cmd2 = con.CreateCommand(
                "INSERT INTO levelingsettings (guildid, text_active, vc_active, text_multi, vc_multi, levelupchannelid, levelupmessage, levelupmessagereward, retainroles, lastrecalc) VALUES (@guildid, false, false, 1.0, 1.0, 0, 'Herzlichen Glückwunsch {usermention}! Du bist nun Level {level}!', 'Herzlichen Glückwunsch {usermention}! Du bist nun Level {level}!', true, 0)");
            cmd2.Parameters.AddWithValue("guildid", (long)targetGuildId);
            await cmd2.ExecuteNonQueryAsync();
        }
    }
}