using Gamma.Erp.Services;

namespace Gamma.Erp.Api;

/// <summary>
/// The services' refusals as problem details: 403 when the request lacks the right, 422 otherwise, with the reason per field (each starting
/// with its code, e.g. "StockError: …"), in the language the request names.
/// </summary>
public sealed class ErrorMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        try
        {
            await next(http).ConfigureAwait(false);
        }
        catch (ValidationException refused) when (!http.Response.HasStarted)
        {
            var status = refused.Errors.ContainsKey("rights") ? StatusCodes.Status403Forbidden : StatusCodes.Status422UnprocessableEntity;
            var language = http.Request.Query["language"].ToString();
            await Results.ValidationProblem(refused.Errors.ToDictionary(e => e.Key, e => new[] { Messages.Translate(e.Value, language) }), statusCode: status,
                title: status == StatusCodes.Status403Forbidden ? "Not allowed" : "Refused").ExecuteAsync(http).ConfigureAwait(false);
        }
    }
}
