using PZTools.Core.Functions;
using PZTools.Core.Models.View;
using System.Windows;

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
