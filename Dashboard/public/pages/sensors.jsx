// =====================================================================
// pages/sensors.jsx — Saúde da frota de sensores
// =====================================================================

function HeartbeatDot({ iso, estado }) {
  if (estado === 'desativado') {
    return <span className="inline-flex items-center gap-1.5 text-rose-600 dark:text-rose-300">
      <span className="w-2 h-2 rounded-full bg-rose-500" />offline
    </span>;
  }
  if (estado === 'manutencao') {
    return <span className="inline-flex items-center gap-1.5 text-amber-600 dark:text-amber-300">
      <span className="w-2 h-2 rounded-full bg-amber-500" />manutenção
    </span>;
  }
  const diff = Date.now() - new Date(iso).getTime();
  const mins = diff / 60000;
  if (mins < 60) {
    return <span className="inline-flex items-center gap-1.5 text-emerald-600 dark:text-emerald-300">
      <span className="relative inline-flex w-2 h-2">
        <span className="absolute inset-0 rounded-full bg-emerald-500 animate-ping opacity-75" />
        <span className="relative inline-block w-2 h-2 rounded-full bg-emerald-500" />
      </span>
      ativo
    </span>;
  }
  if (mins < 24 * 60) {
    return <span className="inline-flex items-center gap-1.5 text-ink-500 dark:text-ink-400">
      <span className="w-2 h-2 rounded-full bg-ink-400" />latente
    </span>;
  }
  return <span className="inline-flex items-center gap-1.5 text-rose-600 dark:text-rose-300">
    <span className="w-2 h-2 rounded-full bg-rose-500" />silencioso
  </span>;
}

function SensorDetailChart({ readings }) {
  const { LineChart, Line, ResponsiveContainer, XAxis, YAxis, Tooltip, CartesianGrid, Legend } = window.Recharts;
  const dark = document.documentElement.classList.contains('dark');
  const axisColor = dark ? '#7d8c97' : '#566873';
  const latest = readings.slice().sort((a, b) => new Date(b.timestamp) - new Date(a.timestamp)).slice(0, 80).reverse();
  const types = Array.from(new Set(latest.map((r) => r.type))).sort();
  const rows = latest.map((r) => ({ ts: new Date(r.timestamp).getTime(), [r.type]: r.value }));
  const colors = ['#236471', '#b45309', '#7a4e9e', '#5a8a5e', '#a13f5d', '#3d6ea0'];

  return (
    <div className="h-[260px]">
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={rows} margin={{ top: 10, right: 16, left: 0, bottom: 0 }}>
          <CartesianGrid vertical={false} />
          <XAxis
            dataKey="ts"
            type="number"
            domain={['dataMin', 'dataMax']}
            tick={{ fill: axisColor, fontSize: 11 }}
            axisLine={false}
            tickLine={false}
            tickFormatter={(t) => fmtTime(new Date(t).toISOString())}
          />
          <YAxis tick={{ fill: axisColor, fontSize: 11 }} axisLine={false} tickLine={false} width={48} />
          <Tooltip
            contentStyle={{
              background: dark ? '#1f262c' : '#fff',
              border: `1px solid ${dark ? '#2f3a43' : '#d6dde2'}`,
              borderRadius: 8,
              fontSize: 12,
            }}
            labelFormatter={(t) => fmtDateTime(new Date(t).toISOString())}
            formatter={(v, name) => [`${fmtNumber(v, name)}`, name]}
          />
          <Legend wrapperStyle={{ fontSize: 11 }} iconType="circle" />
          {types.map((type, i) => (
            <Line key={type} type="monotone" dataKey={type} stroke={colors[i % colors.length]} strokeWidth={1.8} dot={latest.length < 20 ? { r: 2 } : false} connectNulls isAnimationActive={false} />
          ))}
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

function SensorDetailPanel({ sensor, readings, loading, error, onClose, retry }) {
  if (!sensor) return null;
  const sensorReadings = readings ? readings.filter((r) => r.sensorId === sensor.sensorId) : [];
  const latestByType = {};
  sensorReadings.forEach((r) => {
    if (!latestByType[r.type] || new Date(r.timestamp) > new Date(latestByType[r.type].timestamp)) latestByType[r.type] = r;
  });
  const alerts = Object.values(latestByType).filter((r) => window.api.classifyAlert(r.type, r.value) !== 'NORMAL');
  const comparisons = Object.values(latestByType).map((r) => {
    const zoneRows = readings.filter((item) => item.zone === r.zone && item.type === r.type);
    const avg = zoneRows.reduce((sum, item) => sum + Number(item.value || 0), 0) / Math.max(1, zoneRows.length);
    const delta = Number(r.value) - avg;
    return { reading: r, avg, delta };
  });

  return (
    <Card>
      <div className="flex items-start justify-between gap-3 mb-4">
        <div>
          <div className="flex items-center gap-2 flex-wrap">
            <span className="font-mono text-[13px] font-semibold">{sensor.sensorId}</span>
            <ZoneTag zone={sensor.zone} size="sm" />
            <span className="text-[11px] text-ink-500 dark:text-ink-400">{sensor.totalReadings.toLocaleString('pt-PT')} leituras</span>
          </div>
          <h2 className="mt-1 text-lg font-semibold tracking-tight">Detalhe do sensor</h2>
        </div>
        <Button variant="ghost" icon={Icon.X} onClick={onClose}>Fechar</Button>
      </div>

      {loading ? <Loading label="A carregar detalhe do sensor..." /> : error ? <InlineError error={error} retry={retry} /> : (
        <div className="space-y-4">
          {sensorReadings.length === 0 ? (
            <Empty title="Sem leituras para este sensor" hint="Ainda nao ha historico disponivel." />
          ) : (
            <SensorDetailChart readings={sensorReadings} />
          )}

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <div>
              <SectionTitle sub="Ultimo valor por tipo em WARNING ou CRITICAL">Alertas ativos</SectionTitle>
              {alerts.length === 0 ? (
                <div className="text-sm text-ink-500 dark:text-ink-400 rounded-md bg-ink-50 dark:bg-ink-800/50 px-3 py-2">Sem alertas ativos.</div>
              ) : (
                <div className="space-y-2">
                  {alerts.map((r) => {
                    const level = window.api.classifyAlert(r.type, r.value);
                    return (
                      <div key={r.type} className="flex items-center justify-between gap-3 rounded-md bg-ink-50 dark:bg-ink-800/50 px-3 py-2">
                        <div className="flex items-center gap-2"><TypePill type={r.type} /><span className="num text-sm font-semibold">{fmtNumber(r.value, r.type)} {r.unit}</span></div>
                        <AlertBadge level={level} size="sm" />
                      </div>
                    );
                  })}
                </div>
              )}
            </div>

            <div>
              <SectionTitle sub="Ultima leitura do sensor contra a media da mesma zona/tipo">Comparacao com a zona</SectionTitle>
              <div className="space-y-2">
                {comparisons.map(({ reading, avg, delta }) => (
                  <div key={reading.type} className="grid grid-cols-[auto_1fr_auto] items-center gap-3 rounded-md bg-ink-50 dark:bg-ink-800/50 px-3 py-2">
                    <TypePill type={reading.type} />
                    <div className="min-w-0">
                      <div className="text-xs text-ink-500 dark:text-ink-400">media da zona: <span className="num">{fmtNumber(avg, reading.type)} {reading.unit}</span></div>
                      <div className="text-[10px] text-ink-400 font-mono">{fmtDateTime(reading.timestamp)}</div>
                    </div>
                    <div className={`num text-sm font-semibold ${delta >= 0 ? 'text-amber-700 dark:text-amber-300' : 'text-emerald-700 dark:text-emerald-300'}`}>
                      {delta >= 0 ? '+' : ''}{fmtNumber(delta, reading.type)}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      )}
    </Card>
  );
}

function SensorsPage() {
  const [sensors, setSensors] = useState(null);
  const [error, setError] = useState(null);
  const [readings, setReadings] = useState(null);
  const [detailError, setDetailError] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [selectedSensorId, setSelectedSensorId] = useState(null);
  const [query, setQuery] = useState('');
  const [estadoFilter, setEstadoFilter] = useState(null);
  const [sort, setSort] = useState({ key: 'sensorId', direction: 'asc' });
  const [lastUpdated, setLastUpdated] = useState(null);
  const [refreshing, setRefreshing] = useState(false);
  const loadSensors = ({ silent = false } = {}) => {
    if (!silent) setRefreshing(true);
    setError(null);
    window.api.getSensors()
      .then((data) => {
        setSensors(data);
        setLastUpdated(new Date());
      })
      .catch((err) => {
        setError(err);
      })
      .finally(() => {
        if (!silent) setRefreshing(false);
      });
  };
  useEffect(() => {
    loadSensors();
    const id = setInterval(() => loadSensors({ silent: true }), 30000);
    return () => clearInterval(id);
  }, []);
  const loadSensorReadings = () => {
    if (!selectedSensorId) return;
    setDetailLoading(true);
    setDetailError(null);
    window.api.invalidateCache();
    window.api.getReadings()
      .then(setReadings)
      .catch(setDetailError)
      .finally(() => setDetailLoading(false));
  };
  useEffect(() => {
    if (selectedSensorId) loadSensorReadings();
  }, [selectedSensorId]);
  if (error && !sensors) return <ErrorState error={error} retry={loadSensors} />;
  if (!sensors) return <Loading />;
  const estadoOptions = window.api.ESTADOS.map((estado) => ({ value: estado, label: estado }));

  // Agrupar por sensorId (cada sensor pode ter vários tipos)
  const grouped = {};
  sensors.forEach((s) => {
    if (!grouped[s.sensorId]) grouped[s.sensorId] = { sensorId: s.sensorId, zone: s.zone, estado: s.estado, types: [], totalReadings: 0, lastReadingAt: s.lastReadingAt, firstReadingAt: s.firstReadingAt };
    const g = grouped[s.sensorId];
    g.types.push({ type: s.type, totalReadings: s.totalReadings, lastReadingAt: s.lastReadingAt });
    g.totalReadings += s.totalReadings;
    if (new Date(s.lastReadingAt) > new Date(g.lastReadingAt)) g.lastReadingAt = s.lastReadingAt;
    if (new Date(s.firstReadingAt) < new Date(g.firstReadingAt)) g.firstReadingAt = s.firstReadingAt;
  });
  let rows = Object.values(grouped);
  if (query) {
    const q = query.toLowerCase();
    rows = rows.filter((r) =>
      r.sensorId.toLowerCase().includes(q) ||
      r.zone.toLowerCase().includes(q) ||
      window.api.ZONA_LABEL[r.zone].toLowerCase().includes(q) ||
      r.types.some((t) => t.type.toLowerCase().includes(q))
    );
  }
  if (estadoFilter) rows = rows.filter((r) => r.estado === estadoFilter);
  rows = sortRows(rows, sort, {
    zoneLabel: (r) => window.api.ZONA_LABEL[r.zone] || r.zone,
    typesLabel: (r) => r.types.map((t) => t.type).sort().join(', '),
    heartbeat: (r) => r.lastReadingAt,
  });

  const stats = {
    total: Object.keys(grouped).length,
    ativos: Object.values(grouped).filter((s) => s.estado === 'ativo').length,
    manutencao: Object.values(grouped).filter((s) => s.estado === 'manutencao').length,
    desativados: Object.values(grouped).filter((s) => s.estado === 'desativado').length,
  };
  const selectedSensor = selectedSensorId ? grouped[selectedSensorId] : null;

  return (
    <div className="px-4 lg:px-8 py-6 space-y-5">
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <KPI label="Total" value={stats.total} hint="sensores registados" accent="ink" icon={Icon.Sensors} />
        <KPI label="Ativos" value={stats.ativos} hint="a publicar para o gateway" accent="moss" icon={Icon.Pulse} />
        <KPI label="Em manutenção" value={stats.manutencao} hint="dados em pausa" accent="amber" icon={Icon.Sensors} />
        <KPI label="Desativados" value={stats.desativados} hint="fora da rede" accent="rose" icon={Icon.Sensors} />
      </div>

      <SensorDetailPanel
        sensor={selectedSensor}
        readings={readings || []}
        loading={detailLoading || (selectedSensor && readings == null && !detailError)}
        error={detailError}
        retry={loadSensorReadings}
        onClose={() => setSelectedSensorId(null)}
      />

      <Card padding="p-0">
        <div className="flex items-center justify-between gap-3 px-5 py-3 border-b border-ink-100 dark:border-ink-800 flex-wrap">
          <h2 className="text-[13px] font-semibold uppercase tracking-[0.08em] text-ink-500 dark:text-ink-400">Frota de sensores</h2>
          <div className="flex items-center gap-2">
            <RefreshMeta lastUpdated={lastUpdated} refreshing={refreshing} />
            <Button variant="outline" icon={Icon.Refresh} onClick={() => loadSensors()} disabled={refreshing}>Atualizar</Button>
            <div className="relative">
              <Icon.Search width={14} height={14} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-ink-400" />
              <input
                className={`${inputClass} pl-8`}
                style={{ paddingLeft: '2rem', width: '220px' }}
                placeholder="Procurar sensor, zona, tipo…"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
              />
            </div>
            <Select value={estadoFilter} onChange={setEstadoFilter} options={estadoOptions} placeholder="Estado" className="!w-[140px]" />
          </div>
        </div>
        {error && <div className="px-5 pt-3"><InlineError error={error} retry={() => loadSensors()} /></div>}
        {rows.length === 0 ? (
          <Empty title="Nenhum sensor encontrado" hint="Tente outro termo." />
        ) : (
          <div className="overflow-x-auto scroll-thin">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-[11px] font-semibold uppercase tracking-wide text-ink-500 dark:text-ink-400 bg-ink-50 dark:bg-ink-950/40">
                  <SortHeader label="Sensor" sortKey="sensorId" sort={sort} onSort={setSort} />
                  <SortHeader label="Zona" sortKey="zoneLabel" sort={sort} onSort={setSort} />
                  <SortHeader label="Tipos monitorizados" sortKey="typesLabel" sort={sort} onSort={setSort} />
                  <SortHeader label="Estado" sortKey="estado" sort={sort} onSort={setSort} />
                  <SortHeader label="Heartbeat" sortKey="heartbeat" sort={sort} onSort={setSort} />
                  <SortHeader label="Ultima leitura" sortKey="lastReadingAt" sort={sort} onSort={setSort} />
                  <SortHeader label="Total" sortKey="totalReadings" sort={sort} onSort={setSort} align="right" />
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-100 dark:divide-ink-800">
                {rows.map((r) => {
                  const estadoColor = r.estado === 'ativo'
                    ? 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-300 ring-emerald-200 dark:ring-emerald-500/30'
                    : r.estado === 'manutencao'
                    ? 'bg-amber-50 text-amber-800 dark:bg-amber-500/10 dark:text-amber-300 ring-amber-200 dark:ring-amber-500/30'
                    : 'bg-rose-50 text-rose-700 dark:bg-rose-500/10 dark:text-rose-300 ring-rose-200 dark:ring-rose-500/30';
                  return (
                    <tr key={r.sensorId} className="hover:bg-ink-50/60 dark:hover:bg-ink-800/30">
                      <td className="px-4 py-3">
                        <button
                          type="button"
                          onClick={() => {
                            setReadings(null);
                            setDetailLoading(true);
                            if (selectedSensorId === r.sensorId) loadSensorReadings();
                            setSelectedSensorId(r.sensorId);
                          }}
                          className="text-left rounded-md -mx-1 px-1 py-0.5 hover:bg-petrol-50 dark:hover:bg-petrol-900/30 focus:outline-none focus:ring-2 focus:ring-petrol-500/30"
                        >
                          <div className="font-mono text-[13px] font-semibold text-petrol-700 dark:text-petrol-300">{r.sensorId}</div>
                          <div className="text-[10px] text-ink-400 dark:text-ink-500 font-mono">desde {fmtDateTime(r.firstReadingAt).split(' ')[0]}</div>
                        </button>
                      </td>
                      <td className="px-4 py-3"><ZoneTag zone={r.zone} size="sm" /></td>
                      <td className="px-4 py-3">
                        <div className="flex flex-wrap gap-1">
                          {r.types.map((t) => (
                            <span key={t.type} className="inline-flex items-center gap-1">
                              <TypePill type={t.type} />
                              <span className="text-[10px] font-mono text-ink-500">{t.totalReadings}</span>
                            </span>
                          ))}
                        </div>
                      </td>
                      <td className="px-4 py-3">
                        <span className={`inline-flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide ring-1 px-2 py-0.5 rounded-full ${estadoColor}`}>
                          {r.estado}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-[12px]"><HeartbeatDot iso={r.lastReadingAt} estado={r.estado} /></td>
                      <td className="px-4 py-3">
                        <div className="font-mono text-[12px] text-ink-700 dark:text-ink-200">{fmtDateTime(r.lastReadingAt)}</div>
                        <div className="text-[10px] text-ink-500 dark:text-ink-400">{timeAgo(r.lastReadingAt)}</div>
                      </td>
                      <td className="px-4 py-3 text-right num font-semibold">{r.totalReadings.toLocaleString('pt-PT')}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  );
}

window.SensorsPage = SensorsPage;
