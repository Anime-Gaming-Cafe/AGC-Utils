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

        List<Dictionary<string, object>> results = [];

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
            Dictionary<string, object> row = [];

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
            },
            { "counting", "CREATE TABLE IF NOT EXISTS counting (lastnumber BIGINT, lastuser BIGINT)" },
            { "countcounter", "CREATE TABLE IF NOT EXISTS countcounter (userid BIGINT, counter BIGINT, timestamps BIGINT)" },
            { "countingfails", "CREATE TABLE IF NOT EXISTS countingfails (userid BIGINT, counter BIGINT)" },
            { "countinghighscore", "CREATE TABLE IF NOT EXISTS countinghighscore (number BIGINT, userid BIGINT, timestamps BIGINT)" },
            { "countsave", "CREATE TABLE IF NOT EXISTS countsave (userid BIGINT, saves NUMERIC)" },
            {
                "botsettings",
                "CREATE TABLE IF NOT EXISTS botsettings (section TEXT, key TEXT, value TEXT, PRIMARY KEY (section, key))"
            },
            {
                "extra_permissions",
                "CREATE TABLE IF NOT EXISTS extra_permissions (permname TEXT PRIMARY KEY, displayname TEXT, description TEXT, roleid BIGINT, trigger_type TEXT DEFAULT 'none', trigger_value BIGINT DEFAULT 0, trigger_mode TEXT DEFAULT 'recurring', auto_revoke TEXT DEFAULT 'inherit', created_by BIGINT DEFAULT 0, created_at BIGINT DEFAULT 0)"
            },
            {
                "extra_permission_members",
                "CREATE TABLE IF NOT EXISTS extra_permission_members (userid BIGINT, permname TEXT, state TEXT DEFAULT 'auto', expires_at BIGINT DEFAULT 0, actor_id BIGINT DEFAULT 0, reason TEXT DEFAULT '', updated_at BIGINT DEFAULT 0, trigger_fired BOOLEAN DEFAULT false, trigger_fired_at BIGINT DEFAULT 0, PRIMARY KEY (userid, permname))"
            },
            {
                "idx_extra_permission_members_expires",
                "CREATE INDEX IF NOT EXISTS idx_extra_permission_members_expires ON extra_permission_members (expires_at)"
            },
            {
                "idx_extra_permission_members_permname",
                "CREATE INDEX IF NOT EXISTS idx_extra_permission_members_permname ON extra_permission_members (permname)"
            },
            {
                "extra_permission_conditions",
                "CREATE TABLE IF NOT EXISTS extra_permission_conditions (condition_id TEXT PRIMARY KEY, permname TEXT, group_id INTEGER DEFAULT 1, condition_type TEXT DEFAULT 'level', comparator TEXT DEFAULT 'gte', value BIGINT DEFAULT 0, scope_ids BIGINT[] DEFAULT '{}', window_days INTEGER DEFAULT 0, negate BOOLEAN DEFAULT false, created_at BIGINT DEFAULT 0)"
            },
            {
                "idx_extra_permission_conditions_permname",
                "CREATE INDEX IF NOT EXISTS idx_extra_permission_conditions_permname ON extra_permission_conditions (permname)"
            },
            {
                "teamapplication_position",
                "CREATE TABLE IF NOT EXISTS teamapplication_position (position_id TEXT PRIMARY KEY, position_name TEXT, description TEXT DEFAULT '', min_level INTEGER DEFAULT 20, notify_channel_id BIGINT DEFAULT 0, active BOOLEAN DEFAULT false, always_open BOOLEAN DEFAULT false, created_at BIGINT DEFAULT 0)"
            },
            {
                "teamapplication_questionset",
                "CREATE TABLE IF NOT EXISTS teamapplication_questionset (position_id TEXT, version INTEGER, state TEXT DEFAULT 'draft', created_by BIGINT DEFAULT 0, created_at BIGINT DEFAULT 0, PRIMARY KEY (position_id, version))"
            },
            {
                "teamapplication_questions",
                "CREATE TABLE IF NOT EXISTS teamapplication_questions (question_id TEXT PRIMARY KEY, position_id TEXT, version INTEGER DEFAULT 0, sort_order INTEGER DEFAULT 0, type TEXT DEFAULT 'shorttext', text TEXT DEFAULT '', description TEXT DEFAULT '', required BOOLEAN DEFAULT true, min_length INTEGER DEFAULT 0, max_length INTEGER DEFAULT 0, min_value BIGINT DEFAULT 0, max_value BIGINT DEFAULT 0, options JSONB DEFAULT '[]'::jsonb, min_selections INTEGER DEFAULT 0, max_selections INTEGER DEFAULT 0)"
            },
            {
                "idx_teamapplication_questions_set",
                "CREATE INDEX IF NOT EXISTS idx_teamapplication_questions_set ON teamapplication_questions (position_id, version, sort_order)"
            },
            {
                "teamapplication_phase",
                "CREATE TABLE IF NOT EXISTS teamapplication_phase (phase_id TEXT PRIMARY KEY, position_id TEXT, name TEXT DEFAULT '', state TEXT DEFAULT 'draft', questionset_version INTEGER DEFAULT 0, opens_at BIGINT DEFAULT 0, closes_at BIGINT DEFAULT 0, created_by BIGINT DEFAULT 0, created_at BIGINT DEFAULT 0)"
            },
            {
                "idx_teamapplication_phase_position",
                "CREATE INDEX IF NOT EXISTS idx_teamapplication_phase_position ON teamapplication_phase (position_id, state)"
            },
            {
                "teamapplication_application",
                "CREATE TABLE IF NOT EXISTS teamapplication_application (application_id TEXT PRIMARY KEY, user_id BIGINT, position_id TEXT, phase_id TEXT, questionset_version INTEGER DEFAULT 0, attempt INTEGER DEFAULT 1, status TEXT DEFAULT 'eingereicht', submitted_at BIGINT DEFAULT 0, decided_at BIGINT DEFAULT 0, decided_by BIGINT DEFAULT 0, decision_text TEXT DEFAULT '', dm_delivered BOOLEAN, dm_error TEXT DEFAULT '', withdrawn_at BIGINT DEFAULT 0, level_snapshot INTEGER DEFAULT 0, xp_snapshot INTEGER DEFAULT 0, joined_at_snapshot BIGINT DEFAULT 0, account_created_snapshot BIGINT DEFAULT 0)"
            },
            {
                "idx_teamapplication_application_list",
                "CREATE INDEX IF NOT EXISTS idx_teamapplication_application_list ON teamapplication_application (position_id, phase_id, status)"
            },
            {
                "idx_teamapplication_application_user",
                "CREATE INDEX IF NOT EXISTS idx_teamapplication_application_user ON teamapplication_application (user_id)"
            },
            {
                "teamapplication_answers",
                "CREATE TABLE IF NOT EXISTS teamapplication_answers (application_id TEXT, question_id TEXT, sort_order INTEGER DEFAULT 0, question_text_snapshot TEXT DEFAULT '', question_type TEXT DEFAULT 'shorttext', answer TEXT DEFAULT '', answer_options JSONB DEFAULT '[]'::jsonb, PRIMARY KEY (application_id, question_id))"
            },
            {
                "teamapplication_notes",
                "CREATE TABLE IF NOT EXISTS teamapplication_notes (note_id TEXT PRIMARY KEY, application_id TEXT, author_id BIGINT, text TEXT DEFAULT '', created_at BIGINT DEFAULT 0)"
            },
            {
                "idx_teamapplication_notes_application",
                "CREATE INDEX IF NOT EXISTS idx_teamapplication_notes_application ON teamapplication_notes (application_id)"
            },
            {
                "teamapplication_seen",
                "CREATE TABLE IF NOT EXISTS teamapplication_seen (application_id TEXT NOT NULL, user_id BIGINT NOT NULL, seen_at BIGINT DEFAULT 0, PRIMARY KEY (application_id, user_id))"
            },
            {
                "teamapplication_permissions",
                "CREATE TABLE IF NOT EXISTS teamapplication_permissions (permission_id TEXT PRIMARY KEY, role_id BIGINT, position_id TEXT, phase_id TEXT, permission TEXT)"
            },
            {
                "idx_teamapplication_permissions_role",
                "CREATE INDEX IF NOT EXISTS idx_teamapplication_permissions_role ON teamapplication_permissions (role_id)"
            },
            {
                "teamapplication_reapply_grant",
                "CREATE TABLE IF NOT EXISTS teamapplication_reapply_grant (grant_id TEXT PRIMARY KEY, user_id BIGINT, position_id TEXT, phase_id TEXT, granted_by BIGINT DEFAULT 0, granted_at BIGINT DEFAULT 0, reason TEXT DEFAULT '', used_at BIGINT DEFAULT 0)"
            },
            {
                "idx_teamapplication_reapply_grant_lookup",
                "CREATE INDEX IF NOT EXISTS idx_teamapplication_reapply_grant_lookup ON teamapplication_reapply_grant (user_id, position_id, phase_id)"
            },
            {
                "teamapplication_templates",
                "CREATE TABLE IF NOT EXISTS teamapplication_templates (template_id TEXT PRIMARY KEY, position_id TEXT, kind TEXT DEFAULT 'accept', name TEXT DEFAULT '', text TEXT DEFAULT '')"
            },
            {
                "teamapplication_placeholder",
                "CREATE TABLE IF NOT EXISTS teamapplication_placeholder (key TEXT PRIMARY KEY, text TEXT DEFAULT '')"
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
            },
            {
                "extra_permissions",
                new Dictionary<string, string>
                {
                    { "displayname", "ALTER TABLE extra_permissions ADD COLUMN IF NOT EXISTS displayname TEXT" },
                    { "description", "ALTER TABLE extra_permissions ADD COLUMN IF NOT EXISTS description TEXT" },
                    { "roleid", "ALTER TABLE extra_permissions ADD COLUMN IF NOT EXISTS roleid BIGINT" },
                    {
                        "trigger_type",
                        "ALTER TABLE extra_permissions ADD COLUMN IF NOT EXISTS trigger_type TEXT DEFAULT 'none'"
                    },
                    {
                        "trigger_value",
                        "ALTER TABLE extra_permissions ADD COLUMN IF NOT EXISTS trigger_value BIGINT DEFAULT 0"
                    },
                    {
                        "trigger_mode",
                        "ALTER TABLE extra_permissions ADD COLUMN IF NOT EXISTS trigger_mode TEXT DEFAULT 'recurring'"
                    },
                    {
                        "auto_revoke",
                        "ALTER TABLE extra_permissions ADD COLUMN IF NOT EXISTS auto_revoke TEXT DEFAULT 'inherit'"
                    },
                    {
                        "created_by",
                        "ALTER TABLE extra_permissions ADD COLUMN IF NOT EXISTS created_by BIGINT DEFAULT 0"
                    },
                    {
                        "created_at",
                        "ALTER TABLE extra_permissions ADD COLUMN IF NOT EXISTS created_at BIGINT DEFAULT 0"
                    }
                }
            },
            {
                "extra_permission_members",
                new Dictionary<string, string>
                {
                    { "userid", "ALTER TABLE extra_permission_members ADD COLUMN IF NOT EXISTS userid BIGINT" },
                    { "permname", "ALTER TABLE extra_permission_members ADD COLUMN IF NOT EXISTS permname TEXT" },
                    {
                        "state",
                        "ALTER TABLE extra_permission_members ADD COLUMN IF NOT EXISTS state TEXT DEFAULT 'auto'"
                    },
                    {
                        "expires_at",
                        "ALTER TABLE extra_permission_members ADD COLUMN IF NOT EXISTS expires_at BIGINT DEFAULT 0"
                    },
                    {
                        "actor_id",
                        "ALTER TABLE extra_permission_members ADD COLUMN IF NOT EXISTS actor_id BIGINT DEFAULT 0"
                    },
                    {
                        "reason",
                        "ALTER TABLE extra_permission_members ADD COLUMN IF NOT EXISTS reason TEXT DEFAULT ''"
                    },
                    {
                        "updated_at",
                        "ALTER TABLE extra_permission_members ADD COLUMN IF NOT EXISTS updated_at BIGINT DEFAULT 0"
                    },
                    {
                        "trigger_fired",
                        "ALTER TABLE extra_permission_members ADD COLUMN IF NOT EXISTS trigger_fired BOOLEAN DEFAULT false"
                    },
                    {
                        "trigger_fired_at",
                        "ALTER TABLE extra_permission_members ADD COLUMN IF NOT EXISTS trigger_fired_at BIGINT DEFAULT 0"
                    },
                    {
                        "unique_index",
                        "CREATE UNIQUE INDEX IF NOT EXISTS idx_extra_permission_members_userid_permname ON extra_permission_members (userid, permname)"
                    }
                }
            },
            {
                "extra_permission_conditions",
                new Dictionary<string, string>
                {
                    { "permname", "ALTER TABLE extra_permission_conditions ADD COLUMN IF NOT EXISTS permname TEXT" },
                    {
                        "group_id",
                        "ALTER TABLE extra_permission_conditions ADD COLUMN IF NOT EXISTS group_id INTEGER DEFAULT 1"
                    },
                    {
                        "condition_type",
                        "ALTER TABLE extra_permission_conditions ADD COLUMN IF NOT EXISTS condition_type TEXT DEFAULT 'level'"
                    },
                    {
                        "comparator",
                        "ALTER TABLE extra_permission_conditions ADD COLUMN IF NOT EXISTS comparator TEXT DEFAULT 'gte'"
                    },
                    {
                        "value",
                        "ALTER TABLE extra_permission_conditions ADD COLUMN IF NOT EXISTS value BIGINT DEFAULT 0"
                    },
                    {
                        "scope_ids",
                        "ALTER TABLE extra_permission_conditions ADD COLUMN IF NOT EXISTS scope_ids BIGINT[] DEFAULT '{}'"
                    },
                    {
                        "window_days",
                        "ALTER TABLE extra_permission_conditions ADD COLUMN IF NOT EXISTS window_days INTEGER DEFAULT 0"
                    },
                    {
                        "negate",
                        "ALTER TABLE extra_permission_conditions ADD COLUMN IF NOT EXISTS negate BOOLEAN DEFAULT false"
                    },
                    {
                        "created_at",
                        "ALTER TABLE extra_permission_conditions ADD COLUMN IF NOT EXISTS created_at BIGINT DEFAULT 0"
                    }
                }
            },
            {
                "teamapplication_position", new Dictionary<string, string>
                {
                    {
                        "position_name",
                        "ALTER TABLE teamapplication_position ADD COLUMN IF NOT EXISTS position_name TEXT"
                    },
                    {
                        "description",
                        "ALTER TABLE teamapplication_position ADD COLUMN IF NOT EXISTS description TEXT DEFAULT ''"
                    },
                    {
                        "min_level",
                        "ALTER TABLE teamapplication_position ADD COLUMN IF NOT EXISTS min_level INTEGER DEFAULT 20"
                    },
                    {
                        "notify_channel_id",
                        "ALTER TABLE teamapplication_position ADD COLUMN IF NOT EXISTS notify_channel_id BIGINT DEFAULT 0"
                    },
                    {
                        "active",
                        "ALTER TABLE teamapplication_position ADD COLUMN IF NOT EXISTS active BOOLEAN DEFAULT false"
                    },
                    {
                        "always_open",
                        "ALTER TABLE teamapplication_position ADD COLUMN IF NOT EXISTS always_open BOOLEAN DEFAULT false"
                    },
                    {
                        "created_at",
                        "ALTER TABLE teamapplication_position ADD COLUMN IF NOT EXISTS created_at BIGINT DEFAULT 0"
                    }
                }
            },
            {
                "teamapplication_questionset", new Dictionary<string, string>
                {
                    {
                        "state",
                        "ALTER TABLE teamapplication_questionset ADD COLUMN IF NOT EXISTS state TEXT DEFAULT 'draft'"
                    },
                    {
                        "created_by",
                        "ALTER TABLE teamapplication_questionset ADD COLUMN IF NOT EXISTS created_by BIGINT DEFAULT 0"
                    },
                    {
                        "created_at",
                        "ALTER TABLE teamapplication_questionset ADD COLUMN IF NOT EXISTS created_at BIGINT DEFAULT 0"
                    }
                }
            },
            {
                "teamapplication_questions", new Dictionary<string, string>
                {
                    {
                        "position_id",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS position_id TEXT"
                    },
                    {
                        "version",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS version INTEGER DEFAULT 0"
                    },
                    {
                        "sort_order",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS sort_order INTEGER DEFAULT 0"
                    },
                    {
                        "type",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS type TEXT DEFAULT 'shorttext'"
                    },
                    {
                        "text",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS text TEXT DEFAULT ''"
                    },
                    {
                        "description",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS description TEXT DEFAULT ''"
                    },
                    {
                        "required",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS required BOOLEAN DEFAULT true"
                    },
                    {
                        "min_length",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS min_length INTEGER DEFAULT 0"
                    },
                    {
                        "max_length",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS max_length INTEGER DEFAULT 0"
                    },
                    {
                        "min_value",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS min_value BIGINT DEFAULT 0"
                    },
                    {
                        "max_value",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS max_value BIGINT DEFAULT 0"
                    },
                    {
                        "options",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS options JSONB DEFAULT '[]'::jsonb"
                    },
                    {
                        "min_selections",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS min_selections INTEGER DEFAULT 0"
                    },
                    {
                        "max_selections",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS max_selections INTEGER DEFAULT 0"
                    }
                }
            },
            {
                "teamapplication_phase", new Dictionary<string, string>
                {
                    {
                        "position_id",
                        "ALTER TABLE teamapplication_phase ADD COLUMN IF NOT EXISTS position_id TEXT"
                    },
                    {
                        "name",
                        "ALTER TABLE teamapplication_phase ADD COLUMN IF NOT EXISTS name TEXT DEFAULT ''"
                    },
                    {
                        "state",
                        "ALTER TABLE teamapplication_phase ADD COLUMN IF NOT EXISTS state TEXT DEFAULT 'draft'"
                    },
                    {
                        "questionset_version",
                        "ALTER TABLE teamapplication_phase ADD COLUMN IF NOT EXISTS questionset_version INTEGER DEFAULT 0"
                    },
                    {
                        "opens_at",
                        "ALTER TABLE teamapplication_phase ADD COLUMN IF NOT EXISTS opens_at BIGINT DEFAULT 0"
                    },
                    {
                        "closes_at",
                        "ALTER TABLE teamapplication_phase ADD COLUMN IF NOT EXISTS closes_at BIGINT DEFAULT 0"
                    },
                    {
                        "created_by",
                        "ALTER TABLE teamapplication_phase ADD COLUMN IF NOT EXISTS created_by BIGINT DEFAULT 0"
                    },
                    {
                        "created_at",
                        "ALTER TABLE teamapplication_phase ADD COLUMN IF NOT EXISTS created_at BIGINT DEFAULT 0"
                    }
                }
            },
            {
                "teamapplication_application", new Dictionary<string, string>
                {
                    {
                        "user_id",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS user_id BIGINT"
                    },
                    {
                        "position_id",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS position_id TEXT"
                    },
                    {
                        "phase_id",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS phase_id TEXT"
                    },
                    {
                        "questionset_version",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS questionset_version INTEGER DEFAULT 0"
                    },
                    {
                        "attempt",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS attempt INTEGER DEFAULT 1"
                    },
                    {
                        "status",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS status TEXT DEFAULT 'eingereicht'"
                    },
                    {
                        "submitted_at",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS submitted_at BIGINT DEFAULT 0"
                    },
                    {
                        "decided_at",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS decided_at BIGINT DEFAULT 0"
                    },
                    {
                        "decided_by",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS decided_by BIGINT DEFAULT 0"
                    },
                    {
                        "decision_text",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS decision_text TEXT DEFAULT ''"
                    },
                    {
                        "dm_delivered",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS dm_delivered BOOLEAN"
                    },
                    {
                        "dm_error",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS dm_error TEXT DEFAULT ''"
                    },
                    {
                        "withdrawn_at",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS withdrawn_at BIGINT DEFAULT 0"
                    },
                    {
                        // Readers used to be an id array without times. Copy them over as "time unknown",
                        // then drop the column; once it is gone this does nothing.
                        "seen_by_to_seen_table",
                        "DO $$ BEGIN " +
                        "IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() " +
                        "AND table_name = 'teamapplication_application' AND column_name = 'seen_by') THEN " +
                        "INSERT INTO teamapplication_seen (application_id, user_id, seen_at) " +
                        "SELECT a.application_id, s.user_id, 0 FROM teamapplication_application a, " +
                        "unnest(a.seen_by) AS s(user_id) ON CONFLICT DO NOTHING; " +
                        "ALTER TABLE teamapplication_application DROP COLUMN seen_by; " +
                        "END IF; END $$"
                    },
                    {
                        "level_snapshot",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS level_snapshot INTEGER DEFAULT 0"
                    },
                    {
                        "xp_snapshot",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS xp_snapshot INTEGER DEFAULT 0"
                    },
                    {
                        "joined_at_snapshot",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS joined_at_snapshot BIGINT DEFAULT 0"
                    },
                    {
                        "account_created_snapshot",
                        "ALTER TABLE teamapplication_application ADD COLUMN IF NOT EXISTS account_created_snapshot BIGINT DEFAULT 0"
                    },
                    {
                        "slot_unique",
                        "CREATE UNIQUE INDEX IF NOT EXISTS idx_teamapplication_application_slot ON teamapplication_application (user_id, position_id, phase_id, attempt) WHERE status <> 'zurueckgezogen'"
                    }
                }
            },
            {
                "teamapplication_answers", new Dictionary<string, string>
                {
                    {
                        "sort_order",
                        "ALTER TABLE teamapplication_answers ADD COLUMN IF NOT EXISTS sort_order INTEGER DEFAULT 0"
                    },
                    {
                        "question_text_snapshot",
                        "ALTER TABLE teamapplication_answers ADD COLUMN IF NOT EXISTS question_text_snapshot TEXT DEFAULT ''"
                    },
                    {
                        "question_type",
                        "ALTER TABLE teamapplication_answers ADD COLUMN IF NOT EXISTS question_type TEXT DEFAULT 'shorttext'"
                    },
                    {
                        "answer",
                        "ALTER TABLE teamapplication_answers ADD COLUMN IF NOT EXISTS answer TEXT DEFAULT ''"
                    },
                    {
                        "answer_options",
                        "ALTER TABLE teamapplication_answers ADD COLUMN IF NOT EXISTS answer_options JSONB DEFAULT '[]'::jsonb"
                    }
                }
            },
            {
                "teamapplication_notes", new Dictionary<string, string>
                {
                    {
                        "application_id",
                        "ALTER TABLE teamapplication_notes ADD COLUMN IF NOT EXISTS application_id TEXT"
                    },
                    {
                        "author_id",
                        "ALTER TABLE teamapplication_notes ADD COLUMN IF NOT EXISTS author_id BIGINT"
                    },
                    {
                        "text",
                        "ALTER TABLE teamapplication_notes ADD COLUMN IF NOT EXISTS text TEXT DEFAULT ''"
                    },
                    {
                        "created_at",
                        "ALTER TABLE teamapplication_notes ADD COLUMN IF NOT EXISTS created_at BIGINT DEFAULT 0"
                    }
                }
            },
            {
                "teamapplication_permissions", new Dictionary<string, string>
                {
                    {
                        "role_id",
                        "ALTER TABLE teamapplication_permissions ADD COLUMN IF NOT EXISTS role_id BIGINT"
                    },
                    {
                        "position_id",
                        "ALTER TABLE teamapplication_permissions ADD COLUMN IF NOT EXISTS position_id TEXT"
                    },
                    {
                        "phase_id",
                        "ALTER TABLE teamapplication_permissions ADD COLUMN IF NOT EXISTS phase_id TEXT"
                    },
                    {
                        "permission",
                        "ALTER TABLE teamapplication_permissions ADD COLUMN IF NOT EXISTS permission TEXT"
                    },
                    {
                        "rule_unique",
                        "CREATE UNIQUE INDEX IF NOT EXISTS idx_teamapplication_permissions_rule ON teamapplication_permissions (role_id, coalesce(position_id, ''), coalesce(phase_id, ''), permission)"
                    }
                }
            },
            {
                "teamapplication_reapply_grant", new Dictionary<string, string>
                {
                    {
                        "user_id",
                        "ALTER TABLE teamapplication_reapply_grant ADD COLUMN IF NOT EXISTS user_id BIGINT"
                    },
                    {
                        "position_id",
                        "ALTER TABLE teamapplication_reapply_grant ADD COLUMN IF NOT EXISTS position_id TEXT"
                    },
                    {
                        "phase_id",
                        "ALTER TABLE teamapplication_reapply_grant ADD COLUMN IF NOT EXISTS phase_id TEXT"
                    },
                    {
                        "granted_by",
                        "ALTER TABLE teamapplication_reapply_grant ADD COLUMN IF NOT EXISTS granted_by BIGINT DEFAULT 0"
                    },
                    {
                        "granted_at",
                        "ALTER TABLE teamapplication_reapply_grant ADD COLUMN IF NOT EXISTS granted_at BIGINT DEFAULT 0"
                    },
                    {
                        "reason",
                        "ALTER TABLE teamapplication_reapply_grant ADD COLUMN IF NOT EXISTS reason TEXT DEFAULT ''"
                    },
                    {
                        "used_at",
                        "ALTER TABLE teamapplication_reapply_grant ADD COLUMN IF NOT EXISTS used_at BIGINT DEFAULT 0"
                    }
                }
            },
            {
                "teamapplication_templates", new Dictionary<string, string>
                {
                    {
                        "position_id",
                        "ALTER TABLE teamapplication_templates ADD COLUMN IF NOT EXISTS position_id TEXT"
                    },
                    {
                        "kind",
                        "ALTER TABLE teamapplication_templates ADD COLUMN IF NOT EXISTS kind TEXT DEFAULT 'accept'"
                    },
                    {
                        "name",
                        "ALTER TABLE teamapplication_templates ADD COLUMN IF NOT EXISTS name TEXT DEFAULT ''"
                    },
                    {
                        "text",
                        "ALTER TABLE teamapplication_templates ADD COLUMN IF NOT EXISTS text TEXT DEFAULT ''"
                    }
                }
            },
            {
                "teamapplication_placeholder", new Dictionary<string, string>
                {
                    {
                        "text",
                        "ALTER TABLE teamapplication_placeholder ADD COLUMN IF NOT EXISTS text TEXT DEFAULT ''"
                    }
                }
            },
            {
                "teamapplication_seen", new Dictionary<string, string>
                {
                    {
                        "seen_at",
                        "ALTER TABLE teamapplication_seen ADD COLUMN IF NOT EXISTS seen_at BIGINT DEFAULT 0"
                    }
                }
            },
            {
                // The ticket tables predate the schema code and are not created here, hence IF EXISTS.
                "ticketstore", new Dictionary<string, string>
                {
                    {
                        "opened_at",
                        "ALTER TABLE IF EXISTS ticketstore ADD COLUMN IF NOT EXISTS opened_at BIGINT DEFAULT 0"
                    },
                    {
                        "closed_at",
                        "ALTER TABLE IF EXISTS ticketstore ADD COLUMN IF NOT EXISTS closed_at BIGINT DEFAULT 0"
                    }
                }
            },
            {
                "dashboardlogins", new Dictionary<string, string>
                {
                    { "drop", "DROP TABLE IF EXISTS dashboardlogins" }
                }
            },
            {
                "pollsystem", new Dictionary<string, string>
                {
                    { "drop", "DROP TABLE IF EXISTS pollsystem" }
                }
            },
            {
                "pollvotes", new Dictionary<string, string>
                {
                    { "drop", "DROP TABLE IF EXISTS pollvotes" }
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
        await InitBotSettings();
        CurrentApplication.Logger.Information("Database tables updated.");
    }

    private static async Task InitBotSettings()
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var defaults = new (string section, string key, string value)[]
        {
            ("AntiRaid", "DateKickActive", "false"),
            ("AntiRaid", "DateKickDays", "14"),
            ("BoosterColors", "Enabled", "false"),
            ("BoosterColors", "PanelChannelId", "0"),
            ("BoosterColors", "PanelMessageId", "0"),
            ("BoosterColors", "BypassEligibility", "false"),
            ("BoosterColors", "EmbedTitle", "Booster Farben"),
            ("BoosterColors", "EmbedDescription",
                "Hier kannst du dir deine Booster Farbe auswählen. Sie ist jederzeit anpassbar. Deine Farbe wird automatisch wieder entfernt, sobald dein Boost ausläuft."),
            ("ExtraPermissions", "AutoRevokeOnConditionLoss", "false"),
            ("TeamApplications", "LevelGateText",
                "Bring dich doch gerne etwas mehr in den Server ein, bevor du dich bewirbst."),
            ("TeamApplications", "PanelClosedText", "Bewerbungen aktuell geschlossen"),
            ("TeamApplications", "PanelOpensAtText", "Naechste Bewerbungsphase ab"),
            ("TeamApplications", "DmSubmitTitle", "Bewerbung eingegangen"),
            ("TeamApplications", "DmSubmitText",
                "Hey {user}, deine Bewerbung für die Position **{position}** ist bei uns eingegangen. Wir melden uns, sobald wir sie angesehen haben."),
            ("TeamApplications", "DmAcceptTitle", "Deine Bewerbung wurde angenommen"),
            ("TeamApplications", "DmAcceptText",
                "Hey {user}, deine Bewerbung für die Position **{position}** wurde angenommen."),
            ("TeamApplications", "DmRejectTitle", "Deine Bewerbung wurde abgelehnt"),
            ("TeamApplications", "DmRejectText",
                "Hey {user}, deine Bewerbung für die Position **{position}** wurde leider abgelehnt."),
            ("TeamApplications", "DmGrantTitle", "Du darfst dich erneut bewerben"),
            ("TeamApplications", "DmGrantText",
                "Hey {user}, du darfst dich für die Position **{position}** sofort erneut bewerben."),
            ("TeamApplications", "NotifyNewApplicationText", "Neue Bewerbung eingegangen.")
        };

        foreach (var (section, key, value) in defaults)
        {
            await using var cmd = con.CreateCommand(
                "INSERT INTO botsettings (section, key, value) VALUES (@section, @key, @value) ON CONFLICT (section, key) DO NOTHING");
            cmd.Parameters.AddWithValue("section", section);
            cmd.Parameters.AddWithValue("key", key);
            cmd.Parameters.AddWithValue("value", value);
            await cmd.ExecuteNonQueryAsync();
        }
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