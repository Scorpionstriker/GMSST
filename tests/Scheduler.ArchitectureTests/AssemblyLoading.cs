using DemoApps.InventoryMonitor;
using DemoApps.MaintenanceAgent.Handlers;
using Scheduler.Domain.Entities;
using Scheduler.Engine;
using Scheduler.Extensibility.Contracts;
using Scheduler.Infrastructure.Persistence;
using Scheduler.Modules.Security;

namespace Scheduler.ArchitectureTests;

/// <summary>Makes sure every checked assembly is referenced, so Assembly.Load finds it.</summary>
public class AssemblyLoading
{
    [Fact]
    public void AllCheckedAssembliesAreReferenced()
    {
        Type[] anchors =
        [
            typeof(AuditableEntity), typeof(ITaskHandler), typeof(Scheduler.Application.DependencyInjection),
            typeof(SchedulerDbContext), typeof(SchedulerEngine), typeof(SecurityModule),
            typeof(InventoryModule), typeof(HealthCheckHandler)
        ];

        Assert.Equal(8, anchors.Select(t => t.Assembly).Distinct().Count());
    }
}
