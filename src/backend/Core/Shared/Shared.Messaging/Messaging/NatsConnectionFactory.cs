using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
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
    ///     TLS включён по умолчанию и выключается только явным <c>NatsTlsEnabled=false</c>: по шине
    ///     ходят одноразовые ticket-ы на выпуск токенов, и открытый трафик внутри overlay-сети —
    ///     это перехватываемая сессия. Сертификат сервера проверяется как обычно, а
    ///     <c>NatsTlsCaFile</c> задаёт корневой сертификат для стендов с собственным CA
    ///     (deploy/generate-nats-certs.sh); без него используется системное хранилище доверия.
    /// </summary>
    public static IConnection Create(IConfiguration configuration)
    {
        var url = configuration["NatsUrl"];
        var user = configuration["NatsUser"];
        var password = configuration["NatsPassword"];
        var tlsEnabled = !bool.TryParse(configuration["NatsTlsEnabled"], out var tls) || tls;
        var tlsCaFile = configuration["NatsTlsCaFile"];

        var opts = ConnectionFactory.GetDefaultOptions();
        opts.Url = url;

        // Пустые значения не выставляем: тогда сервер вернёт "Authorization Violation",
        // и причина будет видна в логе, а не спрячется за пустым логином.
        if (!string.IsNullOrWhiteSpace(user))
            opts.User = user;

        if (!string.IsNullOrWhiteSpace(password))
            opts.Password = password;

        opts.Secure = tlsEnabled;

        if (tlsEnabled && !string.IsNullOrWhiteSpace(tlsCaFile))
            opts.TLSRemoteCertificationValidationCallback = BuildCustomRootValidation(tlsCaFile);

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

    /// <summary>
    ///     Проверка сертификата сервера против собственного корневого сертификата стенда.
    ///     Это НЕ отключение проверки: имя хоста, срок действия и подпись проверяются по-прежнему,
    ///     меняется только то, какому корню мы доверяем. Ошибки, не связанные с цепочкой
    ///     (несовпадение имени, отсутствие сертификата), остаются фатальными.
    /// </summary>
    private static RemoteCertificateValidationCallback BuildCustomRootValidation(string caFilePath)
    {
        if (!File.Exists(caFilePath))
            throw new FileNotFoundException(
                $"NATS TLS CA file '{caFilePath}' not found. Generate it with deploy/generate-nats-certs.sh " +
                "or clear NatsTlsCaFile to fall back to the system trust store.", caFilePath);

        var authorities = new X509Certificate2Collection();
        authorities.ImportFromPemFile(caFilePath);

        if (authorities.Count == 0)
            throw new InvalidOperationException($"NATS TLS CA file '{caFilePath}' contains no certificate.");

        return (_, certificate, _, errors) =>
        {
            if (errors == SslPolicyErrors.None) return true;

            // Доверяем только другому корню — всё остальное (нет сертификата, чужое имя хоста)
            // по-прежнему означает отказ.
            if ((errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0) return false;

            if (certificate is null) return false;

            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(authorities);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

            using var serverCertificate = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());

            return chain.Build(serverCertificate);
        };
    }
}
