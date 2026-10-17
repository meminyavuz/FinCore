using System.ComponentModel.DataAnnotations;

namespace FinCore.Api.DTOs;

public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(254)]
    public required string Email { get; init; }

    [Required]
    [MaxLength(128)]
    public required string Password { get; init; }
}