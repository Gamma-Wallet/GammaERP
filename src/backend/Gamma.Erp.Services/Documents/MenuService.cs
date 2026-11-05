using Gamma.Erp.Models.Platform;
using Gamma.Erp.Services.Entities;
using Microsoft.EntityFrameworkCore;

namespace Gamma.Erp.Services.Documents;

/// <summary>The users and roles a document type is given to, exactly these.</summary>
public sealed record DocumentTypeUsersRequest(IReadOnlyList<Guid>? UserIds, IReadOnlyList<Guid>? RoleIds);

/// <summary>A document type a documents item lists.</summary>
public sealed record MenuDocumentTypeDto(Guid Id, string? Code, string? Name);

/// <summary>A node of the signed-in user's menu, in the language asked for, with the nodes under it.</summary>
public sealed record MenuNodeDto(Guid Id, string? Code, string? Name, bool IsElement, string? Screen, string? Parameter, Guid? DocumentCategoryId, string? Icon,
    IReadOnlyList<MenuDocumentTypeDto> DocumentTypes, IReadOnlyList<MenuNodeDto> Children);

/// <summary>
/// The menu of the request's company as the signed-in user may see it. An administrator sees every node. Anyone else sees the nodes given to them or to one of their roles, everything under
/// those, and the folders above them. A documents item lists its category's document types the user was given, live: a new type shows without a menu change. A documents item with no type and a folder with nothing
/// under it are left out.
/// </summary>
public sealed class MenuService(Visibility visibility, DocumentService documents)
{
    public async Task<IReadOnlyList<MenuNodeDto>> MenuAsync(string language, CancellationToken ct)
    {
        var (menu, shown) = await visibility.MenuAsync(ct).ConfigureAwait(false);
        var ids = menu.Select(m => m.Id).ToHashSet();
        var children = menu.ToLookup(m => m.IdSup is { } p && ids.Contains(p) ? p : (Guid?)null);

        var types = new Dictionary<Guid, IReadOnlyList<MenuDocumentTypeDto>>();
        foreach (var category in menu.Where(m => shown.Contains(m.Id) && m.DocumentCategoryId is not null).Select(m => m.DocumentCategoryId!.Value).Distinct())
        {
            types[category] = [.. (await documents.AllowedTypesAsync(category, ct).ConfigureAwait(false)).Where(t => t.IdSup is null)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Code, StringComparer.Ordinal)
                .Select(t => new MenuDocumentTypeDto(t.Id!.Value, t.Code, t.Name.GetValueOrDefault(language) ?? t.Name.GetValueOrDefault("en")))];
        }

        List<MenuNodeDto> Build(Guid? parent) => [.. children[parent].Where(m => shown.Contains(m.Id)).OrderBy(m => m.SortOrder).ThenBy(m => m.Code, StringComparer.Ordinal)
            .Select(m => (Node: m, Under: Build(m.Id), Types: m.DocumentCategoryId is { } c ? types[c] : []))
            .Where(x => x.Node.IsElement ? x.Node.DocumentCategoryId is null || x.Types.Count > 0 : x.Under.Count > 0)
            .Select(x => new MenuNodeDto(x.Node.Id, x.Node.Code, x.Node.Name?.In(language), x.Node.IsElement, x.Node.Screen, x.Node.Parameter, x.Node.DocumentCategoryId,
                x.Node.Icon, x.Types, x.Under))];
        return Build(null);
    }
}
