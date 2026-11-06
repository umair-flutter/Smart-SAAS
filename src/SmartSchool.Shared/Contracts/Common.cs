namespace SmartSchool.Shared.Contracts;

/// <summary>Query string parameters for server-side paginated lists.</summary>
public class PagedQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public bool SortDesc { get; set; }

    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => PageSize is < 1 or > 200 ? 20 : PageSize;

    public string ToQueryString()
    {
        var parts = new List<string> { $"page={SafePage}", $"pageSize={SafePageSize}" };
        if (!string.IsNullOrWhiteSpace(Search)) parts.Add($"search={Uri.EscapeDataString(Search)}");
        if (!string.IsNullOrWhiteSpace(SortBy)) parts.Add($"sortBy={Uri.EscapeDataString(SortBy)}");
        if (SortDesc) parts.Add("sortDesc=true");
        return string.Join('&', parts);
    }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>A simple id/name pair for dropdowns.</summary>
public record LookupItem(Guid Id, string Name);

/// <summary>Error body returned by the API for every handled failure.</summary>
public class ApiError
{
    public int Status { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
    public string? TraceId { get; set; }
}
