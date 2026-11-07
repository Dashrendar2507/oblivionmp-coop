-- CoopQuestSyncUI logic. main.lua runs this file once, when the game starts: an update takes a restart of the game.
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
-- Crash safety (2026-11-07): see "The game-thread job" at the end. A file named "off" in the add-on folder stops
-- everything (main.lua reads it); one named "lite" stops the regular menu checks.

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
        S.syncEndedAt = os.clock()
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
    local ok, err = pcall(function()
        fn:ForEachProperty(function(prop)
            local name = prop:GetFName():ToString()
            local kind = "?"
            pcall(function() kind = prop:GetClass():GetFName():ToString() end)
            params[#params + 1] = name .. ":" .. kind
        end)
    end)
    log("  " .. path .. "(" .. table.concat(params, ", ") .. ")" .. (ok and "" or (" [can't list: " .. tostring(err):gsub("\n.*", "") .. "]")))
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

-- Into the game's console from UE4SS. Other Oblivion Remastered mods call KismetSystemLibrary.ExecuteConsoleCommand
-- with the local player as the world context and three arguments, but OblivionMP's UE4SS asks for four (2026-11-07),
-- so a few shapes are tried; the log says which one worked, or why none did. A shape that worked is tried first next
-- time.
local function consoleShapes(pc, cmd)
    local ksl = StaticFindObject("/Script/Engine.Default__KismetSystemLibrary")
    return {
        { "KSL(player, cmd, pc, nil)", function() ksl:ExecuteConsoleCommand(pc.Player, cmd, pc, nil) end },
        { "KSL(pc, cmd, pc, nil)", function() ksl:ExecuteConsoleCommand(pc, cmd, pc, nil) end },
        { "KSL(player, cmd, pc, false)", function() ksl:ExecuteConsoleCommand(pc.Player, cmd, pc, false) end },
        { "KSL(player, cmd, pc)", function() ksl:ExecuteConsoleCommand(pc.Player, cmd, pc) end },
        { "pc:ConsoleCommand(cmd, true)", function() pc:ConsoleCommand(cmd, true) end },
    }
end

local function runConsole(cmd)
    local pc = playerController()
    if not pc then
        return false, "no player controller"
    end
    local shapes = consoleShapes(pc, cmd)
    if S.consoleShape then
        table.insert(shapes, 1, table.remove(shapes, S.consoleShape))
    end
    local errors = {}
    for _, shape in ipairs(shapes) do
        local ok, err = pcall(shape[2])
        if ok then
            for n, original in ipairs(consoleShapes(pc, cmd)) do
                if original[1] == shape[1] then
                    S.consoleShape = n
                end
            end
            return true, shape[1]
        end
        errors[#errors + 1] = shape[1] .. ": " .. tostring(err):gsub("\n.*", "")
    end
    describe("/Script/Engine.KismetSystemLibrary:ExecuteConsoleCommand")
    describe("/Script/Engine.PlayerController:ConsoleCommand")
    return false, table.concat(errors, " / ")
end

-- The client adds commands to command.txt, one a line. Take the file first (a rename, so lines added meanwhile go
-- to a new file) into a queue, then run one line per job, and only while the game is quiet: a console command run
-- while the game closes a menu (OblivionMP shuts the quest pop-up during the join catch-up) crashed the game
-- (2026-11-07, coc and StopCombat). Game thread.
S.commandQueue = S.commandQueue or {}
local QUIET_AFTER_SYNC = 3 -- seconds after the catch-up ends
local QUIET_AFTER_MENU = 2 -- seconds after a menu opens or closes
local COMMAND_MAX_WAIT = 30 -- run anyway after this long, so a stuck menu state can't hold commands forever

local function takeCommands()
    os.remove(COMMAND_TAKEN)
    if not os.rename(COMMAND_FILE, COMMAND_TAKEN) then
        return -- no file, or the client is writing it: next time
    end
    local f = io.open(COMMAND_TAKEN, "r")
    if not f then
        return
    end
    for line in f:lines() do
        local cmd = line:gsub("%s+$", "")
        if cmd ~= "" then
            if allowed(cmd) then
                S.commandQueue[#S.commandQueue + 1] = { cmd = cmd, at = os.clock() }
            else
                log("refused command: " .. cmd)
            end
        end
    end
    f:close()
    os.remove(COMMAND_TAKEN)
end

-- Why commands should wait now, or nil when the game is quiet.
function S.commandBusy()
    local now = os.clock()
    if S.muted then
        return "quest catch-up running"
    end
    if S.syncEndedAt and now - S.syncEndedAt < QUIET_AFTER_SYNC then
        return "quest catch-up just ended"
    end
    if S.menuOpen then
        return "a menu is open"
    end
    if S.menuChangedAt and now - S.menuChangedAt < QUIET_AFTER_MENU then
        return "a menu just opened or closed"
    end
    return nil
end

-- The game's camera fade, black over the world (menus and the client's pages stay on top). Game thread.
local BLACK = { R = 0.0, G = 0.0, B = 0.0, A = 1.0 }
local FADE_OUT = 0.3
local FADE_IN = 0.6
local FADE_MAX = 40 -- the world comes back after this long whatever happens

local function cameraFade(from, to, duration, hold)
    local pc = playerController()
    if not pc then
        return false
    end
    local ok, err = pcall(function()
        local pcm = pc.PlayerCameraManager
        if not pcm or not pcm:IsValid() then
            error("no camera manager")
        end
        pcm:StartCameraFade(from, to, duration, BLACK, false, hold)
    end)
    if not ok then
        log("camera fade failed: " .. tostring(err))
    end
    return ok
end

-- A coc or cow tears down OblivionMP's pages (the client's black travel screen too) as the load starts, and the game
-- showed a second of the world before its own fade (2026-11-07). So the world fades to black first and stays black
-- until the client says the player is in the new place (a file named fade-in), or FADE_MAX.
function S.fadeCheck()
    if not S.fadeHeld then
        return
    end
    local asked = os.rename(FOLDER .. "fade-in", FOLDER .. "fade-in.taken")
    if asked or os.clock() - S.fadeHeldAt > FADE_MAX then
        os.remove(FOLDER .. "fade-in.taken")
        S.fadeHeld = false
        cameraFade(1.0, 0.0, FADE_IN, false)
        log(asked and "world back after the move" or "world back after " .. FADE_MAX .. " s")
    end
end

function S.runCommands()
    takeCommands()
    local next = S.commandQueue[1]
    if not next then
        return
    end
    local busy = S.commandBusy()
    if busy then
        if os.clock() - next.at < COMMAND_MAX_WAIT then
            if S.commandWaitLogged ~= next then
                S.commandWaitLogged = next
                log("waiting to run " .. next.cmd .. ": " .. busy)
            end
            return
        end
        log("running " .. next.cmd .. " after " .. COMMAND_MAX_WAIT .. " s even though " .. busy)
    end
    local moves = next.cmd:match("^coc ") or next.cmd:match("^cow ")
    if moves and not next.faded then
        next.faded = true
        if cameraFade(0.0, 1.0, FADE_OUT, true) then
            S.fadeHeld = true
            S.fadeHeldAt = os.clock()
            next.runAfter = os.clock() + FADE_OUT + 0.1
            os.remove(FOLDER .. "fade-in")
        end
    end
    if next.runAfter and os.clock() < next.runAfter then
        return
    end
    table.remove(S.commandQueue, 1)
    local ok, how = runConsole(next.cmd)
    log((ok and "ran " or "could not run ") .. next.cmd .. " (" .. how .. ")")
    if moves and ok then
        -- The client's black travel screen stays up from here, and tells the add-on when the player has arrived.
        local f = io.open(FOLDER .. "travel-ran", "w")
        if f then
            f:write(next.cmd)
            f:close()
        end
    elseif moves and S.fadeHeld then
        S.fadeHeld = false
        cameraFade(1.0, 0.0, FADE_IN, false)
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

-- Plays an event where the local player stands, so it's heard from nowhere in particular. Only calls without a
-- callback: passing an empty callback (PostOnActor, PostEvent) crashed the game (2026-11-07).
local function postEvent(event, pawn)
    local where = nil
    pcall(function() where = pawn:K2_GetActorLocation() end)
    if not where then
        return false, "can't tell where the player is"
    end
    local loc = { X = where.X, Y = where.Y, Z = where.Z }
    local rot = { Pitch = 0, Yaw = 0, Roll = 0 }
    local tries = {
        { "PostEventAtLocation (4)", function() return statics():PostEventAtLocation(event, loc, rot, pawn) end },
        { "PostEventAtLocation (5)", function() return statics():PostEventAtLocation(event, loc, rot, "", pawn) end },
    }
    local errors = {}
    for _, t in ipairs(tries) do
        local ok, id = pcall(t[2])
        if ok then
            return true, t[1], id
        end
        errors[#errors + 1] = t[1] .. ": " .. tostring(id):gsub("\n.*", "")
    end
    describe("/Script/AkAudio.AkGameplayStatics:PostEventAtLocation")
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

-- A UE4SS value as text: an FName or FString, wrapped or not.
local function asText(value)
    for _, get in ipairs({
        function() return value:ToString() end,
        function() return value:get():ToString() end,
        function() return value:get() end,
    }) do
        local ok, text = pcall(get)
        if ok and type(text) == "string" then
            return text
        end
    end
    return type(value) == "string" and value or nil
end

-- The registered string tables' ids, by short name ("ST_FullNames" -> "/Game/.../ST_FullNames.ST_FullNames").
local function tableIds()
    if S.tableIds then
        return S.tableIds
    end
    local ids = {}
    local ok, err = pcall(function()
        local lib = StaticFindObject("/Script/Engine.Default__KismetStringTableLibrary")
        local list = lib:GetRegisteredStringTables()
        local function add(element)
            local id = asText(element)
            local short = id and id:match("([%w_]+)$")
            if short then
                ids[short] = id
            end
        end
        if type(list) == "table" then
            for _, element in pairs(list) do
                add(element)
            end
        else
            list:ForEach(function(_, element) add(element) end)
        end
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
    -- The string table by its id: the localised text. The id the game registered, or the plain table name.
    local candidates = {}
    if tableIds()[namespace] then
        candidates[#candidates + 1] = tableIds()[namespace]
    end
    candidates[#candidates + 1] = namespace
    for _, id in ipairs(candidates) do
        local ok, text = pcall(function() return lib:TextFromStringTable(FName(id), key):ToString() end)
        if ok and usable(text, key) then
            return text
        end
        if not S.textWarned then
            S.textWarned = true
            log("TextFromStringTable(" .. id .. ", " .. key .. ") " .. (ok and ("gave " .. tostring(text)) or ("failed: " .. tostring(text):gsub("\n.*", ""))))
            if not ok then
                describe("/Script/Engine.KismetTextLibrary:TextFromStringTable")
            end
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

-- The "Quest Added" pop-up ---------------------------------------------------------------------------------------
--
-- Replaying stages on join brings up the game's full-screen "Quest Added" pop-up (not a HUD notice, so the hook above
-- can't hide it). Game thread.

-- What it is (2026-11-07 log): WBP_ModernMenu_QuestSkillPopup_C (a VLegacyQuestAddedMenu, CommonUI activatable
-- widget) in the HUD's primary game layout, holding a WBP_ModernMenu_QuestAdded_C. Its "Continue" button runs
-- OnQuitButtonPressed, the game's own way to close it. During the join's catch-up (and a few seconds after) the
-- add-on presses it, so the catch-up stays silent; a quest that comes later, live, still shows its pop-up.
local POPUP = "WBP_ModernMenu_QuestSkillPopup_C"
local CLOSE_AFTER_SYNC = 5 -- seconds after a catch-up ends that its pop-ups still get closed

function S.questAddedCheck()
    local catchUp = S.muted or (S.syncEndedAt and os.clock() - S.syncEndedAt < CLOSE_AFTER_SYNC)
    if not catchUp or not S.menuOpen then
        S.popupTries = 0
        return
    end
    local okFind, popup = pcall(FindFirstOf, POPUP)
    if not okFind or not popup or not popup:IsValid() then
        return
    end
    local visible = false
    pcall(function() visible = popup:IsVisible() end)
    if not visible then
        return
    end
    -- A pop-up that was just closed may still be fading out: give each try time to work.
    if S.popupTriedAt and os.clock() - S.popupTriedAt < 1.5 then
        return
    end
    S.popupTriedAt = os.clock()
    S.popupTries = (S.popupTries or 0) + 1
    if S.popupTries > 3 then
        if S.popupTries == 4 then
            log("could not close the Quest Added pop-up: close it by hand")
        end
        return
    end
    -- The game's own Continue first; if it is still up a second later, CommonUI's deactivate.
    local how = S.popupTries == 1 and "OnQuitButtonPressed" or "DeactivateWidget"
    local ok, err = pcall(function() popup[how](popup) end)
    log((ok and "closed" or "could not close") .. " the Quest Added pop-up (" .. how .. ")" .. (ok and "" or ": " .. tostring(err)))
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
    S.menuChangedAt = os.clock()
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
--
-- UE4SS runs Lua in two places: the game thread (hooks, ExecuteInGameThread) and its LoopAsync thread, at the same
-- time, on the same Lua state, with no lock. Whatever runs on both at once can corrupt it, and the game crashes in
-- UE4SS a while later (2026-11-07: in the HUD hook, and in the loop). So:
--   * the only Lua that runs on the LoopAsync thread is the few lines below that queue the job; they make nothing
--     new, so they never set off Lua's garbage collector there;
--   * the collector is stopped and run in small steps inside the job, on the game thread;
--   * the HUD hook is registered from this file's first run, when the add-on starts, not from a loop or a job;
--   * everything else (files, menus, commands, voices, text) happens inside the job.

local LOOP_MS = 250    -- how often the job runs
local MENU_EVERY = 0.5 -- seconds between menu checks
local GC_LIMIT = 65536 -- KB; a full collection if memory ever grows past this

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

S.jobBusy = false
S.jobQueuedAt = 0
S.lastMenu = -MENU_EVERY

local function hookHud()
    local ok, err = pcall(RegisterHook, "Function /Script/Altar.VHUDSubtitleViewModel:ConsumeNotification",
        function(param) S.onNotification(param) end)
    if ok then
        S.hudHooked = true
        log("hooked HUD notifications")
    end
    return ok, err
end

-- Everything that touches the game, in one go, on the game thread.
function S.gameJob()
    collectgarbage("step", 0)
    if collectgarbage("count") > GC_LIMIT then
        collectgarbage("collect")
    end
    if not S.languageWritten then
        S.languageWritten = true
        local ok, err = pcall(S.writeLanguage)
        if not ok then
            log("language: " .. tostring(err))
        end
    end
    if not S.hudHooked and not S.hudGaveUp then
        -- Only when the first try at start failed (the HUD wasn't loaded yet). From the job, as a last resort.
        S.hudTries = (S.hudTries or 1) + 1
        local ok, err = hookHud()
        if not ok and S.hudTries % 40 == 0 then
            log("waiting for the HUD (" .. tostring(err) .. ")")
        end
        if not ok and S.hudTries >= 400 then
            S.hudGaveUp = true
            log("gave up hooking the HUD: quest pop-ups won't be hidden")
        end
    end
    if not S.lite and os.clock() - S.lastMenu >= MENU_EVERY then
        S.lastMenu = os.clock()
        pcall(S.menuCheck)
        pcall(S.questAddedCheck)
    end
    for _, step in ipairs({ S.fadeCheck, S.runCommands, S.runVoice, S.runText }) do
        local ok, err = pcall(step)
        if not ok then
            log("job: " .. tostring(err))
        end
    end
end

-- Made once, so queueing it makes nothing new.
local function runJob()
    pcall(S.gameJob)
    S.jobBusy = false
end

-- The LoopAsync thread: queue the job unless the last one hasn't run yet (a load can hold the game thread a while).
local function tick()
    if S.jobBusy then
        if os.clock() - S.jobQueuedAt < 30 then
            return false
        end
    end
    S.jobBusy = true
    S.jobQueuedAt = os.clock()
    ExecuteInGameThread(runJob)
    return false
end

if not S.started then
    S.started = true
    collectgarbage("stop")
    local ok, err = hookHud()
    if not ok then
        log("the HUD isn't loaded yet (" .. tostring(err):gsub("\n.*", "") .. "); trying again from the game thread")
    end
    LoopAsync(LOOP_MS, tick)
end
