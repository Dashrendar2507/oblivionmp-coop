using System.Runtime.InteropServices;
using System.Text;

namespace CoopQuestSync.Server;

/// <summary>
/// Reads the admin list of the ReadyM admin panel (port 9050) from the server's own database, data/web.db.
///
/// The SDK has no permission API, and the panel's web API needs an admin to sign in with ReadyM, so the file is the only
/// way in. Its layout is not documented, so this does not assume one: it opens the file read only, takes every table
/// with "admin" in its name (but not logs and such) plus the panel's "Users" table, and collects every account id (a Guid, as text or as 16 bytes) it finds there. When an
/// admin row only points at another table (a column such as UserId), it follows that one step to the table it names
/// and collects the ids of the rows it points at. A player is a panel admin when their ReadyM account id is in the set.
///
/// SQLite itself comes from the system or the server: e_sqlite3 (shipped with .NET servers), winsqlite3 (part of
/// Windows 10 and 11) or libsqlite3 (Linux). Nothing extra ships with the mod.
/// </summary>
public static class PanelAdminDb
{
    public sealed record Result(HashSet<Guid> Ids, bool Ok, string Status);

    public static Result Load(string path)
    {
        // Relative to the working folder, or failing that to the server's own folder (they differ when the server
        // is started from elsewhere).
        var full = Path.GetFullPath(path);
        if (!File.Exists(full) && !Path.IsPathRooted(path) && Path.GetDirectoryName(Environment.ProcessPath) is { Length: > 0 } serverDir)
            full = Path.GetFullPath(Path.Combine(serverDir, path));
        if (!File.Exists(full))
            return new Result([], false, $"no panel database at {full}");
        if (!Native.TryInit(out var lib))
            return new Result([], false, "SQLite is not available on this machine, so the panel's admin list can't be read");

        try
        {
            using var db = new Db(full);
            var tables = db.Query("SELECT name FROM sqlite_master WHERE type = 'table'").Select(r => r.Values[0] as string)
                .OfType<string>().ToList();
            // Logs and the like may name players who are not admins (who an admin kicked, for one), so leave them out.
            // In OblivionMP server 1.0.1 the panel's admins are its "Users" (accounts that sign in to the panel, shown
            // under Admins), next to GamePlayers, WhitelistedPlayers and BannedPlayers, which are players, not admins.
            string[] notAList = ["log", "audit", "action", "event", "history", "invite", "ban", "kick", "request", "player", "whitelist", "permission"];
            var adminTables = tables.Where(t => (t.Contains("admin", StringComparison.OrdinalIgnoreCase) || t.Equals("Users", StringComparison.OrdinalIgnoreCase))
                && !notAList.Any(w => t.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
            if (adminTables.Count == 0)
                return new Result([], false, $"no admin table in the panel database (tables: {string.Join(", ", tables)})");

            var ids = new HashSet<Guid>();
            var layout = new List<string>();
            foreach (var table in adminTables)
            {
                var rows = db.Query($"SELECT * FROM {Quote(table)}");
                var columns = db.LastColumns;
                layout.Add($"{table}({string.Join(", ", columns)}): {rows.Count} row(s)");
                foreach (var row in rows)
                    foreach (var value in row.Values)
                        AddGuids(ids, value);

                // One step through columns like UserId / AccountId to the table they name (Users, AspNetUsers, Accounts...).
                for (var c = 0; c < columns.Length; c++)
                {
                    var name = columns[c];
                    if (name.Length <= 2 || !name.EndsWith("id", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var prefix = name[..^2].TrimEnd('_').ToLowerInvariant();
                    var keys = rows.Select(r => Key(r.Values[c])).OfType<string>().ToHashSet();
                    if (prefix.Length == 0 || keys.Count == 0)
                        continue;
                    foreach (var target in tables.Where(t => !adminTables.Contains(t) && Singular(t) == prefix))
                    {
                        var targetRows = db.Query($"SELECT * FROM {Quote(target)}");
                        var idColumn = Array.FindIndex(db.LastColumns, n => n.Equals("id", StringComparison.OrdinalIgnoreCase));
                        if (idColumn < 0)
                            continue;
                        var hits = 0;
                        foreach (var row in targetRows.Where(r => Key(r.Values[idColumn]) is { } k && keys.Contains(k)))
                        {
                            hits++;
                            foreach (var value in row.Values)
                                AddGuids(ids, value);
                        }
                        layout.Add($"{table}.{name} -> {target}: {hits} row(s)");
                    }
                }
            }
            foreach (var table in tables.Where(t => t.Contains("permission", StringComparison.OrdinalIgnoreCase)))
            {
                var rows = db.Query($"SELECT * FROM {Quote(table)}");
                var names = rows.SelectMany(r => r.Values).OfType<string>().Where(v => v.Length <= 40 && !Guid.TryParse(v, out _))
                    .Distinct().Take(20);
                layout.Add($"{table}({string.Join(", ", db.LastColumns)}): {rows.Count} row(s), values: {string.Join(", ", names)}");
            }
            return ids.Count == 0
                ? new Result(ids, false, $"no account ids in the admin table(s): {string.Join("; ", layout)}")
                : new Result(ids, true, $"read {string.Join("; ", layout)} [SQLite: {lib}]");
        }
        catch (Exception e)
        {
            return new Result([], false, $"could not read the panel database: {e.Message}");
        }
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    /// <summary>"AspNetUsers" -> "user", "Accounts" -> "account".</summary>
    private static string Singular(string table)
    {
        var t = table.ToLowerInvariant();
        if (t.StartsWith("aspnet"))
            t = t[6..];
        return t.EndsWith('s') ? t[..^1] : t;
    }

    private static string? Key(object? value) => value switch
    {
        null => null,
        byte[] b => Convert.ToHexString(b),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
    };

    private static void AddGuids(HashSet<Guid> into, object? value)
    {
        switch (value)
        {
            case string s when Guid.TryParse(s.Trim(), out var g) && g != Guid.Empty:
                into.Add(g);
                break;
            case byte[] { Length: 16 } b:
                into.Add(new Guid(b));
                into.Add(new Guid(b, bigEndian: true));
                break;
        }
    }

    private sealed record Row(object?[] Values);

    private sealed class Db : IDisposable
    {
        private const int OpenReadOnly = 0x1;
        private const int Ok = 0, RowReady = 100, Done = 101;
        private IntPtr _db;

        public string[] LastColumns { get; private set; } = [];

        public Db(string path)
        {
            var rc = Native.Open(Utf8(path), out _db, OpenReadOnly, IntPtr.Zero);
            if (rc != Ok)
            {
                var message = Error();
                Dispose();
                throw new InvalidOperationException($"open failed ({rc}): {message}");
            }
            Native.BusyTimeout(_db, 2000);
        }

        public List<Row> Query(string sql)
        {
            var bytes = Utf8(sql);
            if (Native.Prepare(_db, bytes, bytes.Length, out var stmt, IntPtr.Zero) != Ok)
                throw new InvalidOperationException(Error());
            try
            {
                var count = Native.ColumnCount(stmt);
                var columns = new string[count];
                for (var i = 0; i < count; i++)
                    columns[i] = Marshal.PtrToStringUTF8(Native.ColumnName(stmt, i)) ?? "";
                LastColumns = columns;

                var rows = new List<Row>();
                while (true)
                {
                    var rc = Native.Step(stmt);
                    if (rc == Done)
                        return rows;
                    if (rc != RowReady)
                        throw new InvalidOperationException(Error());
                    var values = new object?[count];
                    for (var i = 0; i < count; i++)
                        values[i] = Native.ColumnType(stmt, i) switch
                        {
                            1 => Native.ColumnInt64(stmt, i),
                            3 => Marshal.PtrToStringUTF8(Native.ColumnText(stmt, i), Native.ColumnBytes(stmt, i)),
                            4 => Blob(stmt, i),
                            _ => null,
                        };
                    rows.Add(new Row(values));
                }
            }
            finally
            {
                Native.Finalize(stmt);
            }
        }

        private static byte[] Blob(IntPtr stmt, int i)
        {
            var length = Native.ColumnBytes(stmt, i);
            var data = new byte[length];
            if (length > 0)
                Marshal.Copy(Native.ColumnBlob(stmt, i), data, 0, length);
            return data;
        }

        private string Error() => _db == IntPtr.Zero ? "no database" : Marshal.PtrToStringUTF8(Native.ErrMsg(_db)) ?? "unknown error";

        private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s + "\0");

        public void Dispose()
        {
            if (_db != IntPtr.Zero)
                Native.Close(_db);
            _db = IntPtr.Zero;
        }
    }

    /// <summary>The handful of SQLite functions needed, bound at run time to whichever SQLite library is found.</summary>
    private static class Native
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int OpenFn(byte[] file, out IntPtr db, int flags, IntPtr vfs);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int CloseFn(IntPtr db);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int BusyTimeoutFn(IntPtr db, int ms);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int PrepareFn(IntPtr db, byte[] sql, int length, out IntPtr stmt, IntPtr tail);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int StmtFn(IntPtr stmt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int ColumnIntFn(IntPtr stmt, int column);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate long ColumnInt64Fn(IntPtr stmt, int column);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr ColumnPtrFn(IntPtr stmt, int column);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr ErrMsgFn(IntPtr db);

        public static OpenFn Open = null!;
        public static CloseFn Close = null!;
        public static BusyTimeoutFn BusyTimeout = null!;
        public static PrepareFn Prepare = null!;
        public static StmtFn Step = null!, Finalize = null!, ColumnCount = null!;
        public static ColumnIntFn ColumnType = null!, ColumnBytes = null!;
        public static ColumnInt64Fn ColumnInt64 = null!;
        public static ColumnPtrFn ColumnName = null!, ColumnText = null!, ColumnBlob = null!;
        public static ErrMsgFn ErrMsg = null!;

        private static readonly object Gate = new();
        private static string? _loaded;
        private static bool _tried;

        public static bool TryInit(out string library)
        {
            lock (Gate)
            {
                if (!_tried)
                {
                    _tried = true;
                    _loaded = Bind();
                }
                library = _loaded ?? "";
                return _loaded is not null;
            }
        }

        private static string? Bind()
        {
            var serverDir = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
            string[] names = OperatingSystem.IsWindows()
                ? [Path.Combine(serverDir, "e_sqlite3.dll"), "e_sqlite3", "winsqlite3", "sqlite3"]
                : [Path.Combine(serverDir, "libe_sqlite3.so"), "e_sqlite3", "libe_sqlite3.so", "libsqlite3.so.0", "libsqlite3.so", "sqlite3"];
            foreach (var name in names)
            {
                if (!NativeLibrary.TryLoad(name, out var handle))
                    continue;
                try
                {
                    T Fn<T>(string export) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(handle, export));
                    Open = Fn<OpenFn>("sqlite3_open_v2");
                    Close = Fn<CloseFn>("sqlite3_close_v2");
                    BusyTimeout = Fn<BusyTimeoutFn>("sqlite3_busy_timeout");
                    Prepare = Fn<PrepareFn>("sqlite3_prepare_v2");
                    Step = Fn<StmtFn>("sqlite3_step");
                    Finalize = Fn<StmtFn>("sqlite3_finalize");
                    ColumnCount = Fn<StmtFn>("sqlite3_column_count");
                    ColumnType = Fn<ColumnIntFn>("sqlite3_column_type");
                    ColumnBytes = Fn<ColumnIntFn>("sqlite3_column_bytes");
                    ColumnInt64 = Fn<ColumnInt64Fn>("sqlite3_column_int64");
                    ColumnName = Fn<ColumnPtrFn>("sqlite3_column_name");
                    ColumnText = Fn<ColumnPtrFn>("sqlite3_column_text");
                    ColumnBlob = Fn<ColumnPtrFn>("sqlite3_column_blob");
                    ErrMsg = Fn<ErrMsgFn>("sqlite3_errmsg");
                    return Path.GetFileName(name);
                }
                catch (EntryPointNotFoundException)
                {
                    // Not a usable SQLite build; try the next one.
                }
            }
            return null;
        }
    }
}
