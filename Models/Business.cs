namespace gas_station.Models;

public class Business : BaseModel
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? PhoneNumber { get; set; }

    /// <summary>When false, users linked to this business cannot sign in; active sessions are rejected on API calls.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When false, fuel pool / transfer features are hidden and blocked for this business.</summary>
    public bool IsSupportPool { get; set; } = true;

    public ICollection<BusinessUser> BusinessUsers { get; set; } = new List<BusinessUser>();
    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
}
