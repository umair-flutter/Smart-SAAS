using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SmartSchool.Shared.Contracts;

namespace SmartSchool.Client.Services;

/// <summary>Sign-in, sign-out, profile loading and the activity-based session heartbeat.</summary>
public sealed class SessionService(AppState state, ApiClient api, IJSRuntime js, NavigationManager nav) : IAsyncDisposable
{
    private static readonly TimeSpan HeartbeatEvery = TimeSpan.FromMinutes(2);
    private PeriodicTimer? _timer;
    private bool _initialized;

    /// <summary>Restores a stored session on app start. Returns false when the user must sign in.</summary>
    public async Task<bool> InitializeAsync()
    {
        if (_initialized) return state.IsAuthenticated;
        _initialized = true;

        await state.LoadFromStorageAsync();
        if (state.Token is null) return false;

        try
        {
            state.SetUser(await api.GetAsync<CurrentUserDto>("api/auth/me"));
            StartHeartbeat();
            return true;
        }
        catch
        {
            await state.SignOutLocalAsync();
            return false;
        }
    }

    public async Task LoginAsync(LoginRequest request)
    {
        var response = await api.PostAsync<LoginResponse>("api/auth/login", request);
        await state.SignInAsync(response);
        _initialized = true;
        StartHeartbeat();
    }

    public async Task LogoutAsync()
    {
        try { await api.PostAsync("api/auth/logout"); } catch { /* already expired */ }
        StopHeartbeat();
        await state.SignOutLocalAsync();
        nav.NavigateTo("login");
    }

    /// <summary>Reloads the profile, e.g. after a SuperAdmin switches tenant (the school list changes).</summary>
    public async Task RefreshUserAsync() => state.SetUser(await api.GetAsync<CurrentUserDto>("api/auth/me"));

    private void StartHeartbeat()
    {
        if (_timer is not null) return;
        _timer = new PeriodicTimer(HeartbeatEvery);
        _ = RunHeartbeat(_timer);
    }

    private void StopHeartbeat()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private async Task RunHeartbeat(PeriodicTimer timer)
    {
        try
        {
            while (await timer.WaitForNextTickAsync())
            {
                if (!state.IsAuthenticated) continue;
                // Only keep the session alive while someone is actually using the page;
                // an idle tab lets the server-side idle timeout expire the session.
                var idleMs = await js.InvokeAsync<double>("smartSchool.idleMs");
                if (idleMs > HeartbeatEvery.TotalMilliseconds) continue;
                try { await api.PostAsync("api/auth/heartbeat"); } catch { /* 401 is handled by AuthHeaderHandler */ }
            }
        }
        catch (ObjectDisposedException) { }
    }

    public ValueTask DisposeAsync()
    {
        StopHeartbeat();
        return ValueTask.CompletedTask;
    }
}

public enum ToastKind { Success, Error, Info }

public sealed record Toast(Guid Id, ToastKind Kind, string Message);

public sealed class ToastService
{
    public List<Toast> Items { get; } = [];
    public event Action? Changed;

    public void Success(string message) => Add(ToastKind.Success, message);
    public void Error(string message) => Add(ToastKind.Error, message);
    public void Info(string message) => Add(ToastKind.Info, message);

    public void Dismiss(Guid id)
    {
        Items.RemoveAll(t => t.Id == id);
        Changed?.Invoke();
    }

    private void Add(ToastKind kind, string message)
    {
        var toast = new Toast(Guid.NewGuid(), kind, message);
        Items.Add(toast);
        Changed?.Invoke();
        _ = Task.Delay(kind == ToastKind.Error ? 7000 : 4000).ContinueWith(_ => Dismiss(toast.Id));
    }
}
