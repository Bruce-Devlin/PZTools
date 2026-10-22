local clock = 0
expectedHostErrors = 0
local results = {}
local errors = {}
local cleaned = 0
PZT.file = "containment.lua"
PZT.now = function() clock = clock + 0.01; return clock end
PZT.emit = function(kind, file, name, message)
    if kind == "failed" or kind == "passed" then
        assert(not results[name], "Duplicate terminal result: " .. name)
        results[name] = {kind, message}
    elseif kind == "error" then table.insert(errors, message) end
end
test("assertion", function(t)
    t:cleanup(function() cleaned = cleaned + 1 end)
    t:equal(1, 2, "original assertion")
    error("continued after failure")
end)
test("after assertion", function(t) t:equal(cleaned, 1) end)
test("wait", function(t) t:waitUntil(function() return false end, 0) end)
test("after wait", function(t) t:truthy(true) end)
test("native", function(t) t:phase("native call", nativeFailure) end)
test("after native", function(t) t:truthy(true) end)
test("cleanup", function(t)
    t:cleanup(function() cleaned = cleaned + 1; error("cleanup exception") end)
    t:fail("original before cleanup")
end)
test("after cleanup", function(t) t:equal(cleaned, 2) end)
function verifySmoke()
    assert(#errors == 0, "Unexpected harness error")
    for _, name in ipairs({"assertion", "wait", "native", "cleanup"}) do assert(results[name][1] == "failed", name) end
    for _, name in ipairs({"after assertion", "after wait", "after native", "after cleanup"}) do assert(results[name][1] == "passed", name) end
    assert(string.find(results.native[2], "native smoke exception", 1, true))
    assert(string.find(results.cleanup[2], "original before cleanup", 1, true))
    assert(string.find(results.cleanup[2], "cleanup exception", 1, true))
    assert(cleaned == 2)
end
