using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OneHealthMonitor.Core;

namespace OneHealthMonitor.Views
{
    public partial class DashboardView : UserControl
    {
        private readonly MainWindow _main;
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _refreshTimer;
        private readonly DispatcherTimer _blinkTimer;
        private readonly ObservableCollection<LiveDataItem> _liveItems = new();
        private bool _liveDotVisible = true;

        public DashboardView(MainWindow main)
        {
            InitializeComponent();
            _main = main;

            LiveFeedList.ItemsSource = _liveItems;

            // Clock timer — every 1s
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
            _clockTimer.Start();
            ClockText.Text = DateTime.Now.ToString("HH:mm:ss");

            // Refresh timer — every 2s
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _refreshTimer.Tick += (_, _) => RefreshDashboard();
            _refreshTimer.Start();

            // Live dot blink
            _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _blinkTimer.Tick += (_, _) =>
            {
                _liveDotVisible = !_liveDotVisible;
                LiveDot.Opacity = _liveDotVisible ? 1.0 : 0.2;
            };
            _blinkTimer.Start();

            // Subscribe to global data events
            _main.OnGlobalDataReceived += item =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _liveItems.Insert(0, item);
                    if (_liveItems.Count > 100)
                        _liveItems.RemoveAt(100);
                });
            };

            // Subscribe to servidor data events
            _main.ServidorService.OnDataStored += (sensorId, tipo, valor, zona, ts) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Also ensure live feed gets updated from server-side stored events
                    // (already handled via GatewayService.OnDataReceived in most cases)
                });
            };

            RefreshDashboard();
        }

        private void RefreshDashboard()
        {
            try
            {
                // System status
                bool gwRunning = _main.ActiveGateways.Any(g => g.IsRunning);
                bool srvRunning = _main.ServidorService.IsRunning;
                bool systemOnline = gwRunning || srvRunning;

                SystemStatusText.Text = systemOnline ? "SYSTEM ONLINE" : "SYSTEM OFFLINE";
                SystemBadge.Background = systemOnline
                    ? (System.Windows.Media.Brush)FindResource("Success")
                    : (System.Windows.Media.Brush)FindResource("Danger");

                // Gateway
                GatewayDot.Fill = gwRunning
                    ? (System.Windows.Media.Brush)FindResource("Success")
                    : (System.Windows.Media.Brush)FindResource("Danger");
                GatewayStatusText.Text = gwRunning
                    ? $"{_main.ActiveGateways.Count(g => g.IsRunning)} GATEWAYS CONNECTED"
                    : "ALL DISCONNECTED";
                GatewayCount.Text = _main.ActiveGateways.Count(g => g.IsRunning).ToString();

                // Messages
                MessageCount.Text = _main.ActiveGateways.Sum(g => g.TotalForwards).ToString();

                // Buffer
                int bufCount = _main.ActiveGateways.Sum(g => g.BufferCount);
                int bufMax = _main.ActiveGateways.Sum(g => g.BufferMax);
                BufferBar.Value = bufCount;
                BufferPercent.Text = $"{(bufMax > 0 ? (double)bufCount / bufMax * 100 : 0):F1}%";
                BufferText.Text = $"{bufCount} QUEUED MESSAGES / {bufMax} CAP";

                if (bufCount > 800)
                    BufferBar.Foreground = (System.Windows.Media.Brush)FindResource("Warning");
                else
                    BufferBar.Foreground = (System.Windows.Media.Brush)FindResource("Accent");

                // Sensors
                var sensors = _main.ActiveGateways
                                   .SelectMany(g => g.GetConnectedSensors())
                                   .GroupBy(s => s.SensorId)
                                   .Select(grp => grp.First())
                                   .ToList();
                if (sensors.Count > 0)
                {
                    int ativos = sensors.Count(s => s.Estado == "ativo");
                    int manut = sensors.Count(s => s.Estado == "manutencao");
                    int desativ = sensors.Count(s => s.Estado == "desativado");
                    int indisp = sensors.Count(s => s.Estado == "indisponivel");

                    ActiveSensorsCount.Text = ativos.ToString();
                    SensorSummaryText.Text = $"{ativos} ONLINE · {manut} MAINTENANCE · {desativ} DISABLED";

                    SensorStatusList.ItemsSource = sensors;
                }
                else
                {
                    ActiveSensorsCount.Text = "0";
                    SensorSummaryText.Text = "No sensors loaded";
                    SensorStatusList.ItemsSource = null;
                }
            }
            catch { }
        }

        private void RefreshSensors_Click(object sender, RoutedEventArgs e) => RefreshDashboard();

        private void BtnConnectSensor_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to Sensor view
            if (_main.FindName("BtnSensor") is Button btn)
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        private void BtnSendData_Click(object sender, RoutedEventArgs e)
        {
            if (_main.FindName("BtnSensor") is Button btn)
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        private void BtnHeartbeat_Click(object sender, RoutedEventArgs e)
        {
            // Heartbeat is handled by auto-heartbeat in SensorView
        }

        private void BtnEmergencyStop_Click(object sender, RoutedEventArgs e)
        {
            foreach (var gw in _main.ActiveGateways)
            {
                gw.Stop();
            }
            _main.ServidorService.Stop();
            RefreshDashboard();
        }
    }
}
