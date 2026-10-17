
using FinCore.Api.Data;
using FinCore.Api.DTOs;
using FinCore.Api.Models;
using FinCore.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FinCore.Api.Exceptions;

namespace FinCore.Tests;

public class AccountsServiceTests
{
    private static readonly Guid TestCustomerId =
    Guid.Parse("d03a1789-ac4f-49d5-b349-057821df4548");

    private static async Task AddTestCustomerAsync(AppDbContext dbContext)
    {
        var customerExists = await dbContext.Customers
            .AnyAsync(c => c.Id == TestCustomerId);

        if (customerExists)
        {
            return;
        }

        var customer = new Customer
        {
            Id = TestCustomerId,
            CustomerNumber = "TEST-CUSTOMER-001",
            FullName = "Mehmet Yavuz"
        };

        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_CreatesAccountWithZeroBalance()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var request = new CreateAccountRequest
        {
            OwnerName = "Mehmet Yavuz",
            Currency = "TRY"
        };

        var customerExists = await dbContext.Customers
            .AnyAsync(x => x.Id == TestCustomerId);

        Assert.True(customerExists, "Test müşterisi veritabanına eklenemedi.");

        var result = await service.CreateAsync(request, TestCustomerId);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Mehmet Yavuz", result.OwnerName);
        Assert.Equal("TRY", result.Currency);
        Assert.Equal(0m, result.Balance);

        var savedAccount = await dbContext.Accounts
            .FirstOrDefaultAsync(x => x.Id == result.Id);

        Assert.NotNull(savedAccount);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingId_ReturnsNull()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var result = await service.GetByIdAsync(Guid.NewGuid(), TestCustomerId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_ExistingAccounts_ReturnsAllAccounts()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new AppDbContext(options);
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Mehmet Yavuz",
            Currency = "TRY"
        }, TestCustomerId);

        await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Ahmet Demir",
            Currency = "USD"
        }, TestCustomerId);

        var accounts = await service.GetAllAsync(TestCustomerId);

        Assert.Equal(2, accounts.Count);
        Assert.Contains(accounts, x => x.OwnerName == "Mehmet Yavuz");
        Assert.Contains(accounts, x => x.OwnerName == "Ahmet Demir");
    }

    [Fact]
    public async Task DepositAsync_ValidAmount_UpdatesBalanceAndCreatesLedgerEntry()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);

        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var account = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Mehmet Yavuz",
            Currency = "TRY"
        }, TestCustomerId);

        var result = await service.DepositAsync(account.Id, TestCustomerId, 500m);

        Assert.True(result);

        var savedAccount = await dbContext.Accounts
            .AsNoTracking()
            .SingleAsync(x => x.Id == account.Id);

        Assert.Equal(500m, savedAccount.Balance);

        var ledgerEntry = await dbContext.LedgerEntries
            .SingleOrDefaultAsync(x => x.AccountId == account.Id);

        Assert.NotNull(ledgerEntry);
        Assert.Equal(500m, ledgerEntry.Amount);
        Assert.Equal(LedgerEntryType.Deposit, ledgerEntry.Type);
        Assert.Equal(LedgerDirection.Credit, ledgerEntry.Direction);
        Assert.Equal("TRY", ledgerEntry.Currency);
    }

    [Fact]
    public async Task DepositAsync_NonExistingAccount_ReturnsFalse()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var result = await service.DepositAsync(Guid.NewGuid(), TestCustomerId, 500m);

        Assert.False(result);
        Assert.Empty(await dbContext.LedgerEntries.ToListAsync());
    }

    [Fact]
    public async Task WithdrawAsync_ValidAmount_UpdatesBalanceAndCreatesLedgerEntry()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);

        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var account = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Mehmet Yavuz",
            Currency = "TRY"
        }, TestCustomerId);

        await service.DepositAsync(account.Id, TestCustomerId, 500m);

        var result = await service.WithdrawAsync(account.Id, TestCustomerId, 200m);

        Assert.True(result);

        var savedAccount = await dbContext.Accounts
            .AsNoTracking()
            .SingleAsync(x => x.Id == account.Id);

        Assert.Equal(300m, savedAccount.Balance);

        var ledgerEntries = await dbContext.LedgerEntries
            .Where(x => x.AccountId == account.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();

        Assert.Equal(2, ledgerEntries.Count);
        Assert.Equal(LedgerEntryType.Deposit, ledgerEntries[0].Type);
        Assert.Equal(LedgerEntryType.Withdrawal, ledgerEntries[1].Type);
        Assert.Equal(200m, ledgerEntries[1].Amount);
        Assert.Equal(LedgerDirection.Debit, ledgerEntries[1].Direction);
    }

    [Fact]
    public async Task WithdrawAsync_InsufficientBalance_ThrowsAndDoesNotChangeBalance()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var account = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Mehmet Yavuz",
            Currency = "TRY"
        }, TestCustomerId);

        await service.DepositAsync(account.Id, TestCustomerId, 100m);

        await Assert.ThrowsAsync<BusinessException>(
            () => service.WithdrawAsync(account.Id, TestCustomerId, 150m));

        var savedAccount = await dbContext.Accounts
            .AsNoTracking()
            .SingleAsync(x => x.Id == account.Id);

        Assert.Equal(100m, savedAccount.Balance);

        var ledgerEntries = await dbContext.LedgerEntries
            .Where(x => x.AccountId == account.Id)
            .ToListAsync();

        Assert.Single(ledgerEntries);
        Assert.Equal(LedgerEntryType.Deposit, ledgerEntries[0].Type);
    }

    [Fact]
    public async Task TransferAsync_ValidTransfer_UpdatesBalancesAndCreatesLedgerEntries()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);

        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var tables = await dbContext.Database
            .SqlQueryRaw<string>(
                "SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
            .ToListAsync();

        Assert.Contains("Customers", tables);

        var customerExists = await dbContext.Customers
            .AnyAsync(x => x.Id == TestCustomerId);

        Assert.True(customerExists, "Test müşterisi veritabanına eklenemedi.");

        var service = new AccountsService(dbContext);

        var source = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Mehmet Yavuz",
            Currency = "TRY"
        }, TestCustomerId);

        var destination = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Ahmet Demir",
            Currency = "TRY"
        }, TestCustomerId);

        await service.DepositAsync(source.Id, TestCustomerId, 1000m);

        var result = await service.TransferAsync(
            new TransferRequest
            {
                SourceAccountId = source.Id,
                DestinationAccountId = destination.Id,
                Amount = 300m
            },
            TestCustomerId,
            "valid-transfer-test-001");

        Assert.True(result);

        var sourceBalance = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == source.Id)
            .Select(x => x.Balance)
            .SingleAsync();

        var destinationBalance = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == destination.Id)
            .Select(x => x.Balance)
            .SingleAsync();

        Assert.Equal(700m, sourceBalance);
        Assert.Equal(300m, destinationBalance);

        var transferEntries = await dbContext.LedgerEntries
            .Where(x => x.Type == LedgerEntryType.Transfer)
            .ToListAsync();

        Assert.Equal(2, transferEntries.Count);
        Assert.All(
            transferEntries,
            x => Assert.Equal(300m, x.Amount));
    }

    [Fact]
    public async Task TransferAsync_InsufficientBalance_DoesNotChangeBalances()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var source = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Mehmet Yavuz",
            Currency = "TRY"
        },TestCustomerId);

        var destination = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Ahmet Demir",
            Currency = "TRY"
        }, TestCustomerId);

        await service.DepositAsync(source.Id, TestCustomerId, 100m);

        await Assert.ThrowsAsync<BusinessException>(
            () => service.TransferAsync(
                new TransferRequest
                {
                    SourceAccountId = source.Id,
                    DestinationAccountId = destination.Id,
                    Amount = 150m
                },
                TestCustomerId,
                "insufficient-balance-test-key"));

        var sourceBalance = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == source.Id)
            .Select(x => x.Balance)
            .SingleAsync();

        var destinationBalance = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == destination.Id)
            .Select(x => x.Balance)
            .SingleAsync();

        Assert.Equal(100m, sourceBalance);
        Assert.Equal(0m, destinationBalance);

        Assert.Empty(await dbContext.LedgerEntries
            .Where(x => x.Type == LedgerEntryType.Transfer)
            .ToListAsync());

        Assert.Empty(await dbContext.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task TransferAsync_DifferentCurrencies_ThrowsAndDoesNotChangeBalances()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var source = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Mehmet Yavuz",
            Currency = "TRY"
        }, TestCustomerId);

        var destination = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Ahmet Demir",
            Currency = "USD"
        }, TestCustomerId);

        await service.DepositAsync(source.Id, TestCustomerId, 1000m);

        await Assert.ThrowsAsync<BusinessException>(
            () => service.TransferAsync(
                new TransferRequest
                {
                    SourceAccountId = source.Id,
                    DestinationAccountId = destination.Id,
                    Amount = 100m
                },
                TestCustomerId,
                "different-currencies-test-key"));

        var sourceBalance = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == source.Id)
            .Select(x => x.Balance)
            .SingleAsync();

        var destinationBalance = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == destination.Id)
            .Select(x => x.Balance)
            .SingleAsync();

        Assert.Equal(1000m, sourceBalance);
        Assert.Equal(0m, destinationBalance);

        Assert.Empty(await dbContext.LedgerEntries
            .Where(x => x.Type == LedgerEntryType.Transfer)
            .ToListAsync());

        Assert.Empty(await dbContext.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task TransferAsync_SameAccount_ThrowsArgumentException()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var account = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Mehmet Yavuz",
            Currency = "TRY"
        }, TestCustomerId);

        await service.DepositAsync(account.Id, TestCustomerId, 500m);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.TransferAsync(
                new TransferRequest
                {
                    SourceAccountId = account.Id,
                    DestinationAccountId = account.Id,
                    Amount = 100m
                },
                TestCustomerId,
                "same-account-test-key"));

        var savedAccount = await dbContext.Accounts
            .AsNoTracking()
            .SingleAsync(x => x.Id == account.Id);

        Assert.Equal(500m, savedAccount.Balance);

        Assert.Empty(await dbContext.LedgerEntries
            .Where(x => x.Type == LedgerEntryType.Transfer)
            .ToListAsync());

        Assert.Empty(await dbContext.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task TransferAsync_NonExistingAccount_ReturnsFalse()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var existingAccount = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Mehmet Yavuz",
            Currency = "TRY"
        }, TestCustomerId);

        await service.DepositAsync(existingAccount.Id, TestCustomerId, 500m);

        var result = await service.TransferAsync(
            new TransferRequest
            {
                SourceAccountId = existingAccount.Id,
                DestinationAccountId = Guid.NewGuid(),
                Amount = 100m
            },
            TestCustomerId,
            "non-existing-account-test-key");

        Assert.False(result);

        var savedAccount = await dbContext.Accounts
            .AsNoTracking()
            .SingleAsync(x => x.Id == existingAccount.Id);

        Assert.Equal(500m, savedAccount.Balance);

        Assert.Empty(await dbContext.LedgerEntries
            .Where(x => x.Type == LedgerEntryType.Transfer)
            .ToListAsync());

        Assert.Empty(await dbContext.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task TransferAsync_SameKey_DoesNotTransferTwice()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var source = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Mehmet Yavuz",
            Currency = "TRY"
        },TestCustomerId);

        var destination = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Ahmet Demir",
            Currency = "TRY"
        }, TestCustomerId);

        await service.DepositAsync(source.Id, TestCustomerId, 1000m);

        var request = new TransferRequest
        {
            SourceAccountId = source.Id,
            DestinationAccountId = destination.Id,
            Amount = 300m
        };

        var firstResult = await service.TransferAsync(
            request, TestCustomerId, "same-transfer-key");

        var secondResult = await service.TransferAsync(
            request, TestCustomerId, "same-transfer-key");

        Assert.True(firstResult);
        Assert.True(secondResult);

        var sourceBalance = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == source.Id)
            .Select(x => x.Balance)
            .SingleAsync();

        var destinationBalance = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == destination.Id)
            .Select(x => x.Balance)
            .SingleAsync();

        Assert.Equal(700m, sourceBalance);
        Assert.Equal(300m, destinationBalance);

        var transferEntries = await dbContext.LedgerEntries
            .Where(x => x.Type == LedgerEntryType.Transfer)
            .ToListAsync();

        Assert.Equal(2, transferEntries.Count);

        Assert.Single(await dbContext.IdempotencyRecords
            .Where(x => x.Key == "same-transfer-key")
            .ToListAsync());
    }


    [Fact]
    public async Task TransferAsync_SameKeyDifferentRequest_ThrowsAndDoesNotTransferAgain()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var source = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Mehmet Yavuz",
            Currency = "TRY"
        }, TestCustomerId);

        var destination = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Ahmet Demir",
            Currency = "TRY"
        }, TestCustomerId);

        await service.DepositAsync(source.Id, TestCustomerId, 1000m);

        // İlk istek: 300 TL transfer et.
        var firstRequest = new TransferRequest
        {
            SourceAccountId = source.Id,
            DestinationAccountId = destination.Id,
            Amount = 300m
        };

        await service.TransferAsync(
            firstRequest,
            TestCustomerId,
            "same-key-different-request-test");

        // İkinci istek: Aynı anahtarla 400 TL göndermeyi dene.
        var secondRequest = new TransferRequest
        {
            SourceAccountId = source.Id,
            DestinationAccountId = destination.Id,
            Amount = 400m
        };

        await Assert.ThrowsAsync<BusinessException>(
            () => service.TransferAsync(
                secondRequest,
                TestCustomerId,
                "same-key-different-request-test"));

        // İlk transferin gerçekleştiğini, ikincisinin gerçekleşmediğini doğrula.
        var sourceBalance = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == source.Id)
            .Select(x => x.Balance)
            .SingleAsync();

        var destinationBalance = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == destination.Id)
            .Select(x => x.Balance)
            .SingleAsync();

        Assert.Equal(700m, sourceBalance);
        Assert.Equal(300m, destinationBalance);

        var transferEntries = await dbContext.LedgerEntries
            .Where(x => x.Type == LedgerEntryType.Transfer)
            .ToListAsync();

        Assert.Equal(2, transferEntries.Count);

        Assert.Single(await dbContext.IdempotencyRecords
            .Where(x => x.Key == "same-key-different-request-test")
            .ToListAsync());
    }

    [Fact]
    public async Task TransferAsync_DifferentKeys_TransfersTwice()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var source = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Mehmet Yavuz",
            Currency = "TRY"
        }, TestCustomerId);

        var destination = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Ahmet Demir",
            Currency = "TRY"
        }, TestCustomerId);

        await service.DepositAsync(source.Id, TestCustomerId, 1000m);

        var request = new TransferRequest
        {
            SourceAccountId = source.Id,
            DestinationAccountId = destination.Id,
            Amount = 200m
        };

        var firstResult = await service.TransferAsync(
            request, TestCustomerId, "different-key-first");

        var secondResult = await service.TransferAsync(
            request, TestCustomerId, "different-key-second");

        Assert.True(firstResult);
        Assert.True(secondResult);

        var sourceBalance = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == source.Id)
            .Select(x => x.Balance)
            .SingleAsync();

        var destinationBalance = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == destination.Id)
            .Select(x => x.Balance)
            .SingleAsync();

        Assert.Equal(600m, sourceBalance);
        Assert.Equal(400m, destinationBalance);

        Assert.Equal(
            4,
            await dbContext.LedgerEntries
                .CountAsync(x => x.Type == LedgerEntryType.Transfer));

        Assert.Equal(
            2,
            await dbContext.IdempotencyRecords.CountAsync());
    }

    [Fact]
    public async Task TransferAsync_ConcurrentSameKey_TransfersOnlyOnce()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("FINCORE_TEST_POSTGRES_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "FINCORE_TEST_POSTGRES_CONNECTION ortam değişkeni tanımlanmamış.");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        Guid sourceId;
        Guid destinationId;

        // 1. Test hesaplarını oluştur ve başlangıç bakiyesini yatır.
        await using (var setupContext = new AppDbContext(options))
        {
            await setupContext.Database.EnsureCreatedAsync();
            await AddTestCustomerAsync(setupContext);

            var customerExists = await setupContext.Customers
                .AnyAsync(c => c.Id == TestCustomerId);

            Assert.True(
                customerExists,
                $"Test müşterisi PostgreSQL veritabanında bulunamadı. CustomerId: {TestCustomerId}");

            var setupService = new AccountsService(setupContext);

            var source = await setupService.CreateAsync(
                new CreateAccountRequest
                {
                    OwnerName = "Concurrency Source",
                    Currency = "TRY"
                }, TestCustomerId);

            var destination = await setupService.CreateAsync(
                new CreateAccountRequest
                {
                    OwnerName = "Concurrency Destination",
                    Currency = "TRY"
                }, TestCustomerId);

            sourceId = source.Id;
            destinationId = destination.Id;

            await setupService.DepositAsync(sourceId, TestCustomerId, 1000m);
        }

        var request = new TransferRequest
        {
            SourceAccountId = sourceId,
            DestinationAccountId = destinationId,
            Amount = 300m
        };

        var idempotencyKey = $"concurrent-test-{Guid.NewGuid():N}";

        // 2. Her istek ayrı DbContext kullanır.
        async Task<bool> SendTransferAsync()
        {
            await using var context = new AppDbContext(options);
            var service = new AccountsService(context);

            return await service.TransferAsync(request, TestCustomerId, idempotencyKey);
        }

        // 3. Aynı transferi iki ayrı istekle eşzamanlı başlat.
        var results = await Task.WhenAll(
            SendTransferAsync(),
            SendTransferAsync());

        Assert.All(results, result => Assert.True(result));

        // 4. Veritabanındaki son durumu ayrı bir context ile doğrula.
        await using var verifyContext = new AppDbContext(options);

        var sourceBalance = await verifyContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == sourceId)
            .Select(x => x.Balance)
            .SingleAsync();

        var destinationBalance = await verifyContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == destinationId)
            .Select(x => x.Balance)
            .SingleAsync();

        Assert.Equal(700m, sourceBalance);
        Assert.Equal(300m, destinationBalance);

        // 5. Her hesap için yalnızca bir transfer defter kaydı olmalı.
        var sourceTransferCount = await verifyContext.LedgerEntries
            .CountAsync(x =>
                x.AccountId == sourceId &&
                x.Type == LedgerEntryType.Transfer);

        var destinationTransferCount = await verifyContext.LedgerEntries
            .CountAsync(x =>
                x.AccountId == destinationId &&
                x.Type == LedgerEntryType.Transfer);

        Assert.Equal(1, sourceTransferCount);
        Assert.Equal(1, destinationTransferCount);

        // 6. Aynı idempotency anahtarı için yalnızca bir kayıt olmalı.
        var idempotencyRecordCount =
            await verifyContext.IdempotencyRecords
                .CountAsync(x => x.Key == idempotencyKey);

        Assert.Equal(1, idempotencyRecordCount);
    }

    [Fact]
    public async Task LedgerEntries_BalanceChanges_MatchLedgerTotal()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var account = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Ledger Test",
            Currency = "TRY"
        }, TestCustomerId);

        await service.DepositAsync(account.Id, TestCustomerId, 1000m);
        await service.WithdrawAsync(account.Id, TestCustomerId, 250m);

        var balance = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == account.Id)
            .Select(x => x.Balance)
            .SingleAsync();

        var deposits = await dbContext.LedgerEntries
            .Where(x =>
                x.AccountId == account.Id &&
                x.Type == LedgerEntryType.Deposit)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        var withdrawals = await dbContext.LedgerEntries
            .Where(x =>
                x.AccountId == account.Id &&
                x.Type == LedgerEntryType.Withdrawal)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        Assert.Equal(750m, balance);
        Assert.Equal(1000m, deposits);
        Assert.Equal(250m, withdrawals);
        Assert.Equal(deposits - withdrawals, balance);
    }

    [Fact]
    public async Task TransferAsync_BalancesMatchLedgerEntries()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var source = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Ledger Source",
            Currency = "TRY"
        }, TestCustomerId);

        var destination = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Ledger Destination",
            Currency = "TRY"
        }, TestCustomerId);

        await service.DepositAsync(source.Id, TestCustomerId, 1000m);

        await service.TransferAsync(
            new TransferRequest
            {
                SourceAccountId = source.Id,
                DestinationAccountId = destination.Id,
                Amount = 300m
            },
            TestCustomerId,
            $"ledger-transfer-{Guid.NewGuid():N}");

        var sourceBalance = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == source.Id)
            .Select(x => x.Balance)
            .SingleAsync();

        var destinationBalance = await dbContext.Accounts
            .AsNoTracking()
            .Where(x => x.Id == destination.Id)
            .Select(x => x.Balance)
            .SingleAsync();

        var sourceTransferTotal = await dbContext.LedgerEntries
            .Where(x =>
                x.AccountId == source.Id &&
                x.Type == LedgerEntryType.Transfer)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        var destinationTransferTotal = await dbContext.LedgerEntries
            .Where(x =>
                x.AccountId == destination.Id &&
                x.Type == LedgerEntryType.Transfer)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        Assert.Equal(700m, sourceBalance);
        Assert.Equal(300m, destinationBalance);

        Assert.Equal(300m, sourceTransferTotal);
        Assert.Equal(300m, destinationTransferTotal);

        Assert.Equal(
            1000m,
            sourceBalance + sourceTransferTotal);

        Assert.Equal(
            destinationBalance,
            destinationTransferTotal);
    }

    [Fact]
    public async Task GetTransactionsAsync_ToDateIncludesEntireDay()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var account = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Test Kullanıcısı",
            Currency = "TRY"
        }, TestCustomerId);

        dbContext.LedgerEntries.AddRange(
            new LedgerEntry
            {
                AccountId = account.Id,
                Amount = 100m,
                Currency = "TRY",
                Type = LedgerEntryType.Deposit,
                Direction = LedgerDirection.Credit,
                CreatedAtUtc = new DateTime(
                    2026, 10, 10, 0, 0, 0, DateTimeKind.Utc)
            },
            new LedgerEntry
            {
                AccountId = account.Id,
                Amount = 200m,
                Currency = "TRY",
                Type = LedgerEntryType.Deposit,
                Direction = LedgerDirection.Credit,
                CreatedAtUtc = new DateTime(
                    2026, 10, 10, 23, 59, 59, DateTimeKind.Utc)
            },
            new LedgerEntry
            {
                AccountId = account.Id,
                Amount = 300m,
                Currency = "TRY",
                Type = LedgerEntryType.Deposit,
                Direction = LedgerDirection.Credit,
                CreatedAtUtc = new DateTime(
                    2026, 10, 11, 0, 0, 0, DateTimeKind.Utc)
            });

        await dbContext.SaveChangesAsync();

        var result = await service.GetTransactionsAsync(
            account.Id,
            TestCustomerId,
            page: 1,
            pageSize: 20,
            toDate: new DateTime(
                2026, 10, 10, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(
            result.Items,
            item => item.CreatedAtUtc ==
                new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc));
        Assert.Contains(
            result.Items,
            item => item.CreatedAtUtc ==
                new DateTime(2026, 10, 10, 23, 59, 59, DateTimeKind.Utc));
        Assert.DoesNotContain(
            result.Items,
            item => item.CreatedAtUtc ==
                new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc));
    }


    [Fact]
    public async Task GetTransactionsAsync_FiltersByAmountRange()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var account = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Tutar Testi",
            Currency = "TRY"
        }, TestCustomerId);

        dbContext.LedgerEntries.AddRange(
            new LedgerEntry
            {
                AccountId = account.Id,
                Amount = 50m,
                Currency = "TRY",
                Type = LedgerEntryType.Deposit,
                Direction = LedgerDirection.Credit
            },
            new LedgerEntry
            {
                AccountId = account.Id,
                Amount = 250m,
                Currency = "TRY",
                Type = LedgerEntryType.Deposit,
                Direction = LedgerDirection.Credit
            },
            new LedgerEntry
            {
                AccountId = account.Id,
                Amount = 600m,
                Currency = "TRY",
                Type = LedgerEntryType.Deposit,
                Direction = LedgerDirection.Credit
            });

        await dbContext.SaveChangesAsync();

        var result = await service.GetTransactionsAsync(
            account.Id,
            TestCustomerId,
            page: 1,
            pageSize: 20,
            minAmount: 100m,
            maxAmount: 500m);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(250m, result.Items[0].Amount);
    }

    [Fact]
    public async Task GetTransactionsAsync_CombinesMultipleFilters()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var account = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Birleşik Filtre Testi",
            Currency = "TRY"
        }, TestCustomerId);

        dbContext.LedgerEntries.AddRange(
            new LedgerEntry
            {
                AccountId = account.Id,
                Amount = 250m,
                Currency = "TRY",
                Type = LedgerEntryType.Deposit,
                Direction = LedgerDirection.Credit
            },
            new LedgerEntry
            {
                AccountId = account.Id,
                Amount = 600m,
                Currency = "TRY",
                Type = LedgerEntryType.Deposit,
                Direction = LedgerDirection.Credit
            },
            new LedgerEntry
            {
                AccountId = account.Id,
                Amount = 300m,
                Currency = "TRY",
                Type = LedgerEntryType.Withdrawal,
                Direction = LedgerDirection.Debit
            },
            new LedgerEntry
            {
                AccountId = account.Id,
                Amount = 100m,
                Currency = "TRY",
                Type = LedgerEntryType.Deposit,
                Direction = LedgerDirection.Credit
            });

        await dbContext.SaveChangesAsync();

        var result = await service.GetTransactionsAsync(
            account.Id,
            TestCustomerId,
            page: 1,
            pageSize: 20,
            type: "Deposit",
            minAmount: 200m,
            maxAmount: 500m);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(250m, result.Items[0].Amount);
        Assert.Equal("Deposit", result.Items[0].Type);
    }


    [Fact]
    public async Task GetTransactionsAsync_ReturnsCorrectPagination()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);

        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var account = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Sayfalama Testi",
            Currency = "TRY"
        }, TestCustomerId);

        for (var i = 1; i <= 5; i++)
        {
            dbContext.LedgerEntries.Add(new LedgerEntry
            {
                AccountId = account.Id,
                Amount = i * 100m,
                Currency = "TRY",
                Type = LedgerEntryType.Deposit,
                Direction = LedgerDirection.Credit,
                CreatedAtUtc = new DateTime(
                    2026, 10, 1, 0, 0, i, DateTimeKind.Utc)
            });
        }

        await dbContext.SaveChangesAsync();

        var result = await service.GetTransactionsAsync(
            account.Id,
            TestCustomerId,
            page: 2,
            pageSize: 2);

        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(2, result.Items.Count);

        Assert.Equal(300m, result.Items[0].Amount);
        Assert.Equal(200m, result.Items[1].Amount);
    }


    [Fact]
    public async Task GetTransactionsAsync_RejectsInvalidPage()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var account = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Geçersiz Sayfa Testi",
            Currency = "TRY"
        }, TestCustomerId);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetTransactionsAsync(
                account.Id,
                TestCustomerId,
                page: 0,
                pageSize: 20));
    }

    [Fact]
    public async Task GetTransactionsAsync_RejectsInvalidPageSize()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var account = await service.CreateAsync(new CreateAccountRequest
        {
            OwnerName = "Geçersiz Sayfa Boyutu Testi",
            Currency = "TRY"
        }, TestCustomerId);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetTransactionsAsync(
                account.Id,
                TestCustomerId,
                page: 1,
                pageSize: 101));
    }

    [Fact]
    public async Task GetTransactionsAsync_ThrowsWhenAccountDoesNotExist()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        await AddTestCustomerAsync(dbContext);

        var service = new AccountsService(dbContext);

        var nonExistingAccountId = Guid.NewGuid();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetTransactionsAsync(nonExistingAccountId, TestCustomerId));
    }

}
