#region

using AGC_Management.Utils;

#endregion

namespace AGC_Management.Services;

public static class DatabaseService
{
    /// <summary>
    ///     Schema work is not a query: building an index over a grown metrics table takes minutes and
    ///     must not die on Npgsql's 30 second default. A client-side timeout does not stop the server
    ///     either, so every restart would stack another index build on top of the running one.
    /// </summary>
    private const int SchemaCommandTimeoutSeconds = 900;

    public static string GetConnectionString()
    {
        var dbConfigSection = GlobalProperties.DebugMode ? "DatabaseCfgDBG" : "DatabaseCfg";
        var DbHost = BotConfig.GetConfig()[dbConfigSection]["Database_Host"];
        var DbUser = BotConfig.GetConfig()[dbConfigSection]["Database_User"];
        var DbPass = BotConfig.GetConfig()[dbConfigSection]["Database_Password"];
        var DbName = BotConfig.GetConfig()[dbConfigSection]["Database"];
        return $"Host={DbHost};Username={DbUser};Password={DbPass};Database={DbName};Maximum Pool Size=25;Keepalive=30;";
    }


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
                "idx_metrics_messages_timestamp",
                "CREATE INDEX IF NOT EXISTS idx_metrics_messages_timestamp ON metrics_messages (timestamp)"
            },
            {
                "idx_metrics_voice_timestamp",
                "CREATE INDEX IF NOT EXISTS idx_metrics_voice_timestamp ON metrics_voice (timestamp)"
            },
            {
                "idx_metrics_voice_company",
                "CREATE INDEX IF NOT EXISTS idx_metrics_voice_company ON metrics_voice (channelid, timestamp, userid)"
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
                "bans",
                "CREATE TABLE IF NOT EXISTS bans (userid BIGINT, punisherid BIGINT, datum BIGINT, description VARCHAR, caseid VARCHAR)"
            },
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
                "CREATE TABLE IF NOT EXISTS teamapplication_position (position_id TEXT PRIMARY KEY, position_name TEXT, role_name TEXT DEFAULT '', description TEXT DEFAULT '', applicant_hints TEXT DEFAULT '', min_level INTEGER DEFAULT 20, notify_channel_id BIGINT DEFAULT 0, active BOOLEAN DEFAULT false, always_open BOOLEAN DEFAULT false, sort_order INTEGER DEFAULT 0, created_at BIGINT DEFAULT 0)"
            },
            {
                "teamapplication_questionset",
                "CREATE TABLE IF NOT EXISTS teamapplication_questionset (position_id TEXT, version INTEGER, state TEXT DEFAULT 'draft', created_by BIGINT DEFAULT 0, created_at BIGINT DEFAULT 0, PRIMARY KEY (position_id, version))"
            },
            {
                "teamapplication_questions",
                "CREATE TABLE IF NOT EXISTS teamapplication_questions (question_id TEXT PRIMARY KEY, position_id TEXT, version INTEGER DEFAULT 0, sort_order INTEGER DEFAULT 0, type TEXT DEFAULT 'shorttext', text TEXT DEFAULT '', description TEXT DEFAULT '', required BOOLEAN DEFAULT true, min_length INTEGER DEFAULT 0, max_length INTEGER DEFAULT 0, min_value BIGINT DEFAULT 0, max_value BIGINT DEFAULT 0, options JSONB DEFAULT '[]'::jsonb, min_selections INTEGER DEFAULT 0, max_selections INTEGER DEFAULT 0, condition_question_id TEXT DEFAULT '', condition_value TEXT DEFAULT '', number_display TEXT DEFAULT 'text', number_step INTEGER DEFAULT 1, other_option_value TEXT DEFAULT '', other_is_long_text BOOLEAN DEFAULT false)"
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
                "CREATE TABLE IF NOT EXISTS teamapplication_answers (application_id TEXT, question_id TEXT, sort_order INTEGER DEFAULT 0, question_text_snapshot TEXT DEFAULT '', question_type TEXT DEFAULT 'shorttext', answer TEXT DEFAULT '', answer_options JSONB DEFAULT '[]'::jsonb, other_text TEXT DEFAULT '', other_option_snapshot TEXT DEFAULT '', PRIMARY KEY (application_id, question_id))"
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
            },
            {
                "activity_role_rules",
                "CREATE TABLE IF NOT EXISTS activity_role_rules (rule_id TEXT PRIMARY KEY, name TEXT DEFAULT '', enabled BOOLEAN DEFAULT true, metric TEXT DEFAULT 'messages', mode TEXT DEFAULT 'topn', window_type TEXT DEFAULT 'rolling', window_days INTEGER DEFAULT 7, window_start BIGINT, window_end BIGINT, scope_ids BIGINT[] DEFAULT '{}', exclude_scope_ids BIGINT[] DEFAULT '{}', min_activity BIGINT DEFAULT 0, threshold_role_id BIGINT DEFAULT 0, threshold_value BIGINT DEFAULT 0, threshold_comparator TEXT DEFAULT 'gte', auto_revoke BOOLEAN DEFAULT true, announce_channel_id BIGINT DEFAULT 0, announce_message TEXT DEFAULT '', announce_interval_days INTEGER DEFAULT 0, last_announced_at BIGINT DEFAULT 0, winner_line_blocks TEXT DEFAULT 'medal,mention,count,role', medal_rank1 TEXT DEFAULT '🥇', medal_rank2 TEXT DEFAULT '🥈', medal_rank3 TEXT DEFAULT '🥉', medal_other_template TEXT DEFAULT '`#{rank}`', count_divisor BIGINT DEFAULT 1, count_suffix TEXT DEFAULT '', count_monospace BOOLEAN DEFAULT true, exclude_left_members BOOLEAN DEFAULT true, count_muted_voice BOOLEAN DEFAULT true, count_deafened_voice BOOLEAN DEFAULT true, count_solo_voice BOOLEAN DEFAULT true, created_by BIGINT DEFAULT 0, created_at BIGINT DEFAULT 0)"
            },
            {
                "activity_role_tiers",
                "CREATE TABLE IF NOT EXISTS activity_role_tiers (tier_id TEXT PRIMARY KEY, rule_id TEXT, rank_from INTEGER DEFAULT 1, rank_to INTEGER DEFAULT 1, roleid BIGINT DEFAULT 0)"
            },
            {
                "banrequest_stages",
                "CREATE TABLE IF NOT EXISTS banrequest_stages (stage_id TEXT PRIMARY KEY, position INTEGER DEFAULT 0, enabled BOOLEAN DEFAULT true, delay_minutes INTEGER DEFAULT 0, target TEXT DEFAULT 'role', role_id BIGINT DEFAULT 0)"
            },
            {
                "member_lastseen",
                "CREATE TABLE IF NOT EXISTS member_lastseen (userid BIGINT PRIMARY KEY, last_seen BIGINT DEFAULT 0, signal TEXT DEFAULT 'message')"
            },
            {
                "idx_activity_role_tiers_rule",
                "CREATE INDEX IF NOT EXISTS idx_activity_role_tiers_rule ON activity_role_tiers (rule_id)"
            },
            {
                "activity_role_grants",
                "CREATE TABLE IF NOT EXISTS activity_role_grants (rule_id TEXT, userid BIGINT, roleid BIGINT DEFAULT 0, rank INTEGER DEFAULT 0, granted_at BIGINT DEFAULT 0, PRIMARY KEY (rule_id, userid, roleid))"
            },
            {
                "idx_activity_role_grants_userid",
                "CREATE INDEX IF NOT EXISTS idx_activity_role_grants_userid ON activity_role_grants (userid)"
            },
            {
                "eligibility_conditions",
                "CREATE TABLE IF NOT EXISTS eligibility_conditions (condition_id TEXT PRIMARY KEY, owner_type TEXT, owner_id TEXT, group_id INTEGER DEFAULT 1, condition_type TEXT DEFAULT 'role', comparator TEXT DEFAULT 'gte', value BIGINT DEFAULT 0, scope_ids BIGINT[] DEFAULT '{}', negate BOOLEAN DEFAULT false, created_at BIGINT DEFAULT 0)"
            },
            {
                "idx_eligibility_conditions_owner",
                "CREATE INDEX IF NOT EXISTS idx_eligibility_conditions_owner ON eligibility_conditions (owner_type, owner_id)"
            },
            {
                "activity_announcement_groups",
                "CREATE TABLE IF NOT EXISTS activity_announcement_groups (group_id TEXT PRIMARY KEY, name TEXT DEFAULT '', channel_id BIGINT DEFAULT 0, interval_days INTEGER DEFAULT 0, message TEXT DEFAULT '', last_announced_at BIGINT DEFAULT 0, created_by BIGINT DEFAULT 0, created_at BIGINT DEFAULT 0)"
            },
            {
                "activity_announcement_group_rules",
                "CREATE TABLE IF NOT EXISTS activity_announcement_group_rules (group_id TEXT, rule_id TEXT, alias TEXT DEFAULT '', PRIMARY KEY (group_id, rule_id))"
            },
            {
                "idx_activity_announcement_group_rules_alias",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_activity_announcement_group_rules_alias ON activity_announcement_group_rules (group_id, alias)"
            },
            // The ticket tables predate the schema code, so the live database already has
            // ticketstore, ticketcache, ticketcategories and snippets. They are declared here for
            // fresh installs; UpdateTables carries the matching ALTER ... IF EXISTS entries.
            {
                "ticketstore",
                "CREATE TABLE IF NOT EXISTS ticketstore (ticket_id TEXT, ticket_owner BIGINT, tickettype TEXT, closed BOOLEAN DEFAULT false, opened_at BIGINT DEFAULT 0, closed_at BIGINT DEFAULT 0, user_transscript_url TEXT, team_transscript_url TEXT)"
            },
            {
                "ticketcache",
                "CREATE TABLE IF NOT EXISTS ticketcache (ticket_id TEXT, ticket_owner BIGINT, tchannel_id BIGINT, claimed BOOLEAN DEFAULT false, claimed_from BIGINT, ticket_users BIGINT[] DEFAULT '{}', closed_users BIGINT[] DEFAULT '{}', last_activity BIGINT DEFAULT 0, reminder_sent_at BIGINT DEFAULT 0, header_message_id BIGINT DEFAULT 0)"
            },
            {
                "ticketcategories",
                "CREATE TABLE IF NOT EXISTS ticketcategories (custom_id TEXT, category_text TEXT, description TEXT, emoji TEXT, channel_prefix TEXT, discord_category_id BIGINT DEFAULT 0, handler_role_ids BIGINT[] DEFAULT '{}', ping_role_ids BIGINT[] DEFAULT '{}', welcome_text TEXT, intake_enabled BOOLEAN DEFAULT false, max_open_per_user INTEGER DEFAULT 1, autoclose_enabled BOOLEAN DEFAULT false, autoclose_reminder_hours INTEGER DEFAULT 0, autoclose_hours INTEGER DEFAULT 0, sort_order INTEGER DEFAULT 0, enabled BOOLEAN DEFAULT true)"
            },
            {
                "snippets",
                "CREATE TABLE IF NOT EXISTS snippets (snip_id TEXT, snipped_text TEXT)"
            },
            {
                "subscriptions",
                "CREATE TABLE IF NOT EXISTS subscriptions (user_id BIGINT, channel_id BIGINT, mode INTEGER DEFAULT 0)"
            },
            {
                "ticket_category_questions",
                "CREATE TABLE IF NOT EXISTS ticket_category_questions (id TEXT, category_id TEXT, position INTEGER DEFAULT 0, label TEXT, placeholder TEXT, style TEXT DEFAULT 'short', required BOOLEAN DEFAULT true, min_length INTEGER DEFAULT 0, max_length INTEGER DEFAULT 0)"
            },
            {
                "ticket_intake_answers",
                "CREATE TABLE IF NOT EXISTS ticket_intake_answers (ticket_id TEXT, question_id TEXT, question_label TEXT, answer TEXT, position INTEGER DEFAULT 0)"
            },
            {
                "ticket_events",
                "CREATE TABLE IF NOT EXISTS ticket_events (ticket_id TEXT, event_type TEXT, actor_id BIGINT DEFAULT 0, data TEXT, timestamp BIGINT DEFAULT 0)"
            },
            {
                "ticket_counters",
                "CREATE TABLE IF NOT EXISTS ticket_counters (category_id TEXT, next_number BIGINT DEFAULT 0)"
            },
            {
                "idx_ticket_counters_category",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_ticket_counters_category ON ticket_counters (category_id)"
            },
            {
                "idx_ticket_category_questions_category",
                "CREATE INDEX IF NOT EXISTS idx_ticket_category_questions_category ON ticket_category_questions (category_id)"
            },
            {
                "idx_ticket_intake_answers_ticket",
                "CREATE INDEX IF NOT EXISTS idx_ticket_intake_answers_ticket ON ticket_intake_answers (ticket_id)"
            },
            {
                "idx_ticket_events_ticket",
                "CREATE INDEX IF NOT EXISTS idx_ticket_events_ticket ON ticket_events (ticket_id)"
            },
            {
                "infopanels",
                "CREATE TABLE IF NOT EXISTS infopanels (id TEXT, name TEXT, channel_id BIGINT DEFAULT 0, message_id BIGINT DEFAULT 0, enabled BOOLEAN DEFAULT false, header_title TEXT, header_text TEXT, author_name TEXT, author_icon_mode TEXT DEFAULT 'guild', author_icon_url TEXT, banner_mode TEXT DEFAULT 'guild', banner_url TEXT, color TEXT DEFAULT '2F3136', auto_repost BOOLEAN DEFAULT true, rendered_hash TEXT, sort_order INTEGER DEFAULT 0)"
            },
            {
                "infopanel_groups",
                "CREATE TABLE IF NOT EXISTS infopanel_groups (id TEXT, panel_id TEXT, kind TEXT DEFAULT 'select', placeholder TEXT, date_label TEXT, position INTEGER DEFAULT 0, enabled BOOLEAN DEFAULT true)"
            },
            {
                "infopanel_pages",
                "CREATE TABLE IF NOT EXISTS infopanel_pages (id TEXT, group_id TEXT, panel_id TEXT, kind TEXT DEFAULT 'text', label TEXT, description TEXT, emoji TEXT, button_style INTEGER DEFAULT 1, url TEXT, title TEXT, content TEXT, image_url TEXT, color TEXT, position INTEGER DEFAULT 0, enabled BOOLEAN DEFAULT true, updated_at BIGINT DEFAULT 0)"
            },
            {
                "idx_infopanels_id",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_infopanels_id ON infopanels (id)"
            },
            {
                "idx_infopanel_groups_panel",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_infopanel_groups_panel ON infopanel_groups (panel_id, id)"
            },
            {
                "idx_infopanel_pages_group",
                "CREATE UNIQUE INDEX IF NOT EXISTS idx_infopanel_pages_group ON infopanel_pages (group_id, id)"
            },
            {
                "idx_infopanel_pages_panel",
                "CREATE INDEX IF NOT EXISTS idx_infopanel_pages_panel ON infopanel_pages (panel_id)"
            },
            {
                "autoposts",
                "CREATE TABLE IF NOT EXISTS autoposts (autopost_id TEXT PRIMARY KEY, name TEXT DEFAULT '', enabled BOOLEAN DEFAULT false, channel_id BIGINT DEFAULT 0, trigger_type TEXT DEFAULT 'messages', threshold INTEGER DEFAULT 100, count_scope_ids BIGINT[] DEFAULT '{}', subtract_leaves BOOLEAN DEFAULT true, interval_minutes INTEGER DEFAULT 60, parent_autopost_id TEXT DEFAULT '', parent_delay_seconds INTEGER DEFAULT 0, delete_previous BOOLEAN DEFAULT false, rotation_mode TEXT DEFAULT 'sequential', steps JSONB DEFAULT '[]'::jsonb, counter_since BIGINT DEFAULT 0, join_count INTEGER DEFAULT 0, last_posted_at BIGINT DEFAULT 0, last_message_ids BIGINT[] DEFAULT '{}', rotation_state INTEGER[] DEFAULT '{}', created_at BIGINT DEFAULT 0, updated_at BIGINT DEFAULT 0)"
            },
            {
                "autopost_conditions",
                "CREATE TABLE IF NOT EXISTS autopost_conditions (condition_id TEXT PRIMARY KEY, autopost_id TEXT, condition_type TEXT, negate BOOLEAN DEFAULT false, params JSONB DEFAULT '{}'::jsonb, created_at BIGINT DEFAULT 0)"
            },
            {
                "idx_autopost_conditions_autopost",
                "CREATE INDEX IF NOT EXISTS idx_autopost_conditions_autopost ON autopost_conditions (autopost_id)"
            },
            {
                "autopost_queue",
                "CREATE TABLE IF NOT EXISTS autopost_queue (queue_id TEXT PRIMARY KEY, autopost_id TEXT, step_index INTEGER DEFAULT 0, due_at BIGINT DEFAULT 0, check_filters BOOLEAN DEFAULT false)"
            },
            {
                "idx_metrics_messages_channel_timestamp",
                "CREATE INDEX IF NOT EXISTS idx_metrics_messages_channel_timestamp ON metrics_messages (channelid, timestamp)"
            }
        };
        var progressBar = new ConsoleProgressBar(tableCommands.Count);

        foreach (var kvp in tableCommands)
        {
            var tableName = kvp.Key;
            var createTableCommand = kvp.Value;

            await using var cmdCreate = conn.CreateCommand(createTableCommand);
            cmdCreate.CommandTimeout = SchemaCommandTimeoutSeconds;
            await cmdCreate.ExecuteNonQueryAsync();
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
                "bans",
                new Dictionary<string, string>
                {
                    { "userid", "ALTER TABLE bans ADD COLUMN IF NOT EXISTS userid BIGINT" },
                    { "punisherid", "ALTER TABLE bans ADD COLUMN IF NOT EXISTS punisherid BIGINT" },
                    { "datum", "ALTER TABLE bans ADD COLUMN IF NOT EXISTS datum BIGINT" },
                    { "description", "ALTER TABLE bans ADD COLUMN IF NOT EXISTS description VARCHAR" },
                    { "caseid", "ALTER TABLE bans ADD COLUMN IF NOT EXISTS caseid VARCHAR" }
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
                        "applicant_hints",
                        "ALTER TABLE teamapplication_position ADD COLUMN IF NOT EXISTS applicant_hints TEXT DEFAULT ''"
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
                        "sort_order",
                        "ALTER TABLE teamapplication_position ADD COLUMN IF NOT EXISTS sort_order INTEGER DEFAULT 0"
                    },
                    {
                        "created_at",
                        "ALTER TABLE teamapplication_position ADD COLUMN IF NOT EXISTS created_at BIGINT DEFAULT 0"
                    },
                    {
                        "role_name",
                        "ALTER TABLE teamapplication_position ADD COLUMN IF NOT EXISTS role_name TEXT DEFAULT ''"
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
                    },
                    {
                        "condition_question_id",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS condition_question_id TEXT DEFAULT ''"
                    },
                    {
                        "condition_value",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS condition_value TEXT DEFAULT ''"
                    },
                    {
                        "number_display",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS number_display TEXT DEFAULT 'text'"
                    },
                    {
                        "number_step",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS number_step INTEGER DEFAULT 1"
                    },
                    {
                        "other_option_value",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS other_option_value TEXT DEFAULT ''"
                    },
                    {
                        "other_is_long_text",
                        "ALTER TABLE teamapplication_questions ADD COLUMN IF NOT EXISTS other_is_long_text BOOLEAN DEFAULT false"
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
                    },
                    {
                        "other_text",
                        "ALTER TABLE teamapplication_answers ADD COLUMN IF NOT EXISTS other_text TEXT DEFAULT ''"
                    },
                    {
                        "other_option_snapshot",
                        "ALTER TABLE teamapplication_answers ADD COLUMN IF NOT EXISTS other_option_snapshot TEXT DEFAULT ''"
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
                // The ticket tables predate the schema code, so a live database already has them and
                // the CREATE TABLE entries only cover fresh installs. Hence IF EXISTS here.
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
                "ticketcache", new Dictionary<string, string>
                {
                    {
                        "last_activity",
                        "ALTER TABLE IF EXISTS ticketcache ADD COLUMN IF NOT EXISTS last_activity BIGINT DEFAULT 0"
                    },
                    {
                        "reminder_sent_at",
                        "ALTER TABLE IF EXISTS ticketcache ADD COLUMN IF NOT EXISTS reminder_sent_at BIGINT DEFAULT 0"
                    },
                    {
                        "header_message_id",
                        "ALTER TABLE IF EXISTS ticketcache ADD COLUMN IF NOT EXISTS header_message_id BIGINT DEFAULT 0"
                    },
                    {
                        // Closing empties ticket_users, so the roster is kept here to make reopening possible.
                        "closed_users",
                        "ALTER TABLE IF EXISTS ticketcache ADD COLUMN IF NOT EXISTS closed_users BIGINT[] DEFAULT '{}'"
                    },
                    {
                        // Tickets that were already open when this shipped have no recorded activity.
                        // Leaving them at 0 would read as "inactive since 1970" to the auto-close task.
                        "last_activity_backfill",
                        "UPDATE ticketcache SET last_activity = EXTRACT(EPOCH FROM now())::BIGINT WHERE last_activity IS NULL OR last_activity = 0"
                    }
                }
            },
            {
                "ticketcategories", new Dictionary<string, string>
                {
                    { "description", "ALTER TABLE IF EXISTS ticketcategories ADD COLUMN IF NOT EXISTS description TEXT" },
                    { "emoji", "ALTER TABLE IF EXISTS ticketcategories ADD COLUMN IF NOT EXISTS emoji TEXT" },
                    {
                        "channel_prefix",
                        "ALTER TABLE IF EXISTS ticketcategories ADD COLUMN IF NOT EXISTS channel_prefix TEXT"
                    },
                    {
                        "discord_category_id",
                        "ALTER TABLE IF EXISTS ticketcategories ADD COLUMN IF NOT EXISTS discord_category_id BIGINT DEFAULT 0"
                    },
                    {
                        "handler_role_ids",
                        "ALTER TABLE IF EXISTS ticketcategories ADD COLUMN IF NOT EXISTS handler_role_ids BIGINT[] DEFAULT '{}'"
                    },
                    {
                        "ping_role_ids",
                        "ALTER TABLE IF EXISTS ticketcategories ADD COLUMN IF NOT EXISTS ping_role_ids BIGINT[] DEFAULT '{}'"
                    },
                    {
                        "welcome_text",
                        "ALTER TABLE IF EXISTS ticketcategories ADD COLUMN IF NOT EXISTS welcome_text TEXT"
                    },
                    {
                        "intake_enabled",
                        "ALTER TABLE IF EXISTS ticketcategories ADD COLUMN IF NOT EXISTS intake_enabled BOOLEAN DEFAULT false"
                    },
                    {
                        "max_open_per_user",
                        "ALTER TABLE IF EXISTS ticketcategories ADD COLUMN IF NOT EXISTS max_open_per_user INTEGER DEFAULT 1"
                    },
                    {
                        "autoclose_enabled",
                        "ALTER TABLE IF EXISTS ticketcategories ADD COLUMN IF NOT EXISTS autoclose_enabled BOOLEAN DEFAULT false"
                    },
                    {
                        "autoclose_reminder_hours",
                        "ALTER TABLE IF EXISTS ticketcategories ADD COLUMN IF NOT EXISTS autoclose_reminder_hours INTEGER DEFAULT 0"
                    },
                    {
                        "autoclose_hours",
                        "ALTER TABLE IF EXISTS ticketcategories ADD COLUMN IF NOT EXISTS autoclose_hours INTEGER DEFAULT 0"
                    },
                    {
                        "sort_order",
                        "ALTER TABLE IF EXISTS ticketcategories ADD COLUMN IF NOT EXISTS sort_order INTEGER DEFAULT 0"
                    },
                    {
                        "enabled",
                        "ALTER TABLE IF EXISTS ticketcategories ADD COLUMN IF NOT EXISTS enabled BOOLEAN DEFAULT true"
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
            },
            {
                "activity_role_rules", new Dictionary<string, string>
                {
                    { "name", "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS name TEXT DEFAULT ''" },
                    {
                        "enabled",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS enabled BOOLEAN DEFAULT true"
                    },
                    {
                        "metric",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS metric TEXT DEFAULT 'messages'"
                    },
                    { "mode", "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS mode TEXT DEFAULT 'topn'" },
                    {
                        "window_type",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS window_type TEXT DEFAULT 'rolling'"
                    },
                    {
                        "window_days",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS window_days INTEGER DEFAULT 7"
                    },
                    {
                        "window_start",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS window_start BIGINT"
                    },
                    { "window_end", "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS window_end BIGINT" },
                    {
                        "scope_ids",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS scope_ids BIGINT[] DEFAULT '{}'"
                    },
                    {
                        "exclude_scope_ids",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS exclude_scope_ids BIGINT[] DEFAULT '{}'"
                    },
                    {
                        "min_activity",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS min_activity BIGINT DEFAULT 0"
                    },
                    {
                        "threshold_role_id",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS threshold_role_id BIGINT DEFAULT 0"
                    },
                    {
                        "threshold_value",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS threshold_value BIGINT DEFAULT 0"
                    },
                    {
                        "threshold_comparator",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS threshold_comparator TEXT DEFAULT 'gte'"
                    },
                    {
                        "auto_revoke",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS auto_revoke BOOLEAN DEFAULT true"
                    },
                    {
                        "announce_channel_id",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS announce_channel_id BIGINT DEFAULT 0"
                    },
                    {
                        "announce_message",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS announce_message TEXT DEFAULT ''"
                    },
                    {
                        "announce_interval_days",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS announce_interval_days INTEGER DEFAULT 0"
                    },
                    {
                        "last_announced_at",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS last_announced_at BIGINT DEFAULT 0"
                    },
                    {
                        "winner_line_blocks",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS winner_line_blocks TEXT DEFAULT 'medal,mention,count,role'"
                    },
                    {
                        "medal_rank1",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS medal_rank1 TEXT DEFAULT '🥇'"
                    },
                    {
                        "medal_rank2",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS medal_rank2 TEXT DEFAULT '🥈'"
                    },
                    {
                        "medal_rank3",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS medal_rank3 TEXT DEFAULT '🥉'"
                    },
                    {
                        "medal_other_template",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS medal_other_template TEXT DEFAULT '`#{rank}`'"
                    },
                    {
                        "count_divisor",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS count_divisor BIGINT DEFAULT 1"
                    },
                    {
                        "count_suffix",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS count_suffix TEXT DEFAULT ''"
                    },
                    {
                        "count_monospace",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS count_monospace BOOLEAN DEFAULT true"
                    },
                    {
                        "exclude_left_members",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS exclude_left_members BOOLEAN DEFAULT true"
                    },
                    {
                        "count_muted_voice",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS count_muted_voice BOOLEAN DEFAULT true"
                    },
                    {
                        "count_deafened_voice",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS count_deafened_voice BOOLEAN DEFAULT true"
                    },
                    {
                        "count_solo_voice",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS count_solo_voice BOOLEAN DEFAULT true"
                    },
                    {
                        "created_by",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS created_by BIGINT DEFAULT 0"
                    },
                    {
                        "created_at",
                        "ALTER TABLE activity_role_rules ADD COLUMN IF NOT EXISTS created_at BIGINT DEFAULT 0"
                    },
                    {
                        // Superseded by announce_interval_days + last_announced_at (work for every window
                        // type, not just calendar ones). Explicit revert migration, not a silent removal -
                        // this already shipped, so dropping it has to be a real, deliberate ALTER TABLE step.
                        "last_period_key",
                        "ALTER TABLE activity_role_rules DROP COLUMN IF EXISTS last_period_key"
                    }
                }
            },
            {
                "infopanels", new Dictionary<string, string>
                {
                    {
                        // The banner is now a Components V2 media gallery item, which is always full
                        // width on its own - no more need to force width with a second, invisible image.
                        // Explicit revert migration, not a silent removal - this already shipped.
                        "spacer_url",
                        "ALTER TABLE infopanels DROP COLUMN IF EXISTS spacer_url"
                    }
                }
            },
            {
                "activity_role_tiers", new Dictionary<string, string>
                {
                    { "rule_id", "ALTER TABLE activity_role_tiers ADD COLUMN IF NOT EXISTS rule_id TEXT" },
                    {
                        "rank_from",
                        "ALTER TABLE activity_role_tiers ADD COLUMN IF NOT EXISTS rank_from INTEGER DEFAULT 1"
                    },
                    {
                        "rank_to",
                        "ALTER TABLE activity_role_tiers ADD COLUMN IF NOT EXISTS rank_to INTEGER DEFAULT 1"
                    },
                    {
                        "roleid",
                        "ALTER TABLE activity_role_tiers ADD COLUMN IF NOT EXISTS roleid BIGINT DEFAULT 0"
                    }
                }
            },
            {
                "member_lastseen", new Dictionary<string, string>
                {
                    {
                        "last_seen",
                        "ALTER TABLE member_lastseen ADD COLUMN IF NOT EXISTS last_seen BIGINT DEFAULT 0"
                    },
                    {
                        "signal",
                        "ALTER TABLE member_lastseen ADD COLUMN IF NOT EXISTS signal TEXT DEFAULT 'message'"
                    }
                }
            },
            {
                "banrequest_stages", new Dictionary<string, string>
                {
                    {
                        "position",
                        "ALTER TABLE banrequest_stages ADD COLUMN IF NOT EXISTS position INTEGER DEFAULT 0"
                    },
                    {
                        "enabled",
                        "ALTER TABLE banrequest_stages ADD COLUMN IF NOT EXISTS enabled BOOLEAN DEFAULT true"
                    },
                    {
                        "delay_minutes",
                        "ALTER TABLE banrequest_stages ADD COLUMN IF NOT EXISTS delay_minutes INTEGER DEFAULT 0"
                    },
                    { "target", "ALTER TABLE banrequest_stages ADD COLUMN IF NOT EXISTS target TEXT DEFAULT 'role'" },
                    {
                        "role_id",
                        "ALTER TABLE banrequest_stages ADD COLUMN IF NOT EXISTS role_id BIGINT DEFAULT 0"
                    }
                }
            },
            {
                "activity_role_grants", new Dictionary<string, string>
                {
                    { "rule_id", "ALTER TABLE activity_role_grants ADD COLUMN IF NOT EXISTS rule_id TEXT" },
                    { "userid", "ALTER TABLE activity_role_grants ADD COLUMN IF NOT EXISTS userid BIGINT" },
                    {
                        "roleid",
                        "ALTER TABLE activity_role_grants ADD COLUMN IF NOT EXISTS roleid BIGINT DEFAULT 0"
                    },
                    { "rank", "ALTER TABLE activity_role_grants ADD COLUMN IF NOT EXISTS rank INTEGER DEFAULT 0" },
                    {
                        "granted_at",
                        "ALTER TABLE activity_role_grants ADD COLUMN IF NOT EXISTS granted_at BIGINT DEFAULT 0"
                    }
                }
            },
            {
                "eligibility_conditions", new Dictionary<string, string>
                {
                    { "owner_type", "ALTER TABLE eligibility_conditions ADD COLUMN IF NOT EXISTS owner_type TEXT" },
                    { "owner_id", "ALTER TABLE eligibility_conditions ADD COLUMN IF NOT EXISTS owner_id TEXT" },
                    {
                        "group_id",
                        "ALTER TABLE eligibility_conditions ADD COLUMN IF NOT EXISTS group_id INTEGER DEFAULT 1"
                    },
                    {
                        "condition_type",
                        "ALTER TABLE eligibility_conditions ADD COLUMN IF NOT EXISTS condition_type TEXT DEFAULT 'role'"
                    },
                    {
                        "comparator",
                        "ALTER TABLE eligibility_conditions ADD COLUMN IF NOT EXISTS comparator TEXT DEFAULT 'gte'"
                    },
                    {
                        "value",
                        "ALTER TABLE eligibility_conditions ADD COLUMN IF NOT EXISTS value BIGINT DEFAULT 0"
                    },
                    {
                        "scope_ids",
                        "ALTER TABLE eligibility_conditions ADD COLUMN IF NOT EXISTS scope_ids BIGINT[] DEFAULT '{}'"
                    },
                    {
                        "negate",
                        "ALTER TABLE eligibility_conditions ADD COLUMN IF NOT EXISTS negate BOOLEAN DEFAULT false"
                    },
                    {
                        "created_at",
                        "ALTER TABLE eligibility_conditions ADD COLUMN IF NOT EXISTS created_at BIGINT DEFAULT 0"
                    }
                }
            },
            {
                "activity_announcement_groups", new Dictionary<string, string>
                {
                    { "name", "ALTER TABLE activity_announcement_groups ADD COLUMN IF NOT EXISTS name TEXT DEFAULT ''" },
                    {
                        "channel_id",
                        "ALTER TABLE activity_announcement_groups ADD COLUMN IF NOT EXISTS channel_id BIGINT DEFAULT 0"
                    },
                    {
                        "interval_days",
                        "ALTER TABLE activity_announcement_groups ADD COLUMN IF NOT EXISTS interval_days INTEGER DEFAULT 0"
                    },
                    {
                        "message",
                        "ALTER TABLE activity_announcement_groups ADD COLUMN IF NOT EXISTS message TEXT DEFAULT ''"
                    },
                    {
                        "last_announced_at",
                        "ALTER TABLE activity_announcement_groups ADD COLUMN IF NOT EXISTS last_announced_at BIGINT DEFAULT 0"
                    },
                    {
                        "created_by",
                        "ALTER TABLE activity_announcement_groups ADD COLUMN IF NOT EXISTS created_by BIGINT DEFAULT 0"
                    },
                    {
                        "created_at",
                        "ALTER TABLE activity_announcement_groups ADD COLUMN IF NOT EXISTS created_at BIGINT DEFAULT 0"
                    }
                }
            },
            {
                "activity_announcement_group_rules", new Dictionary<string, string>
                {
                    { "group_id", "ALTER TABLE activity_announcement_group_rules ADD COLUMN IF NOT EXISTS group_id TEXT" },
                    { "rule_id", "ALTER TABLE activity_announcement_group_rules ADD COLUMN IF NOT EXISTS rule_id TEXT" },
                    {
                        "alias",
                        "ALTER TABLE activity_announcement_group_rules ADD COLUMN IF NOT EXISTS alias TEXT DEFAULT ''"
                    }
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
                cmdAlter.CommandTimeout = SchemaCommandTimeoutSeconds;
                await cmdAlter.ExecuteNonQueryAsync();
                progressBar.Increment();
                await Task.Delay(10);
            }
        }

        await InitLeveling();
        await InitBotSettings();
        await InitBanRequestStages();
        await InitTicketCategories();
        CurrentApplication.Logger.Information("Database tables updated.");
    }

    private static async Task InitBotSettings()
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var defaults = new (string section, string key, string value)[]
        {
            ("Tickets", "PanelTitle", "AGC Support-System"),
            ("Tickets", "PanelDescription",
                "__Benötigst du Hilfe oder Support? Mach ein Ticket auf.__\n\n" +
                "> Wann sollte ich ein Ticket öffnen?\n" +
                "Wenn du irgendwelche Fragen hast oder irgendetwas unklar ist, du jemanden wegen Regelverstoß der Server Regeln oder der Discord Richtlinen melden möchtest!\n\n" +
                "> Wie öffne ich ein Ticket?\n" +
                "Wenn du ein Ticket öffnen willst, klicke unten auf \"Ticket öffnen\" und wähle danach eine der Kategorien aus, um was es geht. Danach wird ein Ticket mit dir erstellt und du kannst dein Anliegen schlidern."),
            ("Tickets", "PanelFooter", "Troll und absichtlicher Abuse ist zu unterlassen!"),
            ("Tickets", "PickerTitle", "Wähle eine Supportkategorie aus"),
            ("Tickets", "PickerDescription",
                "Wähle unten eine Supportkategorie aus. Dies hilft uns dein Ticket schneller zuzuordnen.\n" +
                "Nach Auswahl der Kategorie wird ein Ticket erstellt, bitte schlildere anschließend im Ticket dein Anliegen."),
            ("Tickets", "AutoCloseEnabled", "false"),
            ("Tickets", "GlobalAccessRoleIds", TicketTeamRoleIdOrZero()),
            // One open ticket per user is what the system did before categories existed. A per
            // category limit alone would silently turn that into one per category.
            ("Tickets", "MaxOpenPerUserTotal", "1"),
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
            ("TeamApplications", "NotifyNewApplicationText", "Neue Bewerbung eingegangen."),
            ("BanRequests", "PingLifetimeSeconds", "0"),
            ("BanRequests", "RequestTimeoutHours", "6"),
            ("BanRequests", "EstimateWindowMinutes", "15")
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

    /// <summary>
    ///     Seeds the escalation ladder once, so a fresh install pings sensibly before anyone touches the
    ///     dashboard. Role stages are skipped when their config key is missing.
    /// </summary>
    private static async Task InitBanRequestStages()
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

        await using (var probe = con.CreateCommand("SELECT COUNT(*) FROM banrequest_stages"))
        {
            if (await probe.ExecuteScalarAsync() is long existing && existing > 0) return;
        }

        var defaults = new List<(int position, int delay, string target, long roleId)>
        {
            (0, 0, "estimated", 0)
        };

        foreach (var (position, delay, configKey) in new[] { (1, 10, "ModRoleId"), (2, 30, "AdminRoleId") })
            try
            {
                var roleId = long.Parse(BotConfig.GetConfig()["ServerConfig"][configKey]);
                if (roleId > 0) defaults.Add((position, delay, "role", roleId));
            }
            catch (Exception)
            {
                // no role configured, that rung simply does not exist
            }

        foreach (var (position, delay, target, roleId) in defaults)
        {
            await using var cmd = con.CreateCommand(
                "INSERT INTO banrequest_stages (stage_id, position, enabled, delay_minutes, target, role_id) " +
                "VALUES (@stage_id, @position, true, @delay_minutes, @target, @role_id)");
            cmd.Parameters.AddWithValue("stage_id", ToolSet.GenerateCaseID());
            cmd.Parameters.AddWithValue("position", position);
            cmd.Parameters.AddWithValue("delay_minutes", delay);
            cmd.Parameters.AddWithValue("target", target);
            cmd.Parameters.AddWithValue("role_id", roleId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static string TicketTeamRoleIdOrZero()
    {
        var roleId = TicketConfigId("TeamRoleId");
        return roleId > 0 ? roleId.ToString() : "0";
    }

    private static long TicketConfigId(string key)
    {
        try
        {
            return long.TryParse(BotConfig.GetConfig()["TicketConfig"][key], out var id) ? id : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    ///     Brings the category table up to the shape the ticket system now expects. Idempotent: it only
    ///     fills in what is missing, so a database that predates categories keeps behaving exactly as
    ///     before until someone changes something in the dashboard.
    /// </summary>
    private static async Task InitTicketCategories()
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        var teamRoleId = TicketConfigId("TeamRoleId");
        var supportCategoryId = TicketConfigId("SupportCategoryId");
        long[] roles = teamRoleId > 0 ? [teamRoleId] : [];

        const string supportWelcome =
            "Hey! Danke fürs öffnen eines Support-Tickets. Ein Teammitglied wird sich gleich um dein Anliegen kümmern. Bitte teile uns in der Zeit alle nötigen Infos mit. ";
        const string reportWelcome =
            "Hey! Danke fürs öffnen eines Report-Tickets. Ein Teammitglied wird sich gleich um dein Anliegen kümmern. Bitte teile uns in der Zeit alle nötigen Infos mit.\n" +
            "1. Um wen geht es (User-ID oder User-Name)\n" +
            "2. Was ist vorgefallen (Bitte versuche die Situation so ausführlich wie möglich zu beschreiben)\n " +
            "3. Hast du eventuelle Beweise? ";

        var seeds = new (string CustomId, string Label, string Description, string Welcome, int SortOrder)[]
        {
            ("report", "Report / Melden",
                "Hier kannst du einen Benutzer melden der gegen Regeln verstößt oder anderweitig auffällt.",
                reportWelcome, 0),
            ("support", "Support", "Hier kannst du dich bei generellen Anliegen melden", supportWelcome, 1)
        };

        foreach (var seed in seeds)
        {
            await using var insert = con.CreateCommand(
                "INSERT INTO ticketcategories (custom_id, category_text, description, channel_prefix, welcome_text, " +
                "discord_category_id, handler_role_ids, ping_role_ids, max_open_per_user, sort_order, enabled) " +
                "SELECT @id, @label, @description, @id, @welcome, @category, @roles, @roles, 1, @sort, true " +
                "WHERE NOT EXISTS (SELECT 1 FROM ticketcategories WHERE custom_id = @id)");
            insert.Parameters.AddWithValue("id", seed.CustomId);
            insert.Parameters.AddWithValue("label", seed.Label);
            insert.Parameters.AddWithValue("description", seed.Description);
            insert.Parameters.AddWithValue("welcome", seed.Welcome);
            insert.Parameters.AddWithValue("category", supportCategoryId);
            insert.Parameters.AddWithValue("roles", roles);
            insert.Parameters.AddWithValue("sort", seed.SortOrder);
            await insert.ExecuteNonQueryAsync();

            await using var fill = con.CreateCommand(
                "UPDATE ticketcategories SET " +
                "description = COALESCE(NULLIF(description, ''), @description), " +
                "welcome_text = COALESCE(NULLIF(welcome_text, ''), @welcome) " +
                "WHERE custom_id = @id");
            fill.Parameters.AddWithValue("id", seed.CustomId);
            fill.Parameters.AddWithValue("description", seed.Description);
            fill.Parameters.AddWithValue("welcome", seed.Welcome);
            await fill.ExecuteNonQueryAsync();
        }

        // Rows that predate this release and are neither support nor report were dead buttons before:
        // the panel rendered them, the handler matched nothing. Filling them in would quietly turn them
        // into working categories, so they start disabled and an admin decides. channel_prefix is NULL
        // exactly for rows this code has never touched.
        await using (var quarantine = con.CreateCommand(
                         "UPDATE ticketcategories SET enabled = false " +
                         "WHERE channel_prefix IS NULL AND custom_id NOT IN ('support', 'report')"))
        {
            await quarantine.ExecuteNonQueryAsync();
        }

        await using (var backfill = con.CreateCommand(
                         "UPDATE ticketcategories SET " +
                         "channel_prefix = COALESCE(NULLIF(channel_prefix, ''), custom_id), " +
                         "discord_category_id = CASE WHEN COALESCE(discord_category_id, 0) = 0 THEN @category ELSE discord_category_id END, " +
                         "handler_role_ids = CASE WHEN COALESCE(cardinality(handler_role_ids), 0) = 0 THEN @roles ELSE handler_role_ids END, " +
                         "ping_role_ids = CASE WHEN COALESCE(cardinality(ping_role_ids), 0) = 0 THEN @roles ELSE ping_role_ids END, " +
                         "max_open_per_user = CASE WHEN COALESCE(max_open_per_user, 0) < 1 THEN 1 ELSE max_open_per_user END, " +
                         "enabled = COALESCE(enabled, true), " +
                         "sort_order = COALESCE(sort_order, 0)"))
        {
            backfill.Parameters.AddWithValue("category", supportCategoryId);
            backfill.Parameters.AddWithValue("roles", roles);
            await backfill.ExecuteNonQueryAsync();
        }

        // Ticket types that exist in the store but have no category row would otherwise show up
        // without a label on the dashboard. They are added disabled so nobody can pick them again.
        await using (var orphans = con.CreateCommand(
                         "INSERT INTO ticketcategories (custom_id, category_text, channel_prefix, discord_category_id, " +
                         "handler_role_ids, ping_role_ids, max_open_per_user, sort_order, enabled) " +
                         "SELECT DISTINCT s.tickettype, s.tickettype, s.tickettype, @category, @roles, @roles, 1, 99, false " +
                         "FROM ticketstore s WHERE s.tickettype IS NOT NULL AND s.tickettype <> '' " +
                         "AND NOT EXISTS (SELECT 1 FROM ticketcategories c WHERE c.custom_id = s.tickettype)"))
        {
            orphans.Parameters.AddWithValue("category", supportCategoryId);
            orphans.Parameters.AddWithValue("roles", roles);
            await orphans.ExecuteNonQueryAsync();
        }

        await InitReportIntakeQuestions(con);

        await EnsureUniqueIndexAsync(con, "ticketcategories", "custom_id", "idx_ticketcategories_custom_id");
        await EnsureUniqueIndexAsync(con, "snippets", "snip_id", "idx_snippets_snip_id");
    }

    /// <summary>
    ///     Adds a unique index, but only once the data allows it. A blind CREATE UNIQUE INDEX would throw on
    ///     a table that already holds duplicates and take the whole startup with it, so duplicates are
    ///     looked for first and named in the log instead.
    /// </summary>
    private static async Task EnsureUniqueIndexAsync(NpgsqlDataSource con, string table, string column,
        string indexName)
    {
        try
        {
            List<string> duplicates = [];
            await using (var probe = con.CreateCommand(
                             $"SELECT {column}, COUNT(*) FROM {table} GROUP BY {column} HAVING COUNT(*) > 1"))
            {
                probe.CommandTimeout = SchemaCommandTimeoutSeconds;
                await using var reader = await probe.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    duplicates.Add($"{(reader.IsDBNull(0) ? "(leer)" : reader.GetString(0))} x{reader.GetInt64(1)}");
            }

            if (duplicates.Count > 0)
            {
                CurrentApplication.Logger.Warning(
                    "{Table}.{Column} holds duplicates, so {Index} was not created. Clean these up and restart: {Duplicates}",
                    table, column, indexName, string.Join(", ", duplicates));
                return;
            }

            await using var index = con.CreateCommand(
                $"CREATE UNIQUE INDEX IF NOT EXISTS {indexName} ON {table} ({column})");
            index.CommandTimeout = SchemaCommandTimeoutSeconds;
            await index.ExecuteNonQueryAsync();
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Warning(e, "Could not ensure the unique index {Index} on {Table}", indexName,
                table);
        }
    }

    /// <summary>
    ///     The three questions the report ticket used to ask in plain text. They are stored so an admin
    ///     can switch the category to a real form, but intake stays off until someone enables it.
    /// </summary>
    private static async Task InitReportIntakeQuestions(NpgsqlDataSource con)
    {
        await using (var probe = con.CreateCommand(
                         "SELECT COUNT(*) FROM ticket_category_questions WHERE category_id = 'report'"))
        {
            if (await probe.ExecuteScalarAsync() is long existing && existing > 0) return;
        }

        var questions = new (string Label, string Placeholder, string Style, bool Required)[]
        {
            ("Um wen geht es?", "User-ID oder User-Name", "short", true),
            ("Was ist vorgefallen?", "Bitte beschreibe die Situation so ausführlich wie möglich.", "long", true),
            ("Hast du Beweise?", "Links zu Screenshots oder Nachrichten, sonst \"nein\".", "long", false)
        };

        for (var i = 0; i < questions.Length; i++)
        {
            var (label, placeholder, style, required) = questions[i];
            await using var cmd = con.CreateCommand(
                "INSERT INTO ticket_category_questions (id, category_id, position, label, placeholder, style, required, min_length, max_length) " +
                "VALUES (@id, 'report', @position, @label, @placeholder, @style, @required, 0, @max)");
            cmd.Parameters.AddWithValue("id", ToolSet.GenerateCaseID());
            cmd.Parameters.AddWithValue("position", i);
            cmd.Parameters.AddWithValue("label", label);
            cmd.Parameters.AddWithValue("placeholder", placeholder);
            cmd.Parameters.AddWithValue("style", style);
            cmd.Parameters.AddWithValue("required", required);
            cmd.Parameters.AddWithValue("max", style == "long" ? 1000 : 200);
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