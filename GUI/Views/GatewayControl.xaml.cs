using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using OneHealthMonitor.Core;

namespace OneHealthMonitor.Views
{
    public partial class GatewayControl : UserControl
    {
        private readonly MainWindow _main;
        private readonly DispatcherTimer _refreshTimer;
        public GatewayService Service { get; } = new GatewayService();

        public GatewayControl(MainWindow main)
        {
            InitializeComponent();
            _main = main;
            _main.RegisterGateway(Service);

            // Default CSV path
            string defaultCsv = Path.GetFullPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Gateway", "sensors.csv"));
            TxtCsvPath.Text = defaultCsv;

            // Subscribe to gateway events
            Service.OnLogMessage += msg =>
            {
                Application.Current.Dispatcher.Invoke(() => AppendLogMessage(msg));
            };

            Service.OnSensorUpdated += sensor =>
            {
                Application.Current.Dispatcher.Invoke(() => RefreshSensorGrid());
            };

            // Refresh timer
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _refreshTimer.Tick += (_, _) => RefreshUI();
            _refreshTimer.Start();

            RefreshUI();
        }

        private async void BtnStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (Service.IsRunning)
            {
                Service.Stop();
                BtnStartStop.Content = "▶  START GATEWAY";
                BtnStartStop.Style = (Style)FindResource("PrimaryButton");
                RefreshUI();
            }
            else
            {
                string gwId = TxtGwId.Text.Trim();
                string serverIp = TxtServerIp.Text.Trim();
                if (!int.TryParse(TxtServerPort.Text.Trim(), out int serverPort)) serverPort = 9090;
                if (!int.TryParse(TxtSensorPort.Text.Trim(), out int sensorPort)) sensorPort = 8080;
                if (!int.TryParse(TxtVideoPort.Text.Trim(), out int videoPort)) videoPort = 8081;
                string csvPath = TxtCsvPath.Text.Trim();

                BtnStartStop.IsEnabled = false;
                BtnStartStop.Content = "CONNECTING...";

                await System.Threading.Tasks.Task.Run(() =>
                {
                    Service.Start(gwId, serverIp, serverPort, sensorPort, videoPort, csvPath);
                });

                BtnStartStop.IsEnabled = true;
                if (Service.IsRunning)
                {
                    BtnStartStop.Content = "⏹  STOP GATEWAY";
                    BtnStartStop.Style = (Style)FindResource("DangerButton");
                }
                else
                {
                    BtnStartStop.Content = "▶  START GATEWAY";
                    BtnStartStop.Style = (Style)FindResource("PrimaryButton");
                }
                RefreshUI();
            }
        }

        private void BtnBrowseCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "CSV files|*.csv|All files|*.*",
                Title = "Selecionar sensors.csv"
            };
            if (dlg.ShowDialog() == true)
                TxtCsvPath.Text = dlg.FileName;
        }

        private void BtnForceRetry_Click(object sender, RoutedEventArgs e)
        {
            if (Service.Buffer != null && Service.IsRunning)
            {
                // Stop and restart the retry loop to reset backoff to minimum interval
                Service.Buffer.Stop();
                Service.Buffer.Start();
                AppendLogMessage("[BUFFER] Retry manual solicitado — backoff reiniciado.");
            }
            RefreshUI();
        }

        private void BtnClearBuffer_Click(object sender, RoutedEventArgs e)
        {
            Service.Buffer?.Clear();
            RefreshUI();
        }

        private void SliderTimeout_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (HbTimeoutText != null)
            {
                int val = (int)SliderTimeout.Value;
                HbTimeoutText.Text = $"{val}s";
                if (Service.HeartbeatMonitor != null)
                    Service.HeartbeatMonitor.TimeoutSeconds = val;
            }
        }

        private void SliderInterval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (HbIntervalText != null)
            {
                int val = (int)SliderInterval.Value;
                HbIntervalText.Text = $"{val}s";
                if (Service.HeartbeatMonitor != null)
                    Service.HeartbeatMonitor.CheckIntervalMs = val * 1000;
            }
        }

        private void RefreshUI()
        {
            bool running = Service.IsRunning;

            GwIdHeader.Text = Service.GatewayId;
            GwStatusText.Text = running ? "CONNECTED TO SERVER" : "DISCONNECTED";
            GwStatusBadge.Background = running
                ? (Brush)FindResource("Success")
                : (Brush)FindResource("Danger");

            // Buffer
            int bc = Service.BufferCount;
            BufferCountText.Text = bc.ToString();
            RetryStatusText.Text = bc > 0 ? "Status: Retrying..." : "Status: Idle";
            NextRetryText.Text = bc > 0 ? $"Next burst in {Service.Buffer?.CurrentRetryMs / 1000}s" : "";

            RefreshSensorGrid();
        }

        private void RefreshSensorGrid()
        {
            try
            {
                var sensors = Service.GetConnectedSensors();
                SensorGrid.ItemsSource = null;
                SensorGrid.ItemsSource = sensors;
            }
            catch { }
        }

        private void AppendLogMessage(string msg)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");

            // Determine category
            Color color;

            if (msg.Contains("[ERRO") || msg.Contains("ERR"))
            {
                color = (Color)ColorConverter.ConvertFromString("#EF5350");
                AddToDoc(LogErrorsDoc, ts, msg, color);
            }
            else if (msg.Contains("DATA") || msg.Contains("FORWARD") || msg.Contains("[VALIDAÇÃO]"))
            {
                color = (Color)ColorConverter.ConvertFromString("#4CAF50");
                AddToDoc(LogDataDoc, ts, msg, color);
            }
            else if (msg.Contains("HEARTBEAT") || msg.Contains("heartbeat"))
            {
                color = (Color)ColorConverter.ConvertFromString("#00BFA5");
                AddToDoc(LogHeartDoc, ts, msg, color);
            }
            else if (msg.Contains("STATUS") || msg.Contains("estado"))
            {
                color = (Color)ColorConverter.ConvertFromString("#9C27B0");
                AddToDoc(LogStatusDoc, ts, msg, color);
            }
            else
            {
                color = (Color)ColorConverter.ConvertFromString("#00BFA5");
            }

            // Always add to ALL
            AddToDoc(LogAllDoc, ts, msg, color);
            LogAll.ScrollToEnd();
        }

        private void AddToDoc(FlowDocument doc, string ts, string msg, Color color)
        {
            var para = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
            para.Inlines.Add(new Run($"[{ts}]  ") { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A9BB0")) });
            para.Inlines.Add(new Run(msg) { Foreground = new SolidColorBrush(color) });
            doc.Blocks.Add(para);

            // Limit to 500 entries
            while (doc.Blocks.Count > 500)
                doc.Blocks.Remove(doc.Blocks.FirstBlock);
        }
    }
}
