local clock = 0
expectedHostErrors = 1
local terminal = 0
local errors = 0
local cleaned = 0
local later = false
PZT.file = "unrecoverable.lua"
PZT.now = function() clock = clock + 0.01; return clock end
PZT.emit = function(kind, file, name, message)
    if kind == "failed" then terminal = terminal + 1 end
    if kind == "error" then errors = errors + 1 end
end
test("unprotected native exception", function(t)
    t:cleanup(function() cleaned = cleaned + 1 end)
    nativeFailure()
end)
test("must not run", function(t) later = true end)
function verifySmoke()
    assert(terminal == 1, "Expected one terminal failure")
    assert(errors == 1, "Expected one explicit infrastructure error")
    assert(cleaned == 1, "Expected cleanup exactly once")
    assert(not later, "Unsafe continuation")
end
