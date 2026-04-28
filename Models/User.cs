namespace gas_station.Models;

public class User : BaseModel
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    public int RoleId { get; set; }

    /// <summary>Navigation property; omitted on API write bodies — use <see cref="RoleId"/> only.</summary>
    public Role? Role { get; set; }
    public ICollection<BusinessUser> BusinessUsers { get; set; } = new List<BusinessUser>();
    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
}
