using System.IO;
using System.Windows;

namespace PZTools.Core.Windows.Dialogs.Project
{
    public partial class ProjectSettings : Window
    {
        public string ModFolderPath { get; set; } = "";
        private string PosterFilePath = "";

        public ProjectSettings(string modFolderPath)
        {
            InitializeComponent();
            ModFolderPath = modFolderPath;

            LoadModInfo();
        }

        private void LoadModInfo()
        {
            string infoPath = Path.Combine(ModFolderPath, "mod.info");
            if (!File.Exists(infoPath)) return;

            var lines = File.ReadAllLines(infoPath);
            foreach (var line in lines)
            {
                if (line.StartsWith("name=")) txtModName.Text = line.Substring(5);
                if (line.StartsWith("id=")) txtModID.Text = line.Substring(3);
                if (line.StartsWith("description=")) txtDescription.Text = line.Substring(12);
                if (line.StartsWith("poster="))
                {
                    string posterPath = Path.Combine(ModFolderPath, line.Substring(7));
                    if (File.Exists(posterPath))
                    {
                        PosterFilePath = posterPath;
                        imgPoster.Source = new System.Windows.Media.Imaging.BitmapImage(new System.Uri(posterPath));
                    }
                }
            }
        }

        private void ChangePoster_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp";
            if (dlg.ShowDialog() == true)
            {
                PosterFilePath = dlg.FileName;
                imgPoster.Source = new System.Windows.Media.Imaging.BitmapImage(new System.Uri(PosterFilePath));
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string infoPath = Path.Combine(ModFolderPath, "mod.info");
            var lines = new string[]
            {
                $"name={txtModName.Text}",
                $"id={txtModID.Text}",
                $"description={txtDescription.Text}",
                $"poster={Path.GetFileName(PosterFilePath)}"
            };
            File.WriteAllLines(infoPath, lines);

            if (!string.IsNullOrEmpty(PosterFilePath))
            {
                string dest = Path.Combine(ModFolderPath, Path.GetFileName(PosterFilePath));
                if (PosterFilePath != dest)
                    File.Copy(PosterFilePath, dest, true);
            }

            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
