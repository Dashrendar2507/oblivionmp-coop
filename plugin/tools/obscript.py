"""Tiny Oblivion script compiler for a small subset of the language.

Supported: scn, short/long/float declarations, begin <mode>/end, set <var> to <expr>,
if/elseif/else/endif, return, Message "text", MessageBox "text", "button"..., and command statements (optionally ref.Command) whose
parameters are form references or integers. Expressions: + - * /, comparisons, && ||,
parentheses, numbers, local variables and known functions (optionally ref.Func).
"""
import re, struct

# opcode, parameter kinds ('ref' = form reference, 'int' = integer)
FUNCS = {
    "getstage":      (0x103A, ["ref"]),
    "getstagedone":  (0x103B, ["ref", "int"]),
    "getitemcount":  (0x102F, ["ref"]),
    "setstage":      (0x1039, ["ref", "int"]),
    "additem":       (0x1002, ["ref", "int"]),
    "removeitem":    (0x1052, ["ref", "int"]),
    "startquest":    (0x1036, ["ref"]),
    "stopquest":     (0x1037, ["ref"]),
    "moveto":        (0x109E, ["ref"]),
    "addtopic":      (0x1058, ["ref"]),
    "getbuttonpressed": (0x101F, None),
    # an optional menu type: the game writes an empty parameter list (a zero count) when there is none
    "menumode":      (0x1024, []),
    # no parameter block at all: just opcode and a zero length
    "enableplayercontrols":  (0x1060, None),
    "disableplayercontrols": (0x1061, None),
}
BLOCKS = {"gamemode": 0, "menumode": 1, "onactivate": 2, "onadd": 3}
OPS = {"||": 1, "&&": 2, "==": 3, "!=": 3, "<": 3, ">": 3, "<=": 3, ">=": 3,
       "+": 4, "-": 4, "*": 5, "/": 5}
TOK = re.compile(r"\s*(\|\||&&|==|!=|<=|>=|[<>()+\-*/]|[A-Za-z_][\w]*(?:\.[A-Za-z_]\w*)?|\d+(?:\.\d+)?)")

class CompileError(Exception):
    pass

class Script:
    def __init__(self, resolve, globals_=()):
        """resolve(editor_id) -> form id (int) or None. globals_: editor ids of GLOB records."""
        self.resolve = resolve
        self.globals = {g.lower() for g in globals_}
        self.refs = []          # form ids in SCRO order
        self.vars = []          # (name, is_int)
        self.name = None

    # ---- references and variables ----
    def ref_index(self, name):
        if name.lower() == "player":
            fid = 0x14
        else:
            fid = self.resolve(name)
            if fid is None:
                raise CompileError(f"unknown form {name}")
        if fid not in self.refs:
            self.refs.append(fid)
        return self.refs.index(fid) + 1

    def var(self, name):
        for i, (n, is_int) in enumerate(self.vars):
            if n.lower() == name.lower():
                return i + 1, is_int
        return None

    def var_token(self, name):
        if self.var(name) is None and name.lower() in self.globals:
            return b"\x47" + struct.pack("<H", self.ref_index(name))
        v = self.var(name)
        if v is None:
            raise CompileError(f"unknown variable {name}")
        idx, is_int = v
        return bytes([0x73 if is_int else 0x66]) + struct.pack("<H", idx)

    # ---- params for command / function calls ----
    def params(self, kinds, args):
        if kinds is None:
            if args:
                raise CompileError(f"expected no params, got {args}")
            return b""
        if len(args) != len(kinds):
            raise CompileError(f"expected {len(kinds)} params, got {args}")
        out = struct.pack("<H", len(kinds))
        for k, a in zip(kinds, args):
            if k == "ref":
                out += b"\x72" + struct.pack("<H", self.ref_index(a))
            else:
                out += b"\x6e" + struct.pack("<i", int(a))
        return out

    def call(self, name, args):
        ref = None
        if "." in name:
            ref, name = name.split(".", 1)
        key = name.lower()
        if key not in FUNCS:
            raise CompileError(f"unknown function {name}")
        op, kinds = FUNCS[key]
        prefix = b""
        if ref is not None:
            prefix = struct.pack("<H", self.ref_index(ref))
        p = self.params(kinds, args)
        return ref is not None, prefix, op, p, kinds

    # ---- expressions to RPN bytes ----
    def expr(self, text):
        toks = [t for t in TOK.findall(text)]
        if "".join(toks).replace(" ", "") != re.sub(r"\s+", "", text):
            raise CompileError(f"bad expression {text!r}")
        out, stack = [], []
        i = 0
        def emit(b):
            out.append(b" " + b)
        while i < len(toks):
            t = toks[i]
            base = t.split(".", 1)[-1].lower()
            if t == "(":
                stack.append(t)
            elif t == ")":
                while stack and stack[-1] != "(":
                    emit(stack.pop().encode())
                stack.pop()
            elif t in OPS:
                while stack and stack[-1] != "(" and OPS[stack[-1]] >= OPS[t]:
                    emit(stack.pop().encode())
                stack.append(t)
            elif re.fullmatch(r"\d+(?:\.\d+)?", t):
                emit(t.encode())
            elif base in FUNCS:
                nargs = len(FUNCS[base][1] or [])
                args = toks[i + 1:i + 1 + nargs]
                hasref, prefix, op, p, _ = self.call(t, args)
                b = (b"\x72" + prefix if hasref else b"") + b"\x58" + struct.pack("<HH", op, len(p)) + p
                emit(b)
                i += nargs
            elif self.var(t) or t.lower() in self.globals:
                emit(self.var_token(t))
            else:
                raise CompileError(f"unknown token {t}")
            i += 1
        while stack:
            emit(stack.pop().encode())
        return b"".join(out)

    # ---- statements ----
    def compile(self, source):
        lines = []
        for raw in source.replace("\r", "").split("\n"):
            l = raw.split(";")[0].strip()
            if l:
                lines.append(l)
        stmts = []      # list of [bytes or None, kind]
        for l in lines:
            words = l.split()
            kw = words[0].lower()
            if kw in ("scn", "scriptname"):
                self.name = words[1]
                stmts.append(["name", b"\x1d\x00\x00\x00"])
            elif kw in ("short", "long", "float"):
                self.vars.append((words[1], kw != "float"))
            elif kw == "begin":
                mode = BLOCKS[words[1].lower()]
                param = None
                if len(words) > 2 and words[2].isdigit():
                    param = b"\x01\x00\x6e" + struct.pack("<i", int(words[2]))   # MenuMode 1008
                elif len(words) > 2:
                    param = b"\x01\x00\x72" + struct.pack("<H", self.ref_index(words[2]))
                stmts.append(["begin", (mode, param)])
            elif kw == "end":
                stmts.append(["end", b"\x11\x00\x00\x00"])
            elif kw == "set":
                m = re.match(r"set\s+(\w+)\s+to\s+(.+)$", l, re.I)
                vt = self.var_token(m.group(1))
                e = self.expr(m.group(2))
                body = vt + struct.pack("<H", len(e)) + e
                stmts.append(["stmt", struct.pack("<HH", 0x15, len(body)) + body])
            elif kw in ("if", "elseif"):
                e = self.expr(l[len(kw):])
                stmts.append([kw, e])
            elif kw == "else":
                stmts.append(["else", None])
            elif kw == "endif":
                stmts.append(["endif", b"\x19\x00\x00\x00"])
            elif kw == "message":
                m = re.match(r'message\s+"([^"%]*)"$', l, re.I)
                if not m:
                    raise CompileError(f"only plain Message \"text\" is supported: {l}")
                text = m.group(1).encode("cp1252")
                body = struct.pack("<HH", 1, len(text)) + text + b"\0" * 6
                stmts.append(["stmt", struct.pack("<HH", 0x1059, len(body)) + body])
            elif kw == "messagebox":
                texts = re.findall(r'"([^"%]*)"', l)
                if not texts or len(texts) > 10 or re.sub(r'"[^"]*"|[\s,]', "", l[len(kw):]):
                    raise CompileError(f"only MessageBox \"text\", \"button\"... is supported: {l}")
                msg, buttons = texts[0].encode("cp1252"), [t.encode("cp1252") for t in texts[1:]]
                body = struct.pack("<HH", 1, len(msg)) + msg + struct.pack("<HH", 0, len(buttons))
                for b in buttons:
                    body += struct.pack("<HH", 1, len(b)) + b
                stmts.append(["stmt", struct.pack("<HH", 0x1000, len(body)) + body])
            elif kw == "return":
                stmts.append(["stmt", b"\x1e\x00\x00\x00"])
            else:
                hasref, prefix, op, p, kinds = self.call(words[0], words[1:])
                b = (b"\x1c\x00" + prefix if hasref else b"") + struct.pack("<HH", op, len(p)) + p
                stmts.append(["stmt", b])
        # resolve jumps: count statements until the matching elseif/else/endif
        for i, s in enumerate(stmts):
            if s[0] in ("if", "elseif", "else"):
                depth, j = 0, i + 1
                while True:
                    k = stmts[j][0]
                    if k == "if":
                        depth += 1
                    elif k == "endif":
                        if depth == 0:
                            break
                        depth -= 1
                    elif k in ("elseif", "else") and depth == 0:
                        break
                    j += 1
                jump = j - i - 1
                if s[0] == "else":
                    s[1] = struct.pack("<HHH", 0x17, 2, jump)
                else:
                    e = s[1]
                    body = struct.pack("<HH", jump, len(e)) + e
                    s[1] = struct.pack("<HH", 0x16 if s[0] == "if" else 0x18, len(body)) + body
        # resolve begin block lengths
        out = b""
        for i, s in enumerate(stmts):
            if s[0] == "begin":
                j = i + 1
                while stmts[j][0] != "end":
                    j += 1
                blen = sum(len(x[1]) for x in stmts[i + 1:j + 1])
                mode, param = s[1]
                body = struct.pack("<HI", mode, blen)
                if param is not None:
                    body += param
                elif mode in (1, 2, 3):  # blocks with an optional parameter list
                    body += b"\x00\x00"
                s[1] = struct.pack("<HH", 0x10, len(body)) + body
        return b"".join(s[1] for s in stmts)
