using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Scheduler.Extensibility.Contracts;

namespace Scheduler.UnitTests;

/// <summary>
/// F-33: every menu entry leads to a page, and every page is in the menu or linked from a page that is. The Admin
/// application is a MAUI project that tests cannot reference, so its menu and pages are read from the source files;
/// module pages are read from the compiled module assemblies.
/// </summary>
public sealed partial class NavigationTests
{
    // Pages that are deliberately not in the menu, with the file and text that links to them.
    private static readonly (string Route, string LinkedFrom, string Link)[] LinkedPages =
    [
        ("/tasks/new", "src/Scheduler.Admin/Components/Pages/Tasks.razor", "href=\"tasks/new\""),
        ("/tasks/{TaskId:guid}", "src/Scheduler.Admin/Components/Pages/Tasks.razor", "href=\"tasks/@task.Id\""),
        ("/login", "src/Scheduler.Admin/Components/Layout/MainLayout.razor", "/login?returnUrl="),
        ("/not-found", "src/Scheduler.Admin/Components/Routes.razor", "NotFoundPage=\"typeof(Pages.NotFound)\"")
    ];

    private static readonly ISchedulerModule[] Modules =
    [
        new Scheduler.Modules.Security.SecurityModule(),
        new DemoApps.InventoryMonitor.InventoryModule(),
        new DemoApps.MaintenanceAgentConnector.MaintenanceAgentConnectorModule()
    ];

    [Fact]
    public void EveryMenuEntry_HasAPage()
    {
        var pages = AllPageRoutes();
        var missing = MenuRoutes().Where(route => !pages.Contains(route)).ToList();

        Assert.True(missing.Count == 0, "Menu entries without a page: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryPage_IsInTheMenuOrLinkedFromAPage()
    {
        var menu = MenuRoutes();
        var unreachable = new List<string>();
        foreach (var route in AllPageRoutes().Where(r => !menu.Contains(r)))
        {
            var link = LinkedPages.FirstOrDefault(p => p.Route == route);
            if (link.Route is null || !File.ReadAllText(Path.Combine(Root, link.LinkedFrom)).Contains(link.Link, StringComparison.Ordinal))
            {
                unreachable.Add(route);
            }
        }

        Assert.True(unreachable.Count == 0, "Pages that no menu entry or page leads to: " + string.Join(", ", unreachable));
    }

    [Fact]
    public void ModuleMenuEntries_PointToPagesOfThatModule()
    {
        foreach (var module in Modules)
        {
            var routes = RoutesIn(module.GetType().Assembly);
            Assert.All(module.MenuItems, item => Assert.Contains(item.Route, routes));
        }
    }

    private static HashSet<string> MenuRoutes()
    {
        var navMenu = File.ReadAllText(Path.Combine(Root, "src/Scheduler.Admin/Components/Layout/NavMenu.razor"));
        var core = MenuItemPattern().Matches(navMenu).Select(m => m.Groups[1].Value);
        Assert.NotEmpty(core);
        return core.Concat(Modules.SelectMany(m => m.MenuItems).Select(i => i.Route)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> AllPageRoutes()
    {
        var pagesFolder = Path.Combine(Root, "src/Scheduler.Admin/Components");
        var admin = Directory.EnumerateFiles(pagesFolder, "*.razor", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadLines(file).TakeWhile(line => line.StartsWith('@')))
            .Select(line => PagePattern().Match(line))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value);
        return admin.Concat(Modules.SelectMany(m => RoutesIn(m.GetType().Assembly))).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> RoutesIn(Assembly assembly) =>
        assembly.GetTypes().SelectMany(t => t.GetCustomAttributes<RouteAttribute>()).Select(r => r.Template).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Root
    {
        get
        {
            for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            {
                if (File.Exists(Path.Combine(folder.FullName, "GSST.slnx")))
                {
                    return folder.FullName;
                }
            }

            throw new InvalidOperationException("GSST.slnx was not found above the test folder.");
        }
    }

    [GeneratedRegex("""new\("[^"]+", "(/[^"]*)", "[^"]+"(, AdministratorOnly: true)?\)""")]
    private static partial Regex MenuItemPattern();

    [GeneratedRegex("""^@page "([^"]+)"$""")]
    private static partial Regex PagePattern();
}
