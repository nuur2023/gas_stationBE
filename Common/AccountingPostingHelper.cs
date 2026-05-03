using gas_station.Data.Context;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Common;

public static class AccountingPostingHelper
{
    public static async Task<Account> EnsureAccountAsync(
        GasStationDBContext db,
        int businessId,
        string code,
        string name,
        string type,
        int? parentId = null)
    {
        // MySQL EF cannot translate String.Equals(..., StringComparison); use LOWER() via ToLower().
        var typeNorm = type.Trim().ToLowerInvariant();
        var chart = await db.ChartsOfAccounts.FirstOrDefaultAsync(x =>
            !x.IsDeleted && x.Type.ToLower() == typeNorm);
        if (chart is null)
        {
            chart = new ChartsOfAccounts
            {
                Type = type,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.ChartsOfAccounts.Add(chart);
            await db.SaveChangesAsync();
        }

        var existing = await db.Accounts
            .Include(x => x.ChartsOfAccounts)
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.Code == code && x.BusinessId == businessId);
        if (existing is not null) return existing;

        var account = new Account
        {
            BusinessId = businessId,
            Name = name,
            Code = code,
            ChartsOfAccountsId = chart.Id,
            ParentAccountId = parentId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    public static async Task<JournalEntry> CreateJournalEntryAsync(
        GasStationDBContext db,
        DateTime date,
        string description,
        int businessId,
        int userId,
        int? stationId,
        IEnumerable<(int accountId, double debit, double credit, string? remark, int? customerId, int? supplierId)> lines,
        JournalEntryKind entryKind = JournalEntryKind.Normal,
        int? recurringJournalEntryId = null)
    {
        var entry = new JournalEntry
        {
            Date = date,
            Description = description,
            BusinessId = businessId,
            UserId = userId,
            StationId = stationId,
            EntryKind = entryKind,
            RecurringJournalEntryId = recurringJournalEntryId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync();

        foreach (var l in lines)
        {
            db.JournalEntryLines.Add(new JournalEntryLine
            {
                JournalEntryId = entry.Id,
                AccountId = l.accountId,
                Debit = l.debit,
                Credit = l.credit,
                Remark = l.remark,
                CustomerId = l.customerId,
                SupplierId = l.supplierId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        return entry;
    }
}

