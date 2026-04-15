using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.AchievementBadges.Api;

public class UserOwnershipFilter : IAsyncActionFilter
{
    private readonly IAuthorizationService _authService;

    public UserOwnershipFilter(IAuthorizationService authService)
    {
        _authService = authService;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Skip if no userId route param
        if (!context.RouteData.Values.TryGetValue("userId", out var routeUserIdObj) || routeUserIdObj is not string routeUserId)
        {
            await next();
            return;
        }

        // Skip if action is [AllowAnonymous] or [RequiresElevation]
        var endpoint = context.HttpContext.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() != null)
        {
            await next();
            return;
        }

        // Admin passes through
        var adminAuth = await _authService.AuthorizeAsync(context.HttpContext.User, null, "RequiresElevation");
        if (adminAuth.Succeeded)
        {
            await next();
            return;
        }

        // Get caller's user ID from claim
        var claimUserId = context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value;
        if (string.IsNullOrEmpty(claimUserId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // Compare GUIDs structurally (handles case differences)
        if (System.Guid.TryParse(claimUserId, out var callerGuid) && System.Guid.TryParse(routeUserId, out var routeGuid))
        {
            if (callerGuid != routeGuid)
            {
                context.Result = new ForbidResult();
                return;
            }
        }
        else if (!string.Equals(claimUserId, routeUserId, System.StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new ForbidResult();
            return;
        }

        await next();
    }
}
