using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using gas_station.Data.Context;
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
    int StationId);

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
    IReadOnlyList<DailyStationExchangeRowDto> ExchangeFromOffice);

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
    /// SuperAdmin: optional query station (null = all). Others: JWT station locks scope when set; else optional query station validated against business (e.g. Admin workspace).
    /// </summary>
    private async Task<(int? StationFilter, IActionResult? Error)> ResolveReportStationAsync(int businessId, int? queryStationId)
    {
        if (IsSuperAdmin(User))
            return queryStationId is > 0 ? (queryStationId, null) : (null, null);

        if (TryGetJwtStation(out var js) && js > 0)
            return (js, null);

        if (queryStationId is > 0)
        {
            var st = await db.Stations.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == queryStationId.Value && !x.IsDeleted);
            if (st is null || st.BusinessId != businessId)
                return (null, BadRequest("Invalid station for this business."));
            return (queryStationId, null);
        }

        return (null, null);
    }

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
        string? expenseType = null)
    {
        var q = db.Expenses.AsNoTracking().Where(x => !x.IsDeleted && x.BusinessId == bid);
        if (from.HasValue)
            q = q.Where(x => x.Date >= from.Value.Date);
        if (to.HasValue)
        {
            var toExclusive = to.Value.Date.AddDays(1);
            q = q.Where(x => x.Date < toExclusive);
        }

        if (stationFilter is > 0)
            q = q.Where(x => x.StationId == stationFilter.Value);
        if (!string.IsNullOrWhiteSpace(expenseType))
            q = q.Where(x => x.Type == expenseType);

        var items = await q
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Id)
            .ToListAsync();

        var lines = items.Select(e => new CashOutDailyLineDto(
            e.Id,
            e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ClassifyCashOutKind(e),
            e.Description,
            string.IsNullOrWhiteSpace(e.CurrencyCode) ? "USD" : e.CurrencyCode,
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
                usdCurrencyOnly += r.LocalAmount;
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
        [FromQuery] string? expenseType = null)
    {
        if (!ResolveBusiness(businessId, out var bid, out var err))
            return err!;

        var (stationFilter, stationErr) = await ResolveReportStationAsync(bid, stationId);
        if (stationErr != null)
            return stationErr;

        return Ok(await BuildCashOutDailyReportAsync(bid, stationFilter, from, to, expenseType));
    }

    /// <summary>
    /// Single source for the Daily Summary block (previous balance, period sales, cash-out lines, footer balances).
    /// </summary>
    [HttpGet("daily-summary-report")]
    public async Task<IActionResult> DailySummaryReport(
        [FromQuery] int businessId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] int? stationId = null)
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

        var periodCashOut = await BuildCashOutDailyReportAsync(bid, stationFilter, fromDay, toDay, null);
        var previousTo = fromDay.AddDays(-1);
        var previousCashOut = await BuildCashOutDailyReportAsync(bid, stationFilter, null, previousTo, null);

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
                    x.g.Name ?? string.Empty,
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

        var exchangeFromStation = stationExpenses
            .Where(e => string.Equals(e.Type, "Exchange", StringComparison.OrdinalIgnoreCase))
            .Select(e => new DailyStationExchangeRowDto(
                e.LocalAmount,
                e.Rate,
                e.AmountUsd,
                e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToList();

        var cashTakenFromStation = stationExpenses
            .Where(e => string.Equals(e.Type, "cashOrUsdTaken", StringComparison.OrdinalIgnoreCase))
            .Select(e => new DailyStationCashTakenRowDto(
                e.LocalAmount,
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
                e.LocalAmount,
                e.Rate,
                e.AmountUsd,
                e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
            .ToList();

        var fuelSalesRaw = await (
            from it in db.InventoryItems.AsNoTracking()
            join sale in db.InventorySales.AsNoTracking() on it.InventorySaleId equals sale.Id
            join nz in db.Nozzles.AsNoTracking() on it.NozzleId equals nz.Id
            join dp in db.DippingPumps.AsNoTracking() on nz.Id equals dp.NozzleId
            join d in db.Dippings.AsNoTracking() on dp.DippingId equals d.Id
            join ft in db.FuelTypes.AsNoTracking() on d.FuelTypeId equals ft.Id
            where !it.IsDeleted && !sale.IsDeleted && !nz.IsDeleted && !dp.IsDeleted && !d.IsDeleted && !ft.IsDeleted
                  && sale.BusinessId == bid
                  && sale.StationId == stationIdVal
                  && it.Date >= fromInclusive && it.Date < toExclusive
            select new
            {
                Date = it.Date.Date,
                FuelName = ft.FuelName,
                Liters = it.SspLiters + it.UsdLiters,
                it.SspAmount,
                it.UsdAmount,
                d.AmountLiter
            }
        ).ToListAsync();

        var fuelGroups = fuelSalesRaw
            .GroupBy(x => new
            {
                x.Date,
                Kind = IsDiesel(x.FuelName) ? "Diesel" : IsPetrol(x.FuelName) ? "Petrol" : x.FuelName
            })
            .OrderBy(g => g.Key.Date)
            .ThenBy(g => g.Key.Kind)
            .Select(g => new DailyStationFuelRowDto(
                g.Key.Kind,
                g.Sum(x => x.Liters),
                g.Sum(x => x.SspAmount),
                g.Sum(x => x.UsdAmount),
                g.Sum(x => x.AmountLiter),
                g.Key.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
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
            exchangeFromOffice
        );

        return Ok(dto);
    }
}
