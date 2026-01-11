using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void GitHub_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/your-repo",
                UseShellExecute = true
            });
        }
    }
}
