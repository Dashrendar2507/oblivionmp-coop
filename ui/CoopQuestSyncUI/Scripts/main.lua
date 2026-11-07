-- CoopQuestSyncUI: the UE4SS half of the CoopQuestSync co-op mod.
--
-- This file loads coop.lua (next to it) once, when the game starts. The co-op mod copies a newer add-on in when the
-- player joins a server that has one, and asks for a restart of the game: the add-on doesn't reload itself while
-- the game runs any more, because that reload ran in UE4SS's background thread (2026-11-07 crashes).
--
-- It also writes running.txt in the add-on folder: a fingerprint of the main.lua and coop.lua it started with (or
-- "off"), so the co-op mod can tell that the game still runs an older add-on and keep asking for a restart.
--
-- A file named "off" (or "off.txt") in the add-on folder keeps the add-on from doing anything, to tell whether a
-- crash comes from it.

local function log(text)
    print("[CoopQuestSyncUI] " .. text .. "\n")
end

local FOLDER = (os.getenv("APPDATA") or "") .. "\\ReadyM.Launcher\\Oblivion\\Mods\\CoopQuestSyncUI\\"

local function scriptsFolder()
    local source = debug.getinfo(1, "S").source or ""
    local folder = source:match("^@(.*[\\/])")
    if folder then
        return folder
    end
    if source:sub(1, 1) == "@" then
        return "" -- started from its own folder
    end
    return FOLDER .. "Scripts\\"
end

local function read(path)
    local f = io.open(path, "rb")
    if not f then
        return nil
    end
    local data = f:read("a")
    f:close()
    return data
end

local function exists(path)
    local f = io.open(path, "r")
    if f then
        f:close()
        return true
    end
    return false
end

-- FNV-1a, 32 bits, as hex. The co-op mod computes the same over the files it installed.
local function fingerprint(...)
    local h = 2166136261
    for _, data in ipairs({ ... }) do
        for i = 1, #data do
            h = ((h ~ data:byte(i)) * 16777619) & 0xFFFFFFFF
        end
    end
    return string.format("%08x", h)
end

local function writeRunning(text)
    local f = io.open(FOLDER .. "running.txt", "w")
    if f then
        f:write(text)
        f:close()
    end
end

local SCRIPTS = scriptsFolder()
local LOGIC = SCRIPTS .. "coop.lua"

if exists(FOLDER .. "off") or exists(FOLDER .. "off.txt") then
    writeRunning("off")
    log("switched off (delete the file \"off\" in the add-on folder and restart the game to turn it on)")
    return
end

local code = read(LOGIC)
if not code then
    writeRunning("missing")
    log("no coop.lua in " .. LOGIC)
    return
end
writeRunning(fingerprint(read(SCRIPTS .. "main.lua") or "", code))

local chunk, err = load(code, "@coop.lua")
if not chunk then
    log("coop.lua has an error: " .. tostring(err))
    return
end
local ok, runErr = pcall(chunk)
log(ok and "coop.lua loaded" or ("coop.lua failed: " .. tostring(runErr)))
