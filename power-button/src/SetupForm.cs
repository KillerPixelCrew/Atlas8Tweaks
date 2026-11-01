using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Atlas8PowerButton
{
    /// <summary>One screen: what this fixes, whether this is an Atlas 8, and one big Install button.</summary>
    internal sealed class SetupForm : Form
    {
        private readonly Button _install;
        private readonly Button _remove;
        private readonly Button _cancel;
        private readonly CheckBox _tray;
        private readonly Ui.StatusLine _status;
        private readonly string _deviceProblem;

        public SetupForm()
        {
            Ui.SetUpForm(this, Strings.SetupTitle);
            var column = Ui.Column(this);
            column.Controls.Add(Ui.Header(this, Strings.AppName));
            column.Controls.Add(Ui.Text(this, Strings.SetupIntro, below: 18));

            _deviceProblem = Installer.DeviceProblem();
            _status = new Ui.StatusLine(this);
            var installed = Installer.InstalledVersion;
            if (_deviceProblem != null)
            {
                _status.Show(Ui.Red, _deviceProblem);
            }
            else
            {
                _status.Show(Ui.Green, Strings.SetupDeviceOk + " " +
                                       (installed == null ? Strings.SetupNotInstalled : Strings.SetupInstalled(installed)));
            }

            column.Controls.Add(_status);
            _tray = Ui.Check(this, Strings.ShowTrayIcon);
            _tray.Checked = installed == null || Installer.TrayAtSignIn;
            _tray.Visible = _deviceProblem == null;
            column.Controls.Add(_tray);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                WrapContents = false,
                Margin = new Padding(0, Ui.S(this, 18), 0, 0)
            };
            var primaryText = installed == null
                ? Strings.SetupInstall
                : CompareVersions(installed, Installer.Version) < 0 ? Strings.SetupUpdate : Strings.SetupReinstall;
            _install = Ui.Button(this, primaryText, true);
            _install.Visible = _deviceProblem == null;
            _install.Click += async (s, e) => await Run(Install);
            _cancel = Ui.Button(this, Strings.SetupCancel, false);
            _cancel.Click += (s, e) => Close();
            _remove = Ui.Button(this, Strings.SetupRemove, false);
            _remove.Visible = installed != null;
            _remove.Click += async (s, e) =>
            {
                if (Confirm(Strings.SetupRemoveConfirm))
                {
                    await Run(Remove);
                }
            };
            buttons.Controls.Add(_install);
            buttons.Controls.Add(_cancel);
            buttons.Controls.Add(_remove);
            column.Controls.Add(buttons);
            Controls.Add(column);
            AcceptButton = _install.Visible ? _install : _cancel;
            CancelButton = _cancel;
        }

        public static bool Confirm(string text)
        {
            return MessageBox.Show(text, Strings.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private async Task Run(Func<Task> work)
        {
            _install.Enabled = _remove.Enabled = _cancel.Enabled = _tray.Enabled = false;
            UseWaitCursor = true;
            _status.Show(Ui.Grey, Strings.SetupWorking);
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                Log.Write("Setup failed: " + ex);
                UseWaitCursor = false;
                MessageBox.Show(this, Strings.SetupFailed(ex.Message), Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            Close();
        }

        private async Task Install()
        {
            var tray = _tray.Checked;
            await Task.Run(() => Installer.Install(tray));
            UseWaitCursor = false;
            MessageBox.Show(this, Strings.SetupDone, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Installer.LaunchAsUser();
        }

        private async Task Remove()
        {
            await Task.Run(Installer.Uninstall);
            UseWaitCursor = false;
            MessageBox.Show(this, Strings.SetupRemoved, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static int CompareVersions(string a, string b)
        {
            return Version.TryParse(a, out var x) && Version.TryParse(b, out var y) ? x.CompareTo(y) : 0;
        }
    }
}
