#region

using AGC_Management.Enums.LevelSystem;
using SkiaSharp;

#endregion

namespace AGC_Management.Utils;

public sealed class ImageUtils
{
    public static SKBitmap CreateProgressBar(int width, int height, float percentage, string text)
    {
        var bmp = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);

        var backgroundPaint = new SKPaint
        {
            Color = SKColors.Gray
        };
        canvas.DrawRect(0, 0, width, height, backgroundPaint);

        var progressPaint = new SKPaint
        {
            Color = SKColors.BlueViolet
        };
        canvas.DrawRect(0, 0, width * percentage, height, progressPaint);

        var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };
        using var typeface = SKTypeface.FromFamilyName("Verdana");
        using var textFont = new SKFont(typeface, 18);
        textFont.MeasureText(text, out var textBounds, textPaint);
        canvas.DrawText(text, width / 2, (height + textBounds.Height) / 2, SKTextAlign.Center, textFont, textPaint);

        return bmp;
    }

    public static async Task<SKData> GenerateRankCard(DiscordUser user, int currentxpforthislevel, int level, int rank,
        float progression, int totalxp,
        int xpforthisleveltocomplete)
    {
        const int cardWidth = 934;
        const int cardHeight = 282;
        var boxalpha = 150;
        var font = "Verdana";

        try
        {
            font = BotConfig.GetConfig()["Leveling"]["RankCardFont"];
        }
        catch (Exception)
        {
            // ignored
        }

        var hasCustomSettings = false;
        var c_bgdata = ""; // base64 image
        var c_barcolor = SKColor.Parse("#9f00ff");
        if (await HasCustomRankCardSettings(user.Id))
        {
            hasCustomSettings = true;
            var customSettings = await GetCustomRankCardSettings(user.Id);
            c_bgdata = customSettings.Background;
            c_barcolor = SKColor.Parse(customSettings.HexColor);
            font = customSettings.Font;
            boxalpha = customSettings.BoxOpacity ?? 150;
        }


        using var bitmap = new SKBitmap(cardWidth, cardHeight);
        using var canvas = new SKCanvas(bitmap);

        var totalXP = Converter.FormatWithCommas(totalxp);
        var xptoCompleteCurrentLevel = Converter.FormatWithCommas(xpforthisleveltocomplete);
        var progress = progression / 100;

        using var httpclient = new HttpClient();
        httpclient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

        var avatarurl = user.GetAvatarUrl(MediaFormat.Png);
        var response2 = await httpclient.GetAsync(avatarurl);
        if (!response2.IsSuccessStatusCode) throw new InvalidDataException("Failed to download avatar image");

        var avatarstream = await response2.Content.ReadAsByteArrayAsync();

        var avatar = SKBitmap.Decode(avatarstream);


        var avatarPaint = new SKPaint
        {
            IsAntialias = true
        };
        var avatarSampling = new SKSamplingOptions(SKCubicResampler.Mitchell);

        var backgroundPaint = new SKPaint
        {
            IsAntialias = true
        };
        var bgurl = "";
        var default_barcolor = SKColor.Parse("#9f00ff");
        var overridecard = false;
        try
        {
            bgurl = BotConfig.GetConfig()["Leveling"]["DefaultRankCardBackgroundUrl"];
            overridecard = true;
        }
        catch (Exception)
        {
        }

        var barcolor = default_barcolor;

        if (hasCustomSettings) barcolor = c_barcolor;


        try
        {
            if (!hasCustomSettings)
            {
                httpclient.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                    "(KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                var response = await httpclient.GetAsync(bgurl);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidDataException("Failed to download background image");

                var bgstream = await response.Content.ReadAsByteArrayAsync();
                using var bg_stream = new MemoryStream(bgstream);
                var backgroundBitmap = SKBitmap.Decode(bg_stream);
                canvas.DrawBitmap(backgroundBitmap, new SKRect(0, 0, cardWidth, cardHeight), backgroundPaint);
            }
            else
            {
                using var bg_stream = new MemoryStream(Convert.FromBase64String(c_bgdata));
                var backgroundBitmap = SKBitmap.Decode(bg_stream);
                canvas.DrawBitmap(backgroundBitmap, new SKRect(0, 0, cardWidth, cardHeight), backgroundPaint);
            }
        }
        catch (Exception e)
        {
            // ignored
        }

        var darkenPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, (byte)boxalpha),
            IsAntialias = true
        };

        canvas.DrawRoundRect(new SKRect(15, 15, cardWidth - 15, cardHeight - 15), 20, 20, darkenPaint);


        var avatarBitmap = avatar;
        var avatarRect = new SKRect(20, 20, 262, 262);
        using var mask = new SKPath();

        mask.AddRoundRect(avatarRect, 15, 15);

        canvas.Save();
        canvas.ClipPath(mask);
        canvas.DrawBitmap(avatarBitmap, avatarRect, avatarSampling, avatarPaint);
        canvas.Restore();


        var guildiconurl = CurrentApplication.TargetGuild.IconUrl;
        if (!string.IsNullOrWhiteSpace(guildiconurl))
        {
            var response3 = await httpclient.GetAsync(guildiconurl);
            if (!response3.IsSuccessStatusCode) throw new InvalidDataException("Failed to download guild icon image");

            var guildiconstream = await response3.Content.ReadAsByteArrayAsync();
            using var guildicon = SKBitmap.Decode(guildiconstream);
            var guildiconSize = 262 * 0.25f;
            var guildiconrect = new SKRect(cardWidth - guildiconSize - 50, 50, cardWidth - 50, guildiconSize + 50);
            using var guildiconmask = new SKPath();

            guildiconmask.AddRoundRect(guildiconrect, 15, 15);

            canvas.Save();
            canvas.ClipPath(guildiconmask);
            canvas.DrawBitmap(guildicon, guildiconrect, avatarSampling, avatarPaint);
            canvas.Restore();

            var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = barcolor,
                StrokeWidth = 2,
                IsAntialias = true
            };

            canvas.DrawRoundRect(guildiconrect, 15, 15, borderPaint); // Zeichnet eine Umrandung
        }


        var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };

        using var boldTypeface = SKTypeface.FromFamilyName(font, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);
        using var nameFont = new SKFont(boldTypeface, 30);
        using var rankLevelFont = new SKFont(boldTypeface, 25);
        using var totalxpFont = new SKFont(boldTypeface, 18);

        canvas.DrawText(user.GetFormattedUserName(), 300, 70, SKTextAlign.Left, nameFont, textPaint);
        canvas.DrawText($"Rang: #{rank}   Level: {level}", 300, 110, SKTextAlign.Left, rankLevelFont, textPaint);
        canvas.DrawText($"Gesamt XP: {totalXP}", 300, 230, SKTextAlign.Left, totalxpFont, textPaint);

        var progressBarBackgroundPaint = new SKPaint
        {
            Color = SKColors.LightGray.WithAlpha(140),
            Style = SKPaintStyle.Fill
        };

        var progressBarBackgroundRect = new SKRect(300, 200, cardWidth - 30, 160);
        canvas.DrawRoundRect(new SKRoundRect(progressBarBackgroundRect, 10, 10), progressBarBackgroundPaint);

        var progressBarPaint = new SKPaint
        {
            Color = barcolor,
            Style = SKPaintStyle.Fill
        };

        var progressBarRect = new SKRect(progressBarBackgroundRect.Left, progressBarBackgroundRect.Top,
            progressBarBackgroundRect.Left + (progressBarBackgroundRect.Width - 20) * progress,
            progressBarBackgroundRect.Bottom);
        canvas.DrawRoundRect(new SKRoundRect(progressBarRect, 10, 10), progressBarPaint);

        var xpText =
            $"{Converter.FormatWithCommas(currentxpforthislevel)}/{xptoCompleteCurrentLevel} XP ({Math.Round(progress * 100, 2)}%)";
        var xpPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };

        using var xpTypeface = SKTypeface.FromFamilyName(font);
        using var xpFont = new SKFont(xpTypeface, 25);

        var xpTextWidth = xpFont.MeasureText(xpText, xpPaint);
        var xpTextX = progressBarBackgroundRect.Left + progressBarBackgroundRect.Width / 2;
        var xpTextY = progressBarBackgroundRect.Top + progressBarBackgroundRect.Height / 2 + xpFont.Size / 2;
        xpTextY -= 2;

        if (xpTextX + xpTextWidth / 2 > progressBarRect.Right)
        {
            canvas.Save();
            canvas.ClipRect(new SKRect(progressBarRect.Left, progressBarRect.Top, progressBarRect.Right,
                progressBarRect.Bottom));
            canvas.DrawText(xpText, xpTextX, xpTextY, SKTextAlign.Center, xpFont, xpPaint);
            canvas.Restore();

            xpPaint.Color = SKColors.White;

            canvas.Save();
            canvas.ClipRect(new SKRect(progressBarRect.Right, progressBarRect.Top, progressBarBackgroundRect.Right,
                progressBarBackgroundRect.Bottom));
            canvas.DrawText(xpText, xpTextX, xpTextY, SKTextAlign.Center, xpFont, xpPaint);
            canvas.Restore();
        }
        else
        {
            canvas.DrawText(xpText, xpTextX, xpTextY, SKTextAlign.Center, xpFont, xpPaint);
        }


        using var image = SKImage.FromBitmap(bitmap);
        var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data;
    }


    /// <summary>
    ///     Generates a rank card for web preview.
    /// </summary>
    /// <param name="user">The Discord user for whom the rank card is generated.</param>
    /// <param name="currentxpforthislevel">The current experience points accumulated for this level.</param>
    /// <param name="level">The current level of the user.</param>
    /// <param name="rank">The rank of the user.</param>
    /// <param name="progression">The progress towards the next level.</param>
    /// <param name="totalxp">The total experience points earned by the user.</param>
    /// <param name="xpforthisleveltocomplete">The experience points required to complete this level.</param>
    /// <param name="boxalpha">The alpha value for the box.</param>
    /// <param name="font">The font used in the rank card.</param>
    /// <param name="bgdata">The background data used in the rank card.</param>
    /// <param name="hexcolor">The hex color used in the rank card.</param>
    /// <returns>
    ///     A Task with the generated rank card in SKData format.
    /// </returns>
    public static async Task<SKData> GenerateRankCardForWebPreview(DiscordUser user, int currentxpforthislevel,
        int level, int rank,
        float progression, int totalxp,
        int xpforthisleveltocomplete, int boxalpha, string font, string bgdata, string hexcolor)
    {
        const int cardWidth = 934;
        const int cardHeight = 282;

        if (string.IsNullOrWhiteSpace(bgdata))
        {
            // check current settings for background if it's empty use fallback
            if (await HasCustomRankCardSettings(user.Id))
            {
                var customSettings = await GetCustomRankCardSettings(user.Id);
                bgdata = customSettings.Background;
            }
            else
            {
                bgdata = await GetFallbackBackground();
            }
        }

        using var bitmap = new SKBitmap(cardWidth, cardHeight);
        using var canvas = new SKCanvas(bitmap);

        var totalXP = Converter.FormatWithCommas(totalxp);
        var xptoCompleteCurrentLevel = Converter.FormatWithCommas(xpforthisleveltocomplete);
        var progress = progression / 100;

        using var httpclient = new HttpClient();
        httpclient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

        var avatarurl = user.GetAvatarUrl(MediaFormat.Png);
        var response2 = await httpclient.GetAsync(avatarurl);
        if (!response2.IsSuccessStatusCode) throw new InvalidDataException("Failed to download avatar image");

        var avatarstream = await response2.Content.ReadAsByteArrayAsync();

        var avatar = SKBitmap.Decode(avatarstream);


        var avatarPaint = new SKPaint
        {
            IsAntialias = true
        };
        var avatarSampling = new SKSamplingOptions(SKCubicResampler.Mitchell);

        var backgroundPaint = new SKPaint
        {
            IsAntialias = true
        };
        var bgurl = "";
        var default_barcolor = SKColor.Parse(hexcolor);


        var barcolor = default_barcolor;

        using var bg_stream = new MemoryStream(Convert.FromBase64String(bgdata));
        var backgroundBitmap = SKBitmap.Decode(bg_stream);
        canvas.DrawBitmap(backgroundBitmap, new SKRect(0, 0, cardWidth, cardHeight), backgroundPaint);

        var darkenPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, (byte)boxalpha),
            IsAntialias = true
        };

        canvas.DrawRoundRect(new SKRect(15, 15, cardWidth - 15, cardHeight - 15), 20, 20, darkenPaint);


        var avatarBitmap = avatar;
        var avatarRect = new SKRect(20, 20, 262, 262);
        using var mask = new SKPath();

        mask.AddRoundRect(avatarRect, 15, 15);

        canvas.Save();
        canvas.ClipPath(mask);
        canvas.DrawBitmap(avatarBitmap, avatarRect, avatarSampling, avatarPaint);
        canvas.Restore();


        var guildiconurl = CurrentApplication.TargetGuild.IconUrl;
        if (!string.IsNullOrWhiteSpace(guildiconurl))
        {
            var response3 = await httpclient.GetAsync(guildiconurl);
            if (!response3.IsSuccessStatusCode) throw new InvalidDataException("Failed to download guild icon image");

            var guildiconstream = await response3.Content.ReadAsByteArrayAsync();
            using var guildicon = SKBitmap.Decode(guildiconstream);
            var guildiconSize = 262 * 0.25f;
            var guildiconrect = new SKRect(cardWidth - guildiconSize - 50, 50, cardWidth - 50, guildiconSize + 50);
            using var guildiconmask = new SKPath();

            guildiconmask.AddRoundRect(guildiconrect, 15, 15);

            canvas.Save();
            canvas.ClipPath(guildiconmask);
            canvas.DrawBitmap(guildicon, guildiconrect, avatarSampling, avatarPaint);
            canvas.Restore();

            var borderPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColor.Parse(hexcolor),
                StrokeWidth = 2,
                IsAntialias = true
            };

            canvas.DrawRoundRect(guildiconrect, 15, 15, borderPaint); // Zeichnet eine Umrandung
        }


        var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };

        using var boldTypeface = SKTypeface.FromFamilyName(font, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal,
            SKFontStyleSlant.Upright);
        using var nameFont = new SKFont(boldTypeface, 30);
        using var rankLevelFont = new SKFont(boldTypeface, 25);
        using var totalxpFont = new SKFont(boldTypeface, 18);

        canvas.DrawText(user.GetFormattedUserName(), 300, 70, SKTextAlign.Left, nameFont, textPaint);
        canvas.DrawText($"Rang: #{rank}   Level: {level}", 300, 110, SKTextAlign.Left, rankLevelFont, textPaint);
        canvas.DrawText($"Gesamt XP: {totalXP}", 300, 230, SKTextAlign.Left, totalxpFont, textPaint);

        var progressBarBackgroundPaint = new SKPaint
        {
            Color = SKColors.LightGray.WithAlpha(140),
            Style = SKPaintStyle.Fill
        };

        var progressBarBackgroundRect = new SKRect(300, 200, cardWidth - 30, 160);
        canvas.DrawRoundRect(new SKRoundRect(progressBarBackgroundRect, 10, 10), progressBarBackgroundPaint);

        var progressBarPaint = new SKPaint
        {
            Color = barcolor,
            Style = SKPaintStyle.Fill
        };

        var progressBarRect = new SKRect(progressBarBackgroundRect.Left, progressBarBackgroundRect.Top,
            progressBarBackgroundRect.Left + (progressBarBackgroundRect.Width - 20) * progress,
            progressBarBackgroundRect.Bottom);
        canvas.DrawRoundRect(new SKRoundRect(progressBarRect, 10, 10), progressBarPaint);

        var xpText =
            $"{Converter.FormatWithCommas(currentxpforthislevel)}/{xptoCompleteCurrentLevel} XP ({Math.Round(progress * 100, 2)}%)";
        var xpPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };

        using var xpTypeface = SKTypeface.FromFamilyName(font);
        using var xpFont = new SKFont(xpTypeface, 25);

        var xpTextWidth = xpFont.MeasureText(xpText, xpPaint);
        var xpTextX = progressBarBackgroundRect.Left + progressBarBackgroundRect.Width / 2;
        var xpTextY = progressBarBackgroundRect.Top + progressBarBackgroundRect.Height / 2 + xpFont.Size / 2;
        xpTextY -= 2;

        if (xpTextX + xpTextWidth / 2 > progressBarRect.Right)
        {
            canvas.Save();
            canvas.ClipRect(new SKRect(progressBarRect.Left, progressBarRect.Top, progressBarRect.Right,
                progressBarRect.Bottom));
            canvas.DrawText(xpText, xpTextX, xpTextY, SKTextAlign.Center, xpFont, xpPaint);
            canvas.Restore();

            xpPaint.Color = SKColors.White;

            canvas.Save();
            canvas.ClipRect(new SKRect(progressBarRect.Right, progressBarRect.Top, progressBarBackgroundRect.Right,
                progressBarBackgroundRect.Bottom));
            canvas.DrawText(xpText, xpTextX, xpTextY, SKTextAlign.Center, xpFont, xpPaint);
            canvas.Restore();
        }
        else
        {
            canvas.DrawText(xpText, xpTextX, xpTextY, SKTextAlign.Center, xpFont, xpPaint);
        }


        using var image = SKImage.FromBitmap(bitmap);
        var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data;
    }


    public static async Task<string> GetFallbackBackground()
    {
        var bgurl = "";
        try
        {
            bgurl = BotConfig.GetConfig()["Leveling"]["DefaultRankCardBackgroundUrl"];
        }
        catch (Exception)
        {
            // ignored
        }

        try
        {
            using var httpclient = new HttpClient();
            httpclient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            var response = await httpclient.GetAsync(bgurl);
            if (!response.IsSuccessStatusCode) throw new InvalidDataException("Failed to download background image");

            var bgstream = await response.Content.ReadAsByteArrayAsync();
            return Convert.ToBase64String(bgstream);
        }
        catch (Exception e)
        {
            CurrentApplication.Logger.Error(e,
                "Failed to download fallback background image. Returning black background.");
        }

        // if everything fails, return a black background
        var bmp = new SKBitmap(934, 282);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        var base64 = Convert.ToBase64String(data.ToArray());
        return base64;
    }


    public static async Task<bool> HasCustomRankCardSettings(ulong UserId)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        using var cmd = con.CreateCommand("SELECT * FROM userrankcardsettings WHERE userid = @userid");
        cmd.Parameters.AddWithValue("userid", (long)UserId);
        await using var reader = await cmd.ExecuteReaderAsync();
        return reader.HasRows;
    }

    public static async Task<CustomRankCard> GetCustomRankCardSettings(ulong UserId)
    {
        var con = CurrentApplication.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        using var cmd = con.CreateCommand("SELECT * FROM userrankcardsettings WHERE userid = @userid");
        cmd.Parameters.AddWithValue("userid", (long)UserId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!reader.HasRows) return null;

        await reader.ReadAsync();
        var bg = "";
        try
        {
            bg = reader.GetString(1);
        }
        catch (Exception)
        {
            // ignored
        }

        if (string.IsNullOrWhiteSpace(bg)) bg = await GetFallbackBackground();

        var card = new CustomRankCard
        {
            UserId = (ulong)reader.GetInt64(0),
            Background = bg,
            HexColor = reader.GetString(2),
            Font = reader.GetString(3),
            BoxOpacity = reader.GetInt32(4)
        };
        return card;
    }
}