using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DoraFood.Helpers;

public static class SlugHelper
{
    public static async Task<string> ResolveAsync(
        string? requestedSlug,
        string title,
        IQueryable<string> existingSlugs,
        CancellationToken cancellationToken,
        int maxLength = 250)
    {
        if (!string.IsNullOrWhiteSpace(requestedSlug))
        {
            return requestedSlug;
        }

        if (maxLength < 10)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        }

        var slug = Generate(title, maxLength);

        var exists = await existingSlugs.AnyAsync(
            existingSlug => existingSlug == slug,
            cancellationToken);

        if (!exists)
        {
            return slug;
        }

        var suffix = $"-{Guid.NewGuid().ToString("N")[..8]}";
        var prefixLength = Math.Min(slug.Length, maxLength - suffix.Length);
        var prefix = slug[..prefixLength].TrimEnd('-');

        return prefix + suffix;
    }

    private static string Generate(string title, int maxLength)
    {
        var normalized = title
            .ToLowerInvariant()
            .Replace('đ', 'd')
            .Normalize(NormalizationForm.FormD);

        var withoutAccents = Regex.Replace(normalized, @"\p{M}+", "");
        var slug = Regex.Replace(withoutAccents, @"[^a-z0-9]+", "-").Trim('-');

        if (slug.Length == 0)
        {
            slug = "bai-viet";
        }

        return slug.Length <= maxLength
            ? slug
            : slug[..maxLength].TrimEnd('-');
    }
}