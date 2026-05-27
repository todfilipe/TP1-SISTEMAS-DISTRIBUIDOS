// =====================================================================
// app.jsx — Composição final + router
// =====================================================================

function App() {
  const [theme, setTheme] = useTheme();
  const [route, nav] = useHashRoute();
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [alertCount, setAlertCount] = useState(0);

  useEffect(() => {
    window.api.getReadings().then((rs) => {
      const n = rs.filter((r) => window.api.classifyAlert(r.type, r.value) !== 'NORMAL').length;
      setAlertCount(n);
    });
  }, []);

  const titles = {
    '/':         { title: 'Dashboard',     subtitle: 'Visão geral da rede ambiental' },
    '/leituras': { title: 'Leituras',      subtitle: 'Explorador de séries temporais — todas as leituras dos sensores' },
    '/analises': { title: 'Análises',      subtitle: 'Histórico estatístico — médias, percentis, tendências' },
    '/nova':     { title: 'Nova análise',  subtitle: 'Calcular análise ou previsão sobre o histórico' },
    '/sensores': { title: 'Sensores',      subtitle: 'Saúde da frota distribuída' },
  };
  let routeKey = '/';
  if (route.startsWith('/analises')) routeKey = '/analises';
  else if (route.startsWith('/leituras')) routeKey = '/leituras';
  else if (route.startsWith('/nova')) routeKey = '/nova';
  else if (route.startsWith('/sensores')) routeKey = '/sensores';
  const { title, subtitle } = titles[routeKey];

  // Página
  let page;
  if (routeKey === '/leituras') page = <ReadingsPage nav={nav} />;
  else if (routeKey === '/analises') page = <AnalysesPage nav={nav} route={route} />;
  else if (routeKey === '/nova') page = <ForecastPage nav={nav} />;
  else if (routeKey === '/sensores') page = <SensorsPage />;
  else page = <DashboardPage nav={nav} />;

  return (
    <div className="min-h-screen flex bg-ink-50 dark:bg-ink-950">
      <Sidebar route={route} nav={nav} open={sidebarOpen} setOpen={setSidebarOpen} />
      <main className="flex-1 min-w-0 flex flex-col">
        <Topbar
          title={title}
          subtitle={subtitle}
          onMenu={() => setSidebarOpen(true)}
          theme={theme}
          setTheme={setTheme}
          alerts={alertCount}
        />
        <div className="flex-1 min-w-0">
          {page}
        </div>
        <footer className="px-4 lg:px-8 py-4 text-[11px] text-ink-400 dark:text-ink-500 border-t border-ink-100 dark:border-ink-800 flex items-center justify-between flex-wrap gap-2">
          <span className="font-mono">One Health · UTAD · Sistemas Distribuídos TP1</span>
          <span className="font-mono">© 2026 · Interface de visualização · build mock</span>
        </footer>
      </main>
    </div>
  );
}

ReactDOM.createRoot(document.getElementById('root')).render(<App />);
