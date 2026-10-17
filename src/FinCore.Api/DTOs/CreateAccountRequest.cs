
using System.ComponentModel.DataAnnotations;

namespace FinCore.Api.DTOs;

public sealed class CreateAccountRequest
{
    [Required(ErrorMessage = "Hesap sahibi adı zorunludur.")]
    [StringLength(100, MinimumLength = 2,
        ErrorMessage = "Hesap sahibi adı 2 ile 100 karakter arasında olmalıdır.")]
    public string OwnerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Para birimi zorunludur.")]
    [RegularExpression(
        "^(TRY|USD|EUR)$",
        ErrorMessage = "Desteklenen para birimleri: TRY, USD, EUR.")]
    public string Currency { get; set; } = string.Empty;
}
