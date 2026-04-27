namespace backend.ViewModels;

public class ExpenseViewModel
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = "";
    public string CurrencyCode { get; set; } = "USD";
    public double LocalAmount { get; set; }
    public double Rate { get; set; }
    public double AmountUsd { get; set; }
    public int UserId { get; set; }
    public int BusinessId { get; set; }
    public int StationId { get; set; }
}
