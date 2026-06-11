using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Nowy_folder__8_
{
    public class SteamSearchResultItem
    {
        public int id { get; set; }
        public string name { get; set; } = "";
        public string tiny_image { get; set; } = "";
    }

    public class SteamSearchResult
    {
        public int total { get; set; }
        public List<SteamSearchResultItem> items { get; set; } = new();
    }

    public class GitHubReleaseAsset
    {
        public string name { get; set; } = "";
        public string browser_download_url { get; set; } = "";
    }

    public class GitHubRelease
    {
        public string tag_name { get; set; } = "";
        public List<GitHubReleaseAsset> assets { get; set; } = new();
    }

    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const string AppVersion = "1.2.0";
        private const string GithubOwner = "kozaaaaczx";
        private const string GithubRepo = "SteamImporter";
        private CancellationTokenSource? _searchDebounce;

        public MainWindow()
        {
            InitializeComponent();
            Log("Steam File Importer v1.2.0 initialized.");
            DetectSteamPath();
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            SetDarkTitleBar();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync();
        }

        private void SetDarkTitleBar()
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                IntPtr hwnd = helper.Handle;
                int trueValue = 1;
                int result = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueValue, sizeof(int));
                if (result != 0)
                {
                    const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref trueValue, sizeof(int));
                }
            }
            catch
            {
            }
        }

        private void DetectSteamPath()
        {
            Log("Searching registry for Steam installation...");
            string? detectedPath = null;

            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        detectedPath = key.GetValue("SteamPath")?.ToString();
                    }
                }

                if (string.IsNullOrEmpty(detectedPath))
                {
                    using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"))
                    {
                        if (key != null)
                        {
                            detectedPath = key.GetValue("InstallPath")?.ToString();
                        }
                    }
                }

                if (string.IsNullOrEmpty(detectedPath))
                {
                    using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Valve\Steam"))
                    {
                        if (key != null)
                        {
                            detectedPath = key.GetValue("InstallPath")?.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[WARNING] Registry access failed: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(detectedPath))
            {
                detectedPath = Path.GetFullPath(detectedPath.Replace('/', '\\'));
            }

            if (string.IsNullOrEmpty(detectedPath) || !Directory.Exists(detectedPath))
            {
                string defaultPath = @"C:\Program Files (x86)\Steam";
                if (Directory.Exists(defaultPath))
                {
                    detectedPath = defaultPath;
                }
            }

            if (!string.IsNullOrEmpty(detectedPath) && Directory.Exists(detectedPath))
            {
                SteamPathTextBox.Text = detectedPath;
                StatusTextBlock.Text = "Status: Steam detected";
                Log($"[SUCCESS] Steam installation directory found: {detectedPath}");
            }
            else
            {
                StatusTextBlock.Text = "Status: Steam directory not found. Please browse manually.";
                Log("[WARNING] Could not automatically locate Steam installation. Please use \"Browse...\" to select the Steam folder.");
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"SteamImporter/{AppVersion}");
                string endpoint = $"https://api.github.com/repos/{GithubOwner}/{GithubRepo}/releases/latest";
                string json = await client.GetStringAsync(endpoint);
                var release = JsonSerializer.Deserialize<GitHubRelease>(json);

                if (release == null || string.IsNullOrWhiteSpace(release.tag_name))
                    return;

                if (!TryParseReleaseTag(release.tag_name, out Version? latestVersion))
                    return;

                if (!Version.TryParse(AppVersion, out Version? currentVersion))
                    currentVersion = new Version(0, 0);

                if (latestVersion <= currentVersion)
                {
                    Log($"[UPDATE] Current version {AppVersion} is up to date.");
                    return;
                }

                var asset = release.assets?.FirstOrDefault(a => a.name.Contains("win-x64.zip", StringComparison.OrdinalIgnoreCase))
                            ?? release.assets?.FirstOrDefault(a => a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

                if (asset == null)
                    return;

                string tempPath = Path.Combine(Path.GetTempPath(), $"SteamImporter-{release.tag_name}.zip");
                await DownloadReleaseAssetAsync(asset.browser_download_url, tempPath);

                Log($"[UPDATE] New version {release.tag_name} downloaded to {tempPath}");
                StatusTextBlock.Text = $"Status: Update downloaded ({release.tag_name})";
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{tempPath}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log($"[UPDATE] Update check failed: {ex.Message}");
            }
        }

        private static bool TryParseReleaseTag(string tag, out Version? version)
        {
            version = null;
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                tag = tag.Substring(1);
            }

            return Version.TryParse(tag, out version);
        }

        private static async Task DownloadReleaseAssetAsync(string url, string destinationPath)
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(destinationPath);
            await stream.CopyToAsync(fileStream);
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "Select Steam Directory",
                    InitialDirectory = Directory.Exists(SteamPathTextBox.Text) ? SteamPathTextBox.Text : "C:\\"
                };

                if (dialog.ShowDialog() == true)
                {
                    string selected = dialog.FolderName;
                    SteamPathTextBox.Text = selected;
                    Log($"[INFO] Steam directory set manually to: {selected}");
                    StatusTextBlock.Text = "Status: Steam path set manually";
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to select folder: {ex.Message}");
                MessageBox.Show($"Could not open folder picker: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            string steamPath = SteamPathTextBox.Text.Trim();

            if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
            {
                Log("[ERROR] Cannot import: The selected Steam directory is empty or does not exist.");
                MessageBox.Show("Please select a valid Steam directory first.", "Invalid Steam Directory", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Log("\n=== STARTING IMPORT PROCESS ===");
            StatusTextBlock.Text = "Status: Importing...";

            int manifestsCopied = 0;
            int luasCopied = 0;

            try
            {
                Log("[STEP 1/2] Opening file picker for .manifest files...");
                var manifestDialog = new OpenFileDialog
                {
                    Title = "Select .manifest Files to Import",
                    Filter = "Steam Manifest Files (*.manifest)|*.manifest|All files (*.*)|*.*",
                    Multiselect = true
                };

                if (manifestDialog.ShowDialog() == true)
                {
                    string[] files = manifestDialog.FileNames;
                    string targetDir = Path.Combine(steamPath, "depotcache");

                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                        Log($"Created directory: {targetDir}");
                    }

                    foreach (string file in files)
                    {
                        try
                        {
                            string fileName = Path.GetFileName(file);
                            string destPath = Path.Combine(targetDir, fileName);
                            File.Copy(file, destPath, overwrite: true);
                            Log($"  + Copied manifest: {fileName} -> \\depotcache\\");
                            manifestsCopied++;
                        }
                        catch (Exception copyEx)
                        {
                            Log($"  [ERROR] Failed to copy manifest '{Path.GetFileName(file)}': {copyEx.Message}");
                        }
                    }
                    Log($"[INFO] Finished importing manifests. Total copied: {manifestsCopied}");
                }
                else
                {
                    Log("[INFO] Manifest selection skipped by user.");
                }

                Log("[STEP 2/2] Opening file picker for .lua script files...");
                var luaDialog = new OpenFileDialog
                {
                    Title = "Select .lua Files to Import",
                    Filter = "LUA Script Files (*.lua)|*.lua|All files (*.*)|*.*",
                    Multiselect = true
                };

                if (luaDialog.ShowDialog() == true)
                {
                    string[] files = luaDialog.FileNames;
                    string targetDir = Path.Combine(steamPath, "config", "stplug-in");

                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                        Log($"Created directory: {targetDir}");
                    }

                    foreach (string file in files)
                    {
                        try
                        {
                            string fileName = Path.GetFileName(file);
                            string destPath = Path.Combine(targetDir, fileName);
                            File.Copy(file, destPath, overwrite: true);
                            Log($"  + Copied LUA script: {fileName} -> \\config\\stplug-in\\");
                            luasCopied++;
                        }
                        catch (Exception copyEx)
                        {
                            Log($"  [ERROR] Failed to copy LUA script '{Path.GetFileName(file)}': {copyEx.Message}");
                        }
                    }
                    Log($"[INFO] Finished importing LUA scripts. Total copied: {luasCopied}");
                }
                else
                {
                    Log("[INFO] LUA script selection skipped by user.");
                }

                Log("=== IMPORT PROCESS COMPLETED ===");
                Log($"Summary: Copied {manifestsCopied} .manifest file(s) and {luasCopied} .lua script(s).");
                StatusTextBlock.Text = $"Status: Import Complete ({manifestsCopied} manifests, {luasCopied} luas)";

                MessageBox.Show(
                    $"Import operation complete!\n\nManifest files copied: {manifestsCopied}\nLua scripts copied: {luasCopied}", 
                    "Import Complete", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                Log($"[CRITICAL ERROR] Import process encountered an error: {ex.Message}");
                StatusTextBlock.Text = "Status: Import failed";
                MessageBox.Show($"Import failed: {ex.Message}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportDropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                if (FindResource("AccentLightBrush") is System.Windows.Media.Brush highlight)
                {
                    ImportDropZone.Background = highlight;
                }
                ImportDropZoneText.Text = "Release to import dropped files";
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void ImportDropZone_DragLeave(object sender, DragEventArgs e)
        {
            if (FindResource("ButtonBgBrush") is System.Windows.Media.Brush normal)
            {
                ImportDropZone.Background = normal;
            }
            ImportDropZoneText.Text = "Drag .manifest and .lua files here";
            e.Handled = true;
        }

        private void ImportDropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void ImportDropZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                ImportFiles(files);
            }

            if (FindResource("ButtonBgBrush") is System.Windows.Media.Brush normal)
            {
                ImportDropZone.Background = normal;
            }
            ImportDropZoneText.Text = "Drag .manifest and .lua files here";
            e.Handled = true;
        }

        private void ImportFiles(IEnumerable<string> files)
        {
            string steamPath = SteamPathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
            {
                Log("[ERROR] Cannot import: The selected Steam directory is empty or does not exist.");
                MessageBox.Show("Please select a valid Steam directory first.", "Invalid Steam Directory", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var manifestFiles = files.Where(f => string.Equals(Path.GetExtension(f), ".manifest", StringComparison.OrdinalIgnoreCase)).ToList();
            var luaFiles = files.Where(f => string.Equals(Path.GetExtension(f), ".lua", StringComparison.OrdinalIgnoreCase)).ToList();

            if (!manifestFiles.Any() && !luaFiles.Any())
            {
                MessageBox.Show("Please drop only .manifest or .lua files.", "Unsupported Files", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Log("\n=== STARTING IMPORT PROCESS ===");
            StatusTextBlock.Text = "Status: Importing dropped files...";

            int manifestsCopied = 0;
            int luasCopied = 0;

            if (manifestFiles.Any())
            {
                string targetDir = Path.Combine(steamPath, "depotcache");
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    Log($"Created directory: {targetDir}");
                }

                foreach (string file in manifestFiles)
                {
                    try
                    {
                        string fileName = Path.GetFileName(file);
                        string destPath = Path.Combine(targetDir, fileName);
                        File.Copy(file, destPath, overwrite: true);
                        Log($"  + Copied manifest: {fileName} -> \\depotcache\\");
                        manifestsCopied++;
                    }
                    catch (Exception copyEx)
                    {
                        Log($"  [ERROR] Failed to copy manifest '{Path.GetFileName(file)}': {copyEx.Message}");
                    }
                }
                Log($"[INFO] Finished importing manifests. Total copied: {manifestsCopied}");
            }

            if (luaFiles.Any())
            {
                string targetDir = Path.Combine(steamPath, "config", "stplug-in");
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    Log($"Created directory: {targetDir}");
                }

                foreach (string file in luaFiles)
                {
                    try
                    {
                        string fileName = Path.GetFileName(file);
                        string destPath = Path.Combine(targetDir, fileName);
                        File.Copy(file, destPath, overwrite: true);
                        Log($"  + Copied LUA script: {fileName} -> \\config\\stplug-in\\");
                        luasCopied++;
                    }
                    catch (Exception copyEx)
                    {
                        Log($"  [ERROR] Failed to copy LUA script '{Path.GetFileName(file)}': {copyEx.Message}");
                    }
                }
                Log($"[INFO] Finished importing LUA scripts. Total copied: {luasCopied}");
            }

            Log("=== IMPORT PROCESS COMPLETED ===");
            Log($"Summary: Copied {manifestsCopied} .manifest file(s) and {luasCopied} .lua script(s).");
            StatusTextBlock.Text = $"Status: Import Complete ({manifestsCopied} manifests, {luasCopied} luas)";

            MessageBox.Show(
                $"Import operation complete!\n\nManifest files copied: {manifestsCopied}\nLua scripts copied: {luasCopied}", 
                "Import Complete", 
                MessageBoxButton.OK, 
                MessageBoxImage.Information
            );
        }

        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounce?.Cancel();
            _searchDebounce = new CancellationTokenSource();
            var token = _searchDebounce.Token;

            try
            {
                await Task.Delay(300, token);
                if (!token.IsCancellationRequested)
                    await SearchGamesAsync(SearchTextBox.Text);
            }
            catch (TaskCanceledException) { }
        }

        private async Task SearchGamesAsync(string term)
        {
            term = term.Trim();
            if (string.IsNullOrWhiteSpace(term))
            {
                SearchResultsListBox.ItemsSource = null;
                StatusTextBlock.Text = "Status: Ready";
                return;
            }

            Log($"\n[SEARCH] Searching for Steam game: \"{term}\"...");
            StatusTextBlock.Text = "Status: Searching games...";
            
            try
            {
                using var client = new HttpClient();
                string url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(term)}&l=english&cc=US";
                string json = await client.GetStringAsync(url);
                
                var result = JsonSerializer.Deserialize<SteamSearchResult>(json);
                if (result != null && result.items != null && result.items.Count > 0)
                {
                    SearchResultsListBox.ItemsSource = result.items;
                    Log($"[SUCCESS] Found {result.items.Count} game(s) matching search.");
                    StatusTextBlock.Text = $"Status: Found {result.items.Count} game(s)";
                }
                else
                {
                    SearchResultsListBox.ItemsSource = null;
                    Log($"[INFO] No games found matching \"{term}\".");
                    StatusTextBlock.Text = "Status: No games found";
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Search failed: {ex.Message}");
                StatusTextBlock.Text = "Status: Search failed";
                MessageBox.Show($"Search failed: {ex.Message}", "Search Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedGame = SearchResultsListBox.SelectedItem as SteamSearchResultItem;
            if (selectedGame != null)
            {
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                DownloadPanel.Visibility = Visibility.Visible;
                SelectedGameTitleText.Text = selectedGame.name;
                SelectedGameIdText.Text = $"AppID: {selectedGame.id}";
            }
            else
            {
                EmptyStatePanel.Visibility = Visibility.Visible;
                DownloadPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void DownloadManifest_Click(object sender, RoutedEventArgs e)
        {
            await DownloadFileAsync("manifest");
        }

        private async void DownloadLua_Click(object sender, RoutedEventArgs e)
        {
            await DownloadFileAsync("lua");
        }

        private async Task DownloadFileAsync(string fileType)
        {
            var selectedGame = SearchResultsListBox.SelectedItem as SteamSearchResultItem;
            if (selectedGame == null)
            {
                MessageBox.Show("Please select a game first.", "No Game Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string baseUrl = "https://generator.ryuu.lol/api";
            string appId = selectedGame.id.ToString();
            Log($"\n[DOWNLOAD] Fetching {fileType} for \"{selectedGame.name}\" (AppID: {appId}) from Ryuu...");
            StatusTextBlock.Text = "Status: Fetching...";

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);

                string requestUrl = $"{baseUrl}/download/{appId}?file_type={fileType}";

                Log($"  Sending GET request to {baseUrl}/download/{appId}...");
                var response = await client.GetAsync(requestUrl);

                if (!response.IsSuccessStatusCode)
                {
                    string errMsg = await response.Content.ReadAsStringAsync();
                    Log($"[ERROR] Download failed. Server returned HTTP {response.StatusCode}");
                    if (!string.IsNullOrEmpty(errMsg))
                    {
                        Log($"  Detail: {errMsg}");
                    }
                    StatusTextBlock.Text = "Status: Download failed";
                    MessageBox.Show(
                        $"Failed to download from Ryuu.\n\nServer Response: {response.StatusCode}\n{errMsg}", 
                        "Download Failed", 
                        MessageBoxButton.OK, 
                        MessageBoxImage.Error
                    );
                    return;
                }

                string steamPath = SteamPathTextBox.Text.Trim();
                if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
                {
                    Log("[ERROR] Cannot save file: Target Steam path does not exist.");
                    MessageBox.Show("Please set a valid Steam path first.", "Invalid Steam Directory", MessageBoxButton.OK, MessageBoxImage.Warning);
                    StatusTextBlock.Text = "Status: Steam path invalid";
                    return;
                }

                string targetDir = fileType == "manifest" 
                    ? Path.Combine(steamPath, "depotcache") 
                    : Path.Combine(steamPath, "config", "stplug-in");

                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    Log($"Created directory: {targetDir}");
                }

                string fileName = "";
                if (response.Content.Headers.ContentDisposition != null)
                {
                    fileName = response.Content.Headers.ContentDisposition.FileName?.Trim('\"') ?? "";
                }

                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = fileType == "manifest" ? $"{appId}.manifest" : $"{appId}.lua";
                }

                string fullDestPath = Path.Combine(targetDir, fileName);

                using (var fileStream = new FileStream(fullDestPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fileStream);
                }

                Log($"[SUCCESS] Download completed! Saved: {fileName} -> {targetDir}");
                StatusTextBlock.Text = "Status: Imported successfully";

                MessageBox.Show(
                    $"Successfully imported {fileType} file!\nSaved as: {fileName}", 
                    "Import Success", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Download encountered an exception: {ex.Message}");
                StatusTextBlock.Text = "Status: Error downloading";
                MessageBox.Show($"Download failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RestartSteamButton_Click(object sender, RoutedEventArgs e)
        {
            string steamPath = SteamPathTextBox.Text.Trim();
            string steamExe = Path.Combine(steamPath, "steam.exe");

            if (string.IsNullOrEmpty(steamPath) || !File.Exists(steamExe))
            {
                Log("[ERROR] Cannot restart Steam: steam.exe not found at the configured path.");
                MessageBox.Show(
                    "Could not find steam.exe in the selected Steam directory.\nPlease verify the Steam path is correct.",
                    "Steam Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            var confirm = MessageBox.Show(
                "This will close Steam and relaunch it.\nAny active downloads or games will be interrupted.\n\nContinue?",
                "Restart Steam",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (confirm != MessageBoxResult.Yes)
                return;

            Log("\n=== RESTARTING STEAM ===");
            StatusTextBlock.Text = "Status: Restarting Steam...";

            try
            {
                Process[] steamProcesses = Process.GetProcessesByName("steam");
                if (steamProcesses.Length > 0)
                {
                    Log($"[INFO] Found {steamProcesses.Length} Steam process(es). Sending shutdown command...");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = steamExe,
                        Arguments = "-shutdown",
                        UseShellExecute = true
                    });

                    int waited = 0;
                    while (waited < 15000)
                    {
                        await Task.Delay(500);
                        waited += 500;
                        steamProcesses = Process.GetProcessesByName("steam");
                        if (steamProcesses.Length == 0)
                            break;
                    }

                    if (Process.GetProcessesByName("steam").Length > 0)
                    {
                        Log("[WARNING] Steam did not shut down gracefully. Force killing...");
                        foreach (var proc in Process.GetProcessesByName("steam"))
                        {
                            try { proc.Kill(); } catch { }
                        }
                        await Task.Delay(1000);
                    }

                    Log("[INFO] Steam closed successfully.");
                }
                else
                {
                    Log("[INFO] Steam is not currently running.");
                }

                Log("[INFO] Launching Steam...");
                Process.Start(new ProcessStartInfo
                {
                    FileName = steamExe,
                    UseShellExecute = true
                });

                Log("[SUCCESS] Steam has been restarted.");
                StatusTextBlock.Text = "Status: Steam restarted";
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to restart Steam: {ex.Message}");
                StatusTextBlock.Text = "Status: Restart failed";
                MessageBox.Show($"Failed to restart Steam:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Log(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogTextBox.AppendText($"[{timestamp}] {message}\n");
            LogTextBox.ScrollToEnd();
        }
    }
}