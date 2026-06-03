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

function SensorsPage() {
  const [sensors, setSensors] = useState(null);
  const [query, setQuery] = useState('');
  const [estadoFilter, setEstadoFilter] = useState(null);
  useEffect(() => {
    const load = () => {
      window.api.invalidateCache();
      window.api.getSensors().then(setSensors);
    };
    load();
    const id = setInterval(load, 30000);
    return () => clearInterval(id);
  }, []);
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
  let rows = Object.values(grouped).sort((a,b) => a.sensorId.localeCompare(b.sensorId));
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

  const stats = {
    total: Object.keys(grouped).length,
    ativos: Object.values(grouped).filter((s) => s.estado === 'ativo').length,
    manutencao: Object.values(grouped).filter((s) => s.estado === 'manutencao').length,
    desativados: Object.values(grouped).filter((s) => s.estado === 'desativado').length,
  };

  return (
    <div className="px-4 lg:px-8 py-6 space-y-5">
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <KPI label="Total" value={stats.total} hint="sensores registados" accent="ink" icon={Icon.Sensors} />
        <KPI label="Ativos" value={stats.ativos} hint="a publicar para o gateway" accent="moss" icon={Icon.Pulse} />
        <KPI label="Em manutenção" value={stats.manutencao} hint="dados em pausa" accent="amber" icon={Icon.Sensors} />
        <KPI label="Desativados" value={stats.desativados} hint="fora da rede" accent="rose" icon={Icon.Sensors} />
      </div>

      <Card padding="p-0">
        <div className="flex items-center justify-between gap-3 px-5 py-3 border-b border-ink-100 dark:border-ink-800 flex-wrap">
          <h2 className="text-[13px] font-semibold uppercase tracking-[0.08em] text-ink-500 dark:text-ink-400">Frota de sensores</h2>
          <div className="flex items-center gap-2">
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
        {rows.length === 0 ? (
          <Empty title="Nenhum sensor encontrado" hint="Tente outro termo." />
        ) : (
          <div className="overflow-x-auto scroll-thin">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-[11px] font-semibold uppercase tracking-wide text-ink-500 dark:text-ink-400 bg-ink-50 dark:bg-ink-950/40">
                  <th className="px-4 py-2.5">Sensor</th>
                  <th className="px-4 py-2.5">Zona</th>
                  <th className="px-4 py-2.5">Tipos monitorizados</th>
                  <th className="px-4 py-2.5">Estado</th>
                  <th className="px-4 py-2.5">Heartbeat</th>
                  <th className="px-4 py-2.5">Última leitura</th>
                  <th className="px-4 py-2.5 text-right">Total</th>
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
                        <div className="font-mono text-[13px] font-semibold">{r.sensorId}</div>
                        <div className="text-[10px] text-ink-400 dark:text-ink-500 font-mono">desde {fmtDateTime(r.firstReadingAt).split(' ')[0]}</div>
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
