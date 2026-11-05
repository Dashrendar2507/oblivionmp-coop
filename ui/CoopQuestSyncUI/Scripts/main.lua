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
local current = nil

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

reload()
LoopAsync(3000, function()
    reload()
    return false
end)

log("loaded")
