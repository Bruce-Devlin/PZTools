-- Shared by the isolated unit runner and the actual game runtime.
if PZT then return end
PZT = { cases = {}, current = nil, finished = false, before = {}, after = {} }
local function fail(message) error(tostring(message), 2) end
function test(name, body)
    assert(type(name) == "string" and type(body) == "function", "test(name, function) expected")
    table.insert(PZT.cases, { name = name, body = body, file = PZT.file, before = PZT.before, after = PZT.after })
end
function beforeEach(fn) table.insert(PZT.before, fn) end
function afterEach(fn) table.insert(PZT.after, fn) end
function PZT.context()
    local t = {}
    function t:equal(actual, expected, message)
        if actual ~= expected then fail((message or "Values differ") .. ": expected " .. tostring(expected) .. ", got " .. tostring(actual)) end
    end
    function t:truthy(value, message) if not value then fail(message or "Expected truthy value") end end
    function t:throws(fn, text)
        local ok, err = pcall(fn)
        if ok then fail("Expected an error") end
        if text and not string.find(tostring(err), text, 1, true) then fail("Unexpected error: " .. tostring(err)) end
    end
    function t:step(name, fn)
        PZT.emit("step", PZT.current.file, PZT.current.name, name, 0)
        return fn(self)
    end
    function t:waitUntil(query, timeout)
        local deadline = PZT.now() + (timeout or 10)
        while not query() do
            if PZT.now() >= deadline then fail("Condition timed out after " .. tostring(timeout or 10) .. " seconds") end
            coroutine.yield()
        end
    end
    function t:expect(query)
        local expectation = {}
        function expectation:equals(expected) t:equal(type(query) == "function" and query() or query, expected) end
        function expectation:eventuallyEquals(expected, options)
            local actual
            t:waitUntil(function() actual = query(); return actual == expected end, (options or {}).timeoutSeconds)
        end
        return expectation
    end
    function t:cleanup(fn) table.insert(PZT.current.cleanup, fn) end
    if PZT.extend then PZT.extend(t) end
    return t
end
function PZT.tick()
    if PZT.finished then return end
    if not PZT.current then
        if #PZT.cases == 0 then PZT.finished = true; PZT.emit("complete", "", "", "", 0); return end
        local c = table.remove(PZT.cases, 1)
        if not c then PZT.finished = true; PZT.emit("complete", "", "", "", 0); return end
        PZT.current = c; c.started = PZT.now(); c.cleanup = {}; c.context = PZT.context()
        PZT.emit("begin", c.file, c.name, "", 0)
        c.thread = coroutine.create(function()
            for _, fn in ipairs(c.before) do fn(c.context) end
            c.body(c.context)
        end)
    end
    local c = PZT.current
    local ok, err = coroutine.resume(c.thread)
    if PZT.now() - c.started > (PZT.timeout or 60) then ok = false; err = "Test timeout" end
    if not ok or coroutine.status(c.thread) == "dead" then
        for _, fn in ipairs(c.after) do
            local clean, message = pcall(fn, c.context)
            if not clean then ok = false; err = tostring(err or "") .. " teardown: " .. tostring(message) end
        end
        for i = #c.cleanup, 1, -1 do
            local clean, message = pcall(c.cleanup[i])
            if not clean then ok = false; err = tostring(err or "") .. " cleanup: " .. tostring(message) end
        end
        PZT.emit(ok and "passed" or "failed", c.file, c.name, ok and "" or tostring(err), PZT.now() - c.started)
        PZT.current = nil
    end
end
