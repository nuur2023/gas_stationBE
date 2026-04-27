namespace backend.ViewModels;

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
}
