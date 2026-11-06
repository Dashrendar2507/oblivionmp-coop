-- CoopQuestSyncUI logic. main.lua runs this file again whenever it changes, so everything here must be safe to run
-- twice: state lives in the global CQS table, and hooks and loops are registered once and call through CQS, so a
-- reload swaps the functions they call.
--
-- What it does:
--   * Hides HUD pop-ups while CoopQuestSync.esp replays quest stages (the plugin frames every batch with two
--     Message lines), and always hides the "Co-op quest token" item notices.
--   * Runs console commands that the co-op client mod writes into command.txt, one a line, only of the shapes in
--     ALLOWED: `coc <cell>`, `cow <world space> <x> <y>`, `<ref>.StopCombat`, `player.scaonactor` and
--     `player.SetCrimeGold 0`.
--   * Tells the co-op client mod when a menu (map, inventory...) is open, through the file menu-open, so the
--     conversation overlay can step aside.
--   * Plays the voice of a line in someone else's conversation, from nowhere, when the co-op client mod writes it
--     into voice.txt ("imperial_m ms26_ms26filecharges_000363be_1"; "stop" ends it).
--   * Writes the game's language to language.txt, so the co-op client mod reads the game's text in that language.
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
local VOICE_FILE = FOLDER .. "voice.txt"
local LANGUAGE_FILE = FOLDER .. "language.txt"
local COMMAND_TAKEN = FOLDER .. "command.run"
local ALLOWED = {
    "^coc [%w_]+$",                 -- into a building (back where you left, travel to a teammate)
    "^cow [%w_]+ %-?%d+ %-?%d+$",   -- to another world space
    "^%x+%.StopCombat$",            -- /coop calm: an NPC stops fighting
    "^player%.scaonactor$",         -- /coop calm: guards stop chasing you
    "^player%.SetCrimeGold 0$",     -- /coop calm: your bounty goes to 0
}

S.muted = S.muted or false
S.mutedAt = S.mutedAt or 0
S.hiddenCount = S.hiddenCount or 0
S.seenClasses = S.seenClasses or {}
S.menuOpen = nil -- unknown after a (re)load, so the first check writes the file's state
S.voiceEventsLogged = S.voiceEventsLogged or 0
S.described = S.described or {}

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

-- The client adds commands to command.txt, one a line. Take the file first (a rename, so lines added meanwhile go
-- to a new file), then run its lines in order.
function S.pollCommand()
    os.remove(COMMAND_TAKEN)
    if not os.rename(COMMAND_FILE, COMMAND_TAKEN) then
        return -- no file, or the client is writing it: next time
    end
    local f = io.open(COMMAND_TAKEN, "r")
    if not f then
        return
    end
    local commands = {}
    for line in f:lines() do
        local cmd = line:gsub("%s+$", "")
        if cmd ~= "" then
            if allowed(cmd) then
                commands[#commands + 1] = cmd
            else
                log("refused command: " .. cmd)
            end
        end
    end
    f:close()
    os.remove(COMMAND_TAKEN)
    if #commands == 0 then
        return
    end
    ExecuteInGameThread(function()
        for _, cmd in ipairs(commands) do
            local ok, how = runConsole(cmd)
            log((ok and "ran " or "could not run ") .. cmd .. " (" .. how .. ")")
        end
    end)
end

-- Voices ---------------------------------------------------------------------------------------------------------
--
-- The remaster plays each spoken line with a Wwise event named after the original voice file:
-- Play_<race>_<sex>_<quest>_<topic>_<line form id>_<n>, some with an extra "altvoice" or "beggar" after the sex, or
-- "_sid" at the end. Where the game keeps those events isn't known yet, so this tries the likely places, learns the
-- right one from any voice event the game loads itself, and writes what it tried to UE4SS.log.

local GUESSED_FOLDERS = {
    "/Game/WwiseAudio/Events/",
    "/Game/WwiseAudio/Events/English(US)/",
    "/Game/WwiseAudio/Event/",
    "/Game/WwiseAudio/Event/English(US)/",
    "/Game/WwiseAudio/Events/Dialogue/",
    "/Game/WwiseAudio/Events/VO/",
    "/Game/Audio/Events/",
    "/Game/Audio/Wwise/Events/",
}

local function looksLikeVoice(name)
    return name:lower():find("play_[%a_]+_[mf]_[%w_]+_%x%x%x%x%x%x%x%x_%d") ~= nil
end

-- "AkAudioEvent /Game/X/Y/Play_a.Play_a" -> "/Game/X/Y/"
local function folderOf(fullName)
    local path = fullName:match("^%S+%s+(%S+)$") or fullName
    return path:match("^(.*/)[^/]*$")
end

function S.onNewAkEvent(event)
    if S.voiceEventsLogged >= 25 then
        return
    end
    local ok, name = pcall(function() return event:GetFullName() end)
    if not ok or not name or not looksLikeVoice(name) then
        return
    end
    S.voiceEventsLogged = S.voiceEventsLogged + 1
    local folder = folderOf(name)
    if folder and folder ~= S.voiceFolder then
        S.voiceFolder = folder
        S.voiceFolderFound = true
        log("learned where voice events live: " .. folder)
    end
    log("voice event loaded by the game: " .. name)
end

-- A function's parameters, written to the log once, for when a call fails.
local function describe(path)
    if S.described[path] then
        return
    end
    S.described[path] = true
    local fn = StaticFindObject(path)
    if not fn or not fn:IsValid() then
        log("  " .. path .. ": not found")
        return
    end
    local params = {}
    pcall(function()
        fn:ForEachProperty(function(prop)
            params[#params + 1] = prop:GetFName():ToString() .. ":" .. prop:GetClass():GetFName():ToString()
        end)
    end)
    log("  " .. path .. "(" .. table.concat(params, ", ") .. ")")
end

local function voiceNames(voice, line)
    local race, sex = voice:match("^(.+)_([mf])$")
    if not race then
        return {}
    end
    local names = {}
    for _, variant in ipairs({ "", "altvoice_", "beggar_" }) do
        local base = "Play_" .. race .. "_" .. sex .. "_" .. variant .. line
        names[#names + 1] = base
        names[#names + 1] = base .. "_sid"
    end
    return names
end

local function findEvent(voice, line)
    local names = voiceNames(voice, line)
    local folders = {}
    if S.voiceFolder then
        folders[#folders + 1] = S.voiceFolder
    end
    for _, f in ipairs(GUESSED_FOLDERS) do
        if f ~= S.voiceFolder then
            folders[#folders + 1] = f
        end
    end
    -- Already loaded?
    for _, folder in ipairs(folders) do
        for _, name in ipairs(names) do
            local path = folder .. name .. "." .. name
            local ok, obj = pcall(StaticFindObject, path)
            if ok and obj and obj:IsValid() then
                S.voiceFolderFound = true
                return obj, path
            end
        end
    end
    -- Load it. Loading what isn't there costs a little each time, so after a few misses stop guessing until the
    -- game shows where its voice events are.
    if (S.voiceMisses or 0) >= 3 and not S.voiceFolderFound then
        return nil, "nothing found in the first lines; waiting to learn where voice events live (talk to an NPC yourself)"
    end
    if type(LoadAsset) == "function" then
        for _, folder in ipairs(folders) do
            for _, name in ipairs(names) do
                local path = folder .. name .. "." .. name
                local ok, obj = pcall(LoadAsset, path)
                if ok and obj and obj:IsValid() then
                    if folder ~= S.voiceFolder then
                        S.voiceFolder = folder
                        log("voice events live in " .. folder)
                    end
                    S.voiceFolderFound = true
                    return obj, path
                end
            end
        end
    else
        log("this UE4SS has no LoadAsset")
    end
    S.voiceMisses = (S.voiceMisses or 0) + 1
    return nil, table.concat(names, " / ") .. " in " .. #folders .. " folders"
end

local function statics()
    return StaticFindObject("/Script/AkAudio.Default__AkGameplayStatics")
end

-- Plays an event on the local player: heard from where the player stands, so from nowhere in particular.
local function postEvent(event, pawn)
    local tries = {
        { "AkAudioEvent:PostOnActor", "/Script/AkAudio.AkAudioEvent:PostOnActor",
            function() return event:PostOnActor(pawn, {}, 0, false) end },
        { "AkGameplayStatics:PostEvent", "/Script/AkAudio.AkGameplayStatics:PostEvent",
            function() return statics():PostEvent(event, pawn, 0, {}, false) end },
        { "AkGameplayStatics:PostEvent (4)", "/Script/AkAudio.AkGameplayStatics:PostEvent",
            function() return statics():PostEvent(event, pawn, 0, {}) end },
    }
    local errors = {}
    for _, t in ipairs(tries) do
        local ok, id = pcall(t[3])
        if ok then
            return true, t[1], id
        end
        errors[#errors + 1] = t[1] .. ": " .. tostring(id):gsub("\n.*", "")
        describe(t[2])
    end
    return false, table.concat(errors, " | ")
end

local function stopVoice()
    if not S.playingId or S.playingId == 0 then
        return
    end
    local id = S.playingId
    S.playingId = nil
    local ok, err = pcall(function() statics():ExecuteActionOnPlayingID(0, id, 0, 0) end)
    if not ok and not S.stopWarned then
        S.stopWarned = true
        log("could not stop a voice: " .. tostring(err):gsub("\n.*", ""))
        describe("/Script/AkAudio.AkGameplayStatics:ExecuteActionOnPlayingID")
    end
end

function S.playVoice(spec)
    if spec == "stop" then
        stopVoice()
        return
    end
    local voice, line = spec:match("^([a-z0-9_]+) ([a-z0-9_]+)$")
    if not voice then
        log("refused voice: " .. spec)
        return
    end
    stopVoice()
    local pc = playerController()
    local pawn = nil
    if pc then
        pcall(function() pawn = pc.Pawn end)
    end
    if not pawn or not pawn:IsValid() then
        log("no player to play " .. spec .. " on")
        return
    end
    local event, where = findEvent(voice, line)
    if not event then
        log("no voice event for " .. spec .. " (tried " .. where .. ")")
        return
    end
    local ok, how, id = postEvent(event, pawn)
    if ok then
        S.playingId = type(id) == "number" and id or nil
        log("voice " .. where .. " (" .. how .. ", playing id " .. tostring(id) .. ")")
    else
        log("could not play " .. where .. ": " .. how)
    end
end

function S.pollVoice()
    local f = io.open(VOICE_FILE, "r")
    if not f then
        return
    end
    local spec = (f:read("*l") or ""):gsub("%s+$", "")
    f:close()
    os.remove(VOICE_FILE)
    ExecuteInGameThread(function()
        local ok, err = pcall(S.playVoice, spec)
        if not ok then
            log("voice failed: " .. tostring(err))
        end
    end)
end

-- The game's language ----------------------------------------------------------------------------------------------

function S.writeLanguage()
    local lib = StaticFindObject("/Script/Engine.Default__KismetInternationalizationLibrary")
    if not lib or not lib:IsValid() then
        log("can't tell the game's language (no KismetInternationalizationLibrary)")
        return
    end
    local found = nil
    for _, fn in ipairs({ "GetCurrentLanguage", "GetCurrentCulture" }) do
        local ok, value = pcall(function() return lib[fn](lib):ToString() end)
        if ok and type(value) == "string" and value:match("^[%a][%w%-]+$") then
            found = value
            break
        end
    end
    if not found then
        log("can't tell the game's language")
        return
    end
    local f = io.open(LANGUAGE_FILE, "w")
    if f then
        f:write(found)
        f:close()
    end
    log("game language: " .. found)
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

ExecuteInGameThread(function()
    local ok, err = pcall(S.writeLanguage)
    if not ok then
        log("language: " .. tostring(err))
    end
end)

if not S.registered then
    S.registered = true

    pcall(NotifyOnNewObject, "/Script/UMG.UserWidget", function(widget) S.onNewWidget(widget) end)
    pcall(NotifyOnNewObject, "/Script/AkAudio.AkAudioEvent", function(event) S.onNewAkEvent(event) end)

    LoopAsync(100, function()
        pcall(S.pollVoice)
        return false
    end)

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
