using Microsoft.AspNetCore.Components;
using SmartSchool.Client.Services;
using SmartSchool.Shared.Contracts;

namespace SmartSchool.Client.Components;

/// <summary>
/// Shared logic for server-paginated list pages: search with debounce, sorting, paging,
/// and reloading when the tenant/school context changes.
/// </summary>
public abstract class ListPageBase<TItem> : ComponentBase, IDisposable
{
    [Inject] protected ApiClient Api { get; set; } = default!;
    [Inject] protected AppState State { get; set; } = default!;
    [Inject] protected ToastService Toasts { get; set; } = default!;

    protected PagedQuery Query { get; } = new();
    protected PagedResult<TItem>? Result { get; set; }
    protected bool Loading { get; set; }
    protected string? LoadError { get; set; }

    private CancellationTokenSource? _searchDelay;

    /// <summary>API route of the list, e.g. "api/schools".</summary>
    protected abstract string Endpoint { get; }

    /// <summary>Extra query string (filters) appended to the request.</summary>
    protected virtual string? ExtraQuery => null;

    protected override async Task OnInitializedAsync()
    {
        State.ContextChanged += OnContextChanged;
        await OnInitializedPageAsync();
        await LoadAsync();
    }

    protected virtual Task OnInitializedPageAsync() => Task.CompletedTask;

    protected async Task LoadAsync()
    {
        Loading = true;
        LoadError = null;
        try
        {
            Result = await Api.GetPagedAsync<TItem>(Endpoint, Query, ExtraQuery);
        }
        catch (ApiException ex)
        {
            LoadError = ex.Friendly;
        }
        finally
        {
            Loading = false;
            StateHasChanged();
        }
    }

    protected async Task OnSearchInput(ChangeEventArgs e)
    {
        Query.Search = e.Value?.ToString();
        _searchDelay?.Cancel();
        _searchDelay = new CancellationTokenSource();
        try
        {
            await Task.Delay(350, _searchDelay.Token);
            Query.Page = 1;
            await LoadAsync();
        }
        catch (TaskCanceledException) { }
    }

    protected async Task SortBy(string column)
    {
        if (string.Equals(Query.SortBy, column, StringComparison.OrdinalIgnoreCase))
            Query.SortDesc = !Query.SortDesc;
        else
        {
            Query.SortBy = column;
            Query.SortDesc = false;
        }
        await LoadAsync();
    }

    protected async Task GoToPage(int page)
    {
        Query.Page = page;
        await LoadAsync();
    }

    protected virtual async Task OnContextChanged()
    {
        Query.Page = 1;
        await InvokeAsync(LoadAsync);
    }

    public virtual void Dispose()
    {
        State.ContextChanged -= OnContextChanged;
        _searchDelay?.Cancel();
    }
}
