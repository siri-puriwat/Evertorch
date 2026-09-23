using System;
using Microsoft.EntityFrameworkCore.Design;

namespace Evertorch.Persistence
{
/// <summary>
///     Lets <c>dotnet ef</c> build the model without the server. Adding a migration needs no database; applying one
///     takes its connection from the <c>ConnectionStrings__Evertorch</c> environment variable, which
///     <c>scripts/db-migrate.ps1</c> sets, so the password never appears on a command line.
/// </summary>
internal sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<EvertorchDbContext>
{
    private const string ConnectionVariable = "ConnectionStrings__Evertorch";
    private const string PlaceholderConnection = "Host=127.0.0.1;Database=evertorch_design";

    public EvertorchDbContext CreateDbContext(string[] args)
    {
        string? connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        return new EvertorchDbContext(
            EvertorchDatabase.CreateOptions(string.IsNullOrWhiteSpace(connection)
                ? PlaceholderConnection
                : connection));
    }
}
}
