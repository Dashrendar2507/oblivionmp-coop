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
        self.pressed = -1        # what GetButtonPressed returns next
        self.boxes = []          # every box shown: (text, buttons)
        self.topics = set()
        self.stopped = set()
        self.at = "prison"

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
            raise Exception(name)
        e = re.sub(r"(player\.)?(GetStageDone|GetStage|GetItemCount)((?: \w+)+?)(?= ==| !=| >=| <=| >| <|$| \|\|| &&)",
                   fn, expr, flags=re.I)
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
        if g.box is not None:
            return                       # a message box pauses the game: GameMode blocks don't run
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
    g.setstage("MQ01", 88)       # the sewer gate's Finished button
    ticks(s, g)
    check("out of the sewers: CQSIntro gone", g.inv.get("CQSIntro") == 0, g.inv)

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

    # 4. The box was lost (a load): it comes back after about 5 s.
    s, g = Script(src), Game(**in_prison)
    g.inv["CQSStartOffer"] = 1
    ticks(s, g, 1)
    g.box = None                 # gone without an answer
    ticks(s, g, 13)
    check("lost window: shown again", len(g.boxes) == 2, g.boxes)

    # 5. A player who already left the sewers gets no window, even with an offer.
    s, g = Script(src), Game(cur={"CHARACTERGEN": 88}, done={"MQ01": {5, 88, 100}}, inv={"CQSIntro": 1})
    g.inv["CQSStartOffer"] = 1
    ticks(s, g)
    check("out of the sewers: no window", not g.boxes)
    check("out of the sewers: stale CQSIntro taken back", g.inv.get("CQSIntro") == 0, g.inv)

    # 6. Never left the sewers, but no intro runs (not the vanilla start): CQSIntro is 2, the skip still works.
    s, g = Script(src), Game()
    ticks(s, g)
    check("no intro running: CQSIntro is 2", g.inv.get("CQSIntro") == 2, g.inv)
    g.inv["CQSStartOffer"] = 2
    ticks(s, g)
    g.press(0)
    ticks(s, g)
    check("no intro running: skipped to the sewer exit", g.at == "CGSewerExitMarker")

    # 7. An admin offers the start again: the answer item is taken, the window comes back.
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
