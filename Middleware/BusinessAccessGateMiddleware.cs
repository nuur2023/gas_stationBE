using System.Security.Claims;
using gas_station.Data.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Middleware;

/// <summary>
/// Blocks authenticated business-scoped users when their business is inactive (SuperAdmin bypasses).
/// </summary>
public sealed class BusinessAccessGateMiddleware(RequestDelegate next)
{
    private const string SuperAdminRole = "SuperAdmin";

    public async Task InvokeAsync(HttpContext context, GasStationDBContext db)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() != null)
        {
            await next(context);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        if (context.User.IsInRole(SuperAdminRole))
        {
            await next(context);
            return;
        }

        var bidStr = context.User.FindFirstValue("business_id");
        if (string.IsNullOrEmpty(bidStr) || !int.TryParse(bidStr, out var bid) || bid <= 0)
        {
            await next(context);
            return;
        }

        var isActive = await db.Businesses.AsNoTracking()
            .Where(b => b.Id == bid && !b.IsDeleted)
            .Select(b => b.IsActive)
            .FirstOrDefaultAsync();

        // Missing business row: treat as inactive for safety.
        if (!isActive)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "business_inactive",
                message = "This business is inactive. You have been signed out.",
            });
            return;
        }

        await next(context);
    }
}
