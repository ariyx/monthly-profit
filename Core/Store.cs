using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Profit.Core;

public sealed class Store
{
    const int SchemaVersion = 2;
    public string Path { get; }
    string PreferencesPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "preferences.json");
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
        if (version > SchemaVersion) throw new InvalidDataException("بانک اطلاعات متعلق به نسخه جدیدتر برنامه است.");
        if (version < SchemaVersion)
        {
            // داده‌های نسخهٔ اول آزمایشی بودند و مدل آن با گردش واقعی کالا سازگار نیست.
            cmd.CommandText = "DROP TABLE IF EXISTS months; DROP TABLE IF EXISTS app_state; CREATE TABLE months (key TEXT PRIMARY KEY, revision INTEGER NOT NULL, data TEXT NOT NULL); CREATE TABLE app_state (id INTEGER PRIMARY KEY CHECK(id=1), data TEXT NOT NULL); INSERT INTO app_state(id,data) VALUES(1,$ledger); PRAGMA user_version=2;";
            cmd.Parameters.AddWithValue("$ledger", JsonSerializer.Serialize(new Ledger(), Rules.Json)); cmd.ExecuteNonQuery();
        }
        else
        {
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS months (key TEXT PRIMARY KEY, revision INTEGER NOT NULL, data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS app_state (id INTEGER PRIMARY KEY CHECK(id=1), data TEXT NOT NULL); INSERT OR IGNORE INTO app_state(id,data) VALUES(1,$ledger);";
            cmd.Parameters.AddWithValue("$ledger", JsonSerializer.Serialize(new Ledger(), Rules.Json)); cmd.ExecuteNonQuery();
        }
    }

    public Ledger LoadLedger()
    {
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT data FROM app_state WHERE id=1";
        var json = cmd.ExecuteScalar() as string ?? throw new InvalidDataException("دفتر تراکنش‌ها یافت نشد.");
        var ledger = JsonSerializer.Deserialize<Ledger>(json, Rules.Json) ?? throw new InvalidDataException("دفتر تراکنش‌ها نامعتبر است.");
        LedgerCalculator.Calculate(ledger); return ledger;
    }
    public void SaveLedger(Ledger ledger)
    {
        LedgerCalculator.Calculate(ledger);
        var keys = ledger.Purchases.Select(x => Rules.MonthOf(x.Date)).Concat(ledger.Sales.Select(x => Rules.MonthOf(x.Date))).Distinct().ToList();
        using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "UPDATE app_state SET data=$data WHERE id=1"; cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(ledger, Rules.Json));
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("ذخیره دفتر تراکنش‌ها انجام نشد.");
        foreach (var key in keys)
        {
            cmd.Parameters.Clear(); cmd.CommandText = "INSERT OR IGNORE INTO months(key,revision,data) VALUES($key,1,$data)";
            cmd.Parameters.AddWithValue("$key", key); cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(new Month { Key = key }, Rules.Json)); cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
    public Month EnsureMonth(string key)
    {
        if (!Rules.ValidMonth(key)) throw new InvalidDataException("ماه نامعتبر است.");
        if (Keys().Contains(key)) return Load(key);
        var month = new Month { Key = key }; Save(month); return month;
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
        if (month.Revision > 0)
        {
            cmd.CommandText = "SELECT data FROM months WHERE key=$key AND revision=$old"; cmd.Parameters.AddWithValue("$key", month.Key); cmd.Parameters.AddWithValue("$old", month.Revision);
            var saved = cmd.ExecuteScalar() as string;
            if (saved is null) throw new InvalidOperationException("اطلاعات تغییر کرده است؛ پرونده را دوباره باز کنید.");
            var prior = JsonSerializer.Deserialize<Month>(saved, Rules.Json) ?? throw new InvalidDataException("پرونده نامعتبر است.");
            if (prior.IsClosed && !month.IsClosed) throw new InvalidOperationException("ماه بسته است؛ ابتدا آن را از بخش مدیریت ماه باز کنید.");
            if (prior.IsClosed && month.IsClosed && JsonSerializer.Serialize(prior, Rules.Json) != JsonSerializer.Serialize(month with { Revision = prior.Revision }, Rules.Json)) throw new InvalidOperationException("ماه بسته است و قابل ویرایش نیست.");
            cmd.Parameters.Clear();
        }
        cmd.CommandText = month.Revision == 0 ? "INSERT INTO months(key,revision,data) VALUES($key,$next,$data)" : "UPDATE months SET revision=$next,data=$data WHERE key=$key AND revision=$old";
        cmd.Parameters.AddWithValue("$key", month.Key); cmd.Parameters.AddWithValue("$next", next.Revision); cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(next, Rules.Json));
        if (month.Revision != 0) cmd.Parameters.AddWithValue("$old", month.Revision);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("اطلاعات تغییر کرده است؛ پرونده را دوباره باز کنید.");
        tx.Commit(); month.Revision = next.Revision;
    }
    public Month SetClosed(Month month, bool closed)
    {
        if (month.IsClosed == closed) return month;
        var next = month with { IsClosed = closed, Audit = [.. month.Audit.TakeLast(499), new AuditEntry { Action = closed ? "ماه بسته شد" : "ماه باز شد" }] };
        if (closed) Save(next); else
        {
            using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "UPDATE months SET revision=$next,data=$data WHERE key=$key AND revision=$old";
            next.Revision = month.Revision + 1; cmd.Parameters.AddWithValue("$key", next.Key); cmd.Parameters.AddWithValue("$next", next.Revision); cmd.Parameters.AddWithValue("$old", month.Revision); cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(next, Rules.Json));
            if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("اطلاعات تغییر کرده است؛ پرونده را دوباره باز کنید."); tx.Commit();
        }
        return next;
    }
    public Preferences LoadPreferences()
    {
        try { return File.Exists(PreferencesPath) ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PreferencesPath), Rules.Json) ?? new Preferences() : new Preferences(); }
        catch { return new Preferences(); }
    }
    public void SavePreferences(Preferences settings)
    {
        if (settings.AutoBackupKeep is < 1 or > 50) throw new InvalidDataException("تعداد نسخه‌های خودکار باید بین ۱ تا ۵۰ باشد.");
        var temp = PreferencesPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(settings, Rules.Json)); File.Move(temp, PreferencesPath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public string CreateAutomaticBackup(int keep)
    {
        if (keep is < 1 or > 50) throw new InvalidDataException("تعداد نسخه‌های خودکار باید بین ۱ تا ۵۰ باشد.");
        var folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "backups", "automatic"); Directory.CreateDirectory(folder);
        var file = System.IO.Path.Combine(folder, "monthly-profit-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".sqlite"); Backup(file);
        foreach (var old in Directory.GetFiles(folder, "*.sqlite").OrderByDescending(File.GetLastWriteTimeUtc).Skip(keep)) File.Delete(old);
        return file;
    }
    public string? LastAutomaticBackup()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "backups", "automatic");
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.sqlite").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
    }
    public string CreateSafetyBackup(string operation)
    {
        var safe = string.Concat(operation.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
        if (string.IsNullOrWhiteSpace(safe)) safe = "change";
        var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "backups", safe + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".sqlite");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!); Backup(file); return file;
    }
    public Month Rename(Month month, string nextKey)
    {
        Rules.Validate(month); if (month.Revision <= 0) throw new InvalidOperationException("ماه ذخیره‌نشده قابل تغییر نیست.");
        if (!Rules.ValidMonth(nextKey)) throw new InvalidDataException("قالب ماه باید مانند 1405/06 باشد.");
        if (month.Key == nextKey) return month;
        var next = month with { Key = nextKey, Revision = month.Revision + 1 }; Rules.Validate(next);
        using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM months WHERE key=$key"; cmd.Parameters.AddWithValue("$key", nextKey);
        if (Convert.ToInt32(cmd.ExecuteScalar()) != 0) throw new InvalidDataException("این ماه قبلاً ایجاد شده است.");
        cmd.Parameters.Clear(); cmd.CommandText = "INSERT INTO months(key,revision,data) VALUES($key,$revision,$data)";
        cmd.Parameters.AddWithValue("$key", next.Key); cmd.Parameters.AddWithValue("$revision", next.Revision); cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(next, Rules.Json));
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("تغییر ماه انجام نشد.");
        cmd.Parameters.Clear(); cmd.CommandText = "DELETE FROM months WHERE key=$key AND revision=$revision";
        cmd.Parameters.AddWithValue("$key", month.Key); cmd.Parameters.AddWithValue("$revision", month.Revision);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("اطلاعات تغییر کرده است؛ پرونده را دوباره باز کنید.");
        tx.Commit(); return next;
    }
    public void Delete(Month month)
    {
        if (month.Revision <= 0) throw new InvalidOperationException("ماه ذخیره‌نشده قابل حذف نیست.");
        using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "DELETE FROM months WHERE key=$key AND revision=$revision";
        cmd.Parameters.AddWithValue("$key", month.Key); cmd.Parameters.AddWithValue("$revision", month.Revision);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("اطلاعات تغییر کرده است؛ پرونده را دوباره باز کنید.");
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
        cmd.CommandText = "PRAGMA user_version"; if (Convert.ToInt32(cmd.ExecuteScalar()) != SchemaVersion) throw new InvalidDataException("نسخه پشتیبان سازگار نیست.");
        cmd.CommandText = "SELECT key, data FROM months"; using var r = cmd.ExecuteReader();
        while (r.Read()) { var m = JsonSerializer.Deserialize<Month>(r.GetString(1), Rules.Json) ?? throw new InvalidDataException("داده نامعتبر"); Rules.Validate(m); if (m.Key != r.GetString(0)) throw new InvalidDataException("شناسه ماه ناسازگار است."); }
        r.Close(); cmd.CommandText = "SELECT data FROM app_state WHERE id=1"; var state = cmd.ExecuteScalar() as string ?? throw new InvalidDataException("دفتر تراکنش‌ها موجود نیست.");
        LedgerCalculator.Calculate(JsonSerializer.Deserialize<Ledger>(state, Rules.Json) ?? throw new InvalidDataException("دفتر تراکنش‌ها نامعتبر است."));
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
