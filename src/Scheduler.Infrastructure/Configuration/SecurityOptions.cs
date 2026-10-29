using Microsoft.Extensions.Options;
using Scheduler.Application.Common;

namespace Scheduler.Infrastructure.Configuration;

/// <summary>The "Security" section of the configuration (WP-4a).</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Opens the Admin application without sign-in. For development only; off by default.</summary>
    public bool AllowLoginBypass { get; set; }
}

/// <summary>
/// Reads Security:AllowLoginBypass from the configuration and follows changes to the user settings file, so turning
/// the bypass on or off on the Configuration page takes effect without a restart.
/// </summary>
internal sealed class ConfigurationLoginBypass : ILoginBypass, IDisposable
{
    private readonly IOptionsMonitor<SecurityOptions> _options;
    private readonly IDisposable? _subscription;
    private bool _isOn;

    public ConfigurationLoginBypass(IOptionsMonitor<SecurityOptions> options)
    {
        _options = options;
        _isOn = options.CurrentValue.AllowLoginBypass;
        _subscription = options.OnChange(o =>
        {
            if (o.AllowLoginBypass != _isOn)
            {
                _isOn = o.AllowLoginBypass;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    public bool IsOn => _options.CurrentValue.AllowLoginBypass;

    public event EventHandler? Changed;

    public void Dispose() => _subscription?.Dispose();
}
