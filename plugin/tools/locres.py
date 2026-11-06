"""Turn the game's localization file into strings.json for the server (key -> text).

    python3 -I plugin/tools/locres.py <Game.locres> <strings.json> [--keys mod/Content]

The remaster keeps the words of names, dialogue and journal entries out of the plugins: the plugins hold keys such
as LOC_FN_YsabelAndronicus and LOC_RT_GREETING_00061c3c_Oblivion_RESP1, and the text is in an Unreal .locres file
packed in the game's .pak files (English: Localization/Game/en/Game.locres, extracted with FModel).

--keys keeps only the keys the mod uses (from tokens.json, dialogue.json and npcs.json in that folder), which
makes the file a lot smaller.

Reads every .locres version: legacy (no header), 1 compact, 2 CRC32 hashes, 3 CityHash64 (UE 4.24 and later).
"""
import json, os, struct, sys

MAGIC = bytes.fromhex("0E147475674A03FC4A15909DC3377F1B")


class Reader:
    def __init__(self, data):
        self.d, self.i = data, 0

    def u8(self):
        self.i += 1
        return self.d[self.i - 1]

    def u32(self):
        self.i += 4
        return struct.unpack_from("<I", self.d, self.i - 4)[0]

    def i32(self):
        self.i += 4
        return struct.unpack_from("<i", self.d, self.i - 4)[0]

    def i64(self):
        self.i += 8
        return struct.unpack_from("<q", self.d, self.i - 8)[0]

    def fstring(self):
        n = self.i32()
        if n == 0:
            return ""
        if n > 0:
            s = self.d[self.i:self.i + n]
            self.i += n
            return s[:-1].decode("latin-1")
        n = -n
        s = self.d[self.i:self.i + 2 * n]
        self.i += 2 * n
        return s[:-2].decode("utf-16-le")


def parse(data):
    """{namespace: {key: text}}"""
    r = Reader(data)
    version = 0
    strings = None
    if data[:16] == MAGIC:
        r.i = 16
        version = r.u8()
        if version >= 1:
            offset = r.i64()
            if offset != -1:
                here = r.i
                r.i = offset
                strings = []
                for _ in range(r.i32()):
                    strings.append(r.fstring())
                    if version >= 2:
                        r.i32()      # reference count
                r.i = here
        if version >= 2:
            r.u32()                  # entries count
    out = {}
    for _ in range(r.u32()):
        if version >= 2:
            r.u32()                  # namespace hash
        ns = r.fstring()
        keys = out.setdefault(ns, {})
        for _ in range(r.u32()):
            if version >= 2:
                r.u32()              # key hash
            key = r.fstring()
            r.u32()                  # source string hash
            if version >= 1:
                idx = r.i32()
                keys[key] = strings[idx] if strings is not None and 0 <= idx < len(strings) else ""
            else:
                keys[key] = r.fstring()
    return out


def used_keys(folder):
    keys = set()

    def walk(v):
        if isinstance(v, str):
            if v.startswith("LOC_"):
                keys.add(v)
        elif isinstance(v, dict):
            for x in v.values():
                walk(x)
        elif isinstance(v, list):
            for x in v:
                walk(x)

    for name in ("tokens.json", "dialogue.json", "npcs.json"):
        path = os.path.join(folder, name)
        if os.path.exists(path):
            with open(path, encoding="utf-8") as f:
                walk(json.load(f))
    return keys


def main():
    src, dst = sys.argv[1], sys.argv[2]
    with open(src, "rb") as f:
        tables = parse(f.read())
    flat = {}
    for ns, keys in tables.items():
        for k, v in keys.items():
            flat.setdefault(k, v)
    if "--keys" in sys.argv:
        want = used_keys(sys.argv[sys.argv.index("--keys") + 1])
        found = {k: v for k, v in flat.items() if k in want}
        print(f"{len(found)} of the {len(want)} keys the mod uses were found ({len(flat)} entries in the file).")
        flat = found
    with open(dst, "w", encoding="utf-8") as f:
        json.dump(flat, f, ensure_ascii=False, separators=(",", ":"))
    print(f"Wrote {len(flat)} entries to {dst}. Namespaces: {', '.join(sorted(tables)[:8])}{'...' if len(tables) > 8 else ''}")


if __name__ == "__main__":
    main()
