namespace Scheduler.Domain.Rules;

/// <summary>
/// Thrown when code asks an entity for a change its rules do not allow, for example completing an
/// execution that is not running. Use cases check the rules first, so this signals a programming error.
/// </summary>
public sealed class DomainRuleException(string message) : InvalidOperationException(message);
