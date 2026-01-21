using PZTools.Core.Models.InputDialog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace PZTools.Core.Windows.Dialogs.Project
{
    public partial class InputDialogs : Window, INotifyPropertyChanged
    {
        public string Question { get; }
        public string? Subtext { get; }

        public ObservableCollection<InputFieldViewModel> Fields { get; }

        public Dictionary<string, string> Responses =>
            Fields.ToDictionary(f => f.Key, f => f.Value ?? string.Empty);

        public string TryGetResponse(string key)
            => Fields.FirstOrDefault(f => f.Key == key)?.Value ?? string.Empty;

        public bool CanSubmit => Fields.All(f => f.IsValid);

        public InputDialogs(
            string question,
            IEnumerable<InputFieldDefinition> fields,
            string title = "Input",
            string? subtext = null)
        {
            InitializeComponent();

            Title = title;
            Question = question;
            Subtext = subtext;

            Fields = new ObservableCollection<InputFieldViewModel>(fields.Select(f => new InputFieldViewModel(f)));

            foreach (var field in Fields)
                field.PropertyChanged += (_, __) => OnPropertyChanged(nameof(CanSubmit));

            DataContext = this;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanSubmit)
                return;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
