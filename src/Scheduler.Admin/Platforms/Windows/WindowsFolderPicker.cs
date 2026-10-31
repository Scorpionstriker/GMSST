using Scheduler.Admin.Services;

namespace Scheduler.Admin.WinUI;

/// <summary>The Windows folder dialog (WinRT FolderPicker), attached to the Admin application's window.</summary>
public sealed class WindowsFolderPicker : IFolderPicker
{
    public bool IsSupported => true;

    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var picker = new global::Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add("*");

            // A WinRT picker needs the handle of the window it belongs to.
            if (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is Microsoft.UI.Xaml.Window window)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
            }

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        });
}
