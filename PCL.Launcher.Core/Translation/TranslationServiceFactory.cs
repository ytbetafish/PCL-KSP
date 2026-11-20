namespace PCL.Translation;

/// <summary>
///     翻译服务工厂：配置了 API 密钥时走 OpenAI 兼容接口，未配置时自动使用免注册的免费接口。
/// </summary>
public static class TranslationServiceFactory
{
    public static ITranslationService Create(TranslationOptions options)
    {
        if (options is not null && !string.IsNullOrWhiteSpace(options.ApiKey))
            return new OpenAiTranslationService(options);
        return new FreeTranslationService();
    }
}
