using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SnabDrive.Web.Data.Entities;

namespace SnabDrive.Web.Data;

/// <summary>
/// Контекст веб-специфичных таблиц: ASP.NET Core Identity + журнал аудита.
/// Только этот контекст владеет миграциями — существующие таблицы реестра он не трогает.
/// </summary>
public class AppIdentityDbContext : IdentityDbContext<ApplicationUser>
{
    public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.DisplayName).HasMaxLength(200);
        });

        builder.Entity<AuditLogEntry>(entity =>
        {
            entity.ToTable("AuditLog");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("CreatedAtUtc");
            entity.Property(x => x.Action).HasColumnName("Action").HasMaxLength(50).IsRequired();
            entity.Property(x => x.EntityName).HasColumnName("EntityName").HasMaxLength(100).IsRequired();
            entity.Property(x => x.EntityId).HasColumnName("EntityId");
            entity.Property(x => x.UserId).HasColumnName("UserId").HasMaxLength(450);
            entity.Property(x => x.UserName).HasColumnName("UserName").HasMaxLength(256).IsRequired();
            entity.Property(x => x.Summary).HasColumnName("Summary").HasMaxLength(500);
            entity.Property(x => x.ChangesJson).HasColumnName("ChangesJson");

            entity.HasIndex(x => new { x.EntityName, x.EntityId });
            entity.HasIndex(x => x.CreatedAtUtc);
        });
    }
}
