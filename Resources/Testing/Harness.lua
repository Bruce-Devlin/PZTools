-- Shared by the isolated unit runner and the actual game runtime.
if PZT then return end
PZT = { cases = {}, current = nil, finished = false, before = {}, after = {} }
-- Never throw an assertion through Kahlua's coroutine.resume. A failed case
-- yields once and is discarded by the owner. Protected phases cannot yield.
local function fail(message, origin)
    local c = PZT.current
    if not c or (c.protectedDepth or 0) > 0 then error(tostring(message), 2) end
    c.failure = tostring(message); c.failureOrigin = origin or "assertion"
    coroutine.yield()
end
local function errorCount() return PZT.errorCount and PZT.errorCount() or nil end
local function loggedError() return PZT.lastError and PZT.lastError() or "Native error logged by the game; see console.txt" end
-- Some installed game builds log exposed Java exceptions and return normally
-- to pcall. Observe the game's error counter around synchronous callbacks too.
local function protected(fn, context)
    local before = errorCount()
    local ok, result = pcall(fn, context)
    local after = errorCount()
    if before and after and after ~= before then
        if PZT.current then PZT.current.handledErrors = after end
        if ok then return false, loggedError() end
    end
    return ok, result
end
function test(name, body)
    assert(type(name) == "string" and type(body) == "function", "test(name, function) expected")
    table.insert(PZT.cases, { name = name, body = body, file = PZT.file, before = PZT.before, after = PZT.after })
end
function beforeEach(fn) table.insert(PZT.before, fn) end
function afterEach(fn) table.insert(PZT.after, fn) end
function PZT.context()
    local t = {}
    function t:fail(message) fail(message or "Test failed") end
    function t:phase(name, fn)
        local c = PZT.current
        c.protectedDepth = (c.protectedDepth or 0) + 1
        local ok, result = protected(fn, self)
        c.protectedDepth = c.protectedDepth - 1
        if not ok then fail(name .. ": " .. tostring(result), "native_phase") end
        return result
    end
    function t:equal(actual, expected, message)
        if actual ~= expected then fail((message or "Values differ") .. ": expected " .. tostring(expected) .. ", got " .. tostring(actual)) end
    end
    function t:truthy(value, message) if not value then fail(message or "Expected truthy value") end end
    function t:throws(fn, text)
        local c = PZT.current
        c.protectedDepth = (c.protectedDepth or 0) + 1
        local ok, err = protected(fn)
        c.protectedDepth = c.protectedDepth - 1
        if ok then fail("Expected an error") end
        if text and not string.find(tostring(err), text, 1, true) then fail("Unexpected error: " .. tostring(err)) end
    end
    function t:step(name, fn)
        PZT.emit("step", PZT.current.file, PZT.current.name, name, 0)
        return fn(self)
    end
    function t:waitUntil(query, timeout)
        local deadline = PZT.now() + (timeout or 10)
        while not self:phase("wait query", query) do
            if PZT.now() >= deadline then fail("Condition timed out after " .. tostring(timeout or 10) .. " seconds") end
            if (PZT.current.protectedDepth or 0) > 0 then error("waitUntil cannot yield inside a synchronous protected phase", 2) end
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
local function finalize(c, message, origin, fatal)
    if c.finalized then return end
    c.finalized = true
    local function clean(fn, label, context)
        c.protectedDepth = 1
        local ok, err = protected(fn, context)
        c.protectedDepth = 0
        if not ok then
            message = (message and message .. "; " or "") .. label .. ": " .. tostring(err)
            origin = origin or "cleanup"
        end
    end
    for _, fn in ipairs(c.after) do clean(fn, "teardown", c.context) end
    for i = #c.cleanup, 1, -1 do clean(c.cleanup[i], "cleanup") end
    if message then PZT.emit("failure_origin", c.file, c.name, origin or "harness", 0) end
    PZT.emit(message and "failed" or "passed", c.file, c.name, message or "", PZT.now() - c.started)
    PZT.current = nil
    if fatal then
        PZT.finished = true
        PZT.emit("error", c.file, c.name, "Harness cannot safely continue: " .. tostring(message), 0)
    elseif #PZT.cases == 0 then
        PZT.finished = true
        PZT.emit("complete", "", "", "", 0)
    end
end
-- May be called by the paused heartbeat. It never advances a test body.
function PZT.watchdog()
    local c = PZT.current
    if not c or PZT.finished then return end
    if c.resuming or coroutine.status(c.thread) == "normal" then
        finalize(c, "Coroutine did not return safely from resume", "harness", true)
    elseif PZT.now() - c.started > (PZT.timeout or 60) then
        finalize(c, "Test timeout", "harness", false)
    end
end
function PZT.tick()
    if PZT.finished then return end
    PZT.watchdog()
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
    if coroutine.status(c.thread) ~= "suspended" then
        finalize(c, "Coroutine is not resumable: " .. coroutine.status(c.thread), "harness", true)
        return
    end
    -- If the VM throws out of resume, the next heartbeat detects this flag
    -- and stops the run instead of repeatedly resuming the damaged coroutine.
    c.resuming = true
    local errorsBefore = errorCount()
    local ok, err = coroutine.resume(c.thread)
    c.resuming = false
    local errorsAfter = errorCount()
    if errorsBefore and errorsAfter and errorsBefore ~= errorsAfter and c.handledErrors ~= errorsAfter then
        finalize(c, loggedError(), "harness", true)
    elseif c.failure then
        finalize(c, c.failure, c.failureOrigin, false)
    elseif not ok then
        finalize(c, tostring(err), "harness", coroutine.status(c.thread) ~= "dead")
    elseif coroutine.status(c.thread) == "dead" then
        finalize(c, nil, nil, false)
    end
end
