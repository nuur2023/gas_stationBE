namespace backend.Models;

public class SubMenu : BaseModel
{
    public int MenuId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;

    /// <summary>Navigation property; omitted on API write bodies — use <see cref="MenuId"/> only.</summary>
    public Menu? Menu { get; set; }
    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
}
