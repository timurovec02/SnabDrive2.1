using Microsoft.EntityFrameworkCore;
using SnabDrive.Web.Data.Entities;

namespace SnabDrive.Web.Data;

/// <summary>
/// Контекст существующих таблиц реестра (SnabDriveDB).
///
/// ВАЖНО: этот контекст НЕ владеет миграциями и никогда не вызывает Database.Migrate() —
/// схема [Regedit], [ArchiveRegedit], [User], [B2BStatus], [TypeOfPurchase], [ExecutionStatus],
/// [CellColors] уже существует в боевой базе и создавалась WPF-клиентом.
/// Все новые таблицы (Identity, AuditLog) вынесены в <see cref="AppIdentityDbContext"/>.
/// </summary>
public class RegistryDbContext : DbContext
{
    public RegistryDbContext(DbContextOptions<RegistryDbContext> options)
        : base(options)
    {
    }

    public DbSet<Regedit> Regedit => Set<Regedit>();
    public DbSet<ArchiveRegedit> ArchiveRegedit => Set<ArchiveRegedit>();
    public DbSet<B2BStatus> B2BStatuses => Set<B2BStatus>();
    public DbSet<TypeOfPurchase> TypesOfPurchase => Set<TypeOfPurchase>();
    public DbSet<ExecutionStatus> ExecutionStatuses => Set<ExecutionStatus>();
    public DbSet<CellColor> CellColors => Set<CellColor>();
    public DbSet<LegacyUser> LegacyUsers => Set<LegacyUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Regedit>(entity =>
        {
            entity.ToTable("Regedit");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.NameLink).HasColumnName("NameLink").HasMaxLength(1000).IsRequired();
            entity.Property(x => x.PlaceOfDelivery).HasColumnName("PlaceOfDelivery").HasMaxLength(1000);
            entity.Property(x => x.ReserveNumber).HasColumnName("ReserveNumber").HasMaxLength(1000);
            entity.Property(x => x.NationalMode).HasColumnName("NationalMode").HasMaxLength(1000);

            entity.Property(x => x.DateOfTransferForPlacement).HasColumnName("DateOfTransferForPlacement");
            entity.Property(x => x.DateOfPlacement).HasColumnName("DateOfPlacement");
            entity.Property(x => x.BiddingDate).HasColumnName("BiddingDate");
            entity.Property(x => x.DateResults).HasColumnName("DateResults");
            entity.Property(x => x.DateOfConclusionOfTheContract).HasColumnName("DateOfConclusionOfTheContract");

            entity.Property(x => x.NMCK).HasColumnName("NMCK").HasPrecision(18, 2);
            entity.Property(x => x.MinPrice).HasColumnName("MinPrice").HasPrecision(18, 2);
            entity.Property(x => x.ResultPrice).HasColumnName("ResultPrice").HasPrecision(18, 2);

            entity.Property(x => x.Winner).HasColumnName("Winner");
            entity.Property(x => x.DeliveryTime).HasColumnName("DeliveryTime");
            entity.Property(x => x.Description).HasColumnName("Description");
            entity.Property(x => x.Note).HasColumnName("Note");
            entity.Property(x => x.Customer).HasColumnName("Customer");

            entity.Property(x => x.TypeOfPurchaseId).HasColumnName("TypeOfPurchaseId");
            entity.Property(x => x.B2BStatusId).HasColumnName("B2BStatusId");
            entity.Property(x => x.ExecutionStatusId).HasColumnName("ExecutionStatusId");
            entity.Property(x => x.IsFinished).HasColumnName("IsFinished");

            entity.HasOne(x => x.TypeOfPurchase)
                  .WithMany()
                  .HasForeignKey(x => x.TypeOfPurchaseId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.B2BStatus)
                  .WithMany()
                  .HasForeignKey(x => x.B2BStatusId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ExecutionStatus)
                  .WithMany()
                  .HasForeignKey(x => x.ExecutionStatusId)
                  .OnDelete(DeleteBehavior.Restrict);

            // Индексы под типовые запросы реестра (создаются только если база пустая/dev).
            entity.HasIndex(x => x.Customer);
            entity.HasIndex(x => x.BiddingDate);
        });

        modelBuilder.Entity<ArchiveRegedit>(entity =>
        {
            entity.ToTable("ArchiveRegedit");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IdOld).HasColumnName("IdOld");

            entity.Property(x => x.NameLink).HasColumnName("NameLink").HasMaxLength(1000).IsRequired();
            entity.Property(x => x.PlaceOfDelivery).HasColumnName("PlaceOfDelivery").HasMaxLength(1000);
            entity.Property(x => x.ReserveNumber).HasColumnName("ReserveNumber").HasMaxLength(1000);
            entity.Property(x => x.NationalMode).HasColumnName("NationalMode").HasMaxLength(1000);

            entity.Property(x => x.DateOfTransferForPlacement).HasColumnName("DateOfTransferForPlacement");
            entity.Property(x => x.DateOfPlacement).HasColumnName("DateOfPlacement");
            entity.Property(x => x.BiddingDate).HasColumnName("BiddingDate");
            entity.Property(x => x.DateResults).HasColumnName("DateResults");
            entity.Property(x => x.DateOfConclusionOfTheContract).HasColumnName("DateOfConclusionOfTheContract");

            entity.Property(x => x.NMCK).HasColumnName("NMCK").HasPrecision(18, 2);
            entity.Property(x => x.MinPrice).HasColumnName("MinPrice").HasPrecision(18, 2);
            entity.Property(x => x.ResultPrice).HasColumnName("ResultPrice").HasPrecision(18, 2);

            entity.Property(x => x.Winner).HasColumnName("Winner");
            entity.Property(x => x.DeliveryTime).HasColumnName("DeliveryTime");
            entity.Property(x => x.Description).HasColumnName("Description");
            entity.Property(x => x.Note).HasColumnName("Note");
            entity.Property(x => x.Customer).HasColumnName("Customer");

            entity.Property(x => x.TypeOfPurchaseId).HasColumnName("TypeOfPurchaseId");
            entity.Property(x => x.B2BStatusId).HasColumnName("B2BStatusId");
            entity.Property(x => x.ExecutionStatusId).HasColumnName("ExecutionStatusId");
            entity.Property(x => x.ArchivateDate).HasColumnName("ArchivateDate");

            entity.HasOne(x => x.TypeOfPurchase)
                  .WithMany()
                  .HasForeignKey(x => x.TypeOfPurchaseId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.B2BStatus)
                  .WithMany()
                  .HasForeignKey(x => x.B2BStatusId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.ExecutionStatus)
                  .WithMany()
                  .HasForeignKey(x => x.ExecutionStatusId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<B2BStatus>(entity =>
        {
            entity.ToTable("B2BStatus");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("ID");
            entity.Property(x => x.NameB2B).HasColumnName("NameB2B").IsRequired();
        });

        modelBuilder.Entity<TypeOfPurchase>(entity =>
        {
            entity.ToTable("TypeOfPurchase");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("ID");
            entity.Property(x => x.NameOfPurchase).HasColumnName("NameOfPurchase").IsRequired();
            entity.Property(x => x.ColorCode).HasColumnName("ColorCode");
        });

        modelBuilder.Entity<ExecutionStatus>(entity =>
        {
            entity.ToTable("ExecutionStatus");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("ID");
            entity.Property(x => x.NameExecution).HasColumnName("NameExecution").IsRequired();
        });

        modelBuilder.Entity<CellColor>(entity =>
        {
            entity.ToTable("CellColors");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RegeditId).HasColumnName("RegeditId");
            entity.Property(x => x.ColumnName).HasColumnName("ColumnName").IsRequired();
            entity.Property(x => x.ColorCode).HasColumnName("ColorCode").IsRequired();
            entity.HasIndex(x => x.RegeditId);

            entity.HasOne(x => x.Regedit)
                  .WithMany()
                  .HasForeignKey(x => x.RegeditId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LegacyUser>(entity =>
        {
            entity.ToTable("User");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Login).HasColumnName("Login").IsRequired();
            entity.Property(x => x.Password).HasColumnName("Password");
            entity.Property(x => x.Email).HasColumnName("Email");
            entity.Property(x => x.IsAdmin).HasColumnName("IsAdmin");
        });
    }
}
