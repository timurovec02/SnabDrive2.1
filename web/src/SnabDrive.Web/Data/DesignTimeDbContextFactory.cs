using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SnabDrive.Web.Data;

/// <summary>
/// Фабрика для `dotnet ef migrations add` / `dotnet ef database update`.
/// Строка подключения берётся из переменной окружения SNABDRIVE_CONNECTION,
/// иначе используется локальный SQLite-файл (для генерации миграций БД не нужна).
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppIdentityDbContext>
{
    public AppIdentityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppIdentityDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("SNABDRIVE_CONNECTION");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            optionsBuilder.UseSqlServer(connectionString);
        }
        else
        {
            optionsBuilder.UseSqlite("Data Source=design-time.db");
        }

        return new AppIdentityDbContext(optionsBuilder.Options);
    }
}
