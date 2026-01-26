using PZTools.Core.Functions;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PZTools.Core.Windows.Dialogs.Project
{
    public partial class ProjectSettings : Window
    {
        public string ModFolderPath { get; set; } = "";

        private ModInfo _modInfo = new();
        private string? _selectedPosterSourcePath;
        private string? _selectedIconSourcePath;

        private int _posterIndex = 0;

        public ProjectSettings(string modFolderPath)
        {
            InitializeComponent();
            this.FreeDragThisWindow();

            ModFolderPath = modFolderPath;
            LoadModInfo();
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

            txtRequire.Text = string.Join(Environment.NewLine, _modInfo.Requires);
            txtIncompatible.Text = string.Join(Environment.NewLine, _modInfo.Incompatibles);
            txtLoadAfter.Text = string.Join(Environment.NewLine, _modInfo.LoadModAfter);
            txtLoadBefore.Text = string.Join(Environment.NewLine, _modInfo.LoadModBefore);

            txtIcon.Text = _modInfo.Icon ?? "";

            _posterIndex = 0;
            RefreshPosterPreview();
            RefreshIconPreview();

            var luaWatermark = Config.GetVariable(VariableType.user, $"{ProjectEngine.CurrentProject.Name}-watermark");
            if (!string.IsNullOrEmpty(luaWatermark))
                txtWatermark.Text = luaWatermark;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
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

            _modInfo.Requires.Clear();
            _modInfo.Requires.AddRange(ParseLines(txtRequire.Text));

            _modInfo.Incompatibles.Clear();
            _modInfo.Incompatibles.AddRange(ParseLines(txtIncompatible.Text));

            _modInfo.LoadModAfter.Clear();
            _modInfo.LoadModAfter.AddRange(ParseLines(txtLoadAfter.Text));

            _modInfo.LoadModBefore.Clear();
            _modInfo.LoadModBefore.AddRange(ParseLines(txtLoadBefore.Text));

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

            ModInfoParser.Save(ModFolderPath, _modInfo);
            ProjectEngine.CurrentProject.ModInfo = _modInfo;
            ProjectEngine.CurrentProject.UpdateModInfo(_modInfo);

            if (!string.IsNullOrEmpty(txtWatermark.Text))
                Config.StoreVariable(VariableType.user, $"{ProjectEngine.CurrentProject.Name}-watermark", txtWatermark.Text);


            DialogResult = true;
            Close();
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
    }
}
