using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using PZTools.Core.Functions;
using PZTools.Core.Functions.Steam;

namespace PZTools.Core.Windows.Dialogs.Project
{
    public partial class WorkshopUploadReview : Window
    {
        public string ChangeNote => ChangeNoteText.Text.Trim();

        public WorkshopUploadReview(WorkshopUploadPlan plan)
        {
            InitializeComponent();
            ActionText.Text = plan.IsNewItem ? "Create a new Workshop item" : "Update the existing Workshop item";
            ItemIdText.Text = plan.IsNewItem ? "Assigned by Steam after upload" : plan.Settings.PublishedFileId;
            VisibilityText.Text = WorkshopSettingsStore.VisibilityLabel(plan.Settings.Visibility);
            TagsText.Text = string.Join(", ", plan.Settings.Tags);
            PreviewText.Text = plan.PreviewPath;
            TitleText.Text = plan.Settings.Title;
            DescriptionText.Text = plan.Settings.Description;
            ChangeNoteText.Text = plan.Settings.DefaultChangeNote;
            Loaded += (_, _) => ChangeNoteText.Focus();
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ChangeNote))
            {
                MessageBox.Show("Enter a change note for this upload.", "Steam Workshop", MessageBoxButton.OK, MessageBoxImage.Warning);
                ChangeNoteText.Focus();
                return;
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void LegalAgreement_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
