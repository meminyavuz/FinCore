
namespace FinCore.Api.DTOs;

public sealed record AccountResponse(
    Guid Id,
    string OwnerName,
    string Currency,
    decimal Balance,
    DateTime CreatedAtUtc);
