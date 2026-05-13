namespace gas_station.ViewModels;

public sealed class AccountingDashboardOverviewDto
{
    public DateTime AsOfDate { get; set; }
    public int BusinessId { get; set; }
    public int? StationId { get; set; }

    public AccountingDashboardKpiDto Kpis { get; set; } = new();

    /// <summary>Revenue, expenses (COGS + operating expenses), profit for this month vs comparable slice of previous month.</summary>
    public AccountingDashboardPlCompareDto ProfitLossCompare { get; set; } = new();

    public AccountingDashboardCashFlowDto CashFlowThisMonth { get; set; } = new();

    public IReadOnlyList<AccountingDashboardCashTrendPointDto> CashTrend { get; set; } = Array.Empty<AccountingDashboardCashTrendPointDto>();

    public AccountingDashboardExpenseBreakdownDto ExpenseBreakdownThisMonth { get; set; } = new();

    public IReadOnlyList<AccountingDashboardRecentLineDto> RecentTransactions { get; set; } = Array.Empty<AccountingDashboardRecentLineDto>();

    public IReadOnlyList<AccountingDashboardAlertDto> Alerts { get; set; } = Array.Empty<AccountingDashboardAlertDto>();
}

public sealed class AccountingDashboardKpiDto
{
    /// <summary>Total revenue (income accounts) for the current month to date.</summary>
    public double TotalRevenue { get; set; }

    /// <summary>Net profit (ordinary income) for the current month to date.</summary>
    public double NetProfit { get; set; }

    /// <summary>COGS + operating expenses for the current month to date.</summary>
    public double TotalExpenses { get; set; }

    public double CashBalance { get; set; }
    public double BankBalance { get; set; }
    public double InventoryValue { get; set; }
}

public sealed class AccountingDashboardPlCompareDto
{
    public AccountingDashboardPlBarDto ThisMonth { get; set; } = new();
    public AccountingDashboardPlBarDto PreviousMonth { get; set; } = new();
}

public sealed class AccountingDashboardPlBarDto
{
    public string Label { get; set; } = "";
    public double Revenue { get; set; }
    public double Expenses { get; set; }
    public double Profit { get; set; }
}

public sealed class AccountingDashboardCashFlowDto
{
    public double OperatingCashFlow { get; set; }
    public double InvestingCashFlow { get; set; }
    public double FinancingCashFlow { get; set; }
    public double NetCashChange { get; set; }
}

public sealed class AccountingDashboardCashTrendPointDto
{
    public string Label { get; set; } = "";
    public double NetCashChange { get; set; }
}

public sealed class AccountingDashboardExpenseBreakdownDto
{
    public double Salaries { get; set; }
    public double Rent { get; set; }
    public double Utilities { get; set; }
    public double Supplies { get; set; }
    public double Other { get; set; }
}

public sealed class AccountingDashboardRecentLineDto
{
    public int JournalEntryId { get; set; }
    public string Kind { get; set; } = "";
    public DateTime Date { get; set; }
    public string Account { get; set; } = "";
    public string? AccountCode { get; set; }
    public double Amount { get; set; }
    public string? Description { get; set; }
}

public sealed class AccountingDashboardRecentTransactionsPagedDto
{
    public IReadOnlyList<AccountingDashboardRecentLineDto> Items { get; set; } = Array.Empty<AccountingDashboardRecentLineDto>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public sealed class AccountingDashboardAlertDto
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string Severity { get; set; } = "warning";
}
