using System.Windows;

namespace PZTools.Core.Windows.Dialogs.Project
{
    public partial class InputDialogs : Window
    {
        public string ResponseText => InputTextBox.Text;

        public InputDialogs(string question, string title = "")
        {
            InitializeComponent();
            Title = title;
            QuestionText.Text = question;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
