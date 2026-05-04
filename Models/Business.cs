namespace gas_station.Models;

public class Business : BaseModel
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? PhoneNumber { get; set; }

    public ICollection<BusinessUser> BusinessUsers { get; set; } = new List<BusinessUser>();
    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
}
