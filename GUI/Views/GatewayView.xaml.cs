using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OneHealthMonitor.Views
{
    public partial class GatewayView : UserControl
    {
        private readonly MainWindow _main;
        private int _gatewayCounter = 1;
        private bool _isAdding;

        public GatewayView(MainWindow main)
        {
            InitializeComponent();
            _main = main;

            // Add initial gateway
            AddNewGatewayTab();
        }

        private void AddTab_Selected(object sender, RoutedEventArgs e)
        {
            if (!AddTab.IsSelected || _isAdding) return;
            _isAdding = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { AddNewGatewayTab(); }
                finally { _isAdding = false; }
            }), DispatcherPriority.Background);
        }

        private void AddNewGatewayTab()
        {
            // Remove the '+' tab temporarily
            GatewayTabs.Items.Remove(AddTab);

            string title = $"GW0{_gatewayCounter}";
            
            var newTab = new TabItem
            {
                Header = title,
                Style = (Style)FindResource("DarkTabItem")
            };

            var gwControl = new GatewayControl(_main);
            // Default config for multi instances to avoid port conflicts out of the box
            gwControl.TxtGwId.Text = title;
            gwControl.TxtSensorPort.Text = (8080 + (_gatewayCounter - 1) * 2).ToString();
            gwControl.TxtVideoPort.Text = (8081 + (_gatewayCounter - 1) * 2).ToString();
            
            newTab.Content = gwControl;

            GatewayTabs.Items.Add(newTab);
            GatewayTabs.Items.Add(AddTab);

            _gatewayCounter++;
            
            // Select the newly added tab
            GatewayTabs.SelectedItem = newTab;
        }
    }
}
