"""Read, tag and patch compiled Oblivion result scripts (quest stages and dialogue lines).

A compiled script is a flat list of statements:
  * opcode (u16), length (u16), body              most statements
  * 0x1C (u16), ref index (u16), opcode, length, body   a call on a reference (player.AddItem ...)
if / elseif / else carry a jump: the number of statements up to the next branch of the same block.

That is enough to work on any vanilla result script without a full compiler: we tag the statements
whose effect lands on the player (teleports, combat, items, status, faction ranks, cut scenes) and can
wrap each one in a guard, `if CQSReplay == 0 || player.GetItemCount CQSAllowX > 0 ... endif`, fixing
the jumps of the blocks around it.
"""
import struct

PLAYER = 0x14
IF, ELSE, ELSEIF, ENDIF, REFCALL = 0x16, 0x17, 0x18, 0x19, 0x1C

# The six categories a stage effect can fall in. Their order is fixed: it is also the order of the
# CQSAllow* control items and of the switches on the web page.
CATEGORIES = ["teleport", "combat", "items", "status", "ranks", "cutscene"]

# opcode -> (category, rule)
#   'self'  : counts when the call is on the player (player.AddItem)
#   'param' : counts when the player is the call's target or one of its parameters (NPC.StartCombat player)
#   'any'   : always counts (no reference, or acts on the player by definition)
OPS = {
    0x109E: ("teleport", "param"),   # MoveTo / MoveToMarker
    0x1007: ("teleport", "self"),    # SetPos
    0x1008: ("teleport", "self"),    # SetAngle (part of placing the player)
    0x1016: ("combat", "any"),       # StartCombat
    0x1017: ("combat", "any"),       # StopCombat
    0x108B: ("combat", "self"),      # Kill, on the player only
    0x1002: ("items", "self"),       # AddItem
    0x1052: ("items", "self"),       # RemoveItem
    0x10AD: ("items", "param"),      # RemoveAllItems (from the player, or into the player)
    0x10EE: ("items", "self"),       # EquipItem
    0x10EF: ("items", "self"),       # UnequipItem
    0x101C: ("status", "self"),      # AddSpell
    0x101D: ("status", "self"),      # RemoveSpell
    0x101E: ("status", "param"),     # Cast (at the player)
    0x100F: ("status", "self"),      # SetAV
    0x1010: ("status", "self"),      # ModAV
    0x10EC: ("status", "self"),      # SetGhost
    0x10F3: ("status", "self"),      # SetRestrained
    0x10F1: ("status", "self"),      # SetUnconscious
    0x10F0: ("status", "self"),      # SetClass
    0x1133: ("status", "self"),      # SetLevel
    0x1075: ("status", "self"),      # SetCrimeGold
    0x1076: ("status", "self"),      # ModCrimeGold
    0x110F: ("status", "any"),       # ModPCSkill
    0x10F8: ("status", "any"),       # ModPCFame
    0x10FA: ("status", "any"),       # ModPCInfamy
    0x1137: ("status", "any"),       # ModPCMiscStat
    0x1089: ("ranks", "self"),       # SetFactionRank
    0x10C2: ("ranks", "any"),        # SetPCExpelled
    0x10C4: ("ranks", "any"),        # SetPCFactionMurder
    0x10C6: ("ranks", "any"),        # SetPCFactionSteal
    0x1060: ("cutscene", "any"),     # EnablePlayerControls
    0x1061: ("cutscene", "any"),     # DisablePlayerControls
    0x1114: ("cutscene", "any"),     # PlayBink
    0x10D9: ("cutscene", "any"),     # ShowClassMenu
    0x10E7: ("cutscene", "any"),     # SetInCharGen
    0x1056: ("cutscene", "param"),   # StartConversation (with the player)
    0x1034: ("cutscene", "param"),   # SayTo (the player)
    0x1111: ("cutscene", "any"),     # EnableFastTravel
    0x115E: ("cutscene", "any"),     # AutoSave
}


class Stmt:
    __slots__ = ("op", "ref", "raw")

    def __init__(self, op, ref, raw):
        self.op, self.ref, self.raw = op, ref, raw

    @property
    def body(self):
        """The parameter bytes after opcode and length."""
        return self.raw[8:] if self.ref is not None else self.raw[4:]


def split(code):
    out, i = [], 0
    while i < len(code):
        op = struct.unpack_from("<H", code, i)[0]
        if op == REFCALL:
            ref, op2, ln = struct.unpack_from("<HHH", code, i + 2)
            out.append(Stmt(op2, ref, code[i:i + 8 + ln]))
            i += 8 + ln
        else:
            ln = struct.unpack_from("<H", code, i + 2)[0]
            out.append(Stmt(op, None, code[i:i + 4 + ln]))
            i += 4 + ln
    if i != len(code):
        raise ValueError("statement lengths do not add up")
    return out


def branch_end(stmts, i):
    """Index of the elseif/else/endif that closes the branch starting at stmts[i]."""
    depth, j = 0, i + 1
    while True:
        op = stmts[j].op if stmts[j].ref is None else None
        if op == IF:
            depth += 1
        elif op == ENDIF:
            if depth == 0:
                return j
            depth -= 1
        elif op in (ELSE, ELSEIF) and depth == 0:
            return j
        j += 1


def with_jump(st, jump):
    raw = bytearray(st.raw)
    struct.pack_into("<H", raw, 4, jump)
    return Stmt(st.op, None, bytes(raw))


def relink(stmts):
    """Recomputes every jump from the block structure."""
    out = list(stmts)
    for i, st in enumerate(out):
        if st.ref is None and st.op in (IF, ELSE, ELSEIF):
            out[i] = with_jump(st, branch_end(out, i) - i - 1)
    return out


def check_jumps(stmts):
    for i, st in enumerate(stmts):
        if st.ref is None and st.op in (IF, ELSE, ELSEIF):
            if struct.unpack_from("<H", st.raw, 4)[0] != branch_end(stmts, i) - i - 1:
                return False
    return True


def ref_indexes_in(body):
    """Ref indexes that appear as form parameters (0x72 + u16) in a statement body."""
    out, i = set(), 0
    while True:
        i = body.find(b"\x72", i)
        if i < 0 or i + 3 > len(body):
            return out
        out.add(struct.unpack_from("<H", body, i + 1)[0])
        i += 1


def category(st, refs):
    """The effect category of one statement, or None. refs: form ids in SCRO order."""
    rule = OPS.get(st.op)
    if rule is None:
        return None
    cat, how = rule
    is_player = lambda idx: 0 < idx <= len(refs) and refs[idx - 1] == PLAYER
    on_player = st.ref is not None and is_player(st.ref)
    if how == "any":
        return cat
    if how == "self":
        return cat if on_player else None
    return cat if on_player or any(is_player(x) for x in ref_indexes_in(st.body)) else None


def categories(code, refs):
    try:
        return {c for st in split(code) if (c := category(st, refs))}
    except (ValueError, struct.error, IndexError):
        return set()


def guard_condition(idx_replay, idx_player, idx_allow):
    """RPN for: CQSReplay == 0 || player.GetItemCount CQSAllowX > 0"""
    getcount = (b"\x72" + struct.pack("<H", idx_player) + b"\x58" + struct.pack("<HH", 0x102F, 5)
                + struct.pack("<H", 1) + b"\x72" + struct.pack("<H", idx_allow))
    parts = [b"\x47" + struct.pack("<H", idx_replay), b"0", b"==", getcount, b"0", b">", b"||"]
    return b"".join(b" " + p for p in parts)


def make_if(cond):
    body = struct.pack("<HH", 1, len(cond)) + cond
    return Stmt(IF, None, struct.pack("<HH", IF, len(body)) + body)


ENDIF_STMT = Stmt(ENDIF, None, b"\x19\x00\x00\x00")


def guard(code, refs, ids):
    """
    Wraps every tagged statement in a guard. ids: {'replay': form id of the CQSReplay global,
    'allow': {category: form id of its CQSAllow item}}.
    Returns (new code, new refs, categories found); the code is unchanged when nothing is tagged.
    """
    stmts = split(code)
    if not check_jumps(stmts):
        raise ValueError("jumps do not match the block structure")
    refs = list(refs)

    def idx(fid):
        if fid not in refs:
            refs.append(fid)
        return refs.index(fid) + 1

    out, found = [], set()
    for st in stmts:
        cat = category(st, refs)
        if cat is None:
            out.append(st)
            continue
        found.add(cat)
        cond = guard_condition(idx(ids["replay"]), idx(PLAYER), idx(ids["allow"][cat]))
        out += [make_if(cond), st, ENDIF_STMT]
    if not found:
        return code, list(refs), found
    out = relink(out)
    new = b"".join(s.raw for s in out)
    assert check_jumps(split(new))
    return new, refs, found


def player_item_calls(code, refs, opcode, toplevel_only=False):
    """(form id, count) for every `player.AddItem <form> <number>` (or RemoveItem: opcode) in the script.
    toplevel_only skips the ones inside an if block, which may or may not run."""
    out, depth = [], 0
    for st in split(code):
        if st.ref is None and st.op == IF:
            depth += 1
        elif st.ref is None and st.op == ENDIF:
            depth -= 1
        if toplevel_only and depth > 0:
            continue
        if st.op == opcode and st.ref is not None and 0 < st.ref <= len(refs) and refs[st.ref - 1] == PLAYER:
            b = st.body
            if len(b) >= 10 and b[2] == 0x72 and b[5] == 0x6E:
                ri = struct.unpack_from("<H", b, 3)[0]
                cnt = struct.unpack_from("<i", b, 6)[0]
                if 0 < ri <= len(refs):
                    if not isinstance(refs[ri - 1], tuple):
                        out.append((refs[ri - 1], cnt))
    return out


def additems(code, refs):
    return player_item_calls(code, refs, 0x1002)
