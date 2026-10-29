using Scheduler.Application.Common;
using Scheduler.Application.Common.Persistence;
using Scheduler.Application.Tasks;
using Scheduler.Domain.Entities;
using Scheduler.Domain.Enums;

namespace Scheduler.Application.Configuration;

/// <summary>Everything on the Configuration page (FR-36, FR-29).</summary>
public sealed record GetSettingsQuery : IRequireAdministrator;

public sealed record SettingsOverview(
    IReadOnlyList<GlobalSettingView> Global,
    IReadOnlyList<TaskSettingView> TaskOverrides,
    IReadOnlyList<LoggingProviderView> LoggingProviders);

public sealed record GlobalSettingView(SettingDefinition Definition, string Value, DateTime? UpdatedAtUtc);

public sealed record TaskSettingView(Guid TaskId, string TaskName, string Key, string Value, DateTime UpdatedAtUtc);

public sealed record LoggingProviderView(Guid Id, string Name, LoggingProviderType Type, SchedulerLogLevel MinimumLevel, bool IsActive);

/// <summary>Sets a global value, or a task's own value when <paramref name="TaskId"/> is given (FR-36, FR-37).</summary>
public sealed record UpdateSettingCommand(string Key, string Value, Guid? TaskId = null) : IRequireAdministrator;

/// <summary>Removes a task's own value, so the global value applies again.</summary>
public sealed record RemoveTaskSettingCommand(Guid TaskId, string Key) : IRequireAdministrator;

/// <summary>Enables or disables a logging provider and sets its minimum level (FR-29).</summary>
public sealed record UpdateLoggingProviderCommand(Guid ProviderId, bool IsActive, SchedulerLogLevel MinimumLevel) : IRequireAdministrator;

internal sealed class GetSettingsHandler(IUnitOfWorkFactory unitOfWorkFactory, ITaskQueries taskQueries) : IQueryHandler<GetSettingsQuery, SettingsOverview>
{
    public async Task<Result<SettingsOverview>> HandleAsync(GetSettingsQuery query, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var stored = await unitOfWork.Configuration.GetAllAsync(cancellationToken);
        var providers = await unitOfWork.Configuration.GetLoggingProvidersAsync(cancellationToken);
        var taskNames = (await taskQueries.GetLookupsAsync(includeDeleted: true, cancellationToken)).ToDictionary(t => t.Id, t => t.Name);

        var global = SettingDefinitions.All
            .Select(d =>
            {
                var row = stored.FirstOrDefault(s => s.TaskId is null && s.Key == d.Key);
                return new GlobalSettingView(d, row?.Value ?? d.DefaultValue.ToString(System.Globalization.CultureInfo.InvariantCulture), row?.UpdatedAtUtc);
            })
            .ToList();

        var overrides = stored
            .Where(s => s.TaskId is not null)
            .Select(s => new TaskSettingView(s.TaskId!.Value, taskNames.GetValueOrDefault(s.TaskId.Value, "(deleted task)"), s.Key, s.Value, s.UpdatedAtUtc))
            .OrderBy(s => s.TaskName).ThenBy(s => s.Key)
            .ToList();

        var loggingProviders = providers
            .Select(p => new LoggingProviderView(p.Id, p.ProviderName, p.ProviderType, p.MinimumLevel, p.IsActive))
            .OrderBy(p => p.Type).ThenBy(p => p.Name)
            .ToList();

        return Result.Success(new SettingsOverview(global, overrides, loggingProviders));
    }
}

internal sealed class UpdateSettingValidator(IUnitOfWorkFactory unitOfWorkFactory) : IValidator<UpdateSettingCommand>
{
    public async Task<IReadOnlyList<Error>> ValidateAsync(UpdateSettingCommand command, CancellationToken cancellationToken = default)
    {
        var errors = new ValidationErrors();
        var definition = SettingDefinitions.Find(command.Key);
        if (definition is null)
        {
            return errors.Add("Key", "Unknown setting.").Errors;
        }

        var message = definition.Validate(command.Value, out _);
        errors.AddIf(message.Length > 0, "Value", message);

        if (command.TaskId is not null)
        {
            errors.AddIf(!definition.AllowTaskOverride, "Key", $"{definition.Key} cannot be set for a single task.");
            await using var unitOfWork = unitOfWorkFactory.Create();
            errors.AddIf(await unitOfWork.Tasks.GetAsync(command.TaskId.Value, cancellationToken: cancellationToken) is null, "TaskId",
                "Select an existing task.");
        }

        return errors.Errors;
    }
}

internal sealed class UpdateSettingHandler(IUnitOfWorkFactory unitOfWorkFactory, ISettingsProvider settings, TimeProvider clock, IEngineSignal engine)
    : ICommandHandler<UpdateSettingCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdateSettingCommand command, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var value = command.Value.Trim();

        await using var unitOfWork = unitOfWorkFactory.Create();
        var existing = await unitOfWork.Configuration.FindAsync(command.TaskId, command.Key, cancellationToken);
        if (existing is null)
        {
            unitOfWork.Configuration.Add(command.TaskId is null
                ? ConfigurationSetting.Global(command.Key, value, now)
                : ConfigurationSetting.ForTask(command.TaskId.Value, command.Key, value, now));
        }
        else
        {
            existing.ChangeValue(value, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        settings.Invalidate();
        engine.Wake();
        return Result.Success(Unit.Value);
    }
}

internal sealed class RemoveTaskSettingHandler(IUnitOfWorkFactory unitOfWorkFactory, ISettingsProvider settings)
    : ICommandHandler<RemoveTaskSettingCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RemoveTaskSettingCommand command, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = unitOfWorkFactory.Create();
        var existing = await unitOfWork.Configuration.FindAsync(command.TaskId, command.Key, cancellationToken);
        if (existing is not null)
        {
            unitOfWork.Configuration.Remove(existing);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            settings.Invalidate();
        }

        return Result.Success(Unit.Value);
    }
}

internal sealed class UpdateLoggingProviderHandler(IUnitOfWorkFactory unitOfWorkFactory, ISettingsProvider settings)
    : ICommandHandler<UpdateLoggingProviderCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(UpdateLoggingProviderCommand command, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(command.MinimumLevel))
        {
            return Error.Validation("MinimumLevel", "Select Info, Warning or Error.");
        }

        await using var unitOfWork = unitOfWorkFactory.Create();
        var provider = await unitOfWork.Configuration.GetLoggingProviderAsync(command.ProviderId, cancellationToken);
        if (provider is null)
        {
            return Error.NotFound("Logging provider");
        }

        provider.Change(command.IsActive, command.MinimumLevel);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        settings.Invalidate();
        return Result.Success(Unit.Value);
    }
}
