
using System.ComponentModel.DataAnnotations;

namespace FinCore.Api.DTOs;

public sealed class DepositRequest
{
    [Range(
        0.01,
        999999999999,
        ErrorMessage = "Yatırılacak tutar sıfırdan büyük olmalıdır.")]
    public decimal Amount { get; set; }
}
