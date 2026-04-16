using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using OneHealthMonitor.Core;

namespace OneHealthMonitor.Views
{
    public partial class SensorControl : UserControl
    {
        private readonly MainWindow _main;
        private SensorClient? _sensor;
        private readonly DispatcherTimer _tsTimer;

        private static readonly string[] AllTypes = { "TEMP", "HUM", "AR", "RUIDO", "PM2.5", "PM10", "LUZ", "VIDEO" };
        private readonly List<ToggleButton> _typeToggles = new();

        public SensorControl(MainWindow main)
        {
            InitializeComponent();
            _main = main;

            // Populate type toggles
            foreach (string t in AllTypes)
            {
                var tb = new ToggleButton
                {
                    Content = t,
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 0, 6, 6),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8EDF2")),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#243447")),
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#243447")),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                tb.Checked += TypeToggle_Changed;
                tb.Unchecked += TypeToggle_Changed;
                _typeToggles.Add(tb);
                TypesPanel.Children.Add(tb);
            }

            // Set default zone
            CmbZona.SelectedIndex = 0;

            // Timestamp auto-update
            _tsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tsTimer.Tick += (_, _) => TxtTimestamp.Text = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            _tsTimer.Start();
            TxtTimestamp.Text = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

            // Update sensor ID badge on text change
            TxtSensorId.TextChanged += (_, _) => SensorIdBadge.Text = $"ID: {TxtSensorId.Text}";
        }

        private void TypeToggle_Changed(object sender, RoutedEventArgs e)
        {
            foreach (var tb in _typeToggles)
            {
                if (tb.IsChecked == true)
                {
                    tb.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00BFA5"));
                    tb.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00BFA5"));
                }
                else
                {
                    tb.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#243447"));
                    tb.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#243447"));
                }
            }
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            string sensorId = TxtSensorId.Text.Trim();
            string ip = TxtGatewayIp.Text.Trim();
            if (!int.TryParse(TxtGatewayPort.Text.Trim(), out int port)) port = 8080;

            if (string.IsNullOrEmpty(sensorId))
            {
                AppendLog("ERR: Sensor ID vazio.", LogColor.Error);
                return;
            }

            // Recolher tipos selecionados na UI thread antes de entrar no Task.Run
            var selectedTypes = _typeToggles
                .Where(tb => tb.IsChecked == true)
                .Select(tb => tb.Content.ToString()!)
                .ToList();

            // Se nenhum tipo selecionado, selecionar todos por defeito
            if (selectedTypes.Count == 0)
            {
                foreach (var tb in _typeToggles)
                    tb.IsChecked = true;
                selectedTypes = _typeToggles.Select(tb => tb.Content.ToString()!).ToList();
                AppendLog("INFO: Nenhum tipo selecionado — a usar todos os tipos por defeito.", LogColor.Received);
            }

            BtnConnect.IsEnabled = false;
            BtnConnect.Content = "CONNECTING...";

            await Task.Run(() =>
            {
                try
                {
                    _sensor = new SensorClient(sensorId, ip, port);
                    _sensor.OnLogMessage += (msg, isSent) =>
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            AppendLog(isSent ? $"→ {msg}" : $"← {msg}",
                                      isSent ? LogColor.Sent : LogColor.Received);
                        });
                    };

                    _sensor.ConnectTcp();
                    string resp = _sensor.SendConnect();

                    if (!_sensor.IsConnected)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            UpdateConnectionStatus("FAILED", "#EF5350");
                            BtnConnect.IsEnabled = true;
                            BtnConnect.Content = "CONNECT TO GATEWAY";
                        });
                        return;
                    }

                    // Enviar REGISTER_TYPES imediatamente após CONNECT para não
                    // exceder o timeout de handshake (10s) do Gateway.
                    Application.Current.Dispatcher.Invoke(() =>
                        BtnConnect.Content = "REGISTERING...");

                    string regResp = _sensor.SendRegisterTypes(selectedTypes);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_sensor.IsOperational)
                        {
                            UpdateConnectionStatus($"OPERATIONAL — {_sensor.SensorId}", "#4CAF50");
                            BtnSendData.IsEnabled = true;
                            BtnVideoStream.IsEnabled = true;
                            BtnDisconnect.IsEnabled = true;
                            ChkHeartbeat.IsEnabled = true;
                            BtnConnect.Content = "CONNECTED ✓";
                            BtnRegisterTypes.Content = "REGISTERED ✓";
                            BtnRegisterTypes.IsEnabled = false;

                            CmbDataType.Items.Clear();
                            foreach (var t in selectedTypes)
                                CmbDataType.Items.Add(t);
                            if (CmbDataType.Items.Count > 0)
                                CmbDataType.SelectedIndex = 0;

                            // Ativar heartbeat automático para evitar timeout do gateway (15s)
                            ChkHeartbeat.IsChecked = true;
                            _sensor.StartHeartbeatAuto();
                            AppendLog("INFO: Auto-Heartbeat ativado (5s) — sensor mantém-se ativo.", LogColor.Received);
                        }
                        else
                        {
                            string detail = regResp == "ERR_TYPE_NOT_SUPPORTED"
                                ? $"ERR: Tipo não permitido para este sensor no sensors.csv. Verifica quais os tipos configurados e seleciona apenas esses. (Resposta: {regResp})"
                                : $"ERR: Falha ao registar tipos: {regResp}";
                            AppendLog(detail, LogColor.Error);
                            ResetConnectionLocal();
                        }
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AppendLog($"ERR: {ex.Message}", LogColor.Error);
                        ResetConnectionLocal();
                    });
                }
            });
        }

        private void BtnRegisterTypes_Click(object sender, RoutedEventArgs e)
        {
            if (_sensor == null || !_sensor.IsConnected) return;

            var selectedTypes = new List<string>();
            foreach (var tb in _typeToggles)
            {
                if (tb.IsChecked == true)
                    selectedTypes.Add(tb.Content.ToString()!);
            }

            if (selectedTypes.Count == 0)
            {
                AppendLog("ERR: Selecione pelo menos um tipo.", LogColor.Error);
                return;
            }

            Task.Run(() =>
            {
                try
                {
                    string resp = _sensor.SendRegisterTypes(selectedTypes);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_sensor.IsOperational)
                        {
                            UpdateConnectionStatus($"OPERATIONAL — {_sensor.SensorId}", "#4CAF50");
                            BtnSendData.IsEnabled = true;
                            BtnVideoStream.IsEnabled = true;
                            BtnDisconnect.IsEnabled = true;
                            ChkHeartbeat.IsEnabled = true;
                            BtnRegisterTypes.Content = "REGISTERED ✓";
                            BtnRegisterTypes.IsEnabled = false;

                            // Populate data type combo
                            CmbDataType.Items.Clear();
                            foreach (var t in selectedTypes)
                                CmbDataType.Items.Add(t);
                            if (CmbDataType.Items.Count > 0)
                                CmbDataType.SelectedIndex = 0;
                        }
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AppendLog($"ERR: {ex.Message}", LogColor.Error);
                        ResetConnectionLocal();
                    });
                }
            });
        }

        private void BtnSendData_Click(object sender, RoutedEventArgs e)
        {
            if (_sensor == null || !_sensor.IsOperational) return;

            string tipo = CmbDataType.SelectedItem?.ToString() ?? "";
            string valor = TxtValue.Text.Trim();
            string zona = (CmbZona.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ZONA_CENTRO";
            string ts = TxtTimestamp.Text;

            if (string.IsNullOrEmpty(tipo) || string.IsNullOrEmpty(valor)) return;

            Task.Run(() =>
            {
                try
                {
                    _sensor.SendData(tipo, valor, zona, ts);
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AppendLog($"ERR: {ex.Message}", LogColor.Error);
                        ResetConnectionLocal();
                    });
                }
            });
        }

        private void BtnVideoStream_Click(object sender, RoutedEventArgs e)
        {
            if (_sensor == null || !_sensor.IsOperational) return;
            if (!int.TryParse(TxtFrameCount.Text.Trim(), out int frames) || frames <= 0) frames = 10;
            if (!int.TryParse(TxtVideoPort.Text.Trim(), out int vPort)) vPort = 8081;

            BtnVideoStream.IsEnabled = false;
            BtnVideoStream.Content = "STREAMING...";

            Task.Run(() =>
            {
                try
                {
                    string result = _sensor.SendVideoStream(vPort, frames);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AppendLog(result, result.StartsWith("OK") ? LogColor.Received : LogColor.Error);
                        BtnVideoStream.IsEnabled = true;
                        BtnVideoStream.Content = "🎥  START VIDEO STREAM";
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AppendLog($"ERR: {ex.Message}", LogColor.Error);
                        ResetConnectionLocal();
                    });
                }
            });
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (_sensor == null) return;

            Task.Run(() =>
            {
                try
                {
                    _sensor.SendDisconnect();
                    _sensor.Dispose();
                    _sensor = null;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ResetConnectionLocal();
                    });
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        AppendLog($"ERR: {ex.Message}", LogColor.Error);
                        ResetConnectionLocal();
                    });
                }
            });
        }

        private void ChkHeartbeat_Changed(object sender, RoutedEventArgs e)
        {
            if (_sensor == null) return;
            if (ChkHeartbeat.IsChecked == true)
                _sensor.StartHeartbeatAuto();
            else
                _sensor.StopHeartbeatAuto();
        }

        private void CmbDataType_Changed(object sender, SelectionChangedEventArgs e)
        {
            string tipo = CmbDataType.SelectedItem?.ToString() ?? "";
            TxtUnit.Text = LiveDataItem.GetUnitForType(tipo);
        }

        private void UpdateConnectionStatus(string text, string colorHex)
        {
            ConnStatusText.Text = $"● {text}";
            ConnDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        }

        private void ResetConnectionLocal()
        {
            if (_sensor != null)
            {
                try { _sensor.Dispose(); } catch { }
                _sensor = null;
            }
            UpdateConnectionStatus("DISCONNECTED", "#EF5350");
            BtnConnect.IsEnabled = true;
            BtnConnect.Content = "CONNECT TO GATEWAY";
            BtnRegisterTypes.IsEnabled = false;
            BtnRegisterTypes.Content = "REGISTER TYPES";
            BtnSendData.IsEnabled = false;
            BtnVideoStream.IsEnabled = false;
            BtnDisconnect.IsEnabled = false;
            ChkHeartbeat.IsEnabled = false;
            ChkHeartbeat.IsChecked = false;
            BtnVideoStream.Content = "🎥  START VIDEO STREAM";
        }

        private enum LogColor { Sent, Received, Error }

        private void AppendLog(string text, LogColor color)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
            var run = new Run($"[{DateTime.Now:HH:mm:ss}]  {text}");
            run.Foreground = color switch
            {
                LogColor.Sent => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00BFA5")),
                LogColor.Received => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")),
                LogColor.Error => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF5350")),
                _ => new SolidColorBrush(Colors.White)
            };
            paragraph.Inlines.Add(run);
            LogDoc.Blocks.Add(paragraph);
            // Limit to 500 lines
            while (LogDoc.Blocks.Count > 500)
                LogDoc.Blocks.Remove(LogDoc.Blocks.FirstBlock);
            LogBox.ScrollToEnd();
        }
    }
}
