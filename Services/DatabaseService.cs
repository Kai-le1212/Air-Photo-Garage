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
    /// 按分组维度聚合。返回每个分组值及其照片数量，按指定方式排序。
    /// <paramref name="column"/> 只能是白名单列（aircraft_model / registration_number /
    /// airport_iata / airport_icao / airport_name / airport_code）。
    /// </summary>
    Task<IReadOnlyList<GroupCount>> GetGroupCountsAsync(string column, bool byCountDescending = false);

    /// <summary>按某个精确值筛出该分组下的全部照片（分页）。</summary>
    Task<IReadOnlyList<Photo>> GetPhotosByColumnValueAsync(string column, string value, PhotoSortOrder order);
}

/// <summary>分组统计结果：分组值 + 照片数 + 最早/最晚拍摄时间。</summary>
public sealed record GroupCount(string Value, int Count, DateTimeOffset? FirstShotAt, DateTimeOffset? LastShotAt);

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
        var order = filter.SortOrder switch
        {
            PhotoSortOrder.ShotAtAscending => "shot_at ASC",
            PhotoSortOrder.ImportedAtDescending => "imported_at DESC",
            PhotoSortOrder.AircraftModelAscending => "aircraft_model ASC",
            PhotoSortOrder.RegistrationAscending => "registration_number ASC",
            _ => "shot_at DESC, imported_at DESC",
        };
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

    public async Task<IReadOnlyList<GroupCount>> GetGroupCountsAsync(string column, bool byCountDescending = false)
    {
        EnsureGroupableColumn(column);

        var order = byCountDescending ? "cnt DESC, value ASC" : "value ASC";
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT {column} AS value,
                   COUNT(*) AS cnt,
                   MIN(shot_at) AS first_shot,
                   MAX(shot_at) AS last_shot
            FROM photos
            WHERE {column} IS NOT NULL AND TRIM({column}) <> ''
            GROUP BY {column}
            ORDER BY {order}
            """;

        var list = new List<GroupCount>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var value = reader.GetString(0);
            var cnt = reader.GetInt32(1);
            DateTimeOffset? first = reader.IsDBNull(2)
                ? null
                : DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
            DateTimeOffset? last = reader.IsDBNull(3)
                ? null
                : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture);
            list.Add(new GroupCount(value, cnt, first, last));
        }
        return list;
    }

    public async Task<IReadOnlyList<Photo>> GetPhotosByColumnValueAsync(string column, string value, PhotoSortOrder order)
    {
        EnsureGroupableColumn(column);

        var orderSql = order switch
        {
            PhotoSortOrder.ShotAtAscending => "shot_at ASC",
            PhotoSortOrder.ImportedAtDescending => "imported_at DESC",
            PhotoSortOrder.AircraftModelAscending => "aircraft_model ASC",
            PhotoSortOrder.RegistrationAscending => "registration_number ASC",
            _ => "shot_at DESC, imported_at DESC",
        };

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM photos WHERE {column} = $v ORDER BY {orderSql}";
        cmd.Parameters.AddWithValue("$v", value);

        var list = new List<Photo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(ReadPhoto(reader));
        }
        return list;
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
