using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Evertorch.Persistence
{
/// <summary>
///     Sorts database failures into "the database is unreachable or slow" and everything else, so the server can
///     retry the first kind without ever seeing a Npgsql or EF Core type.
/// </summary>
internal static class StoreFailures
{
    public static bool IsUnavailable(Exception exception, CancellationToken cancellationToken)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            switch (current)
            {
                case OperationCanceledException:
                    return cancellationToken.IsCancellationRequested;
                case TimeoutException:
                case SocketException:
                case IOException:
                    return true;
                case PostgresException postgres:
                    return postgres.IsTransient;
                case NpgsqlException npgsql when npgsql.IsTransient:
                    return true;
                case DbUpdateException:
                    continue;
            }
        }

        return false;
    }
}
}
