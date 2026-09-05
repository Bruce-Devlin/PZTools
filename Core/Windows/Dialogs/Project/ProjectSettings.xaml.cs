using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PZTools.Core.Functions;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Steam;
using PZTools.Core.Models;

namespace PZTools.Core.Windows.Dialogs.Project
{
    public partial class ProjectSettings : Window
    {
        public string ModFolderPath { get; set; } = "";

        private ModInfo _modInfo = new();
        private WorkshopSettings _workshopSettings = new();
        private string? _selectedPosterSourcePath;
        private string? _selectedIconSourcePath;

        private int _posterIndex = 0;
        private readonly ObservableCollection<DependencyEditorItem> _dependencies = new();
        private List<DiscoveredModDependency> _installedDependencies = new();

        public ProjectSettings(string modFolderPath)
        {
            InitializeComponent();
            this.FreeDragThisWindow();

            var project = ProjectEngine.CurrentProject;
            ModFolderPath = project?.Targets.FirstOrDefault(x => x.IsPrimary)?.Path ?? modFolderPath;
            LoadModInfo();
            Loaded += async (_, _) => await RefreshInstalledDependenciesAsync();
        }

        private void LoadModInfo()
        {
            _modInfo = ModInfoParser.Load(ModFolderPath);

            txtModName.Text = _modInfo.Name ?? "";
            txtModID.Text = _modInfo.Id ?? "";
            txtAuthor.Text = _modInfo.Author ?? "";
            txtModVersion.Text = _modInfo.ModVersion ?? "";
            txtUrl.Text = _modInfo.Url ?? "";
            txtVersionMin.Text = _modInfo.VersionMin ?? "";
            txtVersionMax.Text = _modInfo.VersionMax ?? "";
            txtDescription.Text = _modInfo.Description ?? "";

            SetCategorySelection(_modInfo.Category);

            txtPosters.Text = string.Join(Environment.NewLine, _modInfo.Posters);
            txtPacks.Text = string.Join(Environment.NewLine, _modInfo.Packs);
            txtTiledefs.Text = string.Join(Environment.NewLine, _modInfo.Tiledefs);
            txtIncompatible.Text = string.Join(Environment.NewLine, _modInfo.Incompatibles);
            LoadDependencyRows();

            txtIcon.Text = _modInfo.Icon ?? "";

            var project = ProjectEngine.CurrentProject ?? new ModProject { RootPath = Path.GetFullPath(Path.Combine(ModFolderPath, "..")), Name = Path.GetFileName(ModFolderPath), ModInfo = _modInfo };
            _workshopSettings = WorkshopSettingsStore.Load(project);
            txtWorkshopTitle.Text = _workshopSettings.Title;
            txtWorkshopDescription.Text = _workshopSettings.Description;
            txtWorkshopTags.Text = string.Join(Environment.NewLine, _workshopSettings.Tags);
            cmbWorkshopVisibility.ItemsSource = Enum.GetValues<WorkshopVisibility>();
            cmbWorkshopVisibility.SelectedItem = _workshopSettings.Visibility;
            txtWorkshopPublishedId.Text = _workshopSettings.PublishedFileId;
            txtWorkshopChangeNote.Text = _workshopSettings.DefaultChangeNote;

            _posterIndex = 0;
            RefreshPosterPreview();
            RefreshIconPreview();

            var projectName = ProjectEngine.CurrentProject?.Name ?? Path.GetFileName(ModFolderPath);
            var luaWatermark = Config.GetVariable(VariableType.user, $"{projectName}-watermark");
            if (!string.IsNullOrEmpty(luaWatermark))
                txtWatermark.Text = luaWatermark;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateSettings())
                return;

            _modInfo.Name = txtModName.Text.Trim();
            _modInfo.Id = txtModID.Text.Trim();
            _modInfo.Author = NullIfEmpty(txtAuthor.Text);
            _modInfo.ModVersion = NullIfEmpty(txtModVersion.Text);
            _modInfo.Url = NullIfEmpty(txtUrl.Text);
            _modInfo.Description = NullIfEmpty(txtDescription.Text);
            _modInfo.VersionMin = NullIfEmpty(txtVersionMin.Text);
            _modInfo.VersionMax = NullIfEmpty(txtVersionMax.Text);

            _modInfo.Category = GetCategorySelection();

            _modInfo.Posters.Clear();
            _modInfo.Posters.AddRange(ParseLines(txtPosters.Text));

            _modInfo.Packs.Clear();
            _modInfo.Packs.AddRange(ParseLines(txtPacks.Text));

            _modInfo.Tiledefs.Clear();
            _modInfo.Tiledefs.AddRange(ParseLines(txtTiledefs.Text));

            _modInfo.Incompatibles.Clear();
            _modInfo.Incompatibles.AddRange(ParseLines(txtIncompatible.Text));
            SaveDependencyRows();

            _workshopSettings.Title = txtWorkshopTitle.Text.Trim();
            _workshopSettings.Description = txtWorkshopDescription.Text.Trim();
            _workshopSettings.Tags = ParseLines(txtWorkshopTags.Text).ToList();
            _workshopSettings.Visibility = cmbWorkshopVisibility.SelectedItem is WorkshopVisibility visibility ? visibility : WorkshopVisibility.Public;
            _workshopSettings.PublishedFileId = txtWorkshopPublishedId.Text.Trim();
            _workshopSettings.DefaultChangeNote = txtWorkshopChangeNote.Text.Trim();

            if (!string.IsNullOrWhiteSpace(_selectedPosterSourcePath))
            {
                var fileName = ModInfoParser.EnsureLocalAsset(ModFolderPath, _selectedPosterSourcePath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    _modInfo.Posters.RemoveAll(p => p.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                    _modInfo.Posters.Insert(0, fileName);
                }
            }

            if (!string.IsNullOrWhiteSpace(_selectedIconSourcePath))
            {
                var fileName = ModInfoParser.EnsureLocalAsset(ModFolderPath, _selectedIconSourcePath);
                if (!string.IsNullOrWhiteSpace(fileName))
                    _modInfo.Icon = fileName;
            }
            else
            {
                _modInfo.Icon = NullIfEmpty(txtIcon.Text);
            }

            ModProject? currentProject;
            try
            {
                ModInfoParser.Save(ModFolderPath, _modInfo);
                currentProject = ProjectEngine.CurrentProject;
                if (currentProject != null)
                {
                    currentProject.ModInfo = _modInfo;
                    currentProject.UpdateModInfo(_modInfo);
                    WorkshopSettingsStore.Save(currentProject, _workshopSettings);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Project settings could not be saved: {ex.Message}", "Project Settings",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!string.IsNullOrEmpty(txtWatermark.Text))
                Config.StoreVariable(VariableType.user, $"{currentProject?.Name ?? Path.GetFileName(ModFolderPath)}-watermark", txtWatermark.Text);


            DialogResult = true;
            Close();
        }

        private bool ValidateSettings()
        {
            if (string.IsNullOrWhiteSpace(txtModName.Text))
                return ValidationError("Enter a player-facing mod name.");

            var id = txtModID.Text.Trim();
            if (string.IsNullOrWhiteSpace(id))
                return ValidationError("Enter a stable mod ID. This ID is used by saves, servers, and dependencies.");
            if (!id.Equals(ModInfoUtil.NormalizeModId(id), StringComparison.Ordinal))
                return ValidationError("The mod ID may contain only letters, numbers, underscores, and hyphens.");

            if (!string.IsNullOrWhiteSpace(txtUrl.Text) &&
                (!Uri.TryCreate(txtUrl.Text.Trim(), UriKind.Absolute, out var url) ||
                 (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)))
                return ValidationError("The project URL must be a complete http:// or https:// address.");

            if (!TryVersion(txtVersionMin.Text, out var min))
                return ValidationError("Minimum game version is not valid. Use a value such as 42 or 42.20.4.");
            if (!TryVersion(txtVersionMax.Text, out var max))
                return ValidationError("Maximum game version is not valid. Use a value such as 42 or 42.20.4.");
            if (min != null && max != null && min > max)
                return ValidationError("Minimum game version cannot be newer than the maximum game version.");

            DependencyGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            DependencyGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var dependencyIds = _dependencies.Where(x => !string.IsNullOrWhiteSpace(x.ModId)).Select(x => NormalizeDependencyId(x.ModId)).ToList();
            if (dependencyIds.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
                return ValidationError("Each dependency mod ID can appear only once.");
            if (_dependencies.Any(x => !string.IsNullOrWhiteSpace(x.ModId) && x.LoadAfter && x.LoadBefore))
                return ValidationError("A dependency cannot be both before and after this project.");
            if (_dependencies.Any(x => !string.IsNullOrWhiteSpace(x.ModId) && x.IsRequired && x.LoadBefore))
                return ValidationError("A required dependency must load before this project, so it cannot also be marked Load Before.");
            var required = _dependencies.Where(x => x.IsRequired && !string.IsNullOrWhiteSpace(x.ModId))
                .Select(x => NormalizeDependencyId(x.ModId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var incompatible = ParseLines(txtIncompatible.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var overlap = required.Intersect(incompatible, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (overlap != null)
                return ValidationError($"'{overlap}' cannot be both required and incompatible.");
            if (required.Contains(id))
                return ValidationError("A mod cannot require itself.");
            if (_dependencies.Any(x => !string.IsNullOrWhiteSpace(x.ModId) &&
                                       NormalizeDependencyId(x.ModId).Equals(id, StringComparison.OrdinalIgnoreCase)))
                return ValidationError("A mod cannot declare a dependency or load-order relationship with itself.");
            var workshop = new WorkshopSettings
            {
                Title = txtWorkshopTitle.Text.Trim(),
                Description = txtWorkshopDescription.Text.Trim(),
                Tags = ParseLines(txtWorkshopTags.Text).ToList(),
                Visibility = cmbWorkshopVisibility.SelectedItem is WorkshopVisibility visibility ? visibility : WorkshopVisibility.Public,
                PublishedFileId = txtWorkshopPublishedId.Text.Trim(),
                DefaultChangeNote = txtWorkshopChangeNote.Text.Trim()
            };
            var workshopErrors = WorkshopSettingsStore.Validate(workshop, requireUploadFields: false);
            if (workshopErrors.Count > 0)
                return ValidationError(string.Join(Environment.NewLine, workshopErrors));
            return true;
        }

        private static bool TryVersion(string value, out Version? version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
                return true;
            var normalized = value.Trim();
            if (!normalized.Contains('.'))
                normalized += ".0";
            return Version.TryParse(normalized, out version);
        }

        private static bool ValidationError(string message)
        {
            MessageBox.Show(message, "Check Project Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ChangePoster_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp",
                Multiselect = false
            };

            if (dlg.ShowDialog() == true)
            {
                _selectedPosterSourcePath = dlg.FileName;

                var fn = Path.GetFileName(dlg.FileName);

                var posters = ParseLines(txtPosters.Text).ToList();
                posters.RemoveAll(p => p.Equals(fn, StringComparison.OrdinalIgnoreCase));
                posters.Insert(0, fn);

                txtPosters.Text = string.Join(Environment.NewLine, posters);

                _posterIndex = 0;
                imgPoster.Source = new BitmapImage(new Uri(dlg.FileName));
            }
        }

        private void NextPoster_Click(object sender, RoutedEventArgs e)
        {
            var posters = ParseLines(txtPosters.Text).ToList();
            if (posters.Count == 0)
            {
                imgPoster.Source = null;
                return;
            }

            _posterIndex++;
            if (_posterIndex >= posters.Count)
                _posterIndex = 0;

            RefreshPosterPreview();
        }

        private void BrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp",
                Multiselect = false
            };

            if (dlg.ShowDialog() == true)
            {
                _selectedIconSourcePath = dlg.FileName;
                txtIcon.Text = Path.GetFileName(dlg.FileName);

                imgIcon.Source = new BitmapImage(new Uri(dlg.FileName));
            }
        }

        private void RefreshPosterPreview()
        {
            var posters = ParseLines(txtPosters.Text).ToList();
            if (posters.Count == 0)
            {
                imgPoster.Source = null;
                return;
            }

            if (_posterIndex < 0 || _posterIndex >= posters.Count)
                _posterIndex = 0;

            var entry = posters[_posterIndex];

            if (!string.IsNullOrWhiteSpace(_selectedPosterSourcePath) &&
                Path.GetFileName(_selectedPosterSourcePath).Equals(entry, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(_selectedPosterSourcePath))
            {
                imgPoster.Source = new BitmapImage(new Uri(_selectedPosterSourcePath));
                return;
            }

            var resolved = ModInfoParser.ResolveAssetPath(ModFolderPath, entry);
            imgPoster.Source = resolved != null ? new BitmapImage(new Uri(resolved)) : null;
        }

        private void RefreshIconPreview()
        {
            if (!string.IsNullOrWhiteSpace(_selectedIconSourcePath) && File.Exists(_selectedIconSourcePath))
            {
                imgIcon.Source = new BitmapImage(new Uri(_selectedIconSourcePath));
                return;
            }

            var icon = txtIcon.Text?.Trim();
            if (string.IsNullOrWhiteSpace(icon))
            {
                imgIcon.Source = null;
                return;
            }

            var resolved = ModInfoParser.ResolveAssetPath(ModFolderPath, icon);
            imgIcon.Source = resolved != null ? new BitmapImage(new Uri(resolved)) : null;
        }

        private void SetCategorySelection(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                cmbCategory.SelectedIndex = 0;
                return;
            }

            foreach (var item in cmbCategory.Items)
            {
                if (item is System.Windows.Controls.ComboBoxItem cbi &&
                    string.Equals(cbi.Content?.ToString(), category, StringComparison.OrdinalIgnoreCase))
                {
                    cmbCategory.SelectedItem = item;
                    return;
                }
            }

            cmbCategory.SelectedIndex = 0;
        }

        private string? GetCategorySelection()
        {
            if (cmbCategory.SelectedItem is System.Windows.Controls.ComboBoxItem cbi)
            {
                var s = cbi.Content?.ToString();
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
            return null;
        }

        private static string? NullIfEmpty(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static string[] ParseLines(string? multiLine)
        {
            if (string.IsNullOrWhiteSpace(multiLine))
                return Array.Empty<string>();

            return multiLine
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private void LoadDependencyRows()
        {
            _dependencies.Clear();
            var ids = _modInfo.Requires.Concat(_modInfo.LoadModAfter).Concat(_modInfo.LoadModBefore)
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids)
                _dependencies.Add(new DependencyEditorItem
                {
                    ModId = id,
                    IsRequired = _modInfo.Requires.Contains(id, StringComparer.OrdinalIgnoreCase),
                    LoadAfter = _modInfo.LoadModAfter.Contains(id, StringComparer.OrdinalIgnoreCase),
                    LoadBefore = _modInfo.LoadModBefore.Contains(id, StringComparer.OrdinalIgnoreCase)
                });
            DependencyGrid.ItemsSource = _dependencies;
        }

        private void SaveDependencyRows()
        {
            var rows = _dependencies.Where(x => !string.IsNullOrWhiteSpace(x.ModId)).ToList();
            _modInfo.Requires.Clear();
            _modInfo.Requires.AddRange(rows.Where(x => x.IsRequired).Select(x => NormalizeDependencyId(x.ModId)));
            _modInfo.LoadModAfter.Clear();
            _modInfo.LoadModAfter.AddRange(rows.Where(x => x.LoadAfter).Select(x => NormalizeDependencyId(x.ModId)));
            _modInfo.LoadModBefore.Clear();
            _modInfo.LoadModBefore.AddRange(rows.Where(x => x.LoadBefore).Select(x => NormalizeDependencyId(x.ModId)));
        }

        private async Task RefreshInstalledDependenciesAsync()
        {
            var build = ProjectEngine.CurrentProject?.Targets.FirstOrDefault(x => x.IsPrimary)?.Build ?? 42;
            btnRefreshDependencies.IsEnabled = false;
            DependencyDiscoveryStatus.Text = "Checking local Mods and Workshop content...";
            try
            {
                _installedDependencies = (await Task.Run(() => ModDependencyService.Discover(build)))
                    .Where(x => !x.ModId.Equals(_modInfo.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                cmbInstalledDependencies.ItemsSource = _installedDependencies;
                cmbInstalledDependencies.SelectedIndex = _installedDependencies.Count > 0 ? 0 : -1;
                foreach (var row in _dependencies)
                {
                    var found = ModDependencyService.Resolve(row.ModId, _installedDependencies);
                    row.Availability = found is null ? "Not found" : found.Origin;
                }
                DependencyGrid.Items.Refresh();
                DependencyDiscoveryStatus.Text = $"{_installedDependencies.Count} installed mod definition(s) found. Required mods are synchronized into Playtest Lab.";
            }
            catch (Exception ex)
            {
                DependencyDiscoveryStatus.Text = "Installed dependency scan failed: " + ex.Message;
            }
            finally { btnRefreshDependencies.IsEnabled = true; }
        }

        private void AddInstalledDependency_Click(object sender, RoutedEventArgs e)
        {
            if (cmbInstalledDependencies.SelectedItem is not DiscoveredModDependency selected)
                return;
            var existing = _dependencies.FirstOrDefault(x => x.ModId.Equals(selected.ModId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new DependencyEditorItem { ModId = selected.ModId, IsRequired = true, Availability = selected.Origin };
                _dependencies.Add(existing);
            }
            DependencyGrid.SelectedItem = existing;
            DependencyGrid.ScrollIntoView(existing);
        }

        private void AddDependencyId_Click(object sender, RoutedEventArgs e)
        {
            var item = new DependencyEditorItem { IsRequired = true, Availability = "Not found" };
            _dependencies.Add(item);
            DependencyGrid.SelectedItem = item;
            DependencyGrid.ScrollIntoView(item);
            DependencyGrid.CurrentCell = new DataGridCellInfo(item, DependencyGrid.Columns[0]);
            DependencyGrid.BeginEdit();
        }

        private async void RefreshDependencies_Click(object sender, RoutedEventArgs e)
        {
            await RefreshInstalledDependenciesAsync();
        }

        private void RemoveDependency_Click(object sender, RoutedEventArgs e)
        {
            if (DependencyGrid.SelectedItem is DependencyEditorItem selected)
                _dependencies.Remove(selected);
        }

        private void MoveDependencyUp_Click(object sender, RoutedEventArgs e) => MoveDependency(-1);
        private void MoveDependencyDown_Click(object sender, RoutedEventArgs e) => MoveDependency(1);

        private void MoveDependency(int offset)
        {
            if (DependencyGrid.SelectedItem is not DependencyEditorItem selected)
                return;
            var oldIndex = _dependencies.IndexOf(selected);
            var newIndex = oldIndex + offset;
            if (oldIndex < 0 || newIndex < 0 || newIndex >= _dependencies.Count)
                return;
            _dependencies.Move(oldIndex, newIndex);
            DependencyGrid.SelectedItem = selected;
        }

        private sealed class DependencyEditorItem
        {
            public string ModId { get; set; } = "";
            public bool IsRequired { get; set; }
            public bool LoadAfter { get; set; }
            public bool LoadBefore { get; set; }
            public string Availability { get; set; } = "Not found";
        }

        private static string NormalizeDependencyId(string value) => value.Trim().TrimStart('\\');
    }
}
