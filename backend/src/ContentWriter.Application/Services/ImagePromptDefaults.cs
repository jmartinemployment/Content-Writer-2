namespace ContentWriter.Application.Services;

public static class ImagePromptDefaults
{
    public const string DefaultImageModel = "Default";
    public const string DefaultImageModelId = "default";

    public const int PillarWidth = 1536;
    public const int PillarHeight = 1024;
    public const string PillarStylePreset = "Illustration";

    public const int SocialWidth = 1200;
    public const int SocialHeight = 630;
    public const string SocialStylePreset = "Dynamic";

    public const int PromptMinWords = 20;
    public const int PromptMaxWords = 400;
}
