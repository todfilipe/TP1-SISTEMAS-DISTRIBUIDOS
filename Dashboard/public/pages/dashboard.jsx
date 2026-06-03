// =====================================================================
// pages/dashboard.jsx — Visão geral
// =====================================================================

function KPI({ label, value, hint, accent, icon: IconComp }) {
  const accents = {
    petrol: 'bg-petrol-50 text-petrol-700 dark:bg-petrol-500/15 dark:text-petrol-300',
    moss: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300',
    amber: 'bg-amber-50 text-amber-700 dark:bg-amber-500/15 dark:text-amber-300',
    ink: 'bg-ink-100 text-ink-700 dark:bg-ink-800 dark:text-ink-200',
    rose: 'bg-rose-50 text-rose-700 dark:bg-rose-500/15 dark:text-rose-300'
  };
  return (
    <Card padding="p-4">
      <div className="flex items-start gap-3">
        <div className={`w-10 h-10 rounded-lg flex items-center justify-center ${accents[accent || 'ink']}`}>
          {IconComp && <IconComp width={18} height={18} />}
        </div>
        <div className="flex-1 min-w-0">
          <div className="text-[11px] font-medium uppercase tracking-wide text-ink-500 dark:text-ink-400">{label}</div>
          <div className="num text-2xl font-semibold tracking-tight mt-0.5">{value}</div>
          {hint && <div className="text-[11px] text-ink-500 dark:text-ink-400 mt-0.5">{hint}</div>}
        </div>
      </div>
    </Card>);

}

const CITY_MAP_SRC = 'assets/city-map.png';

function ZoneCityMap({ zoneWorst }) {
  // Foto aérea real como fundo. Cada zona é uma "janela" nítida recortada
  // sobre o mapa esbatido, alinhada à mesma região da imagem original.
  const layout = [
  { zone: 'ZONA_PARQUE', x: 5, y: 4, w: 37, h: 42 },
  { zone: 'ZONA_RESIDENCIAL', x: 5, y: 49, w: 37, h: 47 },
  { zone: 'ZONA_ESCOLAR', x: 43, y: 4, w: 23, h: 18 },
  { zone: 'ZONA_CENTRO', x: 43, y: 24, w: 23, h: 37 },
  { zone: 'ZONA_INDUSTRIAL', x: 68, y: 4, w: 29, h: 92 }];

  const colorFor = (lvl) => lvl === 'CRITICAL' ? '#f43f5e' : lvl === 'WARNING' ? '#f59e0b' : '#10b981';

  // Recorte alinhado: a imagem é dimensionada ao tamanho do contentor e
  // reposicionada por percentagens — tudo responsivo, sem JS.
  const posFor = (b) => ({ left: `${b.x}%`, top: `${b.y}%`, width: `${b.w}%`, height: `${b.h}%` });
  const bgFor = (b) => {
    const zw = b.w / 100,zh = b.h / 100,zx = b.x / 100,zy = b.y / 100;
    return {
      backgroundImage: `url(${CITY_MAP_SRC})`,
      backgroundSize: `${100 / zw}% ${100 / zh}%`,
      backgroundPosition: `${(100 * zx / (1 - zw)).toFixed(3)}% ${(100 * zy / (1 - zh)).toFixed(3)}%`,
      backgroundRepeat: 'no-repeat'
    };
  };
  const ZoneLabel = ({ zone, color, className = 'absolute top-2 left-2' }) =>
  <div className={className}>
      <div className="inline-flex items-center gap-1.5 rounded-lg bg-white/95 dark:bg-ink-900/90 backdrop-blur px-2.5 py-1 shadow-sm ring-1 ring-black/5 dark:ring-white/10">
        <span className="w-2 h-2 rounded-full" style={{ background: color }} />
        <span className="text-[11px] font-bold tracking-wide text-ink-800 dark:text-ink-100 uppercase">
          {window.api.ZONA_LABEL[zone]}
        </span>
      </div>
    </div>;


  return (
    <div className="relative w-full aspect-[1629/965] rounded-lg ring-1 ring-ink-200/60 dark:ring-ink-800 overflow-hidden">
      {/* Base esbatida (foto desaturada) */}
      <div
        className="absolute inset-0"
        style={{ backgroundImage: `url(${CITY_MAP_SRC})`, backgroundSize: 'cover', backgroundPosition: 'center', filter: 'grayscale(0.92) brightness(1.08) contrast(0.95)' }} />

      <div className="absolute inset-0 bg-white/60 dark:bg-ink-950/72" />

      {/* Zonas recortadas */}
      {layout.map((b) => {
        const lvl = zoneWorst[b.zone] || 'NORMAL';
        const color = colorFor(lvl);
        return (
          <div
            key={b.zone}
            className="absolute rounded-2xl overflow-hidden shadow-[0_4px_20px_-4px_rgba(0,0,0,0.35)] transition-transform hover:scale-[1.012]"
            style={{ ...posFor(b), ...bgFor(b), border: `2.5px solid ${color}`, borderRadius: "12px" }}
            title={`${window.api.ZONA_LABEL[b.zone]} — ${lvl}`}>

            <ZoneLabel zone={b.zone} color={color} />
          </div>);

      })}

      {/* Legenda */}
      <div className="absolute bottom-2.5 right-2.5 flex items-center gap-3 text-[11px] font-medium bg-white/92 dark:bg-ink-900/90 backdrop-blur px-3 py-1.5 rounded-lg shadow-sm ring-1 ring-black/5 dark:ring-white/10" style={{ color: "rgb(255, 255, 255)", opacity: "1" }}>
        <span className="inline-flex items-center gap-1.5"><span className="w-2 h-2 rounded-full bg-emerald-500" />Normal</span>
        <span className="inline-flex items-center gap-1.5"><span className="w-2 h-2 rounded-full bg-amber-500" />Aviso</span>
        <span className="inline-flex items-center gap-1.5"><span className="w-2 h-2 rounded-full bg-rose-500" />Crítico</span>
      </div>
    </div>);

}

function ZoneSummaryCard({ zone, lastByType }) {
  const types = Object.keys(lastByType);
  const worst = types.reduce((acc, t) => window.api.worstAlert(acc, window.api.classifyAlert(t, lastByType[t].value)), 'NORMAL');
  const accent = worst === 'CRITICAL' ? 'rose' : worst === 'WARNING' ? 'amber' : 'emerald';
  const borderClass = {
    rose: 'before:bg-rose-500',
    amber: 'before:bg-amber-500',
    emerald: 'before:bg-emerald-500'
  }[accent];
  return (
    <div className={`relative rounded-xl bg-white dark:bg-ink-900 border border-ink-200/70 dark:border-ink-800 p-4 pl-5 overflow-hidden before:content-[''] before:absolute before:inset-y-0 before:left-0 before:w-1 ${borderClass}`}>
      <div className="flex items-center justify-between">
        <div>
          <div className="text-[10px] font-mono uppercase tracking-[0.12em] text-ink-400 dark:text-ink-500">{zone.replace('ZONA_', '')}</div>
          <div className="text-base font-semibold tracking-tight">{window.api.ZONA_LABEL[zone]}</div>
        </div>
        <AlertBadge level={worst} />
      </div>
      <div className="mt-3 grid grid-cols-2 gap-x-3 gap-y-2">
        {types.map((t) => {
          const r = lastByType[t];
          const lvl = window.api.classifyAlert(t, r.value);
          const valColor = lvl === 'CRITICAL' ? 'text-rose-600 dark:text-rose-300' : lvl === 'WARNING' ? 'text-amber-600 dark:text-amber-300' : 'text-ink-800 dark:text-ink-100';
          return (
            <div key={t} className="flex items-baseline justify-between gap-2">
              <TypePill type={t} />
              <div className="text-right">
                <div className={`num text-sm font-semibold ${valColor}`}>{fmtNumber(r.value, t)}<span className="ml-0.5 text-[10px] font-normal text-ink-500 dark:text-ink-400">{r.unit}</span></div>
              </div>
            </div>);

        })}
      </div>
    </div>);

}

function AlertRow({ r }) {
  const lvl = window.api.classifyAlert(r.type, r.value);
  const s = ALERT_STYLES[lvl];
  return (
    <div className="flex items-center gap-3 py-2.5 border-b border-ink-100 dark:border-ink-800 last:border-0">
      <span className={`w-2 h-2 rounded-full ${s.dot}`} />
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2 text-sm">
          <span className="font-mono text-ink-900 dark:text-ink-100 font-medium">{r.sensorId}</span>
          <span className="text-ink-400">·</span>
          <span className="text-ink-700 dark:text-ink-200">{window.api.ZONA_LABEL[r.zone]}</span>
          <TypePill type={r.type} />
        </div>
        <div className="text-[11px] text-ink-500 dark:text-ink-400 mt-0.5">{fmtDateTime(r.timestamp)} · {r.gatewayId} · {r.originalMessageFormat}</div>
      </div>
      <div className="text-right">
        <div className={`num text-sm font-semibold ${lvl === 'CRITICAL' ? 'text-rose-600 dark:text-rose-300' : lvl === 'WARNING' ? 'text-amber-600 dark:text-amber-300' : 'text-ink-800 dark:text-ink-100'}`}>
          {fmtNumber(r.value, r.type)}<span className="ml-0.5 text-[10px] font-normal text-ink-500">{r.unit}</span>
        </div>
        <AlertBadge level={lvl} size="sm" />
      </div>
    </div>);

}

function ArchitectureNote() {
  return (
    <Card padding="p-5">
      <SectionTitle sub="Pub/Sub → Gateways → Servidor → BD · Análise via gRPC">Sobre o sistema</SectionTitle>
      <div className="flex items-center gap-2 text-[11px] font-mono uppercase tracking-wide overflow-x-auto scroll-thin pb-1">
        {['Sensores', 'RabbitMQ', 'Gateways', 'Pré-processamento', 'Servidor', 'MongoDB'].map((step, i, arr) =>
        <React.Fragment key={step}>
            <span className="shrink-0 px-2 py-1 rounded bg-ink-100 dark:bg-ink-800 text-ink-700 dark:text-ink-200">{step}</span>
            {i < arr.length - 1 && <Icon.ArrowRight width={12} height={12} className="shrink-0 text-ink-400" />}
          </React.Fragment>
        )}
      </div>
      <div className="mt-2 flex items-center gap-2 text-[11px] font-mono uppercase tracking-wide">
        <span className="px-2 py-1 rounded bg-ink-100 dark:bg-ink-800 text-ink-700 dark:text-ink-200">Servidor</span>
        <span className="text-ink-400">↔</span>
        <span className="px-2 py-1 rounded bg-emerald-50 dark:bg-emerald-500/10 text-emerald-700 dark:text-emerald-300">Análise gRPC (Python)</span>
      </div>
      <p className="mt-3 text-[12px] text-ink-500 dark:text-ink-400 leading-relaxed">
        Esta interface lê do Servidor/BD e da Análise. O paradigma <span className="font-semibold text-ink-700 dark:text-ink-200">One Health</span> liga saúde
        humana, animal e ambiental — os indicadores ambientais permitem antecipar riscos para a saúde pública urbana.
      </p>
    </Card>);

}

function DashboardPage({ nav }) {
  const [data, setData] = useState(null);
  useEffect(() => {
    Promise.all([
    window.api.getReadings(),
    window.api.getSensors(),
    window.api.getAnalyses()]
    ).then(([readings, sensors, analyses]) => setData({ readings, sensors, analyses }));
  }, []);

  if (!data) return <Loading />;
  const { readings, sensors, analyses } = data;

  // Última leitura por (zone, type)
  const lastByZoneType = {};
  readings.forEach((r) => {
    const k = `${r.zone}|${r.type}`;
    if (!lastByZoneType[k] || new Date(r.timestamp) > new Date(lastByZoneType[k].timestamp)) {
      lastByZoneType[k] = r;
    }
  });
  // Pior nível por zona com base na última leitura de cada tipo
  const zoneWorst = {};
  window.api.ZONAS.forEach((z) => {zoneWorst[z] = 'NORMAL';});
  Object.values(lastByZoneType).forEach((r) => {
    const lvl = window.api.classifyAlert(r.type, r.value);
    zoneWorst[r.zone] = window.api.worstAlert(zoneWorst[r.zone], lvl);
  });

  // Cartões por zona — agrupados
  const zoneCards = window.api.ZONAS.map((z) => {
    const byType = {};
    Object.values(lastByZoneType).forEach((r) => {if (r.zone === z) byType[r.type] = r;});
    if (Object.keys(byType).length === 0) return null;
    return { zone: z, byType };
  }).filter(Boolean);

  // Alertas (todas as leituras com level WARNING/CRITICAL, recentes)
  const alerts = readings.
  map((r) => ({ ...r, level: window.api.classifyAlert(r.type, r.value) })).
  filter((r) => r.level !== 'NORMAL').
  slice(0, 8);

  const activeSensorIds = new Set(sensors.filter((s) => s.estado === 'ativo').map((s) => s.sensorId));
  const zonesMonitored = new Set(sensors.map((s) => s.zone)).size;
  const activeAlerts = readings.filter((r) => window.api.classifyAlert(r.type, r.value) !== 'NORMAL').length;

  return (
    <div className="px-4 lg:px-8 py-6 space-y-6" style={{ width: "1006px" }}>
      {/* KPIs */}
      <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-5 gap-3">
        <KPI label="Leituras" value={readings.length.toLocaleString('pt-PT')} hint="últimos 3 dias" accent="petrol" icon={Icon.Pulse} />
        <KPI label="Sensores ativos" value={`${activeSensorIds.size}/${new Set(sensors.map((s) => s.sensorId)).size}`} hint="estado: ativo" accent="moss" icon={Icon.Sensors} />
        <KPI label="Zonas monitorizadas" value={zonesMonitored} hint="cobertura urbana" accent="ink" icon={Icon.Dashboard} />
        <KPI label="Análises" value={analyses.length} hint="histórico estatístico" accent="ink" icon={Icon.Analyses} />
        <KPI label="Alertas ativos" value={activeAlerts} hint="WARNING + CRITICAL" accent={activeAlerts > 0 ? 'rose' : 'moss'} icon={Icon.Bell} />
      </div>

      {/* Mapa + Alertas */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <Card className="lg:col-span-2">
          <SectionTitle sub="Pior nível de alerta atual por zona" action={
          <span className="text-[11px] text-ink-400 font-mono">{Object.values(zoneWorst).filter((v) => v !== 'NORMAL').length} de 5 zonas com avisos</span>
          }>Mapa da cidade</SectionTitle>
          <ZoneCityMap zoneWorst={zoneWorst} />
        </Card>
        <Card>
          <SectionTitle sub={`${alerts.length} eventos recentes`} action={
          <a href="#/leituras" className="text-[11px] text-petrol-600 dark:text-petrol-300 hover:underline">Ver todos →</a>
          }>Alertas recentes</SectionTitle>
          <div className="max-h-[360px] overflow-y-auto scroll-thin -mx-1 px-1">
            {alerts.length === 0 ?
            <Empty title="Sem alertas" hint="Todas as zonas em estado normal." /> :
            alerts.map((r, i) => <AlertRow key={i} r={r} />)}
          </div>
        </Card>
      </div>

      {/* Zonas */}
      <div>
        <SectionTitle sub="Última leitura por tipo, com nível de alerta correspondente">Estado por zona</SectionTitle>
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
          {zoneCards.map(({ zone, byType }) =>
          <ZoneSummaryCard key={zone} zone={zone} lastByType={byType} />
          )}
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className="lg:col-span-2">
          <Card>
            <SectionTitle sub="Distribuição de leituras por zona nos últimos 3 dias">Atividade por zona</SectionTitle>
            <ZoneActivityChart readings={readings} />
          </Card>
        </div>
        <ArchitectureNote />
      </div>
    </div>);

}

function ZoneActivityChart({ readings }) {
  const { BarChart, Bar, ResponsiveContainer, XAxis, YAxis, Tooltip, CartesianGrid, Legend } = window.Recharts;
  const data = window.api.ZONAS.map((z) => {
    const inZone = readings.filter((r) => r.zone === z);
    const normal = inZone.filter((r) => window.api.classifyAlert(r.type, r.value) === 'NORMAL').length;
    const warning = inZone.filter((r) => window.api.classifyAlert(r.type, r.value) === 'WARNING').length;
    const critical = inZone.filter((r) => window.api.classifyAlert(r.type, r.value) === 'CRITICAL').length;
    return { zone: window.api.ZONA_LABEL[z], Normal: normal, Aviso: warning, Crítico: critical };
  });
  const dark = document.documentElement.classList.contains('dark');
  const axisColor = dark ? '#7d8c97' : '#566873';
  return (
    <div className="h-[260px]">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} margin={{ top: 10, right: 10, left: -10, bottom: 0 }}>
          <CartesianGrid vertical={false} />
          <XAxis dataKey="zone" tick={{ fill: axisColor, fontSize: 11 }} axisLine={false} tickLine={false} />
          <YAxis tick={{ fill: axisColor, fontSize: 11 }} axisLine={false} tickLine={false} />
          <Tooltip
            contentStyle={{
              background: dark ? '#1f262c' : '#fff',
              border: `1px solid ${dark ? '#2f3a43' : '#d6dde2'}`,
              borderRadius: 8,
              fontSize: 12
            }}
            cursor={{ fill: dark ? 'rgba(255,255,255,0.04)' : 'rgba(0,0,0,0.04)' }} />

          <Legend wrapperStyle={{ fontSize: 11 }} iconType="circle" />
          <Bar dataKey="Normal" stackId="a" fill="#10b981" radius={[0, 0, 0, 0]} />
          <Bar dataKey="Aviso" stackId="a" fill="#f59e0b" radius={[0, 0, 0, 0]} />
          <Bar dataKey="Crítico" stackId="a" fill="#f43f5e" radius={[4, 4, 0, 0]} />
        </BarChart>
      </ResponsiveContainer>
    </div>);

}

window.DashboardPage = DashboardPage;
