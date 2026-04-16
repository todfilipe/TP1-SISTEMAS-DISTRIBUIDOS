using System.Windows;
using System.Windows.Controls;

namespace OneHealthMonitor.Views
{
    public partial class GatewayView : UserControl
    {
        private readonly MainWindow _main;
        private int _gatewayCounter = 1;

        public GatewayView(MainWindow main)
        {
            InitializeComponent();
            _main = main;
            
            // Add initial gateway
            AddNewGatewayTab();
        }

        private void AddTab_Selected(object sender, RoutedEventArgs e)
        {
            if (AddTab.IsSelected)
            {
                AddNewGatewayTab();
            }
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
