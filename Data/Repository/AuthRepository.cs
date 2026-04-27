using System.IdentityModel.Tokens.Jwt;

using System.Security.Claims;

using System.Text;

using backend.Common;

using backend.Data.Context;

using backend.Data.Interfaces;

using backend.ViewModels;

using Microsoft.EntityFrameworkCore;

using Microsoft.IdentityModel.Tokens;



namespace backend.Data.Repository;



public class AuthRepository(GasStationDBContext context, IConfiguration configuration) : IAuthRepository

{

    private readonly GasStationDBContext _context = context;

    private readonly IConfiguration _configuration = configuration;



    public async Task<AuthResponseViewModel?> LoginAsync(LoginRequestViewModel model)

    {

        var key = model.EmailOrPhone.Trim();

        if (string.IsNullOrEmpty(key))

        {

            return null;

        }



        var keyLower = key.ToLowerInvariant();



        var user = await _context.Users

            .Include(x => x.Role)

            .Include(x => x.BusinessUsers)

            .FirstOrDefaultAsync(x =>

                !x.IsDeleted &&

                (x.Email.ToLower() == keyLower || x.Phone == key));



        if (user is null)

        {

            var digits = DigitsOnly(key);

            if (digits.Length >= 6)

            {

                var candidates = await _context.Users

                    .Include(x => x.Role)

                    .Include(x => x.BusinessUsers)

                    .Where(x => !x.IsDeleted && x.Phone != null && x.Phone != string.Empty)

                    .ToListAsync();

                user = candidates.FirstOrDefault(x => DigitsOnly(x.Phone) == digits);

            }

        }



        if (user is null || !PasswordHasher.Verify(model.Password, user.PasswordHash))

        {

            return null;

        }



        // Deterministic primary link when a user has multiple business–station rows (same business).
        var bu = user.BusinessUsers
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.StationId)
            .ThenBy(x => x.Id)
            .FirstOrDefault();

        int? businessId = bu?.BusinessId;

        int? stationId = bu is { StationId: > 0 } ? bu.StationId : null;



        var token = GenerateJwtToken(user.Id, user.Name, user.Role!.Name, businessId, stationId);



        return new AuthResponseViewModel
        {
            AccessToken = token,
            UserId = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role!.Name,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(GetAccessTokenMinutes()),
            BusinessId = businessId,
            StationId = stationId,
        };

    }



    private static string DigitsOnly(string s)

    {

        return string.Concat(s.Where(char.IsDigit));

    }



    private int GetAccessTokenMinutes()

    {

        if (int.TryParse(_configuration["Jwt:ExpiresInMinutes"], out var fromExpires))

            return fromExpires;

        if (int.TryParse(_configuration["Jwt:AccessTokenMinutes"], out var fromAccess))

            return fromAccess;

        return 720;

    }



    private string GenerateJwtToken(int userId, string displayName, string role, int? businessId, int? stationId)

    {

        var issuer = _configuration["Jwt:Issuer"] ?? "GasStation";

        var audience = _configuration["Jwt:Audience"] ?? "SchoolMS";

        var key = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key is missing.");



        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));

        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);



        var claims = new List<Claim>

        {

            new(JwtRegisteredClaimNames.Sub, userId.ToString()),

            new(JwtRegisteredClaimNames.UniqueName, displayName),

            new(ClaimTypes.NameIdentifier, userId.ToString()),

            new(ClaimTypes.Name, displayName),

            new(ClaimTypes.Role, role)

        };



        if (businessId.HasValue)

        {

            claims.Add(new Claim("business_id", businessId.Value.ToString()));

        }



        if (stationId.HasValue)

        {

            claims.Add(new Claim("station_id", stationId.Value.ToString()));

        }



        var token = new JwtSecurityToken(

            issuer: issuer,

            audience: audience,

            claims: claims,

            expires: DateTime.UtcNow.AddMinutes(GetAccessTokenMinutes()),

            signingCredentials: credentials

        );



        return new JwtSecurityTokenHandler().WriteToken(token);

    }

}


