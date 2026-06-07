// =====================================================================
// ui.jsx — Primitivas + chrome (Sidebar, Topbar, Card, Badge, etc.)
// =====================================================================

const { useState, useEffect, useMemo, useRef, useCallback } = React;

// ----------------------- Tema (claro/escuro) -------------------------
function useTheme() {
  const [theme, setTheme] = useState(() => {
    try {
      const stored = localStorage.getItem('onehealth.theme');
      if (stored) return stored;
    } catch (_) {}
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  });
  useEffect(() => {
    const root = document.documentElement;
    if (theme === 'dark') root.classList.add('dark');
    else root.classList.remove('dark');
    try { localStorage.setItem('onehealth.theme', theme); } catch (_) {}
  }, [theme]);
  return [theme, setTheme];
}

// ----------------------- Hash router -------------------------------
function useHashRoute() {
  const [route, setRoute] = useState(() => window.location.hash.replace(/^#/, '') || '/');
  useEffect(() => {
    const onChange = () => setRoute(window.location.hash.replace(/^#/, '') || '/');
    window.addEventListener('hashchange', onChange);
    return () => window.removeEventListener('hashchange', onChange);
  }, []);
  const nav = (to) => { window.location.hash = to; };
  return [route, nav];
}

// ----------------------- Ícones (SVG simples) -------------------------
const Icon = {
  Dashboard: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <rect x="3" y="3" width="7" height="9" rx="1.5" /><rect x="14" y="3" width="7" height="5" rx="1.5" />
      <rect x="14" y="12" width="7" height="9" rx="1.5" /><rect x="3" y="16" width="7" height="5" rx="1.5" />
    </svg>
  ),
  Readings: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <path d="M3 3v18h18" /><path d="M7 14l4-5 3 4 5-7" />
    </svg>
  ),
  Analyses: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <rect x="3" y="13" width="4" height="8" rx="1" /><rect x="10" y="8" width="4" height="13" rx="1" /><rect x="17" y="3" width="4" height="18" rx="1" />
    </svg>
  ),
  Forecast: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <path d="M4 18l4-4 3 3 5-7 4 5" /><path d="M14 6h6v6" />
    </svg>
  ),
  Sensors: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <circle cx="12" cy="12" r="2" /><path d="M7.05 16.95a7 7 0 010-9.9M16.95 7.05a7 7 0 010 9.9" />
      <path d="M4.22 19.78a11 11 0 010-15.56M19.78 4.22a11 11 0 010 15.56" />
    </svg>
  ),
  System: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <rect x="3" y="4" width="6" height="5" rx="1.4" />
      <rect x="15" y="4" width="6" height="5" rx="1.4" />
      <rect x="9" y="15" width="6" height="5" rx="1.4" />
      <path d="M9 6.5h6M12 9v6M6 9v4.5h6M18 9v4.5h-6" />
    </svg>
  ),
  Server: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <rect x="4" y="3" width="16" height="7" rx="1.5" />
      <rect x="4" y="14" width="16" height="7" rx="1.5" />
      <path d="M8 6.5h.01M8 17.5h.01M12 6.5h5M12 17.5h5" />
    </svg>
  ),
  Database: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <ellipse cx="12" cy="5" rx="7" ry="3" />
      <path d="M5 5v6c0 1.66 3.13 3 7 3s7-1.34 7-3V5" />
      <path d="M5 11v6c0 1.66 3.13 3 7 3s7-1.34 7-3v-6" />
    </svg>
  ),
  Sun: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.41-1.41M17.66 6.34l1.41-1.41" />
    </svg>
  ),
  Moon: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <path d="M21 12.8A9 9 0 1111.2 3a7 7 0 009.8 9.8z" />
    </svg>
  ),
  Bell: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <path d="M6 8a6 6 0 0112 0c0 7 3 7 3 9H3c0-2 3-2 3-9z" /><path d="M10.3 21a1.94 1.94 0 003.4 0" />
    </svg>
  ),
  Menu: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" {...p}><path d="M4 6h16M4 12h16M4 18h16" /></svg>
  ),
  X: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" {...p}><path d="M6 6l12 12M18 6L6 18" /></svg>
  ),
  ArrowUp: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M12 19V5M5 12l7-7 7 7" /></svg>
  ),
  ArrowRight: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M5 12h14M12 5l7 7-7 7" /></svg>
  ),
  ArrowDown: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" {...p}><path d="M12 5v14M5 12l7 7 7-7" /></svg>
  ),
  Download: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <path d="M12 3v12M7 10l5 5 5-5M5 21h14" />
    </svg>
  ),
  Search: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <circle cx="11" cy="11" r="7" /><path d="M21 21l-4.3-4.3" />
    </svg>
  ),
  Pulse: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <path d="M3 12h4l3-7 4 14 3-7h4" />
    </svg>
  ),
  Refresh: (p) => (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" {...p}>
      <path d="M21 12a9 9 0 01-15.1 6.6" /><path d="M3 12a9 9 0 0115.1-6.6" />
      <path d="M18 2v4h-4M6 22v-4h4" />
    </svg>
  ),
};

// ----------------------- Logo -----------------------------------------
function Logo({ size = 28 }) {
  return (
    <div className="flex items-center gap-2.5">
      <svg width={size} height={size} viewBox="0 0 32 32">
        <defs>
          <linearGradient id="og" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0%" stopColor="#236471" />
            <stop offset="100%" stopColor="#5a8a5e" />
          </linearGradient>
        </defs>
        <circle cx="16" cy="16" r="14" fill="url(#og)" />
        <path d="M9 17l3.5-3.5L15 16l4.5-5L23 14" stroke="#fff" strokeWidth="1.8" fill="none" strokeLinecap="round" strokeLinejoin="round" />
        <circle cx="9" cy="17" r="1.5" fill="#fff" />
        <circle cx="23" cy="14" r="1.5" fill="#fff" />
      </svg>
      <div className="leading-tight">
        <div className="text-[15px] font-semibold tracking-tight">One Health</div>
        <div className="text-[10px] uppercase tracking-[0.14em] text-ink-400 dark:text-ink-500">Monitorização urbana</div>
      </div>
    </div>
  );
}

// ----------------------- Sidebar --------------------------------------
const NAV = [
  { to: '/',          label: 'Dashboard',     icon: Icon.Dashboard },
  { to: '/leituras',  label: 'Leituras',      icon: Icon.Readings },
  { to: '/analises',  label: 'Análises',      icon: Icon.Analyses },
  { to: '/nova',      label: 'Nova análise',  icon: Icon.Forecast },
  { to: '/sensores',  label: 'Sensores',      icon: Icon.Sensors },
  { to: '/sistema',   label: 'Sistema',       icon: Icon.System },
];

function Sidebar({ route, nav, open, setOpen }) {
  const active = (to) => {
    if (to === '/') return route === '/' || route === '';
    return route === to || route.startsWith(to + '/');
  };
  return (
    <React.Fragment>
      {/* Backdrop em mobile */}
      {open && (
        <div className="lg:hidden fixed inset-0 bg-ink-900/40 z-30" onClick={() => setOpen(false)} />
      )}
      <aside
        className={
          'fixed lg:static z-40 inset-y-0 left-0 w-[248px] flex-shrink-0 ' +
          'bg-white dark:bg-ink-900 border-r border-ink-200 dark:border-ink-800 ' +
          'transition-transform duration-200 ' +
          (open ? 'translate-x-0' : '-translate-x-full lg:translate-x-0')
        }
      >
        <div className="h-16 flex items-center px-5 border-b border-ink-100 dark:border-ink-800">
          <Logo />
        </div>
        <nav className="px-3 py-4 space-y-0.5">
          {NAV.map((n) => {
            const isActive = active(n.to);
            return (
              <a
                key={n.to}
                href={`#${n.to}`}
                onClick={() => setOpen(false)}
                className={
                  'flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium transition-colors ' +
                  (isActive
                    ? 'bg-petrol-50 text-petrol-800 dark:bg-petrol-900/40 dark:text-petrol-100'
                    : 'text-ink-600 dark:text-ink-300 hover:bg-ink-100 dark:hover:bg-ink-800/60')
                }
              >
                <n.icon width={18} height={18} className={isActive ? 'text-petrol-600 dark:text-petrol-300' : 'text-ink-400 dark:text-ink-500'} />
                <span>{n.label}</span>
                {isActive && <span className="ml-auto w-1.5 h-1.5 rounded-full bg-petrol-500" />}
              </a>
            );
          })}
        </nav>
        <div className="absolute bottom-0 left-0 right-0 p-4 border-t border-ink-100 dark:border-ink-800">
          <div className="rounded-lg bg-ink-50 dark:bg-ink-800/60 px-3 py-2.5">
            <div className="flex items-center gap-2 text-[11px] uppercase tracking-[0.1em] text-ink-400 dark:text-ink-500">
              <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse" />
              Servidor ligado
            </div>
            <div className="mt-1 font-mono text-[11px] text-ink-500 dark:text-ink-400">srv-onehealth-01</div>
            <div className="text-[11px] text-ink-500 dark:text-ink-400">MongoDB • gRPC análise</div>
          </div>
        </div>
      </aside>
    </React.Fragment>
  );
}

// ----------------------- Topbar ---------------------------------------
function Topbar({ title, subtitle, onMenu, theme, setTheme, alerts }) {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), 1000);
    return () => clearInterval(id);
  }, []);

  return (
    <header className="h-16 flex items-center justify-between gap-3 px-4 lg:px-8 border-b border-ink-100 dark:border-ink-800 bg-white/85 dark:bg-ink-900/85 backdrop-blur sticky top-0 z-20">
      <div className="flex items-center gap-3 min-w-0">
        <button onClick={onMenu} className="lg:hidden p-2 -ml-2 rounded-md hover:bg-ink-100 dark:hover:bg-ink-800">
          <Icon.Menu width={20} height={20} />
        </button>
        <div className="min-w-0">
          <h1 className="text-lg font-semibold tracking-tight truncate">{title}</h1>
          {subtitle && <div className="text-xs text-ink-500 dark:text-ink-400 truncate">{subtitle}</div>}
        </div>
      </div>
      <div className="flex items-center gap-2">
        <div className="hidden md:flex items-center gap-2 px-3 py-1.5 rounded-md bg-ink-50 dark:bg-ink-800/60 text-xs text-ink-500 dark:text-ink-400">
          <span className="w-1.5 h-1.5 rounded-full bg-emerald-500" />
          <span className="font-mono">{now.toLocaleString('pt-PT', { hour12: false })}</span>
        </div>
        <div className="relative">
          <button className="p-2 rounded-md hover:bg-ink-100 dark:hover:bg-ink-800 relative">
            <Icon.Bell width={18} height={18} />
            {alerts > 0 && (
              <span className="absolute -top-0.5 -right-0.5 min-w-[16px] h-4 px-1 rounded-full bg-rose-500 text-white text-[10px] font-semibold flex items-center justify-center">
                {alerts}
              </span>
            )}
          </button>
        </div>
        <button
          onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}
          className="p-2 rounded-md hover:bg-ink-100 dark:hover:bg-ink-800"
          aria-label="Alternar tema"
          title={theme === 'dark' ? 'Tema claro' : 'Tema escuro'}
        >
          {theme === 'dark' ? <Icon.Sun width={18} height={18} /> : <Icon.Moon width={18} height={18} />}
        </button>
        <div className="hidden sm:flex items-center gap-2 ml-1 pl-3 border-l border-ink-200 dark:border-ink-800">
          <div className="w-8 h-8 rounded-full bg-gradient-to-br from-petrol-500 to-moss-500 flex items-center justify-center text-white text-xs font-semibold">SD</div>
          <div className="hidden md:block">
            <div className="text-xs font-medium leading-tight">Sistemas Distribuídos</div>
            <div className="text-[10px] text-ink-500 dark:text-ink-400">TP1 · UTAD</div>
          </div>
        </div>
      </div>
    </header>
  );
}

// ----------------------- Card -----------------------------------------
function Card({ children, className = '', padding = 'p-5' }) {
  return (
    <div className={`rounded-xl bg-white dark:bg-ink-900 border border-ink-200/70 dark:border-ink-800 ${padding} ${className}`}>
      {children}
    </div>
  );
}
function SectionTitle({ children, sub, action }) {
  return (
    <div className="flex items-end justify-between mb-3">
      <div>
        <h2 className="text-[13px] font-semibold uppercase tracking-[0.08em] text-ink-500 dark:text-ink-400">{children}</h2>
        {sub && <div className="text-xs text-ink-400 dark:text-ink-500 mt-0.5">{sub}</div>}
      </div>
      {action}
    </div>
  );
}

// ----------------------- Badges --------------------------------------
const ALERT_STYLES = {
  NORMAL:   { dot: 'bg-emerald-500', pill: 'bg-emerald-50 text-emerald-700 ring-emerald-200 dark:bg-emerald-500/10 dark:text-emerald-300 dark:ring-emerald-500/30', label: 'Normal' },
  WARNING:  { dot: 'bg-amber-500',   pill: 'bg-amber-50 text-amber-800 ring-amber-200 dark:bg-amber-500/10 dark:text-amber-300 dark:ring-amber-500/30', label: 'Aviso' },
  CRITICAL: { dot: 'bg-rose-500',    pill: 'bg-rose-50 text-rose-700 ring-rose-200 dark:bg-rose-500/10 dark:text-rose-300 dark:ring-rose-500/30', label: 'Crítico' },
};
function AlertBadge({ level, size = 'md' }) {
  const s = ALERT_STYLES[level] || ALERT_STYLES.NORMAL;
  const sz = size === 'sm' ? 'text-[10px] px-1.5 py-0.5' : 'text-[11px] px-2 py-0.5';
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full ring-1 font-semibold ${sz} ${s.pill}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${s.dot}`} />{s.label}
    </span>
  );
}
function TrendBadge({ trend }) {
  if (trend === 'Increasing') return (
    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-rose-50 dark:bg-rose-500/10 text-rose-700 dark:text-rose-300 text-[11px] font-semibold ring-1 ring-rose-200 dark:ring-rose-500/30">
      <Icon.ArrowUp width={11} height={11} />Crescente
    </span>
  );
  if (trend === 'Decreasing') return (
    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-emerald-50 dark:bg-emerald-500/10 text-emerald-700 dark:text-emerald-300 text-[11px] font-semibold ring-1 ring-emerald-200 dark:ring-emerald-500/30">
      <Icon.ArrowDown width={11} height={11} />Decrescente
    </span>
  );
  return (
    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-ink-100 dark:bg-ink-800 text-ink-600 dark:text-ink-300 text-[11px] font-semibold ring-1 ring-ink-200 dark:ring-ink-700">
      <Icon.ArrowRight width={11} height={11} />Estável
    </span>
  );
}

function ZoneTag({ zone, size = 'md' }) {
  const sz = size === 'sm' ? 'text-[10px] px-1.5 py-0.5' : 'text-[11px] px-2 py-0.5';
  return <span className={`inline-flex items-center gap-1 rounded-md bg-ink-100 dark:bg-ink-800 text-ink-700 dark:text-ink-200 font-medium ${sz}`}>
    <span className="w-1 h-1 rounded-full bg-petrol-500" />{window.api.ZONA_LABEL[zone] || zone}
  </span>;
}

function TypePill({ type }) {
  const colors = {
    TEMP: 'bg-orange-50 text-orange-700 dark:bg-orange-500/10 dark:text-orange-300',
    HUM: 'bg-sky-50 text-sky-700 dark:bg-sky-500/10 dark:text-sky-300',
    RUIDO: 'bg-violet-50 text-violet-700 dark:bg-violet-500/10 dark:text-violet-300',
    'PM2.5': 'bg-stone-100 text-stone-700 dark:bg-stone-500/10 dark:text-stone-300',
    PM10: 'bg-stone-100 text-stone-700 dark:bg-stone-500/10 dark:text-stone-300',
    LUZ: 'bg-yellow-50 text-yellow-700 dark:bg-yellow-500/10 dark:text-yellow-300',
    AR: 'bg-teal-50 text-teal-700 dark:bg-teal-500/10 dark:text-teal-300',
  };
  return <span className={`inline-flex items-center text-[11px] font-semibold rounded-md px-1.5 py-0.5 ${colors[type] || 'bg-ink-100 text-ink-700'}`}>{type}</span>;
}

// ----------------------- Buttons --------------------------------------
function Button({ children, variant = 'primary', className = '', icon: IconComp, ...rest }) {
  const base = 'inline-flex items-center justify-center gap-2 rounded-md text-sm font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed';
  const sizes = 'px-3.5 py-2';
  const variants = {
    primary: 'bg-petrol-600 hover:bg-petrol-700 text-white shadow-sm',
    secondary: 'bg-ink-100 dark:bg-ink-800 hover:bg-ink-200 dark:hover:bg-ink-700 text-ink-800 dark:text-ink-100',
    ghost: 'bg-transparent hover:bg-ink-100 dark:hover:bg-ink-800 text-ink-700 dark:text-ink-200',
    outline: 'border border-ink-200 dark:border-ink-700 hover:bg-ink-50 dark:hover:bg-ink-800 text-ink-700 dark:text-ink-200',
  };
  return (
    <button className={`${base} ${sizes} ${variants[variant]} ${className}`} {...rest}>
      {IconComp && <IconComp width={15} height={15} />}{children}
    </button>
  );
}

// ----------------------- Form fields -----------------------------------
function Field({ label, children, hint, className = '' }) {
  return (
    <label className={`block ${className}`}>
      <span className="block text-[11px] font-medium uppercase tracking-wide text-ink-500 dark:text-ink-400 mb-1">{label}</span>
      {children}
      {hint && <span className="block text-[11px] text-ink-400 dark:text-ink-500 mt-1">{hint}</span>}
    </label>
  );
}
const inputClass = 'w-full bg-white dark:bg-ink-900 border border-ink-200 dark:border-ink-700 rounded-md px-3 py-2 text-sm text-ink-800 dark:text-ink-100 placeholder-ink-400 focus:outline-none focus:ring-2 focus:ring-petrol-500/40 focus:border-petrol-500';
function Select({ value, onChange, options, placeholder = 'Todos', className = '', required = false }) {
  const [open, setOpen] = useState(false);
  const ref = useRef(null);

  useEffect(() => {
    const close = (e) => { if (ref.current && !ref.current.contains(e.target)) setOpen(false); };
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, []);

  const normalized = options.map((o) => (typeof o === 'string' ? { value: o, label: o } : o));
  const selected = normalized.find((o) => o.value === value);

  const pick = (v) => { onChange(v || null); setOpen(false); };

  const chevron = (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"
      className={`flex-shrink-0 text-ink-400 transition-transform duration-150 ${open ? 'rotate-180' : ''}`}>
      <path d="M6 9l6 6 6-6" />
    </svg>
  );

  return (
    <div ref={ref} className={`relative ${className}`}>
      <button
        type="button"
        className={`${inputClass} w-full flex items-center justify-between gap-2 cursor-pointer text-left`}
        onClick={() => setOpen((o) => !o)}
      >
        <span className={`truncate ${selected ? '' : 'text-ink-400 dark:text-ink-500'}`}>
          {selected ? selected.label : placeholder}
        </span>
        {chevron}
      </button>

      {open && (
        <div className="absolute z-50 mt-1 w-full min-w-max bg-white dark:bg-ink-900 border border-ink-200 dark:border-ink-700 rounded-md shadow-lg overflow-hidden">
          <div className="py-1 max-h-60 overflow-y-auto">
            {!required && (
              <button
                type="button"
                onClick={() => pick('')}
                className={`w-full text-left px-3 py-2 text-sm transition-colors ${
                  !value ? 'bg-petrol-50 dark:bg-petrol-900/40 text-petrol-800 dark:text-petrol-100 font-medium'
                         : 'text-ink-400 dark:text-ink-500 hover:bg-ink-50 dark:hover:bg-ink-800/60'
                }`}
              >
                {placeholder}
              </button>
            )}
            {normalized.map((o) => (
              <button
                key={o.value}
                type="button"
                onClick={() => pick(o.value)}
                className={`w-full text-left px-3 py-2 text-sm transition-colors ${
                  o.value === value
                    ? 'bg-petrol-50 dark:bg-petrol-900/40 text-petrol-800 dark:text-petrol-100 font-medium'
                    : 'text-ink-700 dark:text-ink-200 hover:bg-ink-50 dark:hover:bg-ink-800/60'
                }`}
              >
                {o.label}
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
function Input(props) { return <input className={inputClass} {...props} />; }

// ----------------------- Data / tabela helpers --------------------------
function dateToLocalInputValue(date) {
  const pad = (v) => String(v).padStart(2, '0');
  return [
    date.getFullYear(),
    pad(date.getMonth() + 1),
    pad(date.getDate()),
  ].join('-') + `T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
function datePresetRange(preset) {
  const now = new Date();
  let from = new Date(now);
  if (preset === 'hour') from = new Date(now.getTime() - 60 * 60 * 1000);
  else if (preset === 'day') from = new Date(now.getTime() - 24 * 60 * 60 * 1000);
  else if (preset === 'week') from = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);
  else if (preset === 'month') from = new Date(now.getFullYear(), now.getMonth(), 1, 0, 0, 0, 0);
  return { from: dateToLocalInputValue(from), to: dateToLocalInputValue(now) };
}
function DatePresetButtons({ onApply }) {
  const presets = [
    { key: 'hour', label: 'Ultima hora' },
    { key: 'day', label: 'Ultimas 24h' },
    { key: 'week', label: 'Ultimos 7 dias' },
    { key: 'month', label: 'Este mes' },
  ];
  return (
    <div className="flex flex-wrap gap-1.5">
      {presets.map((preset) => (
        <button
          key={preset.key}
          type="button"
          onClick={() => onApply(datePresetRange(preset.key))}
          className="rounded-md border border-ink-200 dark:border-ink-700 px-2.5 py-1.5 text-xs font-medium text-ink-600 dark:text-ink-300 hover:bg-ink-50 dark:hover:bg-ink-800"
        >
          {preset.label}
        </button>
      ))}
    </div>
  );
}
function sortRows(rows, sort, accessors = {}) {
  if (!sort?.key) return rows;
  const direction = sort.direction === 'desc' ? -1 : 1;
  const getValue = accessors[sort.key] || ((row) => row[sort.key]);
  return rows.slice().sort((a, b) => {
    const av = getValue(a);
    const bv = getValue(b);
    if (av == null && bv == null) return 0;
    if (av == null) return 1;
    if (bv == null) return -1;
    const an = typeof av === 'number' ? av : Number(av);
    const bn = typeof bv === 'number' ? bv : Number(bv);
    if (!Number.isNaN(an) && !Number.isNaN(bn) && String(av).trim() !== '' && String(bv).trim() !== '') {
      return (an - bn) * direction;
    }
    const ad = av instanceof Date ? av.getTime() : Date.parse(av);
    const bd = bv instanceof Date ? bv.getTime() : Date.parse(bv);
    if (!Number.isNaN(ad) && !Number.isNaN(bd)) return (ad - bd) * direction;
    return String(av).localeCompare(String(bv), 'pt-PT', { numeric: true, sensitivity: 'base' }) * direction;
  });
}
function SortHeader({ label, sortKey, sort, onSort, align = 'left', className = '' }) {
  const active = sort?.key === sortKey;
  const nextDirection = active && sort.direction === 'asc' ? 'desc' : 'asc';
  const IconComp = active && sort.direction === 'desc' ? Icon.ArrowDown : Icon.ArrowUp;
  return (
    <th className={`px-4 py-2.5 ${className}`}>
      <button
        type="button"
        onClick={() => onSort({ key: sortKey, direction: nextDirection })}
        className={`inline-flex w-full items-center gap-1.5 ${align === 'right' ? 'justify-end text-right' : 'justify-start text-left'} hover:text-petrol-700 dark:hover:text-petrol-300`}
      >
        <span>{label}</span>
        <IconComp width={11} height={11} className={active ? 'opacity-100' : 'opacity-25'} />
      </button>
    </th>
  );
}
function InlineError({ error, retry }) {
  const message = error?.message || String(error || 'Tente novamente.');
  return (
    <div className="alert-error rounded-md bg-rose-50 dark:bg-rose-500/10 text-rose-700 dark:text-rose-200 ring-1 ring-rose-200 dark:ring-rose-500/30 px-3 py-2 text-sm">
      Erro ao carregar dados: {message}. Tente novamente.
      {retry && <button type="button" onClick={retry} className="ml-2 font-semibold underline underline-offset-2">Repetir</button>}
    </div>
  );
}
function RefreshMeta({ lastUpdated, refreshing }) {
  const [, setTick] = useState(0);
  useEffect(() => {
    const id = setInterval(() => setTick((v) => v + 1), 30000);
    return () => clearInterval(id);
  }, []);
  if (refreshing) return <span className="text-[11px] text-ink-500 dark:text-ink-400">a atualizar...</span>;
  if (!lastUpdated) return <span className="text-[11px] text-ink-500 dark:text-ink-400">ainda sem atualizacao</span>;
  return <span className="text-[11px] text-ink-500 dark:text-ink-400">ultima atualizacao {timeAgo(lastUpdated.toISOString())}</span>;
}

function ToastStack({ toasts, onDismiss }) {
  if (!toasts || toasts.length === 0) return null;
  return (
    <div className="fixed right-4 bottom-4 z-50 w-[min(360px,calc(100vw-2rem))] space-y-2">
      {toasts.map((toast) => {
        const r = toast.latest;
        return (
          <div key={toast.id} className="rounded-lg bg-white dark:bg-ink-900 border border-rose-200 dark:border-rose-500/40 shadow-lg px-3.5 py-3">
            <div className="flex items-start gap-3">
              <div className="mt-1 w-2 h-2 rounded-full bg-rose-500" />
              <div className="min-w-0 flex-1">
                <div className="text-sm font-semibold text-ink-900 dark:text-ink-100">
                  Novo alerta CRITICAL{toast.count > 1 ? ` (${toast.count})` : ''}
                </div>
                <div className="mt-0.5 text-xs text-ink-600 dark:text-ink-300 truncate">
                  {r.sensorId} · {window.api.ZONA_LABEL[r.zone] || r.zone} · {r.type} · {fmtNumber(r.value, r.type)} {r.unit}
                </div>
              </div>
              <button
                type="button"
                onClick={() => onDismiss(toast.id)}
                className="p-1 rounded-md text-ink-400 hover:text-ink-700 dark:hover:text-ink-100 hover:bg-ink-100 dark:hover:bg-ink-800"
                aria-label="Fechar notificacao"
              >
                <Icon.X width={14} height={14} />
              </button>
            </div>
          </div>
        );
      })}
    </div>
  );
}

// ----------------------- Empty / Loading -------------------------------
function Loading({ label = 'A carregar…' }) {
  return (
    <div className="flex items-center gap-3 text-sm text-ink-500 dark:text-ink-400 py-12 justify-center">
      <svg className="animate-spin" width="18" height="18" viewBox="0 0 24 24" fill="none">
        <circle cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="2.5" strokeOpacity=".25" />
        <path d="M22 12a10 10 0 00-10-10" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" />
      </svg>
      {label}
    </div>
  );
}
function Empty({ title, hint }) {
  return (
    <div className="text-center py-12 px-4">
      <div className="w-12 h-12 mx-auto rounded-full bg-ink-100 dark:bg-ink-800 flex items-center justify-center text-ink-400">
        <Icon.Search width={20} height={20} />
      </div>
      <div className="mt-3 text-sm font-medium text-ink-700 dark:text-ink-200">{title || 'Sem dados'}</div>
      {hint && <div className="text-xs text-ink-500 dark:text-ink-400 mt-1">{hint}</div>}
    </div>
  );
}
function ErrorState({ title = 'Erro ao carregar dados', error, retry }) {
  const message = error?.message || String(error || 'Tente novamente mais tarde.');
  return (
    <div className="px-4 lg:px-8 py-6">
      <div className="rounded-lg bg-rose-50 dark:bg-rose-500/10 ring-1 ring-rose-200 dark:ring-rose-500/30 px-4 py-3 text-rose-700 dark:text-rose-200">
        <div className="text-sm font-semibold">{title}</div>
        <div className="mt-1 text-xs text-rose-600 dark:text-rose-300 break-words">{message}</div>
        {retry && (
          <button onClick={retry} className="mt-3 text-xs font-semibold rounded-md px-2.5 py-1.5 bg-white/80 dark:bg-ink-900/70 ring-1 ring-rose-200 dark:ring-rose-500/30 hover:bg-white dark:hover:bg-ink-900">
            Tentar novamente
          </button>
        )}
      </div>
    </div>
  );
}

// ----------------------- Utils --------------------------------------
function fmtNumber(v, type) {
  if (v == null || Number.isNaN(v)) return '—';
  if (type === 'LUZ') return Math.round(v).toLocaleString('pt-PT');
  return Number(v).toLocaleString('pt-PT', { minimumFractionDigits: 1, maximumFractionDigits: 2 });
}
function fmtDateTime(iso) {
  if (!iso) return '—';
  const d = new Date(iso);
  return d.toLocaleString('pt-PT', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
}
function fmtTime(iso) {
  if (!iso) return '—';
  return new Date(iso).toLocaleTimeString('pt-PT', { hour: '2-digit', minute: '2-digit', hour12: false });
}
function timeAgo(iso) {
  if (!iso) return '—';
  // Relativo à hora atual (dados reais vindos da BD).
  const d = Date.now() - new Date(iso).getTime();
  const mins = Math.round(d / 60000);
  if (mins < 1) return 'agora';
  if (mins < 60) return `há ${mins} min`;
  const hours = Math.round(mins / 60);
  if (hours < 24) return `há ${hours} h`;
  const days = Math.round(hours / 24);
  return `há ${days} d`;
}

// Export to window for other files
Object.assign(window, {
  useState, useEffect, useMemo, useRef, useCallback,
  useTheme, useHashRoute,
  Icon, Logo, Sidebar, Topbar, Card, SectionTitle,
  AlertBadge, TrendBadge, ZoneTag, TypePill, Button,
  Field, Select, Input, inputClass, Loading, Empty, ErrorState,
  DatePresetButtons, sortRows, SortHeader, InlineError, RefreshMeta, ToastStack,
  ALERT_STYLES, NAV,
  fmtNumber, fmtDateTime, fmtTime, timeAgo,
});
