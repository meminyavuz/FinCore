using System.ComponentModel.DataAnnotations;

namespace FinCore.Api.DTOs;

public sealed class RegisterRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(254)]
    public required string Email { get; init; }

    [Required]
    [MinLength(12)]
    [MaxLength(128)]
    public required string Password { get; init; }

    [Required]
    [MinLength(2)]
    [MaxLength(150)]
    public required string FullName { get; init; }
}