using MoonSharp.Interpreter;
using PZTools.Core.Models.Test;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PZTools.Core.Functions.Tester
{
    public static class LuaTester
    {
        public static async Task<LuaTestResult> TestFile(string filePath)
        {
            try
            {
                var luaCode = await File.ReadAllTextAsync(filePath);
                return await Test(luaCode, filePath);
            }
            catch (Exception ex)
            {
                var fatal = LuaTestResult.Fatal(ex.Message);
                await Console.Log(
                    $"Lua Syntax Results for {Path.GetFileName(filePath)}: (Ok={fatal.Ok}) {fatal.Type}: {fatal.Message}");
                return fatal;
            }
        }

        public static async Task<LuaTestResult> Test(string luaCode, string filePath)
        {
            var fileName = Path.GetFileName(filePath);

            try
            {
                var script = new Script(CoreModules.Preset_SoftSandbox);
                script.LoadString(luaCode, null, fileName);

                var ok = LuaTestResult.Success();

                await Console.Log(
                    $"Lua Syntax Results for {fileName}: (Ok={ok.Ok}) Syntax: OK");

                return ok;
            }
            catch (SyntaxErrorException ex)
            {
                var results = BuildResult("Syntax", ex, luaCode, fileName);

                await Console.Log(
                    $"Lua Syntax Results for {fileName}: (Ok={results.Ok}) {results.Type}: {results.Message} " +
                    $"[Line:{results.Line}, Col:{results.Column}] = {FormatCodeLine(results.CodeLine)}");

                return results;
            }
            catch (InterpreterException ex)
            {
                var results = BuildResult("Interpreter", ex, luaCode, fileName);

                await Console.Log(
                    $"Lua Syntax Results for {fileName}: (Ok={results.Ok}) {results.Type}: {results.Message} " +
                    $"[Line:{results.Line}, Col:{results.Column}] = {FormatCodeLine(results.CodeLine)}");

                return results;
            }
            catch (Exception ex)
            {
                var fatal = LuaTestResult.Fatal(ex.Message);

                await Console.Log(
                    $"Lua Syntax Results for {fileName}: (Ok={fatal.Ok}) {fatal.Type}: {fatal.Message}");

                return fatal;
            }
        }

        private static LuaTestResult BuildResult(string type, InterpreterException ex, string source, string chunkName)
        {
            (int line, int col) = TryExtractLineCol(ex, chunkName);

            string codeLine = TryGetSourceLine(source, line);

            return new LuaTestResult
            {
                Ok = false,
                Type = type,
                Message = ex.Message ?? string.Empty,
                Line = line,
                Column = col,
                CodeLine = codeLine
            };
        }

        private static (int line, int col) TryExtractLineCol(InterpreterException ex, string chunkName)
        {
            if (TryGetStructuredLocation(ex, out int line, out int col))
                return (line, col);

            var decorated = ex.DecoratedMessage ?? string.Empty;
            var message = ex.Message ?? string.Empty;

            if (TryMatchLocationWithOptionalChunk(decorated, chunkName, out line, out col))
                return (line, col);

            if (TryMatchLocationWithOptionalChunk(message, chunkName, out line, out col))
                return (line, col);

            if (TryMatchLooseLocation(decorated, out line, out col))
                return (line, col);

            if (TryMatchLooseLocation(message, out line, out col))
                return (line, col);

            return (0, 0);
        }

        /// <summary>
        /// Attempts to extract location using internal MoonSharp fields/properties without depending on a specific version.
        /// Common paths include:
        /// - SyntaxErrorException.Token.SourceRef.FromLine / FromChar
        /// - InterpreterException.DecoratedMessage includes a SourceRef
        /// - Some versions store a SourceRef field/property directly on the exception
        /// </summary>
        private static bool TryGetStructuredLocation(object ex, out int line, out int col)
        {
            line = 0;
            col = 0;
            if (ex == null) return false;

            if (TryGetSourceRefLineColFromObject(ex, out line, out col))
                return line > 0;

            var tokenObj = GetMemberValue(ex, "Token");
            if (tokenObj != null)
            {
                var sourceRefObj = GetMemberValue(tokenObj, "SourceRef");
                if (sourceRefObj != null && TryGetSourceRefLineColFromObject(sourceRefObj, out line, out col))
                    return line > 0;

                if (TryGetLineColFromCommonMembers(tokenObj, out line, out col))
                    return line > 0;
            }

            var sr = GetMemberValue(ex, "SourceRef")
                     ?? GetMemberValue(ex, "m_SourceRef")
                     ?? GetMemberValue(ex, "_sourceRef");
            if (sr != null && TryGetSourceRefLineColFromObject(sr, out line, out col))
                return line > 0;

            if (TryGetLineColFromCommonMembers(ex, out line, out col))
                return line > 0;

            return false;
        }

        private static bool TryGetSourceRefLineColFromObject(object sourceRefObj, out int line, out int col)
        {
            line = 0;
            col = 0;
            if (sourceRefObj == null) return false;

            int? fromLine = GetIntMember(sourceRefObj, "FromLine") ?? GetIntMember(sourceRefObj, "fromLine");
            int? fromChar = GetIntMember(sourceRefObj, "FromChar") ?? GetIntMember(sourceRefObj, "fromChar");

            if (!fromLine.HasValue || !fromChar.HasValue) return false;

            if (fromLine > 0)
            {
                line = fromLine.Value;
                col = Math.Max(0, fromChar.Value);
                return true;
            }


            if (TryGetLineColFromCommonMembers(sourceRefObj, out line, out col))
                return line > 0;

            return false;
        }

        private static bool TryGetLineColFromCommonMembers(object obj, out int line, out int col)
        {
            line = 0;
            col = 0;
            if (obj == null) return false;

            int? l =
                GetIntMember(obj, "Line")
                ?? GetIntMember(obj, "line")
                ?? GetIntMember(obj, "m_Line")
                ?? GetIntMember(obj, "_line");

            int? c =
                GetIntMember(obj, "Column")
                ?? GetIntMember(obj, "Col")
                ?? GetIntMember(obj, "column")
                ?? GetIntMember(obj, "m_Column")
                ?? GetIntMember(obj, "_column");

            if (l == null) return false;

            if (l > 0)
            {
                line = l.Value;
                col = Math.Max(0, c.Value);
                return true;
            }

            return false;
        }

        private static bool TryMatchLocationWithOptionalChunk(string text, string chunkName, out int line, out int col)
        {
            line = 0;
            col = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var escapedChunk = Regex.Escape(chunkName ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(chunkName))
            {
                var m = Regex.Match(
                    text,
                    $@"{escapedChunk}:\(\s*(\d+)\s*,\s*(\d+)(?:\s*-\s*(\d+))?\s*\)\s*:"
                );

                if (m.Success)
                {
                    line = SafeInt(m.Groups[1].Value);
                    col = SafeInt(m.Groups[2].Value);
                    return line > 0;
                }

                var m2 = Regex.Match(text, $@"{escapedChunk}:(\d+):");
                if (m2.Success)
                {
                    line = SafeInt(m2.Groups[1].Value);
                    col = 0;
                    return line > 0;
                }
            }

            return TryMatchLooseLocation(text, out line, out col);
        }

        private static bool TryMatchLooseLocation(string text, out int line, out int col)
        {
            line = 0;
            col = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var m = Regex.Match(text, @":\(\s*(\d+)\s*,\s*(\d+)(?:\s*-\s*(\d+))?\s*\)\s*:");
            if (m.Success)
            {
                line = SafeInt(m.Groups[1].Value);
                col = SafeInt(m.Groups[2].Value);
                return line > 0;
            }

            var m2 = Regex.Match(text, @":(\d+):");
            if (m2.Success)
            {
                line = SafeInt(m2.Groups[1].Value);
                col = 0;
                return line > 0;
            }

            var m3 = Regex.Match(text, @"\bline\s+(\d+)\b", RegexOptions.IgnoreCase);
            if (m3.Success)
            {
                line = SafeInt(m3.Groups[1].Value);
                col = 0;
                return line > 0;
            }

            return false;
        }

        private static object GetMemberValue(object obj, string name)
        {
            try
            {
                if (obj == null || string.IsNullOrWhiteSpace(name)) return null;

                var t = obj.GetType();

                var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null)
                    return p.GetValue(obj);

                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                    return f.GetValue(obj);

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static int? GetIntMember(object obj, string name)
        {
            var v = GetMemberValue(obj, name);
            if (v == null) return null;

            if (v is int i) return i;
            if (v is long l) return (int)l;

            if (int.TryParse(v.ToString(), out var parsed))
                return parsed;

            return null;
        }

        private static string TryGetSourceLine(string source, int line)
        {
            if (string.IsNullOrEmpty(source)) return null;
            if (line <= 0) return null;

            var lines = source.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            if (line - 1 >= 0 && line - 1 < lines.Length)
                return lines[line - 1]?.TrimEnd();

            return null;
        }

        private static int SafeInt(string s) => int.TryParse(s, out var v) ? v : 0;

        private static string FormatCodeLine(string codeLine)
            => codeLine == null ? "<unknown>" : $"\"{codeLine}\"";
    }
}
