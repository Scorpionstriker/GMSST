using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Scheduler.Extensibility.Contracts;

namespace DemoApps.InventoryMonitor.Data;

public sealed record Product(Guid Id, string Sku, string Name, decimal UnitPrice, int QuantityOnHand, int ReorderLevel, DateTime UpdatedAtUtc)
{
    public bool IsLow => QuantityOnHand <= ReorderLevel;
    public decimal StockValue => UnitPrice * QuantityOnHand;
}

public sealed record StockReport(Guid Id, string ReportType, DateTime CreatedAtUtc, int ProductCount, int LowStockCount, decimal TotalValue, string Details);

public sealed record LowStockLine(string Sku, string Name, int QuantityOnHand, int ReorderLevel);

/// <summary>
/// The module's own data, in the "inventory" schema of its own database inventory_demo (FR-78, WP-1). The connection
/// comes from <see cref="IModuleDatabases"/> and the "Inventory" configuration section, so the module needs no reference
/// to the scheduler's data layer. Product data stays in inventory_demo; user actions are recorded through the scheduler's
/// audit contract. Changes to products and stock need
/// the Administrator role (WP-4). All SQL is parameterised (Phase 4, Section 8.4).
/// </summary>
public sealed class InventoryStore(IModuleDatabases databases, ICurrentUser currentUser, IAuditLog audit)
{
    public const string ConfigurationSection = "Inventory";
    public const string DefaultDatabaseName = "inventory_demo";
    internal const string AdministratorOnly = "Only an administrator can change products and stock.";

    public const string ReportStock = "StockReport";
    public const string ReportLowStock = "LowStock";
    public const string ReportWeekly = "WeeklySummary";

    private DbDataSource DataSource => databases.Get(ConfigurationSection, DefaultDatabaseName);

    // Idempotent schema script: the module's equivalent of a migration, applied at start-up. execution_id makes a
    // report idempotent: a run that is repeated after a crash finds its report already written (WP-2).
    private const string SchemaSql =
        """
        CREATE SCHEMA IF NOT EXISTS inventory;
        CREATE TABLE IF NOT EXISTS inventory.product (
            product_id UUID PRIMARY KEY,
            sku VARCHAR(30) NOT NULL,
            name VARCHAR(100) NOT NULL,
            unit_price NUMERIC(10,2) NOT NULL CHECK (unit_price >= 0),
            quantity_on_hand INTEGER NOT NULL CHECK (quantity_on_hand >= 0),
            reorder_level INTEGER NOT NULL CHECK (reorder_level >= 0),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT now());
        CREATE UNIQUE INDEX IF NOT EXISTS ux_product_sku ON inventory.product (lower(sku));
        CREATE TABLE IF NOT EXISTS inventory.stock_report (
            report_id UUID PRIMARY KEY,
            report_type VARCHAR(20) NOT NULL CHECK (report_type IN ('StockReport', 'LowStock', 'WeeklySummary')),
            created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
            product_count INTEGER NOT NULL,
            low_stock_count INTEGER NOT NULL,
            total_value NUMERIC(14,2) NOT NULL,
            details JSONB NOT NULL DEFAULT '{}',
            execution_id UUID NULL);
        ALTER TABLE inventory.stock_report ADD COLUMN IF NOT EXISTS execution_id UUID NULL;
        CREATE UNIQUE INDEX IF NOT EXISTS ux_stock_report_execution ON inventory.stock_report (execution_id);
        CREATE INDEX IF NOT EXISTS ix_stock_report_time ON inventory.stock_report (report_type, created_at DESC);
        """;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = DataSource.CreateCommand(SchemaSql);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (DbException ex) when (ex.SqlState == "3D000")
        {
            var name = databases.GetDatabaseName(ConfigurationSection, DefaultDatabaseName);
            throw new InvalidOperationException(
                $"The database {name} does not exist. Run database\\setup_windows.ps1 (or database/create_database.sql) to create it.", ex);
        }
    }

    /// <summary>Adds sample products the first time, so the demonstration has data to work with.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using (var count = DataSource.CreateCommand("SELECT count(*) FROM inventory.product"))
        {
            if (Convert.ToInt64(await count.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0)
            {
                return;
            }
        }

        (string Sku, string Name, decimal Price, int Quantity, int Reorder)[] samples =
        [
            ("A4-PAPER", "A4 printer paper (box)", 89.99m, 42, 20),
            ("TONER-BK", "Black toner cartridge", 1249.00m, 3, 5),
            ("PEN-BLUE", "Blue ballpoint pens (pack of 10)", 34.50m, 120, 30),
            ("STAPLER", "Desk stapler", 159.00m, 8, 4),
            ("USB-32", "USB flash drive 32 GB", 129.00m, 6, 10),
            ("NOTEBOOK", "A5 notebook", 45.00m, 75, 25),
            ("MOUSE", "Wireless mouse", 299.00m, 2, 5),
            ("CABLE-HDMI", "HDMI cable 2 m", 119.00m, 18, 6)
        ];

        foreach (var sample in samples)
        {
            await using var insert = DataSource.CreateCommand(
                "INSERT INTO inventory.product (product_id, sku, name, unit_price, quantity_on_hand, reorder_level) VALUES (@id, @sku, @name, @price, @qty, @reorder)");
            Add(insert, "id", Guid.CreateVersion7());
            Add(insert, "sku", sample.Sku);
            Add(insert, "name", sample.Name);
            Add(insert, "price", sample.Price);
            Add(insert, "qty", sample.Quantity);
            Add(insert, "reorder", sample.Reorder);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        await using var command = DataSource.CreateCommand(
            "SELECT product_id, sku, name, unit_price, quantity_on_hand, reorder_level, updated_at FROM inventory.product ORDER BY name");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var products = new List<Product>();
        while (await reader.ReadAsync(cancellationToken))
        {
            products.Add(new Product(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), reader.GetInt32(4),
                reader.GetInt32(5), reader.GetDateTime(6)));
        }

        return products;
    }

    /// <summary>Creates a product when <paramref name="id"/> is null, otherwise updates it. Returns an error message or null.</summary>
    public async Task<string?> SaveProductAsync(Guid? id, string sku, string name, decimal unitPrice, int quantity, int reorderLevel,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAdministrator)
        {
            return AdministratorOnly;
        }

        sku = sku.Trim().ToUpperInvariant();
        name = name.Trim();
        if (sku.Length is 0 or > 30 || name.Length is 0 or > 100)
        {
            return "Enter a SKU of up to 30 characters and a name of up to 100 characters.";
        }

        if (unitPrice < 0 || quantity < 0 || reorderLevel < 0)
        {
            return "Price, quantity and reorder level cannot be negative.";
        }

        var existing = id is { } existingId ? await GetProductAsync(existingId, cancellationToken) : null;
        if (id is not null && existing is null)
        {
            return "The product no longer exists.";
        }

        await using var command = DataSource.CreateCommand(id is null
            ? "INSERT INTO inventory.product (product_id, sku, name, unit_price, quantity_on_hand, reorder_level) VALUES (@id, @sku, @name, @price, @qty, @reorder)"
            : "UPDATE inventory.product SET sku = @sku, name = @name, unit_price = @price, quantity_on_hand = @qty, reorder_level = @reorder, updated_at = now() WHERE product_id = @id");
        Add(command, "id", id ?? Guid.CreateVersion7());
        Add(command, "sku", sku);
        Add(command, "name", name);
        Add(command, "price", unitPrice);
        Add(command, "qty", quantity);
        Add(command, "reorder", reorderLevel);
        try
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return "The product no longer exists.";
            }

            var action = id is null ? "InventoryProductCreated" : "InventoryProductUpdated";
            var prior = existing is null ? "" : $" Previously SKU '{existing.Sku}' was named '{existing.Name}'.";
            await audit.RecordAsync(action,
                $"Product '{sku}' ({name}) was {(id is null ? "created" : "updated")}.{prior} Quantity {quantity}, reorder level {reorderLevel}, unit price {unitPrice.ToString("0.00", CultureInfo.InvariantCulture)}.",
                "Inventory Monitor", cancellationToken);
            return null;
        }
        catch (DbException ex) when (ex.SqlState == "23505")
        {
            return $"Another product already uses the SKU {sku}.";
        }
    }

    /// <summary>Adds (positive) or removes (negative) stock. Returns an error message or null.</summary>
    public async Task<string?> AdjustStockAsync(Guid id, int change, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAdministrator)
        {
            return AdministratorOnly;
        }

        var product = await GetProductAsync(id, cancellationToken);
        if (product is null)
        {
            return "The product no longer exists.";
        }

        await using var command = DataSource.CreateCommand(
            "UPDATE inventory.product SET quantity_on_hand = quantity_on_hand + @change, updated_at = now() WHERE product_id = @id AND quantity_on_hand + @change >= 0");
        Add(command, "id", id);
        Add(command, "change", change);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            return "There is not enough stock to remove that quantity.";
        }

        await audit.RecordAsync("InventoryStockAdjusted",
            $"Stock for product '{product.Sku}' was adjusted by {change.ToString("+0;-0;0", CultureInfo.InvariantCulture)} to {product.QuantityOnHand + change}.",
            "Inventory Monitor", cancellationToken);
        return null;
    }

    /// <summary>Deletes a product. Returns an error message or null.</summary>
    public async Task<string?> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAdministrator)
        {
            return AdministratorOnly;
        }

        var product = await GetProductAsync(id, cancellationToken);
        if (product is null)
        {
            return "The product no longer exists.";
        }

        await using var command = DataSource.CreateCommand("DELETE FROM inventory.product WHERE product_id = @id");
        Add(command, "id", id);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            return "The product no longer exists.";
        }

        await audit.RecordAsync("InventoryProductDeleted", $"Product '{product.Sku}' ({product.Name}) was deleted.",
            "Inventory Monitor", cancellationToken);
        return null;
    }

    private async Task<Product?> GetProductAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = DataSource.CreateCommand(
            "SELECT product_id, sku, name, unit_price, quantity_on_hand, reorder_level, updated_at FROM inventory.product WHERE product_id = @id");
        Add(command, "id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new Product(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), reader.GetInt32(4),
                reader.GetInt32(5), reader.GetDateTime(6))
            : null;
    }

    /// <summary>
    /// Writes a report. With an <paramref name="executionId"/>, a second call for the same execution writes nothing
    /// and returns the report already written, so a repeated run has no extra effect (WP-2).
    /// </summary>
    public async Task<StockReport> AddReportAsync(string type, IReadOnlyList<Product> products, object details, Guid? executionId = null,
        CancellationToken cancellationToken = default)
    {
        if (executionId is { } existingRun && await GetReportForExecutionAsync(existingRun, cancellationToken) is { } existing)
        {
            return existing;
        }

        var report = new StockReport(Guid.CreateVersion7(), type, DateTime.UtcNow, products.Count, products.Count(p => p.IsLow),
            products.Sum(p => p.StockValue), JsonSerializer.Serialize(details));
        await using var command = DataSource.CreateCommand(
            "INSERT INTO inventory.stock_report (report_id, report_type, created_at, product_count, low_stock_count, total_value, details, execution_id) " +
            "VALUES (@id, @type, @created, @count, @low, @value, CAST(@details AS jsonb), @execution) ON CONFLICT (execution_id) DO NOTHING");
        Add(command, "id", report.Id);
        Add(command, "type", type);
        Add(command, "created", report.CreatedAtUtc);
        Add(command, "count", report.ProductCount);
        Add(command, "low", report.LowStockCount);
        Add(command, "value", report.TotalValue);
        Add(command, "details", report.Details);
        Add(command, "execution", (object?)executionId ?? DBNull.Value, typeof(Guid));
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0 && executionId is { } run
            && await GetReportForExecutionAsync(run, cancellationToken) is { } concurrent)
        {
            return concurrent;
        }

        return report;
    }

    public async Task<StockReport?> GetReportForExecutionAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        await using var command = DataSource.CreateCommand(
            "SELECT report_id, report_type, created_at, product_count, low_stock_count, total_value, details::text FROM inventory.stock_report " +
            "WHERE execution_id = @execution");
        Add(command, "execution", executionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new StockReport(reader.GetGuid(0), reader.GetString(1), reader.GetDateTime(2), reader.GetInt32(3), reader.GetInt32(4),
                reader.GetDecimal(5), reader.GetString(6))
            : null;
    }

    public async Task<IReadOnlyList<StockReport>> GetReportsAsync(string? type, DateTime sinceUtc, int limit, CancellationToken cancellationToken = default)
    {
        await using var command = DataSource.CreateCommand(
            "SELECT report_id, report_type, created_at, product_count, low_stock_count, total_value, details::text FROM inventory.stock_report " +
            "WHERE (@type IS NULL OR report_type = @type) AND created_at >= @since ORDER BY created_at DESC LIMIT @limit");
        Add(command, "type", (object?)type ?? DBNull.Value, typeof(string));
        Add(command, "since", sinceUtc);
        Add(command, "limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var reports = new List<StockReport>();
        while (await reader.ReadAsync(cancellationToken))
        {
            reports.Add(new StockReport(reader.GetGuid(0), reader.GetString(1), reader.GetDateTime(2), reader.GetInt32(3), reader.GetInt32(4),
                reader.GetDecimal(5), reader.GetString(6)));
        }

        return reports;
    }

    private static void Add(DbCommand command, string name, object value, Type? nullType = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        if (nullType == typeof(string))
        {
            parameter.DbType = System.Data.DbType.String;
        }
        else if (nullType == typeof(Guid))
        {
            parameter.DbType = System.Data.DbType.Guid;
        }

        command.Parameters.Add(parameter);
    }
}
