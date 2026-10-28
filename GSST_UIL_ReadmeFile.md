# GSST UI Layer

Project: `Scheduler.Admin`.

A .NET MAUI Blazor Hybrid desktop application for Windows and macOS. It is the composition root: it
registers the core layers and the modules listed in `ModuleCatalog.cs`, starts the scheduling engine when
the window opens and stops it when the window closes.

Menu: Dashboard, Tasks, Queue, Execution History, Error Log, Reports, module pages (Inventory, Users),
Configuration, Modules, Backup & Restore and Help. Pages live in `Components/Pages`; module pages are
found in the module assemblies by the router.
