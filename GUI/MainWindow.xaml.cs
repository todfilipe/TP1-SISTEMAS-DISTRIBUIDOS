using System.Windows;
using System.Windows.Controls;
using OneHealthMonitor.Core;
using OneHealthMonitor.Views;

namespace OneHealthMonitor
{
    public partial class MainWindow : Window
    {
        // Shared services — in-process
        public ServidorService ServidorService { get; } = new ServidorService();
        
        public System.Collections.Generic.List<GatewayService> ActiveGateways { get; } = new();
        public event System.Action<LiveDataItem>? OnGlobalDataReceived;
        public event System.Action<SensorConfig>? OnGlobalSensorUpdated;

        public void RegisterGateway(GatewayService gw)
        {
            ActiveGateways.Add(gw);
            gw.OnDataReceived += item => OnGlobalDataReceived?.Invoke(item);
            gw.OnSensorUpdated += cfg => OnGlobalSensorUpdated?.Invoke(cfg);
        }

        // Views
        private DashboardView? _dashboardView;
        private SensorView? _sensorView;
        private GatewayView? _gatewayView;
        private ServidorView? _servidorView;

        private Button _activeButton;

        public MainWindow()
        {
            InitializeComponent();
            _activeButton = BtnDashboard;
            Loaded += (_, _) => NavigateTo("Dashboard");
        }

        private void NavigateTo(string page)
        {
            switch (page)
            {
                case "Dashboard":
                    _dashboardView ??= new DashboardView(this);
                    ContentArea.Content = _dashboardView;
                    SetActiveButton(BtnDashboard);
                    break;
                case "Sensor":
                    _sensorView ??= new SensorView(this);
                    ContentArea.Content = _sensorView;
                    SetActiveButton(BtnSensor);
                    break;
                case "Gateway":
                    _gatewayView ??= new GatewayView(this);
                    ContentArea.Content = _gatewayView;
                    SetActiveButton(BtnGateway);
                    break;
                case "Servidor":
                    _servidorView ??= new ServidorView(this);
                    ContentArea.Content = _servidorView;
                    SetActiveButton(BtnServidor);
                    break;
            }
        }

        private void SetActiveButton(Button btn)
        {
            // Reset all to inactive style
            BtnDashboard.Style = (Style)FindResource("SidebarButton");
            BtnSensor.Style = (Style)FindResource("SidebarButton");
            BtnGateway.Style = (Style)FindResource("SidebarButton");
            BtnServidor.Style = (Style)FindResource("SidebarButton");

            // Set active
            btn.Style = (Style)FindResource("SidebarButtonActive");
            _activeButton = btn;
        }

        private void BtnDashboard_Click(object sender, RoutedEventArgs e) => NavigateTo("Dashboard");
        private void BtnSensor_Click(object sender, RoutedEventArgs e) => NavigateTo("Sensor");
        private void BtnGateway_Click(object sender, RoutedEventArgs e) => NavigateTo("Gateway");
        private void BtnServidor_Click(object sender, RoutedEventArgs e) => NavigateTo("Servidor");

        protected override void OnClosed(System.EventArgs e)
        {
            foreach (var gw in ActiveGateways)
            {
                gw.Stop();
            }
            ServidorService.Stop();
            base.OnClosed(e);
        }
    }
}
