using Microsoft.JSInterop;
#if MACCATALYST
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
#endif

namespace Scheduler.Admin.Services;

/// <summary>
/// Prints the current page with the print style of app.css (FR-66, WP-8d). On Windows the WebView2 print dialog opens
/// (window.print). The Mac Catalyst web view has no print dialog, so there the printable part of the page is written
/// to an HTML file and opened in the default browser, which can print it or save it as PDF.
/// </summary>
public sealed class PrintService
{
    public async Task PrintAsync(IJSRuntime js)
    {
#if MACCATALYST
        var html = await js.InvokeAsync<string>("gmssPrintableHtml");
        var path = Path.Combine(Path.GetTempPath(), $"gsst-print-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        await File.WriteAllTextAsync(path, html);
        await Launcher.Default.OpenAsync(new OpenFileRequest("Print", new ReadOnlyFile(path)));
#else
        await js.InvokeVoidAsync("gmssPrint");
#endif
    }
}
