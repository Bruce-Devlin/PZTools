-- Install as .pztests/fixtures.game.lua. Setup failures prevent exercise.
local player
beforeEach(function(t)
    player = t:livingCharacter(0, 10)
    t:loadedSquare(math.floor(player:getX()), math.floor(player:getY()), math.floor(player:getZ()), 10)
end)
test("production OnTick event advances", function(t)
    local ticks = 0
    local function onTick() ticks = ticks + 1 end
    Events.OnTick.Add(onTick)
    t:cleanup(function() Events.OnTick.Remove(onTick) end)
    t:waitUntil(function() return ticks >= 3 end, 10)
    t:truthy(not player:isDead(), "Character survived the observed ticks")
end)
