// =====================================================================
// api.js — One Health (camada de acesso a dados)
// Ligado ao backend real: GET /api/readings, /api/analyses, /api/sensors.
// Os dicionários, a classificação de alertas e os cálculos de
// análise/previsão correm no cliente (mantidos do protótipo).
// =====================================================================

(function () {
  'use strict';

  // ------------------------ Enums --------------------------------------
  const PROTOCOL = window.ProtocolConstants;
  const TIPOS = PROTOCOL.SENSOR_TYPES;
  const UNIDADES = PROTOCOL.UNITS_BY_TYPE;
  const ZONAS = PROTOCOL.ZONES;
  const ESTADOS = PROTOCOL.SENSOR_STATES;
  const ESTRATEGIAS = ['linear', 'ewma'];
  const FORMATOS = ['JSON', 'XML', 'CSV'];

  const ZONA_LABEL = PROTOCOL.ZONE_LABELS;

  // ----------------------- Limiares ------------------------------------
  // Devolve "NORMAL" | "WARNING" | "CRITICAL" (espelha o serviço de análise Python)
  function classifyAlert(type, value) {
    const v = Number(value);
    if (Number.isNaN(v)) return 'NORMAL';
    switch (type) {
      case 'TEMP':
        if (v > 38 || v < -15) return 'CRITICAL';
        if (v > 32 || v < -5) return 'WARNING';
        return 'NORMAL';
      case 'HUM':
        if (v > 95 || v < 10) return 'WARNING';
        return 'NORMAL';
      case 'AR':
      case 'PM2.5':
      case 'PM10':
        if (v > 200) return 'CRITICAL';
        if (v > 75) return 'WARNING';
        return 'NORMAL';
      case 'RUIDO':
        if (v > 85) return 'CRITICAL';
        if (v > 65) return 'WARNING';
        return 'NORMAL';
      case 'LUZ':
        if (v > 80000) return 'WARNING';
        return 'NORMAL';
      default:
        return 'NORMAL';
    }
  }
  const ALERT_RANK = { NORMAL: 0, WARNING: 1, CRITICAL: 2 };
  const worstAlert = (a, b) => (ALERT_RANK[a] >= ALERT_RANK[b] ? a : b);

  // --------------------- Helpers estatísticos --------------------------
  function round(v, p = 2) {
    const m = Math.pow(10, p);
    return Math.round(v * m) / m;
  }
  function quantile(sortedArr, q) {
    const pos = (sortedArr.length - 1) * q;
    const base = Math.floor(pos);
    const rest = pos - base;
    if (sortedArr[base + 1] !== undefined) {
      return sortedArr[base] + rest * (sortedArr[base + 1] - sortedArr[base]);
    }
    return sortedArr[base];
  }
  function statsOf(values) {
    const sorted = values.slice().sort((a, b) => a - b);
    const n = sorted.length;
    const avg = sorted.reduce((s, v) => s + v, 0) / n;
    const variance = sorted.reduce((s, v) => s + (v - avg) * (v - avg), 0) / Math.max(1, n - 1);
    const sd = Math.sqrt(variance);
    return {
      average: avg, median: quantile(sorted, 0.5),
      standardDeviation: sd,
      percentile25: quantile(sorted, 0.25),
      percentile75: quantile(sorted, 0.75),
      percentile95: quantile(sorted, 0.95),
      min: sorted[0], max: sorted[n - 1],
      sampleCount: n,
    };
  }
  function trendOf(points) {
    const n = points.length;
    if (n < 2) return { slope: 0, classification: 'Stable' };
    let sx = 0, sy = 0, sxy = 0, sxx = 0;
    for (let i = 0; i < n; i++) { sx += i; sy += points[i]; sxy += i * points[i]; sxx += i * i; }
    const slope = (n * sxy - sx * sy) / (n * sxx - sx * sx);
    const cls = slope > 0.05 ? 'Increasing' : slope < -0.05 ? 'Decreasing' : 'Stable';
    return { slope, classification: cls };
  }
  function forecastLinear(values, n) {
    const tr = trendOf(values);
    const last = values[values.length - 1];
    const out = [];
    for (let i = 1; i <= n; i++) out.push(round(last + tr.slope * i, 3));
    return out;
  }
  function forecastEWMA(values, n, alpha = 0.35) {
    let ewma = values[0];
    for (let i = 1; i < values.length; i++) ewma = alpha * values[i] + (1 - alpha) * ewma;
    return new Array(n).fill(0).map(() => round(ewma, 3));
  }

  // --------------------- Acesso HTTP -----------------------------------
  async function http(path, params) {
    let url = path;
    if (params) {
      const qs = new URLSearchParams();
      Object.entries(params).forEach(([k, v]) => {
        if (v !== undefined && v !== null && v !== '') qs.append(k, v);
      });
      const s = qs.toString();
      if (s) url += `?${s}`;
    }
    const res = await fetch(url, { headers: { Accept: 'application/json' } });
    if (!res.ok) {
      const msg = await res.text().catch(() => res.statusText);
      throw new Error(`API ${res.status}: ${msg || res.statusText}`);
    }
    return res.json();
  }

  function computeAnalysis({ type, zone, sensorId }, readings) {
    const chronological = readings.slice().sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp));
    const values = chronological.map((r) => r.value);
    const s = statsOf(values);
    const tr = trendOf(values);
    const outlierCount = values.filter((v) => v > s.average + 2 * s.standardDeviation || v < s.average - 2 * s.standardDeviation).length;
    const movingWindow = values.slice(-5);
    const movingAverageLast = movingWindow.reduce((a, b) => a + b, 0) / movingWindow.length;
    const worst = chronological.reduce((acc, r) => worstAlert(acc, classifyAlert(r.type, r.value)), 'NORMAL');
    return {
      zone, type, sensorId: sensorId || undefined,
      windowStart: chronological[0].timestamp,
      windowEnd: chronological[chronological.length - 1].timestamp,
      average: round(s.average, 3), median: round(s.median, 3),
      standardDeviation: round(s.standardDeviation, 3),
      percentile25: round(s.percentile25, 3),
      percentile75: round(s.percentile75, 3),
      percentile95: round(s.percentile95, 3),
      min: round(s.min, 3), max: round(s.max, 3),
      outlierCount, trendSlope: round(tr.slope, 4),
      trendClassification: tr.classification, sampleCount: s.sampleCount,
      movingAverageLast: round(movingAverageLast, 3),
      alertLevel: worst,
    };
  }

  // -------- Derivação de SensorMetadata a partir das leituras ----------
  // (a BD não persiste o estado operacional; é derivado por recência)
  function deriveSensors(readings) {
    if (readings.length === 0) return [];
    const maxTs = readings.reduce((m, r) => Math.max(m, new Date(r.timestamp).getTime()), 0);
    const ACTIVE_WINDOW = 60 * 60 * 1000;       // 1 h  -> ativo
    const LATENT_WINDOW = 24 * 60 * 60 * 1000;  // 24 h -> manutenção; acima -> desativado
    const byKey = new Map();
    readings.forEach((r) => {
      const key = `${r.sensorId}|${r.type}`;
      const ts = new Date(r.timestamp).getTime();
      if (!byKey.has(key)) {
        byKey.set(key, { sensorId: r.sensorId, zone: r.zone, type: r.type, first: ts, last: ts, count: 0 });
      }
      const e = byKey.get(key);
      e.first = Math.min(e.first, ts);
      e.last = Math.max(e.last, ts);
      e.count += 1;
    });
    return Array.from(byKey.values()).map((e) => {
      const age = maxTs - e.last;
      let estado = 'ativo';
      if (age > LATENT_WINDOW) estado = 'desativado';
      else if (age > ACTIVE_WINDOW) estado = 'manutencao';
      return {
        sensorId: e.sensorId,
        zone: e.zone,
        type: e.type,
        firstReadingAt: new Date(e.first).toISOString(),
        lastReadingAt: new Date(e.last).toISOString(),
        totalReadings: e.count,
        estado,
      };
    });
  }

  // -------- Cache leve das leituras (várias páginas pedem o conjunto) ---
  let _allReadingsCache = null;
  async function allReadings() {
    if (!_allReadingsCache) _allReadingsCache = http('/api/readings');
    return _allReadingsCache;
  }
  function filterReadings(list, f = {}) {
    let out = list.slice();
    if (f.sensorId) out = out.filter((r) => r.sensorId === f.sensorId);
    if (f.zone)     out = out.filter((r) => r.zone === f.zone);
    if (f.type)     out = out.filter((r) => r.type === f.type);
    if (f.from)     out = out.filter((r) => new Date(r.timestamp) >= new Date(f.from));
    if (f.to)       out = out.filter((r) => new Date(r.timestamp) <= new Date(f.to));
    return out;
  }

  // -------- API pública ------------------------------------------------
  const api = {
    // dicionários e helpers
    TIPOS, UNIDADES, ZONAS, ESTADOS, ESTRATEGIAS, FORMATOS, ZONA_LABEL,
    classifyAlert, worstAlert,

    async getReadings(filters = {}) {
      const all = await allReadings();
      return filterReadings(all, filters);
    },

    async getSensors() {
      const all = await allReadings();
      return deriveSensors(all);
    },

    async getAnalyses() {
      const list = await http('/api/analyses');
      return list.slice().sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt));
    },

    async getAnalysisById(id) {
      try {
        return await http(`/api/analyses/${encodeURIComponent(id)}`);
      } catch (_) {
        const list = await api.getAnalyses();
        return list.find((a) => a.id === id) || null;
      }
    },

    // Sem persistência de previsões na BD: calculamos algumas
    // previsões representativas sobre as leituras reais.
    async getPredictions() {
      const all = await allReadings();
      const targets = [
        { type: 'AR', zone: 'ZONA_INDUSTRIAL', strategy: 'linear', periods: 6 },
        { type: 'RUIDO', zone: 'ZONA_CENTRO', strategy: 'ewma', periods: 6 },
        { type: 'TEMP', zone: 'ZONA_PARQUE', strategy: 'linear', periods: 8 },
      ];
      const out = [];
      targets.forEach((t, idx) => {
        const subset = filterReadings(all, { type: t.type, zone: t.zone })
          .sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp))
          .map((r) => r.value);
        if (subset.length < 3) return;
        const fc = t.strategy === 'ewma' ? forecastEWMA(subset, t.periods) : forecastLinear(subset, t.periods);
        out.push({
          id: `P${201 + idx}`,
          type: t.type, zone: t.zone, strategyUsed: t.strategy, forecast: fc,
          predictionSummary: t.strategy === 'linear'
            ? `Regressão linear sobre ${subset.length} amostras — declive ${trendOf(subset).slope.toFixed(3)}.`
            : `Média móvel exponencial (α=0.35) sobre ${subset.length} amostras.`,
          timestamp: new Date().toISOString(),
        });
      });
      return out;
    },

    // "Calcular análise" — estatísticas sobre o filtro pedido (no cliente).
    async runAnalysis({ type, zone, sensorId, from, to }) {
      const readings = await api.getReadings({ type, zone, sensorId, from, to });
      if (readings.length === 0) return null;
      const a = computeAnalysis({ type, zone, sensorId }, readings);
      return {
        id: `A${Date.now().toString().slice(-4)}`,
        ...a,
        createdAt: new Date().toISOString(),
        _readings: readings.slice().sort((x, y) => new Date(x.timestamp) - new Date(y.timestamp)),
      };
    },

    async runPrediction({ type, zone, sensorId, from, to, strategy = 'linear', periods = 6 }) {
      const readings = await api.getReadings({ type, zone, sensorId, from, to });
      if (readings.length < 3) return null;
      const chronological = readings.slice().sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp));
      const values = chronological.map((r) => r.value);
      const fc = strategy === 'ewma' ? forecastEWMA(values, periods) : forecastLinear(values, periods);
      return {
        id: `P${Date.now().toString().slice(-4)}`,
        type, zone, strategyUsed: strategy, forecast: fc,
        predictionSummary: strategy === 'linear'
          ? `Regressão linear sobre ${values.length} amostras — declive ${trendOf(values).slope.toFixed(3)}.`
          : `Média móvel exponencial (α=0.35) sobre ${values.length} amostras.`,
        timestamp: new Date().toISOString(),
        _history: chronological,
      };
    },
  };

  window.api = api;
})();
