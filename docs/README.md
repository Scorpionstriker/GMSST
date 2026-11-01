# Documentation

## Start here

| File | Audience | Contents |
|---|---|---|
| [How_It_Works.md](How_It_Works.md) | Everyone, especially developers | System boundaries, startup-to-execution flow, persistence and module lifecycle |
| [Module_Development.md](Module_Development.md) | Module and integration developers | Extensibility contracts, module setup, event triggers, notifications, audit and UI contributions |
| [Core_Development.md](Core_Development.md) | Core maintainers | Where to implement scheduler-wide features, schema changes, testing and platform considerations |

## README references

| File | Contents |
|---|---|
| [../README.md](../README.md) | Repository overview and quick start |
| [../EL_ReadmeFile.md](../EL_ReadmeFile.md) | Extensibility contracts |
| [../CSEL_ReadmeFile.md](../CSEL_ReadmeFile.md) | Core engine and application layers |
| [../DL_ReadmeFile.md](../DL_ReadmeFile.md) | Persistence and infrastructure layer |
| [../GSST_UIL_ReadmeFile.md](../GSST_UIL_ReadmeFile.md) | Admin user interface |
| [../database/README.md](../database/README.md) | Database provisioning and schema ownership |
| [../database/seed/README.md](../database/seed/README.md) | Seeded settings and built-in tasks |
| [../demos/DemoApps.InventoryMonitor/Data/README.md](../demos/DemoApps.InventoryMonitor/Data/README.md) | Inventory demo data ownership and integration |

## Guides and test evidence

| File | Contents | Requirement |
|---|---|---|
| [Installation_Guide.md](Installation_Guide.md) | Installing and configuring GSST on Windows, PostgreSQL local-only settings, moving to another computer, login bypass, troubleshooting | DR-04, NFR-04, NFR-14 |
| [Build_macOS.md](Build_macOS.md) | Building and running on macOS 14 or later | DR-04, NFR-10 |
| [Build_Linux.md](Build_Linux.md) | Core libraries and the Maintenance Agent on Linux | DR-04, NFR-10 |
| [User_Manual.md](User_Manual.md) | Every page of the Admin app, roles and common tasks | DR-04 |
| [Technical_Guide.md](Technical_Guide.md) | Detailed architecture, engine, database design, integration methods, extension APIs, configuration, logging, backups and health monitoring | DR-03, NFR-21 |
| [Test_Cases.md](Test_Cases.md) | Test cases for every requirement, manual procedures and the click-through checklist | DR-05 |
| [Test_Results.md](Test_Results.md) | Results of the automated test runs; TRX files in [test-results/](test-results/) | DR-05, NFR-22 |
| [UAT_Plan.md](UAT_Plan.md) | User acceptance test plan | DR-06 |
| [UAT_Report.md](UAT_Report.md) | User acceptance test report (filled in after the sessions) | DR-06 |
