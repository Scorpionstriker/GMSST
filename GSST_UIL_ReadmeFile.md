# GSST UI Layer

Project: `Scheduler.Admin`.

A .NET MAUI Blazor Hybrid desktop application for Windows and macOS. It is the composition root: it
registers the core layers and the modules listed in `ModuleCatalog.cs`, starts the scheduler when the window
opens (showing a start-up screen, and a plain-language message with Try again if PostgreSQL cannot be reached)
and stops it in a controlled way when the window closes.

Menu: Dashboard, Tasks, Queue, Execution History, Error Log, Reports, module pages (Inventory, Maintenance
Agent, Users), Configuration, Modules, Backup & Restore and Help. Pages live in `Components/Pages`, shared parts (status
badges, paging, confirmation dialog, SVG charts) in `Components/Shared`, and the start-up, toast, theme and
file-export services in `Services`. Module pages are found in the module assemblies by the router. When the
Login and Security module is loaded, every page except Login requires a signed-in user and Viewers do not see
administrator-only pages. Without a login module the pages stay closed, unless the developer login bypass is on,
in which case every page shows a red warning.
