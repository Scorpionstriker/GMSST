using System.Data.Common;
using System.Globalization;
using System.Text.Json;

namespace DemoApps.InventoryMonitor.Data;

public sealed record Product(Guid Id, string Sku, string Name, decimal UnitPrice, int QuantityOnHand, int ReorderLevel, DateTime UpdatedAtUtc)
{
    public bool IsLow => QuantityOnHand <= ReorderLevel;
    public decimal StockValue => UnitPrice * QuantityOnHand;
}

public sealed record StockReport(Guid Id, string ReportType, DateTime CreatedAtUtc, int ProductCount, int LowStockCount, decimal TotalValue, string Details);

public sealed record LowStockLine(string Sku, string Name, int QuantityOnHand, int ReorderLevel);

/// <summary>
/// The module's own data in the "demo" schema of the scheduler's database (FR-78). It uses the connection pool the
/// host registers as <see cref="DbDataSource"/>, so the module needs no reference to the scheduler's data layer.
/// All SQL is parameterised (Phase 4, Section 8.4).
/// </summary>
public sealed class InventoryStore(DbDataSource dataSource)
{
    public const string ReportStock = "StockReport";
    public const string ReportLowStock = "LowStock";
    public const string ReportWeekly = "WeeklySummary";

    // Idempotent schema script: the module's equivalent of a migration, applied at start-up.
    private const string SchemaSql =
        """
        CREATE SCHEMA IF NOT EXISTS demo;
        CREATE TABLE IF NOT EXISTS demo.product (
            product_id UUID PRIMARY KEY,
            sku VARCHAR(30) NOT NULL,
            name VARCHAR(100) NOT NULL,
            unit_price NUMERIC(10,2) NOT NULL CHECK (unit_price >= 0),
            quantity_on_hand INTEGER NOT NULL CHECK (quantity_on_hand >= 0),
            reorder_level INTEGER NOT NULL CHECK (reorder_level >= 0),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT now());
        CREATE UNIQUE INDEX IF NOT EXISTS ux_product_sku ON demo.product (lower(sku));
        CREATE TABLE IF NOT EXISTS demo.stock_report (
            report_id UUID PRIMARY KEY,
            report_type VARCHAR(20) NOT NULL CHECK (report_type IN ('StockReport', 'LowStock', 'WeeklySummary')),
            created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
            product_count INTEGER NOT NULL,
            low_stock_count INTEGER NOT NULL,
            total_value NUMERIC(14,2) NOT NULL,
            details JSONB NOT NULL DEFAULT '{}');
        CREATE INDEX IF NOT EXISTS ix_stock_report_time ON demo.stock_report (report_type, created_at DESC);
        """;

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(SchemaSql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Adds sample products the first time, so the demonstration has data to work with.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using (var count = dataSource.CreateCommand("SELECT count(*) FROM demo.product"))
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
            await SaveProductAsync(null, sample.Sku, sample.Name, sample.Price, sample.Quantity, sample.Reorder, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<Product>> GetProductsAsync(CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT product_id, sku, name, unit_price, quantity_on_hand, reorder_level, updated_at FROM demo.product ORDER BY name");
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

        await using var command = dataSource.CreateCommand(id is null
            ? "INSERT INTO demo.product (product_id, sku, name, unit_price, quantity_on_hand, reorder_level) VALUES (@id, @sku, @name, @price, @qty, @reorder)"
            : "UPDATE demo.product SET sku = @sku, name = @name, unit_price = @price, quantity_on_hand = @qty, reorder_level = @reorder, updated_at = now() WHERE product_id = @id");
        Add(command, "id", id ?? Guid.CreateVersion7());
        Add(command, "sku", sku);
        Add(command, "name", name);
        Add(command, "price", unitPrice);
        Add(command, "qty", quantity);
        Add(command, "reorder", reorderLevel);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
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
        await using var command = dataSource.CreateCommand(
            "UPDATE demo.product SET quantity_on_hand = quantity_on_hand + @change, updated_at = now() WHERE product_id = @id AND quantity_on_hand + @change >= 0");
        Add(command, "id", id);
        Add(command, "change", change);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1 ? null : "There is not enough stock to remove that quantity.";
    }

    public async Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand("DELETE FROM demo.product WHERE product_id = @id");
        Add(command, "id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StockReport> AddReportAsync(string type, IReadOnlyList<Product> products, object details, CancellationToken cancellationToken = default)
    {
        var report = new StockReport(Guid.CreateVersion7(), type, DateTime.UtcNow, products.Count, products.Count(p => p.IsLow),
            products.Sum(p => p.StockValue), JsonSerializer.Serialize(details));
        await using var command = dataSource.CreateCommand(
            "INSERT INTO demo.stock_report (report_id, report_type, created_at, product_count, low_stock_count, total_value, details) " +
            "VALUES (@id, @type, @created, @count, @low, @value, CAST(@details AS jsonb))");
        Add(command, "id", report.Id);
        Add(command, "type", type);
        Add(command, "created", report.CreatedAtUtc);
        Add(command, "count", report.ProductCount);
        Add(command, "low", report.LowStockCount);
        Add(command, "value", report.TotalValue);
        Add(command, "details", report.Details);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return report;
    }

    public async Task<IReadOnlyList<StockReport>> GetReportsAsync(string? type, DateTime sinceUtc, int limit, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT report_id, report_type, created_at, product_count, low_stock_count, total_value, details::text FROM demo.stock_report " +
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

        command.Parameters.Add(parameter);
    }
}
