using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PZTools.Core.Models;

namespace PZTools.Core.Functions.Projects
{
    internal static class PzContentValidator
    {
        public static IReadOnlyList<ProjectDiagnostic> ValidateZedScript(string path, string target, double build)
        {
            var results = new List<ProjectDiagnostic>();
            string source;
            try
            {
                source = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                results.Add(Problem(DiagnosticSeverity.Error, "PZS001", target, path,
                    $"Script could not be read: {ex.Message}", "Check file permissions and encoding."));
                return results;
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                results.Add(Problem(DiagnosticSeverity.Warning, "PZS002", target, path,
                    "ZedScript file is empty.", "Add a module and at least one definition, or remove the unused file."));
                return results;
            }

            if (!Regex.IsMatch(source, @"\bmodule\s+[A-Za-z_][\w.]*\s*\{", RegexOptions.IgnoreCase))
                results.Add(Problem(DiagnosticSeverity.Warning, "PZS003", target, path,
                    "No module declaration was found.", "Wrap item and recipe definitions in a named module block."));

            if (build >= 42 && Regex.IsMatch(source, @"\bitem\s+[^\r\n{]+\{[^}]*\bType\s*=", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                results.Add(Problem(DiagnosticSeverity.Warning, "PZS042", target, path,
                    "Build 42 item uses the deprecated Type parameter.",
                    "Replace Type with ItemType, for example ItemType = base:normal."));

            if (build >= 42 && Regex.IsMatch(source, @"(?m)^\s*recipe\s+", RegexOptions.IgnoreCase))
                results.Add(Problem(DiagnosticSeverity.Warning, "PZS043", target, path,
                    "Legacy recipe syntax found in a Build 42 target.",
                    "Use a craftRecipe block with inputs and outputs for Build 42."));

            var brace = FindBraceProblem(source);
            if (brace != null)
                results.Add(Problem(DiagnosticSeverity.Error, "PZS004", target, path,
                    brace.Value.Message, "Balance the script block braces before deploying.", brace.Value.Line));
            return results;
        }

        public static IReadOnlyList<ProjectDiagnostic> ValidateTranslationJson(string path, string target)
        {
            var results = new List<ProjectDiagnostic>();
            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var text = new StreamReader(stream);
                using var reader = new JsonTextReader(text);
                var token = JToken.Load(reader, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    LineInfoHandling = LineInfoHandling.Load
                });
                if (token is not JObject)
                    results.Add(Problem(DiagnosticSeverity.Error, "PZT201", target, path,
                        "Build 42 translation files must contain a JSON object.", "Use a JSON object of translation-key to localized-text pairs."));
                else if (!token.HasValues)
                    results.Add(Problem(DiagnosticSeverity.Warning, "PZT202", target, path,
                        "Translation file has no entries.", "Add localization keys or remove the empty file."));
            }
            catch (JsonReaderException ex)
            {
                results.Add(Problem(DiagnosticSeverity.Error, "PZT200", target, path,
                    $"Translation JSON is invalid: {ex.Message}", "Correct the JSON syntax before launching the game.", ex.LineNumber));
            }
            catch (Exception ex)
            {
                results.Add(Problem(DiagnosticSeverity.Error, "PZT203", target, path,
                    $"Translation file could not be read: {ex.Message}", "Check file permissions and encoding."));
            }
            return results;
        }

        private static (int Line, string Message)? FindBraceProblem(string source)
        {
            var stack = new Stack<int>();
            var line = 1;
            var inString = false;
            var quote = '\0';
            var inLineComment = false;
            var inBlockComment = false;

            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                var next = i + 1 < source.Length ? source[i + 1] : '\0';
                if (c == '\n')
                {
                    line++;
                    inLineComment = false;
                    continue;
                }
                if (inLineComment)
                    continue;
                if (inBlockComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                    continue;
                }
                if (inString)
                {
                    if (c == '\\')
                    {
                        i++;
                        continue;
                    }
                    if (c == quote)
                        inString = false;
                    continue;
                }
                if (c == '/' && next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }
                if (c == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
                if (c == '"' || (c == '\'' && IsSingleQuoteStart(source, i)))
                {
                    inString = true;
                    quote = c;
                    continue;
                }
                if (c == '{')
                    stack.Push(line);
                else if (c == '}')
                {
                    if (stack.Count == 0)
                        return (line, "Closing brace has no matching opening brace.");
                    stack.Pop();
                }
            }

            return stack.Count > 0 ? (stack.Peek(), "Opening brace has no matching closing brace.") : null;
        }

        private static bool IsSingleQuoteStart(string source, int index)
        {
            if (index == 0)
                return true;
            var previous = source[index - 1];
            return !char.IsLetterOrDigit(previous) && previous != '_';
        }

        private static ProjectDiagnostic Problem(DiagnosticSeverity severity, string code, string target,
            string path, string message, string recommendation, int? line = null)
            => new()
            {
                Severity = severity,
                Code = code,
                Target = target,
                FilePath = path,
                Line = line,
                Message = message,
                Recommendation = recommendation
            };
    }
}
