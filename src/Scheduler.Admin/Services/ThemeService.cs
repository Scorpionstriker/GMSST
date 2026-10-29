using Microsoft.JSInterop;

namespace Scheduler.Admin.Services;

/// <summary>Light or dark theme, remembered between sessions (NFR-01).</summary>
public sealed class ThemeService(IPreferenceStore preferences)
{
    private const string Key = "theme";

    public string Theme { get; private set; } = preferences.Get(Key) == "dark" ? "dark" : "light";

    public bool IsDark => Theme == "dark";

    public Task ApplyAsync(IJSRuntime js) => js.InvokeVoidAsync("gmssSetTheme", Theme).AsTask();

    public Task ToggleAsync(IJSRuntime js)
    {
        Theme = IsDark ? "light" : "dark";
        preferences.Set(Key, Theme);
        return ApplyAsync(js);
    }
}
