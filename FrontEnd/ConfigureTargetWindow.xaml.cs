using System;
using System.Windows;
using Microsoft.Win32;

namespace MiSTerCast
{
    public partial class ConfigureTargetWindow : Window
    {
        internal GroovyTargetDeploymentConfig DeploymentConfig { get; private set; }

        public ConfigureTargetWindow(GroovyTargetDeploymentConfig config)
        {
            InitializeComponent();
            if (config == null)
                config = new GroovyTargetDeploymentConfig();

            TargetTextBox.Text = config.Target ?? "";
            UsernameTextBox.Text = String.IsNullOrWhiteSpace(config.Username) ? GroovyTargetConfigurator.DefaultUsername : config.Username;
            PasswordBox.Password = config.Password ?? GroovyTargetConfigurator.DefaultPassword;
            MisterBinaryPathTextBox.Text = config.MisterBinaryPath ?? "";
            GroovyRbfPathTextBox.Text = config.GroovyRbfPath ?? "";
            ForceRedeployCheckBox.IsChecked = config.ForceRedeploy;
        }

        private void BrowseMisterBinaryButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            dialog.Title = "Select MiSTer_groovy";
            dialog.Filter = "MiSTer_groovy|MiSTer_groovy|All Files|*.*";
            if (dialog.ShowDialog(this) == true)
                MisterBinaryPathTextBox.Text = dialog.FileName;
        }

        private void BrowseGroovyRbfButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            dialog.Title = "Select Groovy RBF";
            dialog.Filter = "RBF Files|*.rbf|All Files|*.*";
            if (dialog.ShowDialog(this) == true)
                GroovyRbfPathTextBox.Text = dialog.FileName;
        }

        private async void DownloadLatestButton_Click(object sender, RoutedEventArgs e)
        {
            DownloadLatestButton.IsEnabled = false;
            ReleaseStatusTextBlock.Text = "Checking GitHub...";

            try
            {
                var downloader = new GroovyReleaseDownloader();
                GroovyReleaseInfo release = await downloader.DownloadLatestAsync((message, error) =>
                {
                    ReleaseStatusTextBlock.Text = message;
                });

                MisterBinaryPathTextBox.Text = release.MisterBinaryPath;
                GroovyRbfPathTextBox.Text = release.GroovyRbfPath;
                ReleaseStatusTextBlock.Text = "Ready: " + release.DisplayName;
            }
            catch (Exception exception)
            {
                ReleaseStatusTextBlock.Text = "Download failed";
                MessageBox.Show(this, exception.Message, "Download Latest", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                DownloadLatestButton.IsEnabled = true;
            }
        }

        private void DeployButton_Click(object sender, RoutedEventArgs e)
        {
            if (String.IsNullOrWhiteSpace(TargetTextBox.Text))
            {
                MessageBox.Show(this, "Target is required.", "Configure Target", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (String.IsNullOrWhiteSpace(UsernameTextBox.Text))
            {
                MessageBox.Show(this, "Username is required.", "Configure Target", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DeploymentConfig = new GroovyTargetDeploymentConfig
            {
                Target = TargetTextBox.Text.Trim(),
                Username = UsernameTextBox.Text.Trim(),
                Password = PasswordBox.Password,
                MisterBinaryPath = MisterBinaryPathTextBox.Text.Trim(),
                GroovyRbfPath = GroovyRbfPathTextBox.Text.Trim(),
                ForceRedeploy = ForceRedeployCheckBox.IsChecked == true
            };
            DialogResult = true;
        }
    }
}
