using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using OneHealthMonitor.Core;

namespace OneHealthMonitor.Views
{
    public partial class ServidorView : UserControl
    {
        private readonly MainWindow _main;
        private readonly DispatcherTimer _refreshTimer;
        private int _currentPage = 1;
        private const int PageSize = 10;
        private List<MedicaoEntry> _allFilteredData = new();

        public ServidorView(MainWindow main)
        {
            InitializeComponent();
            _main = main;

            // Subscribe to server events
            _main.ServidorService.OnLogMessage += msg =>
            {
                Application.Current.Dispatcher.Invoke(() => AppendServerLog(msg));
            };

            _main.ServidorService.OnDataStored += (sensorId, tipo, valor, zona, ts) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Refresh stats on new data
                    RefreshStats();
                });
            };

            // Refresh timer — every 2s
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _refreshTimer.Tick += (_, _) => RefreshAll();
            _refreshTimer.Start();

            // Initial port display
            PortHeader.Text = "9090";

            // Refresh imediatamente ao carregar a view
            Loaded += (_, _) => RefreshAll();
        }

        private void BtnStartStop_Click(object sender, RoutedEventArgs e)
        {
            if (_main.ServidorService.IsRunning) return;

            int porta = 9090;
            if (int.TryParse(PortHeader.Text, out int p) && p > 0) porta = p;

            _main.ServidorService.Start(porta);
            UpdateStatus();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _main.ServidorService.Stop();
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            bool running = _main.ServidorService.IsRunning;
            SrvStatusText.Text = running ? "RUNNING" : "STOPPED";
            SrvStatusBadge.Background = running
                ? (Brush)FindResource("Success")
                : (Brush)FindResource("Danger");
            BtnStartStop.IsEnabled = !running;
            BtnStop.IsEnabled = running;
            PortHeader.Text = _main.ServidorService.Porta.ToString();
        }

        private void RefreshAll()
        {
            UpdateStatus();
            RefreshGateways();
            RefreshSensorStatus();
            RefreshFiles();
            RefreshStats();
            LoadFilteredData(); // atualizar grid de dados automaticamente
        }

        private void RefreshGateways()
        {
            try
            {
                var gws = _main.ServidorService.GetGatewayDetails();
                GwCountBadge.Text = $"{gws.Count} ACTIVE";
                GatewayGrid.ItemsSource = gws.Select(g => new
                {
                    g.Id,
                    g.Endpoint,
                    g.Since,
                    g.Messages
                }).ToList();
            }
            catch { }
        }

        private void RefreshSensorStatus()
        {
            try
            {
                // Começar com os estados guardados no servidor (CSV)
                var merged = _main.ServidorService.Store.GetSensorStatuses()
                    .ToDictionary(
                        s => s.SensorId,
                        s => new { SensorId = s.SensorId, Estado = s.Estado, Timestamp = s.Timestamp });

                // Sobrepor com estado live dos gateways ativos (mais recente e inclui "ativo")
                foreach (var gw in _main.ActiveGateways)
                {
                    foreach (var sensor in gw.GetConnectedSensors())
                    {
                        merged[sensor.SensorId] = new
                        {
                            SensorId = sensor.SensorId,
                            Estado = sensor.Estado,
                            Timestamp = sensor.LastSync.ToString("HH:mm:ss")
                        };
                    }
                }

                SensorStatusGrid.ItemsSource = merged.Values
                    .OrderBy(s => s.SensorId)
                    .ToList();
            }
            catch { }
        }

        private void RefreshFiles()
        {
            try
            {
                var files = _main.ServidorService.Store.GetDataFiles();
                FilesList.ItemsSource = files.Select(f => new
                {
                    f.Name,
                    Info = $"{f.LineCount} rows · {f.SizeBytes / 1024.0:F1} KB"
                }).ToList();
            }
            catch { }
        }

        private void RefreshStats()
        {
            try
            {
                // AVG TEMP
                var tempData = _main.ServidorService.GetRecentData("TEMP", null, null, 1000);
                if (tempData.Count > 0)
                {
                    var vals = tempData.Select(d =>
                    {
                        double.TryParse(d.Valor, NumberStyles.Any, CultureInfo.InvariantCulture, out double v);
                        return v;
                    }).ToList();
                    AvgTempText.Text = vals.Average().ToString("F1");
                }

                // AVG PM2.5
                var pmData = _main.ServidorService.GetRecentData("PM2.5", null, null, 1000);
                if (pmData.Count > 0)
                {
                    var vals = pmData.Select(d =>
                    {
                        double.TryParse(d.Valor, NumberStyles.Any, CultureInfo.InvariantCulture, out double v);
                        return v;
                    }).ToList();
                    AvgPmText.Text = vals.Average().ToString("F1");
                }

                // PEAK RUIDO
                var ruidoData = _main.ServidorService.GetRecentData("RUIDO", null, null, 1000);
                if (ruidoData.Count > 0)
                {
                    var vals = ruidoData.Select(d =>
                    {
                        double.TryParse(d.Valor, NumberStyles.Any, CultureInfo.InvariantCulture, out double v);
                        return v;
                    }).ToList();
                    PeakRuidoText.Text = vals.Max().ToString("F1");
                }

                // TOTAL RECORDS
                var allFiles = _main.ServidorService.Store.GetDataFiles();
                int total = allFiles.Sum(f => f.LineCount);
                TotalRecordsText.Text = total.ToString();
            }
            catch { }
        }

        private void BtnFilter_Click(object sender, RoutedEventArgs e)
        {
            _currentPage = 1;
            LoadFilteredData();
        }

        private void LoadFilteredData()
        {
            try
            {
                string tipo = (CmbFilterType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "TYPE: ALL";
                string zona = (CmbFilterZona.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ZONE: ALL";
                string sensor = (CmbFilterSensor.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "SENSOR: ALL";

                string? tipoFilter = tipo.Contains("ALL") ? null : tipo;
                string? zonaFilter = zona.Contains("ALL") ? null : zona;
                string? sensorFilter = sensor.Contains("ALL") ? null : sensor;

                _allFilteredData = _main.ServidorService.GetRecentData(tipoFilter, zonaFilter, sensorFilter, 500);

                ShowPage();
            }
            catch { }
        }

        private void ShowPage()
        {
            int totalPages = Math.Max(1, (int)Math.Ceiling((double)_allFilteredData.Count / PageSize));
            _currentPage = Math.Clamp(_currentPage, 1, totalPages);

            var pageData = _allFilteredData
                .Skip((_currentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            DataExplorerGrid.ItemsSource = pageData;

            int start = (_currentPage - 1) * PageSize + 1;
            int end = Math.Min(_currentPage * PageSize, _allFilteredData.Count);
            PageInfoText.Text = _allFilteredData.Count > 0
                ? $"SHOWING {start}-{end} OF {_allFilteredData.Count} RECORDS"
                : "SHOWING 0 RECORDS";
            PageNumText.Text = _currentPage.ToString();

            BtnPrevPage.IsEnabled = _currentPage > 1;
            BtnNextPage.IsEnabled = _currentPage < totalPages;
        }

        private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            _currentPage--;
            ShowPage();
        }

        private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            _currentPage++;
            ShowPage();
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV files|*.csv|All files|*.*",
                FileName = $"export_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                Title = "Exportar medições para CSV"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                string tipo = (CmbFilterType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "TYPE: ALL";
                string zona = (CmbFilterZona.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ZONE: ALL";
                string sensor = (CmbFilterSensor.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "SENSOR: ALL";

                string? tipoFilter = tipo.Contains("ALL") ? null : tipo;
                string? zonaFilter = zona.Contains("ALL") ? null : zona;
                string? sensorFilter = sensor.Contains("ALL") ? null : sensor;

                var data = _main.ServidorService.GetRecentData(tipoFilter, zonaFilter, sensorFilter, 10000);

                var lines = new List<string> { "SensorId,Zona,Tipo,Valor,Timestamp" };
                lines.AddRange(data.Select(d => $"{d.SensorId},{d.Zona},{d.Tipo},{d.Valor},{d.Timestamp}"));

                File.WriteAllLines(dlg.FileName, lines, System.Text.Encoding.UTF8);

                MessageBox.Show(
                    $"Exportados {data.Count} registos para:\n{dlg.FileName}",
                    "Exportação Concluída",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Erro ao exportar: {ex.Message}",
                    "Erro de Exportação",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void AppendServerLog(string msg)
        {
            var para = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };

            string ts = DateTime.Now.ToString("HH:mm:ss");
            para.Inlines.Add(new Run($"[{ts}]  ")
            {
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A9BB0"))
            });

            Color color;
            if (msg.Contains("FORWARD") || msg.Contains("OK"))
                color = (Color)ColorConverter.ConvertFromString("#4CAF50");
            else if (msg.Contains("ERRO") || msg.Contains("ERR"))
                color = (Color)ColorConverter.ConvertFromString("#EF5350");
            else if (msg.Contains("STATUS"))
                color = (Color)ColorConverter.ConvertFromString("#9C27B0");
            else
                color = (Color)ColorConverter.ConvertFromString("#00BFA5");

            para.Inlines.Add(new Run(msg) { Foreground = new SolidColorBrush(color) });
            ServerLogDoc.Blocks.Add(para);

            while (ServerLogDoc.Blocks.Count > 500)
                ServerLogDoc.Blocks.Remove(ServerLogDoc.Blocks.FirstBlock);

            ServerLog.ScrollToEnd();
        }
    }
}
