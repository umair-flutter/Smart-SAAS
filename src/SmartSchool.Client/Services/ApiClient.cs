using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using SmartSchool.Shared.Contracts;
using SmartSchool.Shared.Security;

namespace SmartSchool.Client.Services;

public sealed class ApiException(ApiError error) : Exception(error.Detail ?? error.Title)
{
    public ApiError Error { get; } = error;

    /// <summary>Field errors flattened into one readable message.</summary>
    public string Friendly =>
        Error.Errors is { Count: > 0 } errs ? string.Join(" ", errs.SelectMany(e => e.Value)) : Message;
}

/// <summary>Adds the bearer token and context headers; signs the user out when the API returns 401.</summary>
public sealed class AuthHeaderHandler(AppState state, NavigationManager nav) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (state.Token is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (state.ActiveTenantId is { } tid)
            request.Headers.Add(AppHeaders.TenantId, tid.ToString());
        if (state.ActiveSchoolId is { } sid)
            request.Headers.Add(AppHeaders.SchoolId, sid.ToString());
        if (state.ActiveYearId is { } yid)
            request.Headers.Add(AppHeaders.AcademicYearId, yid.ToString());

        var response = await base.SendAsync(request, ct);

        var isLogin = request.RequestUri?.AbsolutePath.EndsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase) == true;
        if (response.StatusCode == HttpStatusCode.Unauthorized && !isLogin && state.Token is not null)
        {
            await state.SignOutLocalAsync();
            nav.NavigateTo("login?expired=1");
        }
        return response;
    }
}

/// <summary>Typed wrapper over HttpClient that turns API errors into <see cref="ApiException"/>.</summary>
public sealed class ApiClient(HttpClient http)
{
    public async Task<T> GetAsync<T>(string url, CancellationToken ct = default) =>
        await Read<T>(await http.GetAsync(url, ct), ct);

    public async Task<TResponse> PostAsync<TResponse>(string url, object? body, CancellationToken ct = default) =>
        await Read<TResponse>(await http.PostAsJsonAsync(url, body, ct), ct);

    public async Task PostAsync(string url, object? body = null, CancellationToken ct = default) =>
        await Ensure(await http.PostAsJsonAsync(url, body, ct), ct);

    public async Task PutAsync(string url, object body, CancellationToken ct = default) =>
        await Ensure(await http.PutAsJsonAsync(url, body, ct), ct);

    public async Task<TResponse> PutAsync<TResponse>(string url, object body, CancellationToken ct = default) =>
        await Read<TResponse>(await http.PutAsJsonAsync(url, body, ct), ct);

    public async Task DeleteAsync(string url, CancellationToken ct = default) =>
        await Ensure(await http.DeleteAsync(url, ct), ct);

    /// <summary>Posts a multipart form (file uploads).</summary>
    public async Task PostFormAsync(string url, MultipartFormDataContent form, CancellationToken ct = default) =>
        await Ensure(await http.PostAsync(url, form, ct), ct);

    public async Task<T> PostFormAsync<T>(string url, MultipartFormDataContent form, CancellationToken ct = default) =>
        await Read<T>(await http.PostAsync(url, form, ct), ct);

    /// <summary>Downloads a file with the bearer token and returns its bytes and server-provided name.</summary>
    public async Task<(byte[] Bytes, string FileName, string ContentType)> DownloadAsync(string url, string fallbackName, CancellationToken ct = default)
    {
        var response = await http.GetAsync(url, ct);
        await Ensure(response, ct);
        var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName ?? fallbackName;
        return (await response.Content.ReadAsByteArrayAsync(ct), name.Trim('"'), response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
    }

    public Task<PagedResult<T>> GetPagedAsync<T>(string url, PagedQuery query, string? extra = null, CancellationToken ct = default) =>
        GetAsync<PagedResult<T>>($"{url}?{query.ToQueryString()}{(string.IsNullOrEmpty(extra) ? "" : "&" + extra)}", ct);

    private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await Ensure(response, ct);
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    private static async Task Ensure(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        ApiError? error = null;
        try { error = await response.Content.ReadFromJsonAsync<ApiError>(ct); } catch { /* not JSON */ }
        error ??= new ApiError();
        error.Status = (int)response.StatusCode;
        if (string.IsNullOrWhiteSpace(error.Title))
            error.Title = response.StatusCode switch
            {
                HttpStatusCode.Forbidden => "You do not have permission to do this.",
                HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again.",
                HttpStatusCode.TooManyRequests => "Too many attempts. Please wait a minute and try again.",
                HttpStatusCode.NotFound => "The requested item was not found.",
                _ => "The server could not complete the request."
            };
        throw new ApiException(error);
    }
}
