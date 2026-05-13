namespace gas_station.ViewModels;

public class AuthResponseViewModel
{
    public string AccessToken { get; set; } = "";
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    public int? BusinessId { get; set; }
    public int? StationId { get; set; }

    /// <summary>True when login failed because the linked business is inactive (non–SuperAdmin).</summary>
    public bool BusinessInactive { get; set; }

    /// <summary>Fuel pool / transfer features are available for the signed-in business.</summary>
    public bool IsSupportPool { get; set; } = true;
}
