
using System.ComponentModel.DataAnnotations;

namespace FinCore.Api.DTOs;

public sealed class TransferRequest
{
    [Required]
    public Guid SourceAccountId { get; set; }

    [Required]
    public Guid DestinationAccountId { get; set; }

    [Range(
        0.01,
        999999999999,
        ErrorMessage = "Transfer tutarı sıfırdan büyük olmalıdır.")]
    public decimal Amount { get; set; }
}
