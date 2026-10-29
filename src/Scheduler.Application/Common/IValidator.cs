namespace Scheduler.Application.Common;

/// <summary>
/// Validates a command or query before its handler runs (FR-74). Every class that implements this
/// interface is registered automatically by <see cref="DependencyInjection.AddSchedulerApplication"/>, and a unit
/// test checks that, so a validator cannot silently be left out.
/// </summary>
public interface IValidator<in T>
{
    Task<IReadOnlyList<Error>> ValidateAsync(T instance, CancellationToken cancellationToken = default);
}

/// <summary>Collects field errors in the order the fields appear on the form.</summary>
public sealed class ValidationErrors
{
    private readonly List<Error> _errors = [];

    public bool IsValid => _errors.Count == 0;
    public IReadOnlyList<Error> Errors => _errors;

    public ValidationErrors Add(string field, string message)
    {
        // One message per field: the first problem is the one the user fixes first.
        if (!_errors.Any(e => e.Field == field))
        {
            _errors.Add(Error.Validation(field, message));
        }

        return this;
    }

    public ValidationErrors AddIf(bool condition, string field, string message) => condition ? Add(field, message) : this;
}
