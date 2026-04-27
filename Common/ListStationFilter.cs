using System.Security.Claims;

namespace backend.Common;

/// <summary>
/// Resolves station filter for paged lists. When the client sends <paramref name="filterStationId"/> (workspace
/// selection from Settings), it wins for every non-SuperAdmin role so list endpoints stay aligned with the UI.
/// If no explicit filter is sent, falls back to JWT <c>station_id</c> when present.
/// </summary>
public static class ListStationFilter
{
    public static int? ForNonSuperAdmin(ClaimsPrincipal user, int? filterStationId)
    {
        static bool TryJwtStation(ClaimsPrincipal u, out int sid)
        {
            sid = 0;
            var s = u.FindFirstValue("station_id");
            return !string.IsNullOrEmpty(s) && int.TryParse(s, out sid);
        }

        if (filterStationId is > 0)
            return filterStationId.Value;

        if (TryJwtStation(user, out var jwt) && jwt > 0)
            return jwt;

        return null;
    }
}
