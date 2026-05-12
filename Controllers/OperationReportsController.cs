using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using gas_station.Data.Context;
using gas_station.Data.Repository;
using gas_station.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Controllers;

public record CashOutDailyLineDto(
    int Id,
    string Date,
    string Kind,
    string Description,
    string CurrencyCode,
    double LocalAmount,
    double Rate,
    double AmountUsd,
    int? StationId);

public record CashOutDailyReportDto(
    IReadOnlyList<CashOutDailyLineDto> Lines,
    double TotalCashOut,
    double TotalCashOutUsd);

/// <summary>
/// Central daily summary (same rules as the SPA General Daily Report): petrol/diesel sales only,
/// previous balance from all sales before the range minus cumulative cash-out through the day before <c>from</c>.
/// </summary>
public record DailySummaryReportDto(
    double SalesLocal,
    double SalesSspToUsd,
    double SalesUsd,
    double PeriodFinalUsd,
    double PreviousBalanceLocal,
    double PreviousBalanceUsd,
    double PreviousBalanceSspToUsd,
    double TotalLocal,
    double TotalSspToUsd,
    double TotalUsd,
    double OutLocal,
    double OutUsd,
    double OutAsUsd,
    double BalanceLocal,
    double BalanceUsd,
    double FinalBalanceUsd,
    CashOutDailyReportDto PeriodCashOut);

public record DailyFuelGivenRowDto(
    int Id,
    string Date,
    int StationId,
    int FuelTypeId,
    string FuelTypeName,
    string Name,
    double Price,
    double TotalLiters,
    double TotalAmount,
    double UsdAmount,
    int TransactionCount);

public record DailyStationAmountRowDto(
    double Amount,
    string Description,
    string Date);

public record DailyStationExchangeRowDto(
    double AmountSsp,
    double Rate,
    double Usd,
    string Date);

public record DailyStationCashTakenRowDto(
    double AmountSsp,
    double AmountUsd,
    string Date);

public record DailyStationFuelRowDto(
    string Type,
    double LitersSold,
    double Ssp,
    double Usd,
    double InDipping,
    string Date);

public record DailyStationFuelPriceDto(
    double PetrolSsp,
    double DieselSsp,
    double PetrolUsd,
    double DieselUsd);

/// <summary>
/// Per calendar day and recording user: how many distinct employees were paid and total amount.
/// Scoped to the business only (not filtered by station), like office expense/exchange sections on the daily station report.
/// </summary>
public record DailyStationSalaryPaymentRowDto(
    int Employees,
    double Amount,
    string RecordedBy,
    string Date);

public record DailyStationReportDto(
    string StationName,
    string From,
    string To,
    DailyStationFuelPriceDto FuelPrices,
    IReadOnlyList<DailyStationAmountRowDto> ExpenseFromStation,
    IReadOnlyList<DailyStationFuelRowDto> FuelReport,
    IReadOnlyList<DailyStationExchangeRowDto> ExchangeFromStation,
    IReadOnlyList<DailyStationCashTakenRowDto> CashTakenFromStation,
    IReadOnlyList<DailyStationAmountRowDto> ExpenseFromOffice,
    IReadOnlyList<DailyStationExchangeRowDto> ExchangeFromOffice,
    IReadOnlyList<DailyStationSalaryPaymentRowDto> SalaryPayments);

public record SupplierReportRowDto(
    int Id,
    string Name,
    string Description,
    double? Liters,
    double Amount,
    double Paid,
    double Balance,
    string Date,
    int? PurchaseId,
    string? ReferenceNo);

public record SupplierReportDto(
    string From,
    string To,
    int? SupplierId,
    string? SupplierName,
    IReadOnlyList<SupplierReportRowDto> Rows,
    double TotalCharged,
    double TotalPaid,
    double Balance);

public record CustomerReportRowDto(
    int Id,
    int CustomerId,
    string Name,
    string Phone,
    string Description,
    string? Type,
    int? FuelTypeId,
    string? FuelTypeName,
    double? Liters,
    double? Price,
    double CashTaken,
    double Charged,
    double Paid,
    double Balance,
    string Date,
    string? ReferenceNo);

public record CustomerReportDto(
    string From,
    string To,
    int CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    IReadOnlyList<CustomerReportRowDto> Rows,
    double TotalCharged,
    double TotalCashTaken,
    double TotalLiters,
    double TotalPaid,
    double Balance);

public record EmployeeOptionDto(
    int Id,
    string Name,
    string Phone,
    string Position,
    double BaseSalary,
    int? StationId,
    bool HasSalaryForPeriod);

public record EmployeePaymentHistoryRowDto(
    int Id,
    string Date,
    string Description,
    string? PeriodLabel,
    double Charged,
    double Paid,
    double Balance,
    string? ReferenceNo,
    int? StationId);

public record EmployeePaymentHistoryDto(
    string From,
    string To,
    int EmployeeId,
    string EmployeeName,
    string EmployeePhone,
    string EmployeePosition,
    double BaseSalary,
    IReadOnlyList<EmployeePaymentHistoryRowDto> Rows,
    double TotalCharged,
    double TotalPaid,
    double OutstandingBalance);

public record PayrollEmployeeStatusRowDto(
    int EmployeeId,
    string Name,
    string Phone,
    string Position,
    int? StationId,
    double BaseSalary,
    double TotalCharged,
    double TotalPaid,
    double Balance,
    string? LastPaymentDate);

public record PayrollStatusReportDto(
    int BusinessId,
    string Period,
    int? StationId,
    IReadOnlyList<PayrollEmployeeStatusRowDto> Paid,
    IReadOnlyList<PayrollEmployeeStatusRowDto> Unpaid);

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OperationReportsController(GasStationDBContext db) : ControllerBase
{
    private const string SuperAdminRole = "SuperAdmin";
    private static bool IsSuperAdmin(ClaimsPrincipal user) => user.IsInRole(SuperAdminRole);

    private bool TryGetJwtBusiness(out int businessId)
    {
        businessId = 0;
        var bidStr = User.FindFirstValue("business_id");
        return !string.IsNullOrEmpty(bidStr) && int.TryParse(bidStr, out businessId);
    }

    private bool TryGetJwtStation(out int stationId)
    {
        stationId = 0;
        var s = User.FindFirstValue("station_id");
        return !string.IsNullOrEmpty(s) && int.TryParse(s, out stationId);
    }

    /// <summary>
    /// SuperAdmin: optional query station (null = all). Others: explicit <paramref name="queryStationId"/>
    /// wins when valid for the business (so mobile pickers work); JWT <c>station_id</c> only pins scope for
    /// users who are not Admin/Manager/Accountant. If no query station, fall back to JWT station when set.
    /// </summary>
    private async Task<(int? StationFilter, IActionResult? Error)> ResolveReportStationAsync(int businessId, int? queryStationId)
    {
        if (IsSuperAdmin(User))
            return queryStationId is > 0 ? (queryStationId, null) : (null, null);

        var jwtPinned = TryGetJwtStation(out var js) && js > 0 ? js : (int?)null;

        if (queryStationId is > 0)
        {
            var st = await db.Stations.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == queryStationId.Value && !x.IsDeleted);
            if (st is null || st.BusinessId != businessId)
                return (null, BadRequest("Invalid station for this business."));

            if (jwtPinned is int pinned
                && pinned != queryStationId.Value
                && !IsBusinessWideStationScope(User))
                return (null, Forbid());

            return (queryStationId, null);
        }

        if (jwtPinned is int j)
            return (j, null);

        return (null, null);
    }

    private static bool IsBusinessWideStationScope(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.IsInRole("Manager") || user.IsInRole("Accountant");

    private bool ResolveBusiness(int queryBusinessId, out int bid, out IActionResult? err)
    {
        bid = 0;
        err = null;
        if (IsSuperAdmin(User))
        {
            if (queryBusinessId <= 0)
            {
                err = BadRequest("businessId is required.");
                return false;
            }

            bid = queryBusinessId;
            return true;
        }

        if (!TryGetJwtBusiness(out var jwtBid))
        {
            err = BadRequest("No business assigned to this user.");
            return false;
        }

        if (queryBusinessId > 0 && queryBusinessId != jwtBid)
        {
            err = Forbid();
            return false;
        }

        bid = jwtBid;
        return true;
    }

    /// <summary>
    /// Interprets the first yyyy-MM-dd from a query (<c>2026-05-08</c> or ISO <c>2026-05-08T12:34:56</c>)
    /// as the user's calendar date, without UTC model-binding shifting the day.
    /// </summary>
    private static bool TryParseCalendarDateQuery(string? value, out DateTime dayLocal)
    {
        dayLocal = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var s = value.Trim();
        ReadOnlySpan<char> head = s.Length >= 10 ? s.AsSpan(0, 10) : s.AsSpan();

        if (head.Length != 10 ||
            head[4] != '-' ||
            head[7] != '-' ||
            !int.TryParse(head.Slice(0, 4), out var y) ||
            !int.TryParse(head.Slice(5, 2), out var mo) ||
            !int.TryParse(head.Slice(8, 2), out var dom))
            return false;

        try
        {
            dayLocal = new DateTime(y, mo, dom, 0, 0, 0, DateTimeKind.Unspecified);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string ClassifyCashOutKind(Expense e)
    {
        if (string.Equals(e.Type, "cashOrUsdTaken", StringComparison.OrdinalIgnoreCase))
            return "Cash or USD Taken";
        if (string.Equals(e.Type, "Exchange", StringComparison.OrdinalIgnoreCase))
            return "Exchange";
        if (string.Equals(e.Type, "Expense", StringComparison.OrdinalIgnoreCase))
            return "Expense";
        var d = e.Description ?? string.Empty;
        if (Regex.IsMatch(d, @"cash\s*taken", RegexOptions.IgnoreCase))
            return "Cash taken";
        if (Regex.IsMatch(d, @"exchange", RegexOptions.IgnoreCase))
            return "Exchange";
        if (e.Rate > 1e-9 || Math.Abs(e.AmountUsd) > 1e-9)
            return "Exchange";
        return "Expense";
    }

    private async Task<CashOutDailyReportDto> BuildCashOutDailyReportAsync(
        int bid,
        int? stationFilter,
        DateTime? from,
        DateTime? to,
        string? expenseType = null,
        string? sideAction = null)
    {
        var q = db.Expenses.AsNoTracking().Where(x => !x.IsDeleted && x.BusinessId == bid);
        if (from.HasValue)
            q = q.Where(x => x.Date >= from.Value.Date);
        if (to.HasValue)
        {
            var toExclusive = to.Value.Date.AddDays(1);
            q = q.Where(x => x.Date < toExclusive);
        }

        // Management entries are business-level; ignore the station filter for them so they
        // surface regardless of which station the user is viewing.
        if (stationFilter is > 0 && !string.Equals(sideAction, "Management", StringComparison.OrdinalIgnoreCase))
            q = q.Where(x => x.StationId == stationFilter.Value);
        if (!string.IsNullOrWhiteSpace(expenseType))
            q = q.Where(x => x.Type == expenseType);
        if (!string.IsNullOrWhiteSpace(sideAction))
            q = q.Where(x => x.SideAction == sideAction);

        var items = await q
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Id)
            .ToListAsync();

        var curCodes = await db.Currencies.AsNoTracking()
            .Where(c => !c.IsDeleted)
            .ToDictionaryAsync(c => c.Id, c => (c.Code ?? "USD").Trim());

        var lines = items.Select(e => new CashOutDailyLineDto(
            e.Id,
            e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ClassifyCashOutKind(e),
            e.Description,
            curCodes.TryGetValue(e.CurrencyId, out var cc) ? cc : "USD",
            e.LocalAmount,
            e.Rate,
            e.AmountUsd,
            e.StationId)).ToList();

        var total = lines.Sum(x => x.LocalAmount);
        var totalUsd = lines.Sum(x => x.AmountUsd);
        return new CashOutDailyReportDto(lines, total, totalUsd);
    }

    private static (double LocalNonUsd, double UsdCurrencyOnly, double NonUsdAsUsd) SumCashOutLineSplits(
        IReadOnlyList<CashOutDailyLineDto> lines)
    {
        double localNonUsd = 0;
        double usdCurrencyOnly = 0;
        double nonUsdAsUsd = 0;
        foreach (var r in lines)
        {
            var code = (r.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
            if (code == "USD")
                usdCurrencyOnly += r.AmountUsd;
            else
            {
                localNonUsd += r.LocalAmount;
                nonUsdAsUsd += r.AmountUsd;
            }
        }

        return (localNonUsd, usdCurrencyOnly, nonUsdAsUsd);
    }

    /// <summary>Petrol/diesel classification aligned with the SPA <c>detectFuelKind</c> (General Daily Report).</summary>
    private static bool IsPetrolOrDieselFuelRow(string fuelName)
    {
        var n = (fuelName ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(n))
            return false;
        if (n.Contains("diesel", StringComparison.Ordinal) ||
            n.Contains("gasoil", StringComparison.Ordinal) ||
            n.Contains("ago", StringComparison.Ordinal) ||
            Regex.IsMatch(n, @"\bd2\b", RegexOptions.IgnoreCase))
            return true;
        if (n.Contains("petrol", StringComparison.Ordinal) ||
            n.Contains("gasoline", StringComparison.Ordinal) ||
            n.Contains("pms", StringComparison.Ordinal) ||
            n.Contains("mogas", StringComparison.Ordinal) ||
            n.Contains("benzene", StringComparison.Ordinal) ||
            n.Contains("unleaded", StringComparison.Ordinal) ||
            n.Contains("super", StringComparison.Ordinal) ||
            n == "gas" ||
            Regex.IsMatch(n, @"\bgas\b", RegexOptions.IgnoreCase))
            return true;
        return false;
    }

    /// <summary>Expense rows for cash-out ledger: local amount, optional rate / USD (exchange), total cash out = sum of local amounts.</summary>
    [HttpGet("cash-out-daily")]
    public async Task<IActionResult> CashOutDaily(
        [FromQuery] int businessId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int? stationId = null,
        [FromQuery] string? expenseType = null,
        [FromQuery] string? sideAction = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err))
            return err!;

        var (stationFilter, stationErr) = await ResolveReportStationAsync(bid, stationId);
        if (stationErr != null)
            return stationErr;

        return Ok(await BuildCashOutDailyReportAsync(bid, stationFilter, from, to, expenseType, sideAction));
    }

    /// <summary>
    /// Single source for the Daily Summary block (previous balance, period sales, cash-out lines, footer balances).
    /// </summary>
    [HttpGet("daily-summary-report")]
    public async Task<IActionResult> DailySummaryReport(
        [FromQuery] int businessId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] int? stationId = null,
        [FromQuery] string? sideAction = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err))
            return err!;

        var (stationFilter, stationErr) = await ResolveReportStationAsync(bid, stationId);
        if (stationErr != null)
            return stationErr;

        var fromDay = from.Date;
        var toDay = to.Date;
        if (fromDay > toDay)
            return BadRequest("from must be on or before to.");

        var nozzleIds = await db.Nozzles.AsNoTracking()
            .Where(n => !n.IsDeleted && n.BusinessId == bid &&
                        (!stationFilter.HasValue || stationFilter.Value <= 0 ||
                         n.StationId == stationFilter.Value))
            .Select(n => n.Id)
            .ToListAsync();
        var nozzleIdSet = nozzleIds.ToHashSet();

        var pumpLinks = await (
            from dp in db.DippingPumps.AsNoTracking()
            join d in db.Dippings.AsNoTracking() on dp.DippingId equals d.Id
            join ft in db.FuelTypes.AsNoTracking() on d.FuelTypeId equals ft.Id
            where !dp.IsDeleted && !d.IsDeleted && !ft.IsDeleted && nozzleIdSet.Contains(dp.NozzleId)
            select new { dp.NozzleId, FuelName = ft.FuelName ?? string.Empty }
        ).ToListAsync();

        var fuelByNozzle = new Dictionary<int, string>();
        foreach (var pl in pumpLinks)
        {
            if (!fuelByNozzle.ContainsKey(pl.NozzleId))
                fuelByNozzle[pl.NozzleId] = pl.FuelName;
        }

        var invQuery =
            from it in db.InventoryItems.AsNoTracking()
            join s in db.InventorySales.AsNoTracking() on it.InventorySaleId equals s.Id
            where !it.IsDeleted && !s.IsDeleted && s.BusinessId == bid
            select new { it.NozzleId, it.Date, it.SspAmount, it.UsdAmount, it.ExchangeRate, s.StationId };

        if (stationFilter is > 0)
            invQuery = invQuery.Where(x => x.StationId == stationFilter);

        var invRows = await invQuery.ToListAsync();

        double periodSsp = 0, periodUsd = 0, periodSspToUsd = 0, periodFinal = 0;
        double prevSsp = 0, prevUsd = 0, prevSspToUsd = 0;
        foreach (var row in invRows)
        {
            if (!nozzleIdSet.Contains(row.NozzleId))
                continue;
            if (!fuelByNozzle.TryGetValue(row.NozzleId, out var fuelName) || !IsPetrolOrDieselFuelRow(fuelName))
                continue;

            var day = row.Date.Date;
            var ssp = row.SspAmount;
            var usd = row.UsdAmount;
            var rate = row.ExchangeRate;
            var s2U = rate > 1e-9 ? ssp / rate : 0;
            if (day >= fromDay && day <= toDay)
            {
                periodSsp += ssp;
                periodUsd += usd;
                periodSspToUsd += s2U;
                periodFinal += usd + s2U;
            }
            else if (day < fromDay)
            {
                prevSsp += ssp;
                prevUsd += usd;
                prevSspToUsd += s2U;
            }
        }

        var periodCashOut = await BuildCashOutDailyReportAsync(bid, stationFilter, fromDay, toDay, null, sideAction);
        var previousTo = fromDay.AddDays(-1);
        var previousCashOut = await BuildCashOutDailyReportAsync(bid, stationFilter, null, previousTo, null, sideAction);

        var prevOut = SumCashOutLineSplits(previousCashOut.Lines);
        var periodOut = SumCashOutLineSplits(periodCashOut.Lines);

        var previousBalanceLocal = prevSsp - prevOut.LocalNonUsd;
        var prevSspSales = prevSsp;
        var previousBalanceSspToUsd = prevSspSales > 1e-9
            ? prevSspToUsd * (Math.Max(0, previousBalanceLocal) / prevSspSales)
            : 0;
        var previousBalanceUsd = prevUsd - prevOut.UsdCurrencyOnly;

        var salesLocal = periodSsp;
        var salesSspToUsd = periodSspToUsd;
        var salesUsd = periodUsd;
        var periodFinalUsd = periodFinal;

        var totalLocal = salesLocal + previousBalanceLocal;
        var totalSspToUsd = salesSspToUsd + previousBalanceSspToUsd;
        var totalUsd = salesUsd + previousBalanceUsd;
        var finalBalanceUsd = totalUsd + totalSspToUsd - periodOut.UsdCurrencyOnly;

        var balanceLocal = totalLocal - periodOut.LocalNonUsd;
        var balanceUsd = totalUsd - periodOut.UsdCurrencyOnly;

        var dto = new DailySummaryReportDto(
            SalesLocal: salesLocal,
            SalesSspToUsd: salesSspToUsd,
            SalesUsd: salesUsd,
            PeriodFinalUsd: periodFinalUsd,
            PreviousBalanceLocal: previousBalanceLocal,
            PreviousBalanceUsd: previousBalanceUsd,
            PreviousBalanceSspToUsd: previousBalanceSspToUsd,
            TotalLocal: totalLocal,
            TotalSspToUsd: totalSspToUsd,
            TotalUsd: totalUsd,
            OutLocal: periodOut.LocalNonUsd,
            OutUsd: periodOut.UsdCurrencyOnly,
            OutAsUsd: periodOut.NonUsdAsUsd,
            BalanceLocal: balanceLocal,
            BalanceUsd: balanceUsd,
            FinalBalanceUsd: finalBalanceUsd,
            PeriodCashOut: periodCashOut);

        return Ok(dto);
    }

    /// <summary>Customer fuel given aggregated by calendar day, station, and fuel type.</summary>
    [HttpGet("daily-fuel-given")]
    public async Task<IActionResult> DailyFuelGiven(
        [FromQuery] int businessId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int? stationId = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err))
            return err!;

        var (stationFilter, stationErr) = await ResolveReportStationAsync(bid, stationId);
        if (stationErr != null)
            return stationErr;

        var q =
            from g in db.CustomerFuelGivens.AsNoTracking()
            join ft in db.FuelTypes.AsNoTracking() on g.FuelTypeId equals ft.Id
            where !g.IsDeleted && !ft.IsDeleted && g.BusinessId == bid
            select new { g, ft.FuelName };

        if (from.HasValue)
            q = q.Where(x => x.g.Date >= from.Value.Date);
        if (to.HasValue)
        {
            var toExclusive = to.Value.Date.AddDays(1);
            q = q.Where(x => x.g.Date < toExclusive);
        }
        if (stationFilter is > 0)
            q = q.Where(x => x.g.StationId == stationFilter.Value);

        var raw = await q.ToListAsync();

        var rows = raw
            .Select(x =>
            {
                var liters = x.g.GivenLiter;
                var amount = liters * x.g.Price;
                return new DailyFuelGivenRowDto(
                    x.g.Id,
                    x.g.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    x.g.StationId,
                    x.g.FuelTypeId,
                    x.FuelName,
                    x.g.Customer.Name ?? string.Empty,
                    x.g.Price,
                    liters,
                    amount,
                    x.g.UsdAmount,
                    1);
            })
            .OrderByDescending(x => x.Date)
            .ThenBy(x => x.StationId)
            .ThenBy(x => x.Id)
            .ToList();

        return Ok(rows);
    }

    /// <summary>
    /// Daily station report block sections for PDF/UI.
    /// Defaults to current date when from/to are omitted.
    /// If stationId is omitted, first station in business is used.
    /// </summary>
    [HttpGet("daily-station-report")]
    public async Task<IActionResult> FetchDailyStationReport(
        [FromQuery] int businessId,
        [FromQuery] int? stationId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err))
            return err!;

        var fromDay = (from ?? DateTime.UtcNow).Date;
        var toDay = (to ?? DateTime.UtcNow).Date;
        if (fromDay > toDay)
            return BadRequest("from must be on or before to.");

        var (resolvedStationFilter, stationErr) = await ResolveReportStationAsync(bid, stationId);
        if (stationErr != null)
            return stationErr;

        var effectiveStationId = resolvedStationFilter;
        if (!effectiveStationId.HasValue || effectiveStationId.Value <= 0)
        {
            effectiveStationId = await db.Stations.AsNoTracking()
                .Where(s => !s.IsDeleted && s.BusinessId == bid)
                .OrderBy(s => s.Id)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync();
        }
        if (!effectiveStationId.HasValue || effectiveStationId.Value <= 0)
            return BadRequest("No station found for this business.");

        var selectedStation = await db.Stations.AsNoTracking()
            .FirstOrDefaultAsync(s => !s.IsDeleted && s.BusinessId == bid && s.Id == effectiveStationId.Value);
        if (selectedStation is null)
            return BadRequest("Invalid station for this business.");

        var fromInclusive = fromDay;
        var toExclusive = toDay.AddDays(1);
        var stationIdVal = effectiveStationId.Value;

        var expenseCurrencyCodes = await db.Currencies.AsNoTracking()
            .Where(c => !c.IsDeleted)
            .ToDictionaryAsync(c => c.Id, c => (c.Code ?? "").Trim());
        bool IsUsdExpense(Expense e) =>
            expenseCurrencyCodes.TryGetValue(e.CurrencyId, out var code) &&
            string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase);

        var fuelPriceRows = await (
            from fp in db.FuelPrices.AsNoTracking()
            join ft in db.FuelTypes.AsNoTracking() on fp.FuelTypeId equals ft.Id
            join c in db.Currencies.AsNoTracking() on fp.CurrencyId equals c.Id
            where !fp.IsDeleted && !ft.IsDeleted && !c.IsDeleted
                  && fp.BusinessId == bid && fp.StationId == stationIdVal
            orderby fp.Id descending
            select new { ft.FuelName, CurrencyCode = c.Code, fp.Price }
        ).ToListAsync();

        static bool IsPetrol(string name) =>
            !string.IsNullOrWhiteSpace(name) &&
            (name.Contains("petrol", StringComparison.OrdinalIgnoreCase)
             || name.Contains("gasoline", StringComparison.OrdinalIgnoreCase)
             || name.Contains("pms", StringComparison.OrdinalIgnoreCase)
             || name.Contains("mogas", StringComparison.OrdinalIgnoreCase));
        static bool IsDiesel(string name) =>
            !string.IsNullOrWhiteSpace(name) &&
            (name.Contains("diesel", StringComparison.OrdinalIgnoreCase)
             || name.Contains("gasoil", StringComparison.OrdinalIgnoreCase)
             || name.Contains("ago", StringComparison.OrdinalIgnoreCase));

        double petrolSsp = 0, dieselSsp = 0, petrolUsd = 0, dieselUsd = 0;
        foreach (var row in fuelPriceRows)
        {
            var cur = (row.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
            if (IsPetrol(row.FuelName))
            {
                if (cur == "USD" && petrolUsd == 0) petrolUsd = row.Price;
                if (cur != "USD" && petrolSsp == 0) petrolSsp = row.Price;
            }
            else if (IsDiesel(row.FuelName))
            {
                if (cur == "USD" && dieselUsd == 0) dieselUsd = row.Price;
                if (cur != "USD" && dieselSsp == 0) dieselSsp = row.Price;
            }
            if (petrolSsp > 0 && dieselSsp > 0 && petrolUsd > 0 && dieselUsd > 0) break;
        }

        var stationExpenses = await db.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.BusinessId == bid
                        && e.StationId == stationIdVal
                        && e.Date >= fromInclusive && e.Date < toExclusive)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Id)
            .ToListAsync();

        var officeExpenses = await db.Expenses.AsNoTracking()
            .Where(e => !e.IsDeleted && e.BusinessId == bid
                        && e.SideAction == "Management"
                        && e.Date >= fromInclusive && e.Date < toExclusive)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Id)
            .ToListAsync();

        var expenseFromStation = stationExpenses
            .Where(e => string.Equals(e.Type, "Expense", StringComparison.OrdinalIgnoreCase))
            .Select(e => new DailyStationAmountRowDto(
                e.LocalAmount,
                e.Description ?? string.Empty,
                e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToList();

        // For currencies stored as USD the local lane is zero; USD column uses AmountUsd.
        var exchangeFromStation = stationExpenses
            .Where(e => string.Equals(e.Type, "Exchange", StringComparison.OrdinalIgnoreCase))
            .Select(e => new DailyStationExchangeRowDto(
                IsUsdExpense(e) ? 0 : e.LocalAmount,
                IsUsdExpense(e) ? 0 : e.Rate,
                e.AmountUsd,
                e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToList();

        var cashTakenFromStation = stationExpenses
            .Where(e => string.Equals(e.Type, "cashOrUsdTaken", StringComparison.OrdinalIgnoreCase))
            .Select(e => new DailyStationCashTakenRowDto(
                IsUsdExpense(e) ? 0 : e.LocalAmount,
                e.AmountUsd,
                e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToList();

        var expenseFromOffice = officeExpenses
            .Where(e => string.Equals(e.Type, "Expense", StringComparison.OrdinalIgnoreCase))
            .Select(e => new DailyStationAmountRowDto(
                e.LocalAmount,
                e.Description ?? string.Empty,
                e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToList();

        var exchangeFromOffice = officeExpenses
            .Where(e => string.Equals(e.Type, "Exchange", StringComparison.OrdinalIgnoreCase))
            .Select(e => new DailyStationExchangeRowDto(
                IsUsdExpense(e) ? 0 : e.LocalAmount,
                IsUsdExpense(e) ? 0 : e.Rate,
                e.AmountUsd,
                e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToList();

        // Resolve fuel name per nozzle once (first non-deleted dipping link wins). Avoids the
        // DippingPumps×Dippings cross-product that would otherwise duplicate sales rows when a
        // nozzle has multiple pump links, and lets us drop AmountLiter from the sales aggregation.
        var stationNozzleIds = await db.Nozzles.AsNoTracking()
            .Where(n => !n.IsDeleted && n.BusinessId == bid && n.StationId == stationIdVal)
            .Select(n => n.Id)
            .ToListAsync();
        var stationNozzleSet = stationNozzleIds.ToHashSet();

        var nozzleFuelLinks = await (
            from dp in db.DippingPumps.AsNoTracking()
            join d in db.Dippings.AsNoTracking() on dp.DippingId equals d.Id
            join ft in db.FuelTypes.AsNoTracking() on d.FuelTypeId equals ft.Id
            where !dp.IsDeleted && !d.IsDeleted && !ft.IsDeleted
                  && stationNozzleSet.Contains(dp.NozzleId)
            select new { dp.NozzleId, FuelName = ft.FuelName ?? string.Empty }
        ).ToListAsync();

        var fuelNameByNozzle = new Dictionary<int, string>();
        foreach (var pl in nozzleFuelLinks)
        {
            if (!fuelNameByNozzle.ContainsKey(pl.NozzleId))
                fuelNameByNozzle[pl.NozzleId] = pl.FuelName;
        }

        // Current tank balance per fuel kind (Petrol/Diesel/other). One value per kind, regardless
        // of how many sales rows happened on the day.
        var dippingRows = await (
            from d in db.Dippings.AsNoTracking()
            join ft in db.FuelTypes.AsNoTracking() on d.FuelTypeId equals ft.Id
            where !d.IsDeleted && !ft.IsDeleted
                  && d.BusinessId == bid && d.StationId == stationIdVal
            select new { ft.FuelName, d.AmountLiter }
        ).ToListAsync();

        var dippingByKind = dippingRows
            .GroupBy(x => IsDiesel(x.FuelName) ? "Diesel" : IsPetrol(x.FuelName) ? "Petrol" : (x.FuelName ?? string.Empty))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.AmountLiter));

        var fuelSalesRaw = await (
            from it in db.InventoryItems.AsNoTracking()
            join sale in db.InventorySales.AsNoTracking() on it.InventorySaleId equals sale.Id
            join nz in db.Nozzles.AsNoTracking() on it.NozzleId equals nz.Id
            where !it.IsDeleted && !sale.IsDeleted && !nz.IsDeleted
                  && sale.BusinessId == bid
                  && sale.StationId == stationIdVal
                  && it.Date >= fromInclusive && it.Date < toExclusive
            select new
            {
                Date = it.Date.Date,
                it.NozzleId,
                Liters = it.SspLiters + it.UsdLiters,
                it.SspAmount,
                it.UsdAmount,
            }
        ).ToListAsync();

        var fuelGroups = fuelSalesRaw
            .Select(x => new
            {
                x.Date,
                Kind = fuelNameByNozzle.TryGetValue(x.NozzleId, out var fn)
                    ? (IsDiesel(fn) ? "Diesel" : IsPetrol(fn) ? "Petrol" : (fn ?? string.Empty))
                    : string.Empty,
                x.Liters,
                x.SspAmount,
                x.UsdAmount,
            })
            .Where(x => !string.IsNullOrEmpty(x.Kind))
            .GroupBy(x => new { x.Date, x.Kind })
            .OrderBy(g => g.Key.Date)
            .ThenBy(g => g.Key.Kind)
            .Select(g => new DailyStationFuelRowDto(
                g.Key.Kind,
                g.Sum(x => x.Liters),
                g.Sum(x => x.SspAmount),
                g.Sum(x => x.UsdAmount),
                dippingByKind.TryGetValue(g.Key.Kind, out var dipBalance) ? dipBalance : 0,
                g.Key.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToList();

        // Business-wide (same idea as expense/exchange from office): all salary cash-outs in range, not station-scoped.
        var salaryPaymentRaw = await (
            from ep in db.EmployeePayments.AsNoTracking()
            join e in db.Employees.AsNoTracking() on ep.EmployeeId equals e.Id
            join u in db.Users.AsNoTracking() on ep.UserId equals u.Id
            where !ep.IsDeleted && !e.IsDeleted && !u.IsDeleted
                  && ep.BusinessId == bid
                  && ep.PaymentDate >= fromInclusive && ep.PaymentDate < toExclusive
                  && ep.PaidAmount > 0.00001
            select new
            {
                ep.EmployeeId,
                ep.PaidAmount,
                ep.UserId,
                UserName = u.Name ?? string.Empty,
                Day = ep.PaymentDate.Date,
            }).ToListAsync();

        var salaryPayments = salaryPaymentRaw
            .GroupBy(x => new { x.UserId, x.Day, x.UserName })
            .Select(g => new DailyStationSalaryPaymentRowDto(
                g.Select(x => x.EmployeeId).Distinct().Count(),
                Math.Round(g.Sum(x => x.PaidAmount), 2, MidpointRounding.AwayFromZero),
                g.Key.UserName.Trim(),
                g.Key.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .OrderBy(x => x.Date)
            .ThenBy(x => x.RecordedBy)
            .ToList();

        var dto = new DailyStationReportDto(
            selectedStation.Name,
            fromDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            toDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            new DailyStationFuelPriceDto(petrolSsp, dieselSsp, petrolUsd, dieselUsd),
            expenseFromStation,
            fuelGroups,
            exchangeFromStation,
            cashTakenFromStation,
            expenseFromOffice,
            exchangeFromOffice,
            salaryPayments
        );

        return Ok(dto);
    }

    /// <summary>
    /// Supplier ledger report — every "Purchased" / "Payment" row for a supplier within a business
    /// and date range, with running balance. Liters come from the linked Purchase's items.
    /// </summary>
    [HttpGet("supplier-report")]
    public async Task<IActionResult> SupplierReport(
        [FromQuery] int businessId,
        [FromQuery] int? supplierId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err))
            return err!;

        var fromDay = from?.Date;
        var toDay = to?.Date;
        if (fromDay.HasValue && toDay.HasValue && fromDay.Value > toDay.Value)
            return BadRequest("from must be on or before to.");

        Supplier? supplier = null;
        if (supplierId is > 0)
        {
            supplier = await db.Suppliers.AsNoTracking()
                .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == supplierId.Value && x.BusinessId == bid);
            if (supplier is null)
                return BadRequest("Supplier not found or does not belong to this business.");
        }

        var ledgerQuery =
            from p in db.SupplierPayments.AsNoTracking()
            join s in db.Suppliers.AsNoTracking() on p.SupplierId equals s.Id
            where !p.IsDeleted && !s.IsDeleted && p.BusinessId == bid
            select new { p, SupplierName = s.Name };

        if (supplierId is > 0)
            ledgerQuery = ledgerQuery.Where(x => x.p.SupplierId == supplierId.Value);
        if (fromDay.HasValue)
            ledgerQuery = ledgerQuery.Where(x => x.p.Date >= fromDay.Value);
        if (toDay.HasValue)
        {
            var toExclusive = toDay.Value.AddDays(1);
            ledgerQuery = ledgerQuery.Where(x => x.p.Date < toExclusive);
        }

        var ledger = await ledgerQuery
            .OrderBy(x => x.p.Date)
            .ThenBy(x => x.p.Id)
            .ToListAsync();

        var purchaseIds = ledger
            .Where(x => x.p.PurchaseId.HasValue)
            .Select(x => x.p.PurchaseId!.Value)
            .Distinct()
            .ToList();

        var litersByPurchase = new Dictionary<int, double>();
        if (purchaseIds.Count > 0)
        {
            var litersRows = await db.PurchaseItems.AsNoTracking()
                .Where(x => !x.IsDeleted && purchaseIds.Contains(x.PurchaseId))
                .GroupBy(x => x.PurchaseId)
                .Select(g => new { PurchaseId = g.Key, Liters = g.Sum(x => x.Liters) })
                .ToListAsync();
            foreach (var lr in litersRows)
                litersByPurchase[lr.PurchaseId] = lr.Liters;
        }

        var rows = ledger.Select(x => new SupplierReportRowDto(
            Id: x.p.Id,
            Name: x.SupplierName ?? string.Empty,
            Description: string.IsNullOrWhiteSpace(x.p.Description) ? "Payment" : x.p.Description,
            Liters: x.p.PurchaseId.HasValue && litersByPurchase.TryGetValue(x.p.PurchaseId.Value, out var l) ? l : null,
            Amount: x.p.ChargedAmount,
            Paid: x.p.PaidAmount,
            Balance: x.p.Balance,
            Date: x.p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PurchaseId: x.p.PurchaseId,
            ReferenceNo: x.p.ReferenceNo
        )).ToList();

        var totalCharged = Math.Round(rows.Sum(r => r.Amount), 2, MidpointRounding.AwayFromZero);
        var totalPaid = Math.Round(rows.Sum(r => r.Paid), 2, MidpointRounding.AwayFromZero);
        var endingBalance = rows.Count > 0 ? rows[^1].Balance : 0;
        if (supplierId is null or <= 0)
        {
            // Without a single supplier filter, the per-row Balance snapshots come from different
            // suppliers and are not a single running balance — surface charged−paid for the period instead.
            endingBalance = Math.Round(totalCharged - totalPaid, 2, MidpointRounding.AwayFromZero);
        }

        var dto = new SupplierReportDto(
            From: fromDay?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            To: toDay?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            SupplierId: supplierId,
            SupplierName: supplier?.Name,
            Rows: rows,
            TotalCharged: totalCharged,
            TotalPaid: totalPaid,
            Balance: endingBalance
        );
        return Ok(dto);
    }

    /// <summary>
    /// Customer ledger report — each fuel/cash line from <see cref="CustomerFuelTransaction"/> plus
    /// non–rolled-up <see cref="CustomerPayment"/> rows (excludes Description "Charged"), merged by date with running balance.
    /// </summary>
    [HttpGet("customer-report")]
    public async Task<IActionResult> CustomerReport(
        [FromQuery] int businessId,
        [FromQuery] int customerId,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err))
            return err!;

        if (customerId <= 0)
            return BadRequest("customerId is required.");

        var customer = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == customerId && x.BusinessId == bid);
        if (customer is null) return NotFound();

        DateTime? fromDay = TryParseCalendarDateQuery(from, out var fd) ? fd : null;
        DateTime? toDay = TryParseCalendarDateQuery(to, out var td) ? td : null;

        if (fromDay is null && !string.IsNullOrWhiteSpace(from))
            return BadRequest("Invalid from date; expected yyyy-MM-dd.");
        if (toDay is null && !string.IsNullOrWhiteSpace(to))
            return BadRequest("Invalid to date; expected yyyy-MM-dd.");

        if (fromDay.HasValue && toDay.HasValue && fromDay.Value > toDay.Value)
            return BadRequest("from must be on or before to.");

        // Ledger rows for the report must come from CustomerFuelGivens (per fuel/cash line with liters/price)
        // plus CustomerPayments that are not the rolled-up "Charged" mirror row (that row duplicates cfg totals).
        var fuelTypeNames = await db.FuelTypes.AsNoTracking()
            .Where(f => !f.IsDeleted && f.BusinessId == bid)
            .ToDictionaryAsync(f => f.Id, f => f.FuelName);

        var cfgBase = db.CustomerFuelGivens.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == bid && x.CustomerId == customerId);

        double opening = 0;
        if (fromDay.HasValue)
        {
            var openingCutoff = fromDay.Value;
            var openingCfgs = await cfgBase.Where(x => x.Date < openingCutoff).ToListAsync();
            foreach (var c in openingCfgs)
                opening += CustomerPaymentRepository.ChargedFromCfg(c);

            var openingPayments = await db.CustomerPayments.AsNoTracking()
                .Where(x => !x.IsDeleted
                            && x.BusinessId == bid
                            && x.CustomerId == customerId
                            && x.Description != "Charged"
                            && x.PaymentDate < openingCutoff)
                .ToListAsync();
            foreach (var p in openingPayments)
                opening += p.ChargedAmount - p.AmountPaid;
        }

        opening = Math.Round(opening, 2, MidpointRounding.AwayFromZero);

        var cfgQuery = cfgBase;
        if (fromDay.HasValue)
            cfgQuery = cfgQuery.Where(x => x.Date >= fromDay.Value);
        if (toDay.HasValue)
        {
            var toExclusive = toDay.Value.AddDays(1);
            cfgQuery = cfgQuery.Where(x => x.Date < toExclusive);
        }

        var cfgs = await cfgQuery.ToListAsync();

        var payQuery = db.CustomerPayments.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.BusinessId == bid
                        && x.CustomerId == customerId
                        && x.Description != "Charged");
        if (fromDay.HasValue)
            payQuery = payQuery.Where(x => x.PaymentDate >= fromDay.Value);
        if (toDay.HasValue)
        {
            var toExclusive = toDay.Value.AddDays(1);
            payQuery = payQuery.Where(x => x.PaymentDate < toExclusive);
        }

        var payments = await payQuery.ToListAsync();

        var merged = new List<(DateTime SortDate, int SortId, int Kind, CustomerFuelTransaction? Cfg, CustomerPayment? Pay)>();
        foreach (var c in cfgs)
            merged.Add((c.Date, c.Id, 0, c, null));
        foreach (var p in payments)
            merged.Add((p.PaymentDate, p.Id, 1, null, p));

        merged.Sort((a, b) =>
        {
            var cmp = a.SortDate.CompareTo(b.SortDate);
            if (cmp != 0) return cmp;
            cmp = a.SortId.CompareTo(b.SortId);
            if (cmp != 0) return cmp;
            return a.Kind.CompareTo(b.Kind);
        });

        var rows = new List<CustomerReportRowDto>(merged.Count);
        var running = opening;
        foreach (var m in merged)
        {
            if (m.Cfg is { } cfg)
            {
                var charged = Math.Round(CustomerPaymentRepository.ChargedFromCfg(cfg), 2, MidpointRounding.AwayFromZero);
                var isFuel = string.Equals(cfg.Type, "Fuel", StringComparison.OrdinalIgnoreCase);
                running = Math.Round(running + charged, 2, MidpointRounding.AwayFromZero);
                fuelTypeNames.TryGetValue(cfg.FuelTypeId, out var fuelName);
                rows.Add(new CustomerReportRowDto(
                    Id: -cfg.Id,
                    CustomerId: customerId,
                    Name: customer.Name,
                    Phone: customer.Phone,
                    Description: isFuel ? "Fuel" : "Cash",
                    Type: cfg.Type,
                    FuelTypeId: isFuel ? cfg.FuelTypeId : null,
                    FuelTypeName: isFuel ? fuelName : null,
                    Liters: isFuel ? cfg.GivenLiter : null,
                    Price: isFuel ? cfg.Price : null,
                    CashTaken: charged,
                    Charged: charged,
                    Paid: 0,
                    Balance: running,
                    Date: cfg.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ReferenceNo: string.IsNullOrWhiteSpace(cfg.Remark) ? null : cfg.Remark.Trim()));
            }
            else if (m.Pay is { } x)
            {
                var ch = Math.Round(x.ChargedAmount, 2, MidpointRounding.AwayFromZero);
                var paid = Math.Round(x.AmountPaid, 2, MidpointRounding.AwayFromZero);
                running = Math.Round(running + ch - paid, 2, MidpointRounding.AwayFromZero);
                rows.Add(new CustomerReportRowDto(
                    Id: x.Id,
                    CustomerId: customerId,
                    Name: customer.Name,
                    Phone: customer.Phone,
                    Description: string.IsNullOrWhiteSpace(x.Description) ? "Payment" : x.Description,
                    Type: null,
                    FuelTypeId: null,
                    FuelTypeName: null,
                    Liters: null,
                    Price: null,
                    CashTaken: ch,
                    Charged: ch,
                    Paid: paid,
                    Balance: running,
                    Date: x.PaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ReferenceNo: x.ReferenceNo));
            }
        }

        var totalCharged = Math.Round(rows.Sum(r => r.Charged), 2, MidpointRounding.AwayFromZero);
        var totalPaid = Math.Round(rows.Sum(r => r.Paid), 2, MidpointRounding.AwayFromZero);
        var totalCash = Math.Round(rows.Sum(r => r.CashTaken), 2, MidpointRounding.AwayFromZero);
        var totalLiters = Math.Round(rows.Sum(r => r.Liters ?? 0), 3, MidpointRounding.AwayFromZero);
        var endingBalance = rows.Count > 0 ? rows[^1].Balance : opening;

        var dto = new CustomerReportDto(
            From: fromDay?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            To: toDay?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            CustomerId: customerId,
            CustomerName: customer.Name,
            CustomerPhone: customer.Phone,
            Rows: rows,
            TotalCharged: totalCharged,
            TotalCashTaken: totalCash,
            TotalLiters: totalLiters,
            TotalPaid: totalPaid,
            Balance: endingBalance
        );
        return Ok(dto);
    }

    /// <summary>Distinct customers for a business (fuel ledger and/or customer payment ledger). Used by the Customer Report picker.</summary>
    [HttpGet("customers")]
    public async Task<IActionResult> CustomersList([FromQuery] int businessId)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err))
            return err!;

        var fuelLastByCustomer = await db.CustomerFuelGivens.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == bid)
            .GroupBy(x => x.CustomerId)
            .Select(g => new { CustomerId = g.Key, LastDate = g.Max(x => x.Date) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.LastDate);

        var paymentLastByCustomer = await db.CustomerPayments.AsNoTracking()
            .Where(p => !p.IsDeleted && p.BusinessId == bid)
            .GroupBy(p => p.CustomerId)
            .Select(g => new { CustomerId = g.Key, LastDate = g.Max(x => x.PaymentDate) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.LastDate);

        var allIds = fuelLastByCustomer.Keys.AsEnumerable().Union(paymentLastByCustomer.Keys).Distinct().ToList();
        if (allIds.Count == 0)
            return Ok(Array.Empty<object>());

        var customers = await db.Customers.AsNoTracking()
            .Where(c => !c.IsDeleted && c.BusinessId == bid && allIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Phone })
            .ToListAsync();

        var rows = customers
            .Select(c =>
            {
                fuelLastByCustomer.TryGetValue(c.Id, out var fuelD);
                paymentLastByCustomer.TryGetValue(c.Id, out var payD);
                var last = fuelD > payD ? fuelD : payD;
                return new
                {
                    customerId = c.Id,
                    name = c.Name,
                    phone = c.Phone,
                    lastDate = last,
                };
            })
            .OrderByDescending(x => x.lastDate)
            .ToList();
        return Ok(rows);
    }

    /// <summary>Active employees for the report pickers / payroll runs (optionally filtered by station).</summary>
    [HttpGet("employees")]
    public async Task<IActionResult> EmployeesList(
        [FromQuery] int businessId,
        [FromQuery] int? stationId = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] string? period = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        var (stationFilter, stationErr) = await ResolveReportStationAsync(bid, stationId);
        if (stationErr is not null) return stationErr;

        var q = db.Employees.AsNoTracking().Where(x => !x.IsDeleted && x.BusinessId == bid);
        if (!includeInactive) q = q.Where(x => x.IsActive);
        if (stationFilter is > 0) q = q.Where(x => x.StationId == stationFilter.Value);

        var list = await q
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Phone, x.Position, x.BaseSalary, x.StationId })
            .ToListAsync();

        HashSet<int> salaryRecorded = new();
        var p = (period ?? string.Empty).Trim();
        if (p.Length > 0)
        {
            var ids = await db.EmployeePayments.AsNoTracking()
                .Where(x => !x.IsDeleted
                            && x.BusinessId == bid
                            && x.PeriodLabel == p
                            && x.Description == "Salary"
                            && x.ChargedAmount > 0.00001)
                .Select(x => x.EmployeeId)
                .Distinct()
                .ToListAsync();
            salaryRecorded = ids.ToHashSet();
        }

        var rows = list
            .Select(x => new EmployeeOptionDto(
                x.Id,
                x.Name,
                x.Phone,
                x.Position,
                x.BaseSalary,
                x.StationId,
                salaryRecorded.Contains(x.Id)))
            .ToList();
        return Ok(rows);
    }

    /// <summary>
    /// Full ledger history for one employee with running balance and final outstanding balance.
    /// Used by both the Employee Details page and the Employee Payment History report.
    /// </summary>
    [HttpGet("employee-payment-history")]
    public async Task<IActionResult> EmployeePaymentHistory(
        [FromQuery] int businessId,
        [FromQuery] int employeeId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        if (employeeId <= 0) return BadRequest("employeeId is required.");

        var employee = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == employeeId && !x.IsDeleted && x.BusinessId == bid);
        if (employee is null) return NotFound();

        var fromDay = from?.Date;
        var toDay = to?.Date;
        if (fromDay.HasValue && toDay.HasValue && fromDay.Value > toDay.Value)
            return BadRequest("from must be on or before to.");

        var ledgerQuery = db.EmployeePayments.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == bid && x.EmployeeId == employeeId);
        if (fromDay.HasValue) ledgerQuery = ledgerQuery.Where(x => x.PaymentDate >= fromDay.Value);
        if (toDay.HasValue)
        {
            var toExclusive = toDay.Value.AddDays(1);
            ledgerQuery = ledgerQuery.Where(x => x.PaymentDate < toExclusive);
        }

        var ledger = await ledgerQuery
            .OrderBy(x => x.PaymentDate)
            .ThenBy(x => x.Id)
            .ToListAsync();

        // Outstanding is computed across the full lifetime (not the date range), so reports
        // always show the actual amount still owed regardless of which window is shown.
        var lifetime = await db.EmployeePayments.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == bid && x.EmployeeId == employeeId)
            .Select(x => new { x.ChargedAmount, x.PaidAmount })
            .ToListAsync();
        var lifetimeOutstanding = Math.Max(0, lifetime.Sum(x => x.ChargedAmount - x.PaidAmount));

        var rows = ledger.Select(x => new EmployeePaymentHistoryRowDto(
            Id: x.Id,
            Date: x.PaymentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Description: string.IsNullOrWhiteSpace(x.Description) ? "Payment" : x.Description,
            PeriodLabel: x.PeriodLabel,
            Charged: Math.Round(x.ChargedAmount, 2, MidpointRounding.AwayFromZero),
            Paid: Math.Round(x.PaidAmount, 2, MidpointRounding.AwayFromZero),
            Balance: Math.Round(x.Balance, 2, MidpointRounding.AwayFromZero),
            ReferenceNo: x.ReferenceNo,
            StationId: x.StationId)).ToList();

        var dto = new EmployeePaymentHistoryDto(
            From: fromDay?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            To: toDay?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            EmployeeId: employee.Id,
            EmployeeName: employee.Name,
            EmployeePhone: employee.Phone,
            EmployeePosition: employee.Position,
            BaseSalary: Math.Round(employee.BaseSalary, 2, MidpointRounding.AwayFromZero),
            Rows: rows,
            TotalCharged: Math.Round(rows.Sum(r => r.Charged), 2, MidpointRounding.AwayFromZero),
            TotalPaid: Math.Round(rows.Sum(r => r.Paid), 2, MidpointRounding.AwayFromZero),
            OutstandingBalance: Math.Round(lifetimeOutstanding, 2, MidpointRounding.AwayFromZero));
        return Ok(dto);
    }

    /// <summary>
    /// Splits active employees into paid / unpaid lists for a given period (e.g. "2026-05").
    /// "Paid" = at least one EmployeePayment row with a positive PaidAmount in the period; "Unpaid" otherwise.
    /// </summary>
    [HttpGet("payroll-status")]
    public async Task<IActionResult> PayrollStatus(
        [FromQuery] int businessId,
        [FromQuery] string period,
        [FromQuery] int? stationId = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err)) return err!;
        if (string.IsNullOrWhiteSpace(period))
            return BadRequest("period is required, e.g. \"2026-05\".");
        var p = period.Trim();
        var (stationFilter, stationErr) = await ResolveReportStationAsync(bid, stationId);
        if (stationErr is not null) return stationErr;

        var employeeQuery = db.Employees.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == bid && x.IsActive);
        if (stationFilter is > 0) employeeQuery = employeeQuery.Where(x => x.StationId == stationFilter.Value);
        var employees = await employeeQuery.OrderBy(x => x.Name).ToListAsync();
        if (employees.Count == 0)
        {
            return Ok(new PayrollStatusReportDto(bid, p, stationFilter, [], []));
        }

        var employeeIds = employees.Select(e => e.Id).ToList();

        // Period payments — drive paid/unpaid classification.
        var periodRows = await db.EmployeePayments.AsNoTracking()
            .Where(x => !x.IsDeleted
                        && x.BusinessId == bid
                        && employeeIds.Contains(x.EmployeeId)
                        && x.PeriodLabel == p)
            .Select(x => new { x.EmployeeId, x.ChargedAmount, x.PaidAmount, x.PaymentDate })
            .ToListAsync();
        var periodGroups = periodRows
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => new
            {
                Charged = g.Sum(r => r.ChargedAmount),
                Paid = g.Sum(r => r.PaidAmount),
                Last = g.Max(r => r.PaymentDate),
            });

        // Lifetime balance — used as the outstanding amount for unpaid rows.
        var lifetimeRows = await db.EmployeePayments.AsNoTracking()
            .Where(x => !x.IsDeleted && x.BusinessId == bid && employeeIds.Contains(x.EmployeeId))
            .Select(x => new { x.EmployeeId, x.ChargedAmount, x.PaidAmount })
            .ToListAsync();
        var lifetimeGroups = lifetimeRows
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => new
            {
                Charged = g.Sum(r => r.ChargedAmount),
                Paid = g.Sum(r => r.PaidAmount),
            });

        var paid = new List<PayrollEmployeeStatusRowDto>();
        var unpaid = new List<PayrollEmployeeStatusRowDto>();
        foreach (var emp in employees)
        {
            var periodInfo = periodGroups.GetValueOrDefault(emp.Id);
            var lifetime = lifetimeGroups.GetValueOrDefault(emp.Id);
            var lifetimeBalance = lifetime is null
                ? 0
                : Math.Max(0, lifetime.Charged - lifetime.Paid);

            var row = new PayrollEmployeeStatusRowDto(
                EmployeeId: emp.Id,
                Name: emp.Name,
                Phone: emp.Phone,
                Position: emp.Position,
                StationId: emp.StationId,
                BaseSalary: Math.Round(emp.BaseSalary, 2, MidpointRounding.AwayFromZero),
                TotalCharged: Math.Round(periodInfo?.Charged ?? 0, 2, MidpointRounding.AwayFromZero),
                TotalPaid: Math.Round(periodInfo?.Paid ?? 0, 2, MidpointRounding.AwayFromZero),
                Balance: Math.Round(lifetimeBalance, 2, MidpointRounding.AwayFromZero),
                LastPaymentDate: periodInfo is null
                    ? null
                    : periodInfo.Last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            if (periodInfo is not null && periodInfo.Paid > 1e-6) paid.Add(row);
            else unpaid.Add(row);
        }

        return Ok(new PayrollStatusReportDto(bid, p, stationFilter, paid, unpaid));
    }
}
