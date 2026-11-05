using System.ComponentModel;
using Gamma.Erp.Api.Auth;
using Gamma.Erp.Models.Base;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services;
using Gamma.Erp.Services.Documents;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>A right as Claude gives it: on one category of an entity, what the role or user may do there.</summary>
public sealed record RightLine(
    [property: Description("The entity: partners, products, accounts, units, cost-centres, users, employees, opportunities, documents, or a table name as role_access shows it.")] string Entity,
    [property: Description("The category of that entity: id, code or name.")] string Category,
    [property: Description("Sees every row of the category.")] bool ReadAll = false,
    [property: Description("Sees only the rows they made.")] bool ReadOwn = false,
    [property: Description("May add and change rows.")] bool Write = false,
    [property: Description("May send documents on (forward).")] bool Forward = false);

/// <summary>
/// Who sees which screens and may do what, as the Users register's Menu and Rights tabs set them: for a role (user category) or one user. Only
/// administrators, as on the screens.
/// </summary>
[McpServerToolType]
public sealed class AccessTools(ErpDbContext db, RequestContext request, Visibility visibility, MenuRightsService menus, AccessService access)
{
    private const string RoleHelp = "The role (user category): code, name or id. Give either a role or a user.";
    private const string UserHelp = "Or one user: id or exact code (or name).";

    [McpServerTool(Name = "role_access", Title = "A role's screens and rights", ReadOnly = true)]
    [Description("Reads what a role or a user was given: the whole menu the company offers (each node with code, name, parent and given: true when held; a node gives everything under it) and the rights per entity and category (read all, read own, write, forward). Administrators only.")]
    public Task<object> RoleAccessAsync([Description(RoleHelp)] string? role = null, [Description(UserHelp)] string? user = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsPlace(request);
            Registers.Administrator(request);
            var (userId, roleId) = await WhoAsync(role, user, ct).ConfigureAwait(false);
            var menu = await menus.RightsAsync(userId, roleId, Registers.English, ct).ConfigureAwait(false);
            var rights = await access.RightsAsync(userId, roleId, ct).ConfigureAwait(false);
            var named = new List<object>();
            foreach (var r in rights)
            {
                var category = (await CategoriesAsync(r.Table, ct).ConfigureAwait(false)).FirstOrDefault(c => c.Id == r.CategoryId);
                named.Add(new { entity = r.Table, r.CategoryId, category = category?.Code ?? category?.Name.In(Registers.English), r.ReadAll, r.ReadOwn, r.Write, r.Forward });
            }

            return new { menu = menu.Select(m => new { m.Id, m.Code, m.Name, parentId = m.ParentId, isScreen = m.IsElement, m.Given }), rights = named };
        });

    [McpServerTool(Name = "set_role_access", Title = "Set a role's screens and rights", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description("Sets what a role or a user is given, as the Users register's Menu and Rights tabs do: menu (the menu nodes, by code, name or id, that they hold: the whole list, others are taken away; a node gives everything under it) and/or rights (per entity and category: read all, read own, write, forward: the whole list, others are switched off). Leave one out to keep it as it is. Administrators only. Read role_access first; show the user the new lists and get an explicit yes; then confirm=true.")]
    public Task<object> SetRoleAccessAsync(
        [Description("True only after the administrator said yes.")] bool confirm,
        [Description(RoleHelp)] string? role = null,
        [Description(UserHelp)] string? user = null,
        [Description("Optional: the menu nodes they hold (codes, names or ids), the whole list.")] IReadOnlyList<string>? menu = null,
        [Description("Optional: their rights, the whole list.")] IReadOnlyList<RightLine>? rights = null, CancellationToken ct = default) =>
        Erp.Run<object>(async () =>
        {
            Erp.NeedsYes(confirm, "the new menu and rights of the role or user");
            Erp.NeedsPlace(request);
            Registers.Administrator(request);
            if (menu is null && rights is null)
            {
                throw new McpException("Give the menu, the rights, or both.");
            }

            var (userId, roleId) = await WhoAsync(role, user, ct).ConfigureAwait(false);
            List<Guid>? menuIds = null;
            if (menu is not null)
            {
                var offered = await menus.RightsAsync(userId, roleId, Registers.English, ct).ConfigureAwait(false);
                menuIds = [];
                foreach (var node in menu)
                {
                    var key = node.Trim();
                    var found = offered.Where(m => Registers.Is(m.Id, key) || string.Equals(m.Code, key, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (found.Count == 0)
                    {
                        found = [.. offered.Where(m => string.Equals(m.Name, key, StringComparison.OrdinalIgnoreCase))];
                    }

                    menuIds.Add(found.Count switch
                    {
                        1 => found[0].Id,
                        0 => throw new McpException($"There is no menu node \"{key}\" that can be given (role_access lists them)."),
                        _ => throw new McpException($"Several menu nodes match \"{key}\": give its id or code."),
                    });
                }
            }

            List<RightDto>? rightRows = null;
            if (rights is not null)
            {
                rightRows = [];
                foreach (var r in rights)
                {
                    var table = TableOf(r.Entity);
                    var categories = await CategoriesAsync(table, ct).ConfigureAwait(false);
                    var key = r.Category.Trim();
                    var found = categories.Where(c => Registers.Is(c.Id, key) || string.Equals(c.Code, key, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(c.Name.In(Registers.English), key, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (found.Count != 1)
                    {
                        throw new McpException($"{(found.Count == 0 ? "No" : "Several")} {r.Entity} categories match \"{key}\". Known: "
                            + string.Join(", ", categories.Where(c => c.Active).Select(c => c.Code ?? c.Name.In(Registers.English))) + ".");
                    }

                    rightRows.Add(new RightDto(table, found[0].Id, r.ReadAll, r.ReadOwn, r.Write, r.Forward));
                }
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            if (menuIds is not null)
            {
                await menus.SetAsync(userId, roleId, menuIds, ct).ConfigureAwait(false);
            }

            if (rightRows is not null)
            {
                await access.SetRightsAsync(userId, roleId, rightRows, ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return await RoleAccessAsync(role, user, ct).ConfigureAwait(false);
        });

    private async Task<(Guid? User, Guid? Role)> WhoAsync(string? role, string? user, CancellationToken ct)
    {
        var users = Registers.Users(db, request, visibility);
        if (string.IsNullOrWhiteSpace(role) == string.IsNullOrWhiteSpace(user))
        {
            throw new McpException("Give either a role or a user.");
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            return (null, Registers.Category(await users.CategoriesAsync(ct).ConfigureAwait(false), role, "role"));
        }

        return (await Registers.ElementAsync(s => users.LookupAsync(null, null, s, Registers.English, 1, Registers.MaxPage, ct), user!, "user").ConfigureAwait(false), null);
    }

    /// <summary>The table of an entity named by Claude: a register's word, or the table name itself.</summary>
    private string TableOf(string given)
    {
        var key = given.Trim().ToUpperInvariant().Replace(" ", "-", StringComparison.Ordinal).Replace("_", "-", StringComparison.Ordinal);
        Type? clr = key switch
        {
            "PARTNERS" or "PARTNER" or "CLIENTS" or "SUPPLIERS" => typeof(Gamma.Erp.Models.Partners.Partner),
            "PRODUCTS" or "PRODUCT" or "SERVICES" => typeof(Gamma.Erp.Models.Products.Product),
            "ACCOUNTS" or "ACCOUNT" or "CHART" or "CHARTS" => typeof(Gamma.Erp.Models.Charts.Chart),
            "UNITS" or "UNIT" => typeof(Gamma.Erp.Models.Units.Unit),
            "COST-CENTRES" or "COST-CENTERS" or "COST-CENTRE" or "COST-CENTER" or "PLACES" => typeof(Gamma.Erp.Models.CostCenters.CostCenter),
            "USERS" or "USER" => typeof(Gamma.Erp.Models.Users.User),
            "EMPLOYEES" or "EMPLOYEE" => typeof(Gamma.Erp.Models.Employees.Employee),
            "OPPORTUNITIES" or "OPPORTUNITY" => typeof(Gamma.Erp.Models.Opportunities.Opportunity),
            "DOCUMENTS" or "DOCUMENT" => typeof(Gamma.Erp.Models.Documents.Document),
            _ => null,
        };
        return clr is null ? given.Trim() : db.Model.FindEntityType(clr)?.GetTableName() ?? given.Trim();
    }

    /// <summary>The categories of the entity whose element table this is (its category table, found through the model, as the rights check finds it).</summary>
    private async Task<List<Category>> CategoriesAsync(string table, CancellationToken ct)
    {
        var element = db.Model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == table && typeof(Element).IsAssignableFrom(e.ClrType));
        var category = element is null ? null : db.Model.GetEntityTypes().FirstOrDefault(e => typeof(Category).IsAssignableFrom(e.ClrType)
            && e.ClrType.Namespace == element.ClrType.Namespace);
        if (category is null)
        {
            throw new McpException($"There is no entity \"{table}\" with categories.");
        }

        var set = (IQueryable<Category>)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(category.ClrType).Invoke(db, null)!;
        return await set.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
    }
}
