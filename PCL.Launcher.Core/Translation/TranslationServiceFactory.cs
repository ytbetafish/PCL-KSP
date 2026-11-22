namespace PCL.Translation;

/// <summary>
///     翻译服务工厂。翻译功能只保留内置免费接口（腾讯交互翻译，失败回退 MyMemory），
///     已移除需要 API 密钥的 AI 翻译实现。
/// </summary>
public static class TranslationServiceFactory
{
    public static ITranslationService Create() => new FreeTranslationService();
}
