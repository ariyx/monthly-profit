using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Profit.Core;

public sealed class Store
{
    public string Path { get; }
    private SqliteConnection Open(string? path = null, bool readOnly = false)
    {
        var b = new SqliteConnectionStringBuilder { DataSource = path ?? Path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = false };
        var c = new SqliteConnection(b.ToString()); c.Open(); return c;
    }
    public Store(string path)
    {
        Path = System.IO.Path.GetFullPath(path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA user_version"; var version = Convert.ToInt32(cmd.ExecuteScalar());
        if (version > 1) throw new InvalidDataException("بانک اطلاعات متعلق به نسخه جدیدتر برنامه است.");
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS months (key TEXT PRIMARY KEY, revision INTEGER NOT NULL, data TEXT NOT NULL); PRAGMA user_version=1;"; cmd.ExecuteNonQuery();
    }
    public List<string> Keys()
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT key FROM months ORDER BY key DESC";
        using var r = cmd.ExecuteReader(); var result = new List<string>(); while (r.Read()) result.Add(r.GetString(0)); return result;
    }
    public Month Load(string key)
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT data,revision FROM months WHERE key=$key"; cmd.Parameters.AddWithValue("$key", key);
        using var r = cmd.ExecuteReader(); if (!r.Read()) throw new InvalidDataException("ماه یافت نشد.");
        var m = JsonSerializer.Deserialize<Month>(r.GetString(0), Rules.Json) ?? throw new InvalidDataException("پرونده نامعتبر است.");
        m.Revision = r.GetInt32(1); if (m.Key != key) throw new InvalidDataException("شناسه ماه ناسازگار است."); Rules.Validate(m); return m;
    }
    public void Save(Month month)
    {
        Rules.Validate(month); var next = month with { Revision = month.Revision + 1 };
        using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = month.Revision == 0 ? "INSERT INTO months(key,revision,data) VALUES($key,$next,$data)" : "UPDATE months SET revision=$next,data=$data WHERE key=$key AND revision=$old";
        cmd.Parameters.AddWithValue("$key", month.Key); cmd.Parameters.AddWithValue("$next", next.Revision); cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(next, Rules.Json));
        if (month.Revision != 0) cmd.Parameters.AddWithValue("$old", month.Revision);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("اطلاعات تغییر کرده است؛ پرونده را دوباره باز کنید.");
        tx.Commit(); month.Revision = next.Revision;
    }
    public void Backup(string destination)
    {
        if (System.IO.Path.GetFullPath(destination).Equals(Path, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("پشتیبان باید در مسیر دیگری ذخیره شود.");
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var source = Open()) using (var target = Open(temp)) source.BackupDatabase(target); CheckBackup(temp); File.Move(temp, destination, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void CheckBackup(string file)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA integrity_check";
        if (!Equals(cmd.ExecuteScalar(), "ok")) throw new InvalidDataException("فایل پشتیبان آسیب دیده است.");
        cmd.CommandText = "PRAGMA user_version"; if (Convert.ToInt32(cmd.ExecuteScalar()) != 1) throw new InvalidDataException("نسخه پشتیبان سازگار نیست.");
        cmd.CommandText = "SELECT key, data FROM months"; using var r = cmd.ExecuteReader();
        while (r.Read()) { var m = JsonSerializer.Deserialize<Month>(r.GetString(1), Rules.Json) ?? throw new InvalidDataException("داده نامعتبر"); Rules.Validate(m); if (m.Key != r.GetString(0)) throw new InvalidDataException("شناسه ماه ناسازگار است."); }
    }
    public string Restore(string source)
    {
        if (System.IO.Path.GetFullPath(source).Equals(Path, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("فایل انتخابی همان بانک فعال است.");
        CheckBackup(source);
        var safety = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "backups", "before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".sqlite");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(safety)!); Backup(safety);
        using var from = Open(source, true); using var to = Open(); from.BackupDatabase(to); return safety;
    }
}
