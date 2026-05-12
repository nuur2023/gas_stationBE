namespace gas_station.Models;

public class Expense : BaseModel
{
    /// <summary>Expense | cashOrUsdTaken | Exchange</summary>
    public string Type { get; set; } = "Expense";
    /// <summary>Operation | Management</summary>
    public string SideAction { get; set; } = "Operation";
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
    /// <summary>FK to <see cref="Currency"/> — determines whether amounts are in local lane vs USD.</summary>
    public int CurrencyId { get; set; }
    public double LocalAmount { get; set; }
    public double Rate { get; set; }
    public double AmountUsd { get; set; }
    public int UserId { get; set; }
    public int BusinessId { get; set; }
    /// <summary>
    /// Operation-side entries are scoped to a specific station; Management-side entries are
    /// recorded at the business level and store NULL here.
    /// </summary>
    public int? StationId { get; set; }
}
