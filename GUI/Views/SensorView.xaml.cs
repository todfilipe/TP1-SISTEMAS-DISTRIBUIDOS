using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace OneHealthMonitor.Views
{
    public partial class SensorView : UserControl
    {
        private readonly MainWindow _main;
        private int _sensorCounter = 1;
        private bool _isAdding;
        private bool _isClosing;

        public SensorView(MainWindow main)
        {
            InitializeComponent();
            _main = main;

            // Add initial sensor tab
            AddNewSensorTab();
        }

        private void AddTab_Selected(object sender, RoutedEventArgs e)
        {
            if (!AddTab.IsSelected || _isAdding || _isClosing) return;
            _isAdding = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { AddNewSensorTab(); }
                finally { _isAdding = false; }
            }), DispatcherPriority.Background);
        }

        private void AddNewSensorTab()
        {
            SensorTabs.Items.Remove(AddTab);

            string title = $"S{100 + _sensorCounter:D3}";

            var sensorControl = new SensorControl(_main);
            sensorControl.TxtSensorId.Text = title;

            // Placeholder tab — will set Header with close action after newTab variable exists
            var newTab = new TabItem
            {
                Style = (Style)FindResource("DarkTabItem"),
                Content = sensorControl
            };

            // Now wire the close-button with a reference to newTab
            newTab.Header = BuildTabHeader(title, () =>
            {
                _isClosing = true;

                // Selecionar o tab sensor anterior/seguinte antes de remover
                int idx = SensorTabs.Items.IndexOf(newTab);
                sensorControl.CloseAndDispose();
                SensorTabs.Items.Remove(newTab);

                // Ir para o sensor mais próximo (não para o tab "+")
                int sensorTabCount = SensorTabs.Items.Count - 1; // excluir AddTab
                if (sensorTabCount > 0)
                {
                    int target = Math.Min(idx, sensorTabCount - 1);
                    SensorTabs.SelectedIndex = target;
                }

                _isClosing = false;
            });

            SensorTabs.Items.Add(newTab);
            SensorTabs.Items.Add(AddTab);

            _sensorCounter++;
            SensorTabs.SelectedItem = newTab;
        }

        private static StackPanel BuildTabHeader(string title, System.Action onClose)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            var closeBtn = new Button
            {
                Content = "×",
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A9BB0")),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Fechar sensor"
            };

            closeBtn.Click += (_, _) => onClose();

            panel.Children.Add(closeBtn);
            return panel;
        }
    }
}
