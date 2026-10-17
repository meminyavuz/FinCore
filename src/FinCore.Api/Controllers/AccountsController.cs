
using FinCore.Api.DTOs;
using FinCore.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace FinCore.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AccountsController : ControllerBase
{
    private readonly AccountsService _accountsService;

    public AccountsController(AccountsService accountsService)
    {
        _accountsService = accountsService;
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<AccountResponse>> Create(
    CreateAccountRequest request)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized(new
            {
                message = "Kullanıcı kimliği token içerisinde bulunamadı."
            });
        }

        var customerId = await _accountsService.GetCustomerIdByUserIdAsync(userId);

        if (customerId is null)
        {
            return Unauthorized(new
            {
                message = "Kullanıcıya bağlı müşteri bulunamadı."
            });
        }

        var account = await _accountsService.CreateAsync(request, customerId.Value);

        return CreatedAtAction(
            nameof(GetById),
            new { id = account.Id },
            account);
    }

    [Authorize]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccountResponse>> GetById(Guid id)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized(new
            {
                message = "Kullanıcı kimliği token içerisinde bulunamadı."
            });
        }

        var customerId = await _accountsService.GetCustomerIdByUserIdAsync(userId);

        if (customerId is null)
        {
            return Unauthorized(new
            {
                message = "Kullanıcıya bağlı müşteri bulunamadı."
            });
        }

        var account = await _accountsService.GetByIdAsync(id, customerId.Value);

        if (account is null)
            return NotFound();

        return Ok(account);
    }

    [Authorize]
    [HttpGet]
    public async Task<ActionResult<List<AccountResponse>>> GetAll()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized(new
            {
                message = "Kullanıcı kimliği token içerisinde bulunamadı."
            });
        }

        var customerId = await _accountsService.GetCustomerIdByUserIdAsync(userId);

        if (customerId is null)
        {
            return Unauthorized(new
            {
                message = "Kullanıcıya bağlı müşteri bulunamadı."
            });
        }

        var accounts = await _accountsService.GetAllAsync(customerId.Value);

        return Ok(accounts);
    }

    [Authorize]
    [HttpGet("{id:guid}/transactions")]
    public async Task<ActionResult<PagedResponse<LedgerEntryResponse>>> GetTransactions(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? type = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] decimal? minAmount = null,
        [FromQuery] decimal? maxAmount = null)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized(new
            {
                message = "Kullanıcı kimliği token içerisinde bulunamadı."
            });
        }

        var customerId = await _accountsService.GetCustomerIdByUserIdAsync(userId);

        if (customerId is null)
        {
            return Unauthorized(new
            {
                message = "Kullanıcıya bağlı müşteri bulunamadı."
            });
        }

        var transactions = await _accountsService.GetTransactionsAsync(
            id,
            customerId.Value,
            page,
            pageSize,
            type,
            fromDate,
            toDate,
            minAmount,
            maxAmount);

        return Ok(transactions);
    }

    [Authorize]
    [HttpPost("{id:guid}/deposit")]
    public async Task<IActionResult> Deposit(
        Guid id,
        DepositRequest request)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized(new
            {
                message = "Kullanıcı kimliği token içerisinde bulunamadı."
            });
        }

        var customerId = await _accountsService.GetCustomerIdByUserIdAsync(userId);

        if (customerId is null)
        {
            return Unauthorized(new
            {
                message = "Kullanıcıya bağlı müşteri bulunamadı."
            });
        }

        var success = await _accountsService.DepositAsync(
            id,
            customerId.Value,
            request.Amount);

        if (!success)
            return NotFound(new { message = "Hesap bulunamadı." });

        return Ok(new
        {
            message = "Para yatırma işlemi başarılı."
        });
    }

    [Authorize]
    [HttpPost("{id:guid}/withdraw")]
    public async Task<IActionResult> Withdraw(
        Guid id,
        DepositRequest request)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized(new
            {
                message = "Kullanıcı kimliği token içerisinde bulunamadı."
            });
        }

        var customerId = await _accountsService.GetCustomerIdByUserIdAsync(userId);

        if (customerId is null)
        {
            return Unauthorized(new
            {
                message = "Kullanıcıya bağlı müşteri bulunamadı."
            });
        }

        var success = await _accountsService.WithdrawAsync(
            id,
            customerId.Value,
            request.Amount);

        if (!success)
            return NotFound(new { message = "Hesap bulunamadı." });

        return Ok(new
        {
            message = "Para çekme işlemi başarılı."
        });
    }

    [Authorize]
    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer(
        [FromBody] TransferRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new
            {
                message = "Idempotency-Key header zorunludur."
            });
        }

        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized(new
            {
                message = "Kullanıcı kimliği token içerisinde bulunamadı."
            });
        }

        var customerId = await _accountsService.GetCustomerIdByUserIdAsync(userId);

        if (customerId is null)
        {
            return Unauthorized(new
            {
                message = "Kullanıcıya bağlı müşteri bulunamadı."
            });
        }

        var success = await _accountsService.TransferAsync(
            request,
            customerId.Value,
            idempotencyKey);

        if (!success)
        {
            return NotFound(new
            {
                message = "Gönderen veya alıcı hesap bulunamadı."
            });
        }

        var savedResponse =
            await _accountsService.GetIdempotencyRecordAsync(
                idempotencyKey,
                customerId.Value);

        if (savedResponse is null)
        {
            return StatusCode(500, new
            {
                message = "Transfer yanıtı bulunamadı."
            });
        }

        return new ContentResult
        {
            StatusCode = savedResponse.StatusCode,
            Content = savedResponse.ResponseBody,
            ContentType = "application/json"
        };
    }
}
