using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;
using Shared.Domain.Interfaces;

namespace Shared.Infrastructure;

public sealed class EfCoreDatabaseMigrator<TContext>(
    TContext dbContext,
    ILogger<EfCoreDatabaseMigrator<TContext>> logger) : IDatabaseMigrator
    where TContext : DbContext
{
    // Совокупно ~50 с: docker stack deploy не ждёт готовности postgres перед стартом сервисов.
    private const int ConnectRetryCount = 10;
    private static readonly TimeSpan ConnectRetryDelay = TimeSpan.FromSeconds(5);

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDatabaseExistsAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private Task EnsureDatabaseExistsAsync(CancellationToken cancellationToken)
    {
        var databaseName = dbContext.Database.GetDbConnection().Database;

        return Policy
            .Handle<NpgsqlException>(e => e.IsTransient)
            .Or<SocketException>()
            .WaitAndRetryAsync(ConnectRetryCount, _ => ConnectRetryDelay, (exception, delay, attempt, _) =>
                logger.LogWarning(exception,
                    "PostgreSQL is not reachable yet (database {Database}), retry {Attempt}/{RetryCount} in {Delay}",
                    databaseName, attempt, ConnectRetryCount, delay))
            .ExecuteAsync(async ct =>
            {
                var creator = dbContext.GetService<IRelationalDatabaseCreator>();
                if (await creator.ExistsAsync(ct)) return;

                try
                {
                    await creator.CreateAsync(ct);
                    logger.LogInformation("Database {Database} created", databaseName);
                }
                catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.DuplicateDatabase)
                {
                    // Базу одновременно создал другой процесс с той же строкой подключения
                    // (Identity.Service и Identity.WebApi, реплики) — она уже есть, это не ошибка.
                }
            }, cancellationToken);
    }
}
