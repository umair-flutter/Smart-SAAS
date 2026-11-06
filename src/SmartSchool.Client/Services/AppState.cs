using Microsoft.JSInterop;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Client.Services;

/// <summary>
/// Client-side session: token, signed-in user and the active tenant/school context.
/// The API re-checks everything; this only drives what the UI shows.
/// </summary>
public sealed class AppState(IJSRuntime js)
{
    private const string TokenKey = "ss.token";
    private const string TenantKey = "ss.tenant";
    private const string SchoolKey = "ss.school";
    private const string YearKey = "ss.year";

    public string? Token { get; private set; }
    public CurrentUserDto? User { get; private set; }
    public Guid? ActiveTenantId { get; private set; }
    public Guid? ActiveSchoolId { get; private set; }
    public Guid? ActiveYearId { get; private set; }

    public bool IsAuthenticated => Token is not null && User is not null;
    public bool IsSuperAdmin => User?.RoleType == RoleType.SuperAdmin;

    /// <summary>Raised after sign-in, sign-out or profile refresh.</summary>
    public event Action? UserChanged;

    /// <summary>Raised when the tenant or school context changes; list pages reload their data.</summary>
    public event Func<Task>? ContextChanged;

    public bool Can(string permission) => User?.Has(permission) == true;

    public async Task LoadFromStorageAsync()
    {
        Token = await js.InvokeAsync<string?>("smartSchool.storageGet", TokenKey);
        ActiveTenantId = ParseGuid(await js.InvokeAsync<string?>("smartSchool.storageGet", TenantKey));
        ActiveSchoolId = ParseGuid(await js.InvokeAsync<string?>("smartSchool.storageGet", SchoolKey));
        ActiveYearId = ParseGuid(await js.InvokeAsync<string?>("smartSchool.storageGet", YearKey));
    }

    public async Task SignInAsync(LoginResponse response)
    {
        Token = response.Token;
        await js.InvokeVoidAsync("smartSchool.storageSet", TokenKey, response.Token);
        await SetContextValues(null, null);
        SetUser(response.User);
    }

    public void SetUser(CurrentUserDto? user)
    {
        User = user;
        // Drop a stored school that the user can no longer access.
        if (user is not null && ActiveSchoolId is { } sid && user.Schools.All(s => s.Id != sid))
            ActiveSchoolId = null;
        if (user is not null && user.RoleType.IsSchoolScoped() && ActiveSchoolId is null && user.Schools.Count > 0)
            ActiveSchoolId = user.Schools[0].Id;
        UserChanged?.Invoke();
    }

    public async Task SignOutLocalAsync()
    {
        Token = null;
        User = null;
        await js.InvokeVoidAsync("smartSchool.storageRemove", TokenKey);
        await SetContextValues(null, null);
        UserChanged?.Invoke();
    }

    public async Task ChangeTenantAsync(Guid? tenantId)
    {
        await SetContextValues(tenantId, null);
        await RaiseContextChanged();
    }

    public async Task ChangeSchoolAsync(Guid? schoolId)
    {
        await SetContextValues(ActiveTenantId, schoolId);
        await RaiseContextChanged();
    }

    public async Task ChangeYearAsync(Guid? yearId)
    {
        ActiveYearId = yearId;
        await Store(YearKey, yearId);
        await RaiseContextChanged();
    }

    private async Task SetContextValues(Guid? tenantId, Guid? schoolId)
    {
        ActiveTenantId = tenantId;
        ActiveSchoolId = schoolId;
        await Store(TenantKey, tenantId);
        await Store(SchoolKey, schoolId);
        // A new tenant or school always starts on its current academic year.
        ActiveYearId = null;
        await Store(YearKey, null);
    }

    private async Task RaiseContextChanged()
    {
        if (ContextChanged is null) return;
        foreach (var handler in ContextChanged.GetInvocationList().Cast<Func<Task>>())
            await handler();
    }

    private async Task Store(string key, Guid? value)
    {
        if (value is null) await js.InvokeVoidAsync("smartSchool.storageRemove", key);
        else await js.InvokeVoidAsync("smartSchool.storageSet", key, value.ToString());
    }

    private static Guid? ParseGuid(string? s) => Guid.TryParse(s, out var g) ? g : null;
}
