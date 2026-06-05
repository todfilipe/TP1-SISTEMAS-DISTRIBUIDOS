// =====================================================================
// pages/forecast.jsx — Nova análise / Previsão
// =====================================================================

function ForecastChart({ history, forecast, type, unit }) {
  const { LineChart, Line, ResponsiveContainer, XAxis, YAxis, Tooltip, CartesianGrid, Legend, ReferenceLine } = window.Recharts;
  const dark = document.documentElement.classList.contains('dark');
  const axisColor = dark ? '#7d8c97' : '#566873';

  if (!history || history.length === 0) return null;

  // Eixo temporal: histórico + n períodos de "futuro" espaçados pela mediana do passo
  const sorted = history.slice().sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp));
  const tsArr = sorted.map((r) => new Date(r.timestamp).getTime());
  const deltas = [];
  for (let i = 1; i < tsArr.length; i++) deltas.push(tsArr[i] - tsArr[i - 1]);
  deltas.sort((a,b) => a-b);
  const stepMs = deltas[Math.floor(deltas.length / 2)] || 60 * 60 * 1000;

  const data = sorted.map((r) => ({ ts: new Date(r.timestamp).getTime(), valor: r.value }));
  const lastTs = tsArr[tsArr.length - 1];
  forecast.forEach((v, i) => {
    data.push({ ts: lastTs + stepMs * (i + 1), previsao: v });
  });
  // Liga o último valor real ao primeiro previsto
  const lastIdx = sorted.length - 1;
  data[lastIdx].previsao = sorted[lastIdx].value;

  return (
    <div className="h-[320px]">
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={data} margin={{ top: 10, right: 16, left: 0, bottom: 0 }}>
          <CartesianGrid vertical={false} />
          <XAxis
            dataKey="ts" type="number" domain={['dataMin', 'dataMax']}
            tick={{ fill: axisColor, fontSize: 11 }} axisLine={false} tickLine={false}
            tickFormatter={(t) => {
              const d = new Date(t);
              return `${String(d.getDate()).padStart(2,'0')}/${String(d.getMonth()+1).padStart(2,'0')} ${String(d.getHours()).padStart(2,'0')}h`;
            }}
          />
          <YAxis tick={{ fill: axisColor, fontSize: 11 }} axisLine={false} tickLine={false} width={48} />
          <Tooltip
            contentStyle={{
              background: dark ? '#1f262c' : '#fff',
              border: `1px solid ${dark ? '#2f3a43' : '#d6dde2'}`,
              borderRadius: 8, fontSize: 12,
            }}
            labelFormatter={(t) => fmtDateTime(new Date(t).toISOString())}
            formatter={(v, name) => v == null ? null : [`${fmtNumber(v, type)} ${unit}`, name]}
          />
          <Legend wrapperStyle={{ fontSize: 11 }} iconType="line" />
          <ReferenceLine x={lastTs} stroke="#9aa4ab" strokeDasharray="3 3" label={{ value: 'agora', fill: axisColor, fontSize: 10, position: 'top' }} />
          <Line type="monotone" dataKey="valor" stroke="#236471" strokeWidth={1.75} dot={false} connectNulls={false} isAnimationActive={false} name="Histórico" />
          <Line type="monotone" dataKey="previsao" stroke="#b45309" strokeWidth={2.25} strokeDasharray="5 4" dot={{ r: 3, fill: '#b45309', stroke: 'none' }} connectNulls={false} isAnimationActive={false} name="Previsão" />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

function ForecastPage({ nav }) {
  const [form, setForm] = useState({
    type: 'AR',
    zone: 'ZONA_INDUSTRIAL',
    sensorId: null,
    from: '',
    to: '',
    strategy: 'linear',
    periods: 6,
  });
  const [allReadings, setAllReadings] = useState(null);
  const [result, setResult] = useState(() => {
    const sa = window.api.getSessionAnalyses();
    const sp = window.api.getSessionPredictions();
    if (!sa.length && !sp.length) return null;
    const candidates = [
      ...sa.map((a) => ({ kind: 'analysis', data: a, t: new Date(a.createdAt).getTime() })),
      ...sp.map((p) => ({ kind: 'prediction', data: p, t: new Date(p.timestamp).getTime() })),
    ].sort((a, b) => b.t - a.t);
    const best = candidates[0];
    return best ? { kind: best.kind, data: best.data } : null;
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const [loadError, setLoadError] = useState(null);
  const [lastUpdated, setLastUpdated] = useState(null);
  const [refreshing, setRefreshing] = useState(false);

  const loadReadings = ({ silent = false } = {}) => {
    if (!silent) setRefreshing(true);
    setLoadError(null);
    window.api.invalidateCache();
    window.api.getReadings()
      .then((data) => {
        setAllReadings(data);
        setLastUpdated(new Date());
      })
      .catch(setLoadError)
      .finally(() => {
        if (!silent) setRefreshing(false);
      });
  };
  useEffect(() => { loadReadings(); }, []);
  const applyDatePreset = (range) => setForm((f) => ({ ...f, ...range }));

  const sensorIds = useMemo(() => {
    if (!allReadings) return [];
    return Array.from(new Set(
      allReadings
        .filter((r) => (!form.zone || r.zone === form.zone) && (!form.type || r.type === form.type))
        .map((r) => r.sensorId)
    )).sort();
  }, [allReadings, form.zone, form.type]);

  const runAnalysis = async () => {
    setBusy(true); setError(null);
    try {
      const a = await window.api.runAnalysis({ type: form.type, zone: form.zone, sensorId: form.sensorId, from: form.from || undefined, to: form.to || undefined });
      if (!a) setError('Não há amostras suficientes para os parâmetros indicados.');
      else setResult({ kind: 'analysis', data: a });
    } catch (err) {
      setError(err.message || 'Erro ao executar analise.');
    } finally { setBusy(false); }
  };
  const runPrediction = async () => {
    setBusy(true); setError(null);
    try {
      const p = await window.api.runPrediction({
        type: form.type, zone: form.zone, sensorId: form.sensorId,
        from: form.from || undefined, to: form.to || undefined,
        strategy: form.strategy, periods: Number(form.periods) || 6,
      });
      if (!p) setError('Não há amostras suficientes para os parâmetros indicados (mínimo 3).');
      else setResult({ kind: 'prediction', data: p });
    } catch (err) {
      setError(err.message || 'Erro ao executar previsao.');
    } finally { setBusy(false); }
  };

  if (loadError && !allReadings) return <ErrorState error={loadError} retry={loadReadings} />;

  return (
    <div className="px-4 lg:px-8 py-6 space-y-5">
      <Card>
        <SectionTitle sub="Calcula estatísticas sobre o histórico ou projeta previsão para janelas futuras">
          Parâmetros
        </SectionTitle>
        <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-3">
          <Field label="Tipo (obrigatório)">
            <Select
              value={form.type}
              onChange={(v) => setForm((f) => ({ ...f, type: v || 'AR' }))}
              options={window.api.TIPOS}
              placeholder="Selecionar"
            />
          </Field>
          <Field label="Zona (obrigatório)">
            <Select
              value={form.zone}
              onChange={(v) => setForm((f) => ({ ...f, zone: v || 'ZONA_INDUSTRIAL' }))}
              options={window.api.ZONAS.map((z) => ({ value: z, label: window.api.ZONA_LABEL[z] }))}
              placeholder="Selecionar"
            />
          </Field>
          <Field label="Sensor (opcional)">
            <Select value={form.sensorId} onChange={(v) => setForm((f) => ({ ...f, sensorId: v }))} options={sensorIds} placeholder="Todos os da zona" />
          </Field>
          <Field label="Estratégia de previsão">
            <Select
              required
              value={form.strategy}
              onChange={(v) => setForm((f) => ({ ...f, strategy: v || 'linear' }))}
              options={[{ value: 'linear', label: 'Regressão linear' }, { value: 'ewma', label: 'Média móvel exponencial (EWMA)' }]}
            />
          </Field>
          <Field label="De">
            <Input type="datetime-local" value={form.from} onChange={(e) => setForm((f) => ({ ...f, from: e.target.value }))} />
          </Field>
          <Field label="Até">
            <Input type="datetime-local" value={form.to} onChange={(e) => setForm((f) => ({ ...f, to: e.target.value }))} />
          </Field>
          <Field label="Períodos a prever" hint="apenas para previsão">
            <Input type="number" min="1" max="48" value={form.periods} onChange={(e) => setForm((f) => ({ ...f, periods: e.target.value }))} />
          </Field>
          <div className="flex items-end gap-2">
            <Button variant="secondary" onClick={runAnalysis} disabled={busy}>Executar análise</Button>
            <Button onClick={runPrediction} disabled={busy}>Executar previsão</Button>
          </div>
        </div>
        <div className="mt-3 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
          <DatePresetButtons onApply={applyDatePreset} />
          <div className="flex items-center gap-2">
            <RefreshMeta lastUpdated={lastUpdated} refreshing={refreshing} />
            <Button variant="outline" icon={Icon.Refresh} onClick={() => loadReadings()} disabled={refreshing}>Atualizar leituras</Button>
          </div>
        </div>
        {loadError && allReadings && <div className="mt-3"><InlineError error={loadError} retry={() => loadReadings()} /></div>}
        {error && (
          <div className="mt-3 px-3 py-2 rounded-md bg-amber-50 dark:bg-amber-500/10 text-amber-700 dark:text-amber-300 text-sm ring-1 ring-amber-200 dark:ring-amber-500/30">
            {error}
          </div>
        )}
      </Card>

      {/* Resultado */}
      {busy && <Loading label="A executar pedido gRPC…" />}

      {!busy && result && result.kind === 'analysis' && (
        <AnalysisResultInline a={result.data} unit={window.api.UNIDADES[result.data.type]} />
      )}
      {!busy && result && result.kind === 'prediction' && (
        <PredictionResultInline p={result.data} />
      )}

      {!busy && !result && (
        <Card>
          <Empty title="Sem resultado" hint="Configura parâmetros e executa uma análise ou previsão." />
        </Card>
      )}

      {/* Predictions cache */}
      <PredictionHistory />
    </div>
  );
}

function AnalysisResultInline({ a, unit }) {
  return (
    <div className="space-y-5">
      <Card>
        <div className="flex flex-col md:flex-row md:items-start md:justify-between gap-4">
          <div>
            <div className="flex items-center gap-2">
              <span className="font-mono text-[12px] text-ink-500 dark:text-ink-400">{a.id}</span>
              <TypePill type={a.type} />
              <ZoneTag zone={a.zone} />
              {a.sensorId && <span className="font-mono text-[12px] px-1.5 py-0.5 rounded bg-ink-100 dark:bg-ink-800">{a.sensorId}</span>}
            </div>
            <h2 className="mt-2 text-xl font-semibold tracking-tight">Análise calculada em direto</h2>
            <div className="text-xs text-ink-500 dark:text-ink-400 mt-1">
              <span className="font-mono">{fmtDateTime(a.windowStart)}</span> → <span className="font-mono">{fmtDateTime(a.windowEnd)}</span>
            </div>
          </div>
          <div className="flex items-center gap-2">
            <TrendBadge trend={a.trendClassification} />
            <AlertBadge level={a.alertLevel} />
          </div>
        </div>
      </Card>
      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-3">
        <StatCard label="Média" value={fmtNumber(a.average, a.type)} unit={unit} accent="petrol" />
        <StatCard label="Mediana" value={fmtNumber(a.median, a.type)} unit={unit} accent="petrol" />
        <StatCard label="σ" value={fmtNumber(a.standardDeviation, a.type)} unit={unit} />
        <StatCard label="Mín" value={fmtNumber(a.min, a.type)} unit={unit} accent="emerald" />
        <StatCard label="Máx" value={fmtNumber(a.max, a.type)} unit={unit} accent="rose" />
        <StatCard label="Amostras" value={a.sampleCount} sub={`${a.outlierCount} outliers`} />
      </div>
    </div>
  );
}

function PredictionResultInline({ p }) {
  const unit = window.api.UNIDADES[p.type];
  return (
    <Card>
      <div className="flex flex-col md:flex-row md:items-start md:justify-between gap-3 mb-4">
        <div>
          <div className="flex items-center gap-2">
            <span className="font-mono text-[12px] text-ink-500 dark:text-ink-400">{p.id}</span>
            <TypePill type={p.type} />
            <ZoneTag zone={p.zone} />
            <span className="text-[11px] px-2 py-0.5 rounded-full bg-petrol-50 dark:bg-petrol-500/10 text-petrol-700 dark:text-petrol-300 ring-1 ring-petrol-200 dark:ring-petrol-500/30 font-semibold uppercase tracking-wide">
              {p.strategyUsed}
            </span>
          </div>
          <h2 className="mt-2 text-xl font-semibold tracking-tight">Previsão · {p.forecast.length} janelas</h2>
          <p className="text-sm text-ink-500 dark:text-ink-400 mt-1 max-w-2xl">{p.predictionSummary}</p>
        </div>
        <div className="font-mono text-[11px] text-ink-500 dark:text-ink-400">{fmtDateTime(p.timestamp)}</div>
      </div>
      <ForecastChart history={p._history} forecast={p.forecast} type={p.type} unit={unit} />
      <div className="mt-4 grid grid-cols-3 md:grid-cols-6 lg:grid-cols-8 gap-2">
        {p.forecast.map((v, i) => (
          <div key={i} className="rounded-md ring-1 ring-amber-200 dark:ring-amber-500/30 bg-amber-50/60 dark:bg-amber-500/10 px-2.5 py-2 text-center">
            <div className="text-[9px] uppercase tracking-wide text-ink-500 dark:text-ink-400">t+{i+1}</div>
            <div className="num text-sm font-semibold text-amber-800 dark:text-amber-200">{fmtNumber(v, p.type)}</div>
            <div className="text-[9px] text-ink-500">{unit}</div>
          </div>
        ))}
      </div>
    </Card>
  );
}

function PredictionHistory() {
  const predictions = window.api.getSessionPredictions();
  if (predictions.length === 0) return null;

  return (
    <Card>
      <SectionTitle sub="Previsões calculadas nesta sessão">Histórico de previsões</SectionTitle>
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
        {predictions.map((p) => (
          <div key={p.id} className="rounded-lg ring-1 ring-ink-200 dark:ring-ink-800 p-3">
            <div className="flex items-center gap-1.5 flex-wrap">
              <span className="font-mono text-[11px] text-ink-500">{p.id}</span>
              {p._session && <span className="text-[10px] px-1.5 py-0.5 rounded-full bg-petrol-50 dark:bg-petrol-500/10 text-petrol-700 dark:text-petrol-300 ring-1 ring-petrol-200 dark:ring-petrol-500/30 font-semibold">sessão</span>}
              <TypePill type={p.type} />
              <ZoneTag zone={p.zone} size="sm" />
              <span className="ml-auto text-[10px] uppercase font-semibold text-petrol-700 dark:text-petrol-300 font-mono">{p.strategyUsed}</span>
            </div>
            <div className="mt-2 text-[12px] text-ink-600 dark:text-ink-300 leading-snug">{p.predictionSummary}</div>
            <div className="mt-3 flex items-center gap-1.5 flex-wrap">
              {p.forecast.map((v, i) => (
                <span key={i} className="num text-[11px] px-1.5 py-0.5 rounded bg-ink-100 dark:bg-ink-800 font-mono whitespace-nowrap">{fmtNumber(v, p.type)}</span>
              ))}
            </div>
            <div className="mt-2 text-[10px] font-mono text-ink-400">{fmtDateTime(p.timestamp)}</div>
          </div>
        ))}
      </div>
    </Card>
  );
}

window.ForecastPage = ForecastPage;
