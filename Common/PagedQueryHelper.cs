using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Common;

public static class PagedQueryHelper
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(
        IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) where T : BaseModel
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 2000);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, total, page, pageSize);
    }
}
