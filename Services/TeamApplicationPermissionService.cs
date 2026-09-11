#region

using AGC_Management.Entities.ApplicationSystem;
using AGC_Management.Enums.ApplicationSystem;
using AGC_Management.Enums.Web;
using AGC_Management.Utils;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     The single decision point for the new application system. Pages gate their markup against a
///     <see cref="TeamApplicationPermissionSet" /> loaded once, but every writing handler has to ask
///     <see cref="HasAsync" /> again before it touches the database.
/// </summary>
public static class TeamApplicationPermissionService
{
    private static readonly TimeSpan RuleCacheTtl = TimeSpan.FromSeconds(30);
    private static List<TeamApplicationPermissionRule>? _ruleCache;
    private static DateTime _ruleCacheExpires = DateTime.MinValue;

    private static NpgsqlDataSource Db =>
        CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();

    #region Rules

    public static async Task<List<TeamApplicationPermissionRule>> GetRulesAsync()
    {
        var cached = _ruleCache;
        if (cached != null && _ruleCacheExpires > DateTime.UtcNow) return cached;

        var rules = new List<TeamApplicationPermissionRule>();
        await using var cmd = Db.CreateCommand(
            "SELECT permission_id, role_id, position_id, phase_id, permission FROM teamapplication_permissions");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!Enum.TryParse<TeamApplicationPermission>(reader.IsDBNull(4) ? null : reader.GetString(4), true,
                    out var permission))
                continue;

            rules.Add(new TeamApplicationPermissionRule
            {
                PermissionId = reader.GetString(0),
                RoleId = reader.IsDBNull(1) ? 0 : (ulong)reader.GetInt64(1),
                PositionId = reader.IsDBNull(2) ? null : reader.GetString(2),
                PhaseId = reader.IsDBNull(3) ? null : reader.GetString(3),
                Permission = permission
            });
        }

        _ruleCache = rules;
        _ruleCacheExpires = DateTime.UtcNow + RuleCacheTtl;
        return rules;
    }

    public static void InvalidateCache()
    {
        _ruleCache = null;
    }

    public static async Task AddRuleAsync(ulong roleId, string? positionId, string? phaseId,
        TeamApplicationPermission permission)
    {
        await using var cmd = Db.CreateCommand(
            "INSERT INTO teamapplication_permissions (permission_id, role_id, position_id, phase_id, permission) " +
            "VALUES (@id, @role, @position, @phase, @permission) ON CONFLICT DO NOTHING");
        cmd.Parameters.AddWithValue("id", ToolSet.GenerateCaseID());
        cmd.Parameters.AddWithValue("role", (long)roleId);
        cmd.Parameters.AddWithValue("position", (object?)positionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("phase", (object?)phaseId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("permission", permission.ToString().ToLowerInvariant());
        await cmd.ExecuteNonQueryAsync();

        InvalidateCache();
    }

    public static async Task RemoveRuleAsync(string permissionId)
    {
        await using var cmd = Db.CreateCommand(
            "DELETE FROM teamapplication_permissions WHERE permission_id = @id");
        cmd.Parameters.AddWithValue("id", permissionId);
        await cmd.ExecuteNonQueryAsync();

        InvalidateCache();
    }

    #endregion

    #region Evaluation

    /// <summary>
    ///     Bot owner and administrator always pass, mirroring how the rest of the dashboard treats them.
    ///     Everyone else is matched by their live guild roles; someone who left the server gets nothing.
    /// </summary>
    private static async Task<(bool blanket, HashSet<ulong> roleIds)> GetActorAsync(ulong userId)
    {
        if (GlobalProperties.DebugMode) return (true, []);

        var guild = CurrentApplication.TargetGuild;
        if (guild is null) return (false, []);

        if (userId == GlobalProperties.BotOwnerId) return (true, []);

        var role = await AuthUtils.RetrieveRole(userId);
        if (role == AccessLevel.BotOwner.ToString() || role == AccessLevel.Administrator.ToString())
            return (true, []);

        return !guild.Members.TryGetValue(userId, out var member)
            ? (false, [])
            : (false, member.Roles.Select(r => r.Id).ToHashSet());
    }

    /// <summary>The single place that knows FullAccess stands in for every other permission.</summary>
    private static bool Grants(TeamApplicationPermissionRule rule, TeamApplicationPermission permission)
    {
        return rule.Permission == permission || rule.Permission == TeamApplicationPermission.FullAccess;
    }

    private static bool Matches(TeamApplicationPermissionRule rule, HashSet<ulong> roleIds,
        TeamApplicationPermission permission, string? positionId, string? phaseId)
    {
        if (!Grants(rule, permission)) return false;
        if (!roleIds.Contains(rule.RoleId)) return false;
        if (rule.PositionId != null && rule.PositionId != positionId) return false;
        if (rule.PhaseId != null && phaseId != null && rule.PhaseId != phaseId) return false;
        return true;
    }

    /// <summary>
    ///     Bot owner and administrator. Deliberately not expressible through the matrix, so destructive
    ///     actions cannot be handed to a role by mistake.
    /// </summary>
    public static async Task<bool> IsAdministratorAsync(ulong userId)
    {
        var (blanket, _) = await GetActorAsync(userId);
        return blanket;
    }

    public static async Task<bool> HasAsync(ulong userId, TeamApplicationPermission permission,
        string? positionId = null, string? phaseId = null)
    {
        var (blanket, roleIds) = await GetActorAsync(userId);
        if (blanket) return true;
        if (roleIds.Count == 0) return false;

        var rules = await GetRulesAsync();
        return rules.Any(r => Matches(r, roleIds, permission, positionId, phaseId));
    }

    /// <summary>Every position the user may see with the given permission, already filtered for the UI.</summary>
    public static async Task<HashSet<string>> GetAllowedPositionsAsync(ulong userId,
        TeamApplicationPermission permission)
    {
        var allPositions = (await TeamApplicationService.GetPositionsAsync()).Select(p => p.PositionId).ToList();

        var (blanket, roleIds) = await GetActorAsync(userId);
        if (blanket) return allPositions.ToHashSet();
        if (roleIds.Count == 0) return [];

        var rules = await GetRulesAsync();
        var allowed = new HashSet<string>();
        foreach (var rule in rules)
        {
            if (!Grants(rule, permission)) continue;
            if (!roleIds.Contains(rule.RoleId)) continue;

            if (rule.PositionId == null)
                foreach (var positionId in allPositions)
                    allowed.Add(positionId);
            else
                allowed.Add(rule.PositionId);
        }

        return allowed;
    }

    public static async Task<TeamApplicationPermissionSet> GetForAsync(ulong userId, string? positionId = null,
        string? phaseId = null)
    {
        var (blanket, roleIds) = await GetActorAsync(userId);
        if (blanket) return TeamApplicationPermissionSet.All;
        if (roleIds.Count == 0) return new TeamApplicationPermissionSet();

        var rules = await GetRulesAsync();

        bool Has(TeamApplicationPermission permission)
        {
            return rules.Any(r => Matches(r, roleIds, permission, positionId, phaseId));
        }

        return new TeamApplicationPermissionSet
        {
            ViewApplications = Has(TeamApplicationPermission.ViewApplications),
            MarkInternal = Has(TeamApplicationPermission.MarkInternal),
            Notes = Has(TeamApplicationPermission.Notes),
            Decide = Has(TeamApplicationPermission.Decide),
            ManagePhases = Has(TeamApplicationPermission.ManagePhases),
            ManageQuestions = Has(TeamApplicationPermission.ManageQuestions),
            GrantReapply = Has(TeamApplicationPermission.GrantReapply),
            ViewModerationHistory = Has(TeamApplicationPermission.ViewModerationHistory)
        };
    }

    #endregion
}

public sealed class TeamApplicationPermissionSet
{
    public static TeamApplicationPermissionSet All => new()
    {
        ViewApplications = true,
        MarkInternal = true,
        Notes = true,
        Decide = true,
        ManagePhases = true,
        ManageQuestions = true,
        GrantReapply = true,
        ViewModerationHistory = true
    };

    public bool ViewApplications { get; set; }
    public bool MarkInternal { get; set; }
    public bool Notes { get; set; }
    public bool Decide { get; set; }
    public bool ManagePhases { get; set; }
    public bool ManageQuestions { get; set; }
    public bool GrantReapply { get; set; }
    public bool ViewModerationHistory { get; set; }

    public bool Any => this.ViewApplications || this.MarkInternal || this.Notes || this.Decide ||
                       this.ManagePhases || this.ManageQuestions || this.GrantReapply ||
                       this.ViewModerationHistory;
}
