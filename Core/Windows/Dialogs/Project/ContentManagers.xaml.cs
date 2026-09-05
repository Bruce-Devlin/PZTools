using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;

namespace PZTools.Core.Windows.Dialogs.Project
{
    public partial class ContentManagers : Window
    {
        private readonly ModProject _project;
        private readonly ObservableCollection<ProfessionDefinition> _professions = new();
        private readonly ObservableCollection<TraitDefinition> _traits = new();
        private readonly ObservableCollection<LocalizationEntry> _localizations = new();
        private readonly ObservableCollection<SandboxOptionDefinition> _sandboxOptions = new();
        private bool _populating;

        private ModTarget? CurrentTarget => TargetCombo.SelectedItem as ModTarget;

        public ContentManagers(ModProject project)
        {
            InitializeComponent();
            _project = project ?? throw new ArgumentNullException(nameof(project));
            ProfessionList.ItemsSource = _professions;
            TraitList.ItemsSource = _traits;
            LocalizationGrid.ItemsSource = _localizations;
            SandboxGrid.ItemsSource = _sandboxOptions;
            ((DataGridComboBoxColumn)SandboxGrid.Columns[1]).ItemsSource = new[] { "boolean", "integer", "double", "string", "text", "enum" };
            var localizationCategories = new[] { "UI", "IG_UI", "ContextMenu", "Tooltip", "Sandbox", "ItemName", "RecipeName", "Moveables", "Moodles", "SurvivalGuide" };
            CategoryCombo.ItemsSource = localizationCategories;
            CategoryCombo.SelectedItem = localizationCategories[0];
            TargetCombo.ItemsSource = project.Targets;
            TargetCombo.SelectedItem = project.Targets.FirstOrDefault(x => x.IsPrimary) ?? project.Targets.FirstOrDefault();
        }

        private void TargetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CurrentTarget is not { } target)
                return;
            var professionDefault = ManagedContentService.GetDefaultProfessionPath(_project, target);
            var traitDefault = ManagedContentService.GetDefaultTraitPath(_project, target);
            var professionFiles = ManagedContentService.FindDeclarationFiles(target, true).ToList();
            var traitFiles = ManagedContentService.FindDeclarationFiles(target, false).ToList();
            ProfessionFileCombo.ItemsSource = professionFiles.Count > 0 ? professionFiles : new[] { professionDefault };
            TraitFileCombo.ItemsSource = traitFiles.Count > 0 ? traitFiles : new[] { traitDefault };
            ProfessionFileCombo.SelectedItem = professionFiles.FirstOrDefault() ?? professionDefault;
            TraitFileCombo.SelectedItem = traitFiles.FirstOrDefault() ?? traitDefault;
            SandboxPath.Text = ManagedContentService.GetSandboxOptionsPath(target);
            UpdateLocalizationPath();
            RunFileAction("content", LoadAllForTarget);
        }

        private void LoadAllForTarget()
        {
            if (CurrentTarget is not { } target)
                return;
            LoadProfessions(SelectedPath(ProfessionFileCombo));
            LoadTraits(SelectedPath(TraitFileCombo));

            _localizations.Clear();
            var localizationPath = ManagedContentService.GetLocalizationPath(target, LanguageText.Text, CategoryCombo.Text);
            if (File.Exists(localizationPath))
                foreach (var item in ManagedContentService.ParseLocalization(File.ReadAllText(localizationPath), target.Build >= 42))
                    _localizations.Add(item);

            _sandboxOptions.Clear();
            var sandboxPath = ManagedContentService.GetSandboxOptionsPath(target);
            if (File.Exists(sandboxPath))
                foreach (var item in ManagedContentService.ParseSandboxOptions(File.ReadAllText(sandboxPath)))
                    _sandboxOptions.Add(item);

            SetStatus($"{target.BuildName} · Loaded {_professions.Count} profession(s), {_traits.Count} trait(s), {_localizations.Count} localization string(s), and {_sandboxOptions.Count} sandbox option(s).");
        }

        private void ProfessionFileCombo_DropDownClosed(object sender, EventArgs e) => RunFileAction("professions", () => LoadProfessions(SelectedPath(ProfessionFileCombo)));
        private void TraitFileCombo_DropDownClosed(object sender, EventArgs e) => RunFileAction("traits", () => LoadTraits(SelectedPath(TraitFileCombo)));

        private static string SelectedPath(System.Windows.Controls.ComboBox combo) => combo.SelectedItem as string ?? combo.Text;

        private void LoadProfessions(string value)
        {
            var path = RequiredPath(value);
            _professions.Clear();
            if (File.Exists(path))
                foreach (var item in ManagedContentService.ParseProfessions(File.ReadAllText(path)))
                    _professions.Add(item);
            ProfessionList.SelectedIndex = _professions.Count > 0 ? 0 : -1;
            ProfessionEditor.IsEnabled = ProfessionList.SelectedItem is not null;
        }

        private void LoadTraits(string value)
        {
            var path = RequiredPath(value);
            _traits.Clear();
            if (File.Exists(path))
                foreach (var item in ManagedContentService.ParseTraits(File.ReadAllText(path)))
                    _traits.Add(item);
            TraitList.SelectedIndex = _traits.Count > 0 ? 0 : -1;
            TraitEditor.IsEnabled = TraitList.SelectedItem is not null;
        }

        private void AddProfession_Click(object sender, RoutedEventArgs e)
        {
            var item = new ProfessionDefinition { Id = UniqueId("NewProfession", _professions.Select(x => x.Id)), TranslationKey = "UI_prof_NewProfession", Icon = "profession_new" };
            _professions.Add(item);
            ProfessionList.SelectedItem = item;
        }

        private void RemoveProfession_Click(object sender, RoutedEventArgs e)
        {
            if (ProfessionList.SelectedItem is ProfessionDefinition item)
                _professions.Remove(item);
        }

        private void ProfessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _populating = true;
            if (ProfessionList.SelectedItem is ProfessionDefinition item)
            {
                ProfessionEditor.IsEnabled = true;
                ProfessionId.Text = item.Id;
                ProfessionNameKey.Text = item.TranslationKey;
                ProfessionDescriptionKey.Text = item.DescriptionKey;
                ProfessionIcon.Text = item.Icon;
                ProfessionCost.Text = item.Cost.ToString();
                ProfessionTraits.Text = string.Join(Environment.NewLine, item.FreeTraits);
                ProfessionRecipes.Text = string.Join(Environment.NewLine, item.FreeRecipes);
                ProfessionBoosts.Text = FormatBoosts(item.XpBoosts);
            }
            else
                ProfessionEditor.IsEnabled = false;
            _populating = false;
        }

        private void ProfessionField_Changed(object sender, RoutedEventArgs e)
        {
            if (_populating || ProfessionList.SelectedItem is not ProfessionDefinition item)
                return;
            item.Id = ProfessionId.Text.Trim();
            item.TranslationKey = ProfessionNameKey.Text.Trim();
            item.DescriptionKey = ProfessionDescriptionKey.Text.Trim();
            item.Icon = ProfessionIcon.Text.Trim();
            if (int.TryParse(ProfessionCost.Text, out var cost))
                item.Cost = cost;
            ReplaceLines(item.FreeTraits, ProfessionTraits.Text);
            ReplaceLines(item.FreeRecipes, ProfessionRecipes.Text);
            if (TryParseBoosts(ProfessionBoosts.Text, out var boosts, out _))
                ReplaceBoosts(item.XpBoosts, boosts);
            ProfessionList.Items.Refresh();
        }

        private void LoadProfessions_Click(object sender, RoutedEventArgs e) => RunFileAction("professions", () =>
        {
            var path = RequiredPath(SelectedPath(ProfessionFileCombo));
            LoadProfessions(path);
            SetStatus(File.Exists(path) ? $"Loaded {_professions.Count} profession(s)." : "New profession file");
        });

        private void SaveProfessions_Click(object sender, RoutedEventArgs e) => RunFileAction("professions", () =>
        {
            ProfessionField_Changed(sender, e);
            if (!TryParseBoosts(ProfessionBoosts.Text, out _, out var error) && ProfessionList.SelectedItem != null)
                throw new InvalidOperationException(error);
            var path = RequiredPath(ProfessionFileCombo.Text);
            if (!ConfirmRewrite(path))
                return;
            var build42Script = IsBuild42Script(path);
            var moduleName = ManagedContentService.FindScriptModuleName(File.Exists(path) ? File.ReadAllText(path) : "", ScriptModuleFallback());
            ManagedContentService.SaveWithBackup(path, ManagedContentService.BuildProfessions(_professions, build42Script, moduleName));
            Saved(path, _professions.Count, "profession");
        });

        private void AddTrait_Click(object sender, RoutedEventArgs e)
        {
            var item = new TraitDefinition { Id = UniqueId("NewTrait", _traits.Select(x => x.Id)), TranslationKey = "UI_trait_NewTrait", DescriptionKey = "UI_trait_NewTraitDesc" };
            _traits.Add(item);
            TraitList.SelectedItem = item;
        }

        private void RemoveTrait_Click(object sender, RoutedEventArgs e)
        {
            if (TraitList.SelectedItem is TraitDefinition item)
                _traits.Remove(item);
        }

        private void TraitList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _populating = true;
            if (TraitList.SelectedItem is TraitDefinition item)
            {
                TraitEditor.IsEnabled = true;
                TraitId.Text = item.Id;
                TraitNameKey.Text = item.TranslationKey;
                TraitDescriptionKey.Text = item.DescriptionKey;
                TraitCost.Text = item.Cost.ToString();
                TraitProfessionOnly.IsChecked = item.IsProfessionTrait;
                TraitRemoveMp.IsChecked = item.RemoveInMP;
                TraitBoosts.Text = FormatBoosts(item.XpBoosts);
                TraitRecipes.Text = string.Join(Environment.NewLine, item.FreeRecipes);
            }
            else
                TraitEditor.IsEnabled = false;
            _populating = false;
        }

        private void TraitField_Changed(object sender, RoutedEventArgs e)
        {
            if (_populating || TraitList.SelectedItem is not TraitDefinition item)
                return;
            item.Id = TraitId.Text.Trim();
            item.TranslationKey = TraitNameKey.Text.Trim();
            item.DescriptionKey = TraitDescriptionKey.Text.Trim();
            if (int.TryParse(TraitCost.Text, out var cost))
                item.Cost = cost;
            item.IsProfessionTrait = TraitProfessionOnly.IsChecked == true;
            item.RemoveInMP = TraitRemoveMp.IsChecked == true;
            ReplaceLines(item.FreeRecipes, TraitRecipes.Text);
            if (TryParseBoosts(TraitBoosts.Text, out var boosts, out _))
                ReplaceBoosts(item.XpBoosts, boosts);
            TraitList.Items.Refresh();
        }

        private void LoadTraits_Click(object sender, RoutedEventArgs e) => RunFileAction("traits", () =>
        {
            var path = RequiredPath(SelectedPath(TraitFileCombo));
            LoadTraits(path);
            SetStatus(File.Exists(path) ? $"Loaded {_traits.Count} trait(s)." : "New trait file");
        });

        private void SaveTraits_Click(object sender, RoutedEventArgs e) => RunFileAction("traits", () =>
        {
            TraitField_Changed(sender, e);
            if (!TryParseBoosts(TraitBoosts.Text, out _, out var error) && TraitList.SelectedItem != null)
                throw new InvalidOperationException(error);
            var path = RequiredPath(TraitFileCombo.Text);
            if (!ConfirmRewrite(path))
                return;
            var build42Script = IsBuild42Script(path);
            var moduleName = ManagedContentService.FindScriptModuleName(File.Exists(path) ? File.ReadAllText(path) : "", ScriptModuleFallback());
            ManagedContentService.SaveWithBackup(path, ManagedContentService.BuildTraits(_traits, build42Script, moduleName));
            Saved(path, _traits.Count, "trait");
        });

        private void LocalizationLocation_Changed(object sender, RoutedEventArgs e) => UpdateLocalizationPath();

        private void UpdateLocalizationPath()
        {
            if (CurrentTarget is null || LanguageText is null || CategoryCombo is null || LocalizationPath is null)
                return;
            try
            {
                LocalizationPath.Text = ManagedContentService.GetLocalizationPath(CurrentTarget, LanguageText.Text, CategoryCombo.Text);
            }
            catch { LocalizationPath.Text = "Enter a language and category using letters, numbers, underscores, or hyphens."; }
        }

        private void LoadLocalization_Click(object sender, RoutedEventArgs e) => RunFileAction("localization", () =>
        {
            if (CurrentTarget is null)
                return;
            var path = ManagedContentService.GetLocalizationPath(CurrentTarget, LanguageText.Text, CategoryCombo.Text);
            _localizations.Clear();
            if (File.Exists(path))
                foreach (var item in ManagedContentService.ParseLocalization(File.ReadAllText(path), CurrentTarget.Build >= 42))
                    _localizations.Add(item);
            SetStatus(File.Exists(path) ? $"Loaded {_localizations.Count} localization string(s)." : "New localization file");
        });

        private void SaveLocalization_Click(object sender, RoutedEventArgs e) => RunFileAction("localization", () =>
        {
            if (CurrentTarget is null)
                return;
            LocalizationGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            LocalizationGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var path = ManagedContentService.GetLocalizationPath(CurrentTarget, LanguageText.Text, CategoryCombo.Text);
            var content = ManagedContentService.BuildLocalization(_localizations, CurrentTarget.Build >= 42, CategoryCombo.Text);
            ManagedContentService.SaveWithBackup(path, content);
            Saved(path, _localizations.Count, "localization string");
        });

        private void LoadSandbox_Click(object sender, RoutedEventArgs e) => RunFileAction("sandbox options", () =>
        {
            if (CurrentTarget is null)
                return;
            var path = ManagedContentService.GetSandboxOptionsPath(CurrentTarget);
            _sandboxOptions.Clear();
            if (File.Exists(path))
                foreach (var item in ManagedContentService.ParseSandboxOptions(File.ReadAllText(path)))
                    _sandboxOptions.Add(item);
            SetStatus(File.Exists(path) ? $"Loaded {_sandboxOptions.Count} sandbox option(s)." : "New sandbox options file");
        });

        private void SaveSandbox_Click(object sender, RoutedEventArgs e) => RunFileAction("sandbox options", () =>
        {
            if (CurrentTarget is null)
                return;
            SandboxGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            SandboxGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var path = ManagedContentService.GetSandboxOptionsPath(CurrentTarget);
            ManagedContentService.SaveWithBackup(path, ManagedContentService.BuildSandboxOptions(_sandboxOptions));
            Saved(path, _sandboxOptions.Count, "sandbox option");
        });

        private void Saved(string path, int count, string kind)
        {
            App.MainWindow?.UpdateTreeView();
            SetStatus($"Saved {count} {kind}{(count == 1 ? "" : "s")}. Backup: .pztools.bak");
        }

        private void RunFileAction(string kind, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex) { SetStatus($"Could not process {kind}: {ex.Message}", true); MessageBox.Show(ex.Message, $"Could not process {kind}", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private void SetStatus(string message, bool error = false)
        {
            StatusText.Text = message;
            StatusText.Foreground = FindResource(error ? "Brush.TextDanger" : "Brush.TextMuted") as System.Windows.Media.Brush;
        }

        private string RequiredPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("Choose a declaration file first.");
            if (CurrentTarget is null)
                throw new InvalidOperationException("Choose a build target first.");
            var targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(CurrentTarget.Path));
            var path = Path.GetFullPath(value.Trim());
            if (!path.StartsWith(targetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Managed files must remain inside the selected build target.");
            return path;
        }

        private bool ConfirmRewrite(string path)
        {
            if (!File.Exists(path) || File.ReadLines(path).FirstOrDefault()?.Equals(ManagedContentService.GeneratedHeader, StringComparison.Ordinal) == true)
                return true;
            return MessageBox.Show(
                "This is an existing hand-written declaration file. Saving will replace the file with the declarations shown here; PZTools will retain the current file as .pztools.bak.\n\nContinue?",
                "Replace declaration file", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }
        private bool IsBuild42Script(string path) => CurrentTarget?.Build >= 42 && path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
        private string ScriptModuleFallback()
        {
            var value = string.IsNullOrWhiteSpace(_project.ModInfo.Id) ? _project.Name : _project.ModInfo.Id;
            value = System.Text.RegularExpressions.Regex.Replace(value, "[^A-Za-z0-9_.-]", "");
            return string.IsNullOrWhiteSpace(value) ? "PZToolsMod" : value;
        }
        private static string UniqueId(string basis, IEnumerable<string> values)
        {
            var used = values.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var candidate = basis;
            var n = 2;
            while (used.Contains(candidate))
                candidate = basis + n++;
            return candidate;
        }
        private static string FormatBoosts(IEnumerable<PerkBoostDefinition> boosts) => string.Join(Environment.NewLine, boosts.Select(x => $"{x.Perk}={x.Level}"));
        private static void ReplaceLines(ObservableCollection<string> target, string text)
        {
            target.Clear();
            foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0))
                target.Add(line);
        }
        private static void ReplaceBoosts(ObservableCollection<PerkBoostDefinition> target, IEnumerable<PerkBoostDefinition> values)
        {
            target.Clear();
            foreach (var value in values)
                target.Add(value);
        }

        private static bool TryParseBoosts(string text, out List<PerkBoostDefinition> boosts, out string error)
        {
            boosts = new();
            error = "";
            foreach (var raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = raw.Split('=', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || !int.TryParse(parts[1], out var level))
                {
                    error = $"XP boost '{raw.Trim()}' must use Perk=Level, for example Fitness=2.";
                    return false;
                }
                boosts.Add(new PerkBoostDefinition { Perk = parts[0], Level = level });
            }
            return true;
        }
    }
}
