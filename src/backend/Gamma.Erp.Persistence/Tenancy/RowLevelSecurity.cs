using Microsoft.EntityFrameworkCore.Migrations;

namespace Gamma.Erp.Persistence.Tenancy;

/// <summary>Migration helpers that set up tenant isolation in PostgreSQL.</summary>
public static class RowLevelSecurity
{
    /// <summary>
    /// Creates the <c>gamma_erp_tenant</c> role (no login) that tenant requests switch to, and lets
    /// the connecting user switch to it. Needs a database user that may create roles.
    /// </summary>
    public static void CreateTenantRole(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql($"""
            DO $$
            BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{TenantConnectionInterceptor.TenantRole}') THEN
                CREATE ROLE {TenantConnectionInterceptor.TenantRole} NOLOGIN NOBYPASSRLS;
              END IF;
            END $$;
            GRANT {TenantConnectionInterceptor.TenantRole} TO CURRENT_USER;
            """);
    }

    /// <summary>Lets the tenant role use a schema.</summary>
    public static void GrantSchema(MigrationBuilder migrationBuilder, string schema)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql($"GRANT USAGE ON SCHEMA {schema} TO {TenantConnectionInterceptor.TenantRole};");
    }

    /// <summary>
    /// Enables row-level security on a table with a <c>tenant_id</c> column: tenant requests can only
    /// read, add and change their own rows, never delete them (nothing is ever deleted: deleting is active = false).
    /// The table owner (migrations, setup, login) is not restricted.
    /// </summary>
    public static void EnableForTenantTable(
        MigrationBuilder migrationBuilder, string schema, string table, string privileges = "SELECT, INSERT, UPDATE")
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql($"""
            ALTER TABLE {schema}.{table} ENABLE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON {schema}.{table}
              USING (tenant_id = NULLIF(current_setting('{TenantConnectionInterceptor.TenantSetting}', true), '')::uuid)
              WITH CHECK (tenant_id = NULLIF(current_setting('{TenantConnectionInterceptor.TenantSetting}', true), '')::uuid);
            GRANT {privileges} ON {schema}.{table} TO {TenantConnectionInterceptor.TenantRole};
            """);
    }

    /// <summary>Grants the tenant role access to a table that is shared by all tenants (no tenant_id).</summary>
    public static void GrantSharedTable(
        MigrationBuilder migrationBuilder, string schema, string table, string privileges = "SELECT")
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql($"GRANT {privileges} ON {schema}.{table} TO {TenantConnectionInterceptor.TenantRole};");
    }

    /// <summary>Lets the tenant role use the identity sequences of a schema (for bigint keys).</summary>
    public static void GrantSequences(MigrationBuilder migrationBuilder, string schema)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql($"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {schema} TO {TenantConnectionInterceptor.TenantRole};");
    }
}
