using System.IO;
using System.Text.RegularExpressions;

namespace PZTools.Core.Functions.Help
{
    public sealed record HelpArticle(string Id, string Category, string Title, string Body)
    {
        public string Summary => Body.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
    }

    public static class AppKnowledgeBase
    {
        public static IReadOnlyList<HelpArticle> Articles { get; } = Load();

        public static IReadOnlyList<HelpArticle> Search(string? query, string? category = null)
        {
            var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return Articles.Where(article => (category == null || article.Category == category) &&
                words.All(word => $"{article.Title} {article.Category} {article.Body}"
                    .Contains(word, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(article => words.Count(word => article.Title.Contains(word, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }

        private static IReadOnlyList<HelpArticle> Load()
        {
            using var stream = typeof(AppKnowledgeBase).Assembly.GetManifestResourceStream("PZTools.Resources.Help.KnowledgeBase.md")
                ?? throw new InvalidOperationException("The bundled PZ Tools help resource is missing.");
            using var reader = new StreamReader(stream);
            var source = reader.ReadToEnd().Replace("\r\n", "\n");
            var headings = Regex.Matches(source, @"^# ([a-z0-9-]+) \| ([^\n|]+) \| ([^\n]+)$", RegexOptions.Multiline);
            var articles = headings.Select((heading, index) => new HelpArticle(
                heading.Groups[1].Value, heading.Groups[2].Value, heading.Groups[3].Value,
                source[(heading.Index + heading.Length)..(index + 1 < headings.Count ? headings[index + 1].Index : source.Length)].Trim())).ToArray();
            var ids = articles.Select(article => article.Id).ToHashSet(StringComparer.Ordinal);
            if (articles.Length == 0 || ids.Count != articles.Length || !ids.Contains("welcome"))
                throw new InvalidOperationException("Help articles need unique IDs and a welcome page.");
            foreach (var article in articles)
            {
                if (string.IsNullOrWhiteSpace(article.Body))
                    throw new InvalidOperationException($"Empty help article: {article.Id}");
                foreach (Match link in Regex.Matches(article.Body, @"\[([^\]]+)\]\(([^)]+)\)"))
                    if (!ids.Contains(link.Groups[2].Value))
                        throw new InvalidOperationException($"Broken help link in {article.Id}: {link.Groups[2].Value}");
            }
            return Array.AsReadOnly(articles);
        }
    }
}
