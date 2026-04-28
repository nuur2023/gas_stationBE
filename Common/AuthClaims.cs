using System.Security.Claims;

namespace gas_station.Common;

public static class AuthClaims
{
    public static bool TryGetUserAndBusiness(ClaimsPrincipal user, out int userId, out int businessId)
    {
        userId = 0;
        businessId = 0;
        var uid = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var bid = user.FindFirstValue("business_id");
        if (string.IsNullOrEmpty(uid) || !int.TryParse(uid, out userId))
        {
            return false;
        }

        if (string.IsNullOrEmpty(bid) || !int.TryParse(bid, out businessId))
        {
            return false;
        }

        return true;
    }
}
