using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;

namespace Identity.WebApi.Security;

public static class DataProtectionExtensions
{
    /// <summary>
    /// Кладёт key ring DataProtection в хранилище, общее для всех реплик и переживающее рестарт.
    /// </summary>
    /// <param name="isDeveloperProfile">
    /// Профиль машины разработчика (Development/Local). Только он имеет право на локальный
    /// каталог с ключами; в остальных окружениях отсутствие общего хранилища — ошибка старта.
    /// </param>
    /// <param name="keyRingCertificate">
    /// Сертификат, которым key ring шифруется на диске/в Redis. В не-development окружениях
    /// это сертификат шифрования токенов; в development ключи лежат в открытом виде
    /// в профиле разработчика, что для локального стенда достаточно.
    /// </param>
    public static IServiceCollection AddIdentityDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDeveloperProfile,
        X509Certificate2? keyRingCertificate)
    {
        var settings = new DataProtectionSettings();
        configuration.GetSection(DataProtectionSettings.SectionName).Bind(settings);

        var builder = services.AddDataProtection()
            // Одно имя на все реплики — иначе у каждой получается собственный key ring.
            .SetApplicationName(settings.ApplicationName);

        var redisConnectionString = string.IsNullOrWhiteSpace(settings.RedisConnectionString)
            ? configuration.GetConnectionString("Redis")
            : settings.RedisConnectionString;

        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            var redisConfiguration = ConfigurationOptions.Parse(redisConnectionString);

            // Соединение поднимается лениво, при первом обращении к key ring, и не роняет
            // контейнер, если Redis в момент старта ещё не готов: переподключением
            // занимается сам мультиплексор.
            redisConfiguration.AbortOnConnectFail = false;

            var multiplexer = new Lazy<IConnectionMultiplexer>(
                () => ConnectionMultiplexer.Connect(redisConfiguration),
                LazyThreadSafetyMode.ExecutionAndPublication);

            builder.PersistKeysToStackExchangeRedis(() => multiplexer.Value.GetDatabase(), settings.RedisKey);
        }
        else if (!string.IsNullOrWhiteSpace(settings.KeysDirectory))
        {
            builder.PersistKeysToFileSystem(Directory.CreateDirectory(settings.KeysDirectory));
        }
        else if (isDeveloperProfile)
        {
            // Локальный запуск из IDE: Redis наружу не опубликован, ключи кладём в профиль
            // пользователя. Для одного процесса этого достаточно.
            var developerKeysPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Core",
                "DataProtection-Keys");

            builder.PersistKeysToFileSystem(Directory.CreateDirectory(developerKeysPath));
        }
        else
        {
            throw new InvalidOperationException(
                "DataProtection key ring storage is not configured. Set " +
                $"'{DataProtectionSettings.SectionName}:RedisConnectionString' (or the 'Redis' connection " +
                $"string) to the shared Redis instance, or '{DataProtectionSettings.SectionName}:KeysDirectory' " +
                "to a volume shared by every replica. Without shared storage every restart invalidates all " +
                "issued reference refresh tokens.");
        }

        if (keyRingCertificate is not null)
            builder
                .ProtectKeysWithCertificate(keyRingCertificate)
                // Расшифровать ключ надо тем же сертификатом: по умолчанию DataProtection
                // ищет его в X509-хранилище по отпечатку, а он приезжает файлом-секретом.
                .UnprotectKeysWithAnyCertificate(keyRingCertificate);

        return services;
    }
}
