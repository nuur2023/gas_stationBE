namespace backend.ViewModels;

public class GeneratorUsageViewModel
{
    public int Id { get; set; }
    public double LtrUsage { get; set; }
    public int UsersId { get; set; }
    public int BusinessId { get; set; }
    public int StationId { get; set; }
    public DateTime Date { get; set; }
}
