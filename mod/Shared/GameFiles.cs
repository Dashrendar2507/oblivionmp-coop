using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace CoopQuestSync;

// Shared by the server and the client (linked into both): reading the game's own text out of its .pak files.

/// <summary>Finds Oblivion Remastered and reads its text (names, dialogue, journal) from its .pak files.</summary>
internal static class GameFiles
{
    /// <summary>key -> text from Localization/Game/&lt;language&gt;/Game.locres in the game's .pak files, or null (logged).</summary>
    public static Dictionary<string, string>? ReadText(string game, string language, ILogger logger)
        => ReadText(game, [string.IsNullOrWhiteSpace(language) ? "en" : language.Trim()], logger);

    /// <summary>The same, with the first of <paramref name="languages"/> the game has.</summary>
    public static Dictionary<string, string>? ReadText(string game, IReadOnlyList<string> languages, ILogger logger)
    {
        Oodle.GameFolder = game;
        var paks = Path.Combine(game, "OblivionRemastered", "Content", "Paks");
        if (!Directory.Exists(paks))
        {
            logger.LogWarning("[QuestSync] No Paks folder in {Folder}.", game);
            return null;
        }
        foreach (var pakPath in Directory.GetFiles(paks, "*.pak").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var pak = new PakFile(pakPath, logger);
                foreach (var lang in languages)
                {
                    var entry = pak.Find($"Localization/Game/{lang}/Game.locres");
                    if (entry is null)
                        continue;
                    logger.LogInformation("[QuestSync] Reading the game's text from {Pak} ({Path}, {Method}).",
                        Path.GetFileName(pakPath), entry.Path, pak.MethodName(entry.Method));
                    return Locres.Parse(pak.Read(entry));
                }
            }
            catch (Exception e)
            {
                logger.LogWarning("[QuestSync] Couldn't read the game's text from {Pak}: {Error}", Path.GetFileName(pakPath), e.Message);
                return null;
            }
        }
        logger.LogWarning("[QuestSync] No Localization/Game/{Languages}/Game.locres in the game's .pak files in {Folder}.",
            string.Join("|", languages), paks);
        return null;
    }

    public static bool IsGame(string dir) => Directory.Exists(Path.Combine(dir, "OblivionRemastered", "Content", "Paks"));

    /// <summary>The game folder that holds <paramref name="path"/> (a file or folder inside it), or null.</summary>
    public static string? GameAbove(string? path)
    {
        for (var d = path; !string.IsNullOrEmpty(d); d = Path.GetDirectoryName(d))
            if (IsGame(d))
                return d;
        return null;
    }

    /// <summary>The game's folder: the one given, else the usual Steam places.</summary>
    public static string? FindGame(string configured, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var dir = configured.Trim().Trim('"');
            if (GameAbove(dir) is { } found)
                return found;
            logger.LogWarning("[QuestSync] GameFolder {Folder} doesn't hold Oblivion Remastered.", dir);
            return null;
        }
        foreach (var library in SteamLibraries())
        {
            var dir = Path.Combine(library, "steamapps", "common", "Oblivion Remastered");
            if (IsGame(dir))
                return dir;
        }
        return null;
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var (hive, key, value) in new[]
                     {
                         (Microsoft.Win32.Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                         (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                         (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                     })
            {
                try
                {
                    if (hive.OpenSubKey(key)?.GetValue(value) is string path && path.Length > 0)
                        roots.Add(path.Replace('/', '\\'));
                }
                catch { /* no access: try the next */ }
            }
            roots.Add(@"C:\Program Files (x86)\Steam");
            roots.Add(@"C:\Program Files\Steam");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            roots.Add(Path.Combine(home, ".steam", "steam"));
            roots.Add(Path.Combine(home, ".local", "share", "Steam"));
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (seen.Add(root))
                yield return root;
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;
            string text;
            try { text = File.ReadAllText(vdf); }
            catch { continue; }
            foreach (Match m in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
            {
                var lib = m.Groups[1].Value.Replace(@"\\", @"\");
                if (seen.Add(lib))
                    yield return lib;
            }
        }
    }
}

/// <summary>Reads Unreal .locres files (every version: legacy, compact, CRC32 and CityHash64 keys).</summary>
internal static class Locres
{
    private static readonly byte[] Magic = Convert.FromHexString("0E147475674A03FC4A15909DC3377F1B");

    /// <summary>key -> text, over all namespaces (the first namespace with a key wins).</summary>
    public static Dictionary<string, string> Parse(byte[] data)
    {
        var r = new BinReader(data);
        var version = 0;
        List<string>? strings = null;
        if (data.Length >= 16 && data.AsSpan(0, 16).SequenceEqual(Magic))
        {
            r.Pos = 16;
            version = r.U8();
            if (version >= 1)
            {
                var offset = r.I64();
                if (offset != -1)
                {
                    var here = r.Pos;
                    r.Pos = offset;
                    var count = r.I32();
                    strings = new List<string>(Math.Max(0, count));
                    for (var i = 0; i < count; i++)
                    {
                        strings.Add(r.FString());
                        if (version >= 2)
                            r.I32();                // reference count
                    }
                    r.Pos = here;
                }
            }
            if (version >= 2)
                r.U32();                            // entries count
        }
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        var namespaces = r.U32();
        for (var n = 0; n < namespaces; n++)
        {
            if (version >= 2)
                r.U32();                            // namespace hash
            r.FString();                            // namespace
            var count = r.U32();
            for (var k = 0; k < count; k++)
            {
                if (version >= 2)
                    r.U32();                        // key hash
                var key = r.FString();
                r.U32();                            // source string hash
                string text;
                if (version >= 1)
                {
                    var index = r.I32();
                    text = strings is not null && index >= 0 && index < strings.Count ? strings[index] : "";
                }
                else
                    text = r.FString();
                output.TryAdd(key, text);
            }
        }
        return output;
    }
}

/// <summary>
/// Reads files out of an Unreal .pak (versions 8 to 11; Oblivion Remastered is 11). Uncompressed, Zlib and Gzip entries
/// are read directly; Oodle entries need oo2core_9_win64.dll, looked for next to the server and in the game's folder.
/// Encrypted paks aren't supported (the remaster's isn't encrypted).
/// </summary>
internal sealed class PakFile : IDisposable
{
    private const uint PakMagic = 0x5A6F12E1;

    public sealed record Entry(string Path, long Offset, long Size, long UncompressedSize, int Method, bool Encrypted);

    private readonly FileStream _file;
    private readonly ILogger _logger;
    private readonly int _version;
    private readonly string[] _methods = [];
    private readonly string _mount = "";
    private readonly long _pathHashSeed;
    private readonly byte[] _encoded = [];
    private readonly List<Entry> _plain = new();
    private readonly Dictionary<string, int>? _directory;     // full path (from the mount point) -> encoded offset
    private readonly Dictionary<ulong, int>? _pathHashes;     // FNV64 of the lower-case path -> encoded offset

    public PakFile(string path, ILogger logger)
    {
        _logger = logger;
        _file = File.OpenRead(path);

        // The footer: find its magic near the end (its size depends on the version).
        var tailLength = (int)Math.Min(_file.Length, 1024);
        var tail = ReadAt(_file.Length - tailLength, tailLength);
        var at = -1;
        for (var i = tailLength - 44; i >= 0; i--)
            if (BitConverter.ToUInt32(tail, i) == PakMagic)
            {
                at = i;
                break;
            }
        if (at < 0)
            throw new InvalidDataException("not a .pak file (no footer)");
        _version = BitConverter.ToInt32(tail, at + 4);
        var indexOffset = BitConverter.ToInt64(tail, at + 8);
        var indexSize = BitConverter.ToInt64(tail, at + 16);
        var encryptedIndex = at >= 1 && _version >= 4 && tail[at - 1] != 0;
        if (_version < 8)
            throw new InvalidDataException($".pak version {_version} isn't supported");
        if (encryptedIndex)
            throw new InvalidDataException("the .pak's index is encrypted");
        var names = at + 44 + (_version == 9 ? 1 : 0);
        var count = Math.Max(0, Math.Min(5, (tailLength - names) / 32));
        _methods = Enumerable.Range(0, count)
            .Select(i => Encoding.ASCII.GetString(tail, names + 32 * i, 32).TrimEnd('\0', ' '))
            .ToArray();

        var index = new BinReader(ReadAt(indexOffset, checked((int)indexSize)));
        _mount = index.FString();
        var entryCount = index.I32();
        if (_version >= 10)
        {
            _pathHashSeed = index.I64();
            if (index.U32() != 0)
            {
                var off = index.I64();
                var size = index.I64();
                index.Pos += 20;
                _pathHashes = ReadPathHashIndex(ReadAt(off, checked((int)size)));
            }
            if (index.U32() != 0)
            {
                var off = index.I64();
                var size = index.I64();
                index.Pos += 20;
                _directory = ReadDirectoryIndex(ReadAt(off, checked((int)size)));
            }
            var encodedSize = index.I32();
            _encoded = index.Bytes(encodedSize);
            var plainCount = index.I32();
            for (var i = 0; i < plainCount; i++)
                _plain.Add(ReadEntry(index, ""));
        }
        else
        {
            // Before version 10 the index lists every file with its full entry.
            _directory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < entryCount; i++)
            {
                var name = index.FString();
                _plain.Add(ReadEntry(index, name));
                _directory[name] = -(_plain.Count);   // as a "plain entry" reference: -(index + 1)
            }
        }
        _logger.LogInformation("[QuestSync] {Pak}: .pak version {Version}, {Count} files, compression {Methods}, {Lookup}.",
            System.IO.Path.GetFileName(path), _version, entryCount, string.Join("/", _methods.Where(m => m.Length > 0)),
            _directory is not null ? "with a directory index" : _pathHashes is not null ? "path hashes only" : "no index");
    }

    public string MethodName(int method) => method == 0 ? "not compressed" : method - 1 < _methods.Length ? _methods[method - 1] : $"method {method}";

    /// <summary>The entry for a path inside the game ("Localization/Game/en/Game.locres"), wherever it is mounted.</summary>
    public Entry? Find(string relative)
    {
        relative = relative.Replace('\\', '/').TrimStart('/');
        if (_directory is not null)
        {
            foreach (var (path, encoded) in _directory)
                if (path.EndsWith("/" + relative, StringComparison.OrdinalIgnoreCase) || path.Equals(relative, StringComparison.OrdinalIgnoreCase))
                    return Resolve(path, encoded);
            return null;
        }
        if (_pathHashes is not null)
        {
            // Without a directory index, only an exact path from the mount point can be looked up. The remaster
            // mounts its pak at "../../../", with the game's files under "OblivionRemastered/Content/".
            foreach (var candidate in new[] { "OblivionRemastered/Content/" + relative, relative, "Engine/Content/" + relative })
                if (_pathHashes.TryGetValue(HashPath(candidate), out var encoded))
                    return Resolve(candidate, encoded);
        }
        return null;
    }

    private Entry Resolve(string path, int encoded)
    {
        if (encoded >= 0)
            return DecodeEntry(encoded, path);
        var plain = _plain[-encoded - 1];
        return plain with { Path = path };
    }

    /// <summary>The file's bytes, decompressed.</summary>
    public byte[] Read(Entry entry)
    {
        if (entry.Encrypted)
            throw new InvalidDataException($"{entry.Path} is encrypted");
        // The entry is written again just before its data, with its compression blocks.
        var header = new BinReader(ReadAt(entry.Offset, 64 * 1024 > _file.Length - entry.Offset ? (int)(_file.Length - entry.Offset) : 64 * 1024));
        ReadEntry(header, entry.Path, out var blocks, out var blockSize);
        var dataStart = entry.Offset + header.Pos;
        if (entry.Method == 0)
            return ReadAt(dataStart, checked((int)entry.Size));

        var method = MethodName(entry.Method);
        var output = new byte[entry.UncompressedSize];
        var written = 0;
        var chunk = blocks.Count == 1 ? (int)entry.UncompressedSize : (int)blockSize;
        foreach (var (start, end) in blocks)
        {
            // Block offsets count from the entry's start (since version 5).
            var compressed = ReadAt(entry.Offset + start, checked((int)(end - start)));
            var want = Math.Min(chunk, output.Length - written);
            var got = method.ToLowerInvariant() switch
            {
                "zlib" => Inflate(new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress), output, written, want),
                "gzip" => Inflate(new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress), output, written, want),
                "oodle" => Oodle.Decompress(compressed, output, written, want, _logger),
                _ => throw new InvalidDataException($"{entry.Path} uses {method} compression, which isn't supported"),
            };
            if (got != want)
                throw new InvalidDataException($"{method} gave {got} bytes instead of {want}");
            written += want;
        }
        return output;
    }

    private static int Inflate(Stream stream, byte[] output, int at, int count)
    {
        using (stream)
        {
            var total = 0;
            while (total < count)
            {
                var n = stream.Read(output, at + total, count - total);
                if (n == 0)
                    break;
                total += n;
            }
            return total;
        }
    }

    private Entry ReadEntry(BinReader r, string path) => ReadEntry(r, path, out _, out _);

    /// <summary>A full entry record (in the index before version 10, and before each file's data).</summary>
    private Entry ReadEntry(BinReader r, string path, out List<(long Start, long End)> blocks, out uint blockSize)
    {
        var offset = r.I64();
        var size = r.I64();
        var uncompressed = r.I64();
        var method = (int)r.U32();
        r.Pos += 20;                                 // hash
        blocks = new List<(long, long)>();
        if (method != 0)
        {
            var count = r.I32();
            for (var i = 0; i < count; i++)
                blocks.Add((r.I64(), r.I64()));
        }
        var flags = r.U8();
        blockSize = r.U32();
        return new Entry(path, offset, size, uncompressed, method, (flags & 1) != 0);
    }

    /// <summary>An entry from the compact encoding used since version 10.</summary>
    private Entry DecodeEntry(int at, string path)
    {
        var r = new BinReader(_encoded) { Pos = at };
        var bits = r.U32();
        var method = (int)((bits >> 23) & 0x3f);
        var encrypted = (bits & (1u << 22)) != 0;
        if ((bits & 0x3f) == 0x3f)
            r.U32();                                 // compression block size
        long VarInt(int bit) => (bits & (1u << bit)) != 0 ? r.U32() : r.I64();
        var offset = VarInt(31);
        var uncompressed = VarInt(30);
        var size = method == 0 ? uncompressed : VarInt(29);
        return new Entry(path, offset, size, uncompressed, method, encrypted);
    }

    private static Dictionary<ulong, int> ReadPathHashIndex(byte[] data)
    {
        var r = new BinReader(data);
        var count = r.U32();
        var map = new Dictionary<ulong, int>((int)Math.Min(count, 1_000_000));
        for (var i = 0; i < count; i++)
            map[(ulong)r.I64()] = r.I32();
        return map;
    }

    private static Dictionary<string, int> ReadDirectoryIndex(byte[] data)
    {
        var r = new BinReader(data);
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var dirs = r.U32();
        for (var d = 0; d < dirs; d++)
        {
            var dir = r.FString().Replace('\\', '/');
            var files = r.U32();
            for (var f = 0; f < files; f++)
            {
                var name = r.FString();
                var encoded = r.I32();
                var full = (dir == "/" ? "" : dir.TrimStart('/')) + name;
                map[full] = encoded;
            }
        }
        return map;
    }

    /// <summary>FNV-1a 64 over the lower-case path in UTF-16, the seed added to the offset basis (versions 11+).</summary>
    private ulong HashPath(string relative)
    {
        var bytes = Encoding.Unicode.GetBytes(relative.ToLowerInvariant());
        var hash = 0xcbf29ce484222325UL + (ulong)_pathHashSeed;
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= 0x00000100000001b3UL;
        }
        return hash;
    }

    private byte[] ReadAt(long offset, int count)
    {
        var buffer = new byte[count];
        _file.Position = offset;
        _file.ReadExactly(buffer);
        return buffer;
    }

    public void Dispose() => _file.Dispose();
}

/// <summary>Oodle decompression through oo2core_9_win64.dll, when one is on this computer.</summary>
internal static class Oodle
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate long DecompressFn(byte[] input, long inputSize, byte[] output, long outputSize, int fuzzSafe, int checkCrc,
        int verbosity, IntPtr decodeBufferBase, long decodeBufferSize, IntPtr callback, IntPtr callbackUserData,
        IntPtr decoderMemory, long decoderMemorySize, int threadPhase);

    /// <summary>The game's folder, also searched for the DLL.</summary>
    public static string? GameFolder;

    private static DecompressFn? _decompress;
    private static bool _looked;

    public static int Decompress(byte[] input, byte[] output, int at, int count, ILogger logger)
    {
        if (!_looked)
        {
            _looked = true;
            foreach (var path in Candidates())
            {
                try
                {
                    var lib = NativeLibrary.Load(path);
                    _decompress = Marshal.GetDelegateForFunctionPointer<DecompressFn>(NativeLibrary.GetExport(lib, "OodleLZ_Decompress"));
                    logger.LogInformation("[QuestSync] Using {Dll} for Oodle.", path);
                    break;
                }
                catch (Exception e)
                {
                    logger.LogInformation("[QuestSync] Couldn't load {Dll}: {Error}", path, e.Message);
                }
            }
        }
        if (_decompress is null)
            throw new InvalidDataException("the game's text is Oodle-compressed and there's no oo2core_9_win64.dll on this computer. " +
                "Put one next to the server's CoopQuestSync.Server.dll, or make strings.json by hand (docs/INSTALL.md)");
        var buffer = new byte[count];
        var got = _decompress(input, input.Length, buffer, count, 1, 1, 0, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, 3);
        if (got > 0)
            Array.Copy(buffer, 0, output, at, (int)got);
        return (int)got;
    }

    private static IEnumerable<string> Candidates()
    {
        var here = Path.GetDirectoryName(typeof(Oodle).Assembly.Location) ?? ".";
        var dirs = new List<string> { here, AppContext.BaseDirectory };
        if (GameFolder is not null)
        {
            dirs.Add(Path.Combine(GameFolder, "OblivionRemastered", "Binaries", "Win64"));
            dirs.Add(Path.Combine(GameFolder, "Engine", "Binaries", "ThirdParty", "Oodle", "Win64"));
            dirs.Add(GameFolder);
        }
        foreach (var dir in dirs.Distinct())
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "oo2core_*_win64.dll"))
                    yield return f;
    }
}

/// <summary>Little-endian reader over a byte array, with Unreal's FString.</summary>
internal sealed class BinReader(byte[] data)
{
    public long Pos;

    public byte U8() => data[Pos++];

    public uint U32()
    {
        var v = BitConverter.ToUInt32(data, (int)Pos);
        Pos += 4;
        return v;
    }

    public int I32() => unchecked((int)U32());

    public long I64()
    {
        var v = BitConverter.ToInt64(data, (int)Pos);
        Pos += 8;
        return v;
    }

    public byte[] Bytes(int count)
    {
        var b = data.AsSpan((int)Pos, count).ToArray();
        Pos += count;
        return b;
    }

    public string FString()
    {
        var n = I32();
        if (n == 0)
            return "";
        if (n > 0)
        {
            var s = Encoding.Latin1.GetString(data, (int)Pos, n - 1);
            Pos += n;
            return s;
        }
        n = -n;
        var u = Encoding.Unicode.GetString(data, (int)Pos, 2 * (n - 1));
        Pos += 2 * n;
        return u;
    }
}
