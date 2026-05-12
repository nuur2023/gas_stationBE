namespace gas_station.Models;

public class Customer : BaseModel
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int StationId { get; set; }
    public int BusinessId { get; set; }

    public ICollection<CustomerFuelTransaction> FuelTransactions { get; set; }
        = new List<CustomerFuelTransaction>();
}

public class CustomerFuelTransaction : BaseModel
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    /// <summary>
    /// "Fuel" or "Cash"
    /// </summary>
    public string Type { get; set; } = "Fuel";

    public int FuelTypeId { get; set; }

    public double GivenLiter { get; set; }

    public double Price { get; set; }

    public double UsdAmount { get; set; }

    /// <summary>
    /// Local-currency cash advanced to customer
    /// </summary>
    public double CashAmount { get; set; }

    /// <summary>Currency for fuel price / cash advance display and reporting.</summary>
    public int CurrencyId { get; set; }

    public string? Remark { get; set; }

    public int StationId { get; set; }

    public int BusinessId { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    public ICollection<JournalEntryLine> JournalEntryLines { get; set; } = new List<JournalEntryLine>();
}

