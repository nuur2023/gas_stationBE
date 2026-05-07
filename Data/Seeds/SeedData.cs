using gas_station.Common;
using gas_station.Data.Context;
using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Seeds;

public static class SeedData
{
    public static async Task InitializeAsync(GasStationDBContext context)
    {
        await context.Database.EnsureCreatedAsync();

        if (!await context.Roles.AnyAsync())
        {
            var roles = new[]
            {
                new Role { Name = "SuperAdmin" },
                new Role { Name = "Admin" },
                new Role { Name = "Accountant" },
                new Role { Name = "Manager" }
            };
            // if roles are already added in the database, then skip this
            if (!await context.Roles.AnyAsync())
            {   
                await context.Roles.AddRangeAsync(roles);
                await context.SaveChangesAsync();
            }
           
        
            var superAdminRoleId = roles.First(r => r.Name == "SuperAdmin").Id;
            // if already added in the database
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
                }
            );

            await context.SaveChangesAsync();
        }

        // await EnsureDefaultMenusAsync(context);
        // await EnsureSidebarMenusAsync(context);
        await EnsureDefaultChartsOfAccountsAsync(context);
        await EnsureDefaultCurrenciesAsync(context);
        await EnsurePurchaseStatusColumnsAsync(context);
        // await EnsureInventoryFuelPriceColumnsAsync(context);
        // await EnsureDippingPumpNavAndAdminPermissionsAsync(context);
        // await EnsurePumpNozzlesSubmenuAsync(context);
    }
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

    private static async Task EnsurePurchaseStatusColumnsAsync(GasStationDBContext context)
    {
        var purchasesTableExists = await context.Database
            .SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS Value
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'Purchases'
                """)
            .SingleAsync();
        if (purchasesTableExists == 0) return;

        var hasStatus = await context.Database
            .SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS Value
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'Purchases'
                  AND COLUMN_NAME = 'Status'
                """)
            .SingleAsync();
        if (hasStatus == 0)
        {
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE `Purchases` ADD COLUMN `Status` longtext NOT NULL DEFAULT 'Unpaid'");
        }

        var hasAmountPaid = await context.Database
            .SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS Value
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'Purchases'
                  AND COLUMN_NAME = 'AmountPaid'
                """)
            .SingleAsync();
        if (hasAmountPaid == 0)
        {
            await context.Database.ExecuteSqlRawAsync(
                "ALTER TABLE `Purchases` ADD COLUMN `AmountPaid` double NOT NULL DEFAULT 0");
        }
    }

    /// <summary>
    /// Backfills schema for existing databases: adds inventory fuel-price snapshot columns when missing.
    /// Safe to run repeatedly.
    /// </summary>
    // private static async Task EnsureInventoryFuelPriceColumnsAsync(GasStationDBContext context)
    // {
    //     const string ssp = "SspFuelPrice";
    //     const string usd = "UsdFuelPrice";

    //     // New schema moved runtime reads to InventoryItems/InventorySales.
    //     // Legacy Inventories table may not exist on fresh databases, so skip safely.
    //     var inventoriesExists = await context.Database
    //         .SqlQueryRaw<int>(
    //             """
    //             SELECT CASE WHEN COUNT(*) > 0 THEN 1 ELSE 0 END
    //             FROM INFORMATION_SCHEMA.TABLES
    //             WHERE TABLE_SCHEMA = DATABASE()
    //               AND TABLE_NAME = 'Inventories'
    //             """)
    //         .AnyAsync();
    //     if (!inventoriesExists)
    //         return;

    //     var colNames = await context.Database
    //         .SqlQueryRaw<string>(
    //             """
    //             SELECT COLUMN_NAME
    //             FROM INFORMATION_SCHEMA.COLUMNS
    //             WHERE TABLE_SCHEMA = DATABASE()
    //               AND TABLE_NAME = 'Inventories'
    //               AND COLUMN_NAME IN ('SspFuelPrice', 'UsdFuelPrice')
    //             """)
    //         .ToListAsync();

    //     var hasSsp = colNames.Any(x => string.Equals(x, ssp, StringComparison.OrdinalIgnoreCase));
    //     var hasUsd = colNames.Any(x => string.Equals(x, usd, StringComparison.OrdinalIgnoreCase));

    //     if (!hasSsp)
    //     {
    //         await context.Database.ExecuteSqlRawAsync(
    //             "ALTER TABLE `Inventories` ADD COLUMN `SspFuelPrice` double NOT NULL DEFAULT 0");
    //     }
    //     if (!hasUsd)
    //     {
    //         await context.Database.ExecuteSqlRawAsync(
    //             "ALTER TABLE `Inventories` ADD COLUMN `UsdFuelPrice` double NOT NULL DEFAULT 0");
    //     }
    // }

    /// <summary>
    /// Ensures a sidebar/permission row exists for DippingPump (<c>/dipping-pumps</c>) and grants full access
    /// on that route to every user in the Admin role for each business they are linked to. Idempotent.
    /// </summary>
    private static async Task EnsureDippingPumpNavAndAdminPermissionsAsync(GasStationDBContext context)
    {
        const string route = "/dipping-pumps";
        var now = DateTime.UtcNow;

        var sub = await context.SubMenus.AsNoTracking()
            .FirstOrDefaultAsync(s => !s.IsDeleted && s.Route.Trim() == route);
        if (sub == null)
        {
            context.Menus.Add(new Menu
            {
                Name = "DippingPump",
                Route = route,
                CreatedAt = now,
                UpdatedAt = now,
                SubMenus =
                [
                    new SubMenu { Name = "DippingPump", Route = route, CreatedAt = now, UpdatedAt = now },
                ],
            });
            await context.SaveChangesAsync();
            sub = await context.SubMenus.AsNoTracking()
                .FirstOrDefaultAsync(s => !s.IsDeleted && s.Route.Trim() == route);
        }

        if (sub == null)
            return;

        var adminRoleId = await context.Roles.AsNoTracking()
            .Where(r => !r.IsDeleted && r.Name == "Admin")
            .Select(r => r.Id)
            .FirstOrDefaultAsync();
        if (adminRoleId == 0)
            return;

        var adminUserIds = await context.Users.AsNoTracking()
            .Where(u => !u.IsDeleted && u.RoleId == adminRoleId)
            .Select(u => u.Id)
            .ToListAsync();
        if (adminUserIds.Count == 0)
            return;

        var links = await context.BusinessUsers.AsNoTracking()
            .Where(bu => !bu.IsDeleted && adminUserIds.Contains(bu.UserId))
            .Select(bu => new { bu.UserId, bu.BusinessId })
            .ToListAsync();

        var distinctPairs = links
            .GroupBy(x => new { x.UserId, x.BusinessId })
            .Select(g => g.Key)
            .ToList();

        var existingPairs = await context.Permissions.AsNoTracking()
            .Where(p => !p.IsDeleted && p.SubMenuId == sub.Id && adminUserIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.BusinessId })
            .ToListAsync();
        var existingSet = existingPairs.Select(x => (x.UserId, x.BusinessId)).ToHashSet();

        var anyPermAdded = false;
        foreach (var pair in distinctPairs)
        {
            if (existingSet.Contains((pair.UserId, pair.BusinessId)))
                continue;

            context.Permissions.Add(new Permission
            {
                UserId = pair.UserId,
                BusinessId = pair.BusinessId,
                MenuId = sub.MenuId,
                SubMenuId = sub.Id,
                CanView = true,
                CanCreate = true,
                CanUpdate = true,
                CanDelete = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            existingSet.Add((pair.UserId, pair.BusinessId));
            anyPermAdded = true;
        }

        if (anyPermAdded)
            await context.SaveChangesAsync();
    }

    /// <summary>
    /// Ensures Pumps menu has a Nozzles submenu (route <c>/nozzles</c>) so Permissions shows it under Pumps.
    /// Idempotent and safe on existing databases.
    /// </summary>
    private static async Task EnsurePumpNozzlesSubmenuAsync(GasStationDBContext context)
    {
        const string pumpsRoute = "/pumps";
        const string nozzlesRoute = "/nozzles";
        var now = DateTime.UtcNow;

        var pumpsMenu = await context.Menus
            .FirstOrDefaultAsync(m => !m.IsDeleted && m.Route.Trim() == pumpsRoute);

        if (pumpsMenu == null)
        {
            pumpsMenu = new Menu
            {
                Name = "Pumps",
                Route = pumpsRoute,
                CreatedAt = now,
                UpdatedAt = now,
                SubMenus =
                [
                    new SubMenu { Name = "Pumps", Route = pumpsRoute, CreatedAt = now, UpdatedAt = now },
                ],
            };
            context.Menus.Add(pumpsMenu);
            await context.SaveChangesAsync();
        }

        var hasNozzles = await context.SubMenus.AsNoTracking()
            .AnyAsync(s => !s.IsDeleted && s.MenuId == pumpsMenu.Id && s.Route.Trim() == nozzlesRoute);
        if (hasNozzles)
            return;

        context.SubMenus.Add(new SubMenu
        {
            MenuId = pumpsMenu.Id,
            Name = "Nozzles",
            Route = nozzlesRoute,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Inserts canonical menus/submenus when the Menus table is empty (e.g. first run or fresh DB).
    /// </summary>
    private static async Task EnsureDefaultMenusAsync(GasStationDBContext context)
    {
        if (await context.Menus.AnyAsync()) return;

        var now = DateTime.UtcNow;

        var suppliersMenu = new Menu
        {
            Name = "Suppliers",
            Route = "/suppliers",
            CreatedAt = now,
            UpdatedAt = now,
            SubMenus =
            [
                new SubMenu { Name = "Suppliers", Route = "/suppliers", CreatedAt = now, UpdatedAt = now },
            ],
        };

        var purchasesMenu = new Menu
        {
            Name = "Purchases",
            Route = "/purchases",
            CreatedAt = now,
            UpdatedAt = now,
            SubMenus =
            [
                new SubMenu { Name = "Purchases", Route = "/purchases", CreatedAt = now, UpdatedAt = now },
            ],
        };

        var customersMenu = new Menu
        {
            Name = "Customers",
            Route = "/customer-fuel-givens",
            CreatedAt = now,
            UpdatedAt = now,
            SubMenus =
            [
                new SubMenu { Name = "Customers", Route = "/customer-fuel-givens", CreatedAt = now, UpdatedAt = now },
            ],
        };

        var accounting = new Menu
        {
            Name = "Accounting",
            Route = "/accounting/accounts",
            CreatedAt = now,
            UpdatedAt = now,
            SubMenus =
            [
                new SubMenu { Name = "Accounts", Route = "/accounting/accounts", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Charts of accounts", Route = "/accounting/charts-of-accounts", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Manual journal entry", Route = "/accounting/manual-journal-entry", CreatedAt = now, UpdatedAt = now },
            ],
        };

        var payments = new Menu
        {
            Name = "Payments",
            Route = "/accounting/customer-payments",
            CreatedAt = now,
            UpdatedAt = now,
            SubMenus =
            [
                new SubMenu { Name = "Customer payments", Route = "/accounting/customer-payments", CreatedAt = now, UpdatedAt = now },
            ],
        };

        var financialReports = new Menu
        {
            Name = "Financial reports",
            Route = "/financial-reports/trial-balance",
            CreatedAt = now,
            UpdatedAt = now,
            SubMenus =
            [
                new SubMenu { Name = "Trial balance", Route = "/financial-reports/trial-balance", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "General ledger", Route = "/financial-reports/general-ledger", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Income Statement", Route = "/financial-reports/profit-and-loss", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Balance sheet", Route = "/financial-reports/balance-sheet", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Capital Statement", Route = "/financial-reports/capital-statement", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Customer balances", Route = "/financial-reports/customer-balances", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Supplier balances", Route = "/financial-reports/supplier-balances", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Cash flow statement", Route = "/financial-reports/daily-cash-flow", CreatedAt = now, UpdatedAt = now },
            ],
        };

        var reports = new Menu
        {
            Name = "Reports",
            Route = "/reports",
            CreatedAt = now,
            UpdatedAt = now,
            SubMenus =
            [
                new SubMenu { Name = "Liter received", Route = "/reports/liter-received", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Daily cash sales report", Route = "/reports/daily-cash-sales", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Cash out daily (expenses)", Route = "/reports/cash-out-daily", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Expense reports", Route = "/reports/expenses", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Exchange reports", Route = "/reports/exchange", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Cash or USD Taken reports", Route = "/reports/cash-usd-taken", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Daily given fuel", Route = "/reports/daily-fuel-given", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "General daily report", Route = "/reports/general-daily", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Inventory daily", Route = "/reports/inventory-daily", CreatedAt = now, UpdatedAt = now },
                new SubMenu { Name = "Outstanding customers", Route = "/reports/outstanding-customers", CreatedAt = now, UpdatedAt = now },
            ],
        };

        await context.Menus.AddRangeAsync(
            suppliersMenu,
            purchasesMenu,
            customersMenu,
            accounting,
            payments,
            financialReports,
            reports);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Adds any missing menus that appear in the app sidebar so permissions can be assigned per route.
    /// Idempotent: skips routes that already exist on a non-deleted <see cref="SubMenu"/>.
    /// </summary>
    private static async Task EnsureSidebarMenusAsync(GasStationDBContext context)
    {
        var now = DateTime.UtcNow;
        (string MenuName, string Route, string SubName)[] sidebar =
        [
            ("Dashboard", "/", "Dashboard"),
            ("Expenses", "/expenses", "Expenses"),
            ("Cash or USD Taken", "/operations/cash-usd-taken", "Cash or USD Taken"),
            ("Exchange", "/operations/exchange", "Exchange"),
            ("Expenses", "/management/expenses", "Expenses"),
            ("Exchange", "/management/exchange", "Exchange"),
            ("Inventory", "/inventory", "Inventory"),
            ("Rates", "/rates", "Rates"),
            ("Generator usage", "/generator-usage", "Generator usage"),
            ("DippingPump", "/dipping-pumps", "DippingPump"),
            ("Pumps", "/pumps", "Pumps"),
            ("Dipping", "/dipping", "Dipping"),
            ("Liter received", "/liter-received", "Liter received"),
            ("Roles", "/setup/roles", "Roles"),
            ("Users", "/setup/users", "Users"),
            ("Assigning Station", "/setup/business-users", "Assigning Station"),
            ("Businesses", "/setup/businesses", "Businesses"),
            ("Stations", "/stations", "Stations"),
            ("Menus", "/setup/menus", "Menus"),
            ("Submenus", "/setup/submenus", "Submenus"),
            ("Permissions", "/setup/permissions", "Permissions"),
            // ("Fuel types", "/setup/fuel-types", "Fuel types"),
            ("Currencies", "/setup/currencies", "Currencies"),
            // ("Fuel prices", "/setup/fuel-prices", "Fuel prices"),
            ("Settings", "/setup/settings", "Settings"),
            ("Business fuel pool", "/fuel-inventory", "Business fuel pool"),
            ("Transfer fuels", "/transfers", "Transfer fuels"),
            // ("Reports", "/reports/outstanding-customers", "Outstanding customers"),
            ("Expense reports", "/reports/expenses", "Expense reports"),
            ("Exchange reports", "/reports/exchange", "Exchange reports"),
            ("Cash or USD Taken reports", "/reports/cash-usd-taken", "Cash or USD Taken reports"),
        ];

        foreach (var (menuName, route, subName) in sidebar)
        {
            var r = route.Trim();
            var exists = await context.SubMenus.AsNoTracking()
                .AnyAsync(s => !s.IsDeleted && s.Route == r);
            if (exists) continue;

            context.Menus.Add(new Menu
            {
                Name = menuName,
                Route = r,
                CreatedAt = now,
                UpdatedAt = now,
                SubMenus =
                [
                    new SubMenu { Name = subName, Route = r, CreatedAt = now, UpdatedAt = now },
                ],
            });
        }

        await context.SaveChangesAsync();
    }
}
