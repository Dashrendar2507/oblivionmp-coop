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
