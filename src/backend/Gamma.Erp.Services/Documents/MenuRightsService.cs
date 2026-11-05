using Gamma.Erp.Models.Platform;
using Gamma.Erp.Models.Users;
using Gamma.Erp.Persistence;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Documents;

/// <summary>A node of the company's menu as an administrator gives it: under which node, and whether this user or role was given it.</summary>
public sealed record MenuRightDto(Guid Id, Guid? ParentId, string? Code, string? Name, string? Icon, bool IsElement, bool Given);

/// <summary>
/// The whole menu, and what a user or role was given of it. Only an administrator reads or changes it: to anyone else the menu is only what
/// they were given. A node marked for administrators is not offered, because it is never shown to anyone else.
/// </summary>
public sealed class MenuRightsService(ErpDbContext db, RequestContext request, Visibility visibility)
{
    public async Task<IReadOnlyList<MenuRightDto>> RightsAsync(Guid? userId, Guid? roleId, string language, CancellationToken ct)
    {
        Administrator();
        var menu = await OfferedAsync(ct).ConfigureAwait(false);
        var given = (await LinksAsync(userId, roleId, tracked: false, ct).ConfigureAwait(false)).Select(l => l.MenuId).ToHashSet();
        var ids = menu.Select(m => m.Id).ToHashSet();
        return [.. menu.OrderBy(m => m.SortOrder).ThenBy(m => m.Code, StringComparer.Ordinal)
            .Select(m => new MenuRightDto(m.Id, m.IdSup is { } up && ids.Contains(up) ? up : null, m.Code, m.Name?.In(language), m.Icon, m.IsElement, given.Contains(m.Id)))];
    }

    /// <summary>Makes the user or role hold exactly these nodes of the company's menu; a node gives everything under it.</summary>
    public async Task SetAsync(Guid? userId, Guid? roleId, IReadOnlyList<Guid> menuIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(menuIds);
        Administrator();
        var offered = (await OfferedAsync(ct).ConfigureAwait(false)).ToDictionary(m => m.Id);
        if (menuIds.FirstOrDefault(id => !offered.ContainsKey(id)) is var unknown && unknown != Guid.Empty)
        {
            throw ValidationException.For("menu", $"{ErrorCodes.NotAllowedError}: this is not a node that can be given.");
        }

        var links = await LinksAsync(userId, roleId, tracked: true, ct).ConfigureAwait(false);
        var wanted = menuIds.ToHashSet();
        db.RemoveRange(links.Where(l => l.MenuId is not { } id || !wanted.Contains(id)));
        var have = links.Select(l => l.MenuId).ToHashSet();
        db.AddRange(wanted.Where(id => !have.Contains(id)).Select(id => new InterMenuUser { MenuId = id, UserId = userId, RoleId = roleId, TenantId = request.TenantId, OwnerId = offered[id].OwnerId }));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private void Administrator()
    {
        if (!request.IsAdministrator)
        {
            throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: only an administrator sees the whole menu.");
        }
    }

    private async Task<List<Menu>> OfferedAsync(CancellationToken ct)
    {
        var (menu, _) = await visibility.MenuAsync(ct).ConfigureAwait(false);
        return [.. menu.Where(m => !m.AdminOnly)];
    }

    // The company's links of this user or role: another company's menu stays as it is.
    private async Task<List<InterMenuUser>> LinksAsync(Guid? userId, Guid? roleId, bool tracked, CancellationToken ct)
    {
        var ids = (await OfferedAsync(ct).ConfigureAwait(false)).Select(m => (Guid?)m.Id).ToList();
        var links = db.Set<InterMenuUser>().Where(l => ids.Contains(l.MenuId) && (userId == null ? l.RoleId == roleId : l.UserId == userId));
        return await (tracked ? links : links.AsNoTracking()).ToListAsync(ct).ConfigureAwait(false);
    }
}
