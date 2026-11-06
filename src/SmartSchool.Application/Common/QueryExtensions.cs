using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SmartSchool.Shared.Contracts;

namespace SmartSchool.Application.Common;

public static class QueryExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(this IQueryable<T> query, PagedQuery paging, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((paging.SafePage - 1) * paging.SafePageSize)
            .Take(paging.SafePageSize)
            .ToListAsync(ct);

        return new PagedResult<T>
        {
            Items = items,
            Page = paging.SafePage,
            PageSize = paging.SafePageSize,
            TotalCount = total
        };
    }

    public static IOrderedQueryable<T> OrderByDirection<T, TKey>(this IQueryable<T> query, Expression<Func<T, TKey>> key, bool desc)
        => desc ? query.OrderByDescending(key) : query.OrderBy(key);

    public static string NormalizeKey(this string value) => value.Trim().ToUpperInvariant();
}
