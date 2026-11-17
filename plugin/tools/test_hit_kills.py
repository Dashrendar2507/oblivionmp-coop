"""Runs the kill-on-hit scripts (CQSHitKillScript*, from CoopQuestSync.esp) against the model of the game in
test_new_start: the Emperor dies once the final assassin has fought him up close for a moment, Renault likewise with
the first ambush, and the Shivering Isles orcs only once their own script says they may die (killMe 1).

    python3 -I plugin/tools/test_hit_kills.py mod/Content/CoopQuestSync.esp
"""
import os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tes4
from test_new_start import Game, Script, ticks

EMPEROR, ASSASSIN = "CQSRef00032A18", "CQSRef0001E703"
RENAULT, AMBUSH1 = "CQSRef00032A15", "CQSRef00014A30"
ORC1, GATEKEEPER1 = "CQSRef00018FEC", "CQSRef000435B0"


def main():
    srcs = [r.get("SCTX").decode("cp1252") for r in tes4.iter_records(tes4.load(sys.argv[1]), want={"SCPT"})
            if r.edid and r.edid.startswith("CQSHitKillScript")]
    failures = []

    def check(label, ok, detail=""):
        print(("ok   " if ok else "FAIL ") + label + (f"  ({detail})" if detail and not ok else ""))
        if not ok:
            failures.append(label)

    check("kill-on-hit scripts in the plugin", bool(srcs), len(srcs))
    text = "".join(srcs)
    for name, ref in (("the Emperor", EMPEROR), ("Renault", RENAULT), ("the first SI orc", ORC1)):
        check(f"{name} is watched", f"{ref}.Kill" in text)

    def run(setup, n):
        g = Game()
        setup(g)
        scripts = [Script(src) for src in srcs]
        for _ in range(n):
            for s in scripts:
                s.run(g)
        return g

    # The Emperor: the assassin fights him at sword's reach.
    g = run(lambda g: (g.targets.update({ASSASSIN: EMPEROR}), g.dist.update({(ASSASSIN, EMPEROR): 90})), 2)
    check("Emperor: not on the first moment of the fight", EMPEROR not in g.dead, g.dead)
    g = run(lambda g: (g.targets.update({ASSASSIN: EMPEROR}), g.dist.update({(ASSASSIN, EMPEROR): 90})), 3)
    check("Emperor: dies once the assassin has fought him up close", EMPEROR in g.dead, g.dead)
    g = run(lambda g: (g.targets.update({ASSASSIN: EMPEROR}), g.dist.update({(ASSASSIN, EMPEROR): 900})), 20)
    check("Emperor: not while the assassin is still far off", EMPEROR not in g.dead, g.dead)
    g = run(lambda g: (g.targets.update({ASSASSIN: "CQSRef00000014"}), g.dist.update({(ASSASSIN, EMPEROR): 90})), 20)
    check("Emperor: not when the assassin fights someone else beside him", EMPEROR not in g.dead, g.dead)
    g = run(lambda g: (g.targets.update({ASSASSIN: EMPEROR}), g.dist.update({(ASSASSIN, EMPEROR): 90}),
                       g.dead.add(ASSASSIN)), 20)
    check("Emperor: not by a dead assassin", EMPEROR not in g.dead, g.dead)

    # Renault and the first ambush.
    g = run(lambda g: (g.targets.update({AMBUSH1: RENAULT}), g.dist.update({(AMBUSH1, RENAULT): 120})), 5)
    check("Renault: dies when an assassin of the first ambush fights her up close", RENAULT in g.dead, g.dead)

    # The orcs at the Shivering Isles gate: only once their script allows it.
    def orc(kill_me):
        return lambda g: (g.targets.update({GATEKEEPER1: ORC1}), g.dist.update({(GATEKEEPER1, ORC1): 100}),
                          g.refvars.update({(ORC1, "killme"): kill_me}))
    g = run(orc(0), 20)
    check("SI orc: lives while its script says killMe 0", ORC1 not in g.dead, g.dead)
    g = run(orc(1), 5)
    check("SI orc: dies once killMe is 1", ORC1 in g.dead, g.dead)

    print("all passed" if not failures else f"{len(failures)} failed")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
