using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using SmartSchool.Shared.Contracts;

namespace SmartSchool.Client.Services;

/// <summary>Upload and download helpers shared by pages with documents.</summary>
public sealed class FileHelper(ApiClient api, IJSRuntime js, ToastService toasts)
{
    public const long MaxBytes = 10 * 1024 * 1024;

    public async Task DownloadAsync(FileDto file)
    {
        try
        {
            var (bytes, name, type) = await api.DownloadAsync($"api/files/{file.Id}", file.FileName);
            await js.InvokeVoidAsync("smartSchool.saveFile", name, type, bytes);
        }
        catch (ApiException ex) { toasts.Error(ex.Friendly); }
    }

    public async Task DownloadUrlAsync(string url, string fallbackName)
    {
        try
        {
            var (bytes, name, type) = await api.DownloadAsync(url, fallbackName);
            await js.InvokeVoidAsync("smartSchool.saveFile", name, type, bytes);
        }
        catch (ApiException ex) { toasts.Error(ex.Friendly); }
    }

    /// <summary>Adds a browser file to a multipart form; returns false (with a toast) when it is too large.</summary>
    public bool TryAdd(MultipartFormDataContent form, IBrowserFile? file, string field = "file")
    {
        if (file is null) return true;
        if (file.Size > MaxBytes)
        {
            toasts.Error("Files must be 10 MB or smaller.");
            return false;
        }
        var content = new StreamContent(file.OpenReadStream(MaxBytes));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType);
        form.Add(content, field, file.Name);
        return true;
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB"
    };
}
