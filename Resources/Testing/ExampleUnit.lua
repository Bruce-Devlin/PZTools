test("Arithmetic assertion example", function(t)
    t:equal(2 + 2, 4)
    t:throws(function() error("expected") end, "expected")
end)
