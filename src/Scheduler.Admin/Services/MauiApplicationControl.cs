using Scheduler.Extensibility.Contracts;

namespace Scheduler.Admin.Services;

public sealed class MauiApplicationControl : IApplicationControl
{
    public string Version => Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString;

    public void RequestClose() => Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
    {
#if WINDOWS
        if (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
            is Microsoft.UI.Xaml.Window nativeWindow)
        {
            nativeWindow.Close();
            return;
        }
#endif
        Microsoft.Maui.Controls.Application.Current?.Quit();
    });
}
