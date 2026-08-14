using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using VramMonitor.Services;
using VramMonitor.Theme;

namespace VramMonitor.Forms
{
    public sealed class AboutForm : Form
    {
        public const string GitHubUrl = "https://github.com/ryuya0124/VRAMViewer";

        private readonly bool _isDark;
        private readonly Label _titleLabel;
        private readonly Label _versionLabel;
        private readonly Label _descLabel;
        private readonly LinkLabel _githubLink;
        private readonly Button _okButton;

        public AboutForm(bool isDark)
        {
            _isDark = isDark;

            Text            = I18n.T("AboutDialogTitle");
            Size            = new Size(460, 310);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            ShowInTaskbar   = false;
            StartPosition   = FormStartPosition.CenterParent;
            Font            = new Font("Segoe UI", 9F);

            var panel = new Panel
            {
                Dock    = DockStyle.Fill,
                Padding = new Padding(24, 20, 24, 16),
            };

            _titleLabel = new Label
            {
                Text      = "VRAMViewer",
                Font      = new Font("Segoe UI", 16F, FontStyle.Bold),
                AutoSize  = true,
                Location  = new Point(24, 18),
            };

            _versionLabel = new Label
            {
                Text     = "Version 1.0.0",
                Font     = new Font("Segoe UI", 9F, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(26, 52),
            };

            _descLabel = new Label
            {
                Text     = I18n.T("AboutDialogMessageContent"),
                Font     = new Font("Segoe UI", 9F),
                Location = new Point(26, 80),
                Size     = new Size(390, 85),
            };

            _githubLink = new LinkLabel
            {
                Text     = GitHubUrl,
                Font     = new Font("Segoe UI", 9F, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(26, 175),
                Cursor   = Cursors.Hand,
            };
            _githubLink.LinkClicked += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName        = GitHubUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            };

            _okButton = new Button
            {
                Text      = "OK",
                Size      = new Size(88, 30),
                Location  = new Point(328, 220),
                FlatStyle = FlatStyle.Flat,
                Cursor    = Cursors.Hand,
            };
            _okButton.Click += (_, _) => Close();
            AcceptButton = _okButton;

            panel.Controls.Add(_titleLabel);
            panel.Controls.Add(_versionLabel);
            panel.Controls.Add(_descLabel);
            panel.Controls.Add(_githubLink);
            panel.Controls.Add(_okButton);

            Controls.Add(panel);

            ApplyThemeColors();
        }

        private void ApplyThemeColors()
        {
            if (_isDark)
            {
                BackColor = ThemeManager.Dark.WindowBg;
                _titleLabel.ForeColor = ThemeManager.Dark.TextPrimary;
                _versionLabel.ForeColor = ThemeManager.Dark.TextSecondary;
                _descLabel.ForeColor = ThemeManager.Dark.TextPrimary;
                _githubLink.LinkColor = Color.FromArgb(88, 166, 255);
                _githubLink.ActiveLinkColor = Color.FromArgb(121, 192, 255);
                _githubLink.VisitedLinkColor = Color.FromArgb(88, 166, 255);

                _okButton.BackColor = ThemeManager.Dark.ControlBg;
                _okButton.ForeColor = ThemeManager.Dark.TextPrimary;
                _okButton.FlatAppearance.BorderColor = ThemeManager.Dark.ControlBorder;
            }
            else
            {
                BackColor = ThemeManager.Light.WindowBg;
                _titleLabel.ForeColor = ThemeManager.Light.TextPrimary;
                _versionLabel.ForeColor = ThemeManager.Light.TextSecondary;
                _descLabel.ForeColor = ThemeManager.Light.TextPrimary;
                _githubLink.LinkColor = Color.FromArgb(9, 105, 218);
                _githubLink.ActiveLinkColor = Color.FromArgb(14, 68, 158);
                _githubLink.VisitedLinkColor = Color.FromArgb(9, 105, 218);

                _okButton.BackColor = ThemeManager.Light.ControlBg;
                _okButton.ForeColor = ThemeManager.Light.TextPrimary;
                _okButton.FlatAppearance.BorderColor = ThemeManager.Light.ControlBorder;
            }

            Load += (_, _) =>
            {
                ThemeManager.SetWindowDarkMode(Handle, _isDark);
            };
        }
    }
}
