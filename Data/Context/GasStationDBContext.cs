using gas_station.Models;
using Microsoft.EntityFrameworkCore;

namespace gas_station.Data.Context;

/// <summary>
/// PurchaseItems reference Purchases with ON DELETE CASCADE at the database (physical delete).
/// Normal deletes in this app use soft-delete on both tables.
/// </summary>
public class GasStationDBContext(DbContextOptions<GasStationDBContext> options) : DbContext(options)
{

    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<SubMenu> SubMenus => Set<SubMenu>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<FuelType> FuelTypes => Set<FuelType>();
    public DbSet<User> Users => Set<User>();
    public DbSet<BusinessUser> BusinessUsers => Set<BusinessUser>();
    public DbSet<Rate> Rates => Set<Rate>();
    public DbSet<Pump> Pumps => Set<Pump>();
    public DbSet<Nozzle> Nozzles => Set<Nozzle>();
    public DbSet<DippingPump> DippingPumps => Set<DippingPump>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<InventorySale> InventorySales => Set<InventorySale>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<GeneratorUsage> GeneratorUsages => Set<GeneratorUsage>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Dipping> Dippings => Set<Dipping>();
    public DbSet<LiterReceived> LiterReceiveds => Set<LiterReceived>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<FuelPrice> FuelPrices => Set<FuelPrice>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerFuelTransaction> CustomerFuelGivens => Set<CustomerFuelTransaction>();
    public DbSet<ChartsOfAccounts> ChartsOfAccounts => Set<ChartsOfAccounts>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
    public DbSet<CustomerPayment> CustomerPayments => Set<CustomerPayment>();
    public DbSet<BusinessFuelInventory> BusinessFuelInventories => Set<BusinessFuelInventory>();
    public DbSet<BusinessFuelInventoryCredit> BusinessFuelInventoryCredits => Set<BusinessFuelInventoryCredit>();
    public DbSet<TransferInventory> TransferInventories => Set<TransferInventory>();
    public DbSet<TransferInventoryAudit> TransferInventoryAudits => Set<TransferInventoryAudit>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeePayment> EmployeePayments => Set<EmployeePayment>();
    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();
    public DbSet<RecurringJournalEntry> RecurringJournalEntries => Set<RecurringJournalEntry>();
    public DbSet<AppNotification> AppNotifications => Set<AppNotification>();
    public DbSet<AppNotificationRead> AppNotificationReads => Set<AppNotificationRead>();

       protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JournalEntryLine>(e =>
        {
            e.HasOne(x => x.Customer)
                .WithMany(c => c.JournalEntryLines)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Supplier)
                .WithMany(s => s.JournalEntryLines)
                .HasForeignKey(x => x.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Permission>(e =>
        {
            e.HasOne(x => x.User)
                .WithMany(u => u.Permissions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Business)
                .WithMany(b => b.Permissions)
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InventorySale>(e =>
        {
            e.Property(x => x.ReferenceNumber).HasMaxLength(64);
            e.Property(x => x.EvidenceFilePath).HasMaxLength(512);
            e.Property(x => x.OriginalFileName).HasMaxLength(255);
            e.HasIndex(x => x.ReferenceNumber).IsUnique();
            e.HasIndex(x => new { x.BusinessId, x.StationId, x.RecordedDate });
        });

        modelBuilder.Entity<InventoryItem>(e =>
        {
            e.HasOne(x => x.InventorySale)
                .WithMany(s => s.InventoryItems)
                .HasForeignKey(x => x.InventorySaleId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.InventorySaleId, x.NozzleId }).IsUnique();
            e.HasIndex(x => new { x.NozzleId, x.Date });
        });

        modelBuilder.Entity<BusinessFuelInventory>(e =>
        {
            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<FuelType>()
                .WithMany()
                .HasForeignKey(x => x.FuelTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.BusinessId, x.FuelTypeId }).IsUnique();
        });

        modelBuilder.Entity<BusinessFuelInventoryCredit>(e =>
        {
            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<FuelType>()
                .WithMany()
                .HasForeignKey(x => x.FuelTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.CreatorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.Reference).HasMaxLength(256);
            e.Property(x => x.Note).HasMaxLength(2000);
            e.HasIndex(x => new { x.BusinessId, x.Date });
        });

        modelBuilder.Entity<TransferInventory>(e =>
        {
            e.HasOne<BusinessFuelInventory>()
                .WithMany()
                .HasForeignKey(x => x.BusinessFuelInventoryId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Station>()
                .WithMany()
                .HasForeignKey(x => x.ToStationId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.CreatorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.Note).HasMaxLength(2000);
            e.HasIndex(x => new { x.ToStationId, x.Date });
        });

        modelBuilder.Entity<TransferInventoryAudit>(e =>
        {
            e.HasOne<TransferInventory>()
                .WithMany()
                .HasForeignKey(x => x.TransferInventoryId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.ChangedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Station>()
                .WithMany()
                .HasForeignKey(x => x.ToStationId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.Action).HasMaxLength(32);
            e.Property(x => x.Reason).HasMaxLength(2000);
            e.HasIndex(x => new { x.TransferInventoryId, x.ChangedAt });
            e.HasIndex(x => new { x.BusinessId, x.ChangedAt });
        });

        modelBuilder.Entity<CustomerFuelTransaction>(e =>
        {
            e.Property(x => x.Type).HasMaxLength(16).HasDefaultValue("Fuel");
            e.HasIndex(x => new { x.BusinessId, x.Date });
            e.HasOne(x => x.Customer)
                .WithMany(c => c.FuelTransactions)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.BusinessId, x.CustomerId, x.Date });
        });

        modelBuilder.Entity<Customer>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(256).HasDefaultValue(string.Empty);
            e.Property(x => x.Phone).HasMaxLength(64).HasDefaultValue(string.Empty);
            e.HasIndex(x => new { x.BusinessId, x.StationId });
            e.HasIndex(x => new { x.BusinessId, x.Name, x.Phone });
        });

        modelBuilder.Entity<CustomerPayment>(e =>
        {
            e.HasOne(x => x.Customer)
                .WithMany()
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.ReferenceNo).HasMaxLength(256);
            e.Property(x => x.Description).HasMaxLength(32).HasDefaultValue("Payment");
            e.HasIndex(x => new { x.BusinessId, x.PaymentDate });
            e.HasIndex(x => new { x.BusinessId, x.CustomerId, x.PaymentDate })
                .HasDatabaseName("IX_CustomerPayments_Customer_Date");
            e.HasIndex(x => x.CustomerId);
        });

        modelBuilder.Entity<SupplierPayment>(e =>
        {
            e.HasOne<Supplier>()
                .WithMany()
                .HasForeignKey(x => x.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Purchase>()
                .WithMany()
                .HasForeignKey(x => x.PurchaseId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.ReferenceNo).HasMaxLength(256);
            e.Property(x => x.Description).HasMaxLength(32).HasDefaultValue("Payment");
            e.HasIndex(x => new { x.BusinessId, x.Date });
            e.HasIndex(x => new { x.BusinessId, x.SupplierId, x.Date });
            e.HasIndex(x => x.PurchaseId);
        });

        modelBuilder.Entity<AccountingPeriod>(e =>
        {
            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.Name).HasMaxLength(256);
            e.HasIndex(x => new { x.BusinessId, x.PeriodStart, x.PeriodEnd });
        });

        modelBuilder.Entity<Employee>(e =>
        {
            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Station>()
                .WithMany()
                .HasForeignKey(x => x.StationId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Phone).HasMaxLength(64).HasDefaultValue(string.Empty);
            e.Property(x => x.Email).HasMaxLength(256).HasDefaultValue(string.Empty);
            e.Property(x => x.Address).HasMaxLength(512).HasDefaultValue(string.Empty);
            e.Property(x => x.Position).HasMaxLength(128).HasDefaultValue(string.Empty);
            e.HasIndex(x => new { x.BusinessId, x.IsDeleted });
            e.HasIndex(x => new { x.BusinessId, x.StationId });
            e.HasIndex(x => new { x.BusinessId, x.Name });
        });

        modelBuilder.Entity<EmployeePayment>(e =>
        {
            e.HasOne<Employee>()
                .WithMany()
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Station>()
                .WithMany()
                .HasForeignKey(x => x.StationId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.ReferenceNo).HasMaxLength(256);
            e.Property(x => x.Description).HasMaxLength(32).HasDefaultValue("Payment");
            e.Property(x => x.PeriodLabel).HasMaxLength(16);
            e.HasIndex(x => new { x.BusinessId, x.PaymentDate });
            e.HasIndex(x => new { x.BusinessId, x.EmployeeId, x.PaymentDate })
                .HasDatabaseName("IX_EmployeePayments_Employee_Date");
            e.HasIndex(x => new { x.BusinessId, x.PeriodLabel });
        });

        modelBuilder.Entity<RecurringJournalEntry>(e =>
        {
            e.HasOne(x => x.DebitAccount)
                .WithMany()
                .HasForeignKey(x => x.DebitAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.CreditAccount)
                .WithMany()
                .HasForeignKey(x => x.CreditAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Station)
                .WithMany()
                .HasForeignKey(x => x.StationId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.Name).HasMaxLength(256);
            e.HasIndex(x => new { x.BusinessId, x.NextRunDate });
        });

        modelBuilder.Entity<AppNotification>(e =>
        {
            e.HasOne<Business>()
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<Station>()
                .WithMany()
                .HasForeignKey(x => x.StationId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.ConfirmedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<TransferInventory>()
                .WithMany()
                .HasForeignKey(x => x.TransferInventoryId)
                .OnDelete(DeleteBehavior.SetNull);

            e.Property(x => x.Title).HasMaxLength(256);
            e.Property(x => x.Body).HasMaxLength(4000);
            e.HasIndex(x => new { x.BusinessId, x.CreatedAt });
        });

        modelBuilder.Entity<AppNotificationRead>(e =>
        {
            e.HasOne(x => x.AppNotification)
                .WithMany()
                .HasForeignKey(x => x.AppNotificationId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.AppNotificationId, x.UserId }).IsUnique();
        });
    }
}
