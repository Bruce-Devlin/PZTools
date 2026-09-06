using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PZTools.Core.Functions.Projects;
using PZTools.Core.Functions.Theme;
using PZTools.Core.Models;
using PZTools.Core.Models.Commands;
using PZTools.Core.Windows.Dialogs.Project;

namespace PZTools.Core.Windows;

public partial class MainWindow
{
    private static readonly HashSet<string> ImageExtensions = new([".png", ".jpg", ".jpeg", ".bmp", ".gif"], StringComparer.OrdinalIgnoreCase);
    private readonly List<int> _findOffsets = new();
    private int _findIndex = -1;

    private void InitializeWorkspace()
    {
        WorkspaceName.Text = ModProject.Name;
        WorkspaceDetails.Text = $"{ModProject.Targets.Count} build target{(ModProject.Targets.Count == 1 ? "" : "s")}  ·  {ModProject.ModInfo.Id}";
        WorkspaceName.ToolTip = ModProject.RootPath;
        ThemeManager.ThemeChanged += Workspace_ThemeChanged;
        InputBindings.Add(new KeyBinding(new RelayCommand(ShowPreviewFind), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => MoveFind(1)), Key.F3, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => MoveFind(-1)), Key.F3, ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(ShowProjectSearch), Key.F, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => NewModFile_Click(this, new RoutedEventArgs())), Key.N, ModifierKeys.Control));
        LuaEditor.TextArea.Caret.PositionChanged += (_, _) => PreviewPosition.Text =
            string.IsNullOrEmpty(OpenedFilePath) ? "" : $"Ln {LuaEditor.TextArea.Caret.Line}, Col {LuaEditor.TextArea.Caret.Column}";
    }

    private void Workspace_ThemeChanged(object? sender, string theme)
    {
        if (!IsClosing && !string.IsNullOrEmpty(OpenedFilePath))
            LoadHighlighting(Path.GetExtension(OpenedFilePath));
    }

    public void ShowProjectSearch()
    {
        var dialog = new SearchProject(ModProject) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedMatch is { } match)
        {
            try
            {
                PreviewFile(match.FullPath, match.Line, match.Column);
                LuaEditor.Focus();
            }
            catch (Exception ex)
            {
                StatusBarText.Text = $"Could not open result: {ex.Message}";
            }
        }
    }

    private void SearchProject_Click(object sender, RoutedEventArgs e) => ShowProjectSearch();

    private void ResetPreview()
    {
        _inspectorPath = null;
        StopWatchingOpenedFile();
        OpenedFilePath = string.Empty;
        LuaEditor.Clear();
        LuaEditor.SyntaxHighlighting = null;
        PreviewImage.Source = null;
        ImagePreviewPanel.Visibility = Visibility.Collapsed;
        LuaEditor.Visibility = Visibility.Visible;
        PreviewPath.Text = "Project workspace";
        PreviewPath.ToolTip = null;
        PreviewPosition.Text = "";
        PreviewKind.Text = "PREVIEW";
        PreviewFindBar.Visibility = Visibility.Collapsed;
        PreviewFindQuery.Clear();
        FilePropertiesContent.Visibility = Visibility.Collapsed;
        EmptyPropertiesState.Visibility = Visibility.Visible;
        StatusBarText.Text = "Ready";
        ShowEditorEmptyState("No file selected", "Select a project file to preview it.");
    }

    private void PreviewFile(string fullPath, int line = 0, int column = 1)
    {
        WorkspaceTabs.SelectedIndex = 0;
        ResetPreview();
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("The file no longer exists.", fullPath);
        OpenedFilePath = fullPath;
        PreviewPath.Text = Path.GetRelativePath(ModProject.RootPath, fullPath);
        PreviewPath.ToolTip = fullPath;
        FilePropertiesContent.Visibility = Visibility.Visible;
        EmptyPropertiesState.Visibility = Visibility.Collapsed;
        txtPropName.Text = info.Name;
        txtPropPath.Text = fullPath;
        txtPropSize.Text = info.Length < 1024 ? $"{info.Length} B" : $"{info.Length / 1024d:N1} KB";
        txtPropEncoding.Text = "—";
        InspectPath(fullPath);
        var extension = info.Extension;
        if (PreviewableFileExtensions.Contains(extension))
        {
            if (info.Length > ProjectSearchService.MaxFileBytes)
            {
                ShowEditorEmptyState("Large file", "This file is larger than 2 MB. Open it in VS Code to view its contents.");
                return;
            }
            var text = File.ReadAllText(fullPath);
            if (text.Contains('\0'))
            {
                ShowEditorEmptyState("Binary file", "Open this file in its associated application.");
                return;
            }
            LuaEditor.Text = text;
            LoadHighlighting(extension);
            EditorEmptyState.Visibility = Visibility.Collapsed;
            txtPropEncoding.Text = GetFileEncoding(fullPath).WebName;
            PreviewKind.Text = "READ ONLY";
            WatchOpenedFile(fullPath);
            if (line > 0)
            {
                line = Math.Clamp(line, 1, LuaEditor.Document.LineCount);
                var documentLine = LuaEditor.Document.GetLineByNumber(line);
                LuaEditor.CaretOffset = documentLine.Offset + Math.Clamp(column - 1, 0, documentLine.Length);
                LuaEditor.ScrollToLine(line);
            }
            StatusBarText.Text = "Preview updates when you save in your editor. Ctrl+F to find in this file.";
        }
        else if (ImageExtensions.Contains(extension) && info.Length <= 8 * 1024 * 1024)
        {
            using var stream = info.OpenRead();
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 1600;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewImage.Source = bitmap;
            ImagePreviewPanel.Visibility = Visibility.Visible;
            LuaEditor.Visibility = Visibility.Collapsed;
            EditorEmptyState.Visibility = Visibility.Collapsed;
            PreviewKind.Text = "IMAGE";
            StatusBarText.Text = "Image preview · scaled to fit";
        }
        else
        {
            ShowEditorEmptyState("Preview unavailable", "Open this file in its associated application or in VS Code.");
        }
    }

    private void ShowPreviewFind()
    {
        if (string.IsNullOrEmpty(OpenedFilePath) || LuaEditor.Visibility != Visibility.Visible ||
            EditorEmptyState.Visibility == Visibility.Visible) return;
        PreviewFindBar.Visibility = Visibility.Visible;
        if (LuaEditor.SelectionLength > 0 && LuaEditor.SelectionLength < 200)
            PreviewFindQuery.Text = LuaEditor.SelectedText;
        PreviewFindQuery.Focus();
        PreviewFindQuery.SelectAll();
    }

    private void PreviewFindQuery_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        RebuildFindMatches();
        if (_findOffsets.Count > 0) MoveFind(1);
    }

    private void RebuildFindMatches()
    {
        _findOffsets.Clear();
        _findIndex = -1;
        var query = PreviewFindQuery.Text;
        if (query.Length > 0)
        {
            var text = LuaEditor.Text;
            int offset = 0;
            while (offset <= text.Length - query.Length && _findOffsets.Count < 10000)
            {
                var match = text.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
                if (match < 0) break;
                _findOffsets.Add(match);
                offset = match + query.Length;
            }
        }
        if (PreviewFindCount != null) PreviewFindCount.Text = $"{_findOffsets.Count}{(_findOffsets.Count == 10000 ? "+" : "")} matches";
    }

    private void MoveFind(int direction)
    {
        if (PreviewFindBar.Visibility != Visibility.Visible) { ShowPreviewFind(); return; }
        if (_findOffsets.Count == 0) return;
        _findIndex = (_findIndex + direction + _findOffsets.Count) % _findOffsets.Count;
        var offset = _findOffsets[_findIndex];
        LuaEditor.Select(offset, PreviewFindQuery.Text.Length);
        LuaEditor.ScrollToLine(LuaEditor.Document.GetLineByOffset(offset).LineNumber);
        PreviewFindCount.Text = $"{_findIndex + 1} / {_findOffsets.Count}{(_findOffsets.Count == 10000 ? "+" : "")}";
    }

    private void PreviewFindQuery_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CloseFind_Click(sender, e); e.Handled = true; }
        if (e.Key == Key.Enter) { MoveFind(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1); e.Handled = true; }
    }

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => MoveFind(-1);
    private void FindNext_Click(object sender, RoutedEventArgs e) => MoveFind(1);
    private void CloseFind_Click(object sender, RoutedEventArgs e)
    {
        PreviewFindBar.Visibility = Visibility.Collapsed;
        LuaEditor.Focus();
    }

    private void UpdatePreviewText(string text)
    {
        RefreshInspector();
        if (text.Contains('\0'))
        {
            LuaEditor.Clear();
            PreviewFindBar.Visibility = Visibility.Collapsed;
            ShowEditorEmptyState("Binary file", "Open this file in its associated application.");
            return;
        }
        if (LuaEditor.Text == text) return;
        var offset = LuaEditor.CaretOffset;
        var vertical = LuaEditor.VerticalOffset;
        var horizontal = LuaEditor.HorizontalOffset;
        var selectionStart = LuaEditor.SelectionStart;
        var selectionLength = LuaEditor.SelectionLength;
        LuaEditor.Text = text;
        LuaEditor.CaretOffset = Math.Min(offset, text.Length);
        LuaEditor.Select(Math.Min(selectionStart, text.Length), Math.Min(selectionLength, text.Length - Math.Min(selectionStart, text.Length)));
        LuaEditor.ScrollToVerticalOffset(vertical);
        LuaEditor.ScrollToHorizontalOffset(horizontal);
        RebuildFindMatches();
        EditorEmptyState.Visibility = Visibility.Collapsed;
    }
}
