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
for r in tes4.iter_records(buf):
    if r.edid:
        names[r.edid.lower()] = r.formid
    if r.type == "SCPT" and r.get("SCDA"):
        scripts.append(r)
res = collections.Counter()
for r in scripts:
    s = obscript.Script(lambda n: names.get(n.lower()))
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
    else:
        res["MISMATCH"] += 1
        print("mismatch:", r.edid)
print(dict(res))
sys.exit(1 if res["MISMATCH"] else 0)
