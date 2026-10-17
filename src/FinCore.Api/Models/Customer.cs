namespace FinCore.Api.Models;

public sealed class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string CustomerNumber { get; set; }
    public required string FullName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
