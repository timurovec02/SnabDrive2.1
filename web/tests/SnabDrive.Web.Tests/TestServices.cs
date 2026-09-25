using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SnabDrive.Web.Data;
using SnabDrive.Web.Data.Entities;
using SnabDrive.Web.Domain;
using SnabDrive.Web.Services;

namespace SnabDrive.Web.Tests;

/// <summary>
/// Тестовая база: два независимых SQLite-соединения (реестр и Identity/аудит).
/// Соединения держим открытыми — иначе in-memory база исчезает.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _registryConnection;
    private readonly SqliteConnection _identityConnection;

    public IDbContextFactory<RegistryDbContext> RegistryFactory { get; }
    public IDbContextFactory<AppIdentityDbContext> IdentityFactory { get; }

    public TestDatabase()
    {
        _registryConnection = new SqliteConnection("DataSource=:memory:");
        _registryConnection.Open();

        _identityConnection = new SqliteConnection("DataSource=:memory:");
        _identityConnection.Open();

        var registryOptions = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite(_registryConnection)
            .Options;

        var identityOptions = new DbContextOptionsBuilder<AppIdentityDbContext>()
            .UseSqlite(_identityConnection)
            .Options;

        RegistryFactory = new TestDbContextFactory<RegistryDbContext>(() => new RegistryDbContext(registryOptions));
        IdentityFactory = new TestDbContextFactory<AppIdentityDbContext>(() => new AppIdentityDbContext(identityOptions));

        using var registry = RegistryFactory.CreateDbContext();
        registry.Database.EnsureCreated();

        using var identity = IdentityFactory.CreateDbContext();
        identity.Database.EnsureCreated();
    }

    public RegistryDbContext CreateRegistryContext() => RegistryFactory.CreateDbContext();

    public void Dispose()
    {
        _registryConnection.Dispose();
        _identityConnection.Dispose();
    }

    private sealed class TestDbContextFactory<TContext> : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        private readonly Func<TContext> _create;

        public TestDbContextFactory(Func<TContext> create) => _create = create;

        public TContext CreateDbContext() => _create();
    }
}

/// <summary>Заглушка нотификатора — собирает события, чтобы их можно было проверить.</summary>
public sealed class FakeNotifier : IRegistryNotifier
{
    public List<RegistryChangeEvent> Events { get; } = new();

    public Task PublishAsync(RegistryChangeEvent changeEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(changeEvent);
        return Task.CompletedTask;
    }
}

/// <summary>Общая обвязка сервисов для тестов.</summary>
public sealed class TestServices : IDisposable
{
    public TestDatabase Db { get; } = new();
    public FakeNotifier Notifier { get; } = new();
    public IAuditService Audit { get; }
    public IRegistryService Registry { get; }
    public IDictionaryService Dictionaries { get; }

    public static readonly ChangeActor Actor = new("user-1", "ivanov");

    public TestServices()
    {
        Audit = new AuditService(
            Db.IdentityFactory,
            NullLogger<AuditService>.Instance);

        Registry = new RegistryService(
            Db.RegistryFactory,
            Audit,
            Notifier,
            NullLogger<RegistryService>.Instance);

        Dictionaries = new DictionaryService(Db.RegistryFactory, Audit, Notifier);
    }

    /// <summary>Заполняет справочники и возвращает их Id.</summary>
    public async Task<(int Purchase, int B2B, int Execution)> SeedDictionariesAsync()
    {
        await using var context = Db.CreateRegistryContext();

        var purchase = new TypeOfPurchase { NameOfPurchase = "Аукцион", ColorCode = "#FF4CAF50" };
        var b2b = new B2BStatus { NameB2B = "Подан" };
        var execution = new ExecutionStatus { NameExecution = "В работе" };

        context.TypesOfPurchase.Add(purchase);
        context.B2BStatuses.Add(b2b);
        context.ExecutionStatuses.Add(execution);
        await context.SaveChangesAsync();

        return (purchase.Id, b2b.Id, execution.Id);
    }

    public async Task<Regedit> AddRegeditAsync(Action<Regedit>? configure = null)
    {
        await using var context = Db.CreateRegistryContext();

        var entity = new Regedit
        {
            NameLink = "Поставка оборудования",
            Customer = "Заказчик",
            PlaceOfDelivery = "Москва",
            NMCK = 100m,
            BiddingDate = new DateTime(2025, 3, 10)
        };

        configure?.Invoke(entity);

        context.Regedit.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }

    public static RegeditUpsertRequest ValidRequest() => new()
    {
        NameLink = "Поставка оборудования",
        Customer = "Заказчик",
        NMCK = 100m,
        MinPrice = 90m,
        ResultPrice = 0m,
        BiddingDate = new DateTime(2025, 3, 10)
    };

    public void Dispose() => Db.Dispose();
}
