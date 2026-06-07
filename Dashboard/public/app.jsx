// =====================================================================
// app.jsx — Composição final + router
// =====================================================================

function App() {
  const [theme, setTheme] = useTheme();
  const [route, nav] = useHashRoute();
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [alertCount, setAlertCount] = useState(0);

  const [toasts, setToasts] = useState([]);
  const criticalKeysRef = useRef(new Set());
  const firstAlertLoadRef = useRef(true);

  useEffect(() => {
    const load = () => {
      window.api.invalidateCache();
      window.api.getReadings().then((rs) => {
        const latestBySensorType = new Map();
        rs.forEach((r) => {
          const key = `${r.sensorId}|${r.type}`;
          const current = latestBySensorType.get(key);
          if (!current || new Date(r.timestamp) > new Date(current.timestamp)) {
            latestBySensorType.set(key, r);
          }
        });

        const sensorsInAlert = new Set();
        latestBySensorType.forEach((r) => {
          if (window.api.classifyAlert(r.type, r.value) !== 'NORMAL') {
            sensorsInAlert.add(r.sensorId);
          }
        });
        setAlertCount(sensorsInAlert.size);

        const criticals = Array.from(latestBySensorType.values())
          .filter((r) => window.api.classifyAlert(r.type, r.value) === 'CRITICAL');
        const nextKeys = new Set(criticals.map((r) => `${r.sensorId}|${r.type}|${r.timestamp}`));
        if (firstAlertLoadRef.current) {
          firstAlertLoadRef.current = false;
        } else {
          const newCriticals = criticals.filter((r) => !criticalKeysRef.current.has(`${r.sensorId}|${r.type}|${r.timestamp}`));
          if (newCriticals.length > 0) {
            const toast = {
              id: Date.now(),
              count: newCriticals.length,
              latest: newCriticals[0],
            };
            setToasts((items) => [toast, ...items].slice(0, 3));
            setTimeout(() => {
              setToasts((items) => items.filter((item) => item.id !== toast.id));
            }, 6500);
          }
        }
        criticalKeysRef.current = nextKeys;
      }).catch(() => setAlertCount(0));
    };
    load();
    const id = setInterval(load, 15000);
    return () => clearInterval(id);
  }, []);

  const titles = {
    '/':         { title: 'Dashboard',     subtitle: 'Visão geral da rede ambiental' },
    '/leituras': { title: 'Leituras',      subtitle: 'Explorador de séries temporais — todas as leituras dos sensores' },
    '/analises': { title: 'Análises',      subtitle: 'Histórico estatístico — médias, percentis, tendências' },
    '/nova':     { title: 'Nova análise',  subtitle: 'Calcular análise ou previsão sobre o histórico' },
    '/sensores': { title: 'Sensores',      subtitle: 'Saúde da frota distribuída' },
    '/sistema':  { title: 'Sistema',       subtitle: 'Topologia, gateways, servicos e persistencia' },
  };
  let routeKey = '/';
  if (route.startsWith('/analises')) routeKey = '/analises';
  else if (route.startsWith('/leituras')) routeKey = '/leituras';
  else if (route.startsWith('/nova')) routeKey = '/nova';
  else if (route.startsWith('/sensores')) routeKey = '/sensores';
  else if (route.startsWith('/sistema')) routeKey = '/sistema';
  const { title, subtitle } = titles[routeKey];

  // Página
  let page;
  if (routeKey === '/leituras') page = <ReadingsPage nav={nav} route={route} />;
  else if (routeKey === '/analises') page = <AnalysesPage nav={nav} route={route} />;
  else if (routeKey === '/nova') page = <ForecastPage nav={nav} />;
  else if (routeKey === '/sensores') page = <SensorsPage />;
  else if (routeKey === '/sistema') page = <SystemPage />;
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
        <ToastStack toasts={toasts} onDismiss={(id) => setToasts((items) => items.filter((item) => item.id !== id))} />
        <footer className="px-4 lg:px-8 py-4 text-[11px] text-ink-400 dark:text-ink-500 border-t border-ink-100 dark:border-ink-800 flex items-center justify-between flex-wrap gap-2">
          <span className="font-mono">One Health · UTAD · Sistemas Distribuídos TP1</span>
          <span className="font-mono">© 2026 · Interface de visualização · build mock</span>
        </footer>
      </main>
    </div>
  );
}

ReactDOM.createRoot(document.getElementById('root')).render(<App />);
