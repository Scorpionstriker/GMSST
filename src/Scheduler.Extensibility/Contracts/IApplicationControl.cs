namespace Scheduler.Extensibility.Contracts;

/// <summary>Host application information and user-initiated close requests for module UI.</summary>
public interface IApplicationControl
{
    string Version { get; }

    void RequestClose();
}
