namespace PZTools.Core.Models.Test
{
    public class LuaTestResult
    {
        public bool Ok { get; set; }
        public string Type { get; set; }
        public string Message { get; set; }

        public int? Line { get; set; }
        public int? Column { get; set; }
        public string CodeLine { get; set; }

        public static LuaTestResult Success() => new() { Ok = true };

        public static LuaTestResult SyntaxError(string msg, int line, int col, string code)
            => Create("Syntax", msg, line, col, code);

        public static LuaTestResult RuntimeError(string msg, int line, int col, string code)
            => Create("Runtime", msg, line, col, code);

        public static LuaTestResult Fatal(string msg)
            => Create("Fatal", msg, null, null, null);

        private static LuaTestResult Create(string type, string msg, int? line, int? col, string code)
        {
            return new LuaTestResult
            {
                Ok = false,
                Type = type,
                Message = msg,
                Line = line,
                Column = col,
                CodeLine = code
            };
        }
    }


}
