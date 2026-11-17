"""Runs the menu gate script (CQSMenuGateScript's own text, from CoopQuestSync.esp) against the model of the game in
test_new_start, and checks that the packages waiting for a quest's menu stay shut while the menu is open: the
birthsign menu at Charactergen 44 (the Emperor) and the class menu at 87 (Baurus).

    python3 -I plugin/tools/test_menu_gates.py mod/Content/CoopQuestSync.esp
"""
import os, re, struct, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tes4
from test_new_start import Game, Script, ticks

GET_GLOBAL_VALUE = 74


def main():
    buf = tes4.load(sys.argv[1])
    recs = list(tes4.iter_records(buf, want={"SCPT", "GLOB", "PACK"}))
    src = next(r.get("SCTX").decode("cp1252") for r in recs if r.edid == "CQSMenuGateScript")
    globs = {r.formid: r.edid for r in recs if r.type == "GLOB"}
    failures = []

    def check(label, ok, detail=""):
        print(("ok   " if ok else "FAIL ") + label + (f"  ({detail})" if detail and not ok else ""))
        if not ok:
            failures.append(label)

    # Which global each package waits for, and the stage the script ties it to.
    gate_of = {}
    for r in recs:
        if r.type == "PACK":
            for k, v in r.subs:
                if k == "CTDA" and struct.unpack_from("<I", v, 8)[0] == GET_GLOBAL_VALUE:
                    gate_of[r.edid] = globs[struct.unpack_from("<I", v, 12)[0]].lower()
    check("the Emperor's and Baurus's force greets are gated",
          {"CGEmperorToPlayerB", "CGBaurusToPlayerB"} <= gate_of.keys(), gate_of)
    stages = {g.lower(): int(st) for st, g in re.findall(r"if GetStage Charactergen == (\d+) && (\w+) == 0", src)}
    sign_gate = next(g for g, st in stages.items() if st == 44)
    class_gate = next(g for g, st in stages.items() if st == 87)
    check("the Emperor waits for the birthsign gate", gate_of.get("CGEmperorToPlayerB") == sign_gate, gate_of)
    check("Baurus waits for the class gate", gate_of.get("CGBaurusToPlayerB") == class_gate, gate_of)

    def game(stage, sign=None, cls=None):
        g = Game(cur={"CHARACTERGEN": stage}, running={"CHARACTERGEN"})
        g.globals = {sign_gate: 0, class_gate: 0}
        g.sign, g.cls = sign, cls
        return Script(src), g

    # Birthsign menu (Charactergen 43 opens it and sets 44 at once).
    s, g = game(43)
    ticks(s, g, 300)
    check("birthsign: before the menu, shut", g.globals[sign_gate] == 0, g.globals)
    s, g = game(44)
    ticks(s, g, 300)
    check("birthsign: menu open, a new character, the Emperor waits", g.globals[sign_gate] == 0, g.globals)
    g.sign = "BirthSignThief"
    ticks(s, g, 1)
    check("birthsign: chosen, the Emperor goes on", g.globals[sign_gate] == 1, g.globals)
    check("birthsign: the class gate untouched", g.globals[class_gate] == 0, g.globals)
    s, g = game(44, sign="BirthSignLady")
    ticks(s, g, 20)
    g.sign = "BirthSignLord"
    ticks(s, g, 1)
    check("birthsign: changed, the Emperor goes on", g.globals[sign_gate] == 1, g.globals)
    s, g = game(44, sign="BirthSignLady")
    ticks(s, g, 100)
    check("birthsign: same one kept, waits a minute", g.globals[sign_gate] == 0, g.globals)
    ticks(s, g, 25)
    check("birthsign: same one kept, goes on after a minute", g.globals[sign_gate] == 1, g.globals)
    s, g = game(44)
    ticks(s, g, 610)
    check("birthsign: none ever, goes on after five minutes", g.globals[sign_gate] == 1, g.globals)
    s, g = game(50)
    ticks(s, g, 1)
    check("birthsign: past 44, open", g.globals[sign_gate] == 1, g.globals)

    # Class menu (Charactergen 87).
    s, g = game(87, sign="BirthSignThief", cls="Acrobat")
    ticks(s, g, 20)
    check("class: menu open, Baurus waits", g.globals[class_gate] == 0, g.globals)
    g.cls = "Knight"
    ticks(s, g, 1)
    check("class: chosen, Baurus goes on", g.globals[class_gate] == 1, g.globals)
    s, g = game(87, cls="Acrobat")
    ticks(s, g, 20)
    g.cls = None                  # made their own class
    ticks(s, g, 1)
    check("class: own class made, Baurus goes on", g.globals[class_gate] == 1, g.globals)
    s, g = game(87)                 # their own class already, keeps it or makes another
    ticks(s, g, 100)
    check("class: own class, waits a minute", g.globals[class_gate] == 0, g.globals)
    ticks(s, g, 25)
    check("class: own class, goes on after a minute", g.globals[class_gate] == 1, g.globals)
    s, g = game(88)
    ticks(s, g, 1)
    check("class: past 87, open", g.globals[class_gate] == 1, g.globals)

    print("all passed" if not failures else f"{len(failures)} failed")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
