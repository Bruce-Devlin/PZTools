using System.IO;
using System.Windows;
using System.Windows.Controls;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Models;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace PZTools.Core.Windows.Dialogs.Project;

public sealed class ProjectStorageWindow : Window
{
    public ProjectStorageWindow(ModProject project)
    {
        Title = "Project storage and Git coverage"; Width = 820; Height = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Brush.BgApp"); SetResourceReference(ForegroundProperty, "Brush.TextPrimary");
        var panel = new DockPanel { Margin = new Thickness(16) };
        var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(details); Content = panel;
        string? destination = null;
        var copy = new Button { Content = "Copy reviewed source", IsEnabled = false, Margin = new Thickness(4), Padding = new Thickness(10) };
        void ShowStatus()
        {
            var status = ProjectStorageService.Inspect(project.RootPath);
            details.Text = $"Source location: {status.Path}\n{(status.InBuildOutput ? "WARNING: source is inside build output and may be removed by build cleanup.\n" : "")}Git root: {status.GitRoot}\n{status.Detail}\n\n" +
                "Copy workflow preserves the original project. It copies deployable source and .pztests to a new folder; local run artifacts and existing Git history stay at the original location. Review the file list before copying.\n\n" +
                "Git setup runs only git init here. It does not stage, commit, publish, or change parent ignore rules. Review coverage afterward.";
        }
        var choose = new Button { Content = "Review source copy...", Margin = new Thickness(4), Padding = new Thickness(10) };
        choose.Click += (_, _) =>
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a durable parent folder for a new project copy", UseDescriptionForTitle = true };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            destination = Path.Combine(dialog.SelectedPath, new DirectoryInfo(project.RootPath).Name);
            try { details.Text = $"Source: {project.RootPath}\nDestination: {destination}\nOriginal source retained. Files to copy:\n" + string.Join("\n", ProjectStorageService.RelocationFiles(project.RootPath)); copy.IsEnabled = true; }
            catch (Exception ex) { details.Text = ex.Message; }
        };
        copy.Click += (_, _) =>
        {
            try { ProjectStorageService.CopySource(project.RootPath, destination!); details.Text = "Verified source copy: " + destination + "\nOriginal project remains open. Import/open the new location to switch."; copy.IsEnabled = false; }
            catch (Exception ex) { details.Text = ex.Message; }
        };
        var git = new Button { Content = "Initialize Git here", Margin = new Thickness(4), Padding = new Thickness(10) };
        git.Click += (_, _) =>
        {
            try
            {
                if (ProjectStorageService.Inspect(project.RootPath).GitRoot.Length > 0) { details.Text = "This source already belongs to a Git repository. Review its ignored and untracked files before staging changes."; return; }
                var result = ProjectStorageService.Git(project.RootPath, "init");
                if (result.Exit != 0) throw new IOException(result.Output);
                ShowStatus();
            }
            catch (Exception ex) { details.Text = ex.Message; }
        };
        actions.Children.Add(choose); actions.Children.Add(copy); actions.Children.Add(git);
        Loaded += (_, _) => ShowStatus();
    }
}
