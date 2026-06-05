// =====================================================================
// pages/system.jsx - Topologia operacional do projeto
// =====================================================================

const SYSTEM_TABS = [
  { key: 'topology', label: 'Topologia', icon: Icon.System },
  { key: 'gateways', label: 'Gateways', icon: Icon.Server },
  { key: 'coverage', label: 'Cobertura', icon: Icon.Sensors },
  { key: 'services', label: 'Servicos', icon: Icon.Dashboard },
  { key: 'queues', label: 'Filas e BD', icon: Icon.Database },
];

function MiniStat({ label, value, hint }) {
  return (
    <div className="rounded-lg border border-ink-200 dark:border-ink-800 bg-white dark:bg-ink-900 px-3 py-2.5">
      <div className="text-[10px] uppercase tracking-wide text-ink-500 dark:text-ink-400">{label}</div>
      <div className="num mt-0.5 text-xl font-semibold tracking-tight">{value}</div>
      {hint && <div className="text-[11px] text-ink-500 dark:text-ink-400 mt-0.5">{hint}</div>}
    </div>
  );
}

function SystemTabs({ active, setActive }) {
  return (
    <div className="flex gap-1.5 overflow-x-auto scroll-thin rounded-lg border border-ink-200 dark:border-ink-800 bg-white dark:bg-ink-900 p-1">
      {SYSTEM_TABS.map((tab) => {
        const selected = active === tab.key;
        return (
          <button
            key={tab.key}
            type="button"
            onClick={() => setActive(tab.key)}
            className={
              'inline-flex items-center gap-2 rounded-md px-3 py-2 text-sm font-medium whitespace-nowrap transition-colors ' +
              (selected
                ? 'bg-petrol-50 text-petrol-800 dark:bg-petrol-900/40 dark:text-petrol-100'
                : 'text-ink-600 dark:text-ink-300 hover:bg-ink-100 dark:hover:bg-ink-800/70')
            }
          >
            <tab.icon width={15} height={15} />
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}

function RuntimeBadge({ runtime }) {
  const isCsharp = runtime === 'C#';
  return (
    <span className={`inline-flex items-center rounded-md px-1.5 py-0.5 text-[10px] font-semibold ${
      isCsharp
        ? 'bg-indigo-50 text-indigo-700 dark:bg-indigo-500/10 dark:text-indigo-300'
        : 'bg-emerald-50 text-emerald-700 dark:bg-emerald-500/10 dark:text-emerald-300'
    }`}>
      {runtime}
    </span>
  );
}

function StatusPill({ label = 'docker' }) {
  return (
    <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-50 text-emerald-700 ring-1 ring-emerald-200 dark:bg-emerald-500/10 dark:text-emerald-300 dark:ring-emerald-500/30 px-2 py-0.5 text-[11px] font-semibold">
      <span className="w-1.5 h-1.5 rounded-full bg-emerald-500" />
      {label}
    </span>
  );
}

function PipelineView({ steps }) {
  return (
    <div className="grid grid-cols-1 md:grid-cols-3 xl:grid-cols-6 gap-3">
      {steps.map((step) => (
        <div key={step.step} className="rounded-lg border border-ink-200 dark:border-ink-800 bg-white dark:bg-ink-900 p-3">
          <div className="flex items-center gap-2">
            <span className="num flex h-6 w-6 items-center justify-center rounded-md bg-petrol-50 text-petrol-700 dark:bg-petrol-500/15 dark:text-petrol-300 text-xs font-semibold">
              {step.step}
            </span>
            <div className="text-sm font-semibold text-ink-900 dark:text-ink-100">{step.name}</div>
          </div>
          <div className="mt-2 text-xs leading-5 text-ink-500 dark:text-ink-400">{step.detail}</div>
        </div>
      ))}
    </div>
  );
}

function TopologyTab({ data }) {
  return (
    <div className="space-y-5">
      <div>
        <SectionTitle sub="Fluxo real dos dados desde o dispositivo ate a dashboard">Pipeline</SectionTitle>
        <PipelineView steps={data.pipeline} />
      </div>

      <div>
        <SectionTitle sub="1 gateway por zona, com sensores fisicos associados">Zonas e gateways</SectionTitle>
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
          {data.zones.map((zone) => (
            <div key={zone.zone} className="rounded-lg border border-ink-200 dark:border-ink-800 bg-white dark:bg-ink-900 p-4">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <div className="text-[10px] uppercase tracking-wide text-ink-400 dark:text-ink-500">{zone.zone}</div>
                  <div className="text-base font-semibold tracking-tight">{zone.label}</div>
                </div>
                <ZoneTag zone={zone.zone} size="sm" />
              </div>
              <div className="mt-4 grid grid-cols-2 gap-2">
                <MiniStat label="Gateway" value={zone.gateway.gatewayId} hint={zone.gateway.service} />
                <MiniStat label="Sensores" value={zone.sensorCount} hint={`${zone.logicalSensorCount} sensores logicos`} />
              </div>
              <div className="mt-3 flex flex-wrap gap-1.5">
                {zone.types.map((type) => <TypePill key={type} type={type} />)}
              </div>
              <div className="mt-3 text-[11px] text-ink-500 dark:text-ink-400">
                Ultima atividade: <span className="font-mono">{fmtDateTime(zone.latestAt)}</span>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

function GatewaysTab({ gateways }) {
  return (
    <Card padding="p-0">
      <div className="px-5 py-3 border-b border-ink-100 dark:border-ink-800">
        <SectionTitle sub="Cada gateway consome apenas a routing key da sua zona">Gateways registados</SectionTitle>
      </div>
      <div className="overflow-x-auto scroll-thin">
        <table className="w-full text-sm">
          <thead>
            <tr className="text-left text-[11px] font-semibold uppercase tracking-wide text-ink-500 dark:text-ink-400 bg-ink-50 dark:bg-ink-950/40">
              <th className="px-4 py-2.5">Gateway</th>
              <th className="px-4 py-2.5">Zona</th>
              <th className="px-4 py-2.5">Servico</th>
              <th className="px-4 py-2.5">Binding</th>
              <th className="px-4 py-2.5 text-right">Sensores</th>
              <th className="px-4 py-2.5 text-right">Leituras</th>
              <th className="px-4 py-2.5">Ultima atividade</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-ink-100 dark:divide-ink-800">
            {gateways.map((gateway) => (
              <tr key={gateway.gatewayId} className="hover:bg-ink-50/60 dark:hover:bg-ink-800/30">
                <td className="px-4 py-3 font-mono text-[13px] font-semibold text-petrol-700 dark:text-petrol-300">{gateway.gatewayId}</td>
                <td className="px-4 py-3"><ZoneTag zone={gateway.zone} size="sm" /></td>
                <td className="px-4 py-3 font-mono text-xs">{gateway.service}</td>
                <td className="px-4 py-3 font-mono text-xs">{gateway.binding}</td>
                <td className="px-4 py-3 text-right num">{gateway.sensorCount}</td>
                <td className="px-4 py-3 text-right num">{gateway.totalReadings.toLocaleString('pt-PT')}</td>
                <td className="px-4 py-3">
                  <div className="font-mono text-xs">{fmtDateTime(gateway.latestAt)}</div>
                  <div className="text-[10px] text-ink-500 dark:text-ink-400">{timeAgo(gateway.latestAt)}</div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  );
}

function CoverageTab({ data }) {
  return (
    <div className="space-y-5">
      <Card padding="p-0">
        <div className="px-5 py-3 border-b border-ink-100 dark:border-ink-800">
          <SectionTitle sub="Numero de dispositivos fisicos que medem cada tipo em cada zona">Matriz de cobertura</SectionTitle>
        </div>
        <div className="overflow-x-auto scroll-thin">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-[11px] font-semibold uppercase tracking-wide text-ink-500 dark:text-ink-400 bg-ink-50 dark:bg-ink-950/40">
                <th className="px-4 py-2.5">Zona</th>
                {window.api.TIPOS.filter((type) => type !== 'VIDEO').map((type) => (
                  <th key={type} className="px-3 py-2.5 text-right">{type}</th>
                ))}
                <th className="px-4 py-2.5 text-right">Fisicos</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-ink-100 dark:divide-ink-800">
              {data.typeMatrix.map((row) => (
                <tr key={row.zone} className="hover:bg-ink-50/60 dark:hover:bg-ink-800/30">
                  <td className="px-4 py-3"><ZoneTag zone={row.zone} size="sm" /></td>
                  {window.api.TIPOS.filter((type) => type !== 'VIDEO').map((type) => (
                    <td key={type} className="px-3 py-3 text-right num font-semibold">{row.typeCounts[type] || 0}</td>
                  ))}
                  <td className="px-4 py-3 text-right num font-semibold">{row.sensorCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>

      <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
        {data.zones.map((zone) => {
          const devices = data.devices.filter((device) => device.zone === zone.zone);
          return (
            <div key={zone.zone} className="rounded-lg border border-ink-200 dark:border-ink-800 bg-white dark:bg-ink-900 p-4">
              <div className="flex items-center justify-between gap-3">
                <div className="font-semibold">{zone.label}</div>
                <span className="text-[11px] text-ink-500 dark:text-ink-400">{devices.length} dispositivos</span>
              </div>
              <div className="mt-3 space-y-2">
                {devices.map((device) => (
                  <div key={device.sensorId} className="rounded-md bg-ink-50 dark:bg-ink-800/50 px-3 py-2">
                    <div className="flex items-center justify-between gap-2">
                      <div className="font-mono text-[13px] font-semibold text-petrol-700 dark:text-petrol-300">{device.sensorId}</div>
                      <RuntimeBadge runtime={device.runtime} />
                    </div>
                    <div className="mt-2 flex flex-wrap gap-1.5">
                      {device.types.map((type) => <TypePill key={type} type={type} />)}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function ServicesTab({ services }) {
  return (
    <Card padding="p-0">
      <div className="px-5 py-3 border-b border-ink-100 dark:border-ink-800">
        <SectionTitle sub="Componentes que arrancam com docker compose up -d --build">Inventario Docker</SectionTitle>
      </div>
      <div className="overflow-x-auto scroll-thin">
        <table className="w-full text-sm">
          <thead>
            <tr className="text-left text-[11px] font-semibold uppercase tracking-wide text-ink-500 dark:text-ink-400 bg-ink-50 dark:bg-ink-950/40">
              <th className="px-4 py-2.5">Componente</th>
              <th className="px-4 py-2.5">Servico</th>
              <th className="px-4 py-2.5">Funcao</th>
              <th className="px-4 py-2.5">Porta</th>
              <th className="px-4 py-2.5">Estado esperado</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-ink-100 dark:divide-ink-800">
            {services.map((service) => (
              <tr key={`${service.name}-${service.service}`} className="hover:bg-ink-50/60 dark:hover:bg-ink-800/30">
                <td className="px-4 py-3">
                  <div className="font-semibold">{service.name}</div>
                  {service.count && <div className="text-[10px] text-ink-500 dark:text-ink-400">{service.count} instancias</div>}
                </td>
                <td className="px-4 py-3 font-mono text-xs">{service.service}</td>
                <td className="px-4 py-3 text-ink-600 dark:text-ink-300">{service.role}</td>
                <td className="px-4 py-3 font-mono text-xs">{service.port || '-'}</td>
                <td className="px-4 py-3"><StatusPill label={service.status} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  );
}

function QueuesTab({ data }) {
  const persistedExamples = data.gateways.map((gateway) => `AGREGADO_${gateway.gatewayId}`);
  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
      <div className="rounded-lg border border-ink-200 dark:border-ink-800 bg-white dark:bg-ink-900 p-4">
        <SectionTitle sub="Contrato de mensagens usado pelos sensores e gateways">RabbitMQ</SectionTitle>
        <div className="space-y-3">
          <MiniStat label="Exchange" value={data.queues.exchange} hint="publicacao dos sensores" />
          <MiniStat label="Routing key" value={data.queues.pattern} hint="zona.tipo.sensor_id" />
          <div className="rounded-md bg-ink-50 dark:bg-ink-800/50 px-3 py-2 text-sm">
            <div className="font-semibold">Bindings por gateway</div>
            <div className="mt-2 flex flex-wrap gap-1.5">
              {data.gateways.map((gateway) => (
                <span key={gateway.gatewayId} className="font-mono text-[11px] rounded-md bg-white dark:bg-ink-900 border border-ink-200 dark:border-ink-700 px-2 py-1">
                  {gateway.binding}
                </span>
              ))}
            </div>
          </div>
        </div>
      </div>

      <div className="rounded-lg border border-ink-200 dark:border-ink-800 bg-white dark:bg-ink-900 p-4">
        <SectionTitle sub="Como interpretar os IDs que aparecem nas leituras">MongoDB</SectionTitle>
        <div className="space-y-3">
          <MiniStat label="SensorId persistido" value={data.queues.persistedSensorId} hint="leituras agregadas por gateway" />
          <MiniStat label="Leituras na BD" value={data.summary.totalReadings.toLocaleString('pt-PT')} hint={`${data.summary.analysisCount} analises guardadas`} />
          <div className="rounded-md bg-ink-50 dark:bg-ink-800/50 px-3 py-2 text-xs leading-5 text-ink-600 dark:text-ink-300">
            {data.queues.note}
          </div>
          <div className="flex flex-wrap gap-1.5">
            {persistedExamples.map((id) => (
              <span key={id} className="font-mono text-[11px] rounded-md bg-white dark:bg-ink-900 border border-ink-200 dark:border-ink-700 px-2 py-1">
                {id}
              </span>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

function SystemPage() {
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);
  const [activeTab, setActiveTab] = useState('topology');
  const [lastUpdated, setLastUpdated] = useState(null);
  const [refreshing, setRefreshing] = useState(false);

  const loadSystem = ({ silent = false } = {}) => {
    if (!silent) setRefreshing(true);
    setError(null);
    window.api.getSystem()
      .then((result) => {
        setData(result);
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
    loadSystem();
    const id = setInterval(() => loadSystem({ silent: true }), 30000);
    return () => clearInterval(id);
  }, []);

  if (error && !data) return <ErrorState error={error} retry={loadSystem} />;
  if (!data) return <Loading />;

  const summary = data.summary;
  let content = <TopologyTab data={data} />;
  if (activeTab === 'gateways') content = <GatewaysTab gateways={data.gateways} />;
  else if (activeTab === 'coverage') content = <CoverageTab data={data} />;
  else if (activeTab === 'services') content = <ServicesTab services={data.services} />;
  else if (activeTab === 'queues') content = <QueuesTab data={data} />;

  return (
    <div className="px-4 lg:px-8 py-6 space-y-5">
      <div className="grid grid-cols-2 md:grid-cols-3 xl:grid-cols-6 gap-3">
        <KPI label="Zonas" value={summary.zones} hint="areas monitorizadas" accent="ink" icon={Icon.Dashboard} />
        <KPI label="Gateways" value={summary.gateways} hint="1 por zona" accent="petrol" icon={Icon.Server} />
        <KPI label="Fisicos" value={summary.physicalSensors} hint="dispositivos reais" accent="moss" icon={Icon.Sensors} />
        <KPI label="Logicos" value={summary.logicalSensors} hint="medicoes instaladas" accent="ink" icon={Icon.Pulse} />
        <KPI label="Node.js" value={summary.nodeSensors} hint="publishers automaticos" accent="moss" icon={Icon.Sensors} />
        <KPI label="C#" value={summary.csharpSensors} hint="publishers alternativos" accent="amber" icon={Icon.Sensors} />
      </div>

      <div className="flex items-center justify-between gap-3 flex-wrap">
        <SystemTabs active={activeTab} setActive={setActiveTab} />
        <div className="flex items-center gap-2">
          <RefreshMeta lastUpdated={lastUpdated} refreshing={refreshing} />
          <Button variant="outline" icon={Icon.Refresh} onClick={() => loadSystem()} disabled={refreshing}>Atualizar</Button>
        </div>
      </div>

      {error && <InlineError error={error} retry={() => loadSystem()} />}
      {content}
    </div>
  );
}

window.SystemPage = SystemPage;
