using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;
using PZTools.Core.Models.Test;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PZTools.Core.Functions.Tester
{
    public static class LuaTester
    {
        private static List<Tester> testers = new List<Tester>();
        public static async Task<LuaTestResult> Test(string luaCode)
        {
            Tester tester = new Tester();
            testers.Add(tester);
            return await tester.Test(luaCode);
        }

        private class Tester
        {
            public async Task<LuaTestResult> Test(string luaCode)
            {
                LuaTestResult results;
                try
                {
                    var script = new Script(CoreModules.Preset_SoftSandbox);
                    PZAPI.HookScript(script);
                    script.DoString(luaCode);

                    results = LuaTestResult.Success();
                }
                catch (SyntaxErrorException ex)
                {
                    results = BuildResult("Syntax", ex, luaCode);
                }
                catch (ScriptRuntimeException ex)
                {
                    results = BuildResult("Runtime", ex, luaCode);
                }
                catch (Exception ex)
                {
                    results = LuaTestResult.Fatal(ex.Message);
                }

                await Console.Log($"Lua Test Results for {Path.GetFileName(App.MainWindow.OpenedFilePath)}: (Ok={results.Ok}) {results.Type}: {results.Message} [Line:{results.Line}] = {results.CodeLine}");
                return results;
            }

            private LuaTestResult BuildResult(string type, InterpreterException ex, string source)
            {
                int line = 0;
                int col = 0;
                string codeLine = null;

                // ex.Message looks like: "chunk_0:(6,0-59): attempt to index a nil value"
                var match = System.Text.RegularExpressions.Regex.Match(
                    ex.DecoratedMessage, @"chunk_\d+:\((\d+),(\d+)");
                if (match.Success)
                {
                    line = int.Parse(match.Groups[1].Value);
                    col = int.Parse(match.Groups[2].Value);
                }

                if (line > 0)
                {
                    var lines = source.Split('\n');
                    if (line - 1 < lines.Length)
                        codeLine = lines[line - 1];
                }

                return new LuaTestResult
                {
                    Ok = false,
                    Type = type,
                    Message = ex.Message,
                    Line = line,
                    Column = col,
                    CodeLine = codeLine
                };
            }

        }

        
    }
}
