namespace gas_station.ViewModels;

public class LoginRequestViewModel
{
    /// <summary>User's email or phone number (trimmed; email match is case-insensitive).</summary>
    public string EmailOrPhone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
