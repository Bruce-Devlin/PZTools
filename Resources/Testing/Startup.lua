-- Only loaded by the companion in an isolated run. Stop changing UI as soon as
-- the host releases the startup barrier so tests can deliberately open/pause it.
local startup = {}
function startup.prepare()
    local core = getCore()
    core:setOptionShowSurvivalGuide(false)
    core:setOptionPauseOnFocusloss(false)
end
function startup.dismiss()
    if not getSpecificPlayer(0) then return false end
    if SurvivalGuide and SurvivalGuide.instance and SurvivalGuide.instance:isVisible() then
        SurvivalGuide.instance:setVisible(false)
    end
    local info = getPlayerInfoPanel(0)
    if not info then return false end
    if info:isVisible() then info:setVisible(false) end
    setGameSpeed(1)
    return not info:isVisible() and
        not (SurvivalGuide and SurvivalGuide.instance and SurvivalGuide.instance:isVisible())
end
return startup
