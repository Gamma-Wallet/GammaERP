using Gamma.Erp.Services;
using ModelContextProtocol;

namespace Gamma.Erp.Api.Mcp.Tools;

/// <summary>
/// What every tool shares: the ERP's own refusals reach Claude word for word (the SDK shows only an MCP exception's text), a tool that needs a place
/// says so when none is chosen, and a change waits for the user's yes.
/// </summary>
internal static class Erp
{
    /// <summary>Runs a tool's work; a refusal of the ERP becomes the tool's error, with the ERP's message.</summary>
    public static async Task<T> Run<T>(Func<Task<T>> work)
    {
        try
        {
            return await work().ConfigureAwait(false);
        }
        catch (ValidationException refused)
        {
            throw new McpException(string.Join(" ", refused.Errors.Select(e => $"{e.Key}: {e.Value}")));
        }
    }

    /// <summary>Refuses when no place is chosen (a user with several and no choice yet).</summary>
    public static void NeedsPlace(RequestContext request)
    {
        if (request.NodeId == Guid.Empty)
        {
            throw new McpException("Choose where to work first: list_workplaces, then choose_workplace.");
        }
    }

    /// <summary>Refuses a change the user has not said yes to.</summary>
    public static void NeedsYes(bool confirm, string what)
    {
        if (!confirm)
        {
            throw new McpException($"Not done: show the user {what} and call again with confirm=true after their explicit yes.");
        }
    }
}
