using PZTools.Core.Functions;
using System.Windows;

namespace PZTools.Core.Windows.Dialogs
{
    /// <summary>
    /// Interaction logic for Login.xaml
    /// </summary>
    public partial class SteamLogin : Window
    {
        public string Username { get; private set; }
        public SteamLogin()
        {
            InitializeComponent();
            this.FreeDragThisWindow();

            Loaded += (_, _) => TxtUsername.Focus();
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            Username = TxtUsername.Text.Trim();

            if (string.IsNullOrWhiteSpace(Username))
            {
                MessageBox.Show("Please enter your Steam username.");
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
