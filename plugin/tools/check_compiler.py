"""Compile every vanilla script that uses only the supported subset and compare with the game's bytecode.

    python3 -I plugin/tools/check_compiler.py <path to Oblivion.esm>

Variable indexes in vanilla scripts carry editing history, so a script that differs only there is
counted separately; everything else should match byte for byte.
"""
import os, struct, sys, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tes4, obscript

buf = tes4.load(sys.argv[1])
names = {}
scripts = []
globs = set()
for r in tes4.iter_records(buf):
    if r.edid:
        names[r.edid.lower()] = r.formid
        if r.type == "GLOB":
            globs.add(r.edid.lower())
    if r.type == "SCPT" and r.get("SCDA"):
        scripts.append(r)


def same_refs(code, refs, want, want_refs):
    """Equal once each reference index (after 0x72, or in a ref call's prefix) is turned into the form id it names."""
    def form(rs, idx):
        return rs[idx - 1] if 0 < idx <= len(rs) else None
    i = 0
    while i < len(code):
        if code[i] != want[i]:
            # a 2-byte index right after 0x72, or a ref call's (1C 00 <index>), differing in its low or high byte
            start = (i - 1 if i >= 1 and code[i - 1] == 0x72 else i - 2 if i >= 2 and code[i - 2] == 0x72
                     else i - 1 if i >= 2 and code[i - 2:i] == b"\x1c\x00" else None)
            if start is None:
                return False
            a = form(refs, struct.unpack_from("<H", code, start + 1)[0])
            b = form(want_refs, struct.unpack_from("<H", want, start + 1)[0])
            if a is None or a != b:
                return False
            i = start + 3
            continue
        i += 1
    return True


res = collections.Counter()
for r in scripts:
    s = obscript.Script(lambda n: names.get(n.lower()), globs)
    try:
        code = s.compile(r.get("SCTX").decode("cp1252"))
    except Exception:
        res["unsupported"] += 1
        continue
    want = r.get("SCDA")
    if code == want:
        res["match"] += 1
    elif len(code) == len(want) and all(a == b or (i > 0 and code[i-1] in (0x73, 0x66)) for i, (a, b) in enumerate(zip(code, want))):
        res["match except variable index"] += 1
    elif len(code) == len(want) and same_refs(code, s.refs, want, [struct.unpack("<I", v)[0] for k, v in r.subs if k == "SCRO"]):
        res["match except reference order"] += 1
    else:
        res["MISMATCH"] += 1
        print("mismatch:", r.edid)
print(dict(res))
sys.exit(1 if res["MISMATCH"] else 0)
