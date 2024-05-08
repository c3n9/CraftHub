using MaterialDesignThemes.Wpf;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace CraftHub.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        public MainWindow()
        {
            InitializeComponent();
            App.MainWindow = this;
        }

        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is System.Windows.Controls.Button button))
                return;

            string tabHeader = button.DataContext as string;
            if (string.IsNullOrEmpty(tabHeader))
                return;

            TabItem tabItemToRemove = TCWorkAreas.Items.Cast<TabItem>().FirstOrDefault(item => item.Header as string == tabHeader);
            if (tabItemToRemove != null)
                TCWorkAreas.Items.Remove(tabItemToRemove);
        }
        private void TCWorkAreas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TabControl tabControl = sender as TabControl;
            if (tabControl != null)
            {
                TabItem selectedTab = tabControl.SelectedItem as TabItem;
                if (selectedTab != null && selectedTab.Header is PackIcon packIcon && packIcon.Kind == PackIconKind.TrayPlus)
                {
                    var newFrame = new Frame();
                    newFrame.Content = new WorkingAreaView();
                    var newTabItem = new TabItem
                    {
                        Header = "New Tab",
                        Content = newFrame
                    };

                    tabControl.Items.Insert(tabControl.Items.Count - 1, newTabItem);
                    tabControl.SelectedItem = newTabItem;
                }
            }

        }
        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem?.Parent as ContextMenu;
            var textBox = contextMenu?.PlacementTarget as System.Windows.Controls.TextBox;

            if (textBox != null)
            {
                string previousText = textBox.Text;
                textBox.IsReadOnly = false;

                RoutedEventHandler lostFocusHandler = null;
                lostFocusHandler = (s, args) =>
                {
                    textBox.IsReadOnly = true;
                    textBox.LostFocus -= lostFocusHandler;

                    if (textBox.Text == previousText)
                        return;

                    var result = MessageBox.Show("Save new name?", "Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Сохранить новое имя
                        previousText = textBox.Text;
                    }
                    else if (result == MessageBoxResult.No)
                    {
                        // Откатить изменения
                        textBox.Text = previousText;
                    }
                };

                textBox.LostFocus += lostFocusHandler;
            }



        }
        // all this cringe is to make the window not overlap Windows taskbar. I think it's Ok to do this
        // because this doesn't contradict to the MVVM pattern
        #region WindowResizeFixes
        void Window_SourceInitialized(object sender, EventArgs e)
        {
            IntPtr mWindowHandle = (new WindowInteropHelper(this)).Handle;
            HwndSource.FromHwnd(mWindowHandle).AddHook(new HwndSourceHook(WindowProc));
        }

        private static System.IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case 0x0024:
                    WmGetMinMaxInfo(hwnd, lParam);
                    break;
            }

            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(System.IntPtr hwnd, System.IntPtr lParam)
        {
            POINT lMousePosition;
            GetCursorPos(out lMousePosition);

            IntPtr lCurrentScreen = MonitorFromPoint(lMousePosition, MonitorOptions.MONITOR_DEFAULTTONEAREST);


            MINMAXINFO lMmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));

            MONITORINFO lCurrentScreenInfo = new MONITORINFO();
            if (GetMonitorInfo(lCurrentScreen, lCurrentScreenInfo) == false)
            {
                return;
            }

            //Position relative pour notre fenêtre
            lMmi.ptMaxPosition.X = lCurrentScreenInfo.rcWork.Left - lCurrentScreenInfo.rcMonitor.Left;
            lMmi.ptMaxPosition.Y = lCurrentScreenInfo.rcWork.Top - lCurrentScreenInfo.rcMonitor.Top;
            lMmi.ptMaxSize.X = lCurrentScreenInfo.rcWork.Right - lCurrentScreenInfo.rcWork.Left;
            lMmi.ptMaxSize.Y = lCurrentScreenInfo.rcWork.Bottom - lCurrentScreenInfo.rcWork.Top;

            Marshal.StructureToPtr(lMmi, lParam, true);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr MonitorFromPoint(POINT pt, MonitorOptions dwFlags);
        enum MonitorOptions : uint
        {
            MONITOR_DEFAULTTONULL = 0x00000000,
            MONITOR_DEFAULTTOPRIMARY = 0x00000001,
            MONITOR_DEFAULTTONEAREST = 0x00000002
        }

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                this.X = x;
                this.Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MONITORINFO
        {
            public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            public RECT rcMonitor = new RECT();
            public RECT rcWork = new RECT();
            public int dwFlags = 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;

            public RECT(int left, int top, int right, int bottom)
            {
                this.Left = left;
                this.Top = top;
                this.Right = right;
                this.Bottom = bottom;
            }
        }



        #endregion

        
    }
}