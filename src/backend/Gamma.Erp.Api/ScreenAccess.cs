using Gamma.Erp.Services;
using Gamma.Erp.Services.Entities;

namespace Gamma.Erp.Api;

/// <summary>A screen's routes answer whoever has that screen in their menu (Rights › Menu): the menu is the rule on the server too. Administrators pass.</summary>
public static class ScreenAccess
{
    private const string Report = "report";

    /// <summary>Any of these screens opens the route.</summary>
    public static TBuilder RequireScreen<TBuilder>(this TBuilder route, params string[] screens) where TBuilder : IEndpointConventionBuilder =>
        route.Require(screens, []);

    /// <summary>Any of these reports (the report screen's parameter) opens the route: a report and its PDF and Excel go together.</summary>
    public static TBuilder RequireReport<TBuilder>(this TBuilder route, params string[] reports) where TBuilder : IEndpointConventionBuilder =>
        route.Require([Report], reports);

    /// <summary>Any of these screens, with one of these parameters, opens the route: e.g. the Set up screen of one register.</summary>
    public static TBuilder RequireScreenWith<TBuilder>(this TBuilder route, string[] screens, string[] parameters) where TBuilder : IEndpointConventionBuilder =>
        route.Require(screens, parameters);

    /// <summary>Only an administrator opens the route.</summary>
    public static TBuilder RequireAdministrator<TBuilder>(this TBuilder route) where TBuilder : IEndpointConventionBuilder =>
        route.AddEndpointFilter(async (context, next) =>
        {
            if (!context.HttpContext.RequestServices.GetRequiredService<RequestContext>().IsAdministrator)
            {
                throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: only an administrator does this.");
            }

            return await next(context).ConfigureAwait(false);
        });

    private static TBuilder Require<TBuilder>(this TBuilder route, string[] screens, string[] parameters) where TBuilder : IEndpointConventionBuilder =>
        route.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!await http.RequestServices.GetRequiredService<Visibility>().OpensScreenAsync(screens, parameters, http.RequestAborted).ConfigureAwait(false))
            {
                throw ValidationException.For("rights", $"{ErrorCodes.NotAllowedError}: this screen is not in your menu.");
            }

            return await next(context).ConfigureAwait(false);
        });
}
