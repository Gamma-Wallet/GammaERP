namespace Gamma.Erp.Services;

/// <summary>
/// Who is asking and where: the corporate, the user, the roles the user holds (user categories), and the node of the cost centre tree
/// the user works at. The node decides what is seen: its own rows and those of the nodes below it, never above.
/// An administrator has every right, but still sees only below the node.
/// </summary>
public sealed record RequestContext(Guid TenantId, Guid? UserId, Guid NodeId, IReadOnlySet<Guid> RoleIds, bool IsAdministrator = false);

/// <summary>A request the rules refuse, with the reason per field, each starting with its code (<see cref="ErrorCodes"/>).</summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string> errors)
    : Exception(string.Join("; ", errors.Select(e => $"{e.Key}: {e.Value}")))
{
    public IReadOnlyDictionary<string, string> Errors { get; } = errors;

    public static ValidationException For(string field, string message) => new(new Dictionary<string, string> { [field] = message });
}
