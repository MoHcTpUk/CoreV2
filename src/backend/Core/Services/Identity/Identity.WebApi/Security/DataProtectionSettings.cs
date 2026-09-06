namespace Identity.WebApi.Security;

/// <summary>
/// Где живёт key ring DataProtection. От него зависят reference refresh tokens
/// (<c>UseReferenceRefreshTokens()</c>): ключи обязаны переживать рестарт контейнера
/// и быть общими для всех реплик, иначе токен, выданный репликой A, не расшифрует реплика B.
/// </summary>
public sealed class DataProtectionSettings
{
    public const string SectionName = "DataProtection";

    /// <summary>
    /// Общее имя приложения. Реплики делят один key ring только при совпадающем значении —
    /// по умолчанию ASP.NET Core берёт путь к контенту, который у каждой реплики свой.
    /// </summary>
    public string ApplicationName { get; set; } = "core-app";

    /// <summary>
    /// Строка подключения к Redis (`redis:6379`). Redis уже входит в стек инфраструктуры
    /// и является основным хранилищем ключей для docker-стендов и stage.
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>Ключ Redis, под которым лежит key ring.</summary>
    public string RedisKey { get; set; } = "core:identity:data-protection-keys";

    /// <summary>
    /// Альтернатива Redis: каталог на СМОНТИРОВАННОМ томе, общем для всех реплик.
    /// Используется, только если Redis не сконфигурирован.
    /// </summary>
    public string? KeysDirectory { get; set; }
}
