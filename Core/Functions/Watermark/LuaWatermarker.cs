using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PZTools.Core.Functions.Watermark
{
    public static class LuaWatermarker
    {
        /// <summary>
        /// Adds a watermark at the top of a Lua file.
        /// </summary>
        /// <param name="filePath">Full path to the Lua file.</param>
        /// <param name="watermark">
        /// Watermark text to prepend (typically two or more lines beginning with "--").
        /// </param>
        public static async Task<bool> WatermarkFile(string filePath, string watermark)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is required.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("Lua file not found.", filePath);

            watermark = (watermark ?? string.Empty).TrimEnd();
            if (watermark.Length == 0)
                throw new ArgumentException("Watermark cannot be empty.", nameof(watermark));

            string original = await File.ReadAllTextAsync(filePath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                                        .ConfigureAwait(false);

            string nl = DetectNewLine(original);

            string normalizedWatermark = NormalizeNewLines(watermark, nl);

            string withoutOld = RemoveExistingWatermarkHeader(original, nl);

            string rest = TrimLeadingBlankLines(withoutOld, nl);

            string combined = normalizedWatermark + nl + nl + rest;

            if (string.IsNullOrEmpty(rest))
                combined = normalizedWatermark + nl;

            if (combined == original)
                return false;

            string tempPath = Path.Combine(Path.GetDirectoryName(filePath)!, Path.GetRandomFileName());

            try
            {
                await File.WriteAllTextAsync(tempPath, combined, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                          .ConfigureAwait(false);

                File.Replace(tempPath, filePath, destinationBackupFileName: null);
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Copy(tempPath, filePath, overwrite: true);
                        File.Delete(tempPath);
                    }
                    catch
                    {
                        throw;
                    }
                }

                throw;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch {}
            }

            return true;
        }

        private static string DetectNewLine(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Environment.NewLine;

            int crlf = text.IndexOf("\r\n", StringComparison.Ordinal);
            if (crlf >= 0) return "\r\n";

            int lf = text.IndexOf('\n');
            if (lf >= 0) return "\n";

            return Environment.NewLine;
        }

        private static string NormalizeNewLines(string text, string nl)
        {
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            return nl == "\n" ? text : text.Replace("\n", nl);
        }

        private static string RemoveExistingWatermarkHeader(string original, string nl)
        {
            if (string.IsNullOrEmpty(original))
                return original;

            int i = 0;

            if (original.Length > 0 && original[0] == '\uFEFF')
                i = 1;
            
            int commentLineCount = 0;
            int start = i;

            int pos = start;
            int headerEndPos = start;
            bool sawNonBlankAfterComments = false;

            while (pos < original.Length)
            {
                int lineEnd = IndexOfLineEnd(original, pos);
                string line = GetLine(original, pos, lineEnd);

                bool isBlank = string.IsNullOrWhiteSpace(line);
                bool isComment = IsLuaSingleLineComment(line);

                if (isComment)
                {
                    commentLineCount++;
                    headerEndPos = NextLineStart(original, lineEnd);
                    pos = headerEndPos;
                    continue;
                }

                if (isBlank)
                {
                    if (commentLineCount > 0)
                    {
                        headerEndPos = NextLineStart(original, lineEnd);
                        pos = headerEndPos;
                        continue;
                    }

                    break;
                }

                sawNonBlankAfterComments = true;
                break;
            }

            if (commentLineCount >= 2)
            {
                return original.Substring(0, start) + original.Substring(headerEndPos);
            }

            return original;
        }

        private static bool IsLuaSingleLineComment(string line)
        {
            if (line == null) return false;
            line = line.TrimStart();
            return line.StartsWith("--", StringComparison.Ordinal);
        }

        private static string TrimLeadingBlankLines(string text, string nl)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            int i = 0;

            if (text.Length > 0 && text[0] == '\uFEFF')
                i = 1;

            while (i < text.Length)
            {
                int lineEnd = IndexOfLineEnd(text, i);
                string line = GetLine(text, i, lineEnd);

                if (!string.IsNullOrWhiteSpace(line))
                    break;

                i = NextLineStart(text, lineEnd);
            }

            return text.Substring(0, (text.Length > 0 && text[0] == '\uFEFF') ? 1 : 0) + text.Substring(i);
        }

        private static int IndexOfLineEnd(string text, int startIndex)
        {
            if (startIndex >= text.Length) return text.Length;

            int n = text.IndexOf('\n', startIndex);
            if (n < 0) return text.Length;
            return n;
        }

        private static int NextLineStart(string text, int lineEndIndex)
        {
            if (lineEndIndex >= text.Length) return text.Length;
            return lineEndIndex + 1;
        }

        private static string GetLine(string text, int startIndex, int lineEndIndex)
        {
            int length = lineEndIndex - startIndex;
            if (length <= 0) return string.Empty;

            if (text[startIndex + length - 1] == '\r')
                length--;

            return length <= 0 ? string.Empty : text.Substring(startIndex, length);
        }
    }
}