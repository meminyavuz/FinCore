
namespace FinCore.Api.DTOs;

public sealed record LedgerEntryResponse(
    Guid Id,
    Guid AccountId,
    Guid? TransferId,
    decimal Amount,
    string Currency,
    string Type,
    string? Direction,
    DateTime CreatedAtUtc
);
