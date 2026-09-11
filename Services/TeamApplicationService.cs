#region

using System.Text;
using System.Text.Json;
using AGC_Management.Entities.ApplicationSystem;
using AGC_Management.Enums.ApplicationSystem;
using AGC_Management.Utils;
using NpgsqlTypes;

#endregion

namespace AGC_Management.Services;

public static class TeamApplicationService
{
    public const string Section = "TeamApplications";

    /// <summary>Slugs that would shadow a fixed route under /apply.</summary>
    private static readonly string[] ReservedSlugs = ["status", "legacy"];

    private static readonly TimeSpan PositionCacheTtl = TimeSpan.FromSeconds(30);
    private static List<TeamApplicationPosition>? _positionCache;
    private static DateTime _positionCacheExpires = DateTime.MinValue;

    private static NpgsqlDataSource Db =>
        CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    #region Settings

    public static async Task<string> GetTextAsync(string key, string fallback = "")
    {
        var value = await RuntimeSettings.GetAsync(Section, key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    public static Task SetTextAsync(string key, string value)
    {
        return RuntimeSettings.SetAsync(Section, key, value);
    }

    #endregion

    #region Helpers

    private static T ParseEnum<T>(string? raw, T fallback) where T : struct, Enum
    {
        return Enum.TryParse<T>(raw, true, out var parsed) ? parsed : fallback;
    }

    private static string Store<T>(T value) where T : struct, Enum
    {
        return value.ToString().ToLowerInvariant();
    }

    private static List<string> ReadJsonArray(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(reader.GetString(ordinal)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string Slugify(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
            if (char.IsLetterOrDigit(c))
                builder.Append(c);
            else if (c == ' ' || c == '-' || c == '_')
                builder.Append('-');

        var slug = builder.ToString().Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug;
    }

    public static bool IsReservedSlug(string slug)
    {
        return ReservedSlugs.Contains(slug, StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    #region Positions

    public static async Task<List<TeamApplicationPosition>> GetPositionsAsync(bool activeOnly = false)
    {
        var cached = _positionCache;
        if (cached == null || _positionCacheExpires <= DateTime.UtcNow)
        {
            var positions = new List<TeamApplicationPosition>();
            await using var cmd = Db.CreateCommand(
                "SELECT position_id, position_name, description, min_level, notify_channel_id, active, " +
                "always_open, created_at FROM teamapplication_position ORDER BY position_name");
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                positions.Add(new TeamApplicationPosition
                {
                    PositionId = reader.GetString(0),
                    PositionName = reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1),
                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    MinLevel = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    NotifyChannelId = reader.IsDBNull(4) ? 0 : (ulong)reader.GetInt64(4),
                    Active = !reader.IsDBNull(5) && reader.GetBoolean(5),
                    AlwaysOpen = !reader.IsDBNull(6) && reader.GetBoolean(6),
                    CreatedAt = reader.IsDBNull(7) ? 0 : reader.GetInt64(7)
                });

            _positionCache = positions;
            _positionCacheExpires = DateTime.UtcNow + PositionCacheTtl;
            cached = positions;
        }

        var selected = activeOnly ? cached.Where(p => p.Active) : cached;
        return selected.Select(Copy).ToList();
    }

    /// <summary>The editor binds straight to what it gets back, so it must not be the cached instance.</summary>
    private static TeamApplicationPosition Copy(TeamApplicationPosition position)
    {
        return new TeamApplicationPosition
        {
            PositionId = position.PositionId,
            PositionName = position.PositionName,
            Description = position.Description,
            MinLevel = position.MinLevel,
            NotifyChannelId = position.NotifyChannelId,
            Active = position.Active,
            AlwaysOpen = position.AlwaysOpen,
            CreatedAt = position.CreatedAt
        };
    }

    public static async Task<TeamApplicationPosition?> GetPositionAsync(string positionId)
    {
        var positions = await GetPositionsAsync();
        return positions.FirstOrDefault(p => p.PositionId == positionId);
    }

    private static void InvalidatePositionCache()
    {
        _positionCache = null;
    }

    public static async Task<string?> CreatePositionAsync(string name, int minLevel, ulong notifyChannelId,
        string description)
    {
        var slug = Slugify(name);
        if (string.IsNullOrEmpty(slug) || IsReservedSlug(slug)) return null;
        if (await GetPositionAsync(slug) != null) return null;

        await using var cmd = Db.CreateCommand(
            "INSERT INTO teamapplication_position (position_id, position_name, description, min_level, notify_channel_id, active, created_at) " +
            "VALUES (@id, @name, @description, @minlevel, @channel, false, @createdat)");
        cmd.Parameters.AddWithValue("id", slug);
        cmd.Parameters.AddWithValue("name", name.Trim());
        cmd.Parameters.AddWithValue("description", description);
        cmd.Parameters.AddWithValue("minlevel", minLevel);
        cmd.Parameters.AddWithValue("channel", (long)notifyChannelId);
        cmd.Parameters.AddWithValue("createdat", Now);
        await cmd.ExecuteNonQueryAsync();

        InvalidatePositionCache();
        return slug;
    }

    public static async Task<int> CountApplicationsForPositionAsync(string positionId)
    {
        await using var cmd = Db.CreateCommand(
            "SELECT COUNT(*) FROM teamapplication_application WHERE position_id = @id");
        cmd.Parameters.AddWithValue("id", positionId);
        var result = await cmd.ExecuteScalarAsync();
        return result is long count ? (int)count : 0;
    }

    /// <summary>
    ///     Removes a position and everything hanging off it, including submitted applications. Runs in one
    ///     transaction so a failure halfway through cannot leave rows pointing at a position that is gone.
    ///     Templates and permission rules scoped to every position (position_id IS NULL) are left alone.
    /// </summary>
    public static async Task DeletePositionAsync(string positionId)
    {
        string[] statements =
        [
            "DELETE FROM teamapplication_answers WHERE application_id IN " +
            "(SELECT application_id FROM teamapplication_application WHERE position_id = @id)",
            "DELETE FROM teamapplication_notes WHERE application_id IN " +
            "(SELECT application_id FROM teamapplication_application WHERE position_id = @id)",
            "DELETE FROM teamapplication_seen WHERE application_id IN " +
            "(SELECT application_id FROM teamapplication_application WHERE position_id = @id)",
            "DELETE FROM teamapplication_application WHERE position_id = @id",
            "DELETE FROM teamapplication_reapply_grant WHERE position_id = @id",
            "DELETE FROM teamapplication_phase WHERE position_id = @id",
            "DELETE FROM teamapplication_questions WHERE position_id = @id",
            "DELETE FROM teamapplication_questionset WHERE position_id = @id",
            "DELETE FROM teamapplication_templates WHERE position_id = @id",
            "DELETE FROM teamapplication_permissions WHERE position_id = @id",
            "DELETE FROM teamapplication_position WHERE position_id = @id"
        ];

        await using (var connection = await Db.OpenConnectionAsync())
        {
            await using var transaction = await connection.BeginTransactionAsync();

            foreach (var sql in statements)
            {
                await using var cmd = new NpgsqlCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("id", positionId);
                await cmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }

        InvalidatePositionCache();
        TeamApplicationPermissionService.InvalidateCache();
    }

    public static async Task UpdatePositionAsync(TeamApplicationPosition position)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE teamapplication_position SET position_name = @name, description = @description, " +
            "min_level = @minlevel, notify_channel_id = @channel, active = @active, always_open = @alwaysopen " +
            "WHERE position_id = @id");
        cmd.Parameters.AddWithValue("id", position.PositionId);
        cmd.Parameters.AddWithValue("name", position.PositionName);
        cmd.Parameters.AddWithValue("description", position.Description);
        cmd.Parameters.AddWithValue("minlevel", position.MinLevel);
        cmd.Parameters.AddWithValue("channel", (long)position.NotifyChannelId);
        cmd.Parameters.AddWithValue("active", position.Active);
        cmd.Parameters.AddWithValue("alwaysopen", position.AlwaysOpen);
        await cmd.ExecuteNonQueryAsync();

        InvalidatePositionCache();
    }

    #endregion

    #region Question sets

    public static async Task<List<TeamApplicationQuestionSet>> GetQuestionSetsAsync(string positionId)
    {
        var sets = new List<TeamApplicationQuestionSet>();
        await using var cmd = Db.CreateCommand(
            "SELECT position_id, version, state, created_by, created_at FROM teamapplication_questionset " +
            "WHERE position_id = @id ORDER BY version DESC");
        cmd.Parameters.AddWithValue("id", positionId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            sets.Add(new TeamApplicationQuestionSet
            {
                PositionId = reader.GetString(0),
                Version = reader.GetInt32(1),
                State = ParseEnum(reader.IsDBNull(2) ? null : reader.GetString(2),
                    TeamApplicationQuestionSetState.Draft),
                CreatedBy = reader.IsDBNull(3) ? 0 : (ulong)reader.GetInt64(3),
                CreatedAt = reader.IsDBNull(4) ? 0 : reader.GetInt64(4)
            });

        return sets;
    }

    public static async Task<TeamApplicationQuestionSet?> GetDraftAsync(string positionId)
    {
        var sets = await GetQuestionSetsAsync(positionId);
        return sets.FirstOrDefault(s => s.State == TeamApplicationQuestionSetState.Draft);
    }

    public static async Task<TeamApplicationQuestionSet?> GetPublishedAsync(string positionId)
    {
        var sets = await GetQuestionSetsAsync(positionId);
        return sets.FirstOrDefault(s => s.State == TeamApplicationQuestionSetState.Published);
    }

    /// <summary>
    ///     Opens a new draft, seeded with a copy of the published catalogue so an edit never touches a version
    ///     a phase may already have frozen. Returns the existing draft when one is already open.
    /// </summary>
    public static async Task<int> CreateDraftAsync(string positionId, ulong actorId)
    {
        var existing = await GetDraftAsync(positionId);
        if (existing != null) return existing.Version;

        var sets = await GetQuestionSetsAsync(positionId);
        var version = sets.Count == 0 ? 1 : sets.Max(s => s.Version) + 1;

        await using (var cmd = Db.CreateCommand(
                         "INSERT INTO teamapplication_questionset (position_id, version, state, created_by, created_at) " +
                         "VALUES (@id, @version, 'draft', @actor, @createdat)"))
        {
            cmd.Parameters.AddWithValue("id", positionId);
            cmd.Parameters.AddWithValue("version", version);
            cmd.Parameters.AddWithValue("actor", (long)actorId);
            cmd.Parameters.AddWithValue("createdat", Now);
            await cmd.ExecuteNonQueryAsync();
        }

        var published = await GetPublishedAsync(positionId);
        if (published != null)
            foreach (var question in await GetQuestionsAsync(positionId, published.Version))
            {
                question.QuestionId = ToolSet.GenerateCaseID();
                question.Version = version;
                await UpsertQuestionAsync(question);
            }

        return version;
    }

    public static async Task PublishDraftAsync(string positionId)
    {
        var draft = await GetDraftAsync(positionId);
        if (draft == null) return;

        await using (var archive = Db.CreateCommand(
                         "UPDATE teamapplication_questionset SET state = 'archived' " +
                         "WHERE position_id = @id AND state = 'published'"))
        {
            archive.Parameters.AddWithValue("id", positionId);
            await archive.ExecuteNonQueryAsync();
        }

        await using var publish = Db.CreateCommand(
            "UPDATE teamapplication_questionset SET state = 'published' WHERE position_id = @id AND version = @version");
        publish.Parameters.AddWithValue("id", positionId);
        publish.Parameters.AddWithValue("version", draft.Version);
        await publish.ExecuteNonQueryAsync();
    }

    #endregion

    #region Questions

    public static async Task<List<TeamApplicationQuestion>> GetQuestionsAsync(string positionId, int version)
    {
        var questions = new List<TeamApplicationQuestion>();
        await using var cmd = Db.CreateCommand(
            "SELECT question_id, position_id, version, sort_order, type, text, description, required, " +
            "min_length, max_length, min_value, max_value, options, min_selections, max_selections, " +
            "condition_question_id, condition_value, number_display, number_step " +
            "FROM teamapplication_questions WHERE position_id = @id AND version = @version ORDER BY sort_order");
        cmd.Parameters.AddWithValue("id", positionId);
        cmd.Parameters.AddWithValue("version", version);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            questions.Add(new TeamApplicationQuestion
            {
                QuestionId = reader.GetString(0),
                PositionId = reader.GetString(1),
                Version = reader.GetInt32(2),
                SortOrder = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                Type = ParseEnum(reader.IsDBNull(4) ? null : reader.GetString(4),
                    TeamApplicationQuestionType.ShortText),
                Text = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Description = reader.IsDBNull(6) ? "" : reader.GetString(6),
                Required = !reader.IsDBNull(7) && reader.GetBoolean(7),
                MinLength = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                MaxLength = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                MinValue = reader.IsDBNull(10) ? 0 : reader.GetInt64(10),
                MaxValue = reader.IsDBNull(11) ? 0 : reader.GetInt64(11),
                Options = ReadJsonArray(reader, 12),
                MinSelections = reader.IsDBNull(13) ? 0 : reader.GetInt32(13),
                MaxSelections = reader.IsDBNull(14) ? 0 : reader.GetInt32(14),
                ConditionQuestionId = reader.IsDBNull(15) ? "" : reader.GetString(15),
                ConditionValue = reader.IsDBNull(16) ? "" : reader.GetString(16),
                NumberDisplay = ParseEnum(reader.IsDBNull(17) ? null : reader.GetString(17), NumberDisplay.Text),
                NumberStep = reader.IsDBNull(18) ? 1 : reader.GetInt32(18)
            });

        return questions;
    }

    public static async Task UpsertQuestionAsync(TeamApplicationQuestion question)
    {
        if (string.IsNullOrEmpty(question.QuestionId)) question.QuestionId = ToolSet.GenerateCaseID();

        await using var cmd = Db.CreateCommand(
            "INSERT INTO teamapplication_questions (question_id, position_id, version, sort_order, type, text, " +
            "description, required, min_length, max_length, min_value, max_value, options, min_selections, max_selections, " +
            "condition_question_id, condition_value, number_display, number_step) " +
            "VALUES (@qid, @pid, @version, @sort, @type, @text, @description, @required, @minlen, @maxlen, " +
            "@minval, @maxval, @options, @minsel, @maxsel, @condqid, @condval, @numdisplay, @numstep) " +
            "ON CONFLICT (question_id) DO UPDATE SET sort_order = EXCLUDED.sort_order, type = EXCLUDED.type, " +
            "text = EXCLUDED.text, description = EXCLUDED.description, required = EXCLUDED.required, " +
            "min_length = EXCLUDED.min_length, max_length = EXCLUDED.max_length, min_value = EXCLUDED.min_value, " +
            "max_value = EXCLUDED.max_value, options = EXCLUDED.options, min_selections = EXCLUDED.min_selections, " +
            "max_selections = EXCLUDED.max_selections, condition_question_id = EXCLUDED.condition_question_id, " +
            "condition_value = EXCLUDED.condition_value, number_display = EXCLUDED.number_display, " +
            "number_step = EXCLUDED.number_step");
        cmd.Parameters.AddWithValue("qid", question.QuestionId);
        cmd.Parameters.AddWithValue("pid", question.PositionId);
        cmd.Parameters.AddWithValue("version", question.Version);
        cmd.Parameters.AddWithValue("sort", question.SortOrder);
        cmd.Parameters.AddWithValue("type", Store(question.Type));
        cmd.Parameters.AddWithValue("text", question.Text);
        cmd.Parameters.AddWithValue("description", question.Description);
        cmd.Parameters.AddWithValue("required", question.Required);
        cmd.Parameters.AddWithValue("minlen", question.MinLength);
        cmd.Parameters.AddWithValue("maxlen", question.MaxLength);
        cmd.Parameters.AddWithValue("minval", question.MinValue);
        cmd.Parameters.AddWithValue("maxval", question.MaxValue);
        cmd.Parameters.AddWithValue("options", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(question.Options));
        cmd.Parameters.AddWithValue("minsel", question.MinSelections);
        cmd.Parameters.AddWithValue("maxsel", question.MaxSelections);
        cmd.Parameters.AddWithValue("condqid", question.ConditionQuestionId);
        cmd.Parameters.AddWithValue("condval", question.ConditionValue);
        cmd.Parameters.AddWithValue("numdisplay", Store(question.NumberDisplay));
        cmd.Parameters.AddWithValue("numstep", question.NumberStep);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task DeleteQuestionAsync(string questionId)
    {
        await using var cmd = Db.CreateCommand("DELETE FROM teamapplication_questions WHERE question_id = @qid");
        cmd.Parameters.AddWithValue("qid", questionId);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Phases

    private static TeamApplicationPhase ReadPhase(NpgsqlDataReader reader)
    {
        return new TeamApplicationPhase
        {
            PhaseId = reader.GetString(0),
            PositionId = reader.GetString(1),
            Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
            State = ParseEnum(reader.IsDBNull(3) ? null : reader.GetString(3), TeamApplicationPhaseState.Draft),
            QuestionSetVersion = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            OpensAt = reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
            ClosesAt = reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
            CreatedBy = reader.IsDBNull(7) ? 0 : (ulong)reader.GetInt64(7),
            CreatedAt = reader.IsDBNull(8) ? 0 : reader.GetInt64(8)
        };
    }

    private const string PhaseColumns =
        "phase_id, position_id, name, state, questionset_version, opens_at, closes_at, created_by, created_at";

    public static async Task<List<TeamApplicationPhase>> GetPhasesAsync(string? positionId = null)
    {
        var phases = new List<TeamApplicationPhase>();
        var sql = $"SELECT {PhaseColumns} FROM teamapplication_phase";
        if (positionId != null) sql += " WHERE position_id = @id";
        sql += " ORDER BY created_at DESC";

        await using var cmd = Db.CreateCommand(sql);
        if (positionId != null) cmd.Parameters.AddWithValue("id", positionId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) phases.Add(ReadPhase(reader));

        return phases;
    }

    public static async Task<TeamApplicationPhase?> GetPhaseAsync(string phaseId)
    {
        await using var cmd = Db.CreateCommand($"SELECT {PhaseColumns} FROM teamapplication_phase WHERE phase_id = @id");
        cmd.Parameters.AddWithValue("id", phaseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadPhase(reader) : null;
    }

    public static async Task<TeamApplicationPhase?> GetOpenPhaseAsync(string positionId)
    {
        await using var cmd = Db.CreateCommand(
            $"SELECT {PhaseColumns} FROM teamapplication_phase WHERE position_id = @id AND state = 'open' " +
            "ORDER BY created_at DESC LIMIT 1");
        cmd.Parameters.AddWithValue("id", positionId);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadPhase(reader) : null;
    }

    /// <summary>The draft with the nearest future start, used for the "opens at" hint on the panel.</summary>
    public static async Task<TeamApplicationPhase?> GetNextScheduledPhaseAsync(string positionId)
    {
        await using var cmd = Db.CreateCommand(
            $"SELECT {PhaseColumns} FROM teamapplication_phase WHERE position_id = @id AND state = 'draft' " +
            "AND opens_at > @now ORDER BY opens_at LIMIT 1");
        cmd.Parameters.AddWithValue("id", positionId);
        cmd.Parameters.AddWithValue("now", Now);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadPhase(reader) : null;
    }

    public static async Task<string> CreatePhaseAsync(string positionId, string name, long opensAt, long closesAt,
        ulong actorId)
    {
        var phaseId = ToolSet.GenerateCaseID();
        await using var cmd = Db.CreateCommand(
            "INSERT INTO teamapplication_phase (phase_id, position_id, name, state, questionset_version, opens_at, " +
            "closes_at, created_by, created_at) VALUES (@pid, @posid, @name, 'draft', 0, @opens, @closes, @actor, @createdat)");
        cmd.Parameters.AddWithValue("pid", phaseId);
        cmd.Parameters.AddWithValue("posid", positionId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("opens", opensAt);
        cmd.Parameters.AddWithValue("closes", closesAt);
        cmd.Parameters.AddWithValue("actor", (long)actorId);
        cmd.Parameters.AddWithValue("createdat", Now);
        await cmd.ExecuteNonQueryAsync();
        return phaseId;
    }

    public static async Task UpdatePhaseScheduleAsync(string phaseId, string name, long opensAt, long closesAt)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE teamapplication_phase SET name = @name, opens_at = @opens, closes_at = @closes WHERE phase_id = @pid");
        cmd.Parameters.AddWithValue("pid", phaseId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("opens", opensAt);
        cmd.Parameters.AddWithValue("closes", closesAt);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    ///     Opens a phase, first time or again after a close. A phase that never ran freezes the currently
    ///     published catalogue version; a reopened one keeps the version it froze back then, because
    ///     applications already submitted answer against exactly that one.
    /// </summary>
    public static async Task<TeamApplicationPhaseOpenResult> OpenPhaseAsync(string phaseId)
    {
        var phase = await GetPhaseAsync(phaseId);
        if (phase == null) return TeamApplicationPhaseOpenResult.NotFound;
        if (phase.State == TeamApplicationPhaseState.Open) return TeamApplicationPhaseOpenResult.AlreadyOpen;
        if (phase.State == TeamApplicationPhaseState.Archived) return TeamApplicationPhaseOpenResult.Archived;

        // Two open phases would make GetOpenPhaseAsync pick one of them arbitrarily.
        var other = await GetOpenPhaseAsync(phase.PositionId);
        if (other != null && other.PhaseId != phaseId) return TeamApplicationPhaseOpenResult.OtherPhaseOpen;

        var version = phase.QuestionSetVersion;
        if (version <= 0)
        {
            var published = await GetPublishedAsync(phase.PositionId);
            if (published == null) return TeamApplicationPhaseOpenResult.NoPublishedCatalogue;
            version = published.Version;
        }

        // An end date that already passed would have the phase loop close this again within a minute,
        // so reopening drops it and the phase stays open until someone closes it by hand.
        var closesAt = phase.ClosesAt > 0 && phase.ClosesAt <= Now ? 0 : phase.ClosesAt;

        await using var cmd = Db.CreateCommand(
            "UPDATE teamapplication_phase SET state = 'open', questionset_version = @version, " +
            "closes_at = @closes WHERE phase_id = @pid");
        cmd.Parameters.AddWithValue("pid", phaseId);
        cmd.Parameters.AddWithValue("version", version);
        cmd.Parameters.AddWithValue("closes", closesAt);
        await cmd.ExecuteNonQueryAsync();

        return TeamApplicationPhaseOpenResult.Opened;
    }

    public static async Task ClosePhaseAsync(string phaseId)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE teamapplication_phase SET state = 'closed' WHERE phase_id = @pid");
        cmd.Parameters.AddWithValue("pid", phaseId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task ArchivePhaseAsync(string phaseId)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE teamapplication_phase SET state = 'archived' WHERE phase_id = @pid");
        cmd.Parameters.AddWithValue("pid", phaseId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<int> CountUndecidedAsync(string phaseId)
    {
        await using var cmd = Db.CreateCommand(
            "SELECT COUNT(*) FROM teamapplication_application WHERE phase_id = @pid " +
            "AND status NOT IN ('angenommen', 'abgelehnt', 'zurueckgezogen')");
        cmd.Parameters.AddWithValue("pid", phaseId);
        var result = await cmd.ExecuteScalarAsync();
        return result is long count ? (int)count : 0;
    }

    /// <summary>
    ///     Whether one can submit for this position right now, and against which phase and frozen
    ///     catalogue version. The panel, the listener, the overview and the form all ask this, so none of
    ///     them can drift apart on what "open" means.
    /// </summary>
    public static async Task<TeamApplicationOpening> ResolveOpeningAsync(TeamApplicationPosition position)
    {
        var openPhase = await GetOpenPhaseAsync(position.PositionId);
        if (openPhase != null)
            return new TeamApplicationOpening
            {
                CanApply = true,
                Phase = openPhase,
                QuestionSetVersion = openPhase.QuestionSetVersion,
                Bypassed = position.AlwaysOpen
            };

        var next = await GetNextScheduledPhaseAsync(position.PositionId);
        var nextOpensAt = next?.OpensAt ?? 0;

        if (!position.AlwaysOpen) return new TeamApplicationOpening { NextOpensAt = nextOpensAt };

        // Still needs a phase to file under and a catalogue to answer, but neither has to be live.
        var fallback = (await GetPhasesAsync(position.PositionId))
            .FirstOrDefault(p => p.State != TeamApplicationPhaseState.Archived);
        var published = await GetPublishedAsync(position.PositionId);
        if (fallback == null || published == null)
            return new TeamApplicationOpening { NextOpensAt = nextOpensAt };

        return new TeamApplicationOpening
        {
            CanApply = true,
            Phase = fallback,
            QuestionSetVersion = published.Version,
            Bypassed = true
        };
    }

    /// <summary>The gates the form applies before it shows questions: open phase, level, free slot.</summary>
    public static async Task<bool> CanUserApplyNowAsync(ulong userId, TeamApplicationPosition position)
    {
        var opening = await ResolveOpeningAsync(position);
        if (!opening.CanApply || opening.Phase is null) return false;
        if (!opening.Bypassed && await LevelUtils.GetLevel(userId) < position.MinLevel) return false;

        var (allowed, _, _) = await CanApplyAsync(userId, position.PositionId, opening.Phase.PhaseId,
            opening.Bypassed);
        return allowed;
    }

    #endregion

    #region Applications

    private const string ApplicationColumns =
        "application_id, user_id, position_id, phase_id, questionset_version, attempt, status, submitted_at, " +
        "decided_at, decided_by, decision_text, dm_delivered, dm_error, withdrawn_at, level_snapshot, " +
        "xp_snapshot, joined_at_snapshot, account_created_snapshot";

    private static TeamApplication ReadApplication(NpgsqlDataReader reader)
    {
        return new TeamApplication
        {
            ApplicationId = reader.GetString(0),
            UserId = (ulong)reader.GetInt64(1),
            PositionId = reader.IsDBNull(2) ? "" : reader.GetString(2),
            PhaseId = reader.IsDBNull(3) ? "" : reader.GetString(3),
            QuestionSetVersion = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            Attempt = reader.IsDBNull(5) ? 1 : reader.GetInt32(5),
            Status = ParseEnum(reader.IsDBNull(6) ? null : reader.GetString(6), TeamApplicationStatus.Eingereicht),
            SubmittedAt = reader.IsDBNull(7) ? 0 : reader.GetInt64(7),
            DecidedAt = reader.IsDBNull(8) ? 0 : reader.GetInt64(8),
            DecidedBy = reader.IsDBNull(9) ? 0 : (ulong)reader.GetInt64(9),
            DecisionText = reader.IsDBNull(10) ? "" : reader.GetString(10),
            DmDelivered = reader.IsDBNull(11) ? null : reader.GetBoolean(11),
            DmError = reader.IsDBNull(12) ? "" : reader.GetString(12),
            WithdrawnAt = reader.IsDBNull(13) ? 0 : reader.GetInt64(13),
            LevelSnapshot = reader.IsDBNull(14) ? 0 : reader.GetInt32(14),
            XpSnapshot = reader.IsDBNull(15) ? 0 : reader.GetInt32(15),
            JoinedAtSnapshot = reader.IsDBNull(16) ? 0 : reader.GetInt64(16),
            AccountCreatedSnapshot = reader.IsDBNull(17) ? 0 : reader.GetInt64(17)
        };
    }

    private static async Task DecorateAsync(IEnumerable<TeamApplication> applications)
    {
        var list = applications as IList<TeamApplication> ?? applications.ToList();
        var positions = (await GetPositionsAsync()).ToDictionary(p => p.PositionId, p => p.PositionName);
        var phases = (await GetPhasesAsync()).ToDictionary(p => p.PhaseId, p => p.Name);

        foreach (var application in list)
        {
            application.PositionName = positions.TryGetValue(application.PositionId, out var name)
                ? name
                : application.PositionId;
            application.PhaseName = phases.TryGetValue(application.PhaseId, out var phase)
                ? phase
                : application.PhaseId;
        }

        if (list.Count == 0) return;

        var byId = list.ToDictionary(a => a.ApplicationId);
        await using var cmd = Db.CreateCommand(
            "SELECT application_id, user_id, seen_at FROM teamapplication_seen " +
            "WHERE application_id = ANY(@ids) ORDER BY seen_at, user_id");
        cmd.Parameters.AddWithValue("ids", byId.Keys.ToArray());
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            if (byId.TryGetValue(reader.GetString(0), out var application))
                application.Readers.Add(new TeamApplicationReader
                {
                    UserId = (ulong)reader.GetInt64(1),
                    SeenAt = reader.IsDBNull(2) ? 0 : reader.GetInt64(2)
                });
    }

    public static async Task<TeamApplication?> GetApplicationAsync(string applicationId)
    {
        await using var cmd = Db.CreateCommand(
            $"SELECT {ApplicationColumns} FROM teamapplication_application WHERE application_id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var application = ReadApplication(reader);
        await DecorateAsync([application]);
        return application;
    }

    public static async Task<List<TeamApplication>> GetApplicationsAsync(IReadOnlyCollection<string> positionIds,
        string? phaseId = null, TeamApplicationStatus? status = null)
    {
        if (positionIds.Count == 0) return [];

        var sql = new StringBuilder(
            $"SELECT {ApplicationColumns} FROM teamapplication_application WHERE position_id = ANY(@positions)");
        if (phaseId != null) sql.Append(" AND phase_id = @phase");
        if (status != null) sql.Append(" AND status = @status");
        sql.Append(" ORDER BY submitted_at DESC");

        await using var cmd = Db.CreateCommand(sql.ToString());
        cmd.Parameters.AddWithValue("positions", positionIds.ToArray());
        if (phaseId != null) cmd.Parameters.AddWithValue("phase", phaseId);
        if (status != null) cmd.Parameters.AddWithValue("status", Store(status.Value));

        var applications = new List<TeamApplication>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) applications.Add(ReadApplication(reader));
        }

        await DecorateAsync(applications);
        return applications;
    }

    public static async Task<List<TeamApplication>> GetOwnApplicationsAsync(ulong userId)
    {
        await using var cmd = Db.CreateCommand(
            $"SELECT {ApplicationColumns} FROM teamapplication_application WHERE user_id = @uid ORDER BY submitted_at DESC");
        cmd.Parameters.AddWithValue("uid", (long)userId);

        var applications = new List<TeamApplication>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) applications.Add(ReadApplication(reader));
        }

        await DecorateAsync(applications);
        return applications;
    }

    /// <summary>
    ///     One application per user, position and phase. A withdrawn one frees the slot again; an active one
    ///     only gives way to an unused reapply grant, which then raises the attempt counter.
    /// </summary>
    public static async Task<(bool allowed, TeamApplicationReapplyGrant? grant, int attempt)> CanApplyAsync(
        ulong userId, string positionId, string phaseId, bool bypassSlot = false)
    {
        var hasActive = false;
        var maxAttempt = 0;

        await using (var cmd = Db.CreateCommand(
                         "SELECT attempt, status FROM teamapplication_application " +
                         "WHERE user_id = @uid AND position_id = @pid AND phase_id = @phase"))
        {
            cmd.Parameters.AddWithValue("uid", (long)userId);
            cmd.Parameters.AddWithValue("pid", positionId);
            cmd.Parameters.AddWithValue("phase", phaseId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var attempt = reader.IsDBNull(0) ? 1 : reader.GetInt32(0);
                if (attempt > maxAttempt) maxAttempt = attempt;
                var status = ParseEnum(reader.IsDBNull(1) ? null : reader.GetString(1),
                    TeamApplicationStatus.Eingereicht);
                if (status != TeamApplicationStatus.Zurueckgezogen) hasActive = true;
            }
        }

        if (!hasActive) return (true, null, maxAttempt == 0 ? 1 : maxAttempt);

        // Same trick the grant path uses: a fresh attempt number keeps the slot index satisfied.
        if (bypassSlot) return (true, null, maxAttempt + 1);

        var grant = await GetUnusedGrantAsync(userId, positionId, phaseId);
        return grant == null ? (false, null, 0) : (true, grant, maxAttempt + 1);
    }

    public static async Task<string> SubmitAsync(TeamApplication application,
        IReadOnlyList<TeamApplicationAnswer> answers)
    {
        if (string.IsNullOrEmpty(application.ApplicationId))
            application.ApplicationId = ToolSet.GenerateCaseID();
        application.SubmittedAt = Now;

        await using (var cmd = Db.CreateCommand(
                         "INSERT INTO teamapplication_application (application_id, user_id, position_id, phase_id, " +
                         "questionset_version, attempt, status, submitted_at, level_snapshot, xp_snapshot, " +
                         "joined_at_snapshot, account_created_snapshot) VALUES (@id, @uid, @pid, @phase, @version, " +
                         "@attempt, 'eingereicht', @submitted, @level, @xp, @joined, @created)"))
        {
            cmd.Parameters.AddWithValue("id", application.ApplicationId);
            cmd.Parameters.AddWithValue("uid", (long)application.UserId);
            cmd.Parameters.AddWithValue("pid", application.PositionId);
            cmd.Parameters.AddWithValue("phase", application.PhaseId);
            cmd.Parameters.AddWithValue("version", application.QuestionSetVersion);
            cmd.Parameters.AddWithValue("attempt", application.Attempt);
            cmd.Parameters.AddWithValue("submitted", application.SubmittedAt);
            cmd.Parameters.AddWithValue("level", application.LevelSnapshot);
            cmd.Parameters.AddWithValue("xp", application.XpSnapshot);
            cmd.Parameters.AddWithValue("joined", application.JoinedAtSnapshot);
            cmd.Parameters.AddWithValue("created", application.AccountCreatedSnapshot);
            await cmd.ExecuteNonQueryAsync();
        }

        foreach (var answer in answers)
        {
            await using var cmd = Db.CreateCommand(
                "INSERT INTO teamapplication_answers (application_id, question_id, sort_order, " +
                "question_text_snapshot, question_type, answer, answer_options) " +
                "VALUES (@aid, @qid, @sort, @snapshot, @type, @answer, @options)");
            cmd.Parameters.AddWithValue("aid", application.ApplicationId);
            cmd.Parameters.AddWithValue("qid", answer.QuestionId);
            cmd.Parameters.AddWithValue("sort", answer.SortOrder);
            cmd.Parameters.AddWithValue("snapshot", answer.QuestionTextSnapshot);
            cmd.Parameters.AddWithValue("type", Store(answer.QuestionType));
            cmd.Parameters.AddWithValue("answer", answer.Answer);
            cmd.Parameters.AddWithValue("options", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(answer.AnswerOptions));
            await cmd.ExecuteNonQueryAsync();
        }

        return application.ApplicationId;
    }

    public static async Task<List<TeamApplicationAnswer>> GetAnswersAsync(string applicationId)
    {
        var answers = new List<TeamApplicationAnswer>();
        await using var cmd = Db.CreateCommand(
            "SELECT application_id, question_id, sort_order, question_text_snapshot, question_type, answer, " +
            "answer_options FROM teamapplication_answers WHERE application_id = @aid ORDER BY sort_order");
        cmd.Parameters.AddWithValue("aid", applicationId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            answers.Add(new TeamApplicationAnswer
            {
                ApplicationId = reader.GetString(0),
                QuestionId = reader.GetString(1),
                SortOrder = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                QuestionTextSnapshot = reader.IsDBNull(3) ? "" : reader.GetString(3),
                QuestionType = ParseEnum(reader.IsDBNull(4) ? null : reader.GetString(4),
                    TeamApplicationQuestionType.ShortText),
                Answer = reader.IsDBNull(5) ? "" : reader.GetString(5),
                AnswerOptions = ReadJsonArray(reader, 6)
            });

        return answers;
    }

    public static async Task SetStatusAsync(string applicationId, TeamApplicationStatus status)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE teamapplication_application SET status = @status WHERE application_id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        cmd.Parameters.AddWithValue("status", Store(status));
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task DecideAsync(string applicationId, TeamApplicationStatus status, ulong deciderId,
        string decisionText)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE teamapplication_application SET status = @status, decided_at = @decidedat, " +
            "decided_by = @decider, decision_text = @text WHERE application_id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        cmd.Parameters.AddWithValue("status", Store(status));
        cmd.Parameters.AddWithValue("decidedat", Now);
        cmd.Parameters.AddWithValue("decider", (long)deciderId);
        cmd.Parameters.AddWithValue("text", decisionText);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task SetDmResultAsync(string applicationId, bool delivered, string error)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE teamapplication_application SET dm_delivered = @delivered, dm_error = @error WHERE application_id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        cmd.Parameters.AddWithValue("delivered", delivered);
        cmd.Parameters.AddWithValue("error", error);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>A withdrawn application is never deleted, it only drops out of the slot index.</summary>
    public static async Task WithdrawAsync(string applicationId, ulong userId)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE teamapplication_application SET status = 'zurueckgezogen', withdrawn_at = @withdrawnat " +
            "WHERE application_id = @id AND user_id = @uid AND status NOT IN ('angenommen', 'abgelehnt', 'zurueckgezogen')");
        cmd.Parameters.AddWithValue("id", applicationId);
        cmd.Parameters.AddWithValue("uid", (long)userId);
        cmd.Parameters.AddWithValue("withdrawnat", Now);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    ///     Opening an application is reading it: the reader is recorded with the time of their first view,
    ///     and a fresh submission moves to Gelesen. Any later marking is left alone.
    /// </summary>
    public static async Task MarkSeenAsync(string applicationId, ulong userId)
    {
        await using var cmd = Db.CreateCommand(
            "INSERT INTO teamapplication_seen (application_id, user_id, seen_at) VALUES (@id, @uid, @now) " +
            "ON CONFLICT (application_id, user_id) DO NOTHING; " +
            "UPDATE teamapplication_application SET status = CASE WHEN status = @submitted THEN @read ELSE status END " +
            "WHERE application_id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        cmd.Parameters.AddWithValue("uid", (long)userId);
        cmd.Parameters.AddWithValue("now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("submitted", Store(TeamApplicationStatus.Eingereicht));
        cmd.Parameters.AddWithValue("read", Store(TeamApplicationStatus.Gelesen));
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Notes

    public static async Task<List<TeamApplicationNote>> GetNotesAsync(string applicationId)
    {
        var notes = new List<TeamApplicationNote>();
        await using var cmd = Db.CreateCommand(
            "SELECT note_id, application_id, author_id, text, created_at FROM teamapplication_notes " +
            "WHERE application_id = @aid ORDER BY created_at");
        cmd.Parameters.AddWithValue("aid", applicationId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            notes.Add(new TeamApplicationNote
            {
                NoteId = reader.GetString(0),
                ApplicationId = reader.GetString(1),
                AuthorId = reader.IsDBNull(2) ? 0 : (ulong)reader.GetInt64(2),
                Text = reader.IsDBNull(3) ? "" : reader.GetString(3),
                CreatedAt = reader.IsDBNull(4) ? 0 : reader.GetInt64(4)
            });

        return notes;
    }

    public static async Task AddNoteAsync(string applicationId, ulong authorId, string text)
    {
        await using var cmd = Db.CreateCommand(
            "INSERT INTO teamapplication_notes (note_id, application_id, author_id, text, created_at) " +
            "VALUES (@nid, @aid, @author, @text, @createdat)");
        cmd.Parameters.AddWithValue("nid", ToolSet.GenerateCaseID());
        cmd.Parameters.AddWithValue("aid", applicationId);
        cmd.Parameters.AddWithValue("author", (long)authorId);
        cmd.Parameters.AddWithValue("text", text);
        cmd.Parameters.AddWithValue("createdat", Now);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task DeleteNoteAsync(string noteId)
    {
        await using var cmd = Db.CreateCommand("DELETE FROM teamapplication_notes WHERE note_id = @nid");
        cmd.Parameters.AddWithValue("nid", noteId);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Reapply grants

    public static async Task<TeamApplicationReapplyGrant?> GetUnusedGrantAsync(ulong userId, string positionId,
        string phaseId)
    {
        await using var cmd = Db.CreateCommand(
            "SELECT grant_id, user_id, position_id, phase_id, granted_by, granted_at, reason, used_at " +
            "FROM teamapplication_reapply_grant WHERE user_id = @uid AND position_id = @pid AND phase_id = @phase " +
            "AND used_at = 0 ORDER BY granted_at DESC LIMIT 1");
        cmd.Parameters.AddWithValue("uid", (long)userId);
        cmd.Parameters.AddWithValue("pid", positionId);
        cmd.Parameters.AddWithValue("phase", phaseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new TeamApplicationReapplyGrant
        {
            GrantId = reader.GetString(0),
            UserId = (ulong)reader.GetInt64(1),
            PositionId = reader.IsDBNull(2) ? "" : reader.GetString(2),
            PhaseId = reader.IsDBNull(3) ? "" : reader.GetString(3),
            GrantedBy = reader.IsDBNull(4) ? 0 : (ulong)reader.GetInt64(4),
            GrantedAt = reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
            Reason = reader.IsDBNull(6) ? "" : reader.GetString(6),
            UsedAt = reader.IsDBNull(7) ? 0 : reader.GetInt64(7)
        };
    }

    public static async Task<TeamApplicationReapplyGrant> GrantReapplyAsync(ulong userId, string positionId,
        string phaseId, ulong grantedBy, string reason)
    {
        var grant = new TeamApplicationReapplyGrant
        {
            GrantId = ToolSet.GenerateCaseID(),
            UserId = userId,
            PositionId = positionId,
            PhaseId = phaseId,
            GrantedBy = grantedBy,
            GrantedAt = Now,
            Reason = reason
        };

        await using var cmd = Db.CreateCommand(
            "INSERT INTO teamapplication_reapply_grant (grant_id, user_id, position_id, phase_id, granted_by, " +
            "granted_at, reason, used_at) VALUES (@gid, @uid, @pid, @phase, @by, @at, @reason, 0)");
        cmd.Parameters.AddWithValue("gid", grant.GrantId);
        cmd.Parameters.AddWithValue("uid", (long)userId);
        cmd.Parameters.AddWithValue("pid", positionId);
        cmd.Parameters.AddWithValue("phase", phaseId);
        cmd.Parameters.AddWithValue("by", (long)grantedBy);
        cmd.Parameters.AddWithValue("at", grant.GrantedAt);
        cmd.Parameters.AddWithValue("reason", reason);
        await cmd.ExecuteNonQueryAsync();

        return grant;
    }

    public static async Task MarkGrantUsedAsync(string grantId)
    {
        await using var cmd = Db.CreateCommand(
            "UPDATE teamapplication_reapply_grant SET used_at = @usedat WHERE grant_id = @gid");
        cmd.Parameters.AddWithValue("gid", grantId);
        cmd.Parameters.AddWithValue("usedat", Now);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Templates and placeholders

    public static async Task<List<TeamApplicationTemplate>> GetTemplatesAsync(string? positionId = null)
    {
        var templates = new List<TeamApplicationTemplate>();
        var sql = "SELECT template_id, position_id, kind, name, text FROM teamapplication_templates";
        if (positionId != null) sql += " WHERE position_id IS NULL OR position_id = @pid";
        sql += " ORDER BY kind, name";

        await using var cmd = Db.CreateCommand(sql);
        if (positionId != null) cmd.Parameters.AddWithValue("pid", positionId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            templates.Add(new TeamApplicationTemplate
            {
                TemplateId = reader.GetString(0),
                PositionId = reader.IsDBNull(1) ? null : reader.GetString(1),
                Kind = ParseEnum(reader.IsDBNull(2) ? null : reader.GetString(2),
                    TeamApplicationTemplateKind.Accept),
                Name = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Text = reader.IsDBNull(4) ? "" : reader.GetString(4)
            });

        return templates;
    }

    public static async Task UpsertTemplateAsync(TeamApplicationTemplate template)
    {
        if (string.IsNullOrEmpty(template.TemplateId)) template.TemplateId = ToolSet.GenerateCaseID();

        await using var cmd = Db.CreateCommand(
            "INSERT INTO teamapplication_templates (template_id, position_id, kind, name, text) " +
            "VALUES (@tid, @pid, @kind, @name, @text) ON CONFLICT (template_id) DO UPDATE SET " +
            "position_id = EXCLUDED.position_id, kind = EXCLUDED.kind, name = EXCLUDED.name, text = EXCLUDED.text");
        cmd.Parameters.AddWithValue("tid", template.TemplateId);
        cmd.Parameters.AddWithValue("pid", (object?)template.PositionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("kind", Store(template.Kind));
        cmd.Parameters.AddWithValue("name", template.Name);
        cmd.Parameters.AddWithValue("text", template.Text);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task DeleteTemplateAsync(string templateId)
    {
        await using var cmd = Db.CreateCommand("DELETE FROM teamapplication_templates WHERE template_id = @tid");
        cmd.Parameters.AddWithValue("tid", templateId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<Dictionary<string, string>> GetPlaceholdersAsync()
    {
        var placeholders = new Dictionary<string, string>();
        await using var cmd = Db.CreateCommand("SELECT key, text FROM teamapplication_placeholder ORDER BY key");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            placeholders[reader.GetString(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);

        return placeholders;
    }

    public static async Task UpsertPlaceholderAsync(string key, string text)
    {
        await using var cmd = Db.CreateCommand(
            "INSERT INTO teamapplication_placeholder (key, text) VALUES (@key, @text) " +
            "ON CONFLICT (key) DO UPDATE SET text = EXCLUDED.text");
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("text", text);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task DeletePlaceholderAsync(string key)
    {
        await using var cmd = Db.CreateCommand("DELETE FROM teamapplication_placeholder WHERE key = @key");
        cmd.Parameters.AddWithValue("key", key);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Activity

    /// <summary>
    ///     Reuses the ExtraPermissions metric aggregation so both systems count the same way.
    /// </summary>
    public static async Task<TeamApplicationActivity> GetActivityAsync(ulong userId)
    {
        return new TeamApplicationActivity
        {
            Messages7 = await ExtraPermissionService.CountMetricAsync("metrics_messages", userId, 7, []),
            Messages30 = await ExtraPermissionService.CountMetricAsync("metrics_messages", userId, 30, []),
            Messages90 = await ExtraPermissionService.CountMetricAsync("metrics_messages", userId, 90, []),
            VoiceMinutes7 = await ExtraPermissionService.CountMetricAsync("metrics_voice", userId, 7, []),
            VoiceMinutes30 = await ExtraPermissionService.CountMetricAsync("metrics_voice", userId, 30, []),
            VoiceMinutes90 = await ExtraPermissionService.CountMetricAsync("metrics_voice", userId, 90, [])
        };
    }

    #endregion

    #region Validation

    /// <summary>
    ///     The single validation implementation. The form calls it while typing, the submit path calls it
    ///     again for every question, so client and server can never drift apart.
    /// </summary>
    public static string? ValidateAnswer(TeamApplicationQuestion question, string answer,
        IReadOnlyList<string> selected)
    {
        switch (question.Type)
        {
            case TeamApplicationQuestionType.ShortText:
            case TeamApplicationQuestionType.LongText:
            {
                var trimmed = (answer ?? "").Trim();
                if (trimmed.Length == 0)
                    return question.Required ? "Diese Frage muss beantwortet werden." : null;
                if (question.MinLength > 0 && trimmed.Length < question.MinLength)
                    return $"Mindestens {question.MinLength} Zeichen, aktuell {trimmed.Length}.";
                if (question.MaxLength > 0 && trimmed.Length > question.MaxLength)
                    return $"Höchstens {question.MaxLength} Zeichen, aktuell {trimmed.Length}.";
                return null;
            }
            case TeamApplicationQuestionType.Number:
            {
                var trimmed = (answer ?? "").Trim();
                if (trimmed.Length == 0)
                    return question.Required ? "Diese Frage muss beantwortet werden." : null;
                if (!long.TryParse(trimmed, out var value))
                    return "Bitte gib eine ganze Zahl ein.";
                if (question.MinValue != 0 || question.MaxValue != 0)
                {
                    if (value < question.MinValue)
                        return $"Der Wert muss mindestens {question.MinValue} sein.";
                    if (question.MaxValue != 0 && value > question.MaxValue)
                        return $"Der Wert darf höchstens {question.MaxValue} sein.";
                }

                return null;
            }
            case TeamApplicationQuestionType.SingleChoice:
            case TeamApplicationQuestionType.Dropdown:
            {
                var choice = (answer ?? "").Trim();
                if (choice.Length == 0)
                    return question.Required ? "Bitte wähle eine Option aus." : null;
                return question.Options.Contains(choice) ? null : "Die gewählte Option ist ungültig.";
            }
            case TeamApplicationQuestionType.Date:
            {
                var trimmed = (answer ?? "").Trim();
                if (trimmed.Length == 0)
                    return question.Required ? "Diese Frage muss beantwortet werden." : null;
                return DateOnly.TryParse(trimmed, out _) ? null : "Bitte gib ein gültiges Datum ein.";
            }
            case TeamApplicationQuestionType.MultipleChoice:
            {
                var picked = selected.Where(o => question.Options.Contains(o)).Distinct().ToList();
                if (picked.Count != selected.Count) return "Eine der gewählten Optionen ist ungültig.";
                if (picked.Count == 0)
                    return question.Required ? "Bitte wähle mindestens eine Option aus." : null;
                if (question.MinSelections > 0 && picked.Count < question.MinSelections)
                    return $"Bitte wähle mindestens {question.MinSelections} Optionen aus.";
                if (question.MaxSelections > 0 && picked.Count > question.MaxSelections)
                    return $"Bitte wähle höchstens {question.MaxSelections} Optionen aus.";
                return null;
            }
            default:
                return null;
        }
    }

    #endregion
}
