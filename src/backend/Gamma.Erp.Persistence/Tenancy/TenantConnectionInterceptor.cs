using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Gamma.Erp.Persistence.Tenancy;

/// <summary>
/// Restricts every database connection to the current tenant.
///
/// When the request has a tenant, the connection switches to the <c>gamma_erp_tenant</c> role and
/// stores the tenant id in the <c>app.tenant_id</c> setting. Row-level-security policies on every
/// tenant table compare against that setting, so a query can only ever see and change the current
/// company's rows, even if application code forgets a filter.
///
/// Without a tenant (start-up migrations, setup, login) the connection keeps the owner role.
/// </summary>
public sealed class TenantConnectionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    public const string TenantRole = "gamma_erp_tenant";
    public const string TenantSetting = "app.tenant_id";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";

        if (tenant.TenantId is { } tenantId)
        {
            parameter.Value = tenantId.ToString();
            command.CommandText = $"SELECT set_config('{TenantSetting}', @tenant, false); SET ROLE {TenantRole};";
        }
        else
        {
            parameter.Value = string.Empty;
            command.CommandText = $"RESET ROLE; SELECT set_config('{TenantSetting}', @tenant, false);";
        }

        command.Parameters.Add(parameter);
        return command;
    }
}
