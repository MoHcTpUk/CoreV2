namespace Identity.WebApi.Security;

/// <summary>
/// Источник X509-сертификата для подписи/шифрования токенов.
/// Сам сертификат в репозиторий не попадает: в конфигурации лежит только ПУТЬ к файлу
/// (в swarm это docker secret, смонтированный в <c>/run/secrets/...</c>) либо отпечаток
/// записи в X509-хранилище. Пароль приходит из файла-секрета (<see cref="PasswordFile"/>)
/// или из переменной окружения (<see cref="Password"/>), но никогда из appsettings*.json.
/// </summary>
public sealed class CertificateOptions
{
    /// <summary>Путь к файлу PKCS#12 (.pfx/.p12) с закрытым ключом.</summary>
    public string? Path { get; set; }

    /// <summary>Пароль к PKCS#12. Задаётся переменной окружения, не конфигом в git.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// Файл с паролем к PKCS#12 (docker secret). Приоритетнее <see cref="Password"/>:
    /// пароль не проходит через переменные окружения и не виден в `docker inspect`.
    /// </summary>
    public string? PasswordFile { get; set; }

    /// <summary>Отпечаток сертификата в X509-хранилище. Альтернатива <see cref="Path"/>.</summary>
    public string? Thumbprint { get; set; }

    /// <summary>Имя X509-хранилища (значение <c>StoreName</c> или произвольное имя). Нужно только вместе с <see cref="Thumbprint"/>.</summary>
    public string StoreName { get; set; } = "My";

    /// <summary>Расположение X509-хранилища: <c>CurrentUser</c> или <c>LocalMachine</c>.</summary>
    public string StoreLocation { get; set; } = "CurrentUser";
}
