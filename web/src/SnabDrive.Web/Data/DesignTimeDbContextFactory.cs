using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SnabDrive.Web.Data;

/// <summary>
/// Фабрика для `dotnet ef migrations add` / `dotnet ef database update`.
/// Строка подключения берётся из переменной окружения SNABDRIVE_CONNECTION,
/// иначе — заглушка: для генерации миграций реальное подключение не нужно.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppIdentityDbContext>
{
    public AppIdentityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppIdentityDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("SNABDRIVE_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=snabdrive_design_time;Trusted_Connection=True;MultipleActiveResultSets=True";

        optionsBuilder.UseSqlServer(connectionString);

        return new AppIdentityDbContext(optionsBuilder.Options);
    }
}
