"""Generate CoopQuestSync.esp and tokens.json from Oblivion.esm.

    python3 -I plugin/tools/gen_plugin.py <path to Oblivion.esm> <output dir> [--only MQ01,MQ02]

For every vanilla quest that has stages (minus SKIP_QUESTS), the plugin gets:
  * one hidden token item per stage (unplayable ring, weight 0, value 0)
  * a small start-game-enabled quest whose script mirrors the stages both ways:
      - a stage that is done in the game but has no token gets its token (game -> server)
      - a token with no done stage gets SetStage, one stage per run (server -> game),
        except stages in the no-replay list (teleports, cut scenes, menus)

Control items and globals:
  * CQSBeacon: always in the inventory, so the server can find the plugin's load order slot.
  * CQSSeq: the server bumps its count whenever it pushes tokens; the quest scripts rescan when it changes.
  * CQSGo (global): 0 when a game session starts. Nothing is replayed until the server bumps CQSSeq,
    which tells the plugin the server has seen the inventory as it was before any replay.
  * CQSAck: the plugin adds one when it gets that go signal, so the server knows it arrived.
  * CQSBusy: held while stages are being replayed. The server uses it to undo rewards that a
    replay after reconnecting would hand out a second time.
  * CQSReplay (global): counts down after the last SetStage; the batch ends when it reaches 0.
  * Two Message lines frame each batch. The CoopQuestSyncUI Lua add-on hides every HUD
    notification between them, so a catch-up on join is silent.
"""
import json, os, re, struct, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tes4, tes4write, obscript

PLUGIN = "CoopQuestSync.esp"
MOD = 0x01000000          # our own forms: one master (Oblivion.esm), so index 01
BASE = 0xC05000           # unusual local ids so the server can spot our forms
BEACON, SEQ, BUSY, ACK, GLOB_GO, GLOB_REPLAY = (BASE + i for i in range(6))
FIRST_FREE = BASE + 0x1000   # 0.2 test builds used ids below this; never reuse them
LEGACY = range(BASE + 0x10, BASE + 0x30)  # token ids the 0.2 test build handed out; kept as inert items
IDS_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "token-ids.json")
TOKEN_NAME = "Co-op quest token"
MSG_START = "Co-op: syncing quest progress..."
MSG_END = "Co-op: quest progress synced"
REPLAY_TICKS = 6
STAGES_PER_SCRIPT = 20    # keeps each compiled script well under the size of the largest vanilla ones          # control script runs every 0.5 s, so a batch ends 3 s after its last SetStage

# Quests never synced: character creation and the E3 demo quest.
SKIP_QUESTS = {"charactergen", "e3"}
# Quests that stay local: mirrored so the server can show them, but never shared or replayed.
# MQ01 ("Tutorial") is the help pop-up system, not a real quest: each stage is a modal tip.
LOCAL_ONLY = {"mq01"}
RESCAN_EVERY = 30         # quest scripts do a full check every 30 runs (about 30 s) even if nothing changed
WARMUP_RUNS = 8           # the control script waits 8 runs (4 s of gameplay) after loading before replaying
# A stage whose result script does one of these is mirrored to the server but never replayed:
# replaying it on join would teleport the player, play a cut scene, open a menu or empty the inventory.
NO_REPLAY = re.compile(
    r"player\s*\.\s*(moveto|positioncell|position|removeallitems|setinchargen)\b|playermoveto|"
    r"\bshow(race|class|birthsign)menu\b|\bsetinchargen\b|\bdisableplayercontrols\b|\bplaybink\b",
    re.I)

def clean(name):
    return "".join(ch for ch in name if ch.isalnum() or ch == "_")

def quest_script(q, part, tokens):
    """tokens: list of (stage, token editor id, replay)."""
    qe = q["edid"]
    lines = [f"scn CQSs{clean(qe)}{part}", "float fQuestDelayTime", "short init", "short last",
             "short seq", "short cur", "short c", "short tick", "short go", "begin GameMode",
             "if CQSReplay > 0", "set fQuestDelayTime to 0.1", "else", "set fQuestDelayTime to 1", "endif",
             f"set cur to GetStage {qe}",
             "set c to player.GetItemCount CQSSeq",
             "set tick to tick + 1",
             # CQSGo turning on must trigger a scan too: the seq bump that leads to it can arrive first.
             f"if init == 0 || cur != last || c != seq || go != CQSGo || tick >= {RESCAN_EVERY}",
             "set init to 1", "set last to cur", "set seq to c", "set go to CQSGo", "set tick to 0"]
    for stage, tok, replay in tokens:
        lines += [f"if GetStageDone {qe} {stage} == 1",
                  f"if player.GetItemCount {tok} == 0",
                  f"player.AddItem {tok} 1",
                  "endif"]
        if replay:
            lines += [f"elseif CQSGo == 1 && player.GetItemCount {tok} > 0",
                      "if CQSReplay == 0",
                      f'Message "{MSG_START}"',
                      "player.AddItem CQSBusy 1",
                      "endif",
                      f"set CQSReplay to {REPLAY_TICKS}",
                      f"SetStage {qe} {stage}",
                      "set init to 0",
                      "return"]
        lines += ["endif"]
    lines += ["endif", "end"]
    return "\r\n".join(lines) + "\r\n"

CONTROL_SCRIPT = "\r\n".join([
    "scn CQSControlScript", "float fQuestDelayTime", "short init", "short seq0", "short runs", "begin GameMode",
    "set fQuestDelayTime to 0.5",
    "if init == 0",
    "set init to 1", "set CQSGo to 0", "set CQSReplay to 0",
    "set seq0 to player.GetItemCount CQSSeq",
    "endif",
    "if player.GetItemCount CQSBeacon == 0", "player.AddItem CQSBeacon 1", "endif",
    f"if runs < {WARMUP_RUNS}",
    "set runs to runs + 1",
    "endif",
    f"if CQSGo == 0 && runs >= {WARMUP_RUNS}",
    "if player.GetItemCount CQSSeq != seq0",
    "set CQSGo to 1", "player.AddItem CQSAck 1",
    "endif",
    "endif",
    "if CQSReplay > 0",
    "set CQSReplay to CQSReplay - 1",
    "if CQSReplay == 0",
    "player.RemoveItem CQSBusy 1",
    f'Message "{MSG_END}"',
    "endif",
    "endif",
    "end"]) + "\r\n"

GLOBALS = {"CQSGo": GLOB_GO, "CQSReplay": GLOB_REPLAY}

def scpt_record(formid, edid, source, resolve):
    s = obscript.Script(resolve, GLOBALS.keys())
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

def token_record(formid, edid):
    return tes4write.record("CLOT", formid, [
        ("EDID", edid), ("FULL", TOKEN_NAME),
        ("BMDT", struct.pack("<HBB", 0x0040, 0x40, 0)),   # ring slot, 0x40 = not playable
        ("MODL", "Clothes\\Ring\\Novice01.NIF"), ("MODB", struct.pack("<f", 0)),
        ("ICON", "Clothes\\Ring\\RingNovice.dds"),
        ("DATA", struct.pack("<If", 0, 0.0))])

def glob_record(formid, edid):
    return tes4write.record("GLOB", formid, [("EDID", edid), ("FNAM", b"s"), ("FLTV", struct.pack("<f", 0.0))])

def quest_record(formid, edid, script_id):
    return tes4write.record("QUST", formid, [
        ("EDID", edid), ("SCRI", struct.pack("<I", script_id)),
        ("DATA", bytes([0x01, 0x00]))])               # start game enabled, priority 0

def stage_texts(r):
    """Stage index -> its journal entry (the last one, which is the PC wording when there are two)."""
    out, cur = {}, None
    for k, v in r.subs:
        if k == "INDX":
            cur = struct.unpack("<H", v[:2])[0]
        elif k == "CNAM" and cur is not None:
            text = re.sub(r"&\w+;", "...", v.rstrip(b"\0").decode("cp1252")).strip()
            if text:
                out[cur] = text if len(text) <= 240 else text[:237] + "..."
    return out

def stage_scripts(r):
    """Stage index -> the text of its result scripts."""
    out, cur = {}, None
    for k, v in r.subs:
        if k == "INDX":
            cur = struct.unpack("<H", v[:2])[0]
            out.setdefault(cur, "")
        elif k == "SCTX" and cur is not None:
            out[cur] += "\n".join(l.split(";")[0] for l in v.decode("cp1252").split("\n")) + "\n"
    return out

def main():
    esm, outdir = sys.argv[1], sys.argv[2]
    only = None
    if "--only" in sys.argv:
        only = {x.lower() for x in sys.argv[sys.argv.index("--only") + 1].split(",")}
    buf = tes4.load(esm)
    quests = []
    for r in tes4.iter_records(buf, want={"QUST"}):
        scripts = stage_scripts(r)
        if not scripts or r.edid.lower() in SKIP_QUESTS:
            continue
        if only and r.edid.lower() not in only:
            continue
        full = r.get("FULL")
        quests.append(dict(edid=r.edid, formid=r.formid,
                           sync=r.edid.lower() not in LOCAL_ONLY, texts=stage_texts(r),
                           stages=[(st, r.edid.lower() not in LOCAL_ONLY and not NO_REPLAY.search(scripts[st]))
                                   for st in sorted(scripts)],
                           name=full.rstrip(b"\0").decode("cp1252") if full else None))
    quests.sort(key=lambda q: q["formid"])

    names = {"cqsbeacon": MOD | BEACON, "cqsseq": MOD | SEQ, "cqsbusy": MOD | BUSY, "cqsack": MOD | ACK}
    names.update({k.lower(): MOD | v for k, v in GLOBALS.items()})
    for q in quests:
        names[q["edid"].lower()] = q["formid"]
    # Token ids must never change once players carry them, so they are kept in token-ids.json:
    # existing stages keep their id, new ones get the next free id.
    ids = {}
    if os.path.exists(IDS_FILE):
        with open(IDS_FILE) as f:
            ids = {k: int(v, 16) for k, v in json.load(f).items()}
    nxt = max([FIRST_FREE - 1, *ids.values()]) + 1
    def token_id(key):
        nonlocal nxt
        if key not in ids:
            ids[key] = nxt
            nxt += 1
        return ids[key]
    clot, scpt, qust, mapping = [], [], [], []
    for local, edid in ((BEACON, "CQSBeacon"), (SEQ, "CQSSeq"), (BUSY, "CQSBusy"), (ACK, "CQSAck")):
        clot.append(token_record(MOD | local, edid))
    for local in LEGACY:
        clot.append(token_record(MOD | local, f"CQSLegacy{local:06X}"))
    glob = [glob_record(MOD | v, k) for k, v in GLOBALS.items()]
    plan = []
    for q in quests:
        toks = []
        for st, replay in q["stages"]:
            tid = token_id(f"{q['edid']}:{st}")
            tedid = f"CQSt{clean(q['edid'])}_{st}"
            names[tedid.lower()] = MOD | tid
            toks.append((st, tedid, tid, replay))
        for i in range(0, len(toks), STAGES_PER_SCRIPT):
            part = "" if len(toks) <= STAGES_PER_SCRIPT else f"_{i // STAGES_PER_SCRIPT + 1}"
            sid = nxt; qid = nxt + 1; nxt += 2
            plan.append((q, part, toks[i:i + STAGES_PER_SCRIPT], sid, qid))
    resolve = lambda n: names.get(n.lower())
    max_code = 0
    by_quest = {}
    for q, part, toks, sid, qid in plan:
        for st, tedid, tid, _ in toks:
            clot.append(token_record(MOD | tid, tedid))
        src = quest_script(q, part, [(st, te, rp) for st, te, _, rp in toks])
        rec, size = scpt_record(MOD | sid, f"CQSs{clean(q['edid'])}{part}", src, resolve)
        max_code = max(max_code, size)
        scpt.append(rec)
        qust.append(quest_record(MOD | qid, f"CQSq{clean(q['edid'])}{part}", MOD | sid))
        entry = by_quest.get(q["edid"])
        if entry is None:
            entry = by_quest[q["edid"]] = dict(editorId=q["edid"], formId=f"{q['formid']:08X}", name=q["name"],
                                               sync=q["sync"], stages=[])
            mapping.append(entry)
        entry["stages"] += [dict(stage=st, token=f"{tid:06X}", replay=rp, text=q["texts"].get(st))
                            for st, _, tid, rp in toks]
    csid, cqid = nxt, nxt + 1; nxt += 2
    rec, _ = scpt_record(MOD | csid, "CQSControlScript", CONTROL_SCRIPT, resolve)
    scpt.append(rec)
    qust.append(quest_record(MOD | cqid, "CQSControl", MOD | csid))

    count = len(glob) + len(clot) + len(scpt) + len(qust)
    data = tes4write.header(count, nxt, "CoopQuestSync generator",
                            "Mirrors vanilla quest stages into hidden token items for OblivionMP co-op.",
                            ["Oblivion.esm"])
    data += (tes4write.group("GLOB", glob) + tes4write.group("SCPT", scpt)
             + tes4write.group("CLOT", clot) + tes4write.group("QUST", qust))
    if not only:
        with open(IDS_FILE, "w") as f:
            json.dump({k: f"{v:06X}" for k, v in sorted(ids.items(), key=lambda kv: kv[1])}, f, indent=0)
    os.makedirs(outdir, exist_ok=True)
    with open(os.path.join(outdir, PLUGIN), "wb") as f:
        f.write(data)
    with open(os.path.join(outdir, "tokens.json"), "w") as f:
        json.dump(dict(plugin=PLUGIN, beacon=f"{BEACON:06X}", seq=f"{SEQ:06X}", busy=f"{BUSY:06X}",
                       ack=f"{ACK:06X}", legacy=[f"{BASE + 0x10:06X}", f"{BASE + 0x30:06X}"], tokenName=TOKEN_NAME, quests=mapping), f, indent=1)
    stages = sum(len(q["stages"]) for q in quests)
    no_replay = sum(1 for q in quests for _, rp in q["stages"] if not rp)
    print(f"{len(quests)} quests, {stages} stages ({no_replay} not replayed), "
          f"{count} records, {len(data)} bytes, largest script {max_code} bytes")

if __name__ == "__main__":
    main()
