﻿using CliWrap;
using CliWrap.Models;
using MCT_Windows.Windows;

using Microsoft.Win32;
using MifareWindowsTool.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace MCT_Windows
{
    /// <summary>
    /// Logique d'interaction pour MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public readonly string ACSDriversPage = "https://www.acs.com.hk/en/driver/3/acr122u-usb-nfc-reader/";
        private readonly string Github_MWT_WikiPage = "https://github.com/xavave/Mifare-Windows-Tool/wiki";
        public bool TagFound = false;
        public bool DumpFound = false;
        public bool ScanTagRunning = false;
        string version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        public string MainTitle { get; set; } = $"Mifare Windows Tool";
        Tools t = null;
        OpenFileDialog ofd = new OpenFileDialog();
        public List<File> SelectedKeys = new List<File>();
        Uri BaseUri = null;
        int cptFail = 0;
        protected internal string StdOutNfclist = "";
        string StdOutMfoc = "";
        IObservable<long> ObservableScan = null;
        CancellationTokenSource ScanCTS = null;
        CancellationTokenSource ProcessCTS = null;
        public MainWindow()
        {
            InitializeComponent();

            Uri iconUri = new Uri("pack://application:,,,/Resources/MWT.ico", UriKind.RelativeOrAbsolute);
            BaseUri = BaseUriHelper.GetBaseUri(this);
            this.Icon = BitmapFrame.Create(iconUri);
            MainTitle += $" v{version}";
            this.Title = $"{MainTitle}";
            ofd.Filter = Translate.Key(nameof(MifareWindowsTool.Properties.Resources.DumpFileFilter));

            t = new Tools(this);
            var newVersion = t.CheckNewVersion();
            if (newVersion != null)
            {
                var comp = Assembly.GetExecutingAssembly().GetName().Version.CompareTo(newVersion);
                if (comp < 0)
                {
                    var ret = MessageBox.Show("A newer version exists. Do you want to go on github page ?", "Version", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (ret == MessageBoxResult.Yes)
                    {
                        Process.Start("https://github.com/xavave/Mifare-Windows-Tool/releases/latest");
                    }
                    this.Title += " (Old version)";
                }
                else
                    this.Title += " (Latest version)";

            }

            ofd.InitialDirectory = Path.Combine(t.DefaultWorkingDir, "dumps");

            if (!t.TestWritePermission(ofd.InitialDirectory))
            {
                MessageBox.Show(Translate.Key(nameof(MifareWindowsTool.Properties.Resources.PleaseRestartAsAdmin)));
                Application.Current.Shutdown();
            }

            if (CheckSetDriver())
                PeriodicScanTag();

        }

        private bool CheckSetDriver()
        {
            try
            {
                var acrState = t.DriverState("ACR122U");

                if (acrState != "")
                {
                    var libusbkState = t.DriverState("LibUsbk");
                    if (acrState != "running" && libusbkState == "stopped")
                    {
                        MessageBox.Show("Le Lecteur de badges ACR122U ne semble pas branché !", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    if (libusbkState != "running")
                    {
                        var dr = MessageBox.Show(Translate.Key(nameof(MifareWindowsTool.Properties.Resources.DriverLibUsbKNonInstalled)), "LibUsbK", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                        if (dr == MessageBoxResult.OK)
                        {
                            return t.InstallLibUsbKDriver();
                        }
                    }
                    else
                        return true;
                }
                else
                {

                    var dr = MessageBox.Show(Translate.Key(nameof(MifareWindowsTool.Properties.Resources.DriverACR122NotInstalled)), "ACR122U", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                    if (dr == MessageBoxResult.OK)
                    {
                        Process.Start(ACSDriversPage);

                    }

                }
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }



        public void PeriodicScanTag()
        {
            if (ScanTagRunning) return;

            ObservableScan = Observable.Interval(TimeSpan.FromSeconds(3));
            // Token for cancelation
            ScanCTS = new CancellationTokenSource();
            // Subscribe the observable to the task on execution.
            ObservableScan.Subscribe(x =>
            {
                Application.Current.Dispatcher.Invoke(async () =>
                {
                    if (!ckEnablePeriodicTagScan.IsChecked.HasValue || ckEnablePeriodicTagScan.IsChecked.Value == false) return;
                    rtbOutput.Text += $"{DateTime.Now} -  {Translate.Key(nameof(MifareWindowsTool.Properties.Resources.AutoScanTagRunning))}...\n";
                    await RunNfcListAsync();
                });
            }, ScanCTS.Token);
            ScanTagRunning = true;
        }

        private async void btnReadTag_Click(object sender, RoutedEventArgs e)
        {
            await ReadTagAsync(TagAction.ReadSource);

        }
        private async void btnWriteTag_Click(object sender, RoutedEventArgs e)
        {
            await ReadTagAsync(TagAction.ReadTarget);
        }

        private async Task ReadTagAsync(TagAction act)
        {
            if (t.running) return;
            DumpFound = false;
            if (!TagFound && !ScanTagRunning)
                PeriodicScanTag();

            if (!ckEnablePeriodicTagScan.IsChecked.HasValue || !ckEnablePeriodicTagScan.IsChecked.Value)
            {
                await RunNfcListAsync();
            }

            if (!string.IsNullOrWhiteSpace(t.CurrentUID))
            {
                if (act == TagAction.ReadSource)
                {
                    t.mySourceUID = t.CurrentUID;
                    t.TMPFILE_UNK = $"mfc_{ t.mySourceUID}_unknownMfocSectorInfo.txt";
                    t.TMPFILESOURCE_MFD = $"mfc_{ t.mySourceUID}.dump";
                    t.TMPFILE_FND = $"mfc_{ t.mySourceUID}_foundKeys.txt";
                    if (t.CheckAndUseDumpIfExists(t.TMPFILESOURCE_MFD))
                    {
                        DumpFound = true;

                    }
                }
                else if (act == TagAction.ReadTarget)
                {
                    t.myTargetUID = t.CurrentUID;
                    t.TMPFILE_UNK = $"mfc_{ t.myTargetUID}_unknownMfocSectorInfo.txt";
                    t.TMPFILE_TARGETMFD = $"mfc_{ t.myTargetUID}.dump";
                    t.TMPFILE_FND = $"mfc_{ t.myTargetUID}_foundKeys.txt";
                    if (t.CheckAndUseDumpIfExists(t.TMPFILE_TARGETMFD))
                    {
                        DumpFound = true;
                    }

                }
            }

            if (TagFound)
            {
                StopScanTag();
                if (act == TagAction.ReadSource)
                {
                    if (!DumpFound)
                    {
                        MapKeyToSectorWindow mtsWin = new MapKeyToSectorWindow(this, t, Translate.Key(nameof(MifareWindowsTool.Properties.Resources.UsedForSourceMapping)), Translate.Key(nameof(MifareWindowsTool.Properties.Resources.Source)));
                        var ret = mtsWin.ShowDialog();
                        if (ret.HasValue && ret.Value)
                        {
                            await RunMfocAsync(SelectedKeys, t.TMPFILESOURCE_MFD, act);
                            if (StdOutMfoc.Contains($"dumping keys to a file"))
                            {
                                ShowDump("dumps\\" + t.TMPFILESOURCE_MFD);
                                StdOutMfoc = string.Empty;
                            }
                        }
                        else
                            PeriodicScanTag();
                    }
                    else
                    {
                        ShowDump("dumps\\" + t.TMPFILESOURCE_MFD);
                    }
                }
                else if (act == TagAction.ReadTarget)
                {
                    if (!DumpFound)
                    {
                        MapKeyToSectorWindow mtsWin = new MapKeyToSectorWindow(this, t, Translate.Key(nameof(MifareWindowsTool.Properties.Resources.UsedForTargetMapping)), Translate.Key(nameof(MifareWindowsTool.Properties.Resources.Target)));
                        var ret = mtsWin.ShowDialog();
                        if (ret.HasValue && ret.Value)
                        {
                            await RunMfocAsync(SelectedKeys, t.TMPFILE_TARGETMFD, act);
                            if (StdOutMfoc.Contains($"dumping"))
                            {
                                OpenWriteDumpWindow();
                                StdOutMfoc = string.Empty;
                            }
                        }
                        else
                            PeriodicScanTag();

                    }
                    else
                    {
                        OpenWriteDumpWindow();
                    }

                }

            }
            else
            {
                LogAppend(Translate.Key(nameof(MifareWindowsTool.Properties.Resources.NoTagDetectedOnReader)));
            }

        }

        private void OpenWriteDumpWindow()
        {
            WriteDumpWindow wdw = new WriteDumpWindow(this, t);
            var ret = wdw.ShowDialog();
            if (!ret.HasValue || !ret.Value)
                PeriodicScanTag();
        }

        public void ValidateActions(bool enable)
        {

            btnReadTag.IsEnabled = enable;
            btnWriteTag.IsEnabled = enable;
            ckEnablePeriodicTagScan.IsEnabled = enable;

        }

        public void ShowAbortButton(bool show = true)
        {
            if (show)
            {
                btnAbortCurrentTask.Content = $"{Translate.Key(nameof(MifareWindowsTool.Properties.Resources.Abort))}";
                btnAbortCurrentTask.Visibility = Visibility.Visible;
            }
            else
                btnAbortCurrentTask.Visibility = Visibility.Hidden;

        }


        public void ShowDump(string fileName)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                DumpWindow dw = new DumpWindow(t, fileName);
                var dr = dw.ShowDialog();
                if (dr.HasValue && dr.Value)
                    PeriodicScanTag();
            });

        }

        public static void DoEvents()
        {
            Application.Current.Dispatcher.Invoke(DispatcherPriority.Background,
                                                  new Action(delegate { }));
        }

        public async Task<bool> RunPcscScanACR122()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(TimeSpan.FromSeconds(10)); // e.g. timeout of 5 seconds

                var result = await Cli.Wrap("nfctools\\pcsc_scan.exe")
                    .SetCancellationToken(cts.Token)
                    .SetStandardOutputCallback(l => LogAppend(l))
                    .SetStandardErrorCallback(l => LogAppend(l))
                    .ExecuteAsync();
                return result.StandardOutput.Contains("ACR122");
            }

        }

        public async Task<string> RunNfcListAsync()
        {

            try
            {
                var result = await Cli.Wrap(@"nfctools\\nfc-list.exe")
                                          .SetStandardOutputCallback(l => LogAppend(l))
                                          .SetStandardErrorCallback(l => LogAppend(l))
                                          .EnableExitCodeValidation(false)
                                          .ExecuteAsync();
                if (result.ExitCode != 0)
                {
                    if (cptFail < 1)
                    {
                        var dr = MessageBox.Show(Translate.Key(nameof(MifareWindowsTool.Properties.Resources.DriverLibUsbKNonInstalled)), "LibUsbK", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                        if (dr == MessageBoxResult.OK)
                        {
                            if (t.InstallLibUsbKDriver())
                                cptFail++;
                        }
                    }
                }

                StdOutNfclist = result.StandardOutput;

                if (StdOutNfclist.Contains("No NFC device found."))
                {
                    ScanTagRunning = false;
                    ScanCTS.Cancel();

                }

                return SetCurrentUID(result);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                return "";
            }
        }

        private string SetCurrentUID(ExecutionResult result)
        {
            var newUID = "";
            var exitCode = result.ExitCode;

            var stdErr = result.StandardError;
            var startTime = result.StartTime;
            var exitTime = result.ExitTime;
            var runTime = result.RunTime;



            var retUID = StdOutNfclist.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(t => t.Contains("UID")).LastOrDefault();
            if (retUID != null && retUID.Contains(": "))
            {
                newUID = retUID.Substring(retUID.IndexOf(": ") + ": ".Length).Replace(" ", "").ToUpper();
                if (t.CurrentUID != newUID)
                {
                    t.CurrentUID = newUID;
                    this.Title = $"{MainTitle}: { Translate.Key(nameof(MifareWindowsTool.Properties.Resources.NewUIDFound))}: { t.CurrentUID}";

                    t.PlayBeep(BaseUri);

                    TagFound = true;
                }
            }

            else
            {
                t.CurrentUID = "";
                TagFound = false;
                this.Title = $"{MainTitle}: {Translate.Key(nameof(MifareWindowsTool.Properties.Resources.NoTag))}";
            }
            StdOutNfclist = "";
            return newUID;
        }

        public void StopScanTag()
        {
            if (ScanTagRunning)
            {

                ScanTagRunning = false;
                ScanCTS.Cancel();
                if (ckEnablePeriodicTagScan.IsChecked.Value)
                    LogAppend(Translate.Key(nameof(MifareWindowsTool.Properties.Resources.AutoScanTagStopped)));
            }
        }
        public async Task RunMifareClassicFormatAsync()
        {
            StopScanTag();
            ValidateActions(false);
            ShowAbortButton();
            try
            {
                string args = "";
                if (System.IO.File.Exists("dumps\\" + t.TMPFILE_TARGETMFD))
                    args += $" \"{"dumps\\" + t.TMPFILE_TARGETMFD}\"";
                ProcessCTS = new CancellationTokenSource();
                var arguments = $"-y {args}";
                LogAppend($"nfc-mfclassic {arguments}");
                var result = await Cli.Wrap("nfctools\\mifare-classic-format.exe").SetArguments(arguments)
      .SetStandardOutputCallback(l => LogAppend(l))
      .SetStandardErrorCallback(l => LogAppend(l))
      .SetCancellationToken(ProcessCTS.Token)
      .ExecuteAsync();

                var exitCode = result.ExitCode;
                var stdOut = result.StandardOutput;
                var stdErr = result.StandardError;
                var startTime = result.StartTime;
                var exitTime = result.ExitTime;
                var runTime = result.RunTime;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                ProcessCTS.Dispose();
                ValidateActions(true);
                ShowAbortButton(false);
            }



        }
        public async Task RunNfcMfclassicAsync(TagAction act, bool bWriteBlock0, bool useKeyA, bool haltOnError, TagType tagType)
        {
            try
            {
                StopScanTag();
                ValidateActions(false);
                ShowAbortButton();
                var sourceDump = t.TMPFILESOURCEPATH_MFD; //"dumps\\" + t.TMPFILESOURCE_MFD;
                var targetDump = "dumps\\" + t.TMPFILE_TARGETMFD;
                char writeMode = bWriteBlock0 == true ? 'W' : 'w';
                char useKey = useKeyA == true ? 'A' : 'B';
                char cHaltOnError = haltOnError == true ? useKey = char.ToLower(useKey) : char.ToUpper(useKey);
                if (tagType == TagType.UnlockedGen1) writeMode = 'W'; else if (tagType == TagType.DirectCUIDgen2) writeMode = 'C';
                ProcessCTS = new CancellationTokenSource();
                var arguments = $"{writeMode} {cHaltOnError} u \"{sourceDump}\" \"{targetDump}\"";
                LogAppend($"nfc-mfclassic {arguments}");
                var result = await Cli.Wrap("nfctools\\nfc-mfclassic.exe").SetArguments(arguments)
       .SetStandardOutputCallback(l => LogAppend(l))
       .SetStandardErrorCallback(l => LogAppend(l))
       .SetCancellationToken(ProcessCTS.Token)
       .ExecuteAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {

                throw;
            }
            finally
            {
                ProcessCTS.Dispose();
                ValidateActions(true);
                ShowAbortButton(false);
            }


        }
        public async Task RunMfocAsync(List<File> keys, string tmpFileMfd, TagAction act)
        {
            try
            {
                StopScanTag();
                ValidateActions(false);
                ShowAbortButton();
                string arguments = "";
                tmpFileMfd = "dumps\\" + tmpFileMfd;

                var tmpFileUnk = "dumps\\" + t.TMPFILE_UNK;

                if (System.IO.File.Exists(tmpFileUnk))
                    arguments += $" -D \"{tmpFileUnk}\"";

                foreach (var key in keys.Select(k => k.FileName))
                {
                    arguments += $" -f \"keys\\{key}\"";

                }
                arguments += $" -O\"{tmpFileMfd}\"";
                ProcessCTS = new CancellationTokenSource();
                LogAppend($"mfoc {arguments}");
                var result = await Cli.Wrap("nfctools\\mfoc.exe").SetArguments(arguments).SetWorkingDirectory(t.DefaultWorkingDir)
    .SetStandardOutputCallback(l => LogAppend(l))
    .SetStandardErrorCallback(l => LogAppend(l))
    .SetCancellationToken(ProcessCTS.Token)
    .ExecuteAsync();

                StdOutMfoc = result.StandardOutput;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                PeriodicScanTag();
            }
            finally
            {
                ValidateActions(true);
                ShowAbortButton(false);
                ProcessCTS.Dispose();
            }

        }

        public void LogAppend(string msg)
        {
            Dispatcher.Invoke(new Action(() =>
            {
                rtbOutput.AppendText(msg + "\n");
            }));
        }

        private void btnEditAddKeyFile_Click(object sender, RoutedEventArgs e)
        {
            StopScanTag();
            SelectKeyFilesWindow ekf = new SelectKeyFilesWindow(this, t);
            ekf.ShowDialog();
            PeriodicScanTag();
        }

        private void rtbOutput_TextChanged(object sender, TextChangedEventArgs e)
        {
            (sender as TextBox).ScrollToEnd();
        }


        private void btnEditDumpFile_Click(object sender, RoutedEventArgs e)
        {
            StopScanTag();
            var dr = ofd.ShowDialog();
            if (dr.Value)
            {
                ShowDump(ofd.FileName);
            }
            else
                PeriodicScanTag();

        }

        private void btnInfos_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(Github_MWT_WikiPage);
        }

        private void btnTools_Click(object sender, RoutedEventArgs e)
        {
            var stw = new SelectToolWindow(this, t);
            stw.Show();
        }


        private void btnAbortCurrentTask_Click(object sender, RoutedEventArgs e)
        {
            ProcessCTS.Cancel();
            ShowAbortButton(false);
            ValidateActions(true);
        }
        private void ckEnablePeriodicTagScan_Checked(object sender, RoutedEventArgs e)
        {


            if (ckEnablePeriodicTagScan.IsChecked.HasValue && ckEnablePeriodicTagScan.IsChecked.Value)
                PeriodicScanTag();
        }

    }
}
