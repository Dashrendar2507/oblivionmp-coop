"""Runs the new-player start script (CQSNewStartScript's own text, from CoopQuestSync.esp) against a small model of
the game, and checks each path: the start window, the prison, the skip, the info box, a lost box, and players who
are already out of the sewers.

    python3 -I plugin/tools/test_new_start.py mod/Content/CoopQuestSync.esp
"""
import os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tes4


class Game:
    def __init__(self, done=None, cur=None, inv=None):
        self.done = {q: set(s) for q, s in (done or {}).items()}
        self.cur = dict(cur or {})
        self.inv = dict(inv or {})
        self.box = None          # the buttons of the open message box
        self.menu = False        # another menu is open (the Esc menu, the map...)
        self.pos = [0.0, 0.0]    # the player's X and Y
        self.walking = False     # the player walks 50 units each run (never while a box is up: it takes the input)
        self.pressed = -1        # what GetButtonPressed returns next
        self.boxes = []          # every box shown: (text, buttons)
        self.topics = set()
        self.stopped = set()
        self.at = "prison"
        self.moves = 0

    def setstage(self, q, s):
        self.done.setdefault(q, set()).add(s)
        self.cur[q] = max(self.cur.get(q, 0), s)

    def press(self, i):
        assert self.box is not None and 0 <= i < len(self.box), (self.box, i)
        self.box = None
        self.pressed = i


class Script:
    def __init__(self, src):
        lines = [l.split(";")[0].strip() for l in src.replace("\r", "").split("\n")]
        lines = [l for l in lines if l]
        self.vars = {m.group(2).lower(): 0 for l in lines for m in [re.match(r"(short|float) (\w+)", l, re.I)] if m}
        self.body = [l for l in lines if not re.match(r"(scn|short|float|begin)\b|end$", l, re.I)]

    def ev(self, expr, g):
        def fn(m):
            name, args = m.group(2).lower(), m.group(3).split()
            if name == "getstage":
                return str(g.cur.get(args[0].upper(), 0))
            if name == "getstagedone":
                return "1" if int(args[1]) in g.done.get(args[0].upper(), set()) else "0"
            if name == "getitemcount":
                return str(g.inv.get(args[0], 0))
            if name == "getpos":
                return str(g.pos["XY".index(args[0].upper())])
            raise Exception(name)
        e = re.sub(r"(player\.)?(GetStageDone|GetStage|GetItemCount|GetPos)((?: \w+)+?)(?= ==| !=| >=| <=| >| <|$| \|\|| &&)",
                   fn, expr, flags=re.I)
        # Under OblivionMP, MenuMode stays 0 while a message box is open (2026-11-14).
        e = re.sub(r"\bMenuMode\b", "1" if g.menu else "0", e, flags=re.I)
        if re.search(r"GetButtonPressed", e, re.I):
            pressed, g.pressed = g.pressed, -1
            e = re.sub(r"GetButtonPressed", str(pressed), e, flags=re.I)
        e = e.replace("&&", " and ").replace("||", " or ")

        def var(m):
            w = m.group(0)
            if w in ("and", "or"):
                return w
            return str(self.vars[w.lower()])
        return eval(re.sub(r"[A-Za-z_]\w*", var, e))

    def run(self, g):
        # OblivionMP doesn't pause the game for a message box: GameMode blocks keep running while it is open.
        if g.walking and g.box is None:
            g.pos[0] += 50
        stack, active = [], True
        for l in self.body:
            kw = l.split()[0].lower()
            if kw == "if":
                cond = active and bool(self.ev(l[3:], g))
                stack.append([active, cond])
                active = cond
            elif kw == "elseif":
                parent, taken = stack[-1]
                cond = parent and not taken and bool(self.ev(l[7:], g))
                stack[-1][1] = taken or cond
                active = cond
            elif kw == "else":
                parent, taken = stack[-1]
                active = parent and not taken
                stack[-1][1] = True
            elif kw == "endif":
                active = stack.pop()[0]
            elif not active:
                continue
            elif kw == "set":
                m = re.match(r"set (\w+) to (.+)$", l, re.I)
                self.vars[m.group(1).lower()] = self.ev(m.group(2), g)
            elif kw == "messagebox":
                texts = re.findall(r'"([^"]*)"', l)
                g.box = texts[1:] or ["Ok"]
                g.boxes.append((texts[0], g.box))
            elif kw == "player.additem":
                _, item, n = l.split()
                g.inv[item] = g.inv.get(item, 0) + int(n)
            elif kw == "player.removeitem":
                _, item, n = l.split()
                g.inv[item] = max(0, g.inv.get(item, 0) - int(n))
            elif kw == "setstage":
                _, q, s = l.split()
                g.setstage(q.upper(), int(s))
            elif kw == "addtopic":
                g.topics.add(l.split()[1])
            elif kw == "stopquest":
                g.stopped.add(l.split()[1].upper())
            elif kw == "player.moveto":
                g.at = l.split()[1]
                g.moves += 1
                return                   # MoveTo on the player ends the script there (the game does this)
            else:
                raise Exception("unhandled " + l)
        assert not stack, "unbalanced if/endif"


def ticks(script, g, n=4):
    for _ in range(n):
        script.run(g)


def main():
    src = next(r.get("SCTX").decode("cp1252") for r in tes4.iter_records(tes4.load(sys.argv[1]), want={"SCPT"})
               if r.edid == "CQSNewStartScript")
    failures = []

    def check(label, ok, detail=""):
        print(("ok   " if ok else "FAIL ") + label + (f"  ({detail})" if detail and not ok else ""))
        if not ok:
            failures.append(label)

    in_prison = dict(cur={"CHARACTERGEN": 5}, done={"CHARACTERGEN": {5}, "MQ01": {5}})

    # 1. A new player in the prison picks the sewer exit.
    s, g = Script(src), Game(**in_prison)
    ticks(s, g)
    check("in the prison intro: CQSIntro is 1", g.inv.get("CQSIntro") == 1, g.inv)
    check("no window before the server offers one", not g.boxes)
    g.inv["CQSStartOffer"] = 1
    ticks(s, g)
    check("offer 1: a window with two buttons", g.box is not None and len(g.box) == 2, g.boxes)
    g.press(1)
    ticks(s, g)
    check("skip: MQ02 10 and 20 done", {10, 20} <= g.done.get("MQ02", set()), g.done)
    check("skip: the five topics", len(g.topics) == 5, g.topics)
    check("skip: Charactergen stopped", "CHARACTERGEN" in g.stopped)
    check("skip: at the sewer exit", g.at == "CGSewerExitMarker", g.at)
    check("skip: CQSStartDone given once", g.inv.get("CQSStartDone") == 1, g.inv)
    check("skip: no prison answer", not g.inv.get("CQSStartPrison"))
    check("skip: the window is not shown again", len(g.boxes) == 1, g.boxes)
    check("skip: still in the intro until the gate", g.inv.get("CQSIntro") == 1, g.inv)
    ticks(s, g, 10)
    check("skip: moved to the sewer exit once", g.moves == 1, g.moves)
    g.setstage("MQ01", 88)       # the sewer gate's Finished button
    g.at = "outside"
    ticks(s, g)
    check("out of the sewers: CQSIntro gone", g.inv.get("CQSIntro") == 0, g.inv)
    check("out of the sewers: not moved back", g.at == "outside" and g.moves == 1, (g.at, g.moves))

    # 2. A new player picks the prison.
    s, g = Script(src), Game(**in_prison)
    g.inv["CQSStartOffer"] = 1
    ticks(s, g)
    g.press(0)
    ticks(s, g)
    check("prison: CQSStartPrison given", g.inv.get("CQSStartPrison") == 1, g.inv)
    check("prison: nothing skipped", g.at == "prison" and not g.done.get("MQ02") and not g.stopped)
    check("prison: no second window", len(g.boxes) == 1, g.boxes)

    # 3. The prison is used up: the info box, then the skip.
    s, g = Script(src), Game(**in_prison)
    g.inv["CQSStartOffer"] = 2
    ticks(s, g)
    check("offer 2: one button", g.box is not None and len(g.box) == 1, g.boxes)
    g.press(0)
    ticks(s, g)
    check("offer 2: skipped to the sewer exit", g.at == "CGSewerExitMarker" and g.inv.get("CQSStartDone") == 1)

    # 4. The box stays open a long time (the script keeps running under it): it is not shown again.
    s, g = Script(src), Game(**in_prison)
    g.inv["CQSStartOffer"] = 1
    ticks(s, g, 1)
    ticks(s, g, 120)             # a minute of thinking
    check("open window: not shown again while open", len(g.boxes) == 1, g.boxes)
    g.press(1)
    ticks(s, g)
    check("open window: the late answer still counts", g.at == "CGSewerExitMarker", g.at)

    # 4b. The Esc menu is open: no window over it, and the skip waits until every menu is closed.
    s, g = Script(src), Game(**in_prison)
    g.inv["CQSStartOffer"] = 1
    g.menu = True
    ticks(s, g, 10)
    check("menu open: no window over it", not g.boxes, g.boxes)
    g.menu = False
    ticks(s, g, 1)
    check("menu closed: the window comes", len(g.boxes) == 1, g.boxes)
    g.menu = True                # Esc pressed while the window is up
    g.press(1)
    ticks(s, g, 10)
    check("skip with a menu open: not moved yet", g.at == "prison" and not g.inv.get("CQSStartDone"), (g.at, g.inv))
    check("skip with a menu open: no new window", len(g.boxes) == 1, g.boxes)
    g.menu = False
    ticks(s, g)
    check("skip after the menu closed: at the sewer exit", g.at == "CGSewerExitMarker" and g.inv.get("CQSStartDone") == 1, g.at)

    # 5. The box was lost (a load): it comes back once the player walks about.
    s, g = Script(src), Game(**in_prison)
    g.inv["CQSStartOffer"] = 1
    ticks(s, g, 1)
    g.box = None                 # gone without an answer
    ticks(s, g, 30)
    check("lost window: not back while the player stands still", len(g.boxes) == 1, g.boxes)
    g.pos[1] += 3000             # a teleport: one move
    ticks(s, g, 3)
    check("lost window: not back after a teleport", len(g.boxes) == 1, g.boxes)
    g.walking = True
    ticks(s, g, 3)
    check("lost window: not back after a few steps", len(g.boxes) == 1, g.boxes)
    ticks(s, g, 5)
    check("lost window: shown again once the player walks", len(g.boxes) == 2, g.boxes)
    ticks(s, g, 10)
    check("lost window: shown once, not again while it is up", len(g.boxes) == 2, g.boxes)

    # 6. A player who already left the sewers gets no window, even with an offer.
    s, g = Script(src), Game(cur={"CHARACTERGEN": 88}, done={"MQ01": {5, 88, 100}}, inv={"CQSIntro": 1})
    g.inv["CQSStartOffer"] = 1
    ticks(s, g)
    check("out of the sewers: no window", not g.boxes)
    check("out of the sewers: stale CQSIntro taken back", g.inv.get("CQSIntro") == 0, g.inv)

    # 7. Never left the sewers, but no intro runs (not the vanilla start): CQSIntro is 2, the skip still works.
    s, g = Script(src), Game()
    ticks(s, g)
    check("no intro running: CQSIntro is 2", g.inv.get("CQSIntro") == 2, g.inv)
    g.inv["CQSStartOffer"] = 2
    ticks(s, g)
    g.press(0)
    ticks(s, g)
    check("no intro running: skipped to the sewer exit", g.at == "CGSewerExitMarker")

    # 6b. A save from 0.6.15 or older, stuck at state 3 after going out: no move back.
    s, g = Script(src), Game(cur={"MQ01": 88}, done={"MQ01": {5, 88}, "MQ02": {10, 20}}, inv={"CQSIntro": 0})
    s.vars["state"] = 3
    g.at = "outside"
    ticks(s, g)
    check("old save stuck at state 3: not moved", g.at == "outside" and g.moves == 0, (g.at, g.moves))

    # 8. An admin offers the start again: the answer item is taken, the window comes back.
    s, g = Script(src), Game(**in_prison)
    g.inv["CQSStartOffer"] = 1
    ticks(s, g)
    g.press(0)
    ticks(s, g)
    g.inv["CQSStartPrison"] = 0
    ticks(s, g)
    check("offered again: a new window", len(g.boxes) == 2, g.boxes)

    print("all passed" if not failures else f"{len(failures)} failed")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
