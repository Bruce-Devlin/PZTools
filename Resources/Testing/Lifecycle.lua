-- Isolated session lifecycle receipts. Never loaded into the ordinary user cache.
if PZToolsLifecycle then return end
PZToolsLifecycle = true
local config = getFileReader("pztools-lifecycle.txt", false)
if not config then return end
local session = config:readLine()
local expected = config:readLine()
local mode = config:readLine()
local world = config:readLine()
config:close()
local marker, initialErrors, exiting, observed
local lastPoll = 0
local function receipt(value)
    local writer = getFileWriter("pztools-lifecycle-status.txt", true, false)
    writer:write(session .. "\n" .. value .. "\n")
    writer:close()
end
local function data()
    if isServer() then return ModData.getOrCreate("PZToolsPersistenceProbe") end
    local player = getSpecificPlayer(0)
    if player and not player:isDead() then return player:getModData() end
end
local function poll()
    if getTimestampMs() - lastPoll < 500 then return end
    lastPoll = getTimestampMs()
    local state = data()
    if not state then return end
    if not observed then
        observed = true
        receipt(expected ~= "" and (state.PZToolsPersistenceToken == expected and "restart-marker-verified" or "restart-marker-missing") or "ready")
    end
    if exiting then return end
    local command = getFileReader("pztools-lifecycle-command.txt", false)
    if not command then return end
    local id = command:readLine(); local token = command:readLine(); command:close()
    if id ~= session or not token or token == "" then return end
    marker = token; state.PZToolsPersistenceToken = marker
    initialErrors = type(getLuaDebuggerErrorCount) == "function" and getLuaDebuggerErrorCount() or nil
    exiting = true
    receipt("save-requested:" .. marker)
    if not isServer() then
        -- The production Quit to desktop path force-saves player data and the world.
        require "OptionScreens/MainScreen"
        MainScreen:quitToDesktop()
    end
end
Events.OnTick.Add(poll)
if Events.OnTickEvenPaused then Events.OnTickEvenPaused.Add(poll) end
Events.OnSave.Add(function()
    if marker then receipt("saving:" .. marker) end
end)
if Events.OnPostSave then Events.OnPostSave.Add(function()
    if not marker then return end
    local errors = type(getLuaDebuggerErrorCount) == "function" and getLuaDebuggerErrorCount() or nil
    receipt((initialErrors and errors == initialErrors and "post-save:" or "post-save-unverified:") .. marker)
end) end
if not isServer() and expected ~= "" and world and world ~= "" then
    local loading = false
    Events.OnMainMenuEnter.Add(function()
        if loading then return end
        loading = true
        getWorld():setGameMode(mode); getWorld():setWorld(world)
        MainScreen.continueLatestSave(mode, world)
    end)
end
