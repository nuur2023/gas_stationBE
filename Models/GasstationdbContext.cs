using Microsoft.EntityFrameworkCore;

namespace gas_station.Models;

/// <summary>
/// Legacy scaffolded context moved from DbModels folder.
/// Keep only for compatibility/reference; active app context is Data/Context/GasStationDBContext.
/// </summary>
public partial class GasstationdbContext : DbContext
{
    public GasstationdbContext()
    {
    }

    public GasstationdbContext(DbContextOptions<GasstationdbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Account> Accounts { get; set; }
    public virtual DbSet<Business> Businesses { get; set; }
    public virtual DbSet<BusinessUser> BusinessUsers { get; set; }
    public virtual DbSet<ChartsOfAccounts> ChartsOfAccounts { get; set; }
    public virtual DbSet<Currency> Currencies { get; set; }
    public virtual DbSet<CustomerFuelGiven> CustomerFuelGivens { get; set; }
    public virtual DbSet<CustomerPayment> CustomerPayments { get; set; }
    public virtual DbSet<Dipping> Dippings { get; set; }
    public virtual DbSet<DippingPump> DippingPumps { get; set; }
    public virtual DbSet<EfmigrationsHistory> EfmigrationsHistories { get; set; }
    public virtual DbSet<Expense> Expenses { get; set; }
    public virtual DbSet<FuelPrice> FuelPrices { get; set; }
    public virtual DbSet<FuelType> FuelTypes { get; set; }
    public virtual DbSet<GeneratorUsage> GeneratorUsages { get; set; }
    public virtual DbSet<InventoryItem> InventoryItems { get; set; }
    public virtual DbSet<InventorySale> InventorySales { get; set; }
    public virtual DbSet<JournalEntry> JournalEntries { get; set; }
    public virtual DbSet<JournalEntryLine> JournalEntryLines { get; set; }
    public virtual DbSet<LiterReceived> LiterReceiveds { get; set; }
    public virtual DbSet<Menu> Menus { get; set; }
    public virtual DbSet<Nozzle> Nozzles { get; set; }
    public virtual DbSet<Permission> Permissions { get; set; }
    public virtual DbSet<Pump> Pumps { get; set; }
    public virtual DbSet<Purchase> Purchases { get; set; }
    public virtual DbSet<PurchaseItem> PurchaseItems { get; set; }
    public virtual DbSet<Rate> Rates { get; set; }
    public virtual DbSet<Role> Roles { get; set; }
    public virtual DbSet<Station> Stations { get; set; }
    public virtual DbSet<SubMenu> SubMenus { get; set; }
    public virtual DbSet<Supplier> Suppliers { get; set; }
    public virtual DbSet<User> Users { get; set; }
}
