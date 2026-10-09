using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using Microsoft.Data.Sqlite;

namespace AirPhotoGarage.Services;

public interface IDatabaseService
{
    /// <summary>打开/创建数据库并初始化 Schema。</summary>
    Task InitializeAsync();

    Task<long> InsertPhotoAsync(Photo photo);
    Task UpdatePhotoAsync(Photo photo);
    Task DeletePhotoAsync(long id);
    Task<Photo?> GetPhotoAsync(long id);

    /// <summary>按筛选条件分页查询照片。</summary>
    Task<IReadOnlyList<Photo>> QueryPhotosAsync(PhotoFilter filter, int skip, int take);

    /// <summary>统计符合条件的总数。</summary>
    Task<int> CountPhotosAsync(PhotoFilter filter);

    /// <summary>读取所有不同的机型/机场/注册号（用于自动补全）。</summary>
    Task<IReadOnlyList<string>> GetDistinctValuesAsync(string column);

    /// <summary>
    /// 按分组维度聚合。返回<b>真实值分布</b> + <b>「未填写」桶</b>
    /// （无未填写记录时 <c>Missing</c> 为 null）。
    /// <paramref name="column"/> 只能是白名单列（aircraft_model / registration_number /
    /// airport_iata / airport_icao / airport_name / airport_code）。
    /// </summary>
    /// <param name="rowLabelColumns">
    /// 可选的附加展示列。每组取 MAX(列) 放进 <see cref="GroupCount.DisplayParts"/>，
    /// 供 UI 拼「名称 · 代码」这类复合标题（机场行要显示「香港国际机场 · HKG」）。
    /// 传 null 表示不需要。
    /// </param>
    Task<ColumnGroupCounts> GetGroupCountsAsync(
        string column, bool byCountDescending = false, IReadOnlyList<string>? rowLabelColumns = null);

    /// <summary>
    /// 在父列匹配约束下，按子列再聚合一层（同样是真实值分布 + 未填写桶）。
    /// 用于「机型 → 注册号」这类二级分组。
    /// </summary>
    Task<ColumnGroupCounts> GetSubGroupCountsAsync(
        string parentColumn, ColumnValueMatch parentMatch, string childColumn);

    /// <summary>按单列匹配取照片。</summary>
    Task<IReadOnlyList<Photo>> GetPhotosByColumnValueAsync(
        string column, ColumnValueMatch match, PhotoSortOrder order);

    /// <summary>双列匹配取照片。用于二级分组的叶子层（机型 + 注册号 → 照片）。</summary>
    Task<IReadOnlyList<Photo>> GetPhotosByTwoColumnValuesAsync(
        string parentColumn, ColumnValueMatch parentMatch,
        string childColumn, ColumnValueMatch childMatch,
        PhotoSortOrder order);

    // ---------- 用户自定义分组（仅在注册号维度下使用） ----------

    /// <summary>读取某架飞机（注册号）下的全部自定义分组，按创建时间升序。</summary>
    Task<IReadOnlyList<PhotoGroup>> GetGroupsAsync(string registrationNumber);

    /// <summary>
    /// 新建分组。同一注册号下同名分组已存在时直接返回既有的那条（幂等），
    /// 调用方无需先查重。
    /// </summary>
    Task<PhotoGroup> CreateGroupAsync(string registrationNumber, string name);

    /// <summary>重命名分组。同注册号下重名会抛 <see cref="InvalidOperationException"/>。</summary>
    Task RenameGroupAsync(long groupId, string newName);

    /// <summary>删除分组（解散）。组内照片不会被删除，只是回到「按天」节点。</summary>
    Task DeleteGroupAsync(long groupId);

    /// <summary>
    /// 把照片指派到分组；<paramref name="groupId"/> 为 null 表示移出分组。
    /// 一张照片最多属于一个分组，重复指派即移动。
    /// </summary>
    Task AssignPhotoToGroupAsync(long photoId, long? groupId);

    /// <summary>
    /// 一次取回某注册号下所有照片的「照片 → 分组」映射，
    /// 供时间轴构建时做分区，避免逐张查询。
    /// </summary>
    Task<IReadOnlyDictionary<long, long>> GetPhotoGroupMapAsync(string registrationNumber);
}

/// <summary>分组统计结果：分组值 + 照片数 + 最早/最晚拍摄时间。</summary>
/// <param name="Value">分组键的原始值（如 ICAO 码 "VHHH"）。<b>查询时必须用这个值</b>。</param>
/// <param name="DisplayParts">
/// 附加展示字段（如机场维度的 [机场名, IATA]），供调用方拼「名称 · 代码」这类复合标题。
/// 为 null 表示该维度不需要附加字段，直接用 <paramref name="Value"/> 作为标题。
/// </param>
public sealed record GroupCount(
    string Value, int Count, DateTimeOffset? FirstShotAt, DateTimeOffset? LastShotAt,
    IReadOnlyList<string?>? DisplayParts = null);

/// <summary>
/// 分组/筛选时对某一列取值的匹配方式：要么「等于某个真实值」，要么「未填写桶」。
///
/// <para>
/// 为什么要抽这个类型：分组维度里除了真实值，还有一类<b>「未填写」桶</b>
/// （列为 NULL 或纯空白）。若把两种匹配各写一套查询方法，
/// 父列 × 子列 × 是否未填写的组合会膨胀成 6 个近重复方法；
/// 抽成一个类型后，2 个查询方法即可覆盖全部场景。
/// </para>
/// </summary>
public readonly record struct ColumnValueMatch
{
    private ColumnValueMatch(string? value, bool isMissing)
    {
        Value = value;
        IsMissing = isMissing;
    }

    /// <summary>要匹配的具体值。仅当 <see cref="IsMissing"/> 为 false 时有意义。</summary>
    public string? Value { get; }

    /// <summary>是否匹配「未填写」桶（列为 NULL 或纯空白）。</summary>
    public bool IsMissing { get; }

    /// <summary>匹配某个具体值。</summary>
    public static ColumnValueMatch Of(string value) => new(value, false);

    /// <summary>匹配「未填写」桶。</summary>
    public static ColumnValueMatch Missing { get; } = new(null, true);
}

/// <summary>
/// 某一列的分组统计结果：真实值分布 + 可选的「未填写」桶。
/// <see cref="Missing"/> 为 null 表示该层没有未填写的记录。
/// </summary>
/// <param name="Values">各真实分组值及其统计，已按调用方指定的方式排序。</param>
/// <param name="Missing">「未填写」桶的统计（<c>Value</c> 为占位空串）。</param>
public sealed record ColumnGroupCounts(IReadOnlyList<GroupCount> Values, GroupCount? Missing);

/// <summary>
/// SQLite 数据访问层。所有 SQL 集中在本类，避免散落各处。
/// 连接字符串使用共享缓存模式，提升并发读性能。
/// </summary>
public sealed class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SchemaSql;
        await cmd.ExecuteNonQueryAsync();

        // 旧库迁移：依次尝试 ADD COLUMN，列已存在则忽略异常
        foreach (var migration in MigrationSqls)
        {
            await using var mig = conn.CreateCommand();
            mig.CommandText = migration;
            try { await mig.ExecuteNonQueryAsync(); }
            catch { /* column already exists */ }
        }
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS photos (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            file_path TEXT NOT NULL,
            thumbnail_path TEXT,
            file_size INTEGER NOT NULL DEFAULT 0,
            shot_at TEXT,
            aircraft_model TEXT,
            registration_number TEXT,
            airport_code TEXT,
            airport_iata TEXT,
            airport_icao TEXT,
            airport_name TEXT,
            notes TEXT,
            camera_make TEXT,
            camera_model TEXT,
            lens_model TEXT,
            focal_length REAL,
            aperture TEXT,
            shutter_speed TEXT,
            iso INTEGER,
            latitude REAL,
            longitude REAL,
            exif_json TEXT,
            recognition_status INTEGER NOT NULL DEFAULT 0,
            recognized_aircraft_model TEXT,
            recognition_confidence REAL,
            imported_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_photos_shot_at ON photos(shot_at);
        CREATE INDEX IF NOT EXISTS idx_photos_aircraft_model ON photos(aircraft_model);
        CREATE INDEX IF NOT EXISTS idx_photos_registration ON photos(registration_number);
        CREATE INDEX IF NOT EXISTS idx_photos_airport_code ON photos(airport_code);
        CREATE INDEX IF NOT EXISTS idx_photos_airport_iata ON photos(airport_iata);
        CREATE INDEX IF NOT EXISTS idx_photos_airport_icao ON photos(airport_icao);
        CREATE INDEX IF NOT EXISTS idx_photos_imported_at ON photos(imported_at);

        CREATE TABLE IF NOT EXISTS app_meta (
            key TEXT PRIMARY KEY,
            value TEXT
        );

        -- 用户自定义分组：隶属于某个注册号（同一架飞机）
        CREATE TABLE IF NOT EXISTS photo_groups (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            registration_number TEXT NOT NULL,
            name TEXT NOT NULL,
            created_at TEXT NOT NULL
        );

        -- 同一注册号下不允许出现同名分组
        CREATE UNIQUE INDEX IF NOT EXISTS idx_photo_groups_reg_name
            ON photo_groups(registration_number, name);

        -- 成员表：photo_id 作主键 ⇒ 一张照片最多归属一个分组（重复指派即移动）
        CREATE TABLE IF NOT EXISTS photo_group_members (
            photo_id INTEGER PRIMARY KEY,
            group_id INTEGER NOT NULL,
            added_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_photo_group_members_group
            ON photo_group_members(group_id);
        """;

    /// <summary>
    /// 向已存在但缺少新列的旧库做就地迁移。SQLite 不支持 IF NOT EXISTS for ADD COLUMN，
    /// 因此先尝试 ADD，列已存在时报错就忽略。
    /// </summary>
    private static readonly string[] MigrationSqls =
    {
        "ALTER TABLE photos ADD COLUMN airport_iata TEXT",
        "ALTER TABLE photos ADD COLUMN airport_icao TEXT",
        "CREATE INDEX IF NOT EXISTS idx_photos_airport_iata ON photos(airport_iata)",
        "CREATE INDEX IF NOT EXISTS idx_photos_airport_icao ON photos(airport_icao)",
    };

    public async Task<long> InsertPhotoAsync(Photo photo)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = InsertSql;
        BindPhotoParameters(cmd, photo);
        var id = (long)(await cmd.ExecuteScalarAsync())!;
        photo.Id = id;
        return id;
    }

    public async Task UpdatePhotoAsync(Photo photo)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = UpdateSql;
        cmd.Parameters.AddWithValue("$id", photo.Id);
        BindPhotoParameters(cmd, photo);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeletePhotoAsync(long id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // 一并清掉分组归属：成员表以 photo_id 为主键但没有建 FK，
        // 不手动清理会留下指向已删除照片的孤儿行。
        await using (var m = conn.CreateCommand())
        {
            m.CommandText = "DELETE FROM photo_group_members WHERE photo_id = $id";
            m.Parameters.AddWithValue("$id", id);
            await m.ExecuteNonQueryAsync();
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM photos WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Photo?> GetPhotoAsync(long id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM photos WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return ReadPhoto(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<Photo>> QueryPhotosAsync(PhotoFilter filter, int skip, int take)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var (where, parameters) = BuildWhereClause(filter);
        var order = BuildOrderSql(filter.SortOrder);
        cmd.CommandText = $"SELECT * FROM photos {where} ORDER BY {order} LIMIT $limit OFFSET $offset";
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }
        cmd.Parameters.AddWithValue("$limit", take);
        cmd.Parameters.AddWithValue("$offset", skip);
        var list = new List<Photo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(ReadPhoto(reader));
        }
        return list;
    }

    public async Task<int> CountPhotosAsync(PhotoFilter filter)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var (where, parameters) = BuildWhereClause(filter);
        cmd.CommandText = $"SELECT COUNT(*) FROM photos {where}";
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(p);
        }
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<string>> GetDistinctValuesAsync(string column)
    {
        // 仅允许白名单列名以防 SQL 注入
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "aircraft_model", "registration_number", "airport_code", "airport_name", "camera_model"
        };
        if (!allowed.Contains(column))
        {
            throw new ArgumentException($"Column not allowed: {column}");
        }

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT DISTINCT {column} FROM photos WHERE {column} IS NOT NULL AND TRIM({column}) <> '' ORDER BY {column}";
        var list = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var v = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(v)) list.Add(v);
        }
        return list;
    }

    // ---------- helpers ----------

    /// <summary>分组/筛选允许的列白名单，防 SQL 注入。</summary>
    private static readonly HashSet<string> GroupableColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "aircraft_model", "registration_number",
        "airport_iata", "airport_icao", "airport_name", "airport_code",
    };

    private static void EnsureGroupableColumn(string column)
    {
        if (!GroupableColumns.Contains(column))
        {
            throw new ArgumentException($"Column not allowed for grouping: {column}");
        }
    }

    public async Task<ColumnGroupCounts> GetGroupCountsAsync(
        string column, bool byCountDescending = false, IReadOnlyList<string>? rowLabelColumns = null)
    {
        EnsureGroupableColumn(column);
        if (rowLabelColumns is not null)
        {
            foreach (var c in rowLabelColumns) EnsureGroupableColumn(c);
        }

        var labelCount = rowLabelColumns?.Count ?? 0;
        var labelSelect = labelCount == 0
            ? string.Empty
            : ", " + string.Join(", ", rowLabelColumns!.Select((c, i) => $"MAX({c}) AS label{i}"));

        var order = byCountDescending ? "cnt DESC, value ASC" : "value ASC";
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {column} AS value,
                   COUNT(*) AS cnt,
                   MIN(shot_at) AS first_shot,
                   MAX(shot_at) AS last_shot{labelSelect}
            FROM photos
            WHERE {column} IS NOT NULL AND TRIM({column}) <> ''
            GROUP BY {column}
            ORDER BY {order}
            """;
        var values = await ReadGroupCountsAsync(cmd, labelCount);
        var missing = await ReadMissingCountAsync(conn, MissingCondition(column));
        return new ColumnGroupCounts(values, missing);
    }

    public async Task<IReadOnlyList<Photo>> GetPhotosByColumnValueAsync(
        string column, ColumnValueMatch match, PhotoSortOrder order)
    {
        EnsureGroupableColumn(column);

        var orderSql = BuildOrderSql(order);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var cond = BuildMatchCondition(cmd, column, match, "$v");
        cmd.CommandText = $"SELECT * FROM photos WHERE {cond} ORDER BY {orderSql}";

        var list = new List<Photo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(ReadPhoto(reader));
        }
        return list;
    }

    // ---------- 二级分组：父列约束下的子列聚合 ----------

    public async Task<ColumnGroupCounts> GetSubGroupCountsAsync(
        string parentColumn, ColumnValueMatch parentMatch, string childColumn)
    {
        EnsureGroupableColumn(parentColumn);
        EnsureGroupableColumn(childColumn);
        if (string.Equals(parentColumn, childColumn, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("父列与子列不能相同", nameof(childColumn));
        }

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        var parentCond = BuildMatchCondition(cmd, parentColumn, parentMatch, "$parent");
        cmd.CommandText = $"""
            SELECT {childColumn} AS value,
                   COUNT(*) AS cnt,
                   MIN(shot_at) AS first_shot,
                   MAX(shot_at) AS last_shot
            FROM photos
            WHERE {parentCond}
              AND {childColumn} IS NOT NULL AND TRIM({childColumn}) <> ''
            GROUP BY {childColumn}
            ORDER BY cnt DESC, value ASC
            """;
        var values = await ReadGroupCountsAsync(cmd);

        var missing = await ReadMissingCountAsync(
            conn, $"{parentCond} AND {MissingCondition(childColumn)}", cmd);

        return new ColumnGroupCounts(values, missing);
    }

    public async Task<IReadOnlyList<Photo>> GetPhotosByTwoColumnValuesAsync(
        string parentColumn, ColumnValueMatch parentMatch,
        string childColumn, ColumnValueMatch childMatch,
        PhotoSortOrder order)
    {
        EnsureGroupableColumn(parentColumn);
        EnsureGroupableColumn(childColumn);

        var orderSql = BuildOrderSql(order);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        var parentCond = BuildMatchCondition(cmd, parentColumn, parentMatch, "$p");
        var childCond = BuildMatchCondition(cmd, childColumn, childMatch, "$c");
        cmd.CommandText =
            $"SELECT * FROM photos WHERE {parentCond} AND {childCond} ORDER BY {orderSql}";

        var list = new List<Photo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(ReadPhoto(reader));
        }
        return list;
    }

    // ---------- SQL 片段与读取辅助 ----------

    /// <summary>把排序枚举翻成 ORDER BY 片段（多处共用，避免各写一遍写歪）。</summary>
    private static string BuildOrderSql(PhotoSortOrder order) => order switch
    {
        PhotoSortOrder.ShotAtAscending => "shot_at ASC",
        PhotoSortOrder.ImportedAtDescending => "imported_at DESC",
        PhotoSortOrder.AircraftModelAscending => "aircraft_model ASC",
        PhotoSortOrder.RegistrationAscending => "registration_number ASC",
        _ => "shot_at DESC, imported_at DESC",
    };

    /// <summary>「未填写」的条件片段（列为 NULL 或纯空白都算）。</summary>
    private static string MissingCondition(string column) =>
        $"({column} IS NULL OR TRIM({column}) = '')";

    /// <summary>
    /// 把匹配方式翻成 SQL 条件片段；按值匹配时顺带把参数挂到命令上。
    ///
    /// <para>
    /// 列名来自白名单（调用方已 <see cref="EnsureGroupableColumn"/> 校验），
    /// 值一律走参数占位符，不参与拼接。
    /// </para>
    /// </summary>
    private static string BuildMatchCondition(
        SqliteCommand cmd, string column, ColumnValueMatch match, string paramName)
    {
        if (match.IsMissing) return MissingCondition(column);

        cmd.Parameters.AddWithValue(paramName, match.Value ?? string.Empty);
        return $"{column} = {paramName}";
    }

    /// <summary>
    /// 读「值 + 数量 + 最早/最晚拍摄时间 [+ 附加展示列]」的结果集。
    /// </summary>
    /// <param name="labelCount">结果集尾部的附加展示列数量（0 表示没有）。</param>
    private static async Task<List<GroupCount>> ReadGroupCountsAsync(SqliteCommand cmd, int labelCount = 0)
    {
        var list = new List<GroupCount>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            IReadOnlyList<string?>? parts = null;
            if (labelCount > 0)
            {
                var buf = new string?[labelCount];
                for (var i = 0; i < labelCount; i++)
                {
                    var idx = 4 + i;
                    buf[i] = reader.IsDBNull(idx) ? null : reader.GetString(idx);
                }
                parts = buf;
            }

            list.Add(new GroupCount(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                parts));
        }
        return list;
    }

    /// <summary>
    /// 读「未填写」桶的统计。
    /// <paramref name="reuse"/> 传入已绑定父参数的命令时，会复制其参数，
    /// 避免为同一组参数重新拼一遍（父条件里可能含参数占位符）。
    /// 无记录时返回 null。
    /// </summary>
    private static async Task<GroupCount?> ReadMissingCountAsync(
        SqliteConnection conn, string condition, SqliteCommand? reuse = null)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*), MIN(shot_at), MAX(shot_at) FROM photos WHERE {condition}";
        if (reuse is not null)
        {
            foreach (SqliteParameter p in reuse.Parameters)
            {
                cmd.Parameters.AddWithValue(p.ParameterName, p.Value ?? DBNull.Value);
            }
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var count = reader.GetInt32(0);
        if (count == 0) return null;

        // Value 用空串占位：这一桶没有真实值，展示文案由 ViewModel 决定
        return new GroupCount(
            string.Empty,
            count,
            reader.IsDBNull(1) ? null : DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
            reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture));
    }

    // ---------- 用户自定义分组 ----------

    public async Task<IReadOnlyList<PhotoGroup>> GetGroupsAsync(string registrationNumber)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, registration_number, name, created_at
            FROM photo_groups
            WHERE registration_number = $reg
            ORDER BY created_at ASC, id ASC
            """;
        cmd.Parameters.AddWithValue("$reg", registrationNumber);

        var list = new List<PhotoGroup>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PhotoGroup
            {
                Id = reader.GetInt64(0),
                RegistrationNumber = reader.GetString(1),
                Name = reader.GetString(2),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
            });
        }
        return list;
    }

    public async Task<PhotoGroup> CreateGroupAsync(string registrationNumber, string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("分组名不能为空", nameof(name));
        }
        if (string.IsNullOrWhiteSpace(registrationNumber))
        {
            throw new ArgumentException("注册号不能为空", nameof(registrationNumber));
        }

        var reg = registrationNumber.Trim();

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // 幂等：同注册号下同名分组已存在时直接复用，避免调用方还要先查重
        await using (var find = conn.CreateCommand())
        {
            find.CommandText =
                "SELECT id, created_at FROM photo_groups WHERE registration_number = $reg AND name = $name";
            find.Parameters.AddWithValue("$reg", reg);
            find.Parameters.AddWithValue("$name", trimmed);
            await using var r = await find.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                return new PhotoGroup
                {
                    Id = r.GetInt64(0),
                    RegistrationNumber = reg,
                    Name = trimmed,
                    CreatedAt = DateTimeOffset.Parse(r.GetString(1), CultureInfo.InvariantCulture),
                };
            }
        }

        var now = DateTimeOffset.Now;
        await using (var insert = conn.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO photo_groups (registration_number, name, created_at)
                VALUES ($reg, $name, $created)
                """;
            insert.Parameters.AddWithValue("$reg", reg);
            insert.Parameters.AddWithValue("$name", trimmed);
            insert.Parameters.AddWithValue("$created", now.ToString("o", CultureInfo.InvariantCulture));
            await insert.ExecuteNonQueryAsync();
        }

        // last_insert_rowid() 是连接级函数，必须在同一连接上取
        await using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var id = Convert.ToInt64(await idCmd.ExecuteScalarAsync());

        return new PhotoGroup { Id = id, RegistrationNumber = reg, Name = trimmed, CreatedAt = now };
    }

    public async Task RenameGroupAsync(long groupId, string newName)
    {
        var trimmed = (newName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("分组名不能为空", nameof(newName));
        }

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE photo_groups SET name = $name WHERE id = $id";
        cmd.Parameters.AddWithValue("$name", trimmed);
        cmd.Parameters.AddWithValue("$id", groupId);
        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
        {
            throw new InvalidOperationException($"该注册号下已存在名为「{trimmed}」的分组", ex);
        }
    }

    public async Task DeleteGroupAsync(long groupId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // 先删成员再删分组：不依赖 PRAGMA foreign_keys 是否开启，行为稳定
        await using (var m = conn.CreateCommand())
        {
            m.CommandText = "DELETE FROM photo_group_members WHERE group_id = $id";
            m.Parameters.AddWithValue("$id", groupId);
            await m.ExecuteNonQueryAsync();
        }
        await using (var g = conn.CreateCommand())
        {
            g.CommandText = "DELETE FROM photo_groups WHERE id = $id";
            g.Parameters.AddWithValue("$id", groupId);
            await g.ExecuteNonQueryAsync();
        }
    }

    public async Task AssignPhotoToGroupAsync(long photoId, long? groupId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        if (groupId is null)
        {
            await using var del = conn.CreateCommand();
            del.CommandText = "DELETE FROM photo_group_members WHERE photo_id = $p";
            del.Parameters.AddWithValue("$p", photoId);
            await del.ExecuteNonQueryAsync();
            return;
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO photo_group_members (photo_id, group_id, added_at)
            VALUES ($p, $g, $added)
            ON CONFLICT(photo_id) DO UPDATE
                SET group_id = excluded.group_id, added_at = excluded.added_at
            """;
        cmd.Parameters.AddWithValue("$p", photoId);
        cmd.Parameters.AddWithValue("$g", groupId.Value);
        cmd.Parameters.AddWithValue("$added", DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyDictionary<long, long>> GetPhotoGroupMapAsync(string registrationNumber)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT m.photo_id, m.group_id
            FROM photo_group_members m
            JOIN photos p ON p.id = m.photo_id
            WHERE p.registration_number = $reg
            """;
        cmd.Parameters.AddWithValue("$reg", registrationNumber);

        var map = new Dictionary<long, long>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            map[reader.GetInt64(0)] = reader.GetInt64(1);
        }
        return map;
    }

    private const string InsertSql = """
        INSERT INTO photos (
            file_path, thumbnail_path, file_size, shot_at,
            aircraft_model, registration_number, airport_code, airport_iata, airport_icao, airport_name, notes,
            camera_make, camera_model, lens_model, focal_length, aperture, shutter_speed, iso,
            latitude, longitude, exif_json,
            recognition_status, recognized_aircraft_model, recognition_confidence,
            imported_at
        ) VALUES (
            $file_path, $thumbnail_path, $file_size, $shot_at,
            $aircraft_model, $registration_number, $airport_code, $airport_iata, $airport_icao, $airport_name, $notes,
            $camera_make, $camera_model, $lens_model, $focal_length, $aperture, $shutter_speed, $iso,
            $latitude, $longitude, $exif_json,
            $recognition_status, $recognized_aircraft_model, $recognition_confidence,
            $imported_at
        );
        SELECT last_insert_rowid();
        """;

    private const string UpdateSql = """
        UPDATE photos SET
            file_path=$file_path, thumbnail_path=$thumbnail_path, file_size=$file_size,
            shot_at=$shot_at,
            aircraft_model=$aircraft_model, registration_number=$registration_number,
            airport_code=$airport_code, airport_iata=$airport_iata, airport_icao=$airport_icao,
            airport_name=$airport_name, notes=$notes,
            camera_make=$camera_make, camera_model=$camera_model, lens_model=$lens_model,
            focal_length=$focal_length, aperture=$aperture, shutter_speed=$shutter_speed, iso=$iso,
            latitude=$latitude, longitude=$longitude, exif_json=$exif_json,
            recognition_status=$recognition_status,
            recognized_aircraft_model=$recognized_aircraft_model,
            recognition_confidence=$recognition_confidence,
            imported_at=$imported_at
        WHERE id=$id
        """;

    private static void BindPhotoParameters(SqliteCommand cmd, Photo photo)
    {
        cmd.Parameters.AddWithValue("$file_path", photo.FilePath);
        cmd.Parameters.AddWithValue("$thumbnail_path", (object?)photo.ThumbnailPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$file_size", photo.FileSize);
        cmd.Parameters.AddWithValue("$shot_at", photo.ShotAt.HasValue ? photo.ShotAt.Value.ToString("o", CultureInfo.InvariantCulture) : DBNull.Value);
        cmd.Parameters.AddWithValue("$aircraft_model", (object?)photo.AircraftModel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$registration_number", (object?)photo.RegistrationNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$airport_code", (object?)photo.AirportCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$airport_iata", (object?)photo.AirportIata ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$airport_icao", (object?)photo.AirportIcao ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$airport_name", (object?)photo.AirportName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$notes", (object?)photo.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$camera_make", (object?)photo.CameraMake ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$camera_model", (object?)photo.CameraModel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lens_model", (object?)photo.LensModel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$focal_length", (object?)photo.FocalLength ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$aperture", (object?)photo.Aperture ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$shutter_speed", (object?)photo.ShutterSpeed ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$iso", (object?)photo.Iso ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$latitude", (object?)photo.Latitude ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$longitude", (object?)photo.Longitude ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$exif_json", (object?)photo.ExifJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$recognition_status", photo.RecognitionStatus);
        cmd.Parameters.AddWithValue("$recognized_aircraft_model", (object?)photo.RecognizedAircraftModel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$recognition_confidence", (object?)photo.RecognitionConfidence ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$imported_at", photo.ImportedAt.ToString("o", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 兼容旧库：可能不存在列名。读取时若列不存在则返回 null。
    /// </summary>
    private static string? SafeGetString(SqliteDataReader r, string column)
    {
        try
        {
            var idx = r.GetOrdinal(column);
            return r.IsDBNull(idx) ? null : r.GetString(idx);
        }
        catch
        {
            return null;
        }
    }

    private static Photo ReadPhoto(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        FilePath = r.GetString(r.GetOrdinal("file_path")),
        ThumbnailPath = r.IsDBNull(r.GetOrdinal("thumbnail_path")) ? null : r.GetString(r.GetOrdinal("thumbnail_path")),
        FileSize = r.GetInt64(r.GetOrdinal("file_size")),
        ShotAt = r.IsDBNull(r.GetOrdinal("shot_at")) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal("shot_at")), CultureInfo.InvariantCulture),
        AircraftModel = r.IsDBNull(r.GetOrdinal("aircraft_model")) ? null : r.GetString(r.GetOrdinal("aircraft_model")),
        RegistrationNumber = r.IsDBNull(r.GetOrdinal("registration_number")) ? null : r.GetString(r.GetOrdinal("registration_number")),
        AirportCode = r.IsDBNull(r.GetOrdinal("airport_code")) ? null : r.GetString(r.GetOrdinal("airport_code")),
        AirportIata = SafeGetString(r, "airport_iata"),
        AirportIcao = SafeGetString(r, "airport_icao"),
        AirportName = r.IsDBNull(r.GetOrdinal("airport_name")) ? null : r.GetString(r.GetOrdinal("airport_name")),
        Notes = r.IsDBNull(r.GetOrdinal("notes")) ? null : r.GetString(r.GetOrdinal("notes")),
        CameraMake = r.IsDBNull(r.GetOrdinal("camera_make")) ? null : r.GetString(r.GetOrdinal("camera_make")),
        CameraModel = r.IsDBNull(r.GetOrdinal("camera_model")) ? null : r.GetString(r.GetOrdinal("camera_model")),
        LensModel = r.IsDBNull(r.GetOrdinal("lens_model")) ? null : r.GetString(r.GetOrdinal("lens_model")),
        FocalLength = r.IsDBNull(r.GetOrdinal("focal_length")) ? null : r.GetDouble(r.GetOrdinal("focal_length")),
        Aperture = r.IsDBNull(r.GetOrdinal("aperture")) ? null : r.GetString(r.GetOrdinal("aperture")),
        ShutterSpeed = r.IsDBNull(r.GetOrdinal("shutter_speed")) ? null : r.GetString(r.GetOrdinal("shutter_speed")),
        Iso = r.IsDBNull(r.GetOrdinal("iso")) ? null : r.GetInt32(r.GetOrdinal("iso")),
        Latitude = r.IsDBNull(r.GetOrdinal("latitude")) ? null : r.GetDouble(r.GetOrdinal("latitude")),
        Longitude = r.IsDBNull(r.GetOrdinal("longitude")) ? null : r.GetDouble(r.GetOrdinal("longitude")),
        ExifJson = r.IsDBNull(r.GetOrdinal("exif_json")) ? null : r.GetString(r.GetOrdinal("exif_json")),
        RecognitionStatus = r.GetInt32(r.GetOrdinal("recognition_status")),
        RecognizedAircraftModel = r.IsDBNull(r.GetOrdinal("recognized_aircraft_model")) ? null : r.GetString(r.GetOrdinal("recognized_aircraft_model")),
        RecognitionConfidence = r.IsDBNull(r.GetOrdinal("recognition_confidence")) ? null : r.GetDouble(r.GetOrdinal("recognition_confidence")),
        ImportedAt = DateTimeOffset.Parse(r.GetString(r.GetOrdinal("imported_at")), CultureInfo.InvariantCulture),
    };

    private static (string where, List<SqliteParameter> parameters) BuildWhereClause(PhotoFilter f)
    {
        var sb = new StringBuilder();
        var ps = new List<SqliteParameter>();
        bool first = true;
        void And(string condition, Action addParams)
        {
            if (first) { sb.Append("WHERE "); first = false; }
            else { sb.Append(" AND "); }
            sb.Append(condition);
            addParams();
        }
        if (!string.IsNullOrWhiteSpace(f.Keyword))
        {
            And("(aircraft_model LIKE $kw OR registration_number LIKE $kw OR airport_code LIKE $kw OR airport_name LIKE $kw OR notes LIKE $kw)", () =>
            {
                ps.Add(new SqliteParameter("$kw", $"%{f.Keyword.Trim()}%"));
            });
        }
        if (!string.IsNullOrWhiteSpace(f.AircraftModel))
        {
            And("aircraft_model = $am", () => ps.Add(new SqliteParameter("$am", f.AircraftModel.Trim())));
        }
        if (!string.IsNullOrWhiteSpace(f.RegistrationNumber))
        {
            And("registration_number = $reg", () => ps.Add(new SqliteParameter("$reg", f.RegistrationNumber.Trim())));
        }
        if (!string.IsNullOrWhiteSpace(f.AirportCode))
        {
            And("airport_code = $ap", () => ps.Add(new SqliteParameter("$ap", f.AirportCode.Trim())));
        }
        if (f.ShotFrom.HasValue)
        {
            And("shot_at >= $sf", () => ps.Add(new SqliteParameter("$sf", f.ShotFrom.Value.ToString("o", CultureInfo.InvariantCulture))));
        }
        if (f.ShotTo.HasValue)
        {
            And("shot_at <= $st", () => ps.Add(new SqliteParameter("$st", f.ShotTo.Value.ToString("o", CultureInfo.InvariantCulture))));
        }
        return (sb.ToString(), ps);
    }
}
