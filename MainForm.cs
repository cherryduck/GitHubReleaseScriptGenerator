using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace GitHubReleaseScriptGenerator;

public sealed class MainForm : Form
{
    private readonly TextBox githubUrlTextBox = new() { Width = 430 };
    private readonly TextBox installDirectoryTextBox = new() { Width = 330 };
    private readonly Button browseInstallDirectoryButton = new() { Text = "Browse...", Width = 90, Height = 27 };
    private readonly TextBox shortcutNameTextBox = new() { Width = 430 };
    private readonly TextBox shortcutFolderTextBox = new() { Width = 430 };
    private readonly CheckBox includePrereleasesCheckBox = new() { Text = "Include pre-releases", AutoSize = true };
    private readonly CheckBox runAfterCreationCheckBox = new() { Text = "Run generated script after creation", AutoSize = true };
    private readonly Button generateButton = new() { Text = "Generate update.ps1", Width = 170, Height = 34 };

    public MainForm()
    {
        Text = "GitHub Release Script Generator";
        Width = 625;
        Height = 390;
        MinimumSize = new System.Drawing.Size(625, 390);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 2,
            RowCount = 8
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddLabel(panel, "GitHub URL", 0);
        panel.Controls.Add(githubUrlTextBox, 1, 0);
        githubUrlTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        AddLabel(panel, "Install directory", 1);
        var installPickerPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        installDirectoryTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        browseInstallDirectoryButton.Margin = new Padding(8, 0, 0, 0);
        installPickerPanel.Controls.Add(installDirectoryTextBox);
        installPickerPanel.Controls.Add(browseInstallDirectoryButton);
        panel.Controls.Add(installPickerPanel, 1, 1);

        AddLabel(panel, "Shortcut name", 2);
        panel.Controls.Add(shortcutNameTextBox, 1, 2);
        shortcutNameTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        AddLabel(panel, "Shortcut folder", 3);
        panel.Controls.Add(shortcutFolderTextBox, 1, 3);
        shortcutFolderTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        shortcutFolderTextBox.PlaceholderText = "Optional, relative to Desktop";

        panel.Controls.Add(includePrereleasesCheckBox, 1, 4);
        panel.Controls.Add(runAfterCreationCheckBox, 1, 5);

        var help = new Label
        {
            Text = "The generated script updates from GitHub Releases, remembers selected asset/launcher/icon, creates a shortcut only when missing or invalid, then launches the app.",
            AutoSize = false,
            Dock = DockStyle.Fill
        };
        panel.Controls.Add(help, 0, 6);
        panel.SetColumnSpan(help, 2);

        panel.Controls.Add(generateButton, 1, 7);

        Controls.Add(panel);

        browseInstallDirectoryButton.Click += BrowseInstallDirectoryButton_Click;
        generateButton.Click += GenerateButton_Click;
    }

    private static void AddLabel(TableLayoutPanel panel, string text, int row)
    {
        panel.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 6, 0, 0)
        }, 0, row);
    }

    private void BrowseInstallDirectoryButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the installation directory",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(installDirectoryTextBox.Text)
                ? installDirectoryTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            installDirectoryTextBox.Text = dialog.SelectedPath;
        }
    }

    private void GenerateButton_Click(object? sender, EventArgs e)
    {
        try
        {
            var githubUrl = githubUrlTextBox.Text.Trim();
            var installDir = installDirectoryTextBox.Text.Trim();
            var shortcutName = shortcutNameTextBox.Text.Trim();
            var shortcutFolder = shortcutFolderTextBox.Text.Trim();

            var (owner, repo) = ParseGitHubUrl(githubUrl);

            if (string.IsNullOrWhiteSpace(installDir)) throw new InvalidOperationException("Installation directory is required.");
            if (string.IsNullOrWhiteSpace(shortcutName)) throw new InvalidOperationException("Shortcut name is required.");

            var script = ScriptTemplate.Build(
                targetDirectory: installDir,
                owner: owner,
                repo: repo,
                shortcutName: shortcutName,
                shortcutRelativeFolder: shortcutFolder,
                includePrereleases: includePrereleasesCheckBox.Checked);

            Directory.CreateDirectory(installDir);

            using var save = new SaveFileDialog
            {
                Title = "Save generated script",
                Filter = "PowerShell script (*.ps1)|*.ps1|All files (*.*)|*.*",
                FileName = "update.ps1",
                InitialDirectory = installDir
            };

            if (save.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(save.FileName, script);

            if (runAfterCreationCheckBox.Checked)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{save.FileName}\"",
                    WorkingDirectory = Path.GetDirectoryName(save.FileName) ?? installDir,
                    UseShellExecute = true
                };
                Process.Start(psi);
                MessageBox.Show(this, $"Script generated and started:\n{save.FileName}", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, $"Script generated:\n{save.FileName}", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot generate script", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static (string Owner, string Repo) ParseGitHubUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Enter a valid GitHub URL, for example https://github.com/cherryduck/GitHubReleaseScriptGenerator");
        }

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new InvalidOperationException("GitHub URL must include owner and repo.");

        var owner = parts[0];
        var repo = Regex.Replace(parts[1], "\\.git$", "", RegexOptions.IgnoreCase);
        return (owner, repo);
    }
}
