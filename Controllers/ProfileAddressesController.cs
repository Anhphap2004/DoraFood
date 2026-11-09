using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Data;
using DoraFood.Models;
using DoraFood.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

[Authorize]
[Route("tai-khoan/dia-chi")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ProfileAddressesController : Controller
{
    private readonly DoraFoodContext _context;

    public ProfileAddressesController(DoraFoodContext context)
    {
        _context = context;
    }

    [HttpGet(Name = "DoraFood.MyAddresses")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (await GetActiveUserIdAsync(cancellationToken) is not long userId)
        {
            return Forbid();
        }

        var addresses = await AddressesOf(userId)
            .AsNoTracking()
            .OrderByDescending(address => address.IsDefault)
            .ThenBy(address => address.Id)
            .ToListAsync(cancellationToken);

        return View(addresses);
    }

    [HttpGet("them")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        if (await GetActiveUserIdAsync(cancellationToken) is null)
        {
            return Forbid();
        }

        return View(new UserAddressInput());
    }

    [HttpPost("them")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Create(
        UserAddressInput input,
        CancellationToken cancellationToken)
    {
        return ChangeAddressesAsync(async userId =>
        {
            if (!ModelState.IsValid)
            {
                return View(input);
            }

            var hasDefault = await AddressesOf(userId)
                .AnyAsync(address => address.IsDefault, cancellationToken);

            var address = new UserAddress
            {
                UserId = userId,
                IsDefault = !hasDefault
            };

            ApplyAddressInput(address, input);

            _context.Set<UserAddress>().Add(address);
            await _context.SaveChangesAsync(cancellationToken);

            TempData["AddressSuccess"] = "Đã thêm địa chỉ giao hàng.";
            return RedirectToRoute("DoraFood.MyAddresses");
        }, cancellationToken);
    }

    [HttpGet("{id:long}/sua")]
    public async Task<IActionResult> Edit(
        [FromRoute] long id,
        CancellationToken cancellationToken)
    {
        if (await GetActiveUserIdAsync(cancellationToken) is not long userId)
        {
            return Forbid();
        }

        var input = await AddressesOf(userId)
            .AsNoTracking()
            .Where(address => address.Id == id)
            .Select(address => new UserAddressInput
            {
                Id = address.Id,
                RecipientName = address.RecipientName,
                PhoneNumber = address.PhoneNumber,
                AddressLine = address.AddressLine
            })
            .SingleOrDefaultAsync(cancellationToken);

        return input is null ? NotFound() : View(input);
    }

    [HttpPost("{id:long}/sua")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Edit(
        [FromRoute] long id,
        UserAddressInput input,
        CancellationToken cancellationToken)
    {
        return ChangeAddressesAsync(async userId =>
        {
            var address = await AddressesOf(userId)
                .AsTracking()
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (address is null)
            {
                return NotFound();
            }

            input.Id = address.Id;

            if (!ModelState.IsValid)
            {
                return View(input);
            }

            ApplyAddressInput(address, input);
            await _context.SaveChangesAsync(cancellationToken);

            TempData["AddressSuccess"] = "Đã cập nhật địa chỉ giao hàng.";
            return RedirectToRoute("DoraFood.MyAddresses");
        }, cancellationToken);
    }

    [HttpPost("{id:long}/mac-dinh")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SetDefault(
        [FromRoute] long id,
        CancellationToken cancellationToken)
    {
        return ChangeAddressesAsync(async userId =>
        {
            var address = await AddressesOf(userId)
                .AsTracking()
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (address is null)
            {
                return NotFound();
            }

            if (!address.IsDefault)
            {
                await AddressesOf(userId)
                    .Where(item => item.IsDefault && item.Id != id)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(item => item.IsDefault, false),
                        cancellationToken);

                address.IsDefault = true;
                await _context.SaveChangesAsync(cancellationToken);
            }

            TempData["AddressSuccess"] = "Đã cập nhật địa chỉ mặc định.";
            return RedirectToRoute("DoraFood.MyAddresses");
        }, cancellationToken);
    }

    [HttpPost("{id:long}/xoa")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Delete(
        [FromRoute] long id,
        CancellationToken cancellationToken)
    {
        return ChangeAddressesAsync(async userId =>
        {
            var address = await AddressesOf(userId)
                .AsTracking()
                .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

            if (address is null)
            {
                return NotFound();
            }

            var wasDefault = address.IsDefault;

            _context.Set<UserAddress>().Remove(address);
            await _context.SaveChangesAsync(cancellationToken);

            if (wasDefault)
            {
                var nextAddress = await AddressesOf(userId)
                    .AsTracking()
                    .OrderBy(item => item.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (nextAddress is not null)
                {
                    nextAddress.IsDefault = true;
                    await _context.SaveChangesAsync(cancellationToken);
                }
            }

            TempData["AddressSuccess"] = "Đã xóa địa chỉ giao hàng.";
            return RedirectToRoute("DoraFood.MyAddresses");
        }, cancellationToken);
    }

    private IQueryable<UserAddress> AddressesOf(long userId)
    {
        return _context.Set<UserAddress>()
            .Where(address => address.UserId == userId);
    }

    private bool TryGetUserId(out long userId)
    {
        return long.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            out userId) && userId > 0;
    }

    private async Task<long?> GetActiveUserIdAsync(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return null;
        }

        var isActive = await _context.Set<User>().AnyAsync(
            user => user.Id == userId && user.IsActive,
            cancellationToken);

        return isActive ? userId : null;
    }

    private async Task<IActionResult> ChangeAddressesAsync(
        Func<long, Task<IActionResult>> change,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Forbid();
        }

        await using var transaction = await _context.Database
            .BeginTransactionAsync(cancellationToken);

        var activeUserId = await _context.Database.SqlQuery<long>($"""
            SELECT Id AS [Value]
            FROM dbo.Users WITH (UPDLOCK, HOLDLOCK)
            WHERE Id = {userId} AND IsActive = 1
            """)
            .SingleOrDefaultAsync(cancellationToken);

        if (activeUserId != userId)
        {
            return Forbid();
        }

        var result = await change(userId);
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    private static void ApplyAddressInput(
        UserAddress address,
        UserAddressInput input)
    {
        address.RecipientName = input.RecipientName.Trim();
        address.PhoneNumber = input.PhoneNumber.Trim();
        address.AddressLine = input.AddressLine.Trim();
    }
}