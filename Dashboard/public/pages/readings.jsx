// =====================================================================
// pages/readings.jsx — Explorador de séries temporais
// =====================================================================

function ReadingsChart({ data, type, filters }) {
  const { LineChart, Line, ResponsiveContainer, XAxis, YAxis, Tooltip, CartesianGrid, Legend } = window.Recharts;
  const dark = document.documentElement.classList.contains('dark');
  const axisColor = dark ? '#7d8c97' : '#566873';

  // Reagrupar por sensor para multi-série
  const sensors = Array.from(new Set(data.map((d) => d.sensorId))).sort();

  // Calcular intervalo de tempo para definir o tamanho dinâmico do bucket (binMs)
  const timestamps = data.map((d) => new Date(d.timestamp).getTime());
  const parsedFrom = filters?.from ? window.api.parseInputDate(filters.from)?.getTime() : null;
  const parsedTo = filters?.to ? window.api.parseInputDate(filters.to)?.getTime() : null;

  const minTs = parsedFrom != null ? parsedFrom : (timestamps.length > 0 ? Math.min(...timestamps) : Date.now() - 3600000);
  const maxTs = parsedTo != null ? parsedTo : (timestamps.length > 0 ? Math.max(...timestamps) : Date.now());
  const diffMs = maxTs - minTs;

  let binMs = 30 * 60 * 1000; // Padrão: 30 minutos
  if (diffMs < 5 * 60 * 1000) {
    binMs = 5 * 1000; // < 5 min -> buckets de 5 segundos
  } else if (diffMs < 30 * 60 * 1000) {
    binMs = 30 * 1000; // < 30 min -> buckets de 30 segundos
  } else if (diffMs < 2 * 60 * 60 * 1000) {
    binMs = 1 * 60 * 1000; // < 2 horas -> buckets de 1 minuto
  } else if (diffMs < 12 * 60 * 60 * 1000) {
    binMs = 5 * 60 * 1000; // < 12 horas -> buckets de 5 minutos
  } else if (diffMs < 24 * 60 * 60 * 1000) {
    binMs = 10 * 60 * 1000; // < 24 horas -> buckets de 10 minutos
  }

  const map = new Map();
  data.forEach((d) => {
    const ts = new Date(d.timestamp).getTime();
    const bucket = Math.round(ts / binMs) * binMs;
    if (!map.has(bucket)) map.set(bucket, { ts: bucket });
    const row = map.get(bucket);
    // média entre leituras de mesmo sensor no mesmo bucket
    if (row[d.sensorId] == null) row[d.sensorId] = { sum: d.value, n: 1 };
    else { row[d.sensorId].sum += d.value; row[d.sensorId].n += 1; }
  });
  const rows = Array.from(map.values())
    .sort((a, b) => a.ts - b.ts)
    .map((row) => {
      const out = { ts: row.ts };
      sensors.forEach((s) => { if (row[s]) out[s] = +(row[s].sum / row[s].n).toFixed(2); });
      return out;
    });

  const COLORS = ['#236471','#5a8a5e','#b07c39','#7a4e9e','#a13f5d','#3d6ea0','#8c5a3a','#588b8b','#a89f31','#6f7b80'];
  const unit = data[0]?.unit || '';

  const sparse = rows.length < 16;

  return (
    <div className="h-[320px]">
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={rows} margin={{ top: 10, right: 16, left: 0, bottom: 0 }}>
          <CartesianGrid vertical={false} />
          <XAxis
            dataKey="ts"
            type="number"
            domain={[minTs, maxTs]}
            tick={{ fill: axisColor, fontSize: 11 }}
            axisLine={false}
            tickLine={false}
            tickFormatter={(t) => {
              try {
                const iso = new Date(t).toISOString();
                if (diffMs < 5 * 60 * 1000) {
                  return new Date(t).toLocaleTimeString('pt-PT', { minute: '2-digit', second: '2-digit', hour12: false });
                } else if (diffMs < 12 * 60 * 60 * 1000) {
                  return fmtTime(iso);
                }
                const dateStr = fmtDateTime(iso).split(' ')[0]; // DD/MM/AAAA
                const timeStr = fmtTime(iso); // HH:MM
                const dayMonth = dateStr.slice(0, 5); // DD/MM
                return `${dayMonth} ${timeStr}`;
              } catch (e) {
                return '—';
              }
            }}
          />
          <YAxis
            tick={{ fill: axisColor, fontSize: 11 }}
            axisLine={false}
            tickLine={false}
            width={48}
            tickFormatter={(v) => (type === 'LUZ' ? Math.round(v).toLocaleString('pt-PT') : Number(v).toFixed(1))}
          />
          <Tooltip
            contentStyle={{
              background: dark ? '#1f262c' : '#fff',
              border: `1px solid ${dark ? '#2f3a43' : '#d6dde2'}`,
              borderRadius: 8,
              fontSize: 12,
            }}
            labelFormatter={(t) => fmtDateTime(new Date(t).toISOString())}
            formatter={(v, name) => [`${fmtNumber(v, type)} ${unit}`, name]}
          />
          <Legend wrapperStyle={{ fontSize: 11 }} iconType="circle" />
          {sensors.map((s, i) => (
            <Line
              key={s}
              type="monotone"
              dataKey={s}
              stroke={COLORS[i % COLORS.length]}
              strokeWidth={1.75}
              dot={sparse ? { r: 2 } : false}
              activeDot={{ r: 4 }}
              connectNulls
              isAnimationActive={false}
              name={s}
            />
          ))}
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

function ReadingsPage({ nav, route }) {
  const [allReadings, setAllReadings] = useState(null);
  const [error, setError] = useState(null);
  const routeAlert = (() => {
    const query = String(route || '').split('?')[1] || '';
    return new URLSearchParams(query).get('alert') || null;
  })();
  const [filters, setFilters] = useState({ sensorId: null, zone: null, type: null, alert: routeAlert, from: '', to: '' });
  const [page, setPage] = useState(1);
  const [sort, setSort] = useState({ key: 'timestamp', direction: 'desc' });
  const [lastUpdated, setLastUpdated] = useState(null);
  const [refreshing, setRefreshing] = useState(false);
  const pageSize = 25;
  const loadReadings = ({ silent = false, currentFilters = filters } = {}) => {
    if (!silent) setRefreshing(true);
    setError(null);
    window.api.invalidateCache();
    window.api.getReadings(currentFilters)
      .then((data) => {
        setAllReadings(data);
        setLastUpdated(new Date());
      })
      .catch(setError)
      .finally(() => {
        if (!silent) setRefreshing(false);
      });
  };

  useEffect(() => {
    loadReadings({ silent: false, currentFilters: filters });
    const id = setInterval(() => loadReadings({ silent: true, currentFilters: filters }), 15000);
    return () => clearInterval(id);
  }, [filters.sensorId, filters.zone, filters.type, filters.from, filters.to]);

  useEffect(() => {
    setFilters((f) => ({ ...f, alert: routeAlert }));
  }, [routeAlert]);

  const filtered = useMemo(() => {
    if (!allReadings) return [];
    const rows = allReadings.filter((r) => {
      if (filters.sensorId && r.sensorId !== filters.sensorId) return false;
      if (filters.zone && r.zone !== filters.zone) return false;
      if (filters.type && r.type !== filters.type) return false;
      if (filters.alert) {
        const level = window.api.classifyAlert(r.type, r.value);
        if (filters.alert === 'active' && level === 'NORMAL') return false;
        if (filters.alert !== 'active' && level !== filters.alert) return false;
      }
      if (filters.from) {
        const fromDate = window.api.parseInputDate(filters.from);
        if (fromDate && new Date(r.timestamp) < fromDate) return false;
      }
      if (filters.to) {
        const toDate = window.api.parseInputDate(filters.to);
        if (toDate && new Date(r.timestamp) > toDate) return false;
      }
      return true;
    });
    return sortRows(rows, sort, {
      zoneLabel: (r) => window.api.ZONA_LABEL[r.zone] || r.zone,
      alert: (r) => ({ NORMAL: 0, WARNING: 1, CRITICAL: 2 }[window.api.classifyAlert(r.type, r.value)] ?? 0),
    });
  }, [allReadings, filters, sort]);

  useEffect(() => { setPage(1); }, [filters, sort]);

  const sensorIds = allReadings ? Array.from(new Set(allReadings.map((r) => r.sensorId))).sort() : [];

  const clearFilters = () => {
    setFilters({ sensorId: null, zone: null, type: null, alert: null, from: '', to: '' });
    if (routeAlert) nav('/leituras');
  };
  const applyDatePreset = (range) => setFilters((f) => ({ ...f, ...range }));

  const exportCSV = () => {
    const header = ['sensorId','zone','type','value','unit','timestamp','gatewayId','originalMessageFormat'];
    const rows = filtered.map((r) => header.map((h) => r[h]).join(','));
    const csv = [header.join(','), ...rows].join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `leituras_${new Date().toISOString().slice(0,10)}.csv`;
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  };

  if (error && !allReadings) return <ErrorState error={error} retry={loadReadings} />;
  if (!allReadings) return <Loading />;

  const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize));
  const pageData = filtered.slice((page - 1) * pageSize, page * pageSize);

  const hasActiveFilters = filters.sensorId || filters.zone || filters.type || filters.alert || filters.from || filters.to;

  // Para o gráfico: se nenhum tipo selecionado, usar o tipo mais frequente nos filtrados
  const chartType = filters.type || (() => {
    const counts = {};
    filtered.forEach((r) => { counts[r.type] = (counts[r.type] || 0) + 1; });
    return Object.entries(counts).sort((a,b) => b[1]-a[1])[0]?.[0] || 'TEMP';
  })();

  const chartData = filtered.filter((r) => {
    if (r.type !== chartType) return false;
    // Se não houver sensor selecionado, mostrar apenas os agregados no gráfico
    if (!filters.sensorId && !r.sensorId.startsWith("AGREGADO_")) return false;
    return true;
  });

  return (
    <div className="px-4 lg:px-8 py-6 space-y-5">
      {/* Filters */}
      <Card padding="p-4">
        <div className="grid grid-cols-2 md:grid-cols-4 lg:grid-cols-7 gap-3 items-end">
          <Field label="Sensor">
            <Select value={filters.sensorId} onChange={(v) => setFilters((f) => ({ ...f, sensorId: v }))} options={sensorIds} placeholder="Todos" />
          </Field>
          <Field label="Zona">
            <Select value={filters.zone} onChange={(v) => setFilters((f) => ({ ...f, zone: v }))} options={window.api.ZONAS.map((z) => ({ value: z, label: window.api.ZONA_LABEL[z] }))} placeholder="Todas" />
          </Field>
          <Field label="Tipo">
            <Select value={filters.type} onChange={(v) => setFilters((f) => ({ ...f, type: v }))} options={window.api.TIPOS} placeholder="Todos" />
          </Field>
          <Field label="Alerta">
            <Select
              value={filters.alert}
              onChange={(v) => setFilters((f) => ({ ...f, alert: v }))}
              options={[
                { value: 'active', label: 'WARNING + CRITICAL' },
                { value: 'WARNING', label: 'WARNING' },
                { value: 'CRITICAL', label: 'CRITICAL' },
              ]}
              placeholder="Todos"
            />
          </Field>
          <Field label="De">
            <Input type="datetime-local" value={filters.from} onChange={(e) => setFilters((f) => ({ ...f, from: e.target.value }))} />
          </Field>
          <Field label="Até">
            <Input type="datetime-local" value={filters.to} onChange={(e) => setFilters((f) => ({ ...f, to: e.target.value }))} />
          </Field>
          <div className="flex gap-2">
            <Button variant="outline" onClick={clearFilters} disabled={!hasActiveFilters}>Limpar</Button>
            <Button onClick={exportCSV} icon={Icon.Download} disabled={filtered.length === 0}>CSV</Button>
          </div>
        </div>
        <div className="mt-3 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
          <DatePresetButtons onApply={applyDatePreset} />
          <div className="flex items-center gap-2">
            <RefreshMeta lastUpdated={lastUpdated} refreshing={refreshing} />
            <Button variant="outline" icon={Icon.Refresh} onClick={() => loadReadings()} disabled={refreshing}>Atualizar</Button>
          </div>
        </div>
        {error && <div className="mt-3"><InlineError error={error} retry={() => loadReadings()} /></div>}
        <div className="mt-3 flex items-center gap-2 text-xs text-ink-500 dark:text-ink-400">
          <span className="font-mono">{filtered.length}</span>
          <span>leituras correspondem aos filtros</span>
          {hasActiveFilters && <span className="text-petrol-600 dark:text-petrol-300">·</span>}
          {filters.type && <TypePill type={filters.type} />}
          {filters.alert === 'active' && <span className="text-[10px] px-1.5 py-0.5 rounded bg-amber-50 text-amber-800 dark:bg-amber-500/10 dark:text-amber-300 font-semibold">WARNING + CRITICAL</span>}
          {filters.alert && filters.alert !== 'active' && <AlertBadge level={filters.alert} size="sm" />}
          {filters.zone && <ZoneTag zone={filters.zone} size="sm" />}
          {filters.sensorId && <span className="font-mono px-1.5 py-0.5 rounded bg-ink-100 dark:bg-ink-800">{filters.sensorId}</span>}
        </div>
      </Card>

      {/* Chart */}
      <Card>
        <SectionTitle sub={filters.type ? `Tipo: ${chartType} (${window.api.UNIDADES[chartType]})` : `Tipo predominante: ${chartType} — escolha um tipo no filtro para isolar`} action={
          chartData.length > 0 && <span className="text-[11px] text-ink-400 font-mono">{Array.from(new Set(chartData.map(d=>d.sensorId))).length} séries</span>
        }>Série temporal</SectionTitle>
        {chartData.length === 0
          ? <Empty title="Sem dados para o filtro atual" hint="Ajuste os filtros para visualizar séries." />
          : <ReadingsChart data={chartData} type={chartType} filters={filters} />}
      </Card>

      {/* Table */}
      <Card padding="p-0">
        <div className="flex items-center justify-between px-5 py-3 border-b border-ink-100 dark:border-ink-800">
          <h2 className="text-[13px] font-semibold uppercase tracking-[0.08em] text-ink-500 dark:text-ink-400">Tabela de leituras</h2>
          <div className="text-[11px] text-ink-500 dark:text-ink-400 font-mono">{filtered.length} reg.</div>
        </div>
        {filtered.length === 0 ? (
          <Empty title="Sem leituras" hint="Tente outros filtros." />
        ) : (
          <div className="overflow-x-auto scroll-thin">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-[11px] font-semibold uppercase tracking-wide text-ink-500 dark:text-ink-400 bg-ink-50 dark:bg-ink-950/40">
                  <SortHeader label="Sensor" sortKey="sensorId" sort={sort} onSort={setSort} />
                  <SortHeader label="Zona" sortKey="zoneLabel" sort={sort} onSort={setSort} />
                  <SortHeader label="Tipo" sortKey="type" sort={sort} onSort={setSort} />
                  <SortHeader label="Valor" sortKey="value" sort={sort} onSort={setSort} align="right" />
                  <SortHeader label="Unidade" sortKey="unit" sort={sort} onSort={setSort} />
                  <SortHeader label="Timestamp" sortKey="timestamp" sort={sort} onSort={setSort} />
                  <SortHeader label="Gateway" sortKey="gatewayId" sort={sort} onSort={setSort} />
                  <SortHeader label="Formato" sortKey="originalMessageFormat" sort={sort} onSort={setSort} />
                  <SortHeader label="Alerta" sortKey="alert" sort={sort} onSort={setSort} />
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-100 dark:divide-ink-800">
                {pageData.map((r, idx) => {
                  const lvl = window.api.classifyAlert(r.type, r.value);
                  return (
                    <tr key={idx} className="hover:bg-ink-50/60 dark:hover:bg-ink-800/30">
                      <td className="px-4 py-2 font-mono text-[12.5px] font-medium">{r.sensorId}</td>
                      <td className="px-4 py-2 text-ink-700 dark:text-ink-200">{window.api.ZONA_LABEL[r.zone]}</td>
                      <td className="px-4 py-2"><TypePill type={r.type} /></td>
                      <td className="px-4 py-2 text-right num font-semibold">{fmtNumber(r.value, r.type)}</td>
                      <td className="px-4 py-2 text-ink-500 dark:text-ink-400 font-mono text-[12px]">{r.unit}</td>
                      <td className="px-4 py-2 text-ink-600 dark:text-ink-300 font-mono text-[12px]">{fmtDateTime(r.timestamp)}</td>
                      <td className="px-4 py-2 text-ink-600 dark:text-ink-300 font-mono text-[12px]">{r.gatewayId}</td>
                      <td className="px-4 py-2 text-ink-500 dark:text-ink-400 font-mono text-[12px]">{r.originalMessageFormat}</td>
                      <td className="px-4 py-2"><AlertBadge level={lvl} size="sm" /></td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
        {/* Pagination */}
        {filtered.length > pageSize && (
          <div className="flex items-center justify-between px-5 py-3 border-t border-ink-100 dark:border-ink-800 text-sm">
            <div className="text-xs text-ink-500 dark:text-ink-400">
              Página <span className="font-mono">{page}</span> de <span className="font-mono">{totalPages}</span>
            </div>
            <div className="flex gap-1.5">
              <Button variant="outline" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1}>Anterior</Button>
              <Button variant="outline" onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page === totalPages}>Seguinte</Button>
            </div>
          </div>
        )}
      </Card>
    </div>
  );
}

window.ReadingsPage = ReadingsPage;
