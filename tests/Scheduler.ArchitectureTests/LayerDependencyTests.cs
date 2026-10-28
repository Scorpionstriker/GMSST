using System.Reflection;
using NetArchTest.Rules;

namespace Scheduler.ArchitectureTests;

/// <summary>
/// Fails the build if a project uses a layer it is not allowed to depend on (Phase 4, Table 3.1).
/// The Admin application is the composition root and may use everything, so it is not checked here.
/// </summary>
public class LayerDependencyTests
{
    private const string Domain = "Scheduler.Domain";
    private const string Extensibility = "Scheduler.Extensibility";
    private const string Application = "Scheduler.Application";
    private const string Infrastructure = "Scheduler.Infrastructure";
    private const string Engine = "Scheduler.Engine";
    private const string Security = "Scheduler.Modules.Security";
    private const string Admin = "Scheduler.Admin";
    private const string Inventory = "DemoApps.InventoryMonitor";
    private const string MaintenanceAgent = "DemoApps.MaintenanceAgent";

    public static TheoryData<string, string[]> ForbiddenDependencies => new()
    {
        { Domain, [Extensibility, Application, Infrastructure, Engine, Security, Admin, Inventory, MaintenanceAgent] },
        { Extensibility, [Application, Infrastructure, Engine, Security, Admin, Inventory, MaintenanceAgent] },
        { Application, [Infrastructure, Engine, Security, Admin, Inventory, MaintenanceAgent] },
        { Infrastructure, [Engine, Security, Admin, Inventory, MaintenanceAgent] },
        { Engine, [Infrastructure, Security, Admin, Inventory, MaintenanceAgent] },
        { Security, [Application, Infrastructure, Engine, Admin, Inventory, MaintenanceAgent] },
        { Inventory, [Application, Infrastructure, Engine, Security, Admin, MaintenanceAgent] },
        { MaintenanceAgent, [Security, Admin, Inventory] },
    };

    [Theory]
    [MemberData(nameof(ForbiddenDependencies))]
    public void Project_DoesNotDependOnForbiddenLayers(string project, string[] forbidden)
    {
        var result = Types.InAssembly(Assembly.Load(project))
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        var offenders = string.Join(", ", result.FailingTypeNames ?? []);
        Assert.True(result.IsSuccessful, $"{project} depends on a forbidden layer: {offenders}");
    }

    [Theory]
    [InlineData(Domain)]
    [InlineData(Extensibility)]
    public void CoreContracts_DoNotDependOnEntityFramework(string project)
    {
        var result = Types.InAssembly(Assembly.Load(project))
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }
}
