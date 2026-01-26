using PZTools.Core.Functions;
using PZTools.Core.Functions.Theme;
using PZTools.Core.Functions.Update;
using System.Collections.ObjectModel;
using System.Windows;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace PZTools.Core.Models.View
{
    public sealed class AppOptionsViewModel : ObservableObject
    {
        public AppOptionsViewModel()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var savedSettings = Config.GetAppSettings();

            Settings = savedSettings;
            Pages = new ObservableCollection<SettingsPageViewModel>
                {
                    new GeneralSettingsPageViewModel(Settings),
                    new SystemSettingsPageViewModel(Settings),
                    new EditorSettingsPageViewModel(Settings),
                    new UpdatesSettingsPageViewModel(Settings),
                };

            _filteredPages = new ObservableCollection<SettingsPageViewModel>(Pages);
            SelectedPage = _filteredPages.FirstOrDefault();

            ApplyCommand = new RelayCommand(Apply);
            ResetCommand = new RelayCommand(Reset);
            CloseCommand = new RelayCommand(CloseWindow);
        }

        public void AppInstallPathBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowsHelpers.OpenFile(Settings.AppInstallPath);
        }

        public void ExistingGameInstallPathBtn_Click(object sender, RoutedEventArgs e)
        {
            string path = WindowsHelpers.OpenFolderBrowser("Select existing Project Zomboid installation");
            if (!string.IsNullOrEmpty(path))
            {
                Settings.ExistingGamePath = path;
            }
        }

        public void DefaultFileEditorBtn_Click(object sender, RoutedEventArgs e)
        {
            var fileDialog = new OpenFileDialog
            {
                Title = "Select Default File Editor",
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false
            };

            if (fileDialog.ShowDialog() == true)
            {
                Settings.DefaultFileEditorApp = fileDialog.FileName;
            }
        }

        public void ManagedGameInstallPathBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowsHelpers.OpenFile(Settings.ManagedGamePath);
        }

        public AppSettings Settings { get; private set; }

        public ObservableCollection<SettingsPageViewModel> Pages { get; }

        private ObservableCollection<SettingsPageViewModel> _filteredPages;
        public ObservableCollection<SettingsPageViewModel> FilteredPages
        {
            get => _filteredPages;
            private set => Set(ref _filteredPages, value);
        }

        private SettingsPageViewModel? _selectedPage;
        public SettingsPageViewModel? SelectedPage
        {
            get => _selectedPage;
            set => Set(ref _selectedPage, value);
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (!Set(ref _searchText, value)) return;
                ApplyFilter();
            }
        }

        private string _footerText = "Changes are applied immediately; Apply writes to disk.";
        public string FooterText
        {
            get => _footerText;
            set => Set(ref _footerText, value);
        }

        public RelayCommand ApplyCommand { get; }
        public RelayCommand ResetCommand { get; }
        public RelayCommand CloseCommand { get; }

        private void ApplyFilter()
        {
            var q = (SearchText ?? string.Empty).Trim();
            if (q.Length == 0)
            {
                FilteredPages = new ObservableCollection<SettingsPageViewModel>(Pages);
                if (SelectedPage == null && FilteredPages.Count > 0)
                    SelectedPage = FilteredPages[0];
                return;
            }

            var filtered = Pages.Where(p =>
                p.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(q, StringComparison.OrdinalIgnoreCase));

            FilteredPages = new ObservableCollection<SettingsPageViewModel>(filtered);

            if (FilteredPages.Count > 0 && (SelectedPage == null || !FilteredPages.Contains(SelectedPage)))
                SelectedPage = FilteredPages[0];
        }

        private async void Apply()
        {
            try
            {
                Config.StoreObject(VariableType.system, "appSettings", Settings);
                FooterText = "Saved.";
                await Config.PrintAppSettings();
                await ThemeManager.ApplyThemeFromSettings();
                App.MainWindow.ApplyAppSettings();
                AppUpdater.SwitchUpdateChannel(AppUpdater.GetChannelFromSettings());
            }
            catch (Exception ex)
            {
                FooterText = $"Save failed: {ex.Message}";
            }
        }

        private void Reset()
        {
            Settings.Reset();

            Pages.Clear();
            Pages.Add(new GeneralSettingsPageViewModel(Settings));
            Pages.Add(new SystemSettingsPageViewModel(Settings));
            Pages.Add(new EditorSettingsPageViewModel(Settings));
            Pages.Add(new UpdatesSettingsPageViewModel(Settings));

            ApplyFilter();
            FooterText = "Reset to defaults (not saved until Apply).";
        }

        private static void CloseWindow()
        {
            var win = System.Windows.Application.Current.Windows.OfType<Window>()
                .FirstOrDefault(w => w.IsActive && w.GetType().Name == "AppOptions");

            win?.Close();
        }
    }
}
