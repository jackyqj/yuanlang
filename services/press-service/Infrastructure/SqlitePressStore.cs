namespace Press.Service.Infrastructure;

using Microsoft.Data.Sqlite;
using Press.Judge;
using Press.Service.Application;

public interface IPressStore
{
    Task InitializeAsync(CancellationToken ct);
    Task UpsertRecipeAsync(RuntimeJob job, CancellationToken ct);
    Task SaveRecipeAsync(RecipeDocument recipe, CancellationToken ct);
    Task<IReadOnlyList<RecipeSummary>> ListRecipesAsync(CancellationToken ct);
    Task<RecipeDocument?> GetRecipeAsync(string recipeVersionId, CancellationToken ct);
    Task DeleteRecipeAsync(string recipeVersionId, CancellationToken ct);
    Task<CycleRecord> SaveCycleReturningAsync(CycleRecord record, IReadOnlyList<SamplePoint> samples, CancellationToken ct);
    Task<IReadOnlyList<CycleRecord>> QueryCyclesAsync(CycleQuery query, CancellationToken ct);
    Task<CycleDetail?> GetCycleAsync(string cycleId, CancellationToken ct);
}

public sealed record CycleRecord(
    string CycleId,
    string ProductName,
    string ProductSn,
    string ConnectBar,
    string ConnectName,
    string RecipeVersionId,
    string Result,
    double EndForceN,
    double EndPositionMm,
    double MaxForceN,
    double CycleTimeS,
    string FailCodes,
    string CurvePath,
    string SoftwareVersion,
    string OperatorName,
    DateTimeOffset CreatedAt);

public sealed record CycleDetail(CycleRecord Record, IReadOnlyList<SamplePoint> Samples);

public sealed record CycleQuery(
    string? PcbBarcode = null,
    string? ProductName = null,
    string? ConnectName = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    bool ErrorsOnly = false,
    bool PassOnly = false,
    int Limit = 50);

public sealed class SqlitePressStore : IPressStore
{
    private readonly string _dbPath;
    private readonly string _curveDir;
    private readonly string _connectionString;
    private readonly ILogger<SqlitePressStore> _log;

    public SqlitePressStore(IConfiguration config, IHostEnvironment env, ILogger<SqlitePressStore> log)
    {
        var root = config["Data:Root"] ?? ResolveDataRoot(env.ContentRootPath);
        Directory.CreateDirectory(root);
        _dbPath = Path.Combine(root, "db", "press.db");
        _curveDir = Path.Combine(root, "curves");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        Directory.CreateDirectory(_curveDir);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
        _log = log;
    }

    private static string ResolveDataRoot(string contentRoot)
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(contentRoot, "data"),
                     Path.GetFullPath(Path.Combine(contentRoot, "..", "data")),
                     Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "data")),
                 })
        {
            if (Directory.Exists(candidate) || candidate.EndsWith($"{Path.DirectorySeparatorChar}data"))
            {
                return candidate;
            }
        }
        return Path.Combine(contentRoot, "data");
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS recipe_versions (
              recipe_version_id TEXT PRIMARY KEY,
              product_id TEXT NOT NULL,
              product_name TEXT NOT NULL,
              connector_name TEXT NOT NULL,
              payload_json TEXT NOT NULL,
              published_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS cycles (
              cycle_id TEXT PRIMARY KEY,
              product_name TEXT NOT NULL,
              product_sn TEXT NOT NULL,
              connect_bar TEXT NOT NULL,
              connect_name TEXT NOT NULL,
              recipe_version_id TEXT NOT NULL,
              result TEXT NOT NULL,
              end_force_n REAL NOT NULL,
              end_position_mm REAL NOT NULL,
              max_force_n REAL NOT NULL,
              cycle_time_s REAL NOT NULL,
              fail_codes TEXT NOT NULL,
              curve_path TEXT NOT NULL,
              software_version TEXT NOT NULL,
              operator_name TEXT NOT NULL,
              created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_cycles_created ON cycles(created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_cycles_sn ON cycles(product_sn);
            """;
        await cmd.ExecuteNonQueryAsync(ct);

        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM recipe_versions;";
        var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));
        if (count == 0)
        {
            await SaveRecipeAsync(RecipeDocument.DefaultDemo(), ct);
            _log.LogInformation("Seeded default recipe rv-demo-1");
        }

        _log.LogInformation("SQLite ready at {Path}", _dbPath);
    }

    public async Task UpsertRecipeAsync(RuntimeJob job, CancellationToken ct) =>
        await SaveRecipeAsync(RecipeDocument.FromJob(job), ct);

    public async Task SaveRecipeAsync(RecipeDocument recipe, CancellationToken ct)
    {
        recipe.PublishedAt = DateTimeOffset.UtcNow;
        var payload = System.Text.Json.JsonSerializer.Serialize(recipe, JsonOpts);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO recipe_versions(recipe_version_id, product_id, product_name, connector_name, payload_json, published_at)
            VALUES ($id, $pid, $pname, $cname, $payload, $at)
            ON CONFLICT(recipe_version_id) DO UPDATE SET
              product_id=excluded.product_id,
              product_name=excluded.product_name,
              connector_name=excluded.connector_name,
              payload_json=excluded.payload_json,
              published_at=excluded.published_at;
            """;
        cmd.Parameters.AddWithValue("$id", recipe.RecipeVersionId);
        cmd.Parameters.AddWithValue("$pid", recipe.ProductId);
        cmd.Parameters.AddWithValue("$pname", recipe.ProductName);
        cmd.Parameters.AddWithValue("$cname", recipe.ConnectorName);
        cmd.Parameters.AddWithValue("$payload", payload);
        cmd.Parameters.AddWithValue("$at", recipe.PublishedAt.Value.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<RecipeSummary>> ListRecipesAsync(CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT recipe_version_id, product_id, product_name, connector_name, published_at
            FROM recipe_versions
            ORDER BY published_at DESC;
            """;
        var list = new List<RecipeSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new RecipeSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4))));
        }
        return list;
    }

    public async Task<RecipeDocument?> GetRecipeAsync(string recipeVersionId, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT payload_json, published_at FROM recipe_versions WHERE recipe_version_id=$id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", recipeVersionId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var payload = reader.GetString(0);
        var published = DateTimeOffset.Parse(reader.GetString(1));

        // Support both RecipeDocument and legacy RuntimeJob payloads.
        RecipeDocument? doc = null;
        try
        {
            doc = System.Text.Json.JsonSerializer.Deserialize<RecipeDocument>(payload, JsonOpts);
        }
        catch
        {
            // ignore
        }

        if (doc is null || string.IsNullOrWhiteSpace(doc.RecipeVersionId))
        {
            var job = System.Text.Json.JsonSerializer.Deserialize<RuntimeJob>(payload, JsonOpts);
            if (job is null) return null;
            doc = RecipeDocument.FromJob(job);
        }
        else if (payload.Contains("\"recipe\"", StringComparison.OrdinalIgnoreCase))
        {
            // Legacy RuntimeJob shape may partially bind into RecipeDocument; prefer job mapping.
            var job = System.Text.Json.JsonSerializer.Deserialize<RuntimeJob>(payload, JsonOpts);
            if (job is not null)
                doc = RecipeDocument.FromJob(job);
        }

        doc.PublishedAt = published;
        return doc;
    }

    public async Task DeleteRecipeAsync(string recipeVersionId, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM recipe_versions WHERE recipe_version_id=$id;";
        cmd.Parameters.AddWithValue("$id", recipeVersionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<CycleRecord> SaveCycleReturningAsync(CycleRecord record, IReadOnlyList<SamplePoint> samples, CancellationToken ct)
    {
        var curvePath = Path.Combine(_curveDir, $"{record.CycleId}.json");
        var json = System.Text.Json.JsonSerializer.Serialize(samples, JsonOpts);
        await File.WriteAllTextAsync(curvePath, json, ct);

        var toSave = record with { CurvePath = curvePath };
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO cycles(
              cycle_id, product_name, product_sn, connect_bar, connect_name, recipe_version_id,
              result, end_force_n, end_position_mm, max_force_n, cycle_time_s, fail_codes,
              curve_path, software_version, operator_name, created_at)
            VALUES (
              $id, $pname, $sn, $cbar, $cname, $rid,
              $result, $ef, $ep, $mf, $ct, $codes,
              $curve, $sw, $op, $at);
            """;
        cmd.Parameters.AddWithValue("$id", toSave.CycleId);
        cmd.Parameters.AddWithValue("$pname", toSave.ProductName);
        cmd.Parameters.AddWithValue("$sn", toSave.ProductSn);
        cmd.Parameters.AddWithValue("$cbar", toSave.ConnectBar);
        cmd.Parameters.AddWithValue("$cname", toSave.ConnectName);
        cmd.Parameters.AddWithValue("$rid", toSave.RecipeVersionId);
        cmd.Parameters.AddWithValue("$result", toSave.Result);
        cmd.Parameters.AddWithValue("$ef", toSave.EndForceN);
        cmd.Parameters.AddWithValue("$ep", toSave.EndPositionMm);
        cmd.Parameters.AddWithValue("$mf", toSave.MaxForceN);
        cmd.Parameters.AddWithValue("$ct", toSave.CycleTimeS);
        cmd.Parameters.AddWithValue("$codes", toSave.FailCodes);
        cmd.Parameters.AddWithValue("$curve", toSave.CurvePath);
        cmd.Parameters.AddWithValue("$sw", toSave.SoftwareVersion);
        cmd.Parameters.AddWithValue("$op", toSave.OperatorName);
        cmd.Parameters.AddWithValue("$at", toSave.CreatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
        return toSave;
    }

    public async Task<IReadOnlyList<CycleRecord>> QueryCyclesAsync(CycleQuery query, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.PcbBarcode))
        {
            where.Add("product_sn LIKE $sn");
            cmd.Parameters.AddWithValue("$sn", $"%{query.PcbBarcode}%");
        }
        if (!string.IsNullOrWhiteSpace(query.ProductName))
        {
            where.Add("product_name LIKE $pn");
            cmd.Parameters.AddWithValue("$pn", $"%{query.ProductName}%");
        }
        if (!string.IsNullOrWhiteSpace(query.ConnectName))
        {
            where.Add("connect_name LIKE $cn");
            cmd.Parameters.AddWithValue("$cn", $"%{query.ConnectName}%");
        }
        if (query.From is { } from)
        {
            where.Add("created_at >= $from");
            cmd.Parameters.AddWithValue("$from", from.ToString("O"));
        }
        if (query.To is { } to)
        {
            where.Add("created_at <= $to");
            cmd.Parameters.AddWithValue("$to", to.ToString("O"));
        }
        if (query.ErrorsOnly)
        {
            where.Add("result != 'pass'");
        }
        if (query.PassOnly)
        {
            where.Add("result = 'pass'");
        }
        var sqlWhere = where.Count == 0 ? "" : "WHERE " + string.Join(" AND ", where);
        cmd.CommandText = $"""
            SELECT cycle_id, product_name, product_sn, connect_bar, connect_name, recipe_version_id,
                   result, end_force_n, end_position_mm, max_force_n, cycle_time_s, fail_codes,
                   curve_path, software_version, operator_name, created_at
            FROM cycles
            {sqlWhere}
            ORDER BY created_at DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 2000));

        var list = new List<CycleRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadCycle(reader));
        }
        return list;
    }

    public async Task<CycleDetail?> GetCycleAsync(string cycleId, CancellationToken ct)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT cycle_id, product_name, product_sn, connect_bar, connect_name, recipe_version_id,
                   result, end_force_n, end_position_mm, max_force_n, cycle_time_s, fail_codes,
                   curve_path, software_version, operator_name, created_at
            FROM cycles WHERE cycle_id=$id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", cycleId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var record = ReadCycle(reader);
        IReadOnlyList<SamplePoint> samples = [];
        if (File.Exists(record.CurvePath))
        {
            var text = await File.ReadAllTextAsync(record.CurvePath, ct);
            samples = System.Text.Json.JsonSerializer.Deserialize<List<SamplePoint>>(text, JsonOpts) ?? [];
        }
        return new CycleDetail(record, samples);
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static CycleRecord ReadCycle(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetDouble(7),
        reader.GetDouble(8),
        reader.GetDouble(9),
        reader.GetDouble(10),
        reader.GetString(11),
        reader.GetString(12),
        reader.GetString(13),
        reader.GetString(14),
        DateTimeOffset.Parse(reader.GetString(15)));
}
