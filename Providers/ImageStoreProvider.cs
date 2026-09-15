namespace AGC_Management.Providers;

public enum ImageStoreType
{
    Flag,
    Warn,
    ImageUpload
}

public static class ImageStoreProvider
{
    public static string GetFlagImageStorePath()
    {
        return BotConfig.GetConfig()["ImageStore"]["FlagImagePath"];
    }
    
    public static string GetWarnImageStorePath()
    {
        return BotConfig.GetConfig()["ImageStore"]["WarnImagePath"];
    }

    public static string GetImageStoreDomain()
    {
        return BotConfig.GetConfig()["ImageStore"]["Domain"];
    }
    
    private static string GetImageStoreFolder(ImageStoreType type)
    {
        return type switch
        {
            ImageStoreType.Flag => GetFlagImageStorePath(),
            ImageStoreType.Warn => GetWarnImageStorePath(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }


    public static string SaveModerativeImage(string fileName, byte[] image, ImageStoreType storeType)
    {
        var path = GetImageStoreFolder(storeType);
        var domain = GetImageStoreDomain();
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        var fullPath = Path.Combine(path, fileName);

        var foldername = storeType switch
        {
            ImageStoreType.Flag => "flag",
            ImageStoreType.Warn => "warn",
            _ => throw new ArgumentOutOfRangeException(nameof(storeType), storeType, null)
        };
        
        var prefix = "https://";
        File.WriteAllBytes(fullPath, image);
        return $"{prefix}{domain}/{foldername}/{fileName}";
    }
}