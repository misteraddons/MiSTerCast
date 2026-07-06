using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MiSTerCast
{
    struct SourceOptions
    {
        public byte display;
        public bool audio;
        public bool preview;
        public byte alignment;
        public byte cropmode;
        public UInt16 width;
        public UInt16 height;
        public Int16 xoffset;
        public Int16 yoffset;
        public byte rotation;
    }

    public partial class MainWindow : Window
    {
        private bool isInitialized = false;
        private bool isStreaming = false;
        HelpWindow helpWindow = null;
        const string lastSaveFilename = "lastsave.dat";
        string currentSaveFilename = null;
        private DispatcherTimer statusTimer;

        private void InitializeMiSTerCast()
        {
            if (LogDelegate == null)
                LogDelegate = new MiSTerCastInterop.LogDelegate(LogCallback);
            if (CaptureImageDelegate == null)
                CaptureImageDelegate = new MiSTerCastInterop.CaptureImageDelegate(CaptureImage);
            isInitialized = MiSTerCastInterop.Initialize(LogDelegate, CaptureImageDelegate);
            if (isInitialized)
            {
                OnModelineChanged();
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            ReadModelinesFile();
            PopulateModelineDropdown();
            InitializeMiSTerCast();
            statusTimer = new DispatcherTimer();
            statusTimer.Interval = TimeSpan.FromMilliseconds(500);
            statusTimer.Tick += StatusTimer_Tick;
            statusTimer.Start();
            UpdateStreamStatus();
        }

        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            UpdateStreamStatus();
        }

        private void UpdateStreamStatus()
        {
            MiSTerCastInterop.StreamStats stats;
            if (!MiSTerCastInterop.GetStreamStats(out stats))
            {
                StreamStatusTextBlock.Text = "Status: Unavailable";
                return;
            }

            if (stats.streamFailed != 0)
            {
                if (isStreaming)
                {
                    MiSTerCastInterop.StopStream();
                    SetStreamControls(false);
                }
                StreamStatusTextBlock.Text = "Status: Stream failed - " + DescribeStreamError(stats.streamError);
                return;
            }

            if (stats.streaming != 0)
            {
                StreamStatusTextBlock.Text = StreamStatusFormatter.Format(stats, EnableAudioCheckBox.IsChecked == true);
            }
            else if (stats.capturing != 0)
            {
                StreamStatusTextBlock.Text = "Status: Ready";
            }
            else if (stats.initialized != 0)
            {
                StreamStatusTextBlock.Text = "Status: Initialized";
            }
            else
            {
                StreamStatusTextBlock.Text = "Status: Not initialized";
            }
        }

        private void SetStreamControls(bool streaming)
        {
            isStreaming = streaming;
            ToggleStreamButton.IsEnabled = true;
            ToggleStreamButton.Content = streaming ? "Stop Stream" : "Start Stream";
            CaptureSourceBox.IsEnabled = !streaming;
            EnableAudioCheckBox.IsEnabled = !streaming;
            TestTargetButton.IsEnabled = !streaming;
            ConfigureTargetButton.IsEnabled = !streaming;
            if (!streaming)
                ApplyModelineButton.IsEnabled = false;
        }

        private string DescribeStreamError(UInt32 error)
        {
            switch (error)
            {
                case 23:
                    return "no UDP ACK from MiSTer; use Test Target";
                case 100:
                    return "native stream thread error";
                case 101:
                    return "target IP is empty";
                case 0:
                    return "startup failed";
                default:
                    return "native error " + error;
            }
        }

        private async void TestTargetButton_Click(object sender, RoutedEventArgs e)
        {
            TestTargetButton.IsEnabled = false;
            StreamStatusTextBlock.Text = "Status: Testing target...";

            try
            {
                var result = await GroovyMisterProbe.ProbeAsync(TargetIpAddresTextBox.Text);
                if (result.Success)
                {
                    StreamStatusTextBlock.Text = String.Format(
                        "Status: Groovy_MiSTer detected at {0}:{1}",
                        result.Address,
                        result.Port);
                    Log(String.Format(
                        "Groovy_MiSTer detected at {0}:{1}. frame={2}, vcount={3}, status=0x{4:X2}",
                        result.Address,
                        result.Port,
                        result.Frame,
                        result.VCount,
                        result.StatusBits));
                }
                else
                {
                    StreamStatusTextBlock.Text = "Status: Target test failed - " + result.Message;
                    Log(String.Format("Target test failed for {0}:{1}: {2}", result.Address, result.Port, result.Message), true);
                    Log("Check that Groovy.rbf is loaded, MiSTer_groovy is installed and configured in MiSTer.ini, UDP 32100 is reachable, and a direct gigabit connection is preferred.", true);
                }
            }
            catch (Exception exception)
            {
                StreamStatusTextBlock.Text = "Status: Target test failed";
                Log("Target test failed: " + exception.Message, true);
            }
            finally
            {
                if (!isStreaming)
                    TestTargetButton.IsEnabled = true;
            }
        }

        private async void ConfigureTargetButton_Click(object sender, RoutedEventArgs e)
        {
            await ConfigureTargetWithDialogAsync();
        }

        private async Task<bool> ConfigureTargetWithDialogAsync()
        {
            var dialog = new ConfigureTargetWindow(CreateDefaultTargetDeploymentConfig());
            dialog.Owner = this;
            if (dialog.ShowDialog() != true)
                return false;

            var config = dialog.DeploymentConfig;
            return await ConfigureTargetAsync(config);
        }

        private async Task<bool> ConfigureTargetAsync(GroovyTargetDeploymentConfig config)
        {
            TargetIpAddresTextBox.Text = config.Target;
            ConfigureTargetButton.IsEnabled = false;
            TestTargetButton.IsEnabled = false;
            ToggleStreamButton.IsEnabled = false;
            StreamStatusTextBlock.Text = "Status: Configuring target...";

            try
            {
                var deployer = new GroovyTargetDeployer();
                await deployer.ConfigureAndLaunchAsync(config, Log);
                StreamStatusTextBlock.Text = "Status: Groovy core launched; waiting for UDP ACK...";
                Log("Groovy core launch command sent. Waiting for UDP ACK...");

                var result = await GroovyMisterProbe.ProbeUntilAsync(
                    config.Target,
                    GroovyMisterProbe.DefaultPostLaunchProbeTimeoutMilliseconds,
                    GroovyMisterProbe.DefaultPostLaunchAttemptTimeoutMilliseconds,
                    GroovyMisterProbe.DefaultPostLaunchRetryDelayMilliseconds,
                    Log);
                if (result.Success)
                {
                    StreamStatusTextBlock.Text = String.Format(
                        "Status: Groovy_MiSTer detected at {0}:{1}",
                        result.Address,
                        result.Port);
                    Log(String.Format(
                        "Groovy_MiSTer detected at {0}:{1}. frame={2}, vcount={3}, status=0x{4:X2}",
                        result.Address,
                        result.Port,
                        result.Frame,
                        result.VCount,
                        result.StatusBits));
                    return true;
                }
                else
                {
                    StreamStatusTextBlock.Text = "Status: Target configured; probe failed - " + result.Message;
                    Log("Groovy target configured, but UDP probe failed: " + result.Message, true);
                    return false;
                }
            }
            catch (Exception exception)
            {
                StreamStatusTextBlock.Text = "Status: Configure target failed";
                Log("Configure target failed: " + exception.Message, true);
                return false;
            }
            finally
            {
                ToggleStreamButton.IsEnabled = true;
                if (!isStreaming)
                {
                    ConfigureTargetButton.IsEnabled = true;
                    TestTargetButton.IsEnabled = true;
                }
            }
        }

        private GroovyTargetDeploymentConfig CreateDefaultTargetDeploymentConfig()
        {
            return new GroovyTargetDeploymentConfig
            {
                Target = TargetIpAddresTextBox.Text,
                Username = GroovyTargetConfigurator.DefaultUsername,
                Password = GroovyTargetConfigurator.DefaultPassword,
                MisterBinaryPath = GroovyTargetConfigurator.FindDefaultMisterBinaryPath(),
                GroovyRbfPath = GroovyTargetConfigurator.FindDefaultGroovyRbfPath(),
                ForceRedeploy = false
            };
        }

        void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (isStreaming)
                MiSTerCastInterop.StopStream();
            MiSTerCastInterop.Shutdown();
            if (helpWindow != null)
                helpWindow.Close();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            helpWindow.Show();
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            if (helpWindow == null)
            {
                helpWindow = new HelpWindow();
                if (!File.Exists(lastSaveFilename))
                {
                    // Show help the first time MiSTerCast is opened
                    helpWindow.Show();
                    try
                    {
                        File.Create(lastSaveFilename);
                    }
                    catch (Exception exception)
                    {
                        Log("Creating last save file failed: " + exception.Message, true);
                    }
                }
                else
                {
                    try
                    {
                        currentSaveFilename = null;
                        using (StreamReader sr = File.OpenText(lastSaveFilename))
                        {
                            if (!sr.EndOfStream)
                            {
                                currentSaveFilename = sr.ReadLine();
                                if (!File.Exists(currentSaveFilename))
                                {
                                    currentSaveFilename = null;
                                    Log("Last save is missing.", true);
                                }
                            }
                        }

                        if (currentSaveFilename != null)
                        {
                            Log("Auto loading settings: " + currentSaveFilename);
                            using (StreamReader sr = File.OpenText(currentSaveFilename))
                            {
                                LoadSaveFileFromStream(sr);
                            }
                        }
                    }
                    catch (Exception exception)
                    {
                        Log("Reading last save file failed: " + exception.Message, true);
                    }
                }
            }
        }

        private async void ToggleStreamButton_Click(object sender, RoutedEventArgs e)
        {
            if (isStreaming)
            {
                if (MiSTerCastInterop.StopStream())
                {
                    SetStreamControls(false);
                }
            }
            else
            {
                await StartStreamWithGuardAsync();
            }
        }

        private async Task StartStreamWithGuardAsync()
        {
            if (!isInitialized)
                InitializeMiSTerCast();
            if (!isInitialized)
                return;

            string target = TargetIpAddresTextBox.Text.Trim();
            IPAddress ipAddress;
            if (!TryResolveTargetIpAddress(target, out ipAddress))
                return;

            ToggleStreamButton.IsEnabled = false;
            ConfigureTargetButton.IsEnabled = false;
            TestTargetButton.IsEnabled = false;

            try
            {
                EnablePreviewCheckBox.IsChecked = false;
                StreamStatusTextBlock.Text = "Status: Checking Groovy_MiSTer...";
                var probe = await GroovyMisterProbe.ProbeAsync(target, 1000);
                if (!probe.Success)
                {
                    Log("Groovy_MiSTer is not responding; checking target setup...");
                    bool ready = await EnsureGroovyReadyForStreamingAsync(target);
                    if (!ready)
                    {
                        StreamStatusTextBlock.Text = "Status: Start Stream canceled";
                        return;
                    }

                    if (!TryResolveTargetIpAddress(target, out ipAddress))
                        return;
                }

                if (MiSTerCastInterop.StartStream(ipAddress.ToString()))
                    SetStreamControls(true);
            }
            finally
            {
                if (!isStreaming)
                {
                    ToggleStreamButton.IsEnabled = true;
                    ConfigureTargetButton.IsEnabled = true;
                    TestTargetButton.IsEnabled = true;
                }
            }
        }

        private async Task<bool> EnsureGroovyReadyForStreamingAsync(string target)
        {
            var config = CreateDefaultTargetDeploymentConfig();
            config.Target = target;
            var deployer = new GroovyTargetDeployer();

            try
            {
                GroovyReleaseInfo latestRelease = await TryGetLatestGroovyReleaseAsync();
                GroovyTargetStatus status = await deployer.GetTargetStatusAsync(config, Log);
                GroovyStreamRepairAction action = GroovyStreamGuard.DecideRepairAction(status, latestRelease);

                if (action == GroovyStreamRepairAction.InstallLatest)
                    return await PromptInstallLatestAndStartAsync(config, latestRelease);

                if (action == GroovyStreamRepairAction.UpdateLatest)
                    return await PromptUpdateLatestOrLaunchExistingAsync(config, latestRelease, status.Manifest);

                if (action == GroovyStreamRepairAction.OfferUpdateOrLaunchExisting)
                    return await PromptUnknownVersionUpdateOrLaunchAsync(config, latestRelease);

                return await LaunchExistingAndWaitAsync(config, deployer);
            }
            catch (Exception exception)
            {
                Log("Automatic target repair failed: " + exception.Message, true);
                MessageBoxResult result = MessageBox.Show(
                    this,
                    "MiSTerCast could not automatically check or launch Groovy_MiSTer. Open Configure Target?",
                    "Start Stream",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                return result == MessageBoxResult.Yes && await ConfigureTargetWithDialogAsync();
            }
        }

        private async Task<GroovyReleaseInfo> TryGetLatestGroovyReleaseAsync()
        {
            try
            {
                Log("Checking latest Groovy_MiSTer release...");
                return await new GroovyReleaseDownloader().GetLatestReleaseAsync();
            }
            catch (Exception exception)
            {
                Log("Could not check latest Groovy_MiSTer release: " + exception.Message, true);
                return null;
            }
        }

        private async Task<bool> PromptInstallLatestAndStartAsync(GroovyTargetDeploymentConfig config, GroovyReleaseInfo latestRelease)
        {
            if (latestRelease == null)
                return await PromptConfigureTargetAsync("Groovy_MiSTer is not installed and MiSTerCast could not check GitHub for the latest release. Open Configure Target?");

            MessageBoxResult result = MessageBox.Show(
                this,
                "Groovy_MiSTer is not installed on this target. Install the latest release and start streaming?",
                "Start Stream",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return false;

            return await DeployLatestAndWaitAsync(config, forceRedeploy: false);
        }

        private async Task<bool> PromptUpdateLatestOrLaunchExistingAsync(GroovyTargetDeploymentConfig config, GroovyReleaseInfo latestRelease, GroovyTargetManifest manifest)
        {
            string installed = manifest == null || String.IsNullOrWhiteSpace(manifest.TagName) ? "unknown" : manifest.TagName;
            MessageBoxResult result = MessageBox.Show(
                this,
                "Installed Groovy_MiSTer is " + installed + "; latest is " + latestRelease.TagName + "." + Environment.NewLine +
                "Yes = update and start, No = launch installed version, Cancel = stop.",
                "Start Stream",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel)
                return false;
            if (result == MessageBoxResult.Yes)
                return await DeployLatestAndWaitAsync(config, forceRedeploy: true);

            return await LaunchExistingAndWaitAsync(config, new GroovyTargetDeployer());
        }

        private async Task<bool> PromptUnknownVersionUpdateOrLaunchAsync(GroovyTargetDeploymentConfig config, GroovyReleaseInfo latestRelease)
        {
            MessageBoxResult result = MessageBox.Show(
                this,
                "Groovy_MiSTer is installed, but MiSTerCast does not know its release version." + Environment.NewLine +
                "Yes = update to latest and start, No = launch installed version, Cancel = stop.",
                "Start Stream",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel)
                return false;
            if (result == MessageBoxResult.Yes)
                return await DeployLatestAndWaitAsync(config, forceRedeploy: true);

            return await LaunchExistingAndWaitAsync(config, new GroovyTargetDeployer());
        }

        private async Task<bool> PromptConfigureTargetAsync(string message)
        {
            MessageBoxResult result = MessageBox.Show(
                this,
                message,
                "Start Stream",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            return result == MessageBoxResult.Yes && await ConfigureTargetWithDialogAsync();
        }

        private async Task<bool> DeployLatestAndWaitAsync(GroovyTargetDeploymentConfig config, bool forceRedeploy)
        {
            var downloader = new GroovyReleaseDownloader();
            GroovyReleaseInfo release = await downloader.DownloadLatestAsync(Log);
            config.MisterBinaryPath = release.MisterBinaryPath;
            config.GroovyRbfPath = release.GroovyRbfPath;
            config.ReleaseManifest = GroovyTargetManifest.FromRelease(release);
            config.ForceRedeploy = forceRedeploy;

            await new GroovyTargetDeployer().ConfigureAndLaunchAsync(config, Log);
            return await WaitForGroovyAfterLaunchAsync(config.Target);
        }

        private async Task<bool> LaunchExistingAndWaitAsync(GroovyTargetDeploymentConfig config, GroovyTargetDeployer deployer)
        {
            Log("Launching installed Groovy core...");
            await deployer.LaunchExistingAsync(config, Log);
            return await WaitForGroovyAfterLaunchAsync(config.Target);
        }

        private async Task<bool> WaitForGroovyAfterLaunchAsync(string target)
        {
            StreamStatusTextBlock.Text = "Status: Groovy core launched; waiting for UDP ACK...";
            var result = await GroovyMisterProbe.ProbeUntilAsync(
                target,
                GroovyMisterProbe.DefaultPostLaunchProbeTimeoutMilliseconds,
                GroovyMisterProbe.DefaultPostLaunchAttemptTimeoutMilliseconds,
                GroovyMisterProbe.DefaultPostLaunchRetryDelayMilliseconds,
                Log);

            if (result.Success)
            {
                StreamStatusTextBlock.Text = String.Format(
                    "Status: Groovy_MiSTer detected at {0}:{1}",
                    result.Address,
                    result.Port);
                Log(String.Format(
                    "Groovy_MiSTer detected at {0}:{1}. frame={2}, vcount={3}, status=0x{4:X2}",
                    result.Address,
                    result.Port,
                    result.Frame,
                    result.VCount,
                    result.StatusBits));
                return true;
            }

            StreamStatusTextBlock.Text = "Status: Groovy core launch probe failed - " + result.Message;
            Log("Groovy core launched, but UDP probe failed: " + result.Message, true);
            return false;
        }

        private bool TryResolveTargetIpAddress(string target, out IPAddress ipAddress)
        {
            ipAddress = null;
            if (IPAddress.TryParse(target, out ipAddress))
                return true;

            try
            {
                var hostEntry = Dns.GetHostEntry(target);
                if (hostEntry.AddressList == null || hostEntry.AddressList.Length == 0)
                {
                    Log("No IP addresses found for hostname: " + target, true);
                    return false;
                }

                ipAddress = hostEntry.AddressList.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ?? hostEntry.AddressList[0];
                return true;
            }
            catch (Exception exception)
            {
                Log("Resolving target IP address failed: " + exception.Message, true);
                return false;
            }
        }

        #region Settings

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.Filter = "Settings File|*.sav";
                saveFileDialog.Title = "Save MiSTerCast settings";
                if (currentSaveFilename != null)
                {
                    saveFileDialog.InitialDirectory = Path.GetDirectoryName(currentSaveFilename);
                    saveFileDialog.FileName = Path.GetFileName(currentSaveFilename);
                }
                
                if (saveFileDialog.ShowDialog().Value)
                {
                    if (!String.IsNullOrWhiteSpace(saveFileDialog.FileName))
                    {
                        using (System.IO.FileStream fs = (System.IO.FileStream)saveFileDialog.OpenFile())
                        {
                            using (var sw = new StreamWriter(fs))
                            {
                                new MiSTerCastSettings
                                {
                                    Target = TargetIpAddresTextBox.Text,
                                    ModelinePresetIndex = ModelinePresetsBox.SelectedIndex,
                                    PclockText = pclockTextBox.Text,
                                    HactiveText = hactiveTextBox.Text,
                                    HbeginText = hbeginTextBox.Text,
                                    HendText = hendTextBox.Text,
                                    HtotalText = htotalTextBox.Text,
                                    VactiveText = vactiveTextBox.Text,
                                    VbeginText = vbeginTextBox.Text,
                                    VendText = vendTextBox.Text,
                                    VtotalText = vtotalTextBox.Text,
                                    Interlaced = interlacedCheckBox.IsChecked.Value,
                                    CaptureSourceIndex = CaptureSourceBox.SelectedIndex,
                                    AlignmentIndex = AlignmentBox.SelectedIndex,
                                    RotationIndex = RotateComboBox.SelectedIndex,
                                    AudioEnabled = EnableAudioCheckBox.IsChecked.Value,
                                    PreviewEnabled = EnablePreviewCheckBox.IsChecked.Value,
                                    CropIndex = CropComboBox.SelectedIndex,
                                    CaptureWidthText = CaptureWidth.Text,
                                    CaptureHeightText = CaptureHeight.Text,
                                    CaptureXOffsetText = CaptureXOffset.Text,
                                    CaptureYOffsetText = CaptureYOffset.Text
                                }.Save(sw);

                                Log("Settings saved.");
                            }
                        }

                        UpdateLastSaveFile(saveFileDialog.FileName);
                    }
                }
            }
            catch (Exception exception)
            {
                Log("Save settings failed: " + exception.Message, true);
            }
        }

        private void LoadSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = "Settings File|*.sav";
                openFileDialog.Title = "Load MiSTerCast settings";
                if (currentSaveFilename != null)
                {
                    openFileDialog.InitialDirectory = Path.GetDirectoryName(currentSaveFilename);
                    openFileDialog.FileName = Path.GetFileName(currentSaveFilename);
                }

                if (openFileDialog.ShowDialog().Value)
                {
                    if (!String.IsNullOrWhiteSpace(openFileDialog.FileName))
                    {
                        using (System.IO.FileStream fs = (System.IO.FileStream)openFileDialog.OpenFile())
                        {
                            using (var sr = new StreamReader(fs))
                            {
                                LoadSaveFileFromStream(sr);
                                UpdateLastSaveFile(openFileDialog.FileName);
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Log("Load settings failed: " + exception.Message, true);
            }
        }

        private void LoadSaveFileFromStream(StreamReader sr)
        {
            try
            {
                MiSTerCastSettings settings;
                string error;
                if (!MiSTerCastSettings.TryLoad(sr, out settings, out error))
                {
                    Log(error, true);
                    return;
                }

                TargetIpAddresTextBox.Text = settings.Target;
                ModelinePresetsBox.SelectedIndex = ClampIndex(settings.ModelinePresetIndex, ModelinePresetsBox.Items.Count);
                pclockTextBox.Text = settings.PclockText;
                hactiveTextBox.Text = settings.HactiveText;
                hbeginTextBox.Text = settings.HbeginText;
                hendTextBox.Text = settings.HendText;
                htotalTextBox.Text = settings.HtotalText;
                vactiveTextBox.Text = settings.VactiveText;
                vbeginTextBox.Text = settings.VbeginText;
                vendTextBox.Text = settings.VendText;
                vtotalTextBox.Text = settings.VtotalText;
                interlacedCheckBox.IsChecked = settings.Interlaced;
                CaptureSourceBox.SelectedIndex = ClampIndex(settings.CaptureSourceIndex, CaptureSourceBox.Items.Count);
                AlignmentBox.SelectedIndex = ClampIndex(settings.AlignmentIndex, AlignmentBox.Items.Count);
                RotateComboBox.SelectedIndex = ClampIndex(settings.RotationIndex, RotateComboBox.Items.Count);
                EnableAudioCheckBox.IsChecked = settings.AudioEnabled;
                EnablePreviewCheckBox.IsChecked = settings.PreviewEnabled;
                CropComboBox.SelectedIndex = ClampIndex(settings.CropIndex, CropComboBox.Items.Count);
                CaptureWidth.Text = settings.CaptureWidthText;
                CaptureHeight.Text = settings.CaptureHeightText;
                CaptureXOffset.Text = settings.CaptureXOffsetText;
                CaptureYOffset.Text = settings.CaptureYOffsetText;

                Log("Settings loaded.");
            }
            catch (Exception e)
            {
                Log("Error parsing settings file: " + e.Message, true);
            }
        }

        private static int ClampIndex(int index, int itemCount)
        {
            if (itemCount <= 0)
                return -1;
            return Math.Max(0, Math.Min(index, itemCount - 1));
        }

        private void UpdateLastSaveFile(string fileName)
        {
            currentSaveFilename = fileName;
            try
            {
                using (FileStream fs = File.OpenWrite(lastSaveFilename))
                {
                    using (StreamWriter sw = new StreamWriter(fs))
                    {
                        sw.WriteLine(fileName);
                    }
                }
            }
            catch (Exception exception)
            {
                Log("Save last save file for autoload failed: " + exception.Message, true);
            }
        }

        #endregion Settings

        #region Number Entry Validation

        private void PositiveIntValidation(object sender, TextCompositionEventArgs e)
        {
            int result;
            e.Handled =
                !(int.TryParse(((TextBox)sender).Text + e.Text, out result) &&
                result >= 0);
        }

        private void IntValidation(object sender, TextCompositionEventArgs e)
        {
            int result;
            string fullText = ((TextBox)sender).Text.Insert(((TextBox)sender).CaretIndex, e.Text);
            e.Handled = !int.TryParse(fullText, out result) && fullText != "-";
        }

        private void PositiveDoubleValidation(object sender, TextCompositionEventArgs e)
        {
            double result;
            e.Handled =
                !(double.TryParse((((TextBox)sender).Text + e.Text).Replace(',','.'), NumberStyles.Any, CultureInfo.InvariantCulture, out result) &&
                result >= 0);
        }

        #endregion Number Entry Validation

        #region Logs

        private MiSTerCastInterop.LogDelegate LogDelegate;
        private const int MaxLogEntries = 1000;

        private void Log(string message, bool error = false)
        {
            this.Dispatcher.InvokeAsync(() =>
            {
                TextBlock logText = new TextBlock() { Text = message };
                if (error)
                    logText.Background = Brushes.Pink;
                LogPanel.Children.Add(logText);

                // Remove oldest entries when log exceeds maximum
                while (LogPanel.Children.Count > MaxLogEntries)
                    LogPanel.Children.RemoveAt(0);
            });
        }

        private void LogCallback(string message, bool error)
        {
            Log(message, error);
        }

        private bool autoScrollLogs = true;
        private void LogScrollView_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeightChange == 0)
            {
                if (LogScrollView.VerticalOffset == LogScrollView.ScrollableHeight)
                    autoScrollLogs = true;
                else
                    autoScrollLogs = false;
            }

            if (autoScrollLogs && e.ExtentHeightChange != 0)
                LogScrollView.ScrollToVerticalOffset(LogScrollView.ExtentHeight);
        }

        #endregion Logs

        #region Capture Source

        private SourceOptions currentSourceOptions;

        private void OnCaptureSourceChanged()
        {
            currentSourceOptions.display = (byte)CaptureSourceBox.SelectedIndex;
            currentSourceOptions.alignment = (byte)AlignmentBox.SelectedIndex;
            currentSourceOptions.rotation = (byte)RotateComboBox.SelectedIndex;
            currentSourceOptions.cropmode = (byte)CropComboBox.SelectedIndex;
            CaptureWidth.IsEnabled = CropComboBox.SelectedIndex == 0;
            CaptureHeight.IsEnabled = CropComboBox.SelectedIndex == 0;
            currentSourceOptions.audio = EnableAudioCheckBox.IsChecked.Value;
            currentSourceOptions.preview = EnablePreviewCheckBox.IsChecked.Value;
            ushort.TryParse(CaptureWidth.Text, out currentSourceOptions.width);
            ushort.TryParse(CaptureHeight.Text, out currentSourceOptions.height);
            short.TryParse(CaptureXOffset.Text, out currentSourceOptions.xoffset);
            short.TryParse(CaptureYOffset.Text, out currentSourceOptions.yoffset);

            PreviewImage.Visibility = currentSourceOptions.preview ? Visibility.Visible : Visibility.Hidden;
            PreviewDisabledLabel.Visibility = currentSourceOptions.preview ? Visibility.Hidden : Visibility.Visible;

            if (currentSourceOptions.width > 0 && currentSourceOptions.height > 0)
            {
                MiSTerCastInterop.SetSource(
                    currentSourceOptions.display,
                    currentSourceOptions.audio,
                    currentSourceOptions.preview,
                    currentSourceOptions.alignment,
                    currentSourceOptions.cropmode,
                    currentSourceOptions.width,
                    currentSourceOptions.height,
                    currentSourceOptions.xoffset,
                    currentSourceOptions.yoffset,
                    currentSourceOptions.rotation);
            }
        }

        private void CaptureSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isInitialized)
            {
                if (sender == CropComboBox)
                {
                    currentSourceOptions.cropmode = (byte)CropComboBox.SelectedIndex;
                    UpdateCropSize();
                }

                OnCaptureSourceChanged();
            }
        }

        private void CaptureSource_Checked(object sender, RoutedEventArgs e)
        {
            if (isInitialized)
                OnCaptureSourceChanged();
        }

        private void CaptureSource_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isInitialized)
            {
                OnCaptureSourceChanged();
            }
        }

        private void UpdateCropSize()
        {
            CaptureWidth.TextChanged -= CaptureSource_TextChanged;
            CaptureHeight.TextChanged -= CaptureSource_TextChanged;
            switch (currentSourceOptions.cropmode)
            {
                case 1:
                    CaptureWidth.Text = (currentModeLine.hactive).ToString();
                    CaptureHeight.Text = (currentModeLine.vactive).ToString();
                    break;
                case 2:
                    CaptureWidth.Text = (currentModeLine.hactive * 2).ToString();
                    CaptureHeight.Text = (currentModeLine.vactive * 2).ToString();
                    break;
                case 3:
                    CaptureWidth.Text = (currentModeLine.hactive * 3).ToString();
                    CaptureHeight.Text = (currentModeLine.vactive * 3).ToString();
                    break;
                case 4:
                    CaptureWidth.Text = (currentModeLine.hactive * 4).ToString();
                    CaptureHeight.Text = (currentModeLine.vactive * 4).ToString();
                    break;
                case 5:
                    CaptureWidth.Text = (currentModeLine.hactive * 5).ToString();
                    CaptureHeight.Text = (currentModeLine.vactive * 5).ToString();
                    break;
                default:
                    break;
            }
            CaptureWidth.TextChanged += CaptureSource_TextChanged;
            CaptureHeight.TextChanged += CaptureSource_TextChanged;
        }

        private void CaptureQuickButton_Click(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null || button.Tag == null)
                return;

            CaptureQuickAction action;
            if (!Enum.TryParse(button.Tag.ToString(), out action))
                return;

            int cropIndex = CaptureQuickActions.GetCropModeIndex(action);
            if (cropIndex >= 0 && cropIndex < CropComboBox.Items.Count)
            {
                if (CropComboBox.SelectedIndex == cropIndex)
                {
                    currentSourceOptions.cropmode = (byte)cropIndex;
                    UpdateCropSize();
                    OnCaptureSourceChanged();
                }
                else
                {
                    CropComboBox.SelectedIndex = cropIndex;
                }
            }
        }

        private void ResetCaptureOffsetButton_Click(object sender, RoutedEventArgs e)
        {
            CaptureXOffset.Text = "0";
            CaptureYOffset.Text = "0";
            if (isInitialized)
                OnCaptureSourceChanged();
        }

        #endregion Capture Source

        #region Modelines

        private Modeline currentModeLine;
        private List<Modeline> modelines;

        private bool OnModelineChanged()
        {
            double.TryParse(pclockTextBox.Text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out currentModeLine.pclock);
            ushort.TryParse(hactiveTextBox.Text, out currentModeLine.hactive);
            ushort.TryParse(hbeginTextBox.Text, out currentModeLine.hbegin);
            ushort.TryParse(hendTextBox.Text, out currentModeLine.hend);
            ushort.TryParse(htotalTextBox.Text, out currentModeLine.htotal);
            ushort.TryParse(vactiveTextBox.Text,  out currentModeLine.vactive);
            ushort.TryParse(vbeginTextBox.Text, out currentModeLine.vbegin);
            ushort.TryParse(vendTextBox.Text, out currentModeLine.vend);
            ushort.TryParse(vtotalTextBox.Text, out currentModeLine.vtotal);
            currentModeLine.interlace = interlacedCheckBox.IsChecked.Value;

            string error;
            if (!ModelineValidator.TryValidate(currentModeLine, out error))
            {
                if (isInitialized)
                    Log("Invalid modeline: " + error, true);
                return false;
            }

            if (isInitialized)
            {
                MiSTerCastInterop.SetModeline(
                    currentModeLine.pclock,
                    currentModeLine.hactive,
                    currentModeLine.hbegin,
                    currentModeLine.hend,
                    currentModeLine.htotal,
                    currentModeLine.vactive,
                    currentModeLine.vbegin,
                    currentModeLine.vend,
                    currentModeLine.vtotal,
                    currentModeLine.interlace);

                UpdateCropSize();
                OnCaptureSourceChanged();
            }

            return true;
        }

        private void ApplyModelineButton_Click(object sender, RoutedEventArgs e)
        {
            if (OnModelineChanged())
                ApplyModelineButton.IsEnabled = false;
        }

        private void AutofillModelineButton_Click(object sender, RoutedEventArgs e)
        {
            ushort hactive;
            ushort vactive;
            if (!ushort.TryParse(hactiveTextBox.Text, out hactive) || !ushort.TryParse(vactiveTextBox.Text, out vactive))
            {
                Log("Auto fill failed: active width and height must be valid positive integers.", true);
                return;
            }

            Modeline template = GetAutofillTemplate(interlacedCheckBox.IsChecked.Value, vactive);
            Modeline modeline;
            string error;
            if (!ModelineAutofill.TryBuild(template, hactive, vactive, interlacedCheckBox.IsChecked.Value, out modeline, out error))
            {
                Log("Auto fill failed: " + error, true);
                return;
            }

            SetModelineUI(modeline);
            ModelinePresetsBox.SelectedIndex = 0;
            if (OnModelineChanged())
                ApplyModelineButton.IsEnabled = false;
        }

        private Modeline GetAutofillTemplate(bool interlace, ushort vactive)
        {
            if (ModelinePresetsBox.SelectedIndex > 0 && modelines != null && ModelinePresetsBox.SelectedIndex <= modelines.Count)
                return modelines[ModelinePresetsBox.SelectedIndex - 1];

            Modeline fallback = modelines[0];
            int bestScore = int.MaxValue;
            foreach (Modeline modeline in modelines)
            {
                int score = Math.Abs(modeline.vactive - vactive);
                if (modeline.interlace != interlace)
                    score += 10000;
                if (score < bestScore)
                {
                    bestScore = score;
                    fallback = modeline;
                }
            }

            return fallback;
        }

        private void SetModelineUI(Modeline modeline)
        {
            ignoreModelineChange = true;
            pclockTextBox.Text = modeline.pclock.ToString();
            hactiveTextBox.Text = modeline.hactive.ToString();
            hbeginTextBox.Text = modeline.hbegin.ToString();
            hendTextBox.Text = modeline.hend.ToString();
            htotalTextBox.Text = modeline.htotal.ToString();
            vactiveTextBox.Text = modeline.vactive.ToString();
            vbeginTextBox.Text = modeline.vbegin.ToString();
            vendTextBox.Text = modeline.vend.ToString();
            vtotalTextBox.Text = modeline.vtotal.ToString();
            interlacedCheckBox.IsChecked = modeline.interlace;
            ignoreModelineChange = false;
        }

        private void ModelinePresetsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

            if (ModelinePresetsBox.SelectedIndex > 0 && modelines != null && ModelinePresetsBox.SelectedIndex <= modelines.Count)
            {
                SetModelineUI(modelines[ModelinePresetsBox.SelectedIndex - 1]);
                OnModelineChanged();
            }
        }

        void ReadModelinesFile()
        {
            List<Modeline> newModeLines = new List<Modeline>();
            try
            {
                List<string> lines = new List<string>(File.ReadAllLines("modelines.dat"));

                for (int i = 0; i < lines.Count; i++)
                {
                    Modeline modeline = new Modeline();
                    bool badLine = false;
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == ';')
                    {
                        badLine = true;
                    }
                    else
                    {
                        int nameStart = line.IndexOf('[');
                        int nameEnd = line.IndexOf(']');
                        if (nameStart == -1 || nameEnd == -1 || nameEnd <= nameStart + 1)
                        {
                            Log("Invalid modeline name format: " + lines[i], true);
                            badLine = true;
                        }
                        else
                        {
                            modeline.name = line.Substring(nameStart + 1, nameEnd - nameStart - 1);
                            if (String.IsNullOrEmpty(modeline.name))
                            {
                                Log("Invalid modeline name format: " + lines[i], true);
                                badLine = true;
                            }
                            else
                            {
                                string[] values = line.Remove(nameStart, nameEnd - nameStart + 1)
                                    .Split()
                                    .Select(p => p.Trim())
                                    .Where(p => !string.IsNullOrWhiteSpace(p))
                                    .ToArray();
                                if (values.Length != 10)
                                {
                                    Log("Invalid modeline values count: " + lines[i], true);
                                    badLine = true;
                                }
                                else
                                {
                                    UInt16 interlace;
                                    if (!Double.TryParse(values[0].Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture,out modeline.pclock) ||
                                        !UInt16.TryParse(values[1], out modeline.hactive) ||
                                        !UInt16.TryParse(values[2], out modeline.hbegin) ||
                                        !UInt16.TryParse(values[3], out modeline.hend) ||
                                        !UInt16.TryParse(values[4], out modeline.htotal) ||
                                        !UInt16.TryParse(values[5], out modeline.vactive) ||
                                        !UInt16.TryParse(values[6], out modeline.vbegin) ||
                                        !UInt16.TryParse(values[7], out modeline.vend) ||
                                        !UInt16.TryParse(values[8], out modeline.vtotal) ||
                                        !UInt16.TryParse(values[9], out interlace))
                
                                    {
                                        Log("Invalid modeline values format: " + lines[i], true);
                                        badLine = true;
                                    }
                                    else
                                    {
                                        modeline.interlace = interlace != 0;
                                        string validationError;
                                        if (!ModelineValidator.TryValidate(modeline, out validationError))
                                        {
                                            Log("Invalid modeline values: " + lines[i] + " (" + validationError + ")", true);
                                            badLine = true;
                                        }
                                        else
                                        {
                                            newModeLines.Add(modeline);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (badLine)
                    {
                        lines.RemoveAt(i);
                        i--;
                    }
                }

                if (newModeLines.Count == 0)
                    throw new Exception("No valid modelines.");

                modelines = newModeLines;
            }
            catch (Exception e)
            {
                Log("Failed to read modelines.dat. " + e.Message, true);
            }
        }

        void PopulateModelineDropdown()
        {
            ModelinePresetsBox.Items.Clear();
            ModelinePresetsBox.Items.Add("Custom");
            foreach (Modeline modeline in modelines)
            {
                ModelinePresetsBox.Items.Add(modeline.name);
            }

            ModelinePresetsBox.SelectedIndex = modelines.Count > 0 ? 1 : 0;
        }

        bool ignoreModelineChange = false;
        private void ModeLineTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            OnManualModelineChange();
        }

        private void InterlacedCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            OnManualModelineChange();
        }

        private void OnManualModelineChange()
        {
            if (!ignoreModelineChange)
            {
                ModelinePresetsBox.SelectedIndex = 0;
                if (isStreaming)
                    ApplyModelineButton.IsEnabled = true;
            }
        }

        #endregion Modelines

        #region Preview

        private MiSTerCastInterop.CaptureImageDelegate CaptureImageDelegate;
        private bool isPreviewEnabled = true;
        
        public void CaptureImage(int width, int height, IntPtr buffer)
        {
            if (isPreviewEnabled)
            {
                BitmapSource source = CreateBitmapSource(width, height, buffer);
                source.Freeze();
                this.Dispatcher.InvokeAsync(() =>
                {
                    PreviewImage.Source = source;
                });
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "CopyMemory", SetLastError = false)]
        public static extern void CopyMemory(IntPtr dest, IntPtr src, uint count);

        public BitmapSource CreateBitmapSource(int width, int height, IntPtr buffer)
        {
            WriteableBitmap writableImg = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

            writableImg.Lock();
            CopyMemory(writableImg.BackBuffer, buffer, (uint)(4 * width * height));
            writableImg.AddDirtyRect(new Int32Rect(0, 0, width, height));
            writableImg.Unlock();

            return writableImg;
        }

        #endregion Preview
    }
}
