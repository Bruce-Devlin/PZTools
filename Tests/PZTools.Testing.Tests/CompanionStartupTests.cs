using MoonSharp.Interpreter;
using PZTools.Core.Functions.Tester;

namespace PZTools.Testing.Tests;

public sealed class CompanionStartupTests
{
    internal static void Install(Script script)
    {
        script.DoString("""
            guideVisible = true; characterVisible = true; showGuide = true; pauseOnFocusLoss = true
            function getCore() return {
                setOptionShowSurvivalGuide = function(_, value) showGuide = value end,
                setOptionPauseOnFocusloss = function(_, value) pauseOnFocusLoss = value end
            } end
            SurvivalGuide = { instance = {
                isVisible = function() return guideVisible end,
                setVisible = function(_, value) guideVisible = value end
            } }
            function getPlayerInfoPanel() if missingUI then return nil end; return {
                isVisible = function() return characterVisible end,
                setVisible = function(_, value) characterVisible = value end
            } end
            function setGameSpeed(value) speed = value end
            """);
        script.Globals["startupModule"] = script.DoString(ModTestService.Resource("Startup.lua"));
        script.DoString("""
            local oldRequire = require
            require = function(name)
                if name == "PZToolsTesting/Startup" then return startupModule end
                return oldRequire(name)
            end
            """);
    }

    [Fact]
    public void StartupWaitsForPlayerAndUIThenClearsPanelsAndResumes()
    {
        var script = new Script();
        script.DoString("function getSpecificPlayer() return player end");
        Install(script);
        script.DoString("startupModule.prepare()");
        Assert.False(script.Globals.Get("showGuide").Boolean);
        Assert.False(script.Globals.Get("pauseOnFocusLoss").Boolean);
        Assert.False(script.DoString("return startupModule.dismiss()").Boolean);
        script.DoString("player = {}; missingUI = true");
        Assert.False(script.DoString("return startupModule.dismiss()").Boolean);
        script.DoString("missingUI = false; speed = 0");
        Assert.True(script.DoString("return startupModule.dismiss()").Boolean);
        Assert.False(script.Globals.Get("guideVisible").Boolean);
        Assert.False(script.Globals.Get("characterVisible").Boolean);
        Assert.Equal(1, script.Globals.Get("speed").Number);
    }
}
