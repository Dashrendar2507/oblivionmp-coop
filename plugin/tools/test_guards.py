"""Checks every guarded effect in CoopQuestSync.esp: each tagged statement of a stage script sits alone inside
`if CQSReplay == 0 || player.GetItemCount CQSAllow<its category> > 0`, and that condition is true exactly when it
should be: always for the player's own stages (CQSReplay == 0), and for replayed stages only when the server holds the
category's switch.

    python3 -I plugin/tools/test_guards.py mod/Content/CoopQuestSync.esp mod/Content/tokens.json
"""
import json, struct, sys, os

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tes4, gen_plugin, scriptpatch as sp


def eval_guard(cond, refs, replay, inventory):
    """Evaluates the guard's RPN. refs: SCRO form ids; inventory: set of form ids held."""
    stack = []
    # The guard has a fixed shape; read it token by token.
    parts = cond.split(b" ", 1)[1]
    pos = 0
    out = []
    while pos < len(parts):
        if parts[pos] == 0x47:                                   # global
            idx = struct.unpack_from("<H", parts, pos + 1)[0]
            out.append(("glob", refs[idx - 1]))
            pos += 3
        elif parts[pos] == 0x72 and parts[pos + 3] == 0x58:      # ref.function
            ref = struct.unpack_from("<H", parts, pos + 1)[0]
            op, ln = struct.unpack_from("<HH", parts, pos + 4)
            body = parts[pos + 8:pos + 8 + ln]
            item = struct.unpack_from("<H", body, 3)[0]
            assert refs[ref - 1] == sp.PLAYER and op == 0x102F
            out.append(("count", refs[item - 1]))
            pos += 8 + ln
        else:
            end = parts.find(b" ", pos)
            end = len(parts) if end < 0 else end
            out.append(("op", parts[pos:end].decode()))
            pos = end
        if pos < len(parts) and parts[pos:pos + 1] == b" ":
            pos += 1
    for kind, v in out:
        if kind == "glob":
            stack.append(replay)
        elif kind == "count":
            stack.append(1 if v in inventory else 0)
        elif v in ("==", ">", "||"):
            b, a = stack.pop(), stack.pop()
            stack.append(int(a == b) if v == "==" else int(a > b) if v == ">" else int(bool(a) or bool(b)))
        else:
            stack.append(int(v))
    assert len(stack) == 1
    return bool(stack[0])


def main():
    esp, tokens = sys.argv[1], sys.argv[2]
    with open(tokens) as f:
        t = json.load(f)
    src = gen_plugin.Source(esp)
    mod = len(src.masters) << 24
    allow = {c: mod | int(v, 16) for c, v in t["allow"].items()}
    replay_glob = mod | (int(t["beacon"], 16) + 5)
    checked = scripts = 0
    for r in tes4.iter_records(src.buf, want={"QUST"}):
        if r.formid >> 24 == len(src.masters):
            continue                      # our own quests, not overrides
        for i, j in gen_plugin.script_groups(r.subs):
            code, refs = gen_plugin.read_script(r.subs, i, j)
            if not code:
                continue
            stmts = sp.split(code)
            assert sp.check_jumps(stmts), r.edid
            scripts += 1
            for k, st in enumerate(stmts):
                cat = sp.category(st, refs)
                if cat is None:
                    continue
                guard, end = stmts[k - 1], stmts[k + 1]
                assert guard.op == sp.IF and guard.ref is None and struct.unpack_from("<H", guard.raw, 4)[0] == 1, (r.edid, k)
                assert end.op == sp.ENDIF and end.ref is None, (r.edid, k)
                cond = guard.raw[8:]
                assert refs[struct.unpack_from("<H", cond, 2)[0] - 1] == replay_glob, (r.edid, k)
                # own stage: always runs; replayed: only with the switch
                assert eval_guard(cond, refs, 0, set())
                assert eval_guard(cond, refs, 6, {allow[cat]})
                assert not eval_guard(cond, refs, 6, set())
                assert not eval_guard(cond, refs, 6, {a for c, a in allow.items() if c != cat})
                checked += 1
    print(f"{checked} guarded effects in {scripts} stage scripts: all correct")


if __name__ == "__main__":
    main()
