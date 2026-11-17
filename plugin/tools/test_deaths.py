"""Runs the scripted-death watch scripts (CQSDeathScript*, their own text from CoopQuestSync.esp) against a small model
of the game: a death in play gives the actor's CQSDead token once; an actor already dead when the game loaded doesn't.

    python3 -I plugin/tools/test_deaths.py mod/Content/CoopQuestSync.esp
"""
import os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tes4
from test_new_start import Game, Script, ticks


def main():
    scripts = {r.edid: r.get("SCTX").decode("cp1252") for r in tes4.iter_records(tes4.load(sys.argv[1]), want={"SCPT"})
               if r.edid and r.edid.startswith("CQSDeathScript")}
    failures = []

    def check(label, ok, detail=""):
        print(("ok   " if ok else "FAIL ") + label + (f"  ({detail})" if detail and not ok else ""))
        if not ok:
            failures.append(label)

    check("death scripts generated", len(scripts) >= 1, scripts.keys())
    src = scripts.get("CQSDeathScript1", "")
    refs = re.findall(r"(CQSRef[0-9A-F]{8})\.GetDead", src)
    check("each script watches actors", len(refs) > 1, refs)
    a, b = refs[0], refs[1]
    tok = lambda ref: "CQSDead" + ref[len("CQSRef"):]

    # Dead already when the game loaded: no token. A death in play: one token, once.
    s, g = Script(src), Game()
    g.dead.add(b)
    ticks(s, g, 12)
    check("dead at load: no token", g.inv.get(tok(b), 0) == 0, g.inv)
    check("alive: no token", g.inv.get(tok(a), 0) == 0, g.inv)
    g.dead.add(a)
    ticks(s, g, 2)
    check("dies in play: token", g.inv.get(tok(a)) == 1, g.inv)
    g.inv[tok(a)] = 0            # the server took it back
    ticks(s, g, 10)
    check("still dead: not reported again", g.inv.get(tok(a)) == 0, g.inv)
    g.dead.discard(a)            # brought back
    ticks(s, g, 2)
    g.dead.add(a)                # dies again
    ticks(s, g, 2)
    check("dies again after coming back: reported again", g.inv.get(tok(a)) == 1, g.inv)

    # A death in the first two seconds after a load counts as the save's own state.
    s, g = Script(src), Game()
    ticks(s, g, 3)
    g.dead.add(a)
    ticks(s, g, 10)
    check("dies right after the load: no token", g.inv.get(tok(a), 0) == 0, g.inv)

    print("all passed" if not failures else f"{len(failures)} failed")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
