# GSST UI Layer README

Project: `Scheduler.Admin`.

A .NET MAUI Blazor Hybrid desktop application for Windows and macOS. It is the composition root: it
registers the core layers and the modules listed in `ModuleCatalog.cs`, starts the scheduler when the window
opens (showing a start-up screen, and a plain-language message with Try again if PostgreSQL cannot be reached)
and stops it in a controlled way when the window closes.

Menu: Dashboard, Tasks, Queue, Execution History, Error Log, administrator/developer Log History, Reports,
Notifications and Account, module pages (Inventory, Maintenance Agent, Users), Configuration, Modules,
Backup & Restore and Help. Pages live in `Components/Pages`, shared parts (status badges, paging, confirmation
dialog, SVG charts) in `Components/Shared`, and startup, toast, theme, folder-picker and CSV-save services in
`Services`.

Module pages are discovered from the assemblies in `ModuleCatalog.cs`. Menu visibility is role-aware:
Administrators and Developer super-admins have administrative access; Viewers can read monitoring pages but
cannot change system data. Use cases independently enforce authorization, so hiding a link is not the security
boundary. Local machine paths, including the configured default CSV folder and recent export/backup locations,
are held in the local user settings file, not database backups.

The Error log and Log History are direct query pages. Reports remain generated views with filters and optional
CSV/print output. CSV exports support a configured default and an editable destination. The Help page's Developer
Support topic links to the architecture, module and core-development guides.

See [How_It_Works.md](docs/How_It_Works.md) and [Core_Development.md](docs/Core_Development.md).
