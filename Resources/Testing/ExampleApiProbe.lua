-- Native API observation, not just Lua compilation. Extend with your exact target API.
test("observed player square API", function(t)
    local player = t:setup(function() return t:livingCharacter(0, 10) end)
    local observed = t:observeApi("zombie.Lua.LuaManager.GlobalObject.getSpecificPlayer", function() return getSpecificPlayer(0) end)
    t:equal(observed, player)
    local square = t:phase("player:getCurrentSquare", function() return player:getCurrentSquare() end)
    t:truthy(square, "getCurrentSquare returned a loaded square")
    t:condition("API observation", "IsoPlayer:getCurrentSquare on a living local player")
end)
