"""Minimal TES4 (Oblivion) plugin writer."""
import struct

def sub(t, data):
    if isinstance(data, str):
        data = data.encode("cp1252") + b"\0"
    assert len(t) == 4
    if len(data) > 0xFFFF:
        return b"XXXX" + struct.pack("<HI", 4, len(data)) + t.encode() + struct.pack("<H", 0) + data
    return t.encode() + struct.pack("<H", len(data)) + data

def record(t, formid, subs, flags=0):
    data = b"".join(sub(k, v) for k, v in subs)
    return t.encode() + struct.pack("<IIII", len(data), flags, formid, 0) + data

def group(label, records):
    body = b"".join(records)
    return b"GRUP" + struct.pack("<I", 20 + len(body)) + label.encode() + struct.pack("<iI", 0, 0) + body

def header(num_records, next_id, author, desc, masters):
    subs = [("HEDR", struct.pack("<fiI", 1.0, num_records, next_id)),
            ("CNAM", author), ("SNAM", desc)]
    for m in masters:
        subs += [("MAST", m), ("DATA", b"\0" * 8)]
    return record("TES4", 0, subs)
