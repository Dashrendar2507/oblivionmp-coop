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
--   * Looks up the game's own text (names, dialogue, quest names) by key for the co-op client mod, in the language
--     the game runs in: keys in text-request.txt, answers in text-reply.txt. The game's files are compressed in a
--     way the mod can't read, but the running game has the text at hand.
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
local TEXT_REQUEST = FOLDER .. "text-request.txt"
local TEXT_TAKEN = FOLDER .. "text-request.run"
local TEXT_REPLY = FOLDER .. "text-reply.txt"
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

-- The one with a Player (the local player's). Never kept across a map load: a controller from before the load
-- points at freed memory, and touching it crashes the game. So the cached one is dropped when a map starts loading
-- or a new controller appears, and looked up again every couple of seconds anyway.
S.pc = nil
S.pcAt = 0
S.loading = false
S.loadingAt = 0

local function playerController()
    if S.loading and os.time() - S.loadingAt < 30 then
        return nil
    end
    S.loading = false
    if S.pc and os.time() - S.pcAt < 2 then
        return S.pc
    end
    S.pc = nil
    for _, pc in ipairs(FindAllOf("PlayerController") or {}) do
        local good = false
        pcall(function() good = pc:IsValid() and pc.Player ~= nil and pc.Player:IsValid() end)
        if good then
            S.pc = pc
            S.pcAt = os.time()
            return pc
        end
    end
    return nil
end

function S.forgetController(why)
    S.pc = nil
    if why then
        S.loading = why == "loading"
        S.loadingAt = os.time()
    end
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

-- Console commands -----------------------------------------------------------------------------------------------

local function allowed(cmd)
    for _, pattern in ipairs(ALLOWED) do
        if cmd:match(pattern) then
            return true
        end
    end
    return false
end

-- Two ways into the game's console from UE4SS; the log says which one worked, or why neither did. The first is
-- the one other Oblivion Remastered mods use: KismetSystemLibrary with the local player as the world context.
local function runConsole(cmd)
    local pc = playerController()
    if not pc then
        return false, "no player controller"
    end
    local errors = {}
    local ok, err = pcall(function()
        local ksl = StaticFindObject("/Script/Engine.Default__KismetSystemLibrary")
        ksl:ExecuteConsoleCommand(pc.Player, cmd, pc)
    end)
    if ok then
        return true, "KismetSystemLibrary.ExecuteConsoleCommand"
    end
    errors[#errors + 1] = tostring(err):gsub("\n.*", "")
    ok, err = pcall(function() pc:ConsoleCommand(cmd, true) end)
    if ok then
        return true, "PlayerController.ConsoleCommand"
    end
    errors[#errors + 1] = tostring(err):gsub("\n.*", "")
    describe("/Script/Engine.KismetSystemLibrary:ExecuteConsoleCommand")
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
-- Play_<race>_<sex>_[altvoice_|beggar_]<quest>_<topic>_<line form id>_<n>[_sid], kept in a folder per voice:
-- /Game/WwiseAudio/Events/Voice/oblivion/<race>/<sex>/[altvoice/] (seen in the game, 2026-11-06). The root is
-- learned again from any voice event the game loads itself, in case it differs.

S.voiceRoot = S.voiceRoot or "/Game/WwiseAudio/Events/Voice/oblivion/"

local function looksLikeVoice(name)
    return name:lower():find("play_[%a_]+_[mf]_[%w_]+_%x%x%x%x%x%x%x%x_%d") ~= nil
end

-- "AkAudioEvent /Game/X/Voice/oblivion/dark_elf/m/altvoice/Play_a.Play_a" -> "/Game/X/Voice/oblivion/"
local function rootOf(fullName)
    local path = fullName:match("^%S+%s+(%S+)$") or fullName
    return path:match("^(.*/)[%w_]+/[mf]/[^/]*$") or path:match("^(.*/)[%w_]+/[mf]/[%w_]+/[^/]*$")
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
    local root = rootOf(name)
    if root and root ~= S.voiceRoot then
        S.voiceRoot = root
        log("learned where voice events live: " .. root)
    end
    log("voice event loaded by the game: " .. name)
end

-- Where the event for this line can be: "/Game/.../dark_elf/m/altvoice/Play_dark_elf_m_altvoice_x.Play_..."
local function voicePaths(voice, line)
    local race, sex = voice:match("^(.+)_([mf])$")
    if not race then
        return {}
    end
    local paths = {}
    for _, variant in ipairs({ "", "altvoice", "beggar" }) do
        local folder = S.voiceRoot .. race .. "/" .. sex .. "/" .. (variant ~= "" and variant .. "/" or "")
        local base = "Play_" .. race .. "_" .. sex .. "_" .. (variant ~= "" and variant .. "_" or "") .. line
        for _, name in ipairs({ base, base .. "_sid" }) do
            paths[#paths + 1] = folder .. name .. "." .. name
        end
    end
    return paths
end

local function findEvent(voice, line)
    local paths = voicePaths(voice, line)
    for _, path in ipairs(paths) do
        local ok, obj = pcall(StaticFindObject, path)
        if ok and obj and obj:IsValid() then
            return obj, path
        end
    end
    if type(LoadAsset) ~= "function" then
        return nil, "this UE4SS has no LoadAsset"
    end
    for _, path in ipairs(paths) do
        local ok, obj = pcall(LoadAsset, path)
        if ok and obj and obj:IsValid() then
            return obj, path
        end
    end
    return nil, table.concat(paths, " / ")
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

-- Also answers text requests: the 100 ms loop calls this, so a game running an older coop.lua picks it up on reload.
function S.pollVoice()
    pcall(S.pollText)
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

-- The game's text ------------------------------------------------------------------------------------------------
--
-- The remaster keeps its text in string tables, one per kind, named after the key's prefix: LOC_FN_ (full names) in
-- ST_FullNames, LOC_RT_ (dialogue) in ST_ResponseTexts, and so on. The running game has them loaded, in its language.

local TEXT_TABLES = {
    FN = "ST_FullNames", RT = "ST_ResponseTexts", LE = "ST_LogEntries", DE = "ST_Descriptions",
    BK = "ST_BookContent", SC = "ST_ScriptContent", HC = "ST_HardcodedContent", AD = "ST_AltarDynamicTexts",
    AS = "ST_AltarStaticTexts", ME = "ST_MissingEntries",
}

S.tableIds = S.tableIds or nil

-- The registered string tables' ids, by short name ("ST_FullNames" -> "/Game/.../ST_FullNames.ST_FullNames").
local function tableIds()
    if S.tableIds then
        return S.tableIds
    end
    local ids = {}
    local ok, err = pcall(function()
        local lib = StaticFindObject("/Script/Engine.Default__KismetStringTableLibrary")
        local list = lib:GetRegisteredStringTables()
        list:ForEach(function(_, element)
            local id = element:get():ToString()
            local short = id:match("([%w_]+)$")
            if short then
                ids[short] = id
            end
        end)
    end)
    local count = 0
    for _ in pairs(ids) do count = count + 1 end
    if count > 0 then
        S.tableIds = ids
        log("string tables: " .. count .. (ids.ST_FullNames and (" (names: " .. ids.ST_FullNames .. ")") or ""))
    elseif not S.tablesWarned then
        S.tablesWarned = true
        log("no string tables found" .. (ok and "" or (": " .. tostring(err):gsub("\n.*", ""))))
    end
    return ids
end

local function usable(text, key)
    return type(text) == "string" and text ~= "" and text ~= key and not text:find("MISSING STRING TABLE ENTRY", 1, true)
end

function S.lookupText(key)
    local prefix = key:match("^LOC_(%u%u)_")
    local namespace = prefix and TEXT_TABLES[prefix]
    if not namespace then
        return nil
    end
    local lib = StaticFindObject("/Script/Engine.Default__KismetTextLibrary")
    if not lib or not lib:IsValid() then
        return nil
    end
    -- The string table by its id: the localised text.
    local id = tableIds()[namespace]
    if id then
        local ok, text = pcall(function() return lib:TextFromStringTable(FName(id), key):ToString() end)
        if ok and usable(text, key) then
            return text
        end
        if not ok and not S.textWarned then
            S.textWarned = true
            log("TextFromStringTable failed: " .. tostring(text):gsub("\n.*", ""))
            describe("/Script/Engine.KismetTextLibrary:TextFromStringTable")
        end
    end
    -- Or the localisation table by namespace and key.
    local ok, text = pcall(function()
        local out = {}
        lib:FindTextInLocalizationTable(namespace, key, out, "")
        local value = out.OutText or out[3]
        return value and value:ToString()
    end)
    if ok and usable(text, key) then
        return text
    end
    if not ok and not S.findWarned then
        S.findWarned = true
        log("FindTextInLocalizationTable failed: " .. tostring(text):gsub("\n.*", ""))
        describe("/Script/Engine.KismetTextLibrary:FindTextInLocalizationTable")
    end
    return nil
end

-- Answers the client's text requests: "key<TAB>text" a line, with line breaks in the text written as \n.
function S.pollText()
    os.remove(TEXT_TAKEN)
    if not os.rename(TEXT_REQUEST, TEXT_TAKEN) then
        return
    end
    local f = io.open(TEXT_TAKEN, "r")
    if not f then
        return
    end
    local keys = {}
    for line in f:lines() do
        local key = line:match("^(LOC_[%w_&%-]+)%s*$")
        if key and #keys < 500 then
            keys[#keys + 1] = key
        end
    end
    f:close()
    os.remove(TEXT_TAKEN)
    if #keys == 0 then
        return
    end
    ExecuteInGameThread(function()
        local lines, found = {}, 0
        for _, key in ipairs(keys) do
            local ok, text = pcall(S.lookupText, key)
            text = ok and text or ""
            if text ~= "" then
                found = found + 1
            end
            lines[#lines + 1] = key .. "\t" .. text:gsub("\\", "\\\\"):gsub("\r?\n", "\\n"):gsub("\t", " ")
        end
        local out = io.open(TEXT_REPLY, "a")
        if out then
            out:write(table.concat(lines, "\n") .. "\n")
            out:close()
        end
        if not S.textLogged or found < #keys then
            S.textLogged = true
            log("text: found " .. found .. " of " .. #keys .. " key(s)" .. (found < #keys and (", e.g. not " .. keys[1]) or ""))
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

-- Added after the first registration, so a game already running with an older coop.lua gets them on reload too.
if not S.registeredLoadHooks then
    S.registeredLoadHooks = true
    -- Drop the cached controller whenever it may go away (see playerController).
    pcall(NotifyOnNewObject, "/Script/Engine.PlayerController", function() S.forgetController() end)
    if type(RegisterLoadMapPreHook) == "function" then
        pcall(RegisterLoadMapPreHook, function() S.forgetController("loading") end)
    end
    if type(RegisterLoadMapPostHook) == "function" then
        pcall(RegisterLoadMapPostHook, function() S.forgetController("loaded") end)
    end
end

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
