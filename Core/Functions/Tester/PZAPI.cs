using MoonSharp.Interpreter;

namespace PZTools.Core.Functions.Tester
{
    public static class PZAPI
    {
        public static void HookScript(Script script)
        {
            script.Globals["print"] = (Action<DynValue>)(v =>
            {
                _ = Console.Log(v.ToString(), title: "LUA DEBUGGER");
            });

            Table eventsTable = new Table(script);
            Table onTickTable = new Table(script);

            onTickTable["Add"] = (Action<DynValue>)(fn =>
            {
                _ = Console.Log("Events.OnTick.Add() called (ignored)");
            });

            eventsTable["OnTick"] = onTickTable;
            script.Globals["Events"] = eventsTable;

            foreach (string stub in stubMethods)
            {
                script.Globals[stub] = CreateStubClass(script, stub);
            }
        }

        public static DynValue CreateStubTable(Script script)
        {
            Table stub = new Table(script);

            Table meta = new Table(script);

            meta["__index"] = (Func<DynValue, DynValue, DynValue>)((self, key) =>
            {
                Table sub = new Table(script);
                sub["__call"] = (Func<DynValue, DynValue[], DynValue>)((t, args) =>
                {
                    Console.Log($"Stub function called: {key}");
                    return DynValue.Nil;
                });
                return DynValue.NewTable(sub);
            });

            stub.MetaTable = meta;
            return DynValue.NewTable(stub);
        }

        public static DynValue CreateStubClass(Script script, string className = null)
        {
            Table t = new Table(script);

            Table meta = new Table(script);

            meta["__index"] = (Func<DynValue, DynValue, DynValue>)((self, key) =>
            {
                Table fnTable = new Table(script);
                fnTable["__call"] = (Func<DynValue, DynValue[], DynValue>)(async (tbl, args) =>
                {
                    await Console.Log($"Stub {className ?? "Class"}.{key} called");
                    return CreateStubClass(script);
                });

                return DynValue.NewTable(fnTable);
            });

            t.MetaTable = meta;
            return DynValue.NewTable(t);
        }


        public static string[] stubMethods = new[]
        {
            "getPlayer",
            "getCell",
            "getWorld",
            "getGameTime",
            "getNumActivePlayers",
            "getPlayerByOnlineID",
            "getMaxActivePlayers",
            "getMaxPlayers",
            "getStatistics",
            "getMPStatus",
            "getSandboxOptions",
            "getGameSpeed",
            "setGameSpeed",
            "isGamePaused",
            "playServerSound",
            "getRenderer",
            "getSoundManager",
            "require",
            "getFileReader",
            "getModFileReader",
            "getFileOutput",
            "getModFileWriter",
            "getConnectedPlayers",
            "getPlayerFromUsername",
            "isCoopHost",
            "setAdmin",
            "addWarningPoint",
            "canInviteFriends",
            "inviteFriend",
            "getFriendsList",
            "getCurrentUserSteamID",
            "getCurrentUserProfileName",
            "getTimestamp",
            "getTimestampMs",
            "getGametimeTimestamp",
            "isoToScreenX",
            "isoToScreenY",
            "screenToIsoX",
            "screenToIsoY",

            // ===== Mouse / input =====
            "getMouseX",
            "getMouseY",
            "getMouseXScaled",
            "getMouseYScaled",
            "isMouseButtonDown",
            "setMouseXY",

            // ===== Player utilities =====
            "setPlayerMovementActive",
            "setActivePlayer",

            // ===== Event hooks =====
            "Events.OnTick",
            "Events.OnCreatePlayer",
            "Events.OnPlayerDeath",
            "Events.OnObjectAdded",
            "Events.OnObjectRemoved",
            "Events.OnKeyPressed",
            "Events.OnKeyReleased",
            "Events.OnWorldLoad",
            "Events.OnWorldSave",
            "Events.OnGameStart",
            "Events.OnGameEnd",

            // ===== UI classes (ISUIElement + derived) =====
            "ISUIElement.new",
            "ISUIElement.addChild",
            "ISUIElement.removeChild",
            "ISUIElement.getX",
            "ISUIElement.getY",
            "ISUIElement.getWidth",
            "ISUIElement.getHeight",
            "ISUIElement.setX",
            "ISUIElement.setY",
            "ISUIElement.setWidth",
            "ISUIElement.setHeight",
            "ISUIElement.setVisible",
            "ISUIElement.isVisible",
            "ISUIElement.update",
            "ISUIElement.render",
            "ISUIElement.onMouseDown",
            "ISUIElement.onMouseUp",
            "ISUIElement.onMouseMove",
            "ISUIElement.onMouseWheel",
            "ISUIElement.onKeyPress",
            "ISUIElement.onKeyRelease",
            "ISUIElement.bringToTop",
            "ISUIElement.getParent",
            "ISUIElement.setParent",
            "ISUIElement.getChildren",
            "ISUIElement.hasFocus",
            "ISUIElement.setFocus",
            "ISUIElement.removeFromUIManager",

            "ISButton.new",
            "ISButton.setTitle",
            "ISButton.setEnable",
            "ISButton.setOnClick",
            "ISButton.onMouseDown",
            "ISButton.onMouseUp",

            "ISPanel.new",
            "ISPanel.addChild",
            "ISPanel.removeChild",
            "ISPanel.setVisible",
            "ISPanel.update",
            "ISPanel.render",

            "ISLabel.new",
            "ISLabel.setText",
            "ISLabel.setColor",
            "ISLabel.setVisible",

            "ISScrollingList.new",
            "ISScrollingList.addItem",
            "ISScrollingList.removeItem",
            "ISScrollingList.clear",
            "ISScrollingList.setVisible",
            "ISScrollingList.render",

            "ISInventoryPane.new",
            "ISInventoryPane.addItem",
            "ISInventoryPane.removeItem",
            "ISInventoryPane.clear",

            // ===== Player, Square, Object methods =====
            "player:getX",
            "player:getY",
            "player:getZ",
            "player:getSquare",
            "player:getInventory",
            "player:getModData",
            "player:getStats",
            "square:getRoom",
            "square:getFloor",
            "square:getObjects",
            "square:getWorldObjects",
            "object:getSprite",
            "object:getName",
            "object:getSquare",
            "object:getContainer",
            "object:getModData",

            // ===== Utility modules =====
            "luautils.createSandboxObject",
            "luautils.getDebugString",
            "luautils.dumpTable",
            "SandboxVars.getOption",
            "SandboxVars.getValue",
        };

    }
}

