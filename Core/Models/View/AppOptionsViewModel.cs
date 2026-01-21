using Newtonsoft.Json;
using PZTools.Core.Functions;
using System.Collections.ObjectModel;
using System.Windows;

namespace PZTools.Core.Models.View
{
    public sealed class AppOptionsViewModel : ObservableObject
    {
        public AppOptionsViewModel()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var savedSettings = Config.GetObject<AppSettings>(VariableType.system, "appOptions");

            if (savedSettings == null)
                Settings = new AppSettings();

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

        private void Apply()
        {
            try
            {
                Config.StoreObject(VariableType.system, "appOptions", Settings);
                FooterText = "Saved.";
            }
            catch (Exception ex)
            {
                FooterText = $"Save failed: {ex.Message}";
            }
        }

        private void Reset()
        {
            var defaultSettings = new AppSettings();
            Settings = defaultSettings;

            // Rebuild pages with new Settings reference
            Pages.Clear();
            Pages.Add(new GeneralSettingsPageViewModel(Settings));
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
