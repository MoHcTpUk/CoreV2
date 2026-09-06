using System.Security.Cryptography.X509Certificates;

namespace Identity.WebApi.Security;

/// <summary>
/// Загружает реальный сертификат подписи/шифрования для не-development окружений.
/// Молчаливого отката на development-сертификат здесь нет: если материала нет или он
/// непригоден, метод бросает исключение, и приложение падает на старте — это лучше,
/// чем поднявшийся Stage, который раздаёт токены, умирающие при первом рестарте.
/// </summary>
public static class CertificateLoader
{
    /// <param name="configuration">Корневая конфигурация.</param>
    /// <param name="sectionPath">Секция с <see cref="CertificateOptions"/>, например <c>OpenIddict:SigningCertificate</c>.</param>
    /// <param name="purpose">Человекочитаемое назначение для текста ошибки, например <c>token signing</c>.</param>
    public static X509Certificate2 Load(IConfiguration configuration, string sectionPath, string purpose)
    {
        var options = new CertificateOptions();
        configuration.GetSection(sectionPath).Bind(options);

        var certificate = LoadCore(options, sectionPath, purpose);

        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException(
                $"The {purpose} certificate resolved from '{sectionPath}' has no private key. " +
                "Supply a PKCS#12 (.pfx) bundle that contains the private key.");

        return certificate;
    }

    private static X509Certificate2 LoadCore(CertificateOptions options, string sectionPath, string purpose)
    {
        if (!string.IsNullOrWhiteSpace(options.Path))
            return LoadFromFile(options, sectionPath, purpose);

        if (!string.IsNullOrWhiteSpace(options.Thumbprint))
            return LoadFromStore(options, sectionPath, purpose);

        throw new InvalidOperationException(
            $"No {purpose} certificate is configured. Set '{sectionPath}:Path' to a PKCS#12 file " +
            $"(a docker secret mounted at /run/secrets/..., password via '{sectionPath}:PasswordFile' or " +
            $"'{sectionPath}:Password'), or '{sectionPath}:Thumbprint' to a certificate in the X509 store. " +
            "Development certificates are only generated when ASPNETCORE_ENVIRONMENT/DOTNET_ENVIRONMENT " +
            "is Development or Local.");
    }

    private static X509Certificate2 LoadFromFile(CertificateOptions options, string sectionPath, string purpose)
    {
        var path = options.Path!;

        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"The {purpose} certificate file '{path}' ('{sectionPath}:Path') does not exist. " +
                "On stage the file comes from a docker secret — check that the secret is created " +
                "(`docker secret create ...`) and attached to the identity-webapi service.");

        var password = ResolvePassword(options, sectionPath);

        try
        {
            return X509CertificateLoader.LoadPkcs12FromFile(path, password);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"The {purpose} certificate file '{path}' ('{sectionPath}:Path') could not be read as PKCS#12. " +
                $"Check the file format and the password ('{sectionPath}:PasswordFile' / '{sectionPath}:Password').",
                exception);
        }
    }

    private static X509Certificate2 LoadFromStore(CertificateOptions options, string sectionPath, string purpose)
    {
        var thumbprint = options.Thumbprint!.Replace(" ", string.Empty);

        if (!Enum.TryParse<StoreLocation>(options.StoreLocation, ignoreCase: true, out var storeLocation))
            throw new InvalidOperationException(
                $"'{sectionPath}:StoreLocation' has an unknown value '{options.StoreLocation}'. " +
                "Expected CurrentUser or LocalMachine.");

        using var store = Enum.TryParse<StoreName>(options.StoreName, ignoreCase: true, out var storeName)
            ? new X509Store(storeName, storeLocation)
            : new X509Store(options.StoreName, storeLocation);

        store.Open(OpenFlags.ReadOnly);

        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);

        if (found.Count == 0)
            throw new InvalidOperationException(
                $"No {purpose} certificate with thumbprint '{thumbprint}' ('{sectionPath}:Thumbprint') was found " +
                $"in store {options.StoreName}/{options.StoreLocation}.");

        return found[0];
    }

    private static string? ResolvePassword(CertificateOptions options, string sectionPath)
    {
        if (!string.IsNullOrWhiteSpace(options.PasswordFile))
        {
            if (!File.Exists(options.PasswordFile))
                throw new InvalidOperationException(
                    $"The certificate password file '{options.PasswordFile}' ('{sectionPath}:PasswordFile') does not exist.");

            // Docker secret обычно заканчивается переводом строки — он не часть пароля.
            return File.ReadAllText(options.PasswordFile).Trim('\r', '\n', ' ', '\t');
        }

        return string.IsNullOrEmpty(options.Password) ? null : options.Password;
    }
}
