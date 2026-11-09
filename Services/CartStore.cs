using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;

namespace DoraFood.Services;

// Lưu tạm trên một máy chủ. Chỉ lưu Id khẩu phần và số lượng, không lưu giá.
// Đăng ký Singleton để các request cùng sử dụng một kho giỏ hàng.
public sealed class CartStore : IDisposable
{
    public const int MaxQuantity = 99;
    public const int MaxLines = 50;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10000 });
    private readonly object[] _gates = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();

    private sealed record CartState(Guid Version, Dictionary<long, int> Items);

    public Dictionary<long, int> Read(long userId, Guid stamp)
    {
        return ReadSnapshot(userId, stamp).Items;
    }

    public (Guid Version, Dictionary<long, int> Items) ReadSnapshot(
        long userId, Guid stamp)
    {
        lock (Gate(userId))
        {
            if (_cache.TryGetValue(Key(userId, stamp), out CartState? state)
                && state != null)
            {
                return (state.Version, new Dictionary<long, int>(state.Items));
            }

            return (Guid.Empty, new Dictionary<long, int>());
        }
    }

    public void ClearIfVersion(long userId, Guid stamp, Guid version)
    {
        lock (Gate(userId))
        {
            var key = Key(userId, stamp);

            if (_cache.TryGetValue(key, out CartState? state)
                && state?.Version == version)
            {
                _cache.Remove(key);
            }
        }
    }

    public string? Add(long userId, Guid stamp, long variantId, int quantity)
    {
        if (variantId <= 0 || quantity < 1 || quantity > MaxQuantity)
            return "Số lượng phải từ 1 đến 99.";

        return Change(userId, stamp, cart =>
        {
            var current = cart.GetValueOrDefault(variantId);
            if (current + quantity > MaxQuantity)
                return "Tổng số lượng của một khẩu phần không được vượt quá 99.";
            if (current == 0 && cart.Count >= MaxLines)
                return "Giỏ hàng tối đa 50 khẩu phần khác nhau.";
            cart[variantId] = current + quantity;
            return null;
        });
    }

    public string? Update(long userId, Guid stamp, long variantId, int quantity)
    {
        if (variantId <= 0 || quantity < 1 || quantity > MaxQuantity)
            return "Số lượng phải từ 1 đến 99. Muốn bỏ món, hãy chọn Xóa.";

        return Change(userId, stamp, cart =>
        {
            if (!cart.ContainsKey(variantId)) return "Món không còn trong giỏ. Vui lòng thêm lại.";
            cart[variantId] = quantity;
            return null;
        });
    }

    public string? Remove(long userId, Guid stamp, long variantId) =>
        Change(userId, stamp, cart => cart.Remove(variantId) ? null : "Món không còn trong giỏ.");

    public void Clear(long userId, Guid stamp)
    {
        lock (Gate(userId)) _cache.Remove(Key(userId, stamp));
    }

    private string? Change(long userId, Guid stamp, Func<Dictionary<long, int>, string?> change)
    {
        // Đọc–sửa–ghi trong cùng khóa để hai tab không ghi đè số lượng của nhau.
        lock (Gate(userId))
        {
            var key = Key(userId, stamp);
            var cart = ReadCopy(key);
            var error = change(cart);
            if (error != null) return error;
            if (cart.Count == 0)
            {
                _cache.Remove(key);
                return null;
            }

            var updated = new CartState(Guid.NewGuid(), cart);

            _cache.Set(key, updated, new MemoryCacheEntryOptions
            {
                Size = 1,
                SlidingExpiration = TimeSpan.FromHours(24)
            });

            return _cache.TryGetValue(key, out CartState? saved)
                && ReferenceEquals(saved, updated)
                ? null
                : "Chưa lưu được giỏ. Vui lòng thử lại.";
        }
    }

    private Dictionary<long, int> ReadCopy(string key)
    {
        if (_cache.TryGetValue(key, out CartState? state) && state != null)
        {
            return new Dictionary<long, int>(state.Items);
        }

        return new Dictionary<long, int>();
    }

    private object Gate(long userId)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        return _gates[(int)(userId % _gates.Length)];
    }

    private static string Key(long userId, Guid stamp) => $"DoraFood.Cart.{userId}.{stamp:N}";
    public void Dispose() => _cache.Dispose();
}
