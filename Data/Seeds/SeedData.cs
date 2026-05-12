using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Seeds;

public static class SeedData
{
    public static async Task InitializeAsync(GasStationDBContext context)
    {
        // Schema comes from EF migrations (Program.cs); do not call EnsureCreated — it conflicts with Migrate().

        if (!await context.Roles.AnyAsync())
        {
            var roles = new[]
            {
                new Role { Name = "SuperAdmin" },
                new Role { Name = "Admin" },
                new Role { Name = "Accountant" },
                new Role { Name = "Manager" }
            };
            await context.Roles.AddRangeAsync(roles);
            await context.SaveChangesAsync();

            var superAdminRoleId = roles.First(r => r.Name == "SuperAdmin").Id;
            if (!await context.Users.AnyAsync(u => u.Email == "superadmin@gmail.com"))
            {
                await context.Users.AddRangeAsync(
                    new User
                    {
                        Name = "Nuur Hassan Mohamed",
                        Email = "superadmin@gmail.com",
                        Phone = "612450931",
                        PasswordHash = PasswordHasher.Hash("SuperAdmin@123"),
                        RoleId = superAdminRoleId
                    });

                await context.SaveChangesAsync();
            }
        }

 
        await EnsureDefaultChartsOfAccountsAsync(context);
        await EnsureDefaultCurrenciesAsync(context);
   
    }

    /// <summary>
    /// Ensures the global chart-of-account categories exist (Asset, Liability, Equity, Income, Expense).
    /// Adds any missing types on each startup so the Charts of Accounts screen and account setup stay aligned.
    /// </summary>
    private static async Task EnsureDefaultChartsOfAccountsAsync(GasStationDBContext context)
    {
        var existing = await context.ChartsOfAccounts
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => x.Type)
            .ToListAsync();

        static bool HasType(List<string> types, string t) =>
            types.Exists(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase));

        var now = DateTime.UtcNow;
        var defaultTypes = new[] { "Asset", "Liability", "Equity", "Income", "Expense", "COGS", "Temporary Account" };
        var anyAdded = false;
        // if the charts of accounts already exist, don't add them
        if (await context.ChartsOfAccounts.AnyAsync())
        {
            return;
        }

        foreach (var t in defaultTypes)
        {
            if (HasType(existing, t)) continue;
            context.ChartsOfAccounts.Add(new ChartsOfAccounts
            {
                Type = t,
                CreatedAt = now,
                UpdatedAt = now,
            });
            existing.Add(t);
            anyAdded = true;
        }

        if (anyAdded)
            await context.SaveChangesAsync();
    }

    /// <summary>
    /// Ensures South Sudanese Pound (SSP) and United States Dollar (USD) exist. Idempotent by ISO code.
    /// </summary>
    private static async Task EnsureDefaultCurrenciesAsync(GasStationDBContext context)
    {
        // check if the currencies already exist
        if (await context.Currencies.AnyAsync())
        {
            return;
        }

        var codes = await context.Currencies
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => x.Code)
            .ToListAsync();

        static bool HasCode(List<string> list, string code) =>
            list.Exists(x => string.Equals(x, code, StringComparison.OrdinalIgnoreCase));

        var now = DateTime.UtcNow;
        var anyAdded = false;

        if (!HasCode(codes, "SSP"))
        {
            context.Currencies.Add(new Currency
            {
                CountryName = "South Sudan",
                Code = "SSP",
                Name = "South Sudanese Pound",
                Symbol = "SSP",
                CreatedAt = now,
                UpdatedAt = now,
            });
            codes.Add("SSP");
            anyAdded = true;
        }

        if (!HasCode(codes, "USD"))
        {
            context.Currencies.Add(new Currency
            {
                CountryName = "United States of America",
                Code = "USD",
                Name = "United States Dollar",
                Symbol = "$",
                CreatedAt = now,
                UpdatedAt = now,
            });
            anyAdded = true;
        }

        if (anyAdded)
            await context.SaveChangesAsync();
    }

}
