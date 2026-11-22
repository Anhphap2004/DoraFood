using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DoraFood.Models;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Controllers;

public sealed partial class AiFaqController
{
    private static readonly JsonElement LookupTools =
        JsonSerializer.Deserialize<JsonElement>("""
        [
          {
            "type": "function",
            "name": "search_menu",
            "description": "Tìm món đang bán, phần bán và giá hiện tại.",
            "strict": true,
            "parameters": {
              "type": "object",
              "properties": {
                "keyword": {
                  "type": "string",
                  "description": "Tên món hoặc nhóm món ngắn. Dùng chuỗi rỗng nếu chưa có từ khóa."
                },
                "maxUnitPrice": {
                  "type": ["integer", "null"],
                  "description": "Giá tối đa một phần bằng VND. Dùng null nếu chưa rõ hoặc chỉ biết ngân sách cả bàn."
                }
              },
              "required": ["keyword", "maxUnitPrice"],
              "additionalProperties": false
            }
          },
          {
            "type": "function",
            "name": "find_vouchers",
            "description": "Kiểm tra một mã voucher cụ thể hoặc liệt kê voucher công khai còn hiệu lực và còn lượt dùng chung.",
            "strict": true,
            "parameters": {
              "type": "object",
              "properties": {
                "code": {
                  "type": ["string", "null"],
                  "description": "Mã voucher khách hỏi, ví dụ DORAFAP. Dùng null khi khách muốn xem danh sách."
                },
                "orderAmount": {
                  "type": ["integer", "null"],
                  "description": "Giá trị đơn trước khi áp mã, tính bằng VND. Dùng null nếu khách chưa cung cấp."
                }
              },
              "required": ["code", "orderAmount"],
              "additionalProperties": false
            }
          },
          {
            "type": "function",
            "name": "calculate_menu",
            "description": "Kiểm tra món và tính tổng tiền theo giá hiện hành. Dùng mã biến thể đã tra.",
            "strict": true,
            "parameters": {
              "type": "object",
              "properties": {
                "budgetVnd": {
                  "type": ["integer", "null"],
                  "description": "Tổng ngân sách cả nhóm bằng VND, hoặc null."
                },
                "items": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "variantId": { "type": "integer" },
                      "quantity": { "type": "integer" }
                    },
                    "required": ["variantId", "quantity"],
                    "additionalProperties": false
                  }
                }
              },
              "required": ["budgetVnd", "items"],
              "additionalProperties": false
            }
          }
        ]
        """);

    private async Task<string> RunLookupAsync(
        string? name,
        string arguments,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(arguments);
        var args = document.RootElement;

        switch (name)
        {
            case "search_menu":
                {
                    var keyword = args.GetProperty("keyword").GetString()?.Trim() ?? "";

                    if (keyword.Length > 100
                        || !TryReadAmount(args.GetProperty("maxUnitPrice"), out var maxUnitPrice))
                    {
                        return """{"error":"Từ khóa hoặc giá món không hợp lệ."}""";
                    }

                    return await SearchMenuAsync(keyword, maxUnitPrice, cancellationToken);
                }

            case "find_vouchers":
                {
                    if (!TryReadAmount(args.GetProperty("orderAmount"), out var orderAmount))
                        return """{"error":"Giá trị đơn hàng không hợp lệ."}""";

                    var code = args.GetProperty("code").GetString()?.Trim().ToUpperInvariant();

                    if (code is { Length: > 50 })
                        return """{"error":"Mã voucher tối đa 50 ký tự."}""";

                    return await FindVouchersAsync(
                        string.IsNullOrEmpty(code) ? null : code,
                        orderAmount,
                        cancellationToken);
                }

            default:
                return """{"error":"Chức năng tra cứu chưa được hỗ trợ."}""";
        }
    }

    private static bool TryReadAmount(JsonElement value, out long? amount)
    {
        amount = null;

        if (value.ValueKind == JsonValueKind.Null)
            return true;

        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var number)
            || number is < 0 or > 100_000_000)
        {
            return false;
        }

        amount = number;
        return true;
    }

    private async Task<long[]> GetVisibleCategoryIdsAsync(
        CancellationToken cancellationToken)
    {
        var categories = await _db.DishCategories.AsNoTracking()
            .Where(category => category.IsActive)
            .Select(category => new { category.Id, category.ParentId })
            .ToListAsync(cancellationToken);

        var children = categories.ToLookup(category => category.ParentId);
        var visibleIds = new HashSet<long>();
        var pending = new Queue<long>(
            children[null].Select(category => category.Id));

        while (pending.TryDequeue(out var categoryId))
        {
            if (!visibleIds.Add(categoryId))
                continue;

            foreach (var child in children[categoryId])
                pending.Enqueue(child.Id);
        }

        return visibleIds.ToArray();
    }

    private IQueryable<DishVariant> AvailableVariants(long[] categoryIds) =>
        _db.DishVariants.AsNoTracking()
            .Where(variant => variant.IsAvailable
                && variant.Dish.IsActive
                && categoryIds.Contains(variant.Dish.CategoryId));

    private async Task<string> SearchMenuAsync(
        string keyword,
        long? maxUnitPrice,
        CancellationToken cancellationToken)
    {
        var categoryIds = await GetVisibleCategoryIdsAsync(cancellationToken);
        var query = AvailableVariants(categoryIds);

        if (keyword.Length > 0)
        {
            query = query.Where(variant =>
                variant.Dish.Name.Contains(keyword)
                || variant.Dish.Category.Name.Contains(keyword));
        }

        if (maxUnitPrice is long price)
            query = query.Where(variant => variant.Price <= price);

        var limit = keyword.Length == 0 ? 24 : 12;

        var rows = await query
            .OrderBy(variant => variant.Price)
            .ThenBy(variant => variant.Id)
            .Select(variant => new
            {
                variantId = variant.Id,
                dish = variant.Dish.Name,
                category = variant.Dish.Category.Name,
                variant = variant.Name,
                unit = variant.SellingUnit,
                priceVnd = variant.Price,
                allergenNote = variant.Dish.AllergenNote
            })
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(new
        {
            items = rows.Take(limit),
            hasMore = rows.Count > limit
        });
    }

    private sealed record MenuLine(
        string Dish,
        string Variant,
        string Unit,
        int Quantity,
        decimal UnitPriceVnd,
        decimal SubtotalVnd);

    private sealed record MenuQuote(
        List<MenuLine> Items,
        decimal TotalVnd,
        decimal? RemainingVnd);

    private sealed record MenuCalculation(MenuQuote? Menu, string? Error);

    private async Task<MenuCalculation> CalculateMenuAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(arguments);
        var args = document.RootElement;

        if (!TryReadAmount(args.GetProperty("budgetVnd"), out var budget))
            return new(null, "Ngân sách không hợp lệ.");

        var items = args.GetProperty("items");

        if (items.ValueKind != JsonValueKind.Array
            || items.GetArrayLength() is < 1 or > 12)
        {
            return new(null, "Thực đơn cần từ 1 đến 12 dòng món.");
        }

        var quantities = new Dictionary<long, int>();

        foreach (var item in items.EnumerateArray())
        {
            var variantId = item.GetProperty("variantId");
            var quantityValue = item.GetProperty("quantity");

            if (variantId.ValueKind != JsonValueKind.Number
                || !variantId.TryGetInt64(out var id)
                || id <= 0
                || quantityValue.ValueKind != JsonValueKind.Number
                || !quantityValue.TryGetInt32(out var quantity)
                || quantity is < 1 or > 200
                || !quantities.TryAdd(id, quantity))
            {
                return new(null, "Mã món bị trùng hoặc số lượng không hợp lệ.");
            }
        }

        var ids = quantities.Keys.ToArray();
        var categoryIds = await GetVisibleCategoryIdsAsync(cancellationToken);

        var variants = await AvailableVariants(categoryIds)
            .Where(variant => ids.Contains(variant.Id) && variant.Price >= 0)
            .Select(variant => new
            {
                variant.Id,
                Dish = variant.Dish.Name,
                Variant = variant.Name,
                Unit = variant.SellingUnit,
                variant.Price
            })
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);

        if (variants.Count != ids.Length)
            return new(null, "Có món hoặc biến thể đã ngừng bán. Vui lòng chọn lại.");

        var lines = new List<MenuLine>();

        foreach (var (id, quantity) in quantities)
        {
            var variant = variants[id];

            lines.Add(new MenuLine(
                variant.Dish,
                variant.Variant,
                variant.Unit,
                quantity,
                variant.Price,
                variant.Price * quantity));
        }

        var total = lines.Sum(line => line.SubtotalVnd);

        if (budget.HasValue && total > budget.Value)
        {
            return new(null,
                $"Tổng tiền {total:0.##} VND vượt ngân sách {budget.Value} VND. "
                + "Hãy điều chỉnh món hoặc số lượng.");
        }

        return new(new MenuQuote(lines, total, budget - total), null);
    }

    private async Task<string> FindVouchersAsync(
    string? code,
    long? orderAmount,
    CancellationToken cancellationToken)
    {
        const string notFound =
            """{"status":"not_found","message":"Không tìm thấy mã voucher công khai này."}""";

        var now = DateTime.UtcNow;

        // Cùng bộ lọc công khai với trang /uu-dai.
        var vouchers = _db.Set<Voucher>()
            .AsNoTracking()
            .Where(voucher =>
                voucher.IsActive
                && voucher.StartsAtUtc <= now
                && voucher.Cover != null
                && voucher.Cover.Url.Trim() != "");

        if (code is not null)
        {
            // Tra đúng mã khách hỏi, kể cả khi đã hết hạn, hết lượt
            // hoặc đơn chưa đạt giá trị tối thiểu.
            vouchers = vouchers.Where(voucher => voucher.Code == code);
        }
        else
        {
            vouchers = vouchers.Where(voucher => now < voucher.EndsAtUtc);

            if (orderAmount is long amount)
            {
                vouchers = vouchers.Where(voucher =>
                    voucher.MinimumOrder <= amount);
            }
        }

        var query = vouchers.Select(voucher => new
        {
            Voucher = voucher,
            UsedCount = voucher.UsageLimit.HasValue
                ? _db.Set<Order>().LongCount(order =>
                    order.Status != "CANCELLED"
                    && order.OrderDiscounts.Any(discount =>
                        discount.VoucherId == voucher.Id))
                : 0L
        });

        if (code is not null)
        {
            var row = await query.SingleOrDefaultAsync(cancellationToken);

            return row is null
                ? notFound
                : JsonSerializer.Serialize(
                    DescribeVoucher(row.Voucher, row.UsedCount, orderAmount, now));
        }

        const int limit = 8;

        var rows = await query
            .Where(row => !row.Voucher.UsageLimit.HasValue
                || row.UsedCount < row.Voucher.UsageLimit.Value)
            .OrderByDescending(row => row.Voucher.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(new
        {
            items = rows.Take(limit).Select(row =>
                DescribeVoucher(row.Voucher, row.UsedCount, orderAmount, now)),
            hasMore = rows.Count > limit
        });
    }

    private static object DescribeVoucher(
        Voucher voucher,
        long usedCount,
        long? orderAmount,
        DateTime now)
    {
        long? remainingUses = voucher.UsageLimit is int limit
            ? Math.Max(0L, limit - usedCount)
            : null;

        decimal? missingAmountVnd = orderAmount is long amount
            ? Math.Max(0m, voucher.MinimumOrder - amount)
            : null;

        var status = "potentially_eligible";

        if (!voucher.IsActive)
            status = "inactive";
        else if (now < voucher.StartsAtUtc)
            status = "not_started";
        else if (now >= voucher.EndsAtUtc)
            status = "expired";
        else if (remainingUses == 0)
            status = "no_uses_left";
        else if (orderAmount is null)
            status = "order_amount_required";
        else if (orderAmount == 0)
            status = "empty_order";
        else if (missingAmountVnd is > 0m)
            status = "below_minimum";

        return new
        {
            status,
            code = voucher.Code,
            name = voucher.Name,
            orderAmountVnd = orderAmount,
            minimumOrderVnd = voucher.MinimumOrder,
            missingAmountVnd,
            remainingUses,
            perCustomerLimit = voucher.PerCustomerLimit,
            startsAtUtc = DateTime.SpecifyKind(voucher.StartsAtUtc, DateTimeKind.Utc),
            endsAtUtc = DateTime.SpecifyKind(voucher.EndsAtUtc, DateTimeKind.Utc),
            discountType = voucher.DiscountType,
            discountValue = voucher.DiscountValue,
            maxDiscountVnd = voucher.MaxDiscount
        };
    }
}