using System.Collections.Concurrent;

namespace Gamma.Erp.Api.Mcp;

/// <summary>
/// Where each user works through Claude (the founder, 2026-11-04): the cost centre chosen with the choose_workplace tool, kept while the server runs.
/// The screens send it as the X-Gamma-Node header; Claude chooses it once. A user with one workplace never has to choose.
/// </summary>
public sealed class McpWorkplaces
{
    private readonly ConcurrentDictionary<Guid, Guid> chosen = new();

    public Guid? Of(Guid userId) => chosen.TryGetValue(userId, out var node) ? node : null;

    public void Choose(Guid userId, Guid node) => chosen[userId] = node;
}
