using System.IO;
using Newtonsoft.Json.Linq;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Tester;
using PZTools.Core.Models;

namespace PZTools.Testing.Tests;

public sealed class LiveMultiplayerAcceptance
{
    [LiveGameFact("PZTOOLS_LIVE_GAME_ROOT", "PZTOOLS_LIVE_SOURCE_SESSION")]
    public async Task TwoClientsUseServerConfirmedFixtureBeforeGameplayAssertions()
    {
        var game = Environment.GetEnvironmentVariable("PZTOOLS_LIVE_GAME_ROOT")!;
        var fixture = Environment.GetEnvironmentVariable("PZTOOLS_LIVE_SOURCE_SESSION")!;
        var receipt = JObject.Parse(File.ReadAllText(Path.Combine(fixture, "test-fixture.json")));
        var root = Path.Combine(Path.GetTempPath(), "PZToolsMultiplayerAcceptance", Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "42");
        Directory.CreateDirectory(Path.Combine(target, "media", "lua", "shared")); Directory.CreateDirectory(Path.Combine(root, "common"));
        File.WriteAllText(Path.Combine(target, "media", "lua", "shared", "Acceptance.lua"), "-- Native fixture acceptance\n");
        File.WriteAllText(Path.Combine(target, "mod.info"), "name=Multiplayer Fixture Acceptance\nid=PZToolsFixtureAcceptance\n");
        var project = new ModProject { Name = "MP fixture acceptance", RootPath = root, ModInfo = new() { Id = "PZToolsFixtureAcceptance" },
            Targets = new() { new() { Build = 42, Path = target, IsPrimary = true } } };
        var profile = new PlaytestProfile { Name = "Server-confirmed fixture acceptance", Mode = PlaytestMode.DedicatedServer,
            Build = receipt.Value<double>("Build"), ClientCount = receipt.Value<int>("Clients"), ServerName = receipt.Value<string>("Server")!,
            ServerPort = receipt.Value<int>("Port"), NoSteam = receipt.Value<bool>("NoSteam"), LocalServerOnly = true,
            SaveMode = PlaytestSaveMode.FreshSessionClone, SourceSessionPath = fixture, LaunchArguments = "-nosteam", ServerStartupTimeoutSeconds = 180 };
        Directory.CreateDirectory(Path.Combine(root, ".pztests", "support"));
        File.WriteAllText(Path.Combine(root, ".pztests", "support", "who.lua"), """
            PZTest.register("acceptance:who", function()
                local p = getSpecificPlayer(0)
                return { id=p:getOnlineID(), x=p:getX(), y=p:getY(), z=p:getZ() }
            end)
            """);
        File.WriteAllText(Path.Combine(root, ".pztests", "fixture.game.lua"), """
            test("server confirms both clients are co-located", function(t)
                local me, other
                t:setup(function()
                    me = t:livingCharacter(0, 15)
                    other = t:remote("client-2", "acceptance:who", {}, 15)
                    t:coLocated({me:getOnlineID(),other.id}, 3, 15)
                    t:loadedSquare(math.floor(me:getX()),math.floor(me:getY()),math.floor(me:getZ()),10)
                end)
                t:truthy(other.id ~= me:getOnlineID(), "Two distinct native clients")
            end)
            """);
        var report = await GameTestService.RunAsync(project, profile, game, timeoutSeconds: 240,
            progress: line => File.AppendAllText(Path.Combine(root, "progress.log"), line + Environment.NewLine));
        Assert.True(report.Passed, string.Join("; ", report.Errors.Concat(report.Tests.Select(t => t.Message))) + " Artifacts: " + report.ArtifactDirectory);
        Assert.Equal(3, report.Endpoints.Count);
        File.WriteAllText(Path.Combine(root, "accepted.txt"), "Native two-client server-confirmed co-location fixture passed. Report: " + report.RunId);
    }
}
