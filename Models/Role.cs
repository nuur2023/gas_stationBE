namespace backend.Models;

public class Role : BaseModel
{
    public string Name { get; set; } = string.Empty;
    public ICollection<User> Users { get; set; } = new List<User>();
}
