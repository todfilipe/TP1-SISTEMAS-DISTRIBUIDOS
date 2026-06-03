// =====================================================================
// pages/analyses.jsx — Histórico + detalhe de análise
// =====================================================================

function StatCard({ label, value, unit, sub, accent }) {
  const ringColor = {
    petrol: 'ring-petrol-200 dark:ring-petrol-500/20',
    emerald: 'ring-emerald-200 dark:ring-emerald-500/20',
    amber: 'ring-amber-200 dark:ring-amber-500/20',
    rose: 'ring-rose-200 dark:ring-rose-500/20',
    ink: 'ring-ink-200 dark:ring-ink-700',
  }[accent || 'ink'];
  return (
    <div className={`rounded-lg bg-white dark:bg-ink-900 ring-1 ${ringColor} p-3.5`}>
      <div className="text-[10px] font-semibold uppercase tracking-wider text-ink-500 dark:text-ink-400">{label}</div>
      <div className="mt-1 flex items-baseline gap-1">
        <div className="num text-xl font-semibold tracking-tight">{value}</div>
        {unit && <div className="text-[11px] text-ink-500 dark:text-ink-400">{unit}</div>}
      </div>
      {sub && <div className="text-[11px] text-ink-400 dark:text-ink-500 mt-0.5">{sub}</div>}
    </div>
  );
}

// Histograma simples
function Histogram({ values, unit }) {
  const { BarChart, Bar, ResponsiveContainer, XAxis, YAxis, Tooltip, CartesianGrid } = window.Recharts;
  const dark = document.documentElement.classList.contains('dark');
  const axisColor = dark ? '#7d8c97' : '#566873';
  const bins = 10;
  if (values.length === 0) return null;
  const min = Math.min(...values);
  const max = Math.max(...values);
  const step = (max - min) / bins || 1;
  const buckets = new Array(bins).fill(0).map((_, i) => ({
    range: `${(min + i * step).toFixed(1)}`,
    count: 0,
  }));
  values.forEach((v) => {
    let idx = Math.floor((v - min) / step);
    if (idx >= bins) idx = bins - 1;
    if (idx < 0) idx = 0;
    buckets[idx].count++;
  });
  return (
    <div className="h-[220px]">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={buckets} margin={{ top: 10, right: 8, left: -10, bottom: 0 }}>
          <CartesianGrid vertical={false} />
          <XAxis dataKey="range" tick={{ fill: axisColor, fontSize: 10 }} axisLine={false} tickLine={false} />
          <YAxis tick={{ fill: axisColor, fontSize: 10 }} axisLine={false} tickLine={false} />
          <Tooltip
            contentStyle={{
              background: dark ? '#1f262c' : '#fff',
              border: `1px solid ${dark ? '#2f3a43' : '#d6dde2'}`,
              borderRadius: 8, fontSize: 12,
            }}
            labelFormatter={(l) => `≥ ${l} ${unit}`}
            formatter={(v) => [v, 'leituras']}
          />
          <Bar dataKey="count" fill="#236471" radius={[3,3,0,0]} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}

// Box-plot (SVG manual)
function BoxPlot({ stats, unit }) {
  const W = 600, H = 140, pad = 36;
  const { min, max, median, percentile25, percentile75 } = stats;
  const scale = (v) => pad + ((v - min) / (max - min || 1)) * (W - 2 * pad);
  const cy = H / 2;
  return (
    <div className="w-full overflow-x-auto">
      <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="xMidYMid meet" className="w-full max-w-full">
        {/* eixo */}
        <line x1={pad} x2={W - pad} y1={H - 20} y2={H - 20} stroke="currentColor" strokeWidth="0.5" className="text-ink-300 dark:text-ink-700" />
        {[min, percentile25, median, percentile75, max].map((v, i) => (
          <g key={i}>
            <line x1={scale(v)} x2={scale(v)} y1={H - 24} y2={H - 16} stroke="currentColor" className="text-ink-400 dark:text-ink-500" />
            <text x={scale(v)} y={H - 4} fontSize="9" textAnchor="middle" className="fill-ink-500 dark:fill-ink-400" fontFamily="JetBrains Mono">
              {Number(v).toFixed(1)}
            </text>
          </g>
        ))}
        {/* whiskers */}
        <line x1={scale(min)} x2={scale(percentile25)} y1={cy} y2={cy} stroke="currentColor" className="text-petrol-700 dark:text-petrol-300" strokeWidth="1.5" />
        <line x1={scale(percentile75)} x2={scale(max)} y1={cy} y2={cy} stroke="currentColor" className="text-petrol-700 dark:text-petrol-300" strokeWidth="1.5" />
        <line x1={scale(min)} x2={scale(min)} y1={cy - 12} y2={cy + 12} stroke="currentColor" className="text-petrol-700 dark:text-petrol-300" strokeWidth="1.5" />
        <line x1={scale(max)} x2={scale(max)} y1={cy - 12} y2={cy + 12} stroke="currentColor" className="text-petrol-700 dark:text-petrol-300" strokeWidth="1.5" />
        {/* IQR box */}
        <rect x={scale(percentile25)} y={cy - 22} width={scale(percentile75) - scale(percentile25)} height={44} fill="rgba(35,100,113,0.18)" stroke="#236471" strokeWidth="1.5" />
        <line x1={scale(median)} x2={scale(median)} y1={cy - 22} y2={cy + 22} stroke="#5a8a5e" strokeWidth="2.5" />
      </svg>
    </div>
  );
}

function MovingAvgChart({ readings, movingWindow = 5 }) {
  const { LineChart, Line, ResponsiveContainer, XAxis, YAxis, Tooltip, CartesianGrid, Legend } = window.Recharts;
  const dark = document.documentElement.classList.contains('dark');
  const axisColor = dark ? '#7d8c97' : '#566873';
  if (!readings || readings.length === 0) return null;
  const data = [];
  for (let i = 0; i < readings.length; i++) {
    const slice = readings.slice(Math.max(0, i - movingWindow + 1), i + 1);
    const ma = slice.reduce((s, r) => s + r.value, 0) / slice.length;
    data.push({ ts: new Date(readings[i].timestamp).getTime(), valor: readings[i].value, media: +ma.toFixed(2) });
  }
  return (
    <div className="h-[240px]">
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={data} margin={{ top: 8, right: 10, left: 0, bottom: 0 }}>
          <CartesianGrid vertical={false} />
          <XAxis dataKey="ts" type="number" domain={['dataMin','dataMax']}
            tick={{ fill: axisColor, fontSize: 11 }} axisLine={false} tickLine={false}
            tickFormatter={(t) => {
              const d = new Date(t);
              return `${String(d.getDate()).padStart(2,'0')}/${String(d.getMonth()+1).padStart(2,'0')}`;
            }} />
          <YAxis tick={{ fill: axisColor, fontSize: 11 }} axisLine={false} tickLine={false} width={48} />
          <Tooltip
            contentStyle={{ background: dark ? '#1f262c' : '#fff', border: `1px solid ${dark ? '#2f3a43' : '#d6dde2'}`, borderRadius: 8, fontSize: 12 }}
            labelFormatter={(t) => fmtDateTime(new Date(t).toISOString())}
          />
          <Legend wrapperStyle={{ fontSize: 11 }} iconType="line" />
          <Line type="monotone" dataKey="valor" stroke="#236471" strokeWidth={1.5} dot={false} isAnimationActive={false} name="Valor" />
          <Line type="monotone" dataKey="media" stroke="#5a8a5e" strokeWidth={2.2} strokeDasharray="4 3" dot={false} isAnimationActive={false} name={`Média móvel (n=${movingWindow})`} />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

function AnalysisDetail({ id, nav }) {
  const [analysis, setAnalysis] = useState(null);
  const [readings, setReadings] = useState(null);
  useEffect(() => {
    window.api.getAnalysisById(id).then((a) => {
      setAnalysis(a);
      if (a) window.api.getReadings({ zone: a.zone, type: a.type, sensorId: a.sensorId, from: a.windowStart, to: a.windowEnd }).then((r) => {
        setReadings(r.slice().sort((x,y) => new Date(x.timestamp) - new Date(y.timestamp)));
      });
    });
  }, [id]);
  if (!analysis) return <Loading />;
  const unit = window.api.UNIDADES[analysis.type] || '';

  return (
    <div className="px-4 lg:px-8 py-6 space-y-5">
      {/* Back */}
      <button onClick={() => nav('/analises')} className="inline-flex items-center gap-1.5 text-sm text-ink-500 hover:text-ink-800 dark:hover:text-ink-100">
        <Icon.ArrowRight width={14} height={14} className="rotate-180" />Voltar ao histórico
      </button>

      {/* Header */}
      <Card>
        <div className="flex flex-col md:flex-row md:items-start md:justify-between gap-4">
          <div>
            <div className="flex items-center gap-2">
              <span className="font-mono text-[12px] text-ink-500 dark:text-ink-400">{analysis.id}</span>
              <span className="text-ink-300">·</span>
              <TypePill type={analysis.type} />
              <ZoneTag zone={analysis.zone} />
              {analysis.sensorId && <span className="font-mono text-[12px] px-1.5 py-0.5 rounded bg-ink-100 dark:bg-ink-800">{analysis.sensorId}</span>}
            </div>
            <h1 className="mt-2 text-2xl font-semibold tracking-tight">Análise de {analysis.type} · {window.api.ZONA_LABEL[analysis.zone]}</h1>
            <div className="mt-1 text-sm text-ink-500 dark:text-ink-400">
              Janela: <span className="font-mono">{fmtDateTime(analysis.windowStart)}</span> → <span className="font-mono">{fmtDateTime(analysis.windowEnd)}</span>
            </div>
          </div>
          <div className="flex items-center gap-2">
            <TrendBadge trend={analysis.trendClassification} />
            <AlertBadge level={analysis.alertLevel} />
          </div>
        </div>
      </Card>

      {/* Stats grid */}
      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-3">
        <StatCard label="Média" value={fmtNumber(analysis.average, analysis.type)} unit={unit} accent="petrol" />
        <StatCard label="Mediana" value={fmtNumber(analysis.median, analysis.type)} unit={unit} accent="petrol" />
        <StatCard label="Desvio padrão" value={fmtNumber(analysis.standardDeviation, analysis.type)} unit={unit} />
        <StatCard label="Mínimo" value={fmtNumber(analysis.min, analysis.type)} unit={unit} accent="emerald" />
        <StatCard label="Máximo" value={fmtNumber(analysis.max, analysis.type)} unit={unit} accent="rose" />
        <StatCard label="Outliers" value={analysis.outlierCount} sub={`em ${analysis.sampleCount} amostras`} accent={analysis.outlierCount > 0 ? 'amber' : 'emerald'} />
        <StatCard label="P25" value={fmtNumber(analysis.percentile25, analysis.type)} unit={unit} />
        <StatCard label="P75" value={fmtNumber(analysis.percentile75, analysis.type)} unit={unit} />
        <StatCard label="P95" value={fmtNumber(analysis.percentile95, analysis.type)} unit={unit} />
        <StatCard label="Média móvel" value={fmtNumber(analysis.movingAverageLast, analysis.type)} unit={unit} sub="últimas 5 amostras" accent="petrol" />
        <StatCard label="Declive (tendência)" value={fmtNumber(analysis.trendSlope, 'NUM')} sub={analysis.trendClassification} />
        <StatCard label="Amostras" value={analysis.sampleCount.toLocaleString('pt-PT')} sub={`criada ${timeAgo(analysis.createdAt)}`} />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
        <Card>
          <SectionTitle sub={`${analysis.sampleCount} amostras agrupadas em 10 intervalos`}>Distribuição</SectionTitle>
          {readings ? <Histogram values={readings.map((r) => r.value)} unit={unit} /> : <Loading />}
        </Card>
        <Card>
          <SectionTitle sub="min · P25 · mediana · P75 · max">Box-plot</SectionTitle>
          <div className="py-4"><BoxPlot stats={analysis} unit={unit} /></div>
        </Card>
      </div>

      <Card>
        <SectionTitle sub="Valor por amostra com média móvel sobreposta">Linha temporal</SectionTitle>
        {readings ? <MovingAvgChart readings={readings} /> : <Loading />}
      </Card>
    </div>
  );
}

function AnalysesPage({ nav, route }) {
  // detalhe: /analises/A101
  const match = route.match(/^\/analises\/(.+)/);
  if (match) return <AnalysisDetail id={match[1]} nav={nav} />;

  const [analyses, setAnalyses] = useState(null);
  const [filter, setFilter] = useState({ zone: null, type: null, alert: null });
  useEffect(() => {
    const load = () => window.api.getAnalyses().then(setAnalyses);
    load();
    const id = setInterval(load, 30000);
    return () => clearInterval(id);
  }, []);
  if (!analyses) return <Loading />;

  const sessionAnalyses = window.api.getSessionAnalyses();
  const dbIds = new Set(analyses.map((a) => a.id));
  const merged = [...sessionAnalyses.filter((a) => !dbIds.has(a.id)), ...analyses];

  const filtered = merged.filter((a) => {
    if (filter.zone && a.zone !== filter.zone) return false;
    if (filter.type && a.type !== filter.type) return false;
    if (filter.alert && a.alertLevel !== filter.alert) return false;
    return true;
  });

  return (
    <div className="px-4 lg:px-8 py-6 space-y-5">
      <Card padding="p-4">
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3 items-end">
          <Field label="Zona">
            <Select value={filter.zone} onChange={(v) => setFilter((f) => ({ ...f, zone: v }))} options={window.api.ZONAS.map((z) => ({ value: z, label: window.api.ZONA_LABEL[z] }))} placeholder="Todas" />
          </Field>
          <Field label="Tipo">
            <Select value={filter.type} onChange={(v) => setFilter((f) => ({ ...f, type: v }))} options={window.api.TIPOS} placeholder="Todos" />
          </Field>
          <Field label="Nível de alerta">
            <Select value={filter.alert} onChange={(v) => setFilter((f) => ({ ...f, alert: v }))} options={[{value:'NORMAL',label:'Normal'},{value:'WARNING',label:'Aviso'},{value:'CRITICAL',label:'Crítico'}]} placeholder="Todos" />
          </Field>
          <div>
            <Button variant="outline" onClick={() => setFilter({ zone: null, type: null, alert: null })} disabled={!filter.zone && !filter.type && !filter.alert}>Limpar</Button>
          </div>
        </div>
      </Card>

      <Card padding="p-0">
        <div className="flex items-center justify-between px-5 py-3 border-b border-ink-100 dark:border-ink-800">
          <h2 className="text-[13px] font-semibold uppercase tracking-[0.08em] text-ink-500 dark:text-ink-400">Histórico de análises</h2>
          <Button onClick={() => nav('/nova')} variant="primary">+ Nova análise</Button>
        </div>
        {filtered.length === 0 ? (
          <Empty title="Sem análises" hint="Crie uma nova análise no separador respetivo." />
        ) : (
          <div className="overflow-x-auto scroll-thin">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-[11px] font-semibold uppercase tracking-wide text-ink-500 dark:text-ink-400 bg-ink-50 dark:bg-ink-950/40">
                  <th className="px-4 py-2.5">ID</th>
                  <th className="px-4 py-2.5">Zona</th>
                  <th className="px-4 py-2.5">Tipo</th>
                  <th className="px-4 py-2.5">Sensor</th>
                  <th className="px-4 py-2.5">Janela</th>
                  <th className="px-4 py-2.5 text-right">Média</th>
                  <th className="px-4 py-2.5 text-right">Mediana</th>
                  <th className="px-4 py-2.5 text-right">σ</th>
                  <th className="px-4 py-2.5">Tendência</th>
                  <th className="px-4 py-2.5">Alerta</th>
                  <th className="px-4 py-2.5">Criada</th>
                  <th className="px-4 py-2.5"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-100 dark:divide-ink-800">
                {filtered.map((a) => (
                  <tr key={a.id} className="hover:bg-ink-50/60 dark:hover:bg-ink-800/30 cursor-pointer"
                      onClick={() => nav(`/analises/${a.id}`)}>
                    <td className="px-4 py-2 font-mono text-[12.5px] font-medium">
                      {a.id}
                      {a._session && <span className="ml-1.5 text-[9px] px-1 py-0.5 rounded bg-petrol-50 dark:bg-petrol-500/10 text-petrol-600 dark:text-petrol-300 ring-1 ring-petrol-200 dark:ring-petrol-500/30 font-sans font-semibold align-middle">sessão</span>}
                    </td>
                    <td className="px-4 py-2"><ZoneTag zone={a.zone} size="sm" /></td>
                    <td className="px-4 py-2"><TypePill type={a.type} /></td>
                    <td className="px-4 py-2 font-mono text-[12px] text-ink-600 dark:text-ink-300">{a.sensorId || '—'}</td>
                    <td className="px-4 py-2 font-mono text-[11px] text-ink-500 dark:text-ink-400 whitespace-nowrap">
                      {fmtDateTime(a.windowStart).split(' ')[0]} → {fmtDateTime(a.windowEnd).split(' ')[0]}
                    </td>
                    <td className="px-4 py-2 text-right num font-semibold">{fmtNumber(a.average, a.type)}</td>
                    <td className="px-4 py-2 text-right num">{fmtNumber(a.median, a.type)}</td>
                    <td className="px-4 py-2 text-right num text-ink-500">{fmtNumber(a.standardDeviation, a.type)}</td>
                    <td className="px-4 py-2"><TrendBadge trend={a.trendClassification} /></td>
                    <td className="px-4 py-2"><AlertBadge level={a.alertLevel} size="sm" /></td>
                    <td className="px-4 py-2 text-[11px] text-ink-500 dark:text-ink-400">{timeAgo(a.createdAt)}</td>
                    <td className="px-4 py-2 text-right text-petrol-600 dark:text-petrol-300"><Icon.ArrowRight width={14} height={14} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  );
}

window.AnalysesPage = AnalysesPage;
