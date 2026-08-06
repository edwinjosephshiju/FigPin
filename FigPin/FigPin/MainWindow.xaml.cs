using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FigPin
{
    public sealed partial class MainWindow : Window
    {
        private static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        private const string BackendBaseUrl = "http://127.0.0.1:8000";
        private string? _currentJobDir;
        private string? _currentInputFilePath;
        private AppWindow? _appWindow;

        private int _previousPotencyIndex = 0;
        private bool _isInitializingPotency = true;
        private Process? _backendProcess;

        private ContentDialog? _installDialog;
        private ProgressBar? _installProgressBar;
        private TextBlock? _installStatusText;
        private bool _titleBarConfigured = false;

        private readonly StringBuilder _launcherLogBuffer = new StringBuilder();
        private readonly StringBuilder _backendLogBuffer = new StringBuilder();
        private readonly object _launcherLogLock = new object();
        private readonly object _backendLogLock = new object();
        private const int MaxLogLength = 50000;

        public MainWindow()
        {
            try
            {
                this.InitializeComponent();

                this.Activated += MainWindow_Activated;

                // Set click-to-open on input preview image
                InputPreviewImage.PointerPressed += InputPreviewImage_PointerPressed;
                ToolTipService.SetToolTip(InputPreviewImage, "Click to open in Windows Photos viewer");

                _isInitializingPotency = false;

                // Start Native C# Dependency Manager & Backend Server
                _ = InitializeDependencyEnvironmentAsync();
            }
            catch (Exception ex)
            {
                LogCrash("MainWindow Constructor", ex);
            }
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (!_titleBarConfigured)
            {
                _titleBarConfigured = true;
                ConfigureCustomTitleBar();
            }
        }

        private static void LogCrash(string context, Exception ex)
        {
            try
            {
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".figpin");
                Directory.CreateDirectory(logDir);
                string crashFile = Path.Combine(logDir, "crash.log");
                File.AppendAllText(crashFile, $"[{DateTime.Now}] CRASH IN {context}:\n{ex.Message}\n{ex.StackTrace}\n\n");
            }
            catch { }
        }

        private void ConfigureCustomTitleBar()
        {
            try
            {
                IntPtr hwnd = WindowNative.GetWindowHandle(this);
                if (hwnd == IntPtr.Zero) return;

                WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
                _appWindow = AppWindow.GetFromWindowId(windowId);

                try
                {
                    if (MicaController.IsSupported())
                    {
                        this.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
                    }
                }
                catch { }

                if (AppWindowTitleBar.IsCustomizationSupported())
                {
                    this.ExtendsContentIntoTitleBar = true;
                    this.SetTitleBar(AppTitleBar);

                    var titleBar = _appWindow.TitleBar;
                    titleBar.BackgroundColor = Colors.Transparent;
                    titleBar.ButtonBackgroundColor = Colors.Transparent;
                    titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                    titleBar.ButtonHoverBackgroundColor = Microsoft.UI.Colors.DarkGray;
                    titleBar.ButtonPressedBackgroundColor = Microsoft.UI.Colors.Gray;
                }

                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "FigPin.ico");
                if (!File.Exists(iconPath))
                {
                    iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FigPin.ico");
                }
                if (File.Exists(iconPath))
                {
                    _appWindow.SetIcon(iconPath);
                }
            }
            catch (Exception ex)
            {
                LogCrash("ConfigureCustomTitleBar", ex);
            }
        }

        #region Figma Styled Tab Switcher & Continuous Log Streaming

        private void TabBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int tabIndex))
            {
                SwitchToTab(tabIndex);
            }
        }

        private void SwitchToTab(int tabIndex)
        {
            var activeStyle = (Style)Application.Current.Resources["AccentButtonStyle"];
            var inactiveStyle = (Style)Application.Current.Resources["DefaultButtonStyle"];

            TabStudioBtn.Style = tabIndex == 0 ? activeStyle : inactiveStyle;
            TabLauncherBtn.Style = tabIndex == 1 ? activeStyle : inactiveStyle;
            TabBackendBtn.Style = tabIndex == 2 ? activeStyle : inactiveStyle;

            StudioViewGrid.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            LauncherTerminalGrid.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            BackendTerminalGrid.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AppendLauncherLog(string text)
        {
            lock (_launcherLogLock)
            {
                _launcherLogBuffer.AppendLine(text);
                if (_launcherLogBuffer.Length > MaxLogLength)
                {
                    _launcherLogBuffer.Remove(0, _launcherLogBuffer.Length - (MaxLogLength / 2));
                }
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                lock (_launcherLogLock)
                {
                    LauncherLogTextBox.Text = _launcherLogBuffer.ToString();
                }
                LauncherLogScrollViewer.ChangeView(null, LauncherLogScrollViewer.ScrollableHeight, null);
            });
        }

        private void AppendBackendLog(string text)
        {
            lock (_backendLogLock)
            {
                _backendLogBuffer.AppendLine(text);
                if (_backendLogBuffer.Length > MaxLogLength)
                {
                    _backendLogBuffer.Remove(0, _backendLogBuffer.Length - (MaxLogLength / 2));
                }
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                lock (_backendLogLock)
                {
                    BackendLogTextBox.Text = _backendLogBuffer.ToString();
                }
                BackendLogScrollViewer.ChangeView(null, BackendLogScrollViewer.ScrollableHeight, null);
            });
        }

        private void ClearLauncherLogs_Click(object sender, RoutedEventArgs e)
        {
            lock (_launcherLogLock)
            {
                _launcherLogBuffer.Clear();
            }
            LauncherLogTextBox.Text = string.Empty;
        }

        private void ClearBackendLogs_Click(object sender, RoutedEventArgs e)
        {
            lock (_backendLogLock)
            {
                _backendLogBuffer.Clear();
            }
            BackendLogTextBox.Text = string.Empty;
        }

        private async void RepairVenvBtn_Click(object sender, RoutedEventArgs e)
        {
            if (this.Content == null || this.Content.XamlRoot == null) return;

            var dialog = new ContentDialog
            {
                Title = "Repair Virtual Environment?",
                Content = "This will terminate the backend server, remove the existing Python virtual environment, and reinstall all AI dependencies fresh without using pip cache. Do you wish to proceed?",
                PrimaryButtonText = "Repair Venv",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            try
            {
                RepairVenvBtn.IsEnabled = false;
                SwitchToTab(1); // AI Terminal Logs
                AppendLauncherLog("======================================================================");
                AppendLauncherLog("            TRIGGERING MANUAL VENV REPAIR & FRESH DEPENDENCY INSTALL");
                AppendLauncherLog("======================================================================");

                KillBackendProcess();

                string rootDir = GetProjectRootDir();
                string backendDir = Path.Combine(rootDir, "backend");
                string venvPath = Path.Combine(backendDir, "FigPin");

                if (Directory.Exists(venvPath))
                {
                    AppendLauncherLog($"[REPAIR] Deleting old virtual environment at: {venvPath}");
                    try
                    {
                        Directory.Delete(venvPath, true);
                    }
                    catch (Exception ex)
                    {
                        AppendLauncherLog($"[WARNING] Error deleting venv directory: {ex.Message}");
                    }
                }

                await InstallDependenciesNativeAsync(rootDir, backendDir);

                // Start backend server again
                string venvPython = Path.Combine(backendDir, "FigPin", "Scripts", "python.exe");
                StartBackendServer(backendDir, venvPython);

                // Check server health
                await CheckBackendHealthAsync();
            }
            catch (Exception ex)
            {
                AppendLauncherLog($"[ERROR] Repair Venv failed: {ex.Message}");
                LogCrash("RepairVenvBtn_Click", ex);
            }
            finally
            {
                RepairVenvBtn.IsEnabled = true;
            }
        }

        private void KillBackendProcess()
        {
            try
            {
                if (_backendProcess != null && !_backendProcess.HasExited)
                {
                    _backendProcess.Kill(true);
                    _backendProcess.Dispose();
                    _backendProcess = null;
                    AppendBackendLog("[SERVER] Backend server process stopped.");
                }
            }
            catch (Exception ex)
            {
                AppendBackendLog($"[WARNING] Error stopping backend process: {ex.Message}");
            }
        }

        #endregion

        #region Native C# Dependency Manager & Backend Server

        private async Task InitializeDependencyEnvironmentAsync()
        {
            try
            {
                AppendLauncherLog("======================================================================");
                AppendLauncherLog("           FigPin Native C# Dependency & Environment Manager");
                AppendLauncherLog("======================================================================");

                string rootDir = GetProjectRootDir();
                string backendDir = Path.Combine(rootDir, "backend");
                Directory.CreateDirectory(backendDir);

                // Attempt to detect and reuse cached AI model weights from neighbour directories
                TryReuseNeighbourModels(backendDir);

                string venvPython = Path.Combine(backendDir, "FigPin", "Scripts", "python.exe");

                // Check if target Python virtual environment exists and is functional
                bool isVenvValid = await IsVenvValidAsync(venvPython);
                if (!isVenvValid)
                {
                    AppendLauncherLog("[INFO] Python virtual environment 'FigPin' missing or broken. Launching Dependency Installer...");
                    await InstallDependenciesNativeAsync(rootDir, backendDir);
                }
                else
                {
                    AppendLauncherLog("[OK] Virtual environment 'FigPin' detected & verified.");
                    
                    // Scan & verify models via download_models.py inside C# background process (stream to Tab 2)
                    AppendLauncherLog("[INFO] Verifying AI Model Weights (BiRefNet, SAM 2, YOLO, Grounding DINO)...");
                    string modelScript = Path.Combine(backendDir, "download_models.py");
                    await RunProcessAsync(venvPython, $"\"{modelScript}\"", backendDir, text => AppendLauncherLog(text));
                }

                // Start FastAPI Server in Background C# process (stream to Tab 3)
                StartBackendServer(backendDir, venvPython);

                // Check server health
                await CheckBackendHealthAsync();
            }
            catch (Exception ex)
            {
                LogCrash("InitializeDependencyEnvironmentAsync", ex);
            }
        }

        private List<string> GetCandidateNeighbourRoots()
        {
            var roots = new List<string>();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            roots.Add(baseDir);

            try
            {
                string packageLocalFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                string appDataRoot = Path.Combine(packageLocalFolder, "FigPinStudio");
                roots.Add(appDataRoot);
            }
            catch { }

            string localAppDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FigPinStudio");
            roots.Add(localAppDataRoot);

            try
            {
                string p1 = Path.GetFullPath(Path.Combine(baseDir, ".."));
                roots.Add(p1);
                string p2 = Path.GetFullPath(Path.Combine(baseDir, "..", ".."));
                roots.Add(p2);
                string p3 = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
                roots.Add(p3);
            }
            catch { }

            return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void TryReuseNeighbourModels(string targetBackendDir)
        {
            string targetModelsDir = Path.Combine(targetBackendDir, "models");
            Directory.CreateDirectory(targetModelsDir);

            List<string> candidateRoots = GetCandidateNeighbourRoots();
            foreach (var root in candidateRoots)
            {
                string candidateModels = Path.Combine(root, "backend", "models");
                if (!Directory.Exists(candidateModels)) continue;

                if (Path.GetFullPath(candidateModels).Equals(Path.GetFullPath(targetModelsDir), StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var subDirs = Directory.GetDirectories(candidateModels);
                    foreach (var subDir in subDirs)
                    {
                        string dirName = Path.GetFileName(subDir);
                        string targetSubDir = Path.Combine(targetModelsDir, dirName);
                        if (!Directory.Exists(targetSubDir) || Directory.GetFiles(targetSubDir, "*", SearchOption.AllDirectories).Length == 0)
                        {
                            AppendLauncherLog($"[REUSE] Reusing cached AI model weights '{dirName}' from {candidateModels}...");
                            CopyDirectory(subDir, targetSubDir);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppendLauncherLog($"[WARNING] Error copying candidate models from {candidateModels}: {ex.Message}");
                }
            }
        }

        private async Task<bool> IsVenvValidAsync(string venvPython)
        {
            if (!File.Exists(venvPython)) return false;

            bool isValid = false;
            try
            {
                int exitCode = await RunProcessAsync(venvPython, "--version", Path.GetDirectoryName(venvPython) ?? "", output =>
                {
                    if (!string.IsNullOrEmpty(output) && output.Contains("Python 3.12"))
                    {
                        isValid = true;
                    }
                });
                return exitCode == 0 && isValid;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> VerifyPythonExeAsync(string pythonPath, string args = "--version")
        {
            try
            {
                bool found = false;
                int exitCode = await RunProcessAsync(pythonPath, args, AppDomain.CurrentDomain.BaseDirectory, output =>
                {
                    if (!string.IsNullOrEmpty(output) && output.Contains("Python 3.12"))
                    {
                        found = true;
                    }
                });
                return exitCode == 0 && found;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string?> FindSystemPython312Async()
        {
            string localPy312 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python312", "python.exe");
            if (File.Exists(localPy312) && await VerifyPythonExeAsync(localPy312))
            {
                return localPy312;
            }

            string pfPy312 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Python312", "python.exe");
            if (File.Exists(pfPy312) && await VerifyPythonExeAsync(pfPy312))
            {
                return pfPy312;
            }

            if (File.Exists(@"C:\Python312\python.exe") && await VerifyPythonExeAsync(@"C:\Python312\python.exe"))
            {
                return @"C:\Python312\python.exe";
            }

            if (await VerifyPythonExeAsync("py", "-3.12 --version"))
            {
                return "py -3.12";
            }

            if (await VerifyPythonExeAsync("python"))
            {
                return "python";
            }

            return null;
        }

        private async Task<bool> InstallPythonViaWingetAsync()
        {
            AppendLauncherLog("[INSTALL] Winget: Installing Python 3.12.8 without modifying system PATH...");
            UpdateInstallProgress(20, "Step 1/5: Installing Python 3.12.8 via Winget (No PATH modification)...");

            string wingetArgs = "install --id Python.Python.3.12 --version 3.12.8 --exact --accept-package-agreements --accept-source-agreements --scope user --override \"/passive PrependPath=0\"";

            int exitCode = await RunProcessAsync("winget", wingetArgs, AppDomain.CurrentDomain.BaseDirectory, text => AppendLauncherLog(text));
            
            if (exitCode != 0)
            {
                AppendLauncherLog("[WARNING] Winget exact version install returned non-zero code. Trying default Python.3.12 installer...");
                string fallbackArgs = "install --id Python.Python.3.12 --accept-package-agreements --accept-source-agreements --scope user --override \"/passive PrependPath=0\"";
                exitCode = await RunProcessAsync("winget", fallbackArgs, AppDomain.CurrentDomain.BaseDirectory, text => AppendLauncherLog(text));
            }

            return exitCode == 0;
        }

        private string GetProjectRootDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            try
            {
                // Attempt to resolve Windows.Storage.ApplicationData.Current.LocalFolder (MSIX Packaged Mode)
                string packageLocalFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                string appDataRoot = Path.Combine(packageLocalFolder, "FigPinStudio");
                Directory.CreateDirectory(appDataRoot);

                string backendAppData = Path.Combine(appDataRoot, "backend");
                Directory.CreateDirectory(backendAppData);

                string backendPackage = Path.Combine(baseDir, "backend");
                if (Directory.Exists(backendPackage))
                {
                    CopyDirectory(backendPackage, backendAppData);
                }
                return appDataRoot;
            }
            catch
            {
                // Unpackaged development mode
                if (Directory.Exists(Path.Combine(baseDir, "backend")))
                {
                    return baseDir;
                }

                try
                {
                    string p1 = Path.GetFullPath(Path.Combine(baseDir, ".."));
                    if (Directory.Exists(Path.Combine(p1, "backend"))) return p1;

                    string p2 = Path.GetFullPath(Path.Combine(baseDir, "..", ".."));
                    if (Directory.Exists(Path.Combine(p2, "backend"))) return p2;

                    string p3 = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
                    if (Directory.Exists(Path.Combine(p3, "backend"))) return p3;
                }
                catch { }

                return baseDir;
            }
        }

        private static string GetOutputsRootDir()
        {
            string downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string outputDir = Path.Combine(downloadsDir, "FigPin outputs");
            Directory.CreateDirectory(outputDir);
            return outputDir;
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) return;

            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            foreach (DirectoryInfo subDir in dir.GetDirectories())
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir);
            }
        }

        private async Task InstallDependenciesNativeAsync(string rootDir, string backendDir)
        {
            try
            {
                Directory.CreateDirectory(backendDir);

                // Step 1: Check Python installation
                AppendLauncherLog("[INSTALL] Checking system Python 3.12...");
                
                string venvPath = Path.Combine(backendDir, "FigPin");

                bool installedViaWinget = false;
                string? systemPython = await FindSystemPython312Async();
                if (string.IsNullOrEmpty(systemPython))
                {
                    // Show popup with progress bar ONLY when installing Python via Winget
                    ShowInstallProgressDialog();
                    UpdateInstallProgress(20, "Installing Python 3.12.8 via Winget (No PATH modification)...");
                    AppendLauncherLog("[INSTALL] Python 3.12 not found on system. Triggering Winget installer (without adding to PATH)...");
                    
                    await InstallPythonViaWingetAsync();
                    installedViaWinget = true;

                    systemPython = await FindSystemPython312Async();

                    if (string.IsNullOrEmpty(systemPython))
                    {
                        // Fallback check if winget installed to standard user LocalAppData location
                        string defaultUserPy312 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python312", "python.exe");
                        if (File.Exists(defaultUserPy312))
                        {
                            systemPython = defaultUserPy312;
                        }
                    }

                    // Hide popup after Winget installation finishes
                    HideInstallProgressDialog();

                    if (installedViaWinget && !string.IsNullOrEmpty(systemPython))
                    {
                        AppendLauncherLog("======================================================================");
                        AppendLauncherLog("[RESTART] Python 3.12.8 successfully installed via Winget!");
                        AppendLauncherLog("[RESTART] Restarting FigPin Native C# Environment Manager shell...");
                        AppendLauncherLog("======================================================================");
                        await Task.Delay(1000);
                    }
                }

                if (string.IsNullOrEmpty(systemPython))
                {
                    throw new Exception("Python 3.12 installation could not be verified after Winget installation.");
                }

                AppendLauncherLog($"[INSTALL] Using system Python: {systemPython}");

                // Switch to Tab 1 (AI Terminal) so user sees live C# environment manager progress in-app
                DispatcherQueue.TryEnqueue(() =>
                {
                    SwitchToTab(1);
                });

                // Cleanup broken/stale virtual environment if present
                if (Directory.Exists(venvPath))
                {
                    AppendLauncherLog("[INSTALL] Cleaning up stale/broken virtual environment 'FigPin'...");
                    try
                    {
                        Directory.Delete(venvPath, true);
                    }
                    catch (Exception ex)
                    {
                        AppendLauncherLog($"[WARNING] Could not delete old venv directory: {ex.Message}");
                    }
                }

                // Step 2: Create Python Virtual Environment
                AppendLauncherLog($"[INSTALL] Creating virtual environment 'FigPin' using {systemPython}...");
                
                int venvResult;
                if (systemPython.StartsWith("py "))
                {
                    venvResult = await RunProcessAsync("py", $"-3.12 -m venv \"{venvPath}\"", backendDir, text => AppendLauncherLog(text));
                }
                else
                {
                    venvResult = await RunProcessAsync(systemPython, $"-m venv \"{venvPath}\"", backendDir, text => AppendLauncherLog(text));
                }

                if (venvResult != 0 || !Directory.Exists(venvPath))
                {
                    throw new Exception($"Failed to create virtual environment 'FigPin' using {systemPython}. Exit code: {venvResult}");
                }

                // Step 3: Install PyTorch CUDA & Requirements without cache
                AppendLauncherLog("[INSTALL] Installing PyTorch CUDA 12.1 & AI dependencies (no-cache-dir)...");
                string pipPath = Path.Combine(venvPath, "Scripts", "pip.exe");
                string reqPath = Path.Combine(backendDir, "requirements.txt");
                
                if (File.Exists(pipPath))
                {
                    // 3a. Install PyTorch with CUDA 12.1 wheel
                    AppendLauncherLog("[INSTALL] Step 3a: Installing PyTorch CUDA 12.1 wheel...");
                    await RunProcessAsync(pipPath, "install --no-cache-dir torch torchvision --index-url https://download.pytorch.org/whl/cu121", backendDir, text => AppendLauncherLog(text));

                    // 3b. Install requirements.txt without cache
                    if (File.Exists(reqPath))
                    {
                        AppendLauncherLog("[INSTALL] Step 3b: Installing requirements from requirements.txt (no-cache-dir)...");
                        await RunProcessAsync(pipPath, $"install --no-cache-dir -r \"{reqPath}\" --extra-index-url https://download.pytorch.org/whl/cu121", backendDir, text => AppendLauncherLog(text));
                    }

                    // 3c. Ensure CUDA 12 compatible onnxruntime-gpu is active without CPU onnxruntime conflict
                    AppendLauncherLog("[INSTALL] Step 3c: Configuring CUDA 12 compatible ONNX Runtime GPU engine...");
                    await RunProcessAsync(pipPath, "uninstall -y onnxruntime onnxruntime-gpu", backendDir, text => AppendLauncherLog(text));
                    await RunProcessAsync(pipPath, "install --no-cache-dir \"onnxruntime-gpu<1.20.0\"", backendDir, text => AppendLauncherLog(text));
                }

                // Step 4: Download AI Model Weights (BiRefNet, U2Net, SAM2, YOLO)
                AppendLauncherLog("[INSTALL] Running download_models.py...");
                DispatcherQueue.TryEnqueue(() =>
                {
                    SwitchToTab(2); // Switch to Model Weights tab
                });

                string pythonPath = Path.Combine(venvPath, "Scripts", "python.exe");
                string modelScript = Path.Combine(backendDir, "download_models.py");
                
                if (File.Exists(pythonPath) && File.Exists(modelScript))
                {
                    await RunProcessAsync(pythonPath, $"\"{modelScript}\"", backendDir, text => AppendLauncherLog(text));
                }

                AppendLauncherLog("[SUCCESS] All dependencies & models installed successfully!");
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                AppendLauncherLog($"[ERROR] Dependency installation failed: {ex.Message}");
                LogCrash("InstallDependenciesNativeAsync", ex);
            }
            finally
            {
                HideInstallProgressDialog();
            }
        }

        private void ShowInstallProgressDialog()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (this.Content == null || this.Content.XamlRoot == null) return;
                    if (_installDialog != null) return;

                    _installProgressBar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 10, CornerRadius = new CornerRadius(5) };
                    _installStatusText = new TextBlock { Text = "Initializing environment installer...", TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };

                    var stack = new StackPanel { Spacing = 14, Width = 380 };
                    stack.Children.Add(_installStatusText);
                    stack.Children.Add(_installProgressBar);

                    _installDialog = new ContentDialog
                    {
                        Title = "FigPin AI Dependency Manager",
                        Content = stack,
                        XamlRoot = this.Content.XamlRoot
                    };

                    _ = _installDialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    LogCrash("ShowInstallProgressDialog", ex);
                }
            });
        }

        private void UpdateInstallProgress(int percent, string statusText)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_installProgressBar != null) _installProgressBar.Value = percent;
                if (_installStatusText != null) _installStatusText.Text = statusText;
            });
        }

        private void HideInstallProgressDialog()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (_installDialog != null)
                    {
                        _installDialog.Hide();
                        _installDialog = null;
                    }
                }
                catch { }
            });
        }

        private Task<int> RunProcessAsync(string fileName, string arguments, string workingDir, Action<string> logCallback)
        {
            var tcs = new TaskCompletionSource<int>();
            try
            {
                // Clean fileName (strip surrounding quotes if present)
                string cleanFileName = fileName.Trim().Trim('"');
                Directory.CreateDirectory(workingDir);

                var psi = new ProcessStartInfo
                {
                    FileName = cleanFileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) logCallback(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) logCallback(e.Data); };

                proc.Exited += (s, e) =>
                {
                    tcs.SetResult(proc.ExitCode);
                    proc.Dispose();
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                logCallback($"[PROCESS ERROR] Failed to start {fileName}: {ex.Message}");
                tcs.SetResult(-1);
            }
            return tcs.Task;
        }

        private void StartBackendServer(string backendDir, string pythonPath)
        {
            try
            {
                KillBackendProcess();
                KillExistingPort8000Process();

                AppendBackendLog("======================================================================");
                AppendBackendLog("             Starting FastAPI AI Server (http://127.0.0.1:8000)");
                AppendBackendLog("======================================================================");

                if (!File.Exists(pythonPath))
                {
                    AppendBackendLog($"[SERVER ERROR] Python virtual environment executable not found at: {pythonPath}");
                    return;
                }

                string mainScript = Path.Combine(backendDir, "main.py");
                if (!File.Exists(mainScript))
                {
                    AppendBackendLog($"[SERVER ERROR] Backend main script not found at: {mainScript}");
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"\"{mainScript}\"",
                    WorkingDirectory = backendDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _backendProcess = new Process { StartInfo = psi };
                _backendProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppendBackendLog(e.Data); };
                _backendProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppendBackendLog(e.Data); };

                _backendProcess.Start();
                _backendProcess.BeginOutputReadLine();
                _backendProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                AppendBackendLog($"[SERVER ERROR] Failed to start backend server: {ex.Message}");
                LogCrash("StartBackendServer", ex);
            }
        }

        private void KillExistingPort8000Process()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"for /f \"tokens=5\" %a in ('netstat -aon ^| findstr :8000 ^| findstr LISTENING') do taskkill /F /PID %a\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(2000);
            }
            catch { }
        }

        #endregion

        #region Potency Selector Auto-Reprocess Dialog

        private async void PotencyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializingPotency) return;

            int newIndex = PotencyComboBox.SelectedIndex;
            if (newIndex == _previousPotencyIndex) return;

            if (!string.IsNullOrEmpty(_currentInputFilePath) && File.Exists(_currentInputFilePath))
            {
                int newPotency = newIndex switch { 1 => 2, 2 => 4, 3 => 6, _ => 1 };
                string fileName = Path.GetFileName(_currentInputFilePath);

                if (this.Content != null && this.Content.XamlRoot != null)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Reprocess Image with New Potency?",
                        Content = $"Do you want to reprocess '{fileName}' with {newPotency}X AI Potency?",
                        PrimaryButtonText = "Reprocess",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = this.Content.XamlRoot
                    };

                    var result = await dialog.ShowAsync();
                    if (result == ContentDialogResult.Primary)
                    {
                        _previousPotencyIndex = newIndex;
                        await ProcessImageFileAsync(_currentInputFilePath);
                    }
                    else
                    {
                        _isInitializingPotency = true;
                        PotencyComboBox.SelectedIndex = _previousPotencyIndex;
                        _isInitializingPotency = false;
                    }
                }
            }
            else
            {
                _previousPotencyIndex = newIndex;
            }
        }

        #endregion

        #region Backend Health Check & Image Processing

        private async Task CheckBackendHealthAsync()
        {
            StatusText.Text = "Checking Backend...";
            StatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 152, 0));
            RetryServerBtn.Visibility = Visibility.Collapsed;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    var response = await client.GetAsync($"{BackendBaseUrl}/health");
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        bool gpuAvailable = doc.RootElement.TryGetProperty("gpu_available", out var gpuProp) && gpuProp.GetBoolean();
                        string deviceName = doc.RootElement.TryGetProperty("device_name", out var devProp) ? devProp.GetString()! : "CPU";

                        if (gpuAvailable)
                        {
                            StatusText.Text = $"Backend Online ({deviceName})";
                            GpuBadgeText.Text = $"• {deviceName}";
                            StatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 175, 80));
                        }
                        else
                        {
                            StatusText.Text = "Backend Online (CPU Mode)";
                            GpuBadgeText.Text = "• CPU Accelerated";
                            StatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 152, 0));
                        }

                        FooterInfoText.Text = "Connected to Python AI Backend at http://127.0.0.1:8000";
                        return;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Health check attempt {attempt}: {ex.Message}");
                }

                await Task.Delay(1000);
            }

            StatusText.Text = "AI Server Offline";
            GpuBadgeText.Text = "• Server Offline";
            StatusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 244, 67, 54));
            FooterInfoText.Text = "Error: Python AI server is offline. Check Backend Server Logs tab.";
            RetryServerBtn.Visibility = Visibility.Visible;
        }

        private async void RetryServerBtn_Click(object sender, RoutedEventArgs e)
        {
            await CheckBackendHealthAsync();
        }

        private void DropArea_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            DropArea.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 35, 35, 38));
        }

        private void DropArea_DragLeave(object sender, DragEventArgs e)
        {
            DropArea.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 22, 22, 24));
        }

        private async void DropArea_Drop(object sender, DragEventArgs e)
        {
            DropArea.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 22, 22, 24));

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0)
                {
                    var file = items[0] as StorageFile;
                    if (file != null)
                    {
                        await ProcessImageFileAsync(file.Path);
                    }
                }
            }
        }

        private async void BrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".webp");
            picker.FileTypeFilter.Add(".bmp");

            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await ProcessImageFileAsync(file.Path);
            }
        }

        private async Task ProcessImageFileAsync(string filePath)
        {
            _currentInputFilePath = filePath;
            
            try
            {
                InputPreviewImage.Source = new BitmapImage(new Uri(filePath, UriKind.Absolute));
                InputPreviewGrid.Visibility = Visibility.Visible;
                DropPrompt.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting preview image bitmap: {ex.Message}");
                DropPrompt.Visibility = Visibility.Visible;
            }

            LoadingState.Visibility = Visibility.Visible;
            NoLayersPlaceholder.Visibility = Visibility.Visible;
            LayersScrollViewer.Visibility = Visibility.Collapsed;
            ExportBtn.IsEnabled = false;

            int potencyMultiplier = PotencyComboBox.SelectedIndex switch
            {
                1 => 2,
                2 => 4,
                3 => 6,
                _ => 1
            };

            ProcessingProgressBar.Value = 0;
            ProgressPercentText.Text = "0%";
            ProcessingStatusText.Text = $"Uploading image (AI Potency: {potencyMultiplier}X)...";

            try
            {
                using var content = new MultipartFormDataContent();
                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                var byteContent = new ByteArrayContent(fileBytes);
                content.Add(byteContent, "file", Path.GetFileName(filePath));
                content.Add(new StringContent(potencyMultiplier.ToString()), "potency_multiplier");

                var response = await client.PostAsync($"{BackendBaseUrl}/analyze", content);
                if (response.IsSuccessStatusCode)
                {
                    string analyzeJson = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(analyzeJson);
                    string jobId = doc.RootElement.GetProperty("job_id").GetString()!;

                    await PollJobStatusAsync(jobId, filePath);
                }
                else
                {
                    string errDetail = await response.Content.ReadAsStringAsync();
                    ShowErrorState($"Backend error ({response.StatusCode}): {errDetail}");
                }
            }
            catch (Exception ex)
            {
                ShowErrorState($"Failed to process image: {ex.Message}");
            }
            finally
            {
                LoadingState.Visibility = Visibility.Collapsed;
            }
        }

        private async Task PollJobStatusAsync(string jobId, string originalFilePath)
        {
            bool isFinished = false;

            while (!isFinished)
            {
                await Task.Delay(300);

                try
                {
                    var statusResponse = await client.GetAsync($"{BackendBaseUrl}/status/{jobId}");
                    if (!statusResponse.IsSuccessStatusCode)
                        continue;

                    string statusJson = await statusResponse.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(statusJson);
                    var root = doc.RootElement;

                    string statusStr = root.GetProperty("status").GetString()!;
                    int progress = root.GetProperty("progress_percent").GetInt32();
                    string desc = root.GetProperty("stage_description").GetString()!;

                    ProcessingProgressBar.Value = progress;
                    ProgressPercentText.Text = $"{progress}%";
                    ProcessingStatusText.Text = desc;

                    if (statusStr == "completed")
                    {
                        isFinished = true;
                        
                        string rootOutputDir = GetOutputsRootDir();
                        _currentJobDir = Path.Combine(rootOutputDir, jobId);
                        Directory.CreateDirectory(_currentJobDir);

                        RenderLayersUI(root);

                        try
                        {
                            InputPreviewImage.Source = new BitmapImage(new Uri(originalFilePath, UriKind.Absolute));
                        }
                        catch { }

                        InputPreviewGrid.Visibility = Visibility.Visible;
                        DropPrompt.Visibility = Visibility.Collapsed;

                        NoLayersPlaceholder.Visibility = Visibility.Collapsed;
                        LayersScrollViewer.Visibility = Visibility.Visible;
                        ExportBtn.IsEnabled = true;

                        FooterInfoText.Text = $"Layer separation completed for {Path.GetFileName(originalFilePath)}. Click any layer preview to view in Photos.";
                    }
                    else if (statusStr == "failed")
                    {
                        isFinished = true;
                        string err = root.TryGetProperty("error_message", out var errProp) ? errProp.GetString()! : "Processing failed.";
                        ShowErrorState($"Error: {err}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Polling error: {ex.Message}");
                }
            }
        }

        private void RenderLayersUI(JsonElement root)
        {
            LayersStackPanel.Children.Clear();

            if (root.TryGetProperty("layers", out var layersElement) && layersElement.ValueKind == JsonValueKind.Array)
            {
                int index = 1;
                foreach (var layer in layersElement.EnumerateArray())
                {
                    string name = layer.GetProperty("name").GetString()!;
                    string url = layer.TryGetProperty("preview_url", out var urlProp) ? urlProp.GetString()! : "";
                    string details = layer.TryGetProperty("details", out var detProp) ? detProp.GetString()! : "";
                    string rawPath = layer.GetProperty("file_path").GetString()!;
                    string fileBasename = Path.GetFileName(rawPath);

                    string targetFilePath = rawPath;
                    if (!File.Exists(targetFilePath) && !string.IsNullOrEmpty(_currentJobDir))
                    {
                        targetFilePath = Path.Combine(_currentJobDir, fileBasename);
                    }

                    var card = CreateLayerCard(index++, name, details, fileBasename, url, targetFilePath);
                    LayersStackPanel.Children.Add(card);
                }
            }
        }

        private Border CreateLayerCard(int index, string name, string details, string fileName, string previewUrl, string filePath)
        {
            var border = new Border
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 36, 36, 39)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14),
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 51, 55)),
                BorderThickness = new Thickness(1)
            };

            ToolTipService.SetToolTip(border, "Click to open layer image in Windows Photos");

            border.PointerEntered += (s, e) =>
            {
                border.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 48, 52));
                border.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 120, 212));
            };

            border.PointerExited += (s, e) =>
            {
                border.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 36, 36, 39));
                border.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 51, 55));
            };

            border.PointerPressed += (s, e) =>
            {
                OpenFileInDefaultApp(filePath);
            };

            var grid = new Grid { ColumnSpacing = 14 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var imgBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 17, 17, 17)),
                Height = 70
            };

            if (!string.IsNullOrEmpty(previewUrl))
            {
                var img = new Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
                img.Source = new BitmapImage(new Uri(previewUrl));
                imgBorder.Child = img;
            }

            Grid.SetColumn(imgBorder, 0);

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
            stack.Children.Add(new TextBlock
            {
                Text = $"{index}. {name}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
            });
            stack.Children.Add(new TextBlock
            {
                Text = details,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            stack.Children.Add(new TextBlock
            {
                Text = fileName,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlHighlightAccentBrush"]
            });

            Grid.SetColumn(stack, 1);

            grid.Children.Add(imgBorder);
            grid.Children.Add(stack);
            border.Child = grid;

            return border;
        }

        private void InputPreviewImage_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentInputFilePath))
            {
                OpenFileInDefaultApp(_currentInputFilePath);
            }
        }

        private void OpenFileInDefaultApp(string filePath)
        {
            try
            {
                string targetPath = filePath;
                if (!File.Exists(targetPath) && !string.IsNullOrEmpty(_currentJobDir))
                {
                    targetPath = Path.Combine(_currentJobDir, Path.GetFileName(filePath));
                }

                if (File.Exists(targetPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = targetPath,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open image: {ex.Message}");
            }
        }

        private void ShowErrorState(string errorMessage)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (InputPreviewGrid.Visibility != Visibility.Visible)
                {
                    DropPrompt.Visibility = Visibility.Visible;
                }
                LoadingState.Visibility = Visibility.Collapsed;
                FooterInfoText.Text = errorMessage;
            });
        }

        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            string rootOutputDir = GetOutputsRootDir();
            string targetFolder = !string.IsNullOrEmpty(_currentJobDir) && Directory.Exists(_currentJobDir)
                ? _currentJobDir
                : rootOutputDir;

            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            System.Diagnostics.Process.Start("explorer.exe", targetFolder);
        }

        #endregion
    }
}
