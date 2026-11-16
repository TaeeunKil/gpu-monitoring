using Microsoft.Data.Sqlite;
using System.Globalization;
namespace GpuMonitoring.Core;
public sealed record SampleRow(string Time, string Server, string Gpu, double? UsedMiB, double? TotalMiB, double? Compute, string Source);
public sealed record AlarmRow(string Time, string Server, string Gpu, string Kind, string Message, string Source);
public sealed class HistoryStore : IDisposable
{
    readonly SqliteConnection connection;
    readonly int retention;
    public HistoryStore(string path, int retention = 5000)
    {
        this.retention = Math.Clamp(retention, 1, 50000);
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder is not null)
            Directory.CreateDirectory(folder);
        connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS snapshots(source TEXT, stamp TEXT, PRIMARY KEY(source,stamp));
        CREATE TABLE IF NOT EXISTS samples(id INTEGER PRIMARY KEY,stamp TEXT,source TEXT,server TEXT,gpu TEXT,used REAL,total REAL,compute REAL);
        CREATE TABLE IF NOT EXISTS alarms(id INTEGER PRIMARY KEY,stamp TEXT,source TEXT,server TEXT,gpu TEXT,kind TEXT,message TEXT);
        CREATE TABLE IF NOT EXISTS alarm_state(source TEXT,server_id TEXT,gpu_key TEXT,high INTEGER,PRIMARY KEY(source,server_id,gpu_key));
        """;
        cmd.ExecuteNonQuery();
    }
    public bool Save(GpuState state, string source, double threshold)
    {
        if (state.Refreshing || state.UpdatedAt is null)
            return false;
        if (!double.IsFinite(threshold) || threshold < 1 || threshold > 100)
            throw new ArgumentOutOfRangeException(nameof(threshold));
        using var tx = connection.BeginTransaction();
        var stamp = state.UpdatedAt.Value.ToUniversalTime().ToString("O");
        using (var cmd = Command("INSERT OR IGNORE INTO snapshots(source,stamp) VALUES($source,$stamp)", tx, ("source", source), ("stamp", stamp)))
            if (cmd.ExecuteNonQuery() == 0)
            {
                tx.Rollback();
                return false;
            }
        foreach (var server in state.Servers)
            foreach (var gpu in server.Gpus)
            {
                var label = $"GPU {gpu.Index} · {gpu.Name}";
                using (var cmd = Command("INSERT INTO samples(stamp,source,server,gpu,used,total,compute) VALUES($stamp,$source,$server,$gpu,$used,$total,$compute)", tx,
                    ("stamp", stamp), ("source", source), ("server", server.Name), ("gpu", label), ("used", gpu.UsedMiB), ("total", gpu.TotalMiB), ("compute", gpu.Compute)))
                    cmd.ExecuteNonQuery();
                if (gpu.MemoryPercent is not double percent)
                    continue;
                var key = gpu.Uuid ?? $"index:{gpu.Index}";
                bool wasHigh;
                using (var q = Command("SELECT high FROM alarm_state WHERE source=$source AND server_id=$server AND gpu_key=$gpu", tx, ("source", source), ("server", server.Id), ("gpu", key)))
                    wasHigh = Convert.ToInt32(q.ExecuteScalar() ?? 0) == 1;
                var high = percent >= threshold;
                if (high != wasHigh)
                {
                    var message = high ? $"VRAM {percent:F1}% · 기준 {threshold:F0}% 이상" : $"VRAM {percent:F1}% · 기준 미만으로 복구";
                    using var alarm = Command("INSERT INTO alarms(stamp,source,server,gpu,kind,message) VALUES($stamp,$source,$server,$gpu,$kind,$message)", tx, ("stamp", stamp), ("source", source), ("server", server.Name), ("gpu", label), ("kind", high ? "warning" : "recovery"), ("message", message));
                    alarm.ExecuteNonQuery();
                }
                using var update = Command("INSERT INTO alarm_state(source,server_id,gpu_key,high) VALUES($source,$server,$gpu,$high) ON CONFLICT(source,server_id,gpu_key) DO UPDATE SET high=excluded.high", tx, ("source", source), ("server", server.Id), ("gpu", key), ("high", high ? 1 : 0));
                update.ExecuteNonQuery();
            }
        foreach (var table in new[] { "samples", "alarms", "snapshots", "alarm_state" })
        {
            using var prune = Command($"DELETE FROM {table} WHERE rowid NOT IN (SELECT rowid FROM {table} ORDER BY rowid DESC LIMIT $limit)", tx, ("limit", retention));
            prune.ExecuteNonQuery();
        }
        tx.Commit();
        return true;
    }
    public List<SampleRow> Samples(string source, int limit = 250)
    {
        using var q = Command("SELECT stamp,server,gpu,used,total,compute,source FROM samples WHERE source=$source ORDER BY id DESC LIMIT $limit", null, ("source", source), ("limit", Math.Clamp(limit, 1, 5000)));
        using var r = q.ExecuteReader();
        var rows = new List<SampleRow>();
        while (r.Read())
            rows.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), Number(r, 3), Number(r, 4), Number(r, 5), r.GetString(6)));
        return rows;
    }
    public List<AlarmRow> Alarms(string source, int limit = 250)
    {
        using var q = Command("SELECT stamp,server,gpu,kind,message,source FROM alarms WHERE source=$source ORDER BY id DESC LIMIT $limit", null, ("source", source), ("limit", Math.Clamp(limit, 1, 5000)));
        using var r = q.ExecuteReader();
        var rows = new List<AlarmRow>();
        while (r.Read())
            rows.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));
        return rows;
    }
    SqliteCommand Command(string sql, SqliteTransaction? tx, params (string name, object? value)[] values)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        foreach (var (name, value) in values)
            cmd.Parameters.AddWithValue("$" + name, value ?? DBNull.Value);
        return cmd;
    }
    static double? Number(SqliteDataReader r, int n) => r.IsDBNull(n) ? null : r.GetDouble(n);
    public void Dispose() => connection.Dispose();
}
