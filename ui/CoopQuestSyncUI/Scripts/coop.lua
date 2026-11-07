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
--
-- Crash safety (2026-11-07). UE4SS runs LoopAsync on its own thread, next to the game thread, and the game crashed
-- in that thread while loading. So this file keeps the add-on's footprint small: no watchers on new objects (they
-- fire thousands of times during a load), one LoopAsync that only looks at files, and all game work in a single
-- ExecuteInGameThread job that is never queued again before the last one has finished. The HUD hook is registered
-- outside that job, from its own LoopAsync. A file named "off" in the add-on folder stops everything (main.lua reads
-- it), to tell whether a crash comes from here; one named "lite" stops the regular menu checks.

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
S.menuOpen = nil -- unknown after a (re)load, so the first check writes the file's state
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

-- The local player's controller ---------------------------------------------------------------------------------

-- The one with a Player (the local player's), looked up fresh every time and only on the game thread: a controller
-- kept from before a map load points at freed memory, and touching it crashes the game.
local function playerController()
    for _, pc in ipairs(FindAllOf("PlayerController") or {}) do
        local good = false
        pcall(function() good = pc:IsValid() and pc.Player ~= nil and pc.Player:IsValid() end)
        if good then
            return pc
        end
    end
    return nil
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
-- to a new file), then run its lines in order. Game thread.
function S.runCommands()
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
    for _, cmd in ipairs(commands) do
        local ok, how = runConsole(cmd)
        log((ok and "ran " or "could not run ") .. cmd .. " (" .. how .. ")")
    end
end

-- Voices ---------------------------------------------------------------------------------------------------------
--
-- The remaster plays each spoken line with a Wwise event named after the original voice file:
-- Play_<race>_<sex>_[altvoice_|beggar_]<quest>_<topic>_<line form id>_<n>[_sid], kept in a folder per voice:
-- /Game/WwiseAudio/Events/Voice/oblivion/<race>/<sex>/[altvoice/] (seen in the game, 2026-11-06).

S.voiceRoot = S.voiceRoot or "/Game/WwiseAudio/Events/Voice/oblivion/"

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

-- Game thread.
function S.runVoice()
    local f = io.open(VOICE_FILE, "r")
    if not f then
        return
    end
    local spec = (f:read("*l") or ""):gsub("%s+$", "")
    f:close()
    os.remove(VOICE_FILE)
    local ok, err = pcall(S.playVoice, spec)
    if not ok then
        log("voice failed: " .. tostring(err))
    end
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

-- Answers the client's text requests: "key<TAB>text" a line, with line breaks in the text written as \n. At most
-- TEXT_PER_JOB keys a job, so a long list doesn't stall a frame; the rest wait for the next job. Game thread.
local TEXT_PER_JOB = 100
S.textQueue = S.textQueue or {}

function S.runText()
    os.remove(TEXT_TAKEN)
    if os.rename(TEXT_REQUEST, TEXT_TAKEN) then
        local f = io.open(TEXT_TAKEN, "r")
        if f then
            for line in f:lines() do
                local key = line:match("^(LOC_[%w_&%-]+)%s*$")
                if key and #S.textQueue < 2000 then
                    S.textQueue[#S.textQueue + 1] = key
                end
            end
            f:close()
        end
        os.remove(TEXT_TAKEN)
    end
    if #S.textQueue == 0 then
        return
    end
    local keys = {}
    while #keys < TEXT_PER_JOB and #S.textQueue > 0 do
        keys[#keys + 1] = table.remove(S.textQueue, 1)
    end
    local lines, found = {}, 0
    for _, key in ipairs(keys) do
        local ok, value = pcall(S.lookupText, key)
        value = ok and value or ""
        if value ~= "" then
            found = found + 1
        end
        lines[#lines + 1] = key .. "\t" .. value:gsub("\\", "\\\\"):gsub("\r?\n", "\\n"):gsub("\t", " ")
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

-- A menu (map, inventory, journal...) shows the mouse cursor; walking around doesn't. Game thread.
function S.menuCheck()
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

-- The game-thread job ---------------------------------------------------------------------------------------------

local MENU_EVERY = 0.5 -- seconds between menu checks
local GAP = 0.15       -- seconds between the end of one job and the next, so jobs never pile up

-- Kept across a reload of this file, so a job still waiting on the game thread isn't queued twice.
S.jobBusy = S.jobBusy or false
S.jobQueuedAt = S.jobQueuedAt or 0
S.jobDoneAt = S.jobDoneAt or -GAP
S.lastMenu = S.lastMenu or -MENU_EVERY

local function exists(path)
    local f = io.open(path, "r")
    if f then
        f:close()
        return true
    end
    return false
end

-- A file named "lite" (or "lite.txt") in the add-on folder stops the regular menu checks, so the add-on only touches
-- the game when the co-op mod asks for something (and for the HUD hook). To narrow down a crash.
S.lite = exists(FOLDER .. "lite") or exists(FOLDER .. "lite.txt")
if S.lite then
    log("lite: no menu checks (delete the file \"lite\" in the add-on folder to turn them back on)")
end

-- The HUD class may not be loaded when the add-on starts, so try again every couple of seconds until the hook takes.
-- This runs in its own LoopAsync, as in 0.3 to 0.5, never inside the ExecuteInGameThread job: hooking from there made
-- the game crash at launch (2026-11-07), most likely because UE4SS keeps the Lua thread a hook was registered from,
-- and the job's thread is gone once the job ends.
function S.hookHud()
    if S.hudHooked then
        return
    end
    S.hudTries = (S.hudTries or 0) + 1
    local ok, err = pcall(RegisterHook, "Function /Script/Altar.VHUDSubtitleViewModel:ConsumeNotification",
        function(param) S.onNotification(param) end)
    if ok then
        S.hudHooked = true
        log("hooked HUD notifications")
    elseif S.hudTries == 1 or S.hudTries % 30 == 0 then
        log("waiting for the HUD (" .. tostring(err) .. ")")
    end
end

-- Everything that touches the game, in one go, on the game thread.
function S.gameJob()
    if not S.languageWritten then
        S.languageWritten = true
        local ok, err = pcall(S.writeLanguage)
        if not ok then
            log("language: " .. tostring(err))
        end
    end
    if not S.lite and os.clock() - S.lastMenu >= MENU_EVERY then
        S.lastMenu = os.clock()
        pcall(S.menuCheck)
    end
    for _, step in ipairs({ S.runCommands, S.runVoice, S.runText }) do
        local ok, err = pcall(step)
        if not ok then
            log("job: " .. tostring(err))
        end
    end
end

-- The LoopAsync thread: never touches the game, only decides when the next job is due.
function S.tick()
    local now = os.clock()
    if S.jobBusy then
        if now - S.jobQueuedAt < 30 then
            return -- still waiting for the game thread (a load can take a while)
        end
        S.jobBusy = false -- never wait forever if a job got lost
    end
    if now - S.jobDoneAt < GAP then
        return
    end
    local due = (not S.lite and now - S.lastMenu >= MENU_EVERY) or not S.languageWritten
        or #S.textQueue > 0 or exists(COMMAND_FILE) or exists(VOICE_FILE) or exists(TEXT_REQUEST)
    if not due then
        return
    end
    S.jobBusy = true
    S.jobQueuedAt = now
    ExecuteInGameThread(function()
        pcall(S.gameJob)
        S.jobDoneAt = os.clock()
        S.jobBusy = false
    end)
end

-- Names an older coop.lua registered loops and watchers with, for a game that started with it and reloaded this
-- file: they do nothing now, and the job above does their work.
local function nothing() end
S.pollVoice, S.pollCommand, S.pollText, S.checkMenu = nothing, nothing, nothing, nothing
S.onNewWidget, S.onNewAkEvent, S.forgetController = nothing, nothing, nothing

if not S.registeredJob then
    S.registeredJob = true
    S.hudHooked = S.hudHooked or (S.registered == true) -- an older coop.lua already hooks the HUD
    LoopAsync(100, function()
        pcall(S.tick)
        return false
    end)
    LoopAsync(2000, function()
        pcall(S.hookHud)
        return S.hudHooked == true
    end)
end
