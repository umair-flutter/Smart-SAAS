using System.Globalization;
using System.Net.Http.Json;
using Microsoft.JSInterop;

namespace SmartSchool.Client.Services;

/// <summary>
/// UI translations. Keys are the English texts, so a missing translation simply shows English.
/// Dictionaries live in wwwroot/i18n/{fr,ar}.json. Arabic switches the page to right-to-left.
/// </summary>
public sealed class Localizer
{
    public const string StorageKey = "ss.lang";

    /// <summary>Set when the language was picked on the sign-in page, so it is saved to the account after sign-in.</summary>
    public const string PickedKey = "ss.lang.picked";
    public static readonly (string Code, string Name)[] Languages = [("en", "English"), ("fr", "Français"), ("ar", "العربية")];

    private Dictionary<string, string> _strings = [];

    public string Language { get; private set; } = "en";
    public bool IsRtl => Language == "ar";

    public string this[string key] => _strings.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : key;

    /// <summary>Translated format string, e.g. L.F("Showing {0} to {1} of {2} entries", a, b, c).</summary>
    public string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, this[key], args);

    /// <summary>Loads the language chosen earlier (or the browser default) before the app renders.</summary>
    public async Task InitializeAsync(IJSRuntime js, HttpClient http)
    {
        var stored = await js.InvokeAsync<string?>("smartSchool.storageGet", StorageKey);
        var lang = stored is "fr" or "ar" or "en" ? stored : "en";
        Language = lang;

        var culture = CultureInfo.GetCultureInfo(lang switch { "fr" => "fr-FR", "ar" => "ar-MA", _ => "en-US" });
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        // en.json only holds friendly names for internal codes (e.g. "SignedIn" → "Signed in").
        try { _strings = await http.GetFromJsonAsync<Dictionary<string, string>>($"i18n/{lang}.json") ?? []; }
        catch { _strings = []; }
        await js.InvokeVoidAsync("smartSchool.setDocumentLanguage", lang, IsRtl);
    }

    /// <summary>Stores a new language and reloads so culture, direction and texts all switch together.</summary>
    public static async Task ChangeAsync(IJSRuntime js, string lang)
    {
        await js.InvokeVoidAsync("smartSchool.storageSet", StorageKey, lang);
        await js.InvokeVoidAsync("location.reload");
    }
}
