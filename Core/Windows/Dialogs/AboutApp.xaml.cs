using PZTools.Core.Functions;
using System.Diagnostics;
using System.Windows;

namespace PZTools.Core.Windows.Dialogs
{
    /// <summary>
    /// Interaction logic for AboutApp.xaml
    /// </summary>
    public partial class AboutApp : Window
    {
        public AboutApp()
        {
            InitializeComponent();
            this.FreeDragThisWindow();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void GitHub_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/Bruce-Devlin/PZTools",
                UseShellExecute = true
            });
        }
    }
}
