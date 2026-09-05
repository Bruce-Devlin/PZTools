using System.Collections.ObjectModel;

namespace PZTools.Core.Models
{
    public sealed class ProfessionDefinition
    {
        public string Id { get; set; } = "";
        public string TranslationKey { get; set; } = "UI_prof_";
        public string DescriptionKey { get; set; } = "";
        public string Icon { get; set; } = "profession_";
        public int Cost { get; set; }
        public ObservableCollection<string> FreeTraits { get; } = new();
        public ObservableCollection<string> FreeRecipes { get; } = new();
        public ObservableCollection<PerkBoostDefinition> XpBoosts { get; } = new();
        public Dictionary<string, string> ExtraProperties { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string DisplayName => string.IsNullOrWhiteSpace(Id) ? "New profession" : Id;
    }

    public sealed class TraitDefinition
    {
        public string Id { get; set; } = "";
        public string TranslationKey { get; set; } = "UI_trait_";
        public string DescriptionKey { get; set; } = "UI_trait_Desc";
        public int Cost { get; set; }
        public bool IsProfessionTrait { get; set; }
        public bool RemoveInMP { get; set; }
        public ObservableCollection<string> FreeRecipes { get; } = new();
        public ObservableCollection<PerkBoostDefinition> XpBoosts { get; } = new();
        public Dictionary<string, string> ExtraProperties { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string DisplayName => string.IsNullOrWhiteSpace(Id) ? "New trait" : Id;
    }

    public sealed class PerkBoostDefinition
    {
        public string Perk { get; set; } = "Fitness";
        public int Level { get; set; } = 1;
        public override string ToString() => $"{Perk} +{Level}";
    }

    public sealed class LocalizationEntry
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public sealed class SandboxOptionDefinition
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "boolean";
        public string Default { get; set; } = "false";
        public string Min { get; set; } = "";
        public string Max { get; set; } = "";
        public string Step { get; set; } = "";
        public string NumValues { get; set; } = "";
        public string Page { get; set; } = "";
        public string Translation { get; set; } = "";
        public string Tooltip { get; set; } = "";
        public string ValueTranslation { get; set; } = "";
        public Dictionary<string, string> ExtraProperties { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
