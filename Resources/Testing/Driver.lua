require "PZToolsTesting/Harness"
if PZTest then return end
local reader = getFileReader("pztests-config.txt", false)
if not reader then return end
local runId = reader:readLine()
local endpoint = reader:readLine()
local saveMode = reader:readLine()
local saveName = reader:readLine()
reader:close()
if not runId or not endpoint then return end
local sequence = 0
local function escape(value)
    return tostring(value or ""):gsub("%%", "%%25"):gsub("\t", "%%09"):gsub("\r", "%%0D"):gsub("\n", "%%0A")
end
PZT.now = function() return getTimestampMs() / 1000 end
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
            assert(adapters[action], "Unknown adapter: " .. action)
            return adapters[action](arguments)
        end
        assert(isClient() or isServer(), "Remote actors require a multiplayer session")
        rpcId = rpcId + 1
        local id = endpoint .. ":" .. rpcId
        local args = { runId = runId, id = id, source = endpoint, target = target, action = action, arguments = arguments or {} }
        if isServer() then sendServerCommand("PZToolsTesting", "request", args)
        else sendClientCommand("PZToolsTesting", "request", args) end
        self:waitUntil(function() return replies[id] ~= nil end, timeout or 15)
        local reply = replies[id]; replies[id] = nil
        assert(reply.ok, reply.error)
        return reply.value
    end
    function t:actor(index)
        assert(not isServer(), "Player actions require a client endpoint")
        local player = getSpecificPlayer(type(index) == "number" and index or 0)
        assert(player, "Player is not loaded")
        local actor = {}
        function actor:player() return player end
        function actor:inventoryCount(fullType)
            local count = 0; local items = player:getInventory():getItems()
            for i = 0, items:size() - 1 do if items:get(i):getFullType() == fullType then count = count + 1 end end
            return count
        end
        function actor:givenItem(fullType)
            local item = player:getInventory():AddItem(fullType)
            assert(item, "Unknown item: " .. fullType)
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
        assert(adapters["fixture:" .. name], "Register fixture with PZTest.register('fixture:" .. name .. "', fn)")
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
        assert(item, "Item is not in inventory: " .. fullType)
        ISTimedActionQueue.add(ISEquipWeaponAction:new(actor:player(), item, 50, true, false))
        t:waitUntil(function() return actor:player():getPrimaryHandItem() == item end, 15)
    end)
end
function Tasks.action(name, factory, completed, timeout)
    return Tasks.perform(name, function(actor, t)
        local action = factory(actor:player())
        assert(action, "Action factory did not return an action")
        ISTimedActionQueue.add(action)
        t:waitUntil(function() return completed(actor:player(), action) end, timeout or 30)
    end)
end

local started = false
local function start()
    if started then return end
    started = true
    PZT.emit("ready", "", "", "World started", 0)
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
    if not ok then PZT.emit("error", "", "", tostring(err), 0) end
    Events.OnTick.Add(PZT.tick)
end
if isServer() then Events.OnServerStarted.Add(start)
else
    Events.OnGameStart.Add(start)
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
