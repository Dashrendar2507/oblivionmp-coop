"""Generate CoopQuestSync.esp and tokens.json from Oblivion.esm.

    python3 -I plugin/tools/gen_plugin.py <path to Oblivion.esm> <output dir> [--only MQ01,MQ02]

For every vanilla quest that has stages, the plugin gets:
  * one hidden token item per stage (unplayable ring, weight 0, value 0)
  * a small start-game-enabled quest whose script mirrors the stages both ways:
      - a stage that is done in the game but has no token gets its token (game -> server)
      - a token with no done stage gets SetStage, one stage per run (server -> game)
Plus a beacon item (so the server can find the plugin's load order slot) and a
sequence item (the server bumps its count whenever it pushes tokens, which makes the
quest scripts rescan instead of only watching GetStage).
"""
import json, os, struct, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tes4, tes4write, obscript

PLUGIN = "CoopQuestSync.esp"
MOD = 0x01000000          # our own forms: one master (Oblivion.esm), so index 01
BASE = 0xC05000           # unusual local ids so the server can spot our forms
BEACON, SEQ = BASE, BASE + 1
FIRST_FREE = BASE + 0x10
QUEST_DELAY = "1"

def clean(name):
    return "".join(ch for ch in name if ch.isalnum() or ch == "_")

def quest_script(q, tokens):
    """tokens: list of (stage, token editor id)."""
    qe = q["edid"]
    lines = [f"scn CQSs{clean(qe)}", "float fQuestDelayTime", "short init", "short last",
             "short seq", "short cur", "short c", "begin GameMode",
             f"set fQuestDelayTime to {QUEST_DELAY}",
             f"set cur to GetStage {qe}",
             "set c to player.GetItemCount CQSSeq",
             "if init == 0 || cur != last || c != seq",
             "set init to 1", "set last to cur", "set seq to c"]
    for stage, tok in tokens:
        lines += [f"if GetStageDone {qe} {stage} == 1",
                  f"if player.GetItemCount {tok} == 0",
                  f"player.AddItem {tok} 1",
                  "endif",
                  f"elseif player.GetItemCount {tok} > 0",
                  f"SetStage {qe} {stage}",
                  "set init to 0",
                  "return",
                  "endif"]
    lines += ["endif", "end"]
    return "\r\n".join(lines) + "\r\n"

CONTROL_SCRIPT = "\r\n".join([
    "scn CQSControlScript", "float fQuestDelayTime", "begin GameMode",
    "set fQuestDelayTime to 2",
    "if player.GetItemCount CQSBeacon == 0", "player.AddItem CQSBeacon 1", "endif",
    "end"]) + "\r\n"

def scpt_record(formid, edid, source, resolve):
    s = obscript.Script(resolve)
    code = s.compile(source)
    subs = [("EDID", edid),
            ("SCHR", struct.pack("<IIIIHH", 0, len(s.refs), len(code), len(s.vars), 1, 0)),
            ("SCDA", code), ("SCTX", source.encode("cp1252"))]
    for i, (name, is_int) in enumerate(s.vars, 1):
        subs.append(("SLSD", struct.pack("<I", i) + b"\0" * 12 + bytes([1 if is_int else 0]) + b"\0" * 7))
        subs.append(("SCVR", name))
    for fid in s.refs:
        subs.append(("SCRO", struct.pack("<I", fid)))
    return tes4write.record("SCPT", formid, subs), len(code)

def token_record(formid, edid, name):
    return tes4write.record("CLOT", formid, [
        ("EDID", edid), ("FULL", name),
        ("BMDT", struct.pack("<HBB", 0x0040, 0x40, 0)),   # ring slot, 0x40 = not playable
        ("MODL", "Clothes\\Ring\\Novice01.NIF"), ("MODB", struct.pack("<f", 0)),
        ("ICON", "Clothes\\Ring\\RingNovice.dds"),
        ("DATA", struct.pack("<If", 0, 0.0))])

def quest_record(formid, edid, script_id):
    return tes4write.record("QUST", formid, [
        ("EDID", edid), ("SCRI", struct.pack("<I", script_id)),
        ("DATA", bytes([0x01, 0x00]))])               # start game enabled, priority 0

def main():
    esm, outdir = sys.argv[1], sys.argv[2]
    only = None
    if "--only" in sys.argv:
        only = {x.lower() for x in sys.argv[sys.argv.index("--only") + 1].split(",")}
    buf = tes4.load(esm)
    quests = []
    for r in tes4.iter_records(buf, want={"QUST"}):
        stages = sorted({struct.unpack("<H", v[:2])[0] for k, v in r.subs if k == "INDX"})
        if not stages:
            continue
        if only and r.edid.lower() not in only:
            continue
        full = r.get("FULL")
        quests.append(dict(edid=r.edid, formid=r.formid, stages=stages,
                           name=full.rstrip(b"\0").decode("cp1252") if full else None))
    quests.sort(key=lambda q: q["formid"])

    names = {"cqsbeacon": MOD | BEACON, "cqsseq": MOD | SEQ}
    for q in quests:
        names[q["edid"].lower()] = q["formid"]
    nxt = FIRST_FREE
    clot, scpt, qust, mapping = [], [], [], []
    clot.append(token_record(MOD | BEACON, "CQSBeacon", "Co-op quest sync"))
    clot.append(token_record(MOD | SEQ, "CQSSeq", "Co-op quest sync"))
    plan = []
    for q in quests:
        toks = []
        for st in q["stages"]:
            tid = nxt; nxt += 1
            tedid = f"CQSt{clean(q['edid'])}_{st}"
            names[tedid.lower()] = MOD | tid
            toks.append((st, tedid, tid))
        sid = nxt; qid = nxt + 1; nxt += 2
        plan.append((q, toks, sid, qid))
    resolve = lambda n: names.get(n.lower())
    max_code = 0
    for q, toks, sid, qid in plan:
        for st, tedid, tid in toks:
            clot.append(token_record(MOD | tid, tedid, "Co-op quest sync"))
        src = quest_script(q, [(st, te) for st, te, _ in toks])
        rec, size = scpt_record(MOD | sid, f"CQSs{clean(q['edid'])}", src, resolve)
        max_code = max(max_code, size)
        scpt.append(rec)
        qust.append(quest_record(MOD | qid, f"CQSq{clean(q['edid'])}", MOD | sid))
        mapping.append(dict(editorId=q["edid"], formId=f"{q['formid']:08X}", name=q["name"],
                            stages=[dict(stage=st, token=f"{tid:06X}") for st, _, tid in toks]))
    csid, cqid = nxt, nxt + 1; nxt += 2
    rec, _ = scpt_record(MOD | csid, "CQSControlScript", CONTROL_SCRIPT, resolve)
    scpt.append(rec)
    qust.append(quest_record(MOD | cqid, "CQSControl", MOD | csid))

    count = len(clot) + len(scpt) + len(qust)
    data = tes4write.header(count, nxt, "CoopQuestSync generator",
                            "Mirrors vanilla quest stages into hidden token items for OblivionMP co-op.",
                            ["Oblivion.esm"])
    data += tes4write.group("SCPT", scpt) + tes4write.group("CLOT", clot) + tes4write.group("QUST", qust)
    os.makedirs(outdir, exist_ok=True)
    with open(os.path.join(outdir, PLUGIN), "wb") as f:
        f.write(data)
    with open(os.path.join(outdir, "tokens.json"), "w") as f:
        json.dump(dict(plugin=PLUGIN, beacon=f"{BEACON:06X}", seq=f"{SEQ:06X}", quests=mapping), f, indent=1)
    print(f"{len(quests)} quests, {sum(len(q['stages']) for q in quests)} stages, "
          f"{count} records, {len(data)} bytes, largest script {max_code} bytes")

if __name__ == "__main__":
    main()
