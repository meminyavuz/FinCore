
using FinCore.Api.Data;
using FinCore.Api.DTOs;
using FinCore.Api.Models;
using FinCore.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace FinCore.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController(
    AppDbContext dbContext,
    PasswordService passwordService,
    JwtService jwtService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var emailExists = await dbContext.Users
            .AnyAsync(u => u.Email == email, cancellationToken);

        if (emailExists)
        {
            return Conflict(new
            {
                message = "Bu e-posta adresi zaten kayıtlı."
            });
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var customer = new Customer
        {
            CustomerNumber =
                $"C{Guid.NewGuid():N}"[..13].ToUpperInvariant(),
            FullName = request.FullName.Trim()
        };

        var user = new User
        {
            CustomerId = customer.Id,
            Email = email,
            PasswordHash = passwordService.HashPassword(request.Password)
        };

        dbContext.Customers.Add(customer);
        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);

            return Conflict(new
            {
                message = "Kayıt oluşturulamadı. E-posta adresi daha önce kullanılmış olabilir."
            });
        }

        var token = jwtService.GenerateToken(user.Id, user.Email);

        return StatusCode(StatusCodes.Status201Created, new
        {
            userId = user.Id,
            customerId = customer.Id,
            customerNumber = customer.CustomerNumber,
            fullName = customer.FullName,
            email = user.Email,
            accessToken = token,
            tokenType = "Bearer",
            expiresIn = 1800
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Email == email,
                cancellationToken);

        if (user is null ||
            !passwordService.VerifyPassword(
                request.Password,
                user.PasswordHash))
        {
            return Unauthorized(new
            {
                message = "E-posta veya parola hatalı."
            });
        }

        var token = jwtService.GenerateToken(user.Id, user.Email);

        return Ok(new
        {
            userId = user.Id,
            customerId = user.CustomerId,
            email = user.Email,
            accessToken = token,
            tokenType = "Bearer",
            expiresIn = 1800
        });
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            userId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            email = User.FindFirstValue(ClaimTypes.Email)
        });
    }
}
