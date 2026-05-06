using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TaskbarOverlay.Models;
using TaskbarOverlay.Interop;
using System.Diagnostics;

namespace TaskbarOverlay
{
    public partial class MainWindow : Window
    {
        private Config _config;
        private bool _isDragging = false;
        private Point _dragStartPoint;
        private double _dragStartLeft;

        public MainWindow()
        {
            InitializeComponent();
            LoadConfig();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        }

        private DispatcherTimer _resizeTimer;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WndProc);

            _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _resizeTimer.Tick += (s, args) =>
            {
                _resizeTimer.Stop();
                PositionWindow();
            };
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_DPICHANGED || msg == NativeMethods.WM_DISPLAYCHANGE)
            {
                // Debounce layout updates
                if (_resizeTimer != null)
                {
                    _resizeTimer.Stop();
                    _resizeTimer.Start();
                }
            }
            return IntPtr.Zero;
        }

        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (_resizeTimer != null)
            {
                _resizeTimer.Stop();
                _resizeTimer.Start();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            base.OnClosed(e);
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists("config.json"))
                {
                    string json = File.ReadAllText("config.json");
                    _config = JsonSerializer.Deserialize<Config>(json);
                }
                else
                {
                    // Default configuration
                    _config = new Config();
                    _config.Groups.Add(new GroupConfig
                    {
                        Name = "Dev",
                        Apps = new System.Collections.Generic.List<AppConfig>
                        {
                            new AppConfig { Name = "VSCode", Path = "code" },
                            new AppConfig { Name = "Terminal", Path = "wt" }
                        }
                    });
                }

                // Extract icons
                foreach (var group in _config.Groups)
                {
                    foreach (var app in group.Apps)
                    {
                        app.IconSource = ExtractIconFromPath(app.Path);
                    }
                }

                GroupsItemsControl.ItemsSource = _config.Groups;
                TaskbarPanel.Height = _config.Layout.Height;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load configuration: {ex.Message}");
                _config = new Config();
            }
        }

        private string ResolveFullPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            string fullPath = path;

            // Resolve .lnk shortcut if necessary
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                try
                {
                    NativeMethods.IShellLinkW link = (NativeMethods.IShellLinkW)new NativeMethods.ShellLink();
                    System.Runtime.InteropServices.ComTypes.IPersistFile file = (System.Runtime.InteropServices.ComTypes.IPersistFile)link;
                    file.Load(path, 0);
                    System.Text.StringBuilder sb = new System.Text.StringBuilder(260);
                    NativeMethods.WIN32_FIND_DATAW w32fd;
                    link.GetPath(sb, sb.Capacity, out w32fd, 0);

                    if (sb.Length > 0)
                    {
                        fullPath = sb.ToString();
                        return fullPath;
                    }
                }
                catch
                {
                    // Fallback to original path if resolution fails
                }
            }

            if (!Path.IsPathRooted(fullPath) && !File.Exists(fullPath))
            {
                var values = Environment.GetEnvironmentVariable("PATH");
                if (values != null)
                {
                    foreach (var pathDir in values.Split(Path.PathSeparator))
                    {
                        var testPath = Path.Combine(pathDir, fullPath);
                        if (File.Exists(testPath)) return testPath;
                        if (File.Exists(testPath + ".exe")) return testPath + ".exe";
                    }
                }
            }
            return fullPath;
        }

        private ImageSource? ExtractIconFromPath(string path)
        {
            try
            {
                string fullPath = ResolveFullPath(path);
                if (File.Exists(fullPath))
                {
                    using (var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(fullPath))
                    {
                        if (sysIcon != null)
                        {
                            var img = Imaging.CreateBitmapSourceFromHIcon(
                                sysIcon.Handle,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            img.Freeze(); // Optimize memory
                            return img;
                        }
                    }
                }
            }
            catch
            {
                // Ignore extraction errors
            }
            return null;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PositionWindow();
        }

        private void PositionWindow()
        {
            // Use SHAppBarMessage for robust taskbar state detection
            NativeMethods.APPBARDATA abd = new NativeMethods.APPBARDATA();
            abd.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(abd);
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref abd);

            // Get DPI scaling factors
            double dpiX = 1.0;
            double dpiY = 1.0;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformFromDevice.M11;
                dpiY = source.CompositionTarget.TransformFromDevice.M22;
            }

            // Apply DPI scaling to physical coordinates
            double taskbarLeft = abd.rc.Left * dpiX;
            double taskbarTop = abd.rc.Top * dpiY;
            double taskbarWidth = (abd.rc.Right - abd.rc.Left) * dpiX;
            double taskbarHeight = (abd.rc.Bottom - abd.rc.Top) * dpiY;

            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            double popupHeightAllowance = 400;

            // Calculate layout depending on which edge the taskbar is on
            if (abd.uEdge == NativeMethods.ABE_TOP)
            {
                this.Left = _config.Layout.LeftMargin;
                this.Width = screenWidth - _config.Layout.LeftMargin - _config.Layout.RightMargin;
                this.Top = taskbarTop;
                this.Height = taskbarHeight + popupHeightAllowance;

                TaskbarPanel.VerticalAlignment = VerticalAlignment.Top;
                PopupGrid.VerticalAlignment = VerticalAlignment.Top;
                PopupGrid.Margin = new Thickness(0, taskbarHeight + 5, 0, 0);
            }
            else if (abd.uEdge == NativeMethods.ABE_LEFT)
            {
                // Vertical layout
                this.Left = taskbarLeft;
                this.Top = 0;
                this.Width = taskbarWidth + popupHeightAllowance;
                this.Height = screenHeight;

                TaskbarPanel.VerticalAlignment = VerticalAlignment.Center;
                TaskbarPanel.HorizontalAlignment = HorizontalAlignment.Left;
                PopupGrid.VerticalAlignment = VerticalAlignment.Center;
                PopupGrid.HorizontalAlignment = HorizontalAlignment.Left;
                PopupGrid.Margin = new Thickness(taskbarWidth + 5, 0, 0, 0);
            }
            else if (abd.uEdge == NativeMethods.ABE_RIGHT)
            {
                // Vertical layout
                this.Left = taskbarLeft - popupHeightAllowance;
                this.Top = 0;
                this.Width = taskbarWidth + popupHeightAllowance;
                this.Height = screenHeight;

                TaskbarPanel.VerticalAlignment = VerticalAlignment.Center;
                TaskbarPanel.HorizontalAlignment = HorizontalAlignment.Right;
                PopupGrid.VerticalAlignment = VerticalAlignment.Center;
                PopupGrid.HorizontalAlignment = HorizontalAlignment.Right;
                PopupGrid.Margin = new Thickness(0, 0, taskbarWidth + 5, 0);
            }
            else // Default to BOTTOM
            {
                this.Left = _config.Layout.LeftMargin;
                this.Width = screenWidth - _config.Layout.LeftMargin - _config.Layout.RightMargin;
                this.Top = taskbarTop - popupHeightAllowance;
                this.Height = taskbarHeight + popupHeightAllowance;

                TaskbarPanel.VerticalAlignment = VerticalAlignment.Bottom;
                TaskbarPanel.HorizontalAlignment = HorizontalAlignment.Center;
                PopupGrid.VerticalAlignment = VerticalAlignment.Bottom;
                PopupGrid.HorizontalAlignment = HorizontalAlignment.Center;
                PopupGrid.Margin = new Thickness(0, 0, 0, taskbarHeight + 5);
            }
        }

        private void RootGrid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Close popup if clicking on the background grid (transparent area)
            HidePopup();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            HidePopup();
        }

        private void GroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is GroupConfig group)
            {
                PopupTitle.Text = group.Name;
                // Sort apps by usage frequency (LaunchCount descending)
                group.Apps.Sort((a, b) => b.LaunchCount.CompareTo(a.LaunchCount));
                PopupItemsControl.ItemsSource = null; // force refresh
                PopupItemsControl.ItemsSource = group.Apps;
                ShowPopup();
            }
        }

        private async void AppButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is AppConfig app)
            {
                HidePopup();
                try
                {
                    await System.Threading.Tasks.Task.Run(() =>
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = app.Path,
                            UseShellExecute = true
                        });
                    });

                    // Increment LaunchCount and save config
                    app.LaunchCount++;
                    SaveConfig();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to launch {app.Name}: {ex.Message}");
                }
            }
        }

        private async void RunAsAdmin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is AppConfig app)
            {
                HidePopup();
                try
                {
                    await System.Threading.Tasks.Task.Run(() =>
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = app.Path,
                            UseShellExecute = true,
                            Verb = "runas" // Request admin privileges
                        });
                    });

                    app.LaunchCount++;
                    SaveConfig();
                }
                catch (Exception ex)
                {
                    // User might cancel UAC prompt, which throws an exception. Safely ignore or log.
                    Debug.WriteLine($"Failed to launch as Admin {app.Name}: {ex.Message}");
                }
            }
        }

        private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is AppConfig app)
            {
                try
                {
                    string fullPath = ResolveFullPath(app.Path);
                    if (File.Exists(fullPath))
                    {
                        Process.Start("explorer.exe", $"/select,\"{fullPath}\"");
                    }
                    else
                    {
                        MessageBox.Show($"Could not resolve file location for {app.Name}");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open file location for {app.Name}: {ex.Message}");
                }
                finally
                {
                    HidePopup();
                }
            }
        }

        private void SaveConfig()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_config, options);

                // Atomic save
                string tempFile = "config.json.tmp";
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, "config.json", true);
            }
            catch (Exception ex)
            {
                // Ignore save errors silently
                Debug.WriteLine($"Failed to save config: {ex.Message}");
            }
        }

        private void ShowPopup()
        {
            if (PopupGrid.Visibility == Visibility.Collapsed)
            {
                PopupGrid.Visibility = Visibility.Visible;
                Storyboard sb = (Storyboard)FindResource("ShowPopupStoryboard");
                sb.Begin();
            }
        }

        private void HidePopup()
        {
            if (PopupGrid.Visibility == Visibility.Visible)
            {
                Storyboard sb = (Storyboard)FindResource("HidePopupStoryboard");
                sb.Begin();
            }
        }

        private void HidePopupStoryboard_Completed(object sender, EventArgs e)
        {
            PopupGrid.Visibility = Visibility.Collapsed;
        }

        // --- Dragging Logic for the Taskbar Panel ---
        private void TaskbarPanel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Only initiate drag if left clicking not on a button, or handle drag on panel
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _dragStartPoint = e.GetPosition(this);
                // Capture the starting Left margin of the StackPanel relative to its parent container (Center alignment handles it differently, so we use Margin to shift)
                // Actually, a simpler way is to shift the horizontal alignment or margin
                TaskbarPanel.CaptureMouse();
            }
        }

        private void TaskbarPanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPoint = e.GetPosition(this);

                // Handle horizontal vs vertical dragging based on alignment
                if (TaskbarPanel.VerticalAlignment == VerticalAlignment.Top || TaskbarPanel.VerticalAlignment == VerticalAlignment.Bottom)
                {
                    double offsetX = currentPoint.X - _dragStartPoint.X;
                    Thickness currentMargin = TaskbarPanel.Margin;
                    double newLeft = currentMargin.Left + offsetX;
                    double newRight = currentMargin.Right - offsetX;

                    double maxMargin = this.Width / 2 - TaskbarPanel.ActualWidth / 2;
                    if (newLeft > maxMargin) { newLeft = maxMargin; newRight = -maxMargin; }
                    else if (newLeft < -maxMargin) { newLeft = -maxMargin; newRight = maxMargin; }

                    TaskbarPanel.Margin = new Thickness(newLeft, currentMargin.Top, newRight, currentMargin.Bottom);
                }
                else
                {
                    double offsetY = currentPoint.Y - _dragStartPoint.Y;
                    Thickness currentMargin = TaskbarPanel.Margin;
                    double newTop = currentMargin.Top + offsetY;
                    double newBottom = currentMargin.Bottom - offsetY;

                    double maxMargin = this.Height / 2 - TaskbarPanel.ActualHeight / 2;
                    if (newTop > maxMargin) { newTop = maxMargin; newBottom = -maxMargin; }
                    else if (newTop < -maxMargin) { newTop = -maxMargin; newBottom = maxMargin; }

                    TaskbarPanel.Margin = new Thickness(currentMargin.Left, newTop, currentMargin.Right, newBottom);
                }

                _dragStartPoint = currentPoint;
            }
        }

        private void TaskbarPanel_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                TaskbarPanel.ReleaseMouseCapture();
            }
        }
    }
}