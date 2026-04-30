using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
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

        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            PositionWindow();
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

                GroupsItemsControl.ItemsSource = _config.Groups;
                TaskbarPanel.Height = _config.Layout.Height;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load configuration: {ex.Message}");
                _config = new Config();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PositionWindow();
        }

        private void PositionWindow()
        {
            IntPtr taskbarHWnd = NativeMethods.FindWindow("Shell_TrayWnd", null);

            // Get DPI scaling factors
            double dpiX = 1.0;
            double dpiY = 1.0;
            PresentationSource source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformFromDevice.M11;
                dpiY = source.CompositionTarget.TransformFromDevice.M22;
            }

            if (taskbarHWnd != IntPtr.Zero)
            {
                if (NativeMethods.GetWindowRect(taskbarHWnd, out NativeMethods.RECT rect))
                {
                    // Apply DPI scaling to physical coordinates
                    double taskbarTop = rect.Top * dpiY;
                    double taskbarHeight = (rect.Bottom - rect.Top) * dpiY;

                    // Full window covers the taskbar area up to the screen width, minus margins
                    double screenWidth = SystemParameters.PrimaryScreenWidth;

                    // We need enough height to show the popup above the taskbar
                    double popupHeightAllowance = 400; // Arbitrary allowance for popup

                    this.Left = _config.Layout.LeftMargin;
                    this.Width = screenWidth - _config.Layout.LeftMargin - _config.Layout.RightMargin;
                    this.Top = taskbarTop - popupHeightAllowance;
                    this.Height = taskbarHeight + popupHeightAllowance;

                    // Adjust popup margin so it sits exactly above the taskbar height
                    PopupGrid.Margin = new Thickness(0, 0, 0, taskbarHeight + 5);
                }
            }
            else
            {
                // Fallback positioning if taskbar not found
                this.Left = _config.Layout.LeftMargin;
                this.Width = SystemParameters.PrimaryScreenWidth - _config.Layout.LeftMargin - _config.Layout.RightMargin;
                this.Top = SystemParameters.PrimaryScreenHeight - _config.Layout.Height - 400;
                this.Height = _config.Layout.Height + 400;
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
                PopupItemsControl.ItemsSource = group.Apps;
                ShowPopup();
            }
        }

        private void AppButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is AppConfig app)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = app.Path,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to launch {app.Name}: {ex.Message}");
                }
                finally
                {
                    HidePopup();
                }
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
                double offsetX = currentPoint.X - _dragStartPoint.X;

                // Calculate new margins based on dragging
                Thickness currentMargin = TaskbarPanel.Margin;
                double newLeft = currentMargin.Left + offsetX;
                double newRight = currentMargin.Right - offsetX;

                // Max margin logic to prevent moving off window
                // Allow movement but restrict so it does not exceed the container width bounds
                double maxMargin = this.Width / 2 - TaskbarPanel.ActualWidth / 2;

                if (newLeft > maxMargin)
                {
                    newLeft = maxMargin;
                    newRight = -maxMargin;
                }
                else if (newLeft < -maxMargin)
                {
                    newLeft = -maxMargin;
                    newRight = maxMargin;
                }

                TaskbarPanel.Margin = new Thickness(newLeft, currentMargin.Top, newRight, currentMargin.Bottom);

                _dragStartPoint = currentPoint; // Reset start point for incremental dragging
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