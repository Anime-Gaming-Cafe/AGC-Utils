#region

using SkiaSharp;

#endregion

namespace AGC_Management.Services;

/// <summary>
///     Central logic for the booster color system.
///     Color roles are not hardcoded: they are all roles whose position is strictly between the two
///     boundary roles named <see cref="BeginRoleName" /> and <see cref="EndRoleName" />.
///     Each color role gets an auto generated colored circle emoji on the resolved emoji guild.
/// </summary>
public static class BoosterColorService
{
    public const string Section = "BoosterColors";

    public const string BeginRoleName = "BEGIN_BOOSTERCOLOR";
    public const string EndRoleName = "END_BOOSTERCOLOR";

    // On the production AGC guild the emojis live on a dedicated internal emoji server.
    // On any other guild (test/debug) the emojis live on the guild itself.
    public const ulong ProductionGuildId = 750365461945778209;
    public const ulong ProductionEmojiGuildId = 826878963354959933;

    // Custom id of the select menu in the panel.
    public const string SelectorCustomId = "boostercolorselector";
    public const string ResetValue = "reset";

    #region Boundaries / color roles

    /// <summary>
    ///     Resolves the two boundary roles by their exact name. Returns null if one of them is missing.
    /// </summary>
    public static (DiscordRole begin, DiscordRole end)? GetBoundaries(DiscordGuild guild)
    {
        if (guild == null) return null;
        var begin = guild.Roles.Values.FirstOrDefault(r => r.Name == BeginRoleName);
        var end = guild.Roles.Values.FirstOrDefault(r => r.Name == EndRoleName);
        if (begin == null || end == null) return null;
        return (begin, end);
    }

    /// <summary>
    ///     Returns all color roles (roles strictly between the two boundaries), ordered top first.
    /// </summary>
    public static List<DiscordRole> GetColorRoles(DiscordGuild guild)
    {
        var boundaries = GetBoundaries(guild);
        if (boundaries == null) return new List<DiscordRole>();

        var low = Math.Min(boundaries.Value.begin.Position, boundaries.Value.end.Position);
        var high = Math.Max(boundaries.Value.begin.Position, boundaries.Value.end.Position);

        return guild.Roles.Values
            .Where(r => r.Position > low && r.Position < high)
            .OrderByDescending(r => r.Position)
            .ToList();
    }

    #endregion

    #region Eligibility

    public static bool IsStaff(DiscordMember member)
    {
        return member.Roles.Any(r => r.Id == GlobalProperties.StaffRoleId);
    }

    /// <summary>
    ///     A member may use booster colors if they are not staff and are currently boosting the server.
    ///     When the dev bypass is enabled the boost requirement is skipped (handy for development/testing).
    /// </summary>
    public static async Task<bool> IsEligibleAsync(DiscordMember member)
    {
        if (member == null || member.IsBot) return false;
        if (IsStaff(member)) return false;

        if (member.PremiumSince.HasValue) return true;

        return await GetBypassEligibilityAsync();
    }

    #endregion

    #region Apply / reset / cleanup

    public static async Task ApplyColorAsync(DiscordMember member, DiscordRole targetRole)
    {
        var colorRoles = GetColorRoles(member.Guild);

        foreach (var role in colorRoles)
        {
            if (role.Id == targetRole.Id) continue;
            if (member.Roles.Any(r => r.Id == role.Id))
                await member.RevokeRoleAsync(role, "Booster color changed");
        }

        if (member.Roles.All(r => r.Id != targetRole.Id))
            await member.GrantRoleAsync(targetRole, "Booster color selected");
    }

    public static async Task ResetColorAsync(DiscordMember member)
    {
        var colorRoles = GetColorRoles(member.Guild);
        foreach (var role in colorRoles)
            if (member.Roles.Any(r => r.Id == role.Id))
                await member.RevokeRoleAsync(role, "Booster color reset");
    }

    /// <summary>
    ///     Removes any color role from a member that is no longer eligible.
    /// </summary>
    public static async Task CleanupMemberAsync(DiscordMember member)
    {
        var colorRoles = GetColorRoles(member.Guild);
        if (colorRoles.Count == 0) return;

        var owned = colorRoles.Where(r => member.Roles.Any(mr => mr.Id == r.Id)).ToList();
        if (owned.Count == 0) return;

        if (await IsEligibleAsync(member)) return;

        foreach (var role in owned)
            await member.RevokeRoleAsync(role, "No longer eligible for booster color");
    }

    #endregion

    #region Settings accessors (botsettings via RuntimeSettings)

    public static async Task<bool> GetEnabledAsync()
    {
        return await RuntimeSettings.GetBoolAsync(Section, "Enabled", false);
    }

    public static Task SetEnabledAsync(bool enabled)
    {
        return RuntimeSettings.SetAsync(Section, "Enabled", enabled.ToString());
    }

    public static Task<ulong> GetPanelChannelIdAsync()
    {
        return GetUlongSettingAsync("PanelChannelId");
    }

    public static Task<ulong> GetPanelMessageIdAsync()
    {
        return GetUlongSettingAsync("PanelMessageId");
    }

    public static async Task SetPanelLocationAsync(ulong channelId, ulong messageId)
    {
        await RuntimeSettings.SetAsync(Section, "PanelChannelId", channelId.ToString());
        await RuntimeSettings.SetAsync(Section, "PanelMessageId", messageId.ToString());
    }

    public static Task<bool> GetBypassEligibilityAsync()
    {
        return RuntimeSettings.GetBoolAsync(Section, "BypassEligibility", false);
    }

    public static Task SetBypassEligibilityAsync(bool bypass)
    {
        return RuntimeSettings.SetAsync(Section, "BypassEligibility", bypass.ToString());
    }

    public static async Task<string> GetEmbedTitleAsync()
    {
        var v = await RuntimeSettings.GetAsync(Section, "EmbedTitle");
        return string.IsNullOrWhiteSpace(v) ? "Booster Farben" : v;
    }

    public static Task SetEmbedTitleAsync(string title)
    {
        return RuntimeSettings.SetAsync(Section, "EmbedTitle", title ?? "");
    }

    public static async Task<string> GetEmbedDescriptionAsync()
    {
        var v = await RuntimeSettings.GetAsync(Section, "EmbedDescription");
        return string.IsNullOrWhiteSpace(v)
            ? "Hier kannst du dir deine Booster Farbe auswählen."
            : v;
    }

    public static Task SetEmbedDescriptionAsync(string description)
    {
        return RuntimeSettings.SetAsync(Section, "EmbedDescription", description ?? "");
    }

    private static async Task<ulong> GetUlongSettingAsync(string key)
    {
        var raw = await RuntimeSettings.GetAsync(Section, key);
        return ulong.TryParse(raw, out var value) ? value : 0UL;
    }

    #endregion

    #region Emoji handling

    public static ulong GetEmojiGuildId()
    {
        var serverId = ulong.Parse(BotConfig.GetConfig()["ServerConfig"]["ServerId"]);
        return serverId == ProductionGuildId ? ProductionEmojiGuildId : serverId;
    }

    public static string GetEmojiName(ulong roleId)
    {
        return $"bc_{roleId}";
    }

    private static async Task<DiscordGuild?> GetEmojiGuildAsync()
    {
        try
        {
            return await CurrentApplication.DiscordClient.GetGuildAsync(GetEmojiGuildId());
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "BoosterColors: failed to resolve emoji guild");
            return null;
        }
    }

    /// <summary>
    ///     Generates / refreshes the colored circle emoji for a color role on the emoji guild.
    ///     Returns the created emoji, or null on failure (caller falls back to a unicode circle).
    /// </summary>
    public static async Task<DiscordGuildEmoji?> SyncEmojiAsync(DiscordRole role)
    {
        var emojiGuild = await GetEmojiGuildAsync();
        if (emojiGuild == null) return null;

        var name = GetEmojiName(role.Id);
        try
        {
            var existing = (await emojiGuild.GetEmojisAsync()).FirstOrDefault(e => e.Name == name);
            if (existing != null)
                await emojiGuild.DeleteEmojiAsync(existing, "Booster color emoji refresh");

            await using var stream = GenerateCircle(role.Color, role.Colors?.SecondaryColor);
            return await emojiGuild.CreateEmojiAsync(name, stream, reason: "Booster color emoji");
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "BoosterColors: failed to sync emoji for role {RoleId}", role.Id);
            return null;
        }
    }

    public static async Task DeleteEmojiAsync(ulong roleId)
    {
        var emojiGuild = await GetEmojiGuildAsync();
        if (emojiGuild == null) return;

        var name = GetEmojiName(roleId);
        try
        {
            var existing = (await emojiGuild.GetEmojisAsync()).FirstOrDefault(e => e.Name == name);
            if (existing != null)
                await emojiGuild.DeleteEmojiAsync(existing, "Booster color role deleted");
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "BoosterColors: failed to delete emoji for role {RoleId}", roleId);
        }
    }

    /// <summary>
    ///     Builds a name -> emoji id lookup of all color role emojis on the emoji guild (single fetch).
    /// </summary>
    public static async Task<Dictionary<string, ulong>> GetEmojiLookupAsync()
    {
        var result = new Dictionary<string, ulong>();
        var emojiGuild = await GetEmojiGuildAsync();
        if (emojiGuild == null) return result;

        try
        {
            foreach (var emoji in await emojiGuild.GetEmojisAsync())
                if (emoji.Name.StartsWith("bc_"))
                    result[emoji.Name] = emoji.Id;
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e, "BoosterColors: failed to fetch emoji lookup");
        }

        return result;
    }

    /// <summary>
    ///     Returns a component emoji for the dropdown: the custom circle emoji if present, otherwise
    ///     the nearest matching unicode circle for the role color.
    /// </summary>
    public static DiscordComponentEmoji GetComponentEmoji(DiscordRole role, Dictionary<string, ulong> emojiLookup)
    {
        if (emojiLookup != null && emojiLookup.TryGetValue(GetEmojiName(role.Id), out var emojiId))
            return new DiscordComponentEmoji(emojiId);

        return new DiscordComponentEmoji(NearestUnicodeCircle(role.Color));
    }

    private static MemoryStream GenerateCircle(DiscordColor primary, DiscordColor? secondary = null)
    {
        const int size = 128;
        using var bmp = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        var primaryColor = new SKColor(primary.R, primary.G, primary.B);
        if (secondary.HasValue)
        {
            // Horizontal blend (left -> right) to match the WebUI gradient preview.
            var secondaryColor = new SKColor(secondary.Value.R, secondary.Value.G, secondary.Value.B);
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(size, 0),
                new[] { primaryColor, secondaryColor },
                null,
                SKShaderTileMode.Clamp);
        }
        else
        {
            paint.Color = primaryColor;
        }

        canvas.DrawCircle(size / 2f, size / 2f, size / 2f - 4, paint);

        paint.Shader?.Dispose();

        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;
        return ms;
    }

    private static readonly (string emoji, byte r, byte g, byte b)[] UnicodeCircles =
    {
        ("🔴", 237, 66, 69), // red
        ("🟠", 244, 144, 12), // orange
        ("🟡", 253, 203, 88), // yellow
        ("🟢", 87, 242, 135), // green
        ("🔵", 88, 101, 242), // blue
        ("🟣", 169, 90, 240), // purple
        ("🟤", 121, 80, 51), // brown
        ("⚫", 35, 39, 42), // black
        ("⚪", 255, 255, 255) // white
    };

    private static string NearestUnicodeCircle(DiscordColor color)
    {
        var best = UnicodeCircles[0];
        var bestDistance = double.MaxValue;

        foreach (var candidate in UnicodeCircles)
        {
            var dr = color.R - candidate.r;
            var dg = color.G - candidate.g;
            var db = color.B - candidate.b;
            var distance = dr * dr + dg * dg + db * db;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best.emoji;
    }

    #endregion
}
