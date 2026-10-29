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

    /// <summary>
    /// Positive control: the same rule must fail for a dependency that really exists, otherwise the tests above
    /// could pass without checking anything (for example if the names given to the matcher were wrong).
    /// </summary>
    [Theory]
    [InlineData(Infrastructure, Application)]
    [InlineData(Engine, Application)]
    [InlineData(Security, Extensibility)]
    [InlineData(Inventory, Extensibility)]
    [InlineData(MaintenanceAgent, Engine)]
    public void Rule_DetectsAnExistingDependency(string project, string dependency)
    {
        var result = Types.InAssembly(Assembly.Load(project))
            .ShouldNot()
            .HaveDependencyOn(dependency)
            .GetResult();

        Assert.False(result.IsSuccessful, $"{project} should use {dependency}; if this fails the dependency rules are not being checked.");
    }

    /// <summary>Only the infrastructure layer talks to PostgreSQL; the other layers use its interfaces (Phase 4, Section 3.1).</summary>
    [Theory]
    [InlineData(Domain)]
    [InlineData(Extensibility)]
    [InlineData(Application)]
    [InlineData(Engine)]
    [InlineData(Security)]
    [InlineData(Inventory)]
    public void OnlyInfrastructure_UsesEntityFrameworkOrNpgsql(string project)
    {
        var result = Types.InAssembly(Assembly.Load(project))
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }
}
