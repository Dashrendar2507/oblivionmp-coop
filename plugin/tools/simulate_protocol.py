"""Simulates the generated plugin scripts against a model of the game and of the server handshake.

    python3 -I plugin/tools/simulate_protocol.py mod/Content/CoopQuestSync.esp


The script interpreter runs the actual SCTX text from CoopQuestSync.esp. The server part mirrors
QuestTokenSync.Update (phases, bumps, baseline, revert, push) closely enough to check the protocol.
"""
import os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tes4

SCRIPTS = {r.edid: r.get("SCTX").decode("cp1252")
           for r in tes4.iter_records(tes4.load(sys.argv[1]), want={"SCPT"})
           if r.edid in ("CQSControlScript", "CQSsMS16", "CQSsMS93")}
TOK = {}  # editor id -> token item name (same as editor id)

class Game:
    def __init__(self, inventory, globals_):
        self.inv = dict(inventory)          # item editor id -> count
        self.done = {}                      # quest -> set of stages
        self.cur = {}                       # quest -> current stage
        self.glob = dict(globals_)
        self.messages = []
        self.rewards = {"MS16": {15: ("Gold001", 50)}}
    def setstage(self, q, s):
        self.done.setdefault(q, set()).add(s)
        self.cur[q] = max(self.cur.get(q, 0), s)
        r = self.rewards.get(q, {}).get(s)
        if r:
            self.inv[r[0]] = self.inv.get(r[0], 0) + r[1]
        self.messages.append(f"quest {q} {s}")

class Script:
    def __init__(self, src):
        self.lines = [l.strip() for l in src.replace("\r", "").split("\n") if l.strip()]
        self.vars = {}
        for l in self.lines:
            m = re.match(r"(short|float) (\w+)", l)
            if m:
                self.vars[m.group(2).lower()] = 0
        self.body = [l for l in self.lines if not re.match(r"(scn|short|float|begin|end$)", l)]
        self.delay = 5.0
        self.next = 0.0

    def ev(self, expr, g):
        def fn(m):
            ref, name, args = m.group(1), m.group(2).lower(), m.group(3).split()
            if name == "getstage": return str(g.cur.get(args[0], 0))
            if name == "getstagedone": return "1" if int(args[1]) in g.done.get(args[0], set()) else "0"
            if name == "getitemcount": return str(g.inv.get(args[0], 0))
            raise Exception(name)
        e = re.sub(r"(player\.)?(GetStageDone|GetStage|GetItemCount)((?: \w+)+?)(?= ==| !=| >| <|$| \|\|| &&)", fn, expr)
        e = e.replace("&&", " and ").replace("||", " or ")
        def var(m):
            w = m.group(0)
            if w in ("and", "or"): return w
            if w.lower() in self.vars: return str(self.vars[w.lower()])
            if w in g.glob: return str(g.glob[w])
            raise Exception("unknown " + w)
        e = re.sub(r"[A-Za-z_]\w*", var, e)
        return eval(e)

    def run(self, g):
        i, skip_stack = 0, []
        # simple structured interpreter: stack of (taken_any, active)
        stack = []
        active = True
        while i < len(self.body):
            l = self.body[i]; i += 1
            kw = l.split()[0].lower()
            if kw == "if":
                parent = active
                cond = parent and bool(self.ev(l[3:], g))
                stack.append([parent, cond])
                active = cond
                continue
            if kw == "elseif":
                parent, taken = stack[-1]
                cond = parent and not taken and bool(self.ev(l[7:], g))
                stack[-1][1] = taken or cond
                active = cond
                continue
            if kw == "else":
                parent, taken = stack[-1]
                active = parent and not taken
                stack[-1][1] = True
                continue
            if kw == "endif":
                parent, _ = stack.pop()
                active = parent
                continue
            if not active:
                continue
            if kw == "set":
                m = re.match(r"set (\w+) to (.+)$", l, re.I)
                v = self.ev(m.group(2), g)
                name = m.group(1)
                if name.lower() == "fquestdelaytime": self.delay = float(v)
                if name.lower() in self.vars: self.vars[name.lower()] = v if name.lower() == "fquestdelaytime" else int(v)
                else: g.glob[name] = int(v)
            elif kw == "return":
                return
            elif kw == "message":
                g.messages.append(re.match(r'message "(.*)"', l, re.I).group(1))
            elif l.lower().startswith("player.additem"):
                _, item, n = l.split(); g.inv[item] = g.inv.get(item, 0) + int(n)
            elif l.lower().startswith("player.removeitem"):
                _, item, n = l.split(); g.inv[item] = max(0, g.inv.get(item, 0) - int(n))
            elif kw == "setstage":
                _, q, s = l.split(); g.setstage(q, int(s))
            else:
                raise Exception("unhandled " + l)

class Server:
    """Mirror of QuestTokenSync for one player; tokens are names starting with CQSt."""
    def __init__(self, shared):
        self.shared = set(shared); self.phase = "NoPlugin"; self.clock = 0; self.prev = {}; self.baseline = None
        self.stages = set(); self.log = []
    def items(self, inv):
        return {k: v for k, v in inv.items() if not k.startswith("CQS") and v > 0}
    def poll(self, g):
        self.clock += 1
        inv = g.inv
        if inv.get("CQSBeacon", 0) == 0:
            return
        snap_stages = {k for k, v in inv.items() if k.startswith("CQSt") and v > 0}
        busy, ack = inv.get("CQSBusy", 0), inv.get("CQSAck", 0)
        items = self.items(inv)
        if self.phase == "NoPlugin":
            if busy: inv["CQSBusy"] = 0
            self.ack0 = ack; self.phase = "Handshake"; self.bump(g); self.log.append(f"{self.clock}: handshake")
        elif self.phase == "Handshake" and ack > self.ack0:
            self.phase = "Restoring"; self.ackAt = self.clock; self.log.append(f"{self.clock}: ack")
        elif self.phase == "Handshake" and self.clock - self.lastBump > 3:
            self.bump(g)
        ready_now = False
        if self.phase == "Restoring":
            if busy and self.baseline is None:
                self.baseline = self.prev; self.log.append(f"{self.clock}: restore start")
            elif self.baseline is not None and busy == 0:
                for k in set(self.baseline) | set(items):
                    d = items.get(k, 0) - self.baseline.get(k, 0)
                    if d: inv[k] = inv.get(k, 0) - d; self.log.append(f"{self.clock}: revert {k} {-d:+d}")
                self.baseline = None; self.phase = "Ready"; ready_now = True; self.log.append(f"{self.clock}: restore finished")
            elif self.baseline is None and self.clock - self.ackAt > 8:
                self.phase = "Ready"; ready_now = True; self.log.append(f"{self.clock}: nothing to restore")
        new = snap_stages - self.stages
        self.stages = snap_stages
        for s in new: self.shared.add(s)
        if ready_now or (new and self.phase == "Ready"):
            missing = self.shared - self.stages
            for s in missing: inv[s] = 1; self.stages.add(s); self.log.append(f"{self.clock}: push {s}")
            if missing: self.bump(g)
        self.prev = self.items(inv)
    def bump(self, g):
        g.inv["CQSSeq"] = g.inv.get("CQSSeq", 0) + 1; self.lastBump = self.clock

def session(inventory, shared, label, seconds=40):
    g = Game(inventory, {"CQSGo": 0, "CQSReplay": 0})
    scripts = {k: Script(v) for k, v in SCRIPTS.items()}
    srv = Server(shared)
    t = 0.0
    while t < seconds:
        for s in scripts.values():
            if t >= s.next:
                s.run(g); s.next = t + s.delay
        if abs(t - round(t)) < 1e-9 and t > 0:
            srv.poll(g)
        t = round(t + 0.1, 1)
    print(f"== {label}")
    print("  server:", srv.log)
    print("  messages:", g.messages)
    print("  done:", {k: sorted(v) for k, v in g.done.items()}, " gold:", g.inv.get("Gold001", 0),
          " busy:", g.inv.get("CQSBusy", 0), " go:", g.glob["CQSGo"])
    return g

# 1) first session: nothing done yet, the server already shares MS93 10 and MS16 15
g1 = session({"Gold001": 100}, {"CQStMS93_10", "CQStMS16_15"}, "first join, shared progress pushed")
# the player then advances MS93 20 by hand
# 2) rejoin: game quest state is gone, inventory (with tokens and rewards) is kept
inv = dict(g1.inv); inv["CQStMS93_20"] = 1
g2 = session(inv, {"CQStMS93_10", "CQStMS16_15", "CQStMS93_20"}, "rejoin: own stages restored, rewards undone")

ok = (g1.inv.get("Gold001") == 150 and g2.inv.get("Gold001") == 150
      and g2.done.get("MS93") == {10, 20} and g2.done.get("MS16") == {15} and g2.inv.get("CQSBusy", 0) == 0)
print("PASS" if ok else "FAIL")
sys.exit(0 if ok else 1)
