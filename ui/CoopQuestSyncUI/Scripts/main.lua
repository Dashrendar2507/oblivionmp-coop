-- CoopQuestSyncUI: the UE4SS half of the CoopQuestSync co-op mod.
--
-- This file only loads coop.lua (next to it), and loads it again whenever it changes. The co-op mod updates
-- coop.lua when the player joins a server with a newer version, so changes take effect within seconds instead of
-- needing a restart of the game. Only a change to this file needs a restart.

local function log(text)
    print("[CoopQuestSyncUI] " .. text .. "\n")
end

local function scriptsFolder()
    local source = debug.getinfo(1, "S").source or ""
    local folder = source:match("^@(.*[\\/])")
    if folder then
        return folder
    end
    if source:sub(1, 1) == "@" then
        return "" -- started from its own folder
    end
    return (os.getenv("APPDATA") or "") .. "\\ReadyM.Launcher\\Oblivion\\Mods\\CoopQuestSyncUI\\Scripts\\"
end

local LOGIC = scriptsFolder() .. "coop.lua"
-- A file named "off" in the add-on folder keeps the add-on from doing anything, to tell whether a crash comes from it.
-- Windows hides extensions, so a new text document named "off" is really "off.txt": both count.
local FOLDER = (os.getenv("APPDATA") or "") .. "\\ReadyM.Launcher\\Oblivion\\Mods\\CoopQuestSyncUI\\"
local current = nil

local function switchedOff()
    for _, name in ipairs({ "off", "off.txt" }) do
        local f = io.open(FOLDER .. name, "r")
        if f then
            f:close()
            return true
        end
    end
    return false
end

local function reload()
    local f = io.open(LOGIC, "rb")
    if not f then
        if current ~= false then
            log("no coop.lua in " .. LOGIC)
            current = false
        end
        return
    end
    local code = f:read("*a")
    f:close()
    if code == current then
        return
    end
    current = code
    local chunk, err = load(code, "@coop.lua")
    if not chunk then
        log("coop.lua has an error: " .. tostring(err))
        return
    end
    local ok, runErr = pcall(chunk)
    log(ok and "coop.lua loaded" or ("coop.lua failed: " .. tostring(runErr)))
end

if switchedOff() then
    log("switched off (delete the file \"off\" in the add-on folder and restart the game to turn it on)")
    return
end

reload()
LoopAsync(3000, function()
    reload()
    return false
end)

log("loaded")
