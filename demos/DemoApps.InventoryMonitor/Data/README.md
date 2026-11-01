# Data

Products and stock report tables, kept in the `inventory` schema of the module's own database `inventory_demo` (FR-78).
The connection comes from the `Inventory` section of appsettings.json; the password comes from `GMSS_DB_PASSWORD`.

The Inventory Monitor is a removable module, not part of the scheduler core. `InventoryStore` owns its tables and
product/report queries; task handlers use `ISchedulerClient`-managed schedules and `ITaskContext` for run metadata.
Successful product creation, edits, stock adjustments and deletes are recorded through the scheduler's `IAuditLog`
contract. Modules can use event triggers for business events without moving inventory rules into the core.
