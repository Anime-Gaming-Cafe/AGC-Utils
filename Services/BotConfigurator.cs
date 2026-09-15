#region

using AGC_Management.Utils;
using IniParser;
using IniParser.Model;

#endregion

namespace AGC_Management;

public static class BotConfig
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private static IniData _cachedConfig;
    private static DateTime _cacheExpires = DateTime.MinValue;

    public static IniData GetConfig()
    {
        if (_cachedConfig != null && DateTime.UtcNow < _cacheExpires)
            return _cachedConfig;

        FileIniDataParser parser = new();
        try
        {
            _cachedConfig = parser.ReadFile("config.ini");
            _cacheExpires = DateTime.UtcNow + CacheTtl;
        }
        catch
        {
            Console.WriteLine("Die Konfigurationsdatei konnte nicht geladen werden. Bitte überprüfe die config.");
            Console.WriteLine("Drücke eine beliebige Taste um das Programm zu beenden.");
            Console.WriteLine(Directory.GetCurrentDirectory());
            throw new ApplicationException();
        }

        return _cachedConfig;
    }


    public static void SetConfig(string key, string value, string data)
    {
        IniData ConfigIni;
        FileIniDataParser parser = new();
        ConfigIni = parser.ReadFile("config.ini");
        ConfigIni[key][value] = data;
        parser.WriteFile("config.ini", ConfigIni);

        _cachedConfig = ConfigIni;
        _cacheExpires = DateTime.UtcNow + CacheTtl;
    }


    public static DiscordColor GetEmbedColor()
    {
        var fallbackColor = "000000";
        string colorString;

        try
        {
            var colorConfig = GetConfig()["EmbedConfig"]["DefaultEmbedColor"];
            if (colorConfig.StartsWith('#')) colorConfig = colorConfig[1..];

            if (string.IsNullOrEmpty(colorConfig) || !HexCheck.IsHexColor(colorConfig))
            {
                colorString = fallbackColor;
                return new DiscordColor(colorString);
            }

            colorString = colorConfig;
        }
        catch
        {
            colorString = fallbackColor;
        }

        return new DiscordColor(colorString);
    }
}