"""Generate CoopQuestSync.esp, CoopQuestSyncDialogue.esp and the tables the server embeds.

    python3 -I plugin/tools/gen_plugin.py <Oblivion.esm> <output dir> [--plugins Knights.esp,DLCx.esp,...] [--only MQ01,MQ02]

--plugins lists more game plugins in load order (paths, or names next to Oblivion.esm). Quests and dialogue are read
from every one of them, and where two edit the same record the later one wins, the way the game loads them.

CoopQuestSync.esp
  For every vanilla quest that has stages (minus SKIP_QUESTS):
  * one hidden token item per stage (unplayable ring, weight 0, value 0)
  * a small start-game-enabled quest whose script mirrors the stages both ways:
      - a stage that is done in the game but has no token gets its token (game -> server)
      - a token with no done stage gets SetStage, one stage per run (server -> game)
  * an override of the quest itself where a stage's result script does something to the player. Each such
    statement (a teleport, combat, items, a status change, a faction rank, a cut scene) is wrapped in
        if CQSReplay == 0 || player.GetItemCount CQSAllow<Category> > 0
    CQSReplay is only above 0 while our scripts set stages, so the player who reached the stage in their own game
    always gets every effect. When the stage arrives from someone else, the server decides per category, by giving
    or taking the CQSAllow items (the switches on the web page).

  Control items and globals:
  * CQSBeacon: always in the inventory, so the server can find the plugin's load order slot.
  * CQSSeq: the server bumps its count whenever it pushes tokens; the quest scripts rescan when it changes.
  * CQSGo (global): 0 when a game session starts. Nothing is replayed until the server bumps CQSSeq,
    which tells the plugin the server has seen the inventory as it was before any replay.
  * CQSAck: the plugin adds one when it gets that go signal, so the server knows it arrived.
  * CQSBusy: held while stages are being replayed. The server uses it to undo rewards that a
    replay after reconnecting would hand out a second time.
  * CQSReplay (global): counts down after the last SetStage; the batch ends when it reaches 0.
  * CQSAllow*: one per effect category (see scriptpatch.CATEGORIES).
  * CQSFreeze: while the server gives it, the player's controls are off (watching someone's conversation).
  * Two Message lines frame each batch. The CoopQuestSyncUI Lua add-on hides every HUD
    notification between them, so a catch-up on join is silent.

CoopQuestSyncDialogue.esp (optional, separate so it can be left out if it clashes with another dialogue mod)
  Overrides every line of player dialogue (topics) to add `player.AddItem <its own token> 1` to its result script.
  The server takes the token back at once and shows the line to the other players.
  While the player has a menu from DLG_MENUS open (barter, persuasion, training... or lockpicking), its CQSMenu*
  token is in the inventory, so the server can tell the others what the talker is doing.
"""
import json, os, re, struct, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tes4, tes4write, obscript, scriptpatch

PLUGIN = "CoopQuestSync.esp"
DIALOGUE_PLUGIN = "CoopQuestSyncDialogue.esp"
BASE = 0xC05000           # unusual local ids so the server can spot our forms
BEACON, SEQ, BUSY, ACK, GLOB_GO, GLOB_REPLAY = (BASE + i for i in range(6))
ALLOW = {c: BASE + 0x100 + i for i, c in enumerate(scriptpatch.CATEGORIES)}
FREEZE = BASE + 0x110
FIRST_FREE = BASE + 0x1000   # 0.2 test builds used ids below this; never reuse them
LEGACY = range(BASE + 0x10, BASE + 0x30)  # token ids the 0.2 test build handed out; kept as inert items
DLG_BEACON = 0xC08000        # the dialogue plugin's own beacon
DLG_FIRST = 0xC10000         # dialogue line tokens
HERE = os.path.dirname(os.path.abspath(__file__))
IDS_FILE = os.path.join(HERE, "..", "token-ids.json")
DLG_IDS_FILE = os.path.join(HERE, "..", "dialogue-ids.json")
TOKEN_NAME = "Co-op quest token"
MSG_START = "Co-op: syncing quest progress..."
MSG_END = "Co-op: quest progress synced"
REPLAY_TICKS = 6          # control script runs every 0.5 s, so a batch ends 3 s after its last SetStage
STAGES_PER_SCRIPT = 20    # keeps each compiled script well under the size of the largest vanilla ones

# Quests never synced: character creation and the E3 demo quest.
SKIP_QUESTS = {"charactergen", "e3"}
# Quests that stay local: mirrored so the server can show them, but never shared or replayed.
# MQ01 ("Tutorial") is the help pop-up system, not a real quest: each stage is a modal tip.
LOCAL_ONLY = {"mq01"}
RESCAN_EVERY = 30         # quest scripts do a full check every 30 runs (about 30 s) even if nothing changed
WARMUP_RUNS = 8           # the control script waits 8 runs (4 s of gameplay) after loading before replaying
TOPIC = 0                 # DIAL type: a topic the player picks (greetings included)
GET_IS_ID = 72            # condition function: the speaker is this NPC


def clean(name):
    return "".join(ch for ch in name if ch.isalnum() or ch == "_")


# ---------------------------------------------------------------------------------------------------------------
# Reading the game's plugins

class Source:
    """One game plugin, with its masters, so its form ids can be put into our own numbering."""

    def __init__(self, path):
        self.path = path
        self.name = os.path.basename(path)
        self.buf = tes4.load(path)
        hdr = next(tes4.iter_records(self.buf, want={"TES4"}, end=self._header_end()))
        self.masters = [v.rstrip(b"\0").decode("cp1252") for k, v in hdr.subs if k == "MAST"]

    def _header_end(self):
        return 20 + struct.unpack_from("<I", self.buf, 4)[0]

    def owner(self, formid):
        """The plugin a form id of this file belongs to."""
        i = formid >> 24
        return self.masters[i] if i < len(self.masters) else self.name


class LoadOrder:
    """The game's plugins in load order. Our plugins list all of them as masters, in the same order."""

    def __init__(self, paths):
        self.sources = [Source(p) for p in paths]
        self.masters = [s.name for s in self.sources]
        self.mod = len(self.masters) << 24          # our own forms

    def remap(self, src, formid):
        """A form id as written in src -> the same form in our numbering."""
        owner = src.owner(formid)
        if owner not in self.masters:
            return None
        return (self.masters.index(owner) << 24) | (formid & 0x00FFFFFF)

    def winning(self, types):
        """Every record of these types, the last plugin's version winning, in first-seen order."""
        out = {}
        for src in self.sources:
            for r in tes4.iter_records(src.buf, want=set(types)):
                if r.type not in types:
                    continue
                key = self.remap(src, r.formid)
                if key is None:
                    continue
                out[key] = (src, r)
        return out

    def dialogue(self):
        """{topic id: (src, DIAL record, {info id: (src, INFO record)})} with winners resolved."""
        topics = {}
        for src in self.sources:
            current = None
            for r in tes4.iter_records(src.buf, want={"DIAL", "INFO"}):
                if r.type == "DIAL":
                    key = self.remap(src, r.formid)
                    prev = topics.get(key)
                    topics[key] = (src, r, prev[2] if prev else {})
                    current = key
                elif r.type == "INFO" and current is not None:
                    topics[current][2][self.remap(src, r.formid)] = (src, r)
        return topics


def remap_record(lo, src, rec, form_subs, script_groups=True):
    """
    Rewrites the form ids inside a record copied from src into our numbering. form_subs: subrecords that are
    a single form id. Condition parameters are remapped when they point into a plugin (a value whose top byte
    names a plugin other than the first), which leaves plain numbers alone.
    """
    if src is lo.sources[0] and len(src.masters) == 0:
        return list(rec.subs)            # Oblivion.esm: its numbering is already ours
    out = []
    for k, v in rec.subs:
        if k in form_subs and len(v) >= 4:
            fid = lo.remap(src, struct.unpack_from("<I", v)[0])
            v = struct.pack("<I", fid if fid is not None else 0) + v[4:]
        elif k == "CTDA" and len(v) >= 20:
            b = bytearray(v)
            for off in (12, 16):
                val = struct.unpack_from("<I", b, off)[0]
                if val >> 24 and val >> 24 <= len(src.masters):
                    fid = lo.remap(src, val)
                    if fid is not None:
                        struct.pack_into("<I", b, off, fid)
            v = bytes(b)
        out.append((k, v))
    return out


# ---------------------------------------------------------------------------------------------------------------
# Script text

def quest_script(q, part, tokens):
    """tokens: list of (stage, token editor id)."""
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
    for stage, tok in tokens:
        lines += [f"if GetStageDone {qe} {stage} == 1",
                  f"if player.GetItemCount {tok} == 0",
                  f"player.AddItem {tok} 1",
                  "endif"]
        if q["sync"]:
            # CQSReplay goes up before SetStage, so the stage's patched result script knows the stage came from
            # the server and checks the CQSAllow switches.
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
    "scn CQSControlScript", "float fQuestDelayTime", "short init", "short seq0", "short runs", "short frozen",
    "begin GameMode",
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
    # Watching someone else's conversation: the server holds CQSFreeze while the player should stand still.
    "if player.GetItemCount CQSFreeze > 0",
    "if frozen == 0",
    "set frozen to 1",
    "DisablePlayerControls",
    "endif",
    "elseif frozen == 1",
    "set frozen to 0",
    "EnablePlayerControls",
    "endif",
    "end"]) + "\r\n"

# Menus the talker can open from a conversation (and lockpicking), by the game's menu codes. While one is open, the
# dialogue plugin keeps its token in the inventory, so the server can tell the witnesses "Alice is bartering".
# Barter is the container menu: the server only calls it bartering during a conversation.
DLG_MENUS = [("barter", 1008), ("persuade", 1034), ("training", 1028), ("repair", 1035), ("spells", 1037),
             ("recharge", 1049), ("spellmaking", 1041), ("enchanting", 1042), ("lockpick", 1014)]


def dlg_menu_edid(name):
    return "CQSMenu" + name.capitalize()


def _menu_sync():
    out = []
    for i, (name, _) in enumerate(DLG_MENUS, 1):
        e = dlg_menu_edid(name)
        out += [f"if seen == {i}", f"if player.GetItemCount {e} == 0", f"player.AddItem {e} 1", "endif",
                f"elseif player.GetItemCount {e} > 0", f"player.RemoveItem {e} 1", "endif"]
    return out


DLG_CONTROL_SCRIPT = "\r\n".join(
    ["scn CQSDialogueControlScript", "float fQuestDelayTime", "short seen",
     "begin GameMode",
     "set fQuestDelayTime to 0.5",
     "if player.GetItemCount CQSDlgBeacon == 0", "player.AddItem CQSDlgBeacon 1", "endif",
     "set seen to 0"] + _menu_sync() + ["end"]
    # The game runs the blocks in order: the ones for a given menu note it, the last one (any menu) reports it.
    + sum(([f"begin MenuMode {code}", f"set seen to {i}", "end"] for i, (_, code) in enumerate(DLG_MENUS, 1)), [])
    + ["begin MenuMode", "set fQuestDelayTime to 0.5"] + _menu_sync() + ["set seen to 0", "end"]) + "\r\n"

GLOBALS = {"CQSGo": GLOB_GO, "CQSReplay": GLOB_REPLAY}


# ---------------------------------------------------------------------------------------------------------------
# Records

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


def script_groups(subs):
    """Yields (start, end) index ranges of each embedded script: SCHR followed by SCDA/SCTX/SCRO/SCRV..."""
    i = 0
    while i < len(subs):
        if subs[i][0] == "SCHR":
            j = i + 1
            while j < len(subs) and subs[j][0] in ("SCDA", "SCTX", "SCRO", "SCRV", "SLSD", "SCVR"):
                j += 1
            yield i, j
            i = j
        else:
            i += 1


def read_script(subs, i, j):
    code, refs = b"", []
    for k, v in subs[i:j]:
        if k == "SCDA":
            code = v
        elif k == "SCRO":
            refs.append(struct.unpack_from("<I", v)[0])
        elif k == "SCRV":
            refs.append(("var", struct.unpack_from("<I", v)[0]))
    return code, refs


def write_script(subs, i, j, code, refs):
    """Replaces one embedded script's compiled data, keeping its source text and local variables."""
    old = subs[i:j]
    schr = bytearray(old[0][1])
    struct.pack_into("<I", schr, 4, len(refs))
    struct.pack_into("<I", schr, 8, len(code))
    group = [("SCHR", bytes(schr)), ("SCDA", code)]
    group += [(k, v) for k, v in old[1:] if k in ("SCTX", "SLSD", "SCVR")]
    for ref in refs:
        if isinstance(ref, tuple):
            group.append(("SCRV", struct.pack("<I", ref[1])))
        else:
            group.append(("SCRO", struct.pack("<I", ref)))
    return subs[:i] + group + subs[j:]


def patch_quest(subs, ids):
    """Guards every player effect in the quest's stage result scripts. Returns (subs, {stage: categories})."""
    effects, stage = {}, None
    i = 0
    while i < len(subs):
        k, v = subs[i]
        if k == "INDX":
            stage = struct.unpack_from("<H", v)[0]
        if k == "SCHR":
            j = i + 1
            while j < len(subs) and subs[j][0] in ("SCDA", "SCTX", "SCRO", "SCRV", "SLSD", "SCVR"):
                j += 1
            code, refs = read_script(subs, i, j)
            if code:
                new, nrefs, found = scriptpatch.guard(code, refs, ids)
                if found:
                    effects.setdefault(stage, set()).update(found)
                    subs = write_script(subs, i, j, new, nrefs)
                    j = i + 1
                    while j < len(subs) and subs[j][0] in ("SCDA", "SCTX", "SCRO", "SCRV", "SLSD", "SCVR"):
                        j += 1
            i = j
            continue
        i += 1
    return subs, effects


def stage_texts(subs):
    """Stage index -> its journal entry (the last one, which is the PC wording when there are two)."""
    out, cur = {}, None
    for k, v in subs:
        if k == "INDX":
            cur = struct.unpack("<H", v[:2])[0]
        elif k == "CNAM" and cur is not None:
            text = re.sub(r"&\w+;", "...", v.rstrip(b"\0").decode("cp1252")).strip()
            if text:
                out[cur] = text if len(text) <= 240 else text[:237] + "..."
    return out


def stage_list(subs):
    """Stage indexes that have at least one log entry or result script."""
    out = []
    for k, v in subs:
        if k == "INDX":
            s = struct.unpack("<H", v[:2])[0]
            if s not in out:
                out.append(s)
    return sorted(out)


def text_of(v):
    return v.rstrip(b"\0").decode("cp1252", "replace") if v else None


def load_ids(path, first):
    ids = {}
    if os.path.exists(path):
        with open(path) as f:
            ids = {k: int(v, 16) for k, v in json.load(f).items()}
    return ids, max([first - 1, *ids.values()]) + 1


def save_ids(path, ids):
    with open(path, "w") as f:
        json.dump({k: f"{v:06X}" for k, v in sorted(ids.items(), key=lambda kv: kv[1])}, f, indent=0)


def group_typed(label_int, gtype, records):
    body = b"".join(records)
    return b"GRUP" + struct.pack("<IIiI", 20 + len(body), label_int, gtype, 0) + body


# ---------------------------------------------------------------------------------------------------------------

def build_main(lo, outdir, only):
    quests = []
    winners = lo.winning({"QUST"})
    for key, (src, r) in winners.items():
        subs = remap_record(lo, src, r, {"SCRI", "QSTA", "SCRO"})
        stages = stage_list(subs)
        if not stages or not r.edid or r.edid.lower() in SKIP_QUESTS:
            continue
        if only and r.edid.lower() not in only:
            continue
        quests.append(dict(edid=r.edid, formid=key, subs=subs, sync=r.edid.lower() not in LOCAL_ONLY,
                           texts=stage_texts(subs), stages=stages, name=text_of(r.get("FULL"))))
    quests.sort(key=lambda q: q["formid"])

    mod = lo.mod
    names = {"cqsbeacon": mod | BEACON, "cqsseq": mod | SEQ, "cqsbusy": mod | BUSY, "cqsack": mod | ACK,
             "cqsfreeze": mod | FREEZE}
    names.update({k.lower(): mod | v for k, v in GLOBALS.items()})
    names.update({f"cqsallow{c}": mod | v for c, v in ALLOW.items()})
    for q in quests:
        names[q["edid"].lower()] = q["formid"]
    # Token ids must never change once players carry them, so they are kept in token-ids.json:
    # existing stages keep their id, new ones get the next free id.
    ids, nxt = load_ids(IDS_FILE, FIRST_FREE)

    def token_id(k):
        nonlocal nxt
        if k not in ids:
            ids[k] = nxt
            nxt += 1
        return ids[k]

    clot, scpt, qust, mapping = [], [], [], []
    for local, edid in ((BEACON, "CQSBeacon"), (SEQ, "CQSSeq"), (BUSY, "CQSBusy"), (ACK, "CQSAck"),
                        (FREEZE, "CQSFreeze")):
        clot.append(token_record(mod | local, edid))
    for c, local in ALLOW.items():
        clot.append(token_record(mod | local, f"CQSAllow{c.capitalize()}"))
    for local in LEGACY:
        clot.append(token_record(mod | local, f"CQSLegacy{local:06X}"))
    glob = [glob_record(mod | v, k) for k, v in GLOBALS.items()]

    # Quest overrides: the stage result scripts with every player effect behind a switch.
    guard_ids = {"replay": mod | GLOB_REPLAY, "allow": {c: mod | v for c, v in ALLOW.items()}}
    overrides = []
    for q in quests:
        if not q["sync"]:
            q["effects"] = {}
            continue
        subs, effects = patch_quest(q["subs"], guard_ids)
        q["effects"] = effects
        if effects:
            overrides.append(tes4write.record("QUST", q["formid"], subs))

    plan = []
    for q in quests:
        toks = []
        for st in q["stages"]:
            tid = token_id(f"{q['edid']}:{st}")
            tedid = f"CQSt{clean(q['edid'])}_{st}"
            names[tedid.lower()] = mod | tid
            toks.append((st, tedid, tid))
        for i in range(0, len(toks), STAGES_PER_SCRIPT):
            part = "" if len(toks) <= STAGES_PER_SCRIPT else f"_{i // STAGES_PER_SCRIPT + 1}"
            sid = nxt; qid = nxt + 1; nxt += 2
            plan.append((q, part, toks[i:i + STAGES_PER_SCRIPT], sid, qid))
    resolve = lambda n: names.get(n.lower())
    max_code = 0
    by_quest = {}
    for q, part, toks, sid, qid in plan:
        for st, tedid, tid in toks:
            clot.append(token_record(mod | tid, tedid))
        src = quest_script(q, part, [(st, te) for st, te, _ in toks])
        rec, size = scpt_record(mod | sid, f"CQSs{clean(q['edid'])}{part}", src, resolve)
        max_code = max(max_code, size)
        scpt.append(rec)
        qust.append(quest_record(mod | qid, f"CQSq{clean(q['edid'])}{part}", mod | sid))
        entry = by_quest.get(q["edid"])
        if entry is None:
            entry = by_quest[q["edid"]] = dict(editorId=q["edid"], formId=f"{q['formid']:08X}", name=q["name"],
                                               sync=q["sync"], stages=[])
            mapping.append(entry)
        entry["stages"] += [dict(stage=st, token=f"{tid:06X}", replay=q["sync"], text=q["texts"].get(st),
                                 effects=sorted(q["effects"].get(st, ()), key=scriptpatch.CATEGORIES.index))
                            for st, _, tid in toks]
    csid, cqid = nxt, nxt + 1; nxt += 2
    rec, _ = scpt_record(mod | csid, "CQSControlScript", CONTROL_SCRIPT, resolve)
    scpt.append(rec)
    qust.append(quest_record(mod | cqid, "CQSControl", mod | csid))

    count = len(glob) + len(clot) + len(scpt) + len(qust) + len(overrides)
    data = tes4write.header(count, nxt, "CoopQuestSync generator",
                            "Mirrors vanilla quest stages into hidden token items for OblivionMP co-op.",
                            lo.masters)
    data += (tes4write.group("GLOB", glob) + tes4write.group("SCPT", scpt)
             + tes4write.group("CLOT", clot) + tes4write.group("QUST", qust + overrides))
    if not only:
        save_ids(IDS_FILE, ids)
    with open(os.path.join(outdir, PLUGIN), "wb") as f:
        f.write(data)
    quest_items = sorted(find_quest_items(lo))
    with open(os.path.join(outdir, "tokens.json"), "w") as f:
        json.dump(dict(plugin=PLUGIN, masters=lo.masters, beacon=f"{BEACON:06X}", seq=f"{SEQ:06X}",
                       busy=f"{BUSY:06X}", ack=f"{ACK:06X}", freeze=f"{FREEZE:06X}",
                       allow={c: f"{v:06X}" for c, v in ALLOW.items()}, categories=scriptpatch.CATEGORIES,
                       legacy=[f"{BASE + 0x10:06X}", f"{BASE + 0x30:06X}"], tokenName=TOKEN_NAME,
                       questItems=[f"{x:08X}" for x in quest_items], quests=mapping), f, indent=1)
    stages = sum(len(q["stages"]) for q in quests)
    patched = sum(len(q["effects"]) for q in quests)
    print(f"{PLUGIN}: {len(quests)} quests, {stages} stages, {patched} stages with guarded effects in "
          f"{len(overrides)} quest overrides, {len(quest_items)} quest items, {count} records, {len(data)} bytes, "
          f"largest script {max_code} bytes")


def find_quest_items(lo):
    """Items some script marks with SetQuestObject: keys, letters and the like that quests need."""
    out = set()
    for key, (src, r) in lo.winning({"SCPT", "QUST", "INFO"}).items():
        subs = remap_record(lo, src, r, {"SCRO"})
        for i, j in script_groups(subs):
            code, refs = read_script(subs, i, j)
            if not code:
                continue
            try:
                stmts = scriptpatch.split(code)
            except Exception:
                continue
            for st in stmts:
                if st.op == 0x110D and len(st.body) >= 5 and st.body[2] == 0x72:
                    idx = struct.unpack_from("<H", st.body, 3)[0]
                    if 0 < idx <= len(refs) and not isinstance(refs[idx - 1], tuple):
                        out.add(refs[idx - 1])
    return out


def build_dialogue(lo, outdir, npc_names, quest_edids):
    mod = lo.mod
    ids, nxt = load_ids(DLG_IDS_FILE, DLG_FIRST)
    clot = [token_record(mod | DLG_BEACON, "CQSDlgBeacon")]
    names = {"cqsdlgbeacon": mod | DLG_BEACON}
    menus = {}
    for i, (name, _) in enumerate(DLG_MENUS, 1):
        clot.append(token_record(mod | DLG_BEACON + i, dlg_menu_edid(name)))
        names[dlg_menu_edid(name).lower()] = mod | DLG_BEACON + i
        menus[f"{DLG_BEACON + i:06X}"] = name
    groups, table, n_infos = [], {}, 0
    for topic_id, (tsrc, dial, infos) in lo.dialogue().items():
        data = dial.get("DATA")
        if not data or data[0] != TOPIC or not infos:
            continue
        topic_name = text_of(dial.get("FULL")) or dial.edid
        recs = []
        for info_id, (src, r) in infos.items():
            subs = remap_record(lo, src, r, {"QSTI", "TPIC", "NAME", "TCLT", "TCLF", "SCRO", "PNAM"})
            # NAM2 is the voice actors' direction ("Shock, then fear"): Bethesda's text, and the game never shows it.
            # Keep the field (each response is TRDT, NAM1, NAM2) but empty it.
            subs = [(k, b"\0") if k == "NAM2" else (k, v) for k, v in subs]
            if not any(k == "SCHR" for k, _ in subs):
                continue                      # old record layout (SCHD): leave alone
            key = f"info:{info_id:08X}"
            if key not in ids:
                ids[key] = nxt
                nxt += 1
            tid = mod | ids[key]
            i, j = next(script_groups(subs))
            code, refs = read_script(subs, i, j)
            refs = list(refs)
            gives, takes = [], []
            if code:
                try:
                    # Only unconditional ones: a reward inside an if block may not have been handed out.
                    gives = [[f"{fid:08X}", cnt] for fid, cnt in scriptpatch.player_item_calls(code, refs, 0x1002, True)]
                    takes = [[f"{fid:08X}", cnt] for fid, cnt in scriptpatch.player_item_calls(code, refs, 0x1052, True)]
                except Exception:
                    pass
            # player.AddItem <token> 1, appended to the line's result script.
            for fid in (scriptpatch.PLAYER, tid):
                if fid not in refs:
                    refs.append(fid)
            body = struct.pack("<H", 2) + b"\x72" + struct.pack("<H", refs.index(tid) + 1) + b"\x6e" + struct.pack("<i", 1)
            stmt = struct.pack("<HHHH", scriptpatch.REFCALL, refs.index(scriptpatch.PLAYER) + 1, 0x1002, len(body)) + body
            subs = write_script(subs, i, j, code + stmt, refs)
            recs.append(tes4write.record("INFO", info_id, subs))
            clot.append(token_record(tid, f"CQSd{ids[key]:06X}"))
            lines = [text_of(v) for k, v in subs if k == "NAM1"]
            speakers = []
            for k, v in subs:
                if k == "CTDA" and len(v) >= 16 and struct.unpack_from("<I", v, 8)[0] == GET_IS_ID and not v[0] & 0x01:
                    nm = npc_names.get(struct.unpack_from("<I", v, 12)[0])
                    if nm and nm not in speakers:
                        speakers.append(nm)
            qsti = [struct.unpack_from("<I", v)[0] for k, v in subs if k == "QSTI"]
            entry = dict(topic=topic_name, lines=[l for l in lines if l])
            if len(speakers) == 1:
                entry["speaker"] = speakers[0]
            if qsti and qsti[0] in quest_edids:
                entry["quest"] = quest_edids[qsti[0]]
            if gives:
                entry["gives"] = gives
            if takes:
                entry["takes"] = takes
            table[f"{ids[key]:06X}"] = entry
            n_infos += 1
        if recs:
            dsubs = remap_record(lo, tsrc, dial, {"QSTI", "QSTR"})
            groups.append(tes4write.record("DIAL", topic_id, dsubs) + group_typed(topic_id, 7, recs))
    resolve = lambda n: names.get(n.lower())
    sid, qid = nxt, nxt + 1
    nxt += 2
    rec, _ = scpt_record(mod | sid, "CQSDialogueControlScript", DLG_CONTROL_SCRIPT, resolve)
    count = len(clot) + 2 + n_infos + len(groups)
    data = tes4write.header(count, nxt, "CoopQuestSync generator",
                            "Reports each line of dialogue to the OblivionMP co-op server, so other players can follow the conversation.",
                            lo.masters)
    body = b"".join(groups)
    data += (tes4write.group("SCPT", [rec]) + tes4write.group("CLOT", clot)
             + tes4write.group("QUST", [quest_record(mod | qid, "CQSDialogueControl", mod | sid)])
             + b"GRUP" + struct.pack("<I", 20 + len(body)) + b"DIAL" + struct.pack("<iI", 0, 0) + body)
    save_ids(DLG_IDS_FILE, ids)
    with open(os.path.join(outdir, DIALOGUE_PLUGIN), "wb") as f:
        f.write(data)
    with open(os.path.join(outdir, "dialogue.json"), "w") as f:
        json.dump(dict(plugin=DIALOGUE_PLUGIN, beacon=f"{DLG_BEACON:06X}", menus=menus, lines=table), f, separators=(",", ":"))
    print(f"{DIALOGUE_PLUGIN}: {n_infos} dialogue lines in {len(groups)} topics, {len(data)} bytes")


def npc_table(lo):
    out = {}
    for key, (src, r) in lo.winning({"NPC_", "CREA"}).items():
        nm = text_of(r.get("FULL"))
        if nm:
            out[key] = nm
    return out


def snake(name):
    """DarkElf -> dark_elf: the remaster's voice events name races this way."""
    voice = re.sub(r"(?<=[a-z])(?=[A-Z])", "_", name).replace(" ", "_").lower()
    return {"golden_saint": "holy_saint"}.get(voice, voice)   # the game's voice folder for Golden Saints


def voice_table(lo):
    """Who speaks with which voice: NPC base id -> "<race>_<m|f>", and the same by name key (for lines whose
    speaker is only known by name). The remaster's voice events are Play_<race>_<sex>_<quest>_<topic>_<info>_<n>."""
    races = {key: r.edid for key, (src, r) in lo.winning({"RACE"}).items() if r.edid}
    by_id, by_name = {}, {}
    for key, (src, r) in lo.winning({"NPC_"}).items():
        rnam, acbs = r.get("RNAM"), r.get("ACBS")
        if not rnam or len(rnam) < 4 or not acbs:
            continue
        race = races.get(lo.remap(src, struct.unpack_from("<I", rnam)[0]))
        if not race:
            continue
        voice = f"{snake(race)}_{'f' if struct.unpack_from('<I', acbs)[0] & 1 else 'm'}"
        by_id[f"{key:08X}"] = voice
        name = text_of(r.get("FULL"))
        if name and name not in by_name:
            by_name[name] = voice
    return dict(npcs=by_id, names=by_name)


def cell_table(lo):
    """Interior cells: form id -> editor id, for `player.PositionCell` (bringing a player back inside a building)."""
    out = {}
    for key, (src, r) in lo.winning({"CELL"}).items():
        data = r.get("DATA")
        if r.edid and data and data[0] & 0x01:
            out[f"{key:08X}"] = r.edid
    return out


def main():
    esm, outdir = sys.argv[1], sys.argv[2]
    only = None
    if "--only" in sys.argv:
        only = {x.lower() for x in sys.argv[sys.argv.index("--only") + 1].split(",")}
    paths = [esm]
    if "--plugins" in sys.argv:
        for p in sys.argv[sys.argv.index("--plugins") + 1].split(","):
            paths.append(p if os.path.isabs(p) or os.path.exists(p) else os.path.join(os.path.dirname(esm), p))
    lo = LoadOrder(paths)
    os.makedirs(outdir, exist_ok=True)
    build_main(lo, outdir, only)
    if only:
        return
    cells = cell_table(lo)
    with open(os.path.join(outdir, "cells.json"), "w") as f:
        json.dump(cells, f, separators=(",", ":"), sort_keys=True)
    print(f"cells.json: {len(cells)} interior cells")
    voices = voice_table(lo)
    with open(os.path.join(outdir, "voices.json"), "w") as f:
        json.dump(voices, f, separators=(",", ":"), sort_keys=True)
    print(f"voices.json: {len(voices['npcs'])} voiced NPCs")
    npcs = npc_table(lo)
    with open(os.path.join(outdir, "npcs.json"), "w") as f:
        json.dump({f"{k:08X}": v for k, v in sorted(npcs.items())}, f, separators=(",", ":"))
    quest_edids = {key: r.edid for key, (src, r) in lo.winning({"QUST"}).items() if r.edid}
    build_dialogue(lo, outdir, npcs, quest_edids)


if __name__ == "__main__":
    main()
