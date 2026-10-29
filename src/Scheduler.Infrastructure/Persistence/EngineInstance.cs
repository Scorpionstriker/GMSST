namespace Scheduler.Infrastructure.Persistence;

/// <summary>
/// The single row of scheduler.engine_instance: which host runs the engine on this database (WP-1). It is written by
/// <see cref="Engine.PostgresEngineInstanceLock"/> and shown in the banner of a second engine that cannot start.
/// </summary>
public sealed class EngineInstance
{
    public const short SingletonId = 1;

    public short Id { get; set; } = SingletonId;
    public string HostName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime HeartbeatAtUtc { get; set; }
}
