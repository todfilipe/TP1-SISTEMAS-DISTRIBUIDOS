using System;
using System.Collections.ObjectModel;
using System.Globalization;
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
        private readonly Action<LiveDataItem> _dataReceivedHandler;

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

            // Subscribe to global gateway data events
            _dataReceivedHandler = item =>
            {
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    _liveItems.Insert(0, item);
                    if (_liveItems.Count > 50)
                        _liveItems.RemoveAt(50);
                });
            };
            _main.OnGlobalDataReceived += _dataReceivedHandler;

            Unloaded += DashboardView_Unloaded;

            RefreshDashboard();
        }

        private void DashboardView_Unloaded(object sender, RoutedEventArgs e)
        {
            _clockTimer?.Stop();
            _refreshTimer?.Stop();
            _blinkTimer?.Stop();

            _main.OnGlobalDataReceived -= _dataReceivedHandler;
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

                // Gateways
                int gwActive = _main.ActiveGateways.Count(g => g.IsRunning);
                GatewayCount.Text = gwActive.ToString();
                GatewayDot.Fill = gwActive > 0
                    ? (System.Windows.Media.Brush)FindResource("Success")
                    : (System.Windows.Media.Brush)FindResource("Danger");
                GatewayStatusText.Text = gwActive > 0
                    ? $"{gwActive} CONNECTED"
                    : "ALL DISCONNECTED";

                // Messages
                MessageCount.Text = _main.ActiveGateways.Sum(g => g.TotalForwards).ToString();

                // Sensors
                var sensors = _main.ActiveGateways
                                   .SelectMany(g => g.GetConnectedSensors())
                                   .GroupBy(s => s.SensorId)
                                   .Select(grp => grp.First())
                                   .ToList();

                int ativos = sensors.Count(s => s.Estado == "ativo");
                int manut = sensors.Count(s => s.Estado == "manutencao");
                int desativ = sensors.Count(s => s.Estado == "desativado");
                int indisp = sensors.Count(s => s.Estado == "indisponivel");
                int desligados = sensors.Count(s => s.Estado == "desligado");
                int alertas = manut + desativ + indisp + desligados;

                ActiveSensorsCount.Text = ativos.ToString();
                SensorSummaryText.Text = sensors.Count > 0
                    ? $"{ativos} online · {manut} maint · {desativ} off"
                    : "No sensors loaded";

                // Alertas (estados != ativo)
                AlertCount.Text = alertas.ToString();
                AlertCount.Foreground = alertas > 0
                    ? (System.Windows.Media.Brush)FindResource("Warning")
                    : (System.Windows.Media.Brush)FindResource("Success");

                // Sensor status list
                SensorStatusList.ItemsSource = sensors.Count > 0 ? sensors : null;

                // Retry buffer
                int bufCount = _main.ActiveGateways.Sum(g => g.BufferCount);
                int bufMax = _main.ActiveGateways.Sum(g => g.BufferMax);
                if (bufMax == 0) bufMax = 1000;
                BufferBar.Value = bufCount;
                BufferPercent.Text = $"{(double)bufCount / bufMax * 100:F1}%";
                BufferText.Text = $"{bufCount} QUEUED / {bufMax} CAP";

                BufferBar.Foreground = bufCount > 800
                    ? (System.Windows.Media.Brush)FindResource("Warning")
                    : (System.Windows.Media.Brush)FindResource("Accent");

                // Server metrics
                RefreshServerMetrics();
            }
            catch { }
        }

        private void RefreshServerMetrics()
        {
            try
            {
                // Avg TEMP
                var tempData = _main.ServidorService.GetRecentData("TEMP", null, null, 1000);
                if (tempData.Count > 0)
                {
                    var vals = tempData
                        .Select(d => { double.TryParse(d.Valor, NumberStyles.Any, CultureInfo.InvariantCulture, out double v); return v; })
                        .ToList();
                    AvgTempDash.Text = vals.Average().ToString("F1");
                }
                else
                {
                    AvgTempDash.Text = "--";
                }

                // Avg PM2.5
                var pmData = _main.ServidorService.GetRecentData("PM2.5", null, null, 1000);
                if (pmData.Count > 0)
                {
                    var vals = pmData
                        .Select(d => { double.TryParse(d.Valor, NumberStyles.Any, CultureInfo.InvariantCulture, out double v); return v; })
                        .ToList();
                    AvgPmDash.Text = vals.Average().ToString("F1");
                }
                else
                {
                    AvgPmDash.Text = "--";
                }

                // Peak RUIDO
                var ruidoData = _main.ServidorService.GetRecentData("RUIDO", null, null, 1000);
                if (ruidoData.Count > 0)
                {
                    var vals = ruidoData
                        .Select(d => { double.TryParse(d.Valor, NumberStyles.Any, CultureInfo.InvariantCulture, out double v); return v; })
                        .ToList();
                    PeakRuidoDash.Text = vals.Max().ToString("F1");
                }
                else
                {
                    PeakRuidoDash.Text = "--";
                }

                // Total records
                var allFiles = _main.ServidorService.Store.GetDataFiles();
                TotalRecordsDash.Text = allFiles.Sum(f => f.LineCount).ToString();
            }
            catch { }
        }

        private void RefreshSensors_Click(object sender, RoutedEventArgs e) => RefreshDashboard();

        private void BtnForceRetry_Click(object sender, RoutedEventArgs e)
        {
            foreach (var gw in _main.ActiveGateways)
            {
                int count = gw.BufferCount;
                if (count > 0 && gw.IsRunning)
                {
                    // The RetryBuffer runs continuously; stopping/restarting resets backoff to minimum
                    gw.Buffer?.Stop();
                    gw.Buffer?.Start();
                }
            }
            RefreshDashboard();
        }
    }
}
