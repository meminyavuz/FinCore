
using FinCore.Api.Data;
using FinCore.Api.DTOs;
using FinCore.Api.Exceptions;
using FinCore.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FinCore.Api.Services;

public sealed class AccountsService
{
    private readonly AppDbContext _dbContext;

    public AccountsService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AccountResponse> CreateAsync(
        CreateAccountRequest request,
        Guid customerId)
    {
        var customerExists = await _dbContext.Customers
            .AnyAsync(c => c.Id == customerId);

        if (!customerExists)
            throw new KeyNotFoundException("Müşteri bulunamadı.");

        var account = new Account
        {
            OwnerName = request.OwnerName.Trim(),
            Currency = request.Currency.Trim().ToUpperInvariant(),
            Balance = 0m,
            CustomerId = customerId
        };

        _dbContext.Accounts.Add(account);
        await _dbContext.SaveChangesAsync();

        return new AccountResponse(
            account.Id,
            account.OwnerName,
            account.Currency,
            account.Balance,
            account.CreatedAtUtc);
    }

    public async Task<Guid?> GetCustomerIdByUserIdAsync(Guid userId)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (Guid?)u.CustomerId)
            .FirstOrDefaultAsync();
    }

    public async Task<AccountResponse?> GetByIdAsync(
        Guid id,
        Guid customerId)
    {
        var account = await _dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.CustomerId == customerId);

        if (account is null)
            return null;

        return new AccountResponse(
            account.Id,
            account.OwnerName,
            account.Currency,
            account.Balance,
            account.CreatedAtUtc);
    }

    public async Task<List<AccountResponse>> GetAllAsync(Guid customerId)
    {
        return await _dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new AccountResponse(
                a.Id,
                a.OwnerName,
                a.Currency,
                a.Balance,
                a.CreatedAtUtc))
            .ToListAsync();
    }

    public async Task<PagedResponse<LedgerEntryResponse>> GetTransactionsAsync(
        Guid accountId,
        Guid customerId,
        int page = 1,
        int pageSize = 20,
        string? type = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        decimal? minAmount = null,
        decimal? maxAmount = null)
    {
        if (page < 1)
            throw new ArgumentException("Sayfa numarası en az 1 olmalıdır.");

        if (pageSize < 1 || pageSize > 100)
            throw new ArgumentException("Sayfa boyutu 1 ile 100 arasında olmalıdır.");

        var accountExists = await _dbContext.Accounts
            .AnyAsync(a =>
                a.Id == accountId &&
                a.CustomerId == customerId);

        if (!accountExists)
            throw new KeyNotFoundException("Hesap bulunamadı.");

        var query = _dbContext.LedgerEntries
            .AsNoTracking()
            .Where(entry => entry.AccountId == accountId);

        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!Enum.TryParse<LedgerEntryType>(type, true, out var parsedType)
                || !Enum.IsDefined(parsedType))
            {
                throw new ArgumentException(
                    "Geçersiz işlem türü. Deposit, Withdrawal veya Transfer kullanın.");
            }

            query = query.Where(entry => entry.Type == parsedType);
        }

        if (fromDate.HasValue && toDate.HasValue &&
            fromDate.Value.Date > toDate.Value.Date)
        {
            throw new ArgumentException(
                "Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        }

        if (fromDate.HasValue)
        {
            var startDate = fromDate.Value.Date;

            query = query.Where(entry =>
                entry.CreatedAtUtc >= startDate);
        }

        if (toDate.HasValue)
        {
            var endDateExclusive = toDate.Value.Date.AddDays(1);

            query = query.Where(entry =>
                entry.CreatedAtUtc < endDateExclusive);
        }

        if (minAmount.HasValue && minAmount.Value < 0)
        {
            throw new ArgumentException(
                "Minimum tutar negatif olamaz.");
        }

        if (maxAmount.HasValue && maxAmount.Value < 0)
        {
            throw new ArgumentException(
                "Maksimum tutar negatif olamaz.");
        }

        if (minAmount.HasValue && maxAmount.HasValue &&
            minAmount.Value > maxAmount.Value)
        {
            throw new ArgumentException(
                "Minimum tutar maksimum tutardan büyük olamaz.");
        }

        if (minAmount.HasValue)
        {
            query = query.Where(entry =>
                entry.Amount >= minAmount.Value);
        }

        if (maxAmount.HasValue)
        {
            query = query.Where(entry =>
                entry.Amount <= maxAmount.Value);
        }

        var totalCount = await query.CountAsync();

        var entries = await query
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(entry => new LedgerEntryResponse(
                entry.Id,
                entry.AccountId,
                entry.TransferId,
                entry.Amount,
                entry.Currency,
                entry.Type.ToString(),
                entry.Direction.HasValue
                    ? entry.Direction.Value.ToString()
                    : null,
                entry.CreatedAtUtc
            ))
            .ToListAsync();

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return new PagedResponse<LedgerEntryResponse>(
            entries,
            page,
            pageSize,
            totalCount,
            totalPages);
    }

    public async Task<bool> DepositAsync(
        Guid accountId,
        Guid customerId,
        decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException(
                "Yatırılacak tutar sıfırdan büyük olmalıdır.");

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync();

        var account = await _dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == accountId &&
                x.CustomerId == customerId);

        if (account is null)
            return false;

        var affectedRows = await _dbContext.Accounts
            .Where(x =>
                x.Id == accountId &&
                x.CustomerId == customerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    x => x.Balance,
                    x => x.Balance + amount));

        if (affectedRows == 0)
            return false;

        var trackedAccount = _dbContext.Accounts.Local
            .FirstOrDefault(x => x.Id == accountId);

        if (trackedAccount is not null)
        {
            await _dbContext.Entry(trackedAccount).ReloadAsync();
        }

        var ledgerEntry = new LedgerEntry
        {
            AccountId = account.Id,
            Amount = amount,
            Currency = account.Currency,
            Direction = LedgerDirection.Credit,
            Type = LedgerEntryType.Deposit
        };

        _dbContext.LedgerEntries.Add(ledgerEntry);

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return true;
    }

    public async Task<bool> WithdrawAsync(
        Guid accountId,
        Guid customerId,
        decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException(
                "Çekilecek tutar sıfırdan büyük olmalıdır.");

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync();

        var affectedRows = await _dbContext.Accounts
            .Where(x =>
                x.Id == accountId &&
                x.CustomerId == customerId &&
                x.Balance >= amount)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    x => x.Balance,
                    x => x.Balance - amount));

        if (affectedRows == 0)
        {
            var accountExists = await _dbContext.Accounts
                .AnyAsync(x =>
                    x.Id == accountId &&
                    x.CustomerId == customerId);

            if (!accountExists)
                return false;

            throw new BusinessException("Yetersiz bakiye.");
        }

        var account = await _dbContext.Accounts
            .AsNoTracking()
            .FirstAsync(x =>
                x.Id == accountId &&
                x.CustomerId == customerId);

        _dbContext.LedgerEntries.Add(new LedgerEntry
        {
            AccountId = account.Id,
            Amount = amount,
            Currency = account.Currency,
            Direction = LedgerDirection.Debit,
            Type = LedgerEntryType.Withdrawal
        });

        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return true;
    }

    public async Task<bool> TransferAsync(
        TransferRequest request,
        Guid customerId,
        string idempotencyKey)
    {
        if (request.Amount <= 0)
            throw new ArgumentException(
                "Transfer tutarı sıfırdan büyük olmalıdır.");

        if (request.SourceAccountId == request.DestinationAccountId)
            throw new ArgumentException(
                "Gönderen ve alıcı hesap aynı olamaz.");

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException(
                "Idempotency-Key zorunludur.");

        var requestData =
            $"{request.SourceAccountId:N}|" +
            $"{request.DestinationAccountId:N}|" +
            request.Amount.ToString(CultureInfo.InvariantCulture);

        var requestHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(requestData)));

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync();

        var sourceAccount = await _dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == request.SourceAccountId &&
                x.CustomerId == customerId);

        if (sourceAccount is null)
            return false;

        var existingRecord = await _dbContext.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.CustomerId == customerId &&
                x.Key == idempotencyKey);

        if (existingRecord is not null)
        {
            if (existingRecord.RequestHash != requestHash)
                throw new BusinessException(
                    "Bu Idempotency-Key farklı bir istek için kullanılmış.");

            return true;
        }

        var destinationAccount = await _dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == request.DestinationAccountId);

        if (destinationAccount is null)
            return false;

        if (sourceAccount.Currency != destinationAccount.Currency)
            throw new BusinessException(
                "Hesapların para birimleri aynı olmalıdır.");

        var sourceAffectedRows = await _dbContext.Accounts
            .Where(x =>
                x.Id == request.SourceAccountId &&
                x.CustomerId == customerId &&
                x.Balance >= request.Amount)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    x => x.Balance,
                    x => x.Balance - request.Amount));

        if (sourceAffectedRows == 0)
        {
            var sourceExists = await _dbContext.Accounts
                .AnyAsync(x =>
                    x.Id == request.SourceAccountId
                    && x.CustomerId == customerId);

            if (!sourceExists)
                return false;

            throw new BusinessException("Yetersiz bakiye.");
        }

        var destinationAffectedRows = await _dbContext.Accounts
            .Where(x => x.Id == request.DestinationAccountId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(
                    x => x.Balance,
                    x => x.Balance + request.Amount));

        if (destinationAffectedRows == 0)
            return false;

        var transferId = Guid.NewGuid();

        _dbContext.LedgerEntries.AddRange(
            new LedgerEntry
            {
                AccountId = sourceAccount.Id,
                Amount = request.Amount,
                Currency = sourceAccount.Currency,
                Direction = LedgerDirection.Debit,
                Type = LedgerEntryType.Transfer,
                TransferId = transferId
            },
            new LedgerEntry
            {
                AccountId = destinationAccount.Id,
                Amount = request.Amount,
                Currency = destinationAccount.Currency,
                Direction = LedgerDirection.Credit,
                Type = LedgerEntryType.Transfer,
                TransferId = transferId
            });

        _dbContext.IdempotencyRecords.Add(new IdempotencyRecord
        {
            CustomerId = customerId,
            Key = idempotencyKey,
            RequestHash = requestHash,
            StatusCode = 200,
            ResponseBody = "{\"message\":\"Para transferi başarılı.\"}"
        });

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();

            // Başarısız işlemden kalan takip edilen nesneleri temizle.
            _dbContext.ChangeTracker.Clear();

            // Çakışmaya neden olan kayıt veritabanında oluşmuş mu?
            var concurrentRecord = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.CustomerId == customerId &&
                    x.Key == idempotencyKey);

            // Aynı anahtarla başarılı bir kayıt yoksa asıl hatayı fırlat.
            if (concurrentRecord is null)
                throw;

            // Anahtar farklı bir istek için kullanılmışsa reddet.
            if (concurrentRecord.RequestHash != requestHash)
                throw new BusinessException(
                    "Bu Idempotency-Key farklı bir istek için kullanılmış.");

            // Aynı istek zaten başarıyla tamamlanmış.
            return true;
        }

        await transaction.CommitAsync();

        return true;

    }

    public async Task<IdempotencyRecord?> GetIdempotencyRecordAsync(
        string idempotencyKey,
        Guid customerId)
    {
        return await _dbContext.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.CustomerId == customerId &&
                x.Key == idempotencyKey);
    }
}
