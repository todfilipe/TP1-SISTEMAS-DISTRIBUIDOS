using System.Windows;
using System.Windows.Controls;

namespace OneHealthMonitor.Views
{
    public partial class SensorView : UserControl
    {
        private readonly MainWindow _main;
        private int _sensorCounter = 1;

        public SensorView(MainWindow main)
        {
            InitializeComponent();
            _main = main;
            
            // Add initial sensor
            AddNewSensorTab();
        }

        private void AddTab_Selected(object sender, RoutedEventArgs e)
        {
            if (AddTab.IsSelected)
            {
                AddNewSensorTab();
            }
        }

        private void AddNewSensorTab()
        {
            SensorTabs.Items.Remove(AddTab);

            string title = $"S10{_sensorCounter}";
            
            var newTab = new TabItem
            {
                Header = title,
                Style = (Style)FindResource("DarkTabItem")
            };

            var sensorControl = new SensorControl(_main);
            sensorControl.TxtSensorId.Text = title;
            
            newTab.Content = sensorControl;

            SensorTabs.Items.Add(newTab);
            SensorTabs.Items.Add(AddTab);

            _sensorCounter++;
            SensorTabs.SelectedItem = newTab;
        }
    }
}
