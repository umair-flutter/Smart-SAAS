using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SmartSchool.Client;
using SmartSchool.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The API address defaults to the host that serves the client (hosted deployment).
// For a separately hosted client set "ApiBaseUrl" in wwwroot/appsettings.json.
var apiBase = builder.Configuration["ApiBaseUrl"];
var baseAddress = new Uri(string.IsNullOrWhiteSpace(apiBase) ? builder.HostEnvironment.BaseAddress : apiBase);

builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped(sp => new HttpClient(new AuthHeaderHandler(
        sp.GetRequiredService<AppState>(), sp.GetRequiredService<NavigationManager>())
    { InnerHandler = new HttpClientHandler() })
{
    BaseAddress = baseAddress
});
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<FileHelper>();
builder.Services.AddSingleton<Localizer>();

var host = builder.Build();
// Load the chosen language (texts, culture, right-to-left) before the first render.
await host.Services.GetRequiredService<Localizer>().InitializeAsync(
    host.Services.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
await host.RunAsync();
