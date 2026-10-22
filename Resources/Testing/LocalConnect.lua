-- Each isolated client has its own local test account. Use the game's connection
-- screen so version errors, spawn selection and character creation stay visible.
local joining = false
Events.OnMainMenuEnter.Add(function()
    if joining then return end
    local reader = getFileReader("pztools-local-login.txt", false)
    if not reader then return end
    local name = reader:readLine()
    local port = reader:readLine()
    local user = reader:readLine()
    local password = reader:readLine()
    reader:close()
    if not password or not tonumber(port) then return end
    joining = true
    local ok, err = pcall(function()
        local screen = assert(MainScreen.instance, "Main menu is not available")
        assert(screen.connectToServer, "Connection screen is not available")
        screen.connectToServer:connect(assert(screen.bottomPanel, "Main menu buttons are not available"), name, user, password, "127.0.0.1", "127.0.0.1", port, "", false, true, 1)
    end)
    if not ok then print("PZTools: automatic connection failed: " .. tostring(err) .. ". Join 127.0.0.1:" .. port .. " as " .. user) end
end)
