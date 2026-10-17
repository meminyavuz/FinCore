namespace FinCore.Api.Models;
public sealed class LedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AccountId { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public LedgerDirection? Direction { get; set; }

    public LedgerEntryType Type { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Account Account { get; set; } = null!;

    public Guid? TransferId { get; set; }
}

public enum LedgerEntryType
{
    Deposit = 1,
    Withdrawal = 2,
    Transfer = 3
}
public enum LedgerDirection
{
    Debit = 1,
    Credit = 2
}
