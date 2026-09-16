using Microsoft.Extensions.Configuration;
using NATS.Client;

namespace Shared.Messaging.Messaging;

public static class NatsConnectionFactory
{
    /// <summary>
    ///     Создаёт подключение к NATS.
    ///     Шина требует аутентификации (deploy/nats/nats-server.conf), поэтому логин и пароль
    ///     обязательны на всех стендах. Значения приходят из конфигурации:
    ///     <list type="bullet">
    ///         <item>ключи <c>NatsUser</c> / <c>NatsPassword</c> (appsettings или переменные окружения);</item>
    ///         <item>либо прямо в <c>NatsUrl</c> в виде <c>nats://user:password@host:4222</c>.</item>
    ///     </list>
    ///     TLS на шине не используется: она доступна только внутри доверенной overlay-сети стенда
    ///     и наружу не публикуется.
    /// </summary>
    public static IConnection Create(IConfiguration configuration)
    {
        var url = configuration["NatsUrl"];
        var user = configuration["NatsUser"];
        var password = configuration["NatsPassword"];

        var opts = ConnectionFactory.GetDefaultOptions();
        opts.Url = url;

        // Пустые значения не выставляем: тогда сервер вернёт "Authorization Violation",
        // и причина будет видна в логе, а не спрячется за пустым логином.
        if (!string.IsNullOrWhiteSpace(user))
            opts.User = user;

        if (!string.IsNullOrWhiteSpace(password))
            opts.Password = password;

        const int maxAttempts = 10;
        const int delaySeconds = 5;

        for (var i = 1; i <= maxAttempts; i++)
            try
            {
                return new ConnectionFactory().CreateConnection(opts);
            }
            catch (NATSConnectionException ex)
            {
                Console.WriteLine($"[NATS][{url}] Attempt {i} of {maxAttempts} failed: {ex.Message}");
                if (i == maxAttempts)
                    throw;

                Thread.Sleep(TimeSpan.FromSeconds(delaySeconds));
            }

        throw new InvalidOperationException("Unreachable code");
    }
}
