namespace backend.Models;

public class BusinessUser : BaseModel
{
    public int UserId { get; set; }
    public int BusinessId { get; set; }
    public int StationId { get; set; }

    public User User { get; set; } = null!;
    public Business Business { get; set; } = null!;
}
