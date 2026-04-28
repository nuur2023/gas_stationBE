namespace gas_station.ViewModels;

public class UserViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public int RoleId { get; set; }
}
