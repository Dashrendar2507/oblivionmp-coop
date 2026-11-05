-- CoopQuestSyncUI: hides HUD pop-ups while CoopQuestSync.esp replays quest stages.
--
-- The plugin frames every batch of replayed stages with two Message lines. Everything the HUD would show
-- between them (quest updates, items added or removed by stage scripts) is hidden, so catching up on join is
-- silent. The plugin's own hidden token items never show a pop-up. Each pop-up the add-on sees is written to
-- UE4SS.log, which helps when something slips through.
--
-- The hook (VHUDSubtitleViewModel:ConsumeNotification, then shortening Notification.ShowSeconds) is the
-- technique the Oblivion Archipelago bridge uses for the same job.

local START = "Co-op: syncing quest progress..."
local FINISH = "Co-op: quest progress synced"
local TOKEN = "Co-op quest token"
local MUTE_TIMEOUT = 120 -- seconds; never stay muted forever if the closing line is lost
local HIDDEN = 0.0001

local muted = false
local mutedAt = 0
local hiddenCount = 0

local function log(text)
    print("[CoopQuestSyncUI] " .. text .. "\n")
end

local function onNotification(param)
    local okVm, vm = pcall(function() return param:get() end)
    if not okVm or not vm or not vm.Notification then
        return
    end
    local okText, text = pcall(function() return vm.Notification.Text:ToString() end)
    if not okText or type(text) ~= "string" then
        return
    end
    local isQuest = false
    pcall(function() isQuest = vm.Notification.bIsQuest end)

    if muted and os.clock() - mutedAt > MUTE_TIMEOUT then
        log("mute timed out")
        muted = false
    end

    local hide = false
    if text == START then
        muted = true
        mutedAt = os.clock()
        hiddenCount = 0
        hide = true
    elseif text == FINISH then
        watchUntil = os.clock() + WATCH_AFTER
        if muted then
            log("batch done, hid " .. hiddenCount .. " pop-up(s)")
        end
        muted = false
    elseif text:find(TOKEN, 1, true) then
        hide = true
    elseif muted then
        hide = true
        hiddenCount = hiddenCount + 1
    end

    log((hide and "hide" or "show") .. (isQuest and " quest" or "") .. ": " .. text)
    if hide then
        pcall(function() vm.Notification.ShowSeconds = HIDDEN end)
    end
end

-- Finding the "Set as active quest / Continue" box: it is not a HUD notification, so the hook above misses it.
-- While a batch runs, and for a while after, write the class of every new UI widget to UE4SS.log, so the next
-- version can close that box by itself.
local WATCH_AFTER = 20 -- seconds after a batch
local watchUntil = 0
local seenClasses = {}

local function onNewWidget(widget)
    if not muted and os.clock() > watchUntil then
        return
    end
    local ok, name = pcall(function() return widget:GetClass():GetFullName() end)
    if ok and name and not seenClasses[name] then
        seenClasses[name] = true
        log("new widget during sync: " .. name)
    end
end

pcall(NotifyOnNewObject, "/Script/UMG.UserWidget", onNewWidget)

-- Console commands from the co-op client mod. The mod can't run console commands, so it writes one into
-- command.txt in this add-on's folder; this runs it and deletes the file. Only the shapes below are run (taking a
-- player back inside the building they left from), whatever the file says.
local COMMAND_FILE = (os.getenv("APPDATA") or "") .. "\\ReadyM.Launcher\\Oblivion\\Mods\\CoopQuestSyncUI\\command.txt"
local ALLOWED = { "^coc [%w_]+$" }

local function allowed(cmd)
    for _, pattern in ipairs(ALLOWED) do
        if cmd:match(pattern) then
            return true
        end
    end
    return false
end

-- The local player's controller: the one with a Player (FindFirstOf can return another controller, with no player).
local function playerController()
    local ok, helpers = pcall(require, "UEHelpers")
    if ok and helpers and helpers.GetPlayerController then
        local pc = helpers.GetPlayerController()
        if pc and pc:IsValid() then
            return pc
        end
    end
    for _, pc in ipairs(FindAllOf("PlayerController") or {}) do
        if pc:IsValid() and pc.Player and pc.Player:IsValid() then
            return pc
        end
    end
    return nil
end

-- The game's console takes Oblivion commands. Two ways in from UE4SS; the log says which one worked.
local function runConsole(cmd)
    local pc = playerController()
    if not pc then
        return false, "no player controller"
    end
    local ok, err = pcall(function()
        local ksl = StaticFindObject("/Script/Engine.Default__KismetSystemLibrary")
        ksl:ExecuteConsoleCommand(pc.Player, cmd, pc)
    end)
    if ok then
        return true, "KismetSystemLibrary.ExecuteConsoleCommand"
    end
    local ok2, err2 = pcall(function() pc:ConsoleCommand(cmd, true) end)
    if ok2 then
        return true, "PlayerController.ConsoleCommand"
    end
    return false, tostring(err) .. " / " .. tostring(err2)
end

LoopAsync(1000, function()
    local f = io.open(COMMAND_FILE, "r")
    if not f then
        return false
    end
    local cmd = (f:read("*l") or ""):gsub("%s+$", "")
    f:close()
    os.remove(COMMAND_FILE)
    if not allowed(cmd) then
        log("refused command: " .. cmd)
        return false
    end
    ExecuteInGameThread(function()
        local ok, how = runConsole(cmd)
        log((ok and "ran " or "could not run ") .. cmd .. " (" .. how .. ")")
    end)
    return false
end)

-- The HUD class may not be loaded when UE4SS starts mods, so keep trying until the hook takes.
local tries = 0
LoopAsync(2000, function()
    tries = tries + 1
    local ok, err = pcall(RegisterHook, "Function /Script/Altar.VHUDSubtitleViewModel:ConsumeNotification", onNotification)
    if ok then
        log("hooked HUD notifications")
        return true
    end
    if tries == 1 or tries % 30 == 0 then
        log("waiting for the HUD (" .. tostring(err) .. ")")
    end
    return false
end)

log("loaded")
