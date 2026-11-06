"""Minimal TES4 (Oblivion) plugin reader."""
import struct, zlib, sys

class Record:
    __slots__ = ("type", "flags", "formid", "subs", "offset")
    def __init__(self, type, flags, formid, subs, offset):
        self.type, self.flags, self.formid, self.subs, self.offset = type, flags, formid, subs, offset
    def get(self, t):
        for k, v in self.subs:
            if k == t:
                return v
        return None
    def all(self, t):
        return [v for k, v in self.subs if k == t]
    @property
    def edid(self):
        v = self.get("EDID")
        return v.rstrip(b"\0").decode("cp1252") if v else None

def parse_subs(data):
    subs, i, big = [], 0, None
    while i < len(data):
        t = data[i:i+4].decode("ascii", "replace")
        sz = struct.unpack_from("<H", data, i + 4)[0]
        i += 6
        if t == "XXXX":
            big = struct.unpack_from("<I", data, i)[0]
            i += sz
            continue
        if big is not None:
            sz, big = big, None
        subs.append((t, data[i:i+sz]))
        i += sz
    return subs

def iter_records(buf, want=None, start=0, end=None):
    """Yield Records (recursing into groups). want: set of types to decode."""
    i = start
    end = len(buf) if end is None else end
    while i < end:
        t = buf[i:i+4].decode("ascii", "replace")
        if t == "GRUP":
            gsize = struct.unpack_from("<I", buf, i + 4)[0]
            label = buf[i+8:i+12]
            gtype = struct.unpack_from("<i", buf, i + 12)[0]
            # top-level groups: skip if label not wanted
            if gtype == 0 and want is not None and label.decode("ascii", "replace") not in want:
                i += gsize
                continue
            yield from iter_records(buf, want, i + 20, i + gsize)
            i += gsize
            continue
        dsize, flags, formid = struct.unpack_from("<III", buf, i + 4)
        data = buf[i+20:i+20+dsize]
        if want is None or t in want:
            if flags & 0x00040000:
                data = zlib.decompress(data[4:])
            yield Record(t, flags, formid, parse_subs(data), i)
        i += 20 + dsize

def load(path):
    with open(path, "rb") as f:
        return f.read()

if __name__ == "__main__":
    buf = load(sys.argv[1])
    from collections import Counter
    c = Counter()
    for r in iter_records(buf, want=set(sys.argv[2:]) or None):
        c[r.type] += 1
    print(c.most_common())
