-- CoopQuestSyncUI logic. main.lua runs this file again whenever it changes, so everything here must be safe to run
-- twice: state lives in the global CQS table, and hooks and loops are registered once and call through CQS, so a
-- reload swaps the functions they call.
--
-- What it does:
--   * Hides HUD pop-ups while CoopQuestSync.esp replays quest stages (the plugin frames every batch with two
--     Message lines), and always hides the "Co-op quest token" item notices.
--   * Runs console commands that the co-op client mod writes into command.txt (only `coc <cell>`).
--   * Tells the co-op client mod when a menu (map, inventory...) is open, through the file menu-open, so the
--     conversation overlay can step aside.
--   * Writes the class of every new UI widget to UE4SS.log once, to find widgets that need handling.

local S = CQS or {}
CQS = S

local START = "Co-op: syncing quest progress..."
local FINISH = "Co-op: quest progress synced"
local TOKEN = "Co-op quest token"
local MUTE_TIMEOUT = 120 -- seconds; never stay muted forever if the closing line is lost
local HIDDEN = 0.0001

local FOLDER = (os.getenv("APPDATA") or "") .. "\\ReadyM.Launcher\\Oblivion\\Mods\\CoopQuestSyncUI\\"
local COMMAND_FILE = FOLDER .. "command.txt"
local MENU_FILE = FOLDER .. "menu-open"
local ALLOWED = { "^coc [%w_]+$" }

S.muted = S.muted or false
S.mutedAt = S.mutedAt or 0
S.hiddenCount = S.hiddenCount or 0
S.seenClasses = S.seenClasses or {}
S.menuOpen = nil -- unknown after a (re)load, so the first check writes the file's state

local function log(text)
    print("[CoopQuestSyncUI] " .. text .. "\n")
end

-- HUD notifications --------------------------------------------------------------------------------------------

-- The HUD's text can carry markup in front, such as "[NL]", so look for the lines rather than compare them.
local function has(text, part)
    return text:find(part, 1, true) ~= nil
end

function S.onNotification(param)
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

    if S.muted and os.clock() - S.mutedAt > MUTE_TIMEOUT then
        log("mute timed out")
        S.muted = false
    end

    local hide = false
    if has(text, START) then
        S.muted = true
        S.mutedAt = os.clock()
        S.hiddenCount = 0
        hide = true
    elseif has(text, FINISH) then
        if S.muted then
            log("batch done, hid " .. S.hiddenCount .. " pop-up(s)")
        end
        S.muted = false
    elseif has(text, TOKEN) then
        hide = true
    elseif S.muted then
        hide = true
        S.hiddenCount = S.hiddenCount + 1
    end

    log((hide and "hide" or "show") .. (isQuest and " quest" or "") .. ": " .. text)
    if hide then
        pcall(function() vm.Notification.ShowSeconds = HIDDEN end)
    end
end

-- New widgets: each class once, with a note when it appears during a sync -----------------------------------------

function S.onNewWidget(widget)
    local ok, name = pcall(function() return widget:GetClass():GetFullName() end)
    if ok and name and not S.seenClasses[name] then
        S.seenClasses[name] = true
        log("new widget" .. (S.muted and " during sync" or "") .. ": " .. name)
    end
end

-- The local player's controller ---------------------------------------------------------------------------------

-- The one with a Player: FindFirstOf can return another controller, with no player.
local function playerController()
    if S.pc and S.pc:IsValid() then
        return S.pc
    end
    S.pc = nil
    local ok, helpers = pcall(require, "UEHelpers")
    if ok and type(helpers) == "table" and helpers.GetPlayerController then
        local okPc, pc = pcall(helpers.GetPlayerController)
        if okPc and pc and pc:IsValid() then
            S.pc = pc
            return pc
        end
    end
    for _, pc in ipairs(FindAllOf("PlayerController") or {}) do
        local good = false
        pcall(function() good = pc:IsValid() and pc.Player and pc.Player:IsValid() end)
        if good then
            S.pc = pc
            return pc
        end
    end
    return nil
end

-- Console commands -----------------------------------------------------------------------------------------------

local function allowed(cmd)
    for _, pattern in ipairs(ALLOWED) do
        if cmd:match(pattern) then
            return true
        end
    end
    return false
end

-- Two ways into the game's console from UE4SS; the log says which one worked, or why neither did.
local function runConsole(cmd)
    local pc = playerController()
    if not pc then
        return false, "no player controller"
    end
    local errors = {}
    local ok, err = pcall(function() pc:ConsoleCommand(cmd, true) end)
    if ok then
        return true, "PlayerController.ConsoleCommand"
    end
    errors[#errors + 1] = tostring(err)
    ok, err = pcall(function()
        local ksl = StaticFindObject("/Script/Engine.Default__KismetSystemLibrary")
        ksl:ExecuteConsoleCommand(pc, cmd, pc)
    end)
    if ok then
        return true, "KismetSystemLibrary.ExecuteConsoleCommand"
    end
    errors[#errors + 1] = tostring(err)
    return false, table.concat(errors, " / ")
end

function S.pollCommand()
    local f = io.open(COMMAND_FILE, "r")
    if not f then
        return
    end
    local cmd = (f:read("*l") or ""):gsub("%s+$", "")
    f:close()
    os.remove(COMMAND_FILE)
    if not allowed(cmd) then
        log("refused command: " .. cmd)
        return
    end
    ExecuteInGameThread(function()
        local ok, how = runConsole(cmd)
        log((ok and "ran " or "could not run ") .. cmd .. " (" .. how .. ")")
    end)
end

-- Menus ----------------------------------------------------------------------------------------------------------

-- A menu (map, inventory, journal...) shows the mouse cursor; walking around doesn't.
function S.checkMenu()
    local pc = playerController()
    local open = false
    if pc then
        pcall(function() open = pc.bShowMouseCursor == true end)
    end
    if open == S.menuOpen then
        return
    end
    S.menuOpen = open
    if open then
        local f = io.open(MENU_FILE, "w")
        if f then
            f:write("1")
            f:close()
        end
    else
        os.remove(MENU_FILE)
    end
    log(open and "menu open" or "menu closed")
end

-- Hooks and loops, registered once per game start ------------------------------------------------------------------

if not S.registered then
    S.registered = true

    pcall(NotifyOnNewObject, "/Script/UMG.UserWidget", function(widget) S.onNewWidget(widget) end)

    LoopAsync(1000, function()
        pcall(S.pollCommand)
        return false
    end)

    LoopAsync(250, function()
        ExecuteInGameThread(function() pcall(S.checkMenu) end)
        return false
    end)

    -- The HUD class may not be loaded when UE4SS starts mods, so keep trying until the hook takes.
    local tries = 0
    LoopAsync(2000, function()
        tries = tries + 1
        local ok, err = pcall(RegisterHook, "Function /Script/Altar.VHUDSubtitleViewModel:ConsumeNotification",
            function(param) S.onNotification(param) end)
        if ok then
            log("hooked HUD notifications")
            return true
        end
        if tries == 1 or tries % 30 == 0 then
            log("waiting for the HUD (" .. tostring(err) .. ")")
        end
        return false
    end)
end
