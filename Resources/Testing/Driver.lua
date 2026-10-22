require "PZToolsTesting/Harness"
if PZTest then return end
local reader = getFileReader("pztests-config.txt", false)
if not reader then return end
local runId = reader:readLine()
local endpoint = reader:readLine()
local saveMode = reader:readLine()
local saveName = reader:readLine()
local purpose = reader:readLine()
reader:close()
if not runId or not endpoint then return end
local sequence = 0
local function escape(value)
    return tostring(value or ""):gsub("%%", "%%25"):gsub("\t", "%%09"):gsub("\r", "%%0D"):gsub("\n", "%%0A")
end
PZT.now = function() return getTimestampMs() / 1000 end
if type(getLuaDebuggerErrorCount) == "function" then
    PZT.errorCount = getLuaDebuggerErrorCount
    PZT.lastError = function()
        if type(getLuaDebuggerErrors) == "function" then
            local errors = getLuaDebuggerErrors()
            if errors and errors:size() > 0 then return tostring(errors:get(errors:size() - 1)) end
        end
        return "Native error logged by the game; see console.txt"
    end
end
PZT.emit = function(kind, file, name, message, duration)
    sequence = sequence + 1
    local writer = getFileWriter("pztests-events.txt", true, true)
    writer:write(runId .. "\t" .. endpoint .. "\t" .. sequence .. "\t" .. kind .. "\t" .. escape(file) .. "\t" .. escape(name) .. "\t" .. escape(message) .. "\t" .. tostring(duration or 0) .. "\n")
    writer:close()
end
PZT.emit("ready", "", "", "Driver loaded", 0)

local adapters = {}
PZTest = { endpoint = endpoint }
function PZTest.register(name, fn) adapters[name] = fn end
local replies = {}
local rpcId = 0
local function invoke(args)
    local fn = adapters[args.action]
    if not fn then return { id = args.id, ok = false, error = "Unknown adapter: " .. tostring(args.action), source = args.source } end
    local ok, result = pcall(fn, args.arguments)
    return { id = args.id, ok = ok, value = ok and result or nil, error = not ok and tostring(result) or nil, source = args.source }
end
if isServer() then
    Events.OnClientCommand.Add(function(module, command, player, args)
        if module ~= "PZToolsTesting" or args.runId ~= runId then return end
        if command == "request" then
            if args.target == "server" then
                local reply = invoke(args); reply.runId = runId
                sendServerCommand(player, module, "reply", reply)
            else sendServerCommand(module, "request", args) end
        elseif command == "reply" then
            if args.source == "server" then replies[args.id] = args
            else sendServerCommand(module, "reply", args) end
        end
    end)
else
    Events.OnServerCommand.Add(function(module, command, args)
        if module ~= "PZToolsTesting" or args.runId ~= runId then return end
        if command == "request" and args.target == endpoint then
            local reply = invoke(args); reply.runId = runId
            sendClientCommand(module, "reply", reply)
        elseif command == "reply" and args.source == endpoint then replies[args.id] = args end
    end)
end

PZT.extend = function(t)
    t.endpoint = endpoint
    function t:remote(target, action, arguments, timeout)
        if target == endpoint then
            self:truthy(adapters[action], "Unknown adapter: " .. action)
            return adapters[action](arguments)
        end
        self:truthy(isClient() or isServer(), "Remote actors require a multiplayer session")
        rpcId = rpcId + 1
        local id = endpoint .. ":" .. rpcId
        local args = { runId = runId, id = id, source = endpoint, target = target, action = action, arguments = arguments or {} }
        if isServer() then sendServerCommand("PZToolsTesting", "request", args)
        else sendClientCommand("PZToolsTesting", "request", args) end
        self:waitUntil(function() return replies[id] ~= nil end, timeout or 15)
        local reply = replies[id]; replies[id] = nil
        self:truthy(reply.ok, reply.error)
        return reply.value
    end
    function t:actor(index)
        self:truthy(not isServer(), "Player actions require a client endpoint")
        local player = getSpecificPlayer(type(index) == "number" and index or 0)
        self:truthy(player, "Player is not loaded")
        local actor = {}
        function actor:player() return player end
        function actor:inventoryCount(fullType)
            local count = 0; local items = player:getInventory():getItems()
            for i = 0, items:size() - 1 do if items:get(i):getFullType() == fullType then count = count + 1 end end
            return count
        end
        function actor:givenItem(fullType)
            local item = player:getInventory():AddItem(fullType)
            t:truthy(item, "Unknown item: " .. fullType)
            return item
        end
        function actor:givenItems(items)
            for fullType, count in pairs(items) do for i = 1, count do self:givenItem(fullType) end end
        end
        function actor:attemptsTo(...)
            for _, task in ipairs({...}) do task(self, t) end
        end
        return actor
    end
    function t:fixture(name)
        self:truthy(adapters["fixture:" .. name], "Register fixture with PZTest.register('fixture:" .. name .. "', fn)")
        return adapters["fixture:" .. name](self)
    end
end

Tasks = {}
function Tasks.perform(name, fn)
    return function(actor, t) t:step(name, function() fn(actor, t) end) end
end
function Tasks.walkTo(x, y, z)
    return Tasks.perform("Walk to " .. x .. "," .. y, function(actor, t)
        require "TimedActions/WalkToTimedAction"
        local square
        t:waitUntil(function() square = getCell():getGridSquare(x, y, z); return square ~= nil end, 15)
        ISTimedActionQueue.add(ISWalkToTimedAction:new(actor:player(), square))
        t:waitUntil(function()
            local p = actor:player()
            return math.abs(p:getX() - (x + 0.5)) < 0.8 and math.abs(p:getY() - (y + 0.5)) < 0.8 and math.floor(p:getZ()) == z
        end, 30)
    end)
end
function Tasks.equip(fullType)
    return Tasks.perform("Equip " .. fullType, function(actor, t)
        require "TimedActions/ISEquipWeaponAction"
        local item = actor:player():getInventory():getFirstTypeRecurse(fullType)
        t:truthy(item, "Item is not in inventory: " .. fullType)
        ISTimedActionQueue.add(ISEquipWeaponAction:new(actor:player(), item, 50, true, false))
        t:waitUntil(function() return actor:player():getPrimaryHandItem() == item end, 15)
    end)
end
function Tasks.action(name, factory, completed, timeout)
    return Tasks.perform(name, function(actor, t)
        local action = factory(actor:player())
        t:truthy(action, "Action factory did not return an action")
        ISTimedActionQueue.add(action)
        t:waitUntil(function() return completed(actor:player(), action) end, timeout or 30)
    end)
end

local started = false
local worldEntered = false
local supportReady = false
local released = false
local simulationTicks = 0
local nextHeartbeat = 0
local function heartbeat(force)
    if PZT.finished then return end
    if not force and PZT.now() < nextHeartbeat then return end
    nextHeartbeat = PZT.now() + 1
    local player = not isServer() and getSpecificPlayer(0) or nil
    local present = isServer() or player ~= nil and not player:isDead()
    local paused = "unknown"
    if not isServer() and UIManager and UIManager.getSpeedControls then
        local controls = UIManager.getSpeedControls()
        if controls then paused = tostring(controls:getCurrentGameSpeed() == 0) end
    end
    local phase = released and "running" or not worldEntered and "awaiting_world" or not present and "awaiting_player"
        or not supportReady and "support_setup" or not released and "awaiting_barrier" or "running"
    PZT.emit("status", "", PZT.current and PZT.current.name or "", phase .. "|" .. simulationTicks .. "|" .. paused .. "|" .. tostring(present) .. "|" .. tostring(supportReady), PZT.now())
end
if Events.OnTickEvenPaused then
    Events.OnTickEvenPaused.Add(function()
        if PZT.watchdog then PZT.watchdog() end
        heartbeat(false)
    end)
end
local function start()
    worldEntered = true
    if started then return end
    if not isServer() then
        local player = getSpecificPlayer(0)
        if not player or player:isDead() then
            heartbeat(true)
            return
        end
    end
    started = true
    PZT.emit("ready", "", "", "World started", 0)
    if purpose == "setup" then return end
    local ok, err = pcall(function()
        require "PZToolsTesting/Manifest"
        for _, setup in ipairs(PZTestSupport or {}) do setup() end
        for _, entry in ipairs(PZTestManifest) do
            if entry.endpoint == endpoint or entry.endpoint == "all-clients" and not isServer() then
                PZT.file = entry.file; PZT.before = {}; PZT.after = {}
                local loaded, message = pcall(entry.body)
                if not loaded then PZT.emit("error", entry.file, "", tostring(message), 0) end
            end
        end
    end)
    if not ok then PZT.emit("error", "", "", tostring(err), 0); PZT.finished = true; return end
    supportReady = true
    -- Support adapters are registered before the barrier opens. Client 1 cannot
    -- race client 2's character creation, nor can server tests run too early.
    local nextCheck = 0
    Events.OnTick.Add(function()
        if PZT.finished then return end
        simulationTicks = simulationTicks + 1
        if not released and PZT.now() >= nextCheck then
            nextCheck = PZT.now() + 0.25
            local gate = getFileReader("pztests-go.txt", false)
            if gate then released = gate:readLine() == runId; gate:close() end
        end
        heartbeat(simulationTicks <= 2)
        if released then PZT.tick() end
    end)
end
if isServer() then Events.OnServerStarted.Add(start)
else
    Events.OnGameStart.Add(start)
    Events.OnTick.Add(function() if worldEntered and not started then start() end end)
    if purpose == "setup" then
        Events.OnPlayerDeath.Add(function(player)
            if player == getSpecificPlayer(0) then
                PZT.emit("error", "", "", "The test character died. Capture a new fixture with a living character.", 0)
            end
        end)
    end
    -- Uses the game's own continue-save workflow, including its compatibility checks.
    local loading = false
    Events.OnMainMenuEnter.Add(function()
        if loading or saveName == "" or not saveName then return end
        loading = true
        local ok, err = pcall(function()
            getWorld():setGameMode(saveMode)
            getWorld():setWorld(saveName)
            assert(checkSavePlayerExists(), "The source save has no living character. Choose a living-character save in Playtest Lab.")
            local info = getSaveInfo(saveName)
            assert(info and tonumber(info.worldVersion) == IsoWorld.getWorldVersion(),
                "The source save needs conversion or is incompatible. Open and save it in this game build before running tests.")
            MainScreen.continueLatestSave(saveMode, saveName)
        end)
        if not ok then PZT.emit("error", "", "", "Save startup: " .. tostring(err), 0) end
    end)
end
