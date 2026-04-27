namespace backend.Models;

public class Menu : BaseModel
{
    public string Name { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;

    public ICollection<SubMenu> SubMenus { get; set; } = new List<SubMenu>();
    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
}
