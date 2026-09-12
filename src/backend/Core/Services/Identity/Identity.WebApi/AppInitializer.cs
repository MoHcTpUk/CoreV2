using Identity.WebApi.Security;
using OpenIddict.Abstractions;

namespace Identity.WebApi;

internal sealed class AppInitializer(IServiceProvider serviceProvider, IConfiguration configuration)
    : IHostedService
{
    /// <summary>
    ///     Разрешения клиента. Список пересобирается при каждом старте: клиент, заведённый до появления
    ///     grant-а <c>auth_request</c>, иначе остался бы без него, и вход по коду из письма возвращал бы
    ///     <c>unauthorized_client</c> на уже существующем стенде.
    /// </summary>
    private static readonly string[] RequiredPermissions =
    [
        OpenIddictConstants.Permissions.Endpoints.Token,
        OpenIddictConstants.Permissions.GrantTypes.Password,
        OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
        OpenIddictConstants.Permissions.Prefixes.GrantType + AuthRequestGrant.GrantType,
        "scp:openid",
        "scp:email",
        "scp:profile",
        "scp:roles"
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var clientId = configuration["OpenIddict:ClientId"] ?? "core-app";

        // Проверяем, существует ли клиент
        var existingClient = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);

        if (existingClient is null)
        {
            // Создаем нового клиента
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                DisplayName = "Core App",
                ClientType = OpenIddictConstants.ClientTypes.Public // Public client для мобильных приложений
            };

            foreach (var permission in RequiredPermissions) descriptor.Permissions.Add(permission);

            await applicationManager.CreateAsync(descriptor, cancellationToken);

            return;
        }

        var existingPermissions = await applicationManager.GetPermissionsAsync(existingClient, cancellationToken);
        var missing = RequiredPermissions.Except(existingPermissions, StringComparer.Ordinal).ToList();

        if (missing.Count == 0) return;

        var existingDescriptor = new OpenIddictApplicationDescriptor();
        await applicationManager.PopulateAsync(existingDescriptor, existingClient, cancellationToken);

        foreach (var permission in missing) existingDescriptor.Permissions.Add(permission);

        await applicationManager.UpdateAsync(existingClient, existingDescriptor, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
