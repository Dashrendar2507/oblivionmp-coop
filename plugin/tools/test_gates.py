"""Runs the Oblivion gate scripts (CQSGateRealmScript and CQSGateScript*, from CoopQuestSync.esp) against the model of
the game in test_new_start. A gate closed in a player's game is reported with its CQSGate token; with the token from
the server, another player still inside the gate's realm is sent out through it (CloseCurrentOblivionGate), and a
player near the gate in Tamriel sees it close (ForceCloseOblivionGate).

    python3 -I plugin/tools/test_gates.py mod/Content/CoopQuestSync.esp mod/Content/tokens.json
"""
import json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tes4
from test_new_start import Game, Script

KVATCH = "CQSRef0001E8A3"           # the gate at Kvatch (MS48KvatchGate), one realm of its own
KVATCH_WORLD = "CQSPlace0001D0BC"    # OblivionMQKvatch
KVATCH_KEEP = "CQSPlace0001D4D5"     # OblivionMqKvatchCitadelLord, where the sigil stone is
TAMRIEL = "CQSPlace0000003C"
RD_WORLD = "CQSPlace0003F551"        # OblivionRD002, one of the random realms


def token(ref):
    return "CQSGate" + ref[len("CQSRef"):]


def main():
    plugin = tes4.load(sys.argv[1])
    texts = {r.edid: r.get("SCTX").decode("cp1252") for r in tes4.iter_records(plugin, want={"SCPT"})
             if r.edid and r.edid.startswith("CQSGate")}
    tokens = json.load(open(sys.argv[2], encoding="utf-8"))
    failures = []

    def check(label, ok, detail=""):
        print(("ok   " if ok else "FAIL ") + label + (f"  ({detail})" if detail and not ok else ""))
        if not ok:
            failures.append(label)

    realm_src = texts.pop("CQSGateRealmScript", None)
    check("realm script in the plugin", realm_src is not None)
    check("gate scripts in the plugin", bool(texts), len(texts))
    gates = tokens.get("gates", [])
    check("tokens.json lists the gates", len(gates) >= 90, len(gates))
    check("Kvatch's gate is watched", any(x["ref"] == "0001E8A3" for x in gates))
    text = "".join(texts.values())
    # A random gate: one whose closing from inside doesn't also go by its realm.
    RANDOM = "CQSRef" + next(x["ref"] for x in gates
                             if "CQSRealm == " not in text.split(f"if CQSRef{x['ref']}.GetDestroyed == 1", 1)[1].split("ForceCloseOblivionGate", 1)[0])

    def number(ref):
        m = re.search(rf"if CQSRef{ref[6:]}\.GetDestroyed == 1.*?if CQSGateLast == (\d+)", text, re.S)
        return int(m.group(1))

    def fresh():
        g = Game()
        g.globals.update({"cqsrealm": 0, "cqsgatelast": 0, "cqsgatewait": 0})
        return g

    def run(setup, n=4):
        g = fresh()
        setup(g)
        scripts = [Script(realm_src)] + [Script(src) for src in texts.values()]
        for _ in range(n):
            for s in scripts:
                s.run(g)
        return g

    # The closer: the gate is destroyed in their game.
    g = run(lambda g: (g.destroyed.add(KVATCH), g.where.update({TAMRIEL})))
    check("closer: the gate's token is reported", g.inv.get(token(KVATCH)) == 1, g.inv)
    check("closer: nothing else is closed", g.closed_current == 0 and not g.force_closed, (g.closed_current, g.force_closed))

    # Another player at the sigil keep of the same realm when the token comes.
    def inside(g):
        g.where.update({KVATCH_KEEP})
        g.current_gate = KVATCH
        g.inv[token(KVATCH)] = 1
    g = run(inside, 10)
    check("inside Kvatch's realm: sent out through the gate, once", g.closed_current == 1, g.closed_current)
    check("inside: the gate is closed after", KVATCH in g.destroyed)
    g = run(lambda g: (g.where.update({KVATCH_WORLD}), g.inv.update({token(KVATCH): 1})), 10)
    check("in Kvatch's realm outdoors: sent out too", g.closed_current == 1, g.closed_current)
    g = run(lambda g: (g.where.update({RD_WORLD}), g.inv.update({token(KVATCH): 1})), 10)
    check("in another realm: not sent out for Kvatch's gate", g.closed_current == 0, g.closed_current)

    # Near the gate in Tamriel.
    def near(dist, weather=True):
        def setup(g):
            g.where.update({TAMRIEL})
            g.inv[token(KVATCH)] = 1
            g.dist[(KVATCH, "player")] = dist
            if weather:
                g.refvars[("MQ00", "nearobliviongate")] = KVATCH
        return setup
    g = run(near(3000))
    check("near the gate in Tamriel: it closes", g.force_closed == [KVATCH], g.force_closed)
    check("near: the gate's storm is let go", g.weather_released == 1 and g.refvars[("MQ00", "nearobliviongate")] == 0,
          (g.weather_released, g.refvars))
    check("near: nobody is sent anywhere", g.closed_current == 0)
    g = run(near(30000))
    check("far from the gate: left alone until the player comes near", not g.force_closed, g.force_closed)
    g = run(lambda g: (near(3000)(g), g.where.clear(), g.where.add("CQSPlace00023777")))
    check("in a building: the gate is left alone", not g.force_closed, g.force_closed)
    g = run(lambda g: (near(3000)(g), g.disabled.add(KVATCH)))
    check("a gate that isn't open in this game: left alone", not g.force_closed, g.force_closed)

    # A random gate: only the one the player came through sends them out.
    n = number(RANDOM)

    def through(ref):
        def setup(g):
            g.where.update({TAMRIEL})
            g.dist[(ref, "player")] = 200
        return setup
    g = run(through(RANDOM), 2)
    check("standing at a random gate in Tamriel: it is the player's gate", g.globals.get("cqsgatelast") == n,
          (g.globals.get("cqsgatelast"), n))
    g.where.clear()
    g.where.add(RD_WORLD)
    g.dist.clear()
    g.current_gate = RANDOM
    g.inv[token(RANDOM)] = 1
    for _ in range(10):
        for s in [Script(realm_src)] + [Script(src) for src in texts.values()]:
            s.run(g)
    check("came through that random gate: sent out when it closes", g.closed_current == 1, g.closed_current)
    g = fresh()
    g.where.add(RD_WORLD)
    g.globals["cqsgatelast"] = n + 1 if n < len(gates) else n - 1
    g.inv[token(RANDOM)] = 1
    for _ in range(10):
        for s in [Script(realm_src)] + [Script(src) for src in texts.values()]:
            s.run(g)
    check("came through another random gate: stays", g.closed_current == 0, g.closed_current)

    print("all passed" if not failures else f"{len(failures)} failed")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
