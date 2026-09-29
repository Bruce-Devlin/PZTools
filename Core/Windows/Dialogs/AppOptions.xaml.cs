using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using PZTools.Core.Functions;
using PZTools.Core.Models.View;

namespace PZTools.Core.Windows.Dialogs
{
    /// <summary>
    /// Interaction logic for AppOptions.xaml
    /// </summary>
    public partial class AppOptions : Window
    {
        public AppOptions()
        {
            InitializeComponent();
            this.FreeDragThisWindow();
        }

        private void AiGuidance_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            e.Handled = true;
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open the PZwiki guidance in your browser.\n\n{e.Uri.AbsoluteUri}\n\n{ex.Message}",
                    "AI in modding", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void AppInstallPathBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AppOptionsViewModel vm)
            {
                vm.AppInstallPathBtn_Click(sender, e);
            }
        }

        private void ExistingGameInstallPathBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AppOptionsViewModel vm)
            {
                vm.ExistingGameInstallPathBtn_Click(sender, e);
            }
        }

        private void ManagedGameInstallPathBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AppOptionsViewModel vm)
            {
                vm.ManagedGameInstallPathBtn_Click(sender, e);
            }
        }

        private void DefaultFileEditorBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AppOptionsViewModel vm)
            {
                vm.DefaultFileEditorBtn_Click(sender, e);
            }
        }
    }
}
