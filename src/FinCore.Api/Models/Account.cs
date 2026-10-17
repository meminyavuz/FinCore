namespace FinCore.Api.Models;
public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string OwnerName { get; set; }
    public required string Currency { get; set; }
    public decimal Balance { get; set; } = 0m;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; } = null!;
}
