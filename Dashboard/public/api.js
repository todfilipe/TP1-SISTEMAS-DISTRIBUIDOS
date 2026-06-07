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
  async function postJson(path, body) {
    const res = await fetch(path, {
      method: 'POST',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: JSON.stringify(body || {}),
    });
    if (!res.ok) {
      const msg = await res.text().catch(() => res.statusText);
      const err = new Error(`API ${res.status}: ${msg || res.statusText}`);
      err.status = res.status;
      throw err;
    }
    return res.json();
  }

  // -------- Cache leve das leituras + persistência de sessão -----------
  let _allReadingsCache = null;
  const _sessionAnalyses = [];
  const _sessionPredictions = [];

  async function allReadings() {
    if (!_allReadingsCache) {
      _allReadingsCache = http('/api/readings').catch((err) => {
        _allReadingsCache = null;
        throw err;
      });
    }
    return _allReadingsCache;
  }
  function parseInputDate(dateStr) {
    if (!dateStr) return null;
    const parts = dateStr.split(/[-T:]/);
    if (parts.length >= 5) {
      const y = parseInt(parts[0], 10);
      const m = parseInt(parts[1], 10) - 1;
      const d = parseInt(parts[2], 10);
      const h = parseInt(parts[3], 10);
      const min = parseInt(parts[4], 10);
      return new Date(y, m, d, h, min);
    }
    return new Date(dateStr);
  }

  function filterReadings(list, f = {}) {
    let out = list.slice();
    if (f.sensorId) out = out.filter((r) => r.sensorId === f.sensorId);
    if (f.zone)     out = out.filter((r) => r.zone === f.zone);
    if (f.type)     out = out.filter((r) => r.type === f.type);
    if (f.from) {
      const fromDate = parseInputDate(f.from);
      if (fromDate) out = out.filter((r) => new Date(r.timestamp) >= fromDate);
    }
    if (f.to) {
      const toDate = parseInputDate(f.to);
      if (toDate) out = out.filter((r) => new Date(r.timestamp) <= toDate);
    }
    return out;
  }

  // -------- API pública ------------------------------------------------
  const api = {
    // dicionários e helpers
    TIPOS, UNIDADES, ZONAS, ESTADOS, ESTRATEGIAS, FORMATOS, ZONA_LABEL,
    classifyAlert, worstAlert, parseInputDate,

    async getReadings(filters = {}) {
      const hasBackendFilters = !!(filters.sensorId || filters.zone || filters.type || filters.from || filters.to);
      if (hasBackendFilters) {
        const params = {};
        if (filters.sensorId) params.sensorId = filters.sensorId;
        if (filters.zone) params.zone = filters.zone;
        if (filters.type) params.type = filters.type;
        if (filters.from) {
          const fromDate = parseInputDate(filters.from);
          if (fromDate) params.from = fromDate.toISOString();
        }
        if (filters.to) {
          const toDate = parseInputDate(filters.to);
          if (toDate) params.to = toDate.toISOString();
        }
        params.limit = 50000;
        return http('/api/readings', params);
      }
      const all = await allReadings();
      return filterReadings(all, filters);
    },

    async getSensors() {
      return http('/api/sensors');
    },

    async getStatus() {
      return http('/api/status');
    },

    async getSystem() {
      return http('/api/system');
    },

    async getAnalyses() {
      const list = await http('/api/analyses');
      return list.slice().sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt));
    },

    async getAnalysisById(id) {
      const sessionMatch = _sessionAnalyses.find((a) => a.id === id);
      if (sessionMatch) return sessionMatch;
      try {
        return await http(`/api/analyses/${encodeURIComponent(id)}`);
      } catch (_) {
        const list = await api.getAnalyses();
        return list.find((a) => a.id === id) || null;
      }
    },

    async getPredictions() {
      return _sessionPredictions.slice();
    },

    async runAnalysis({ type, zone, sensorId, from, to }) {
      try {
        const result = await postJson('/api/analyses', { type, zone, sensorId, from, to });
        const sessionResult = { ...result, _session: true };
        _sessionAnalyses.unshift(sessionResult);
        return sessionResult;
      } catch (err) {
        if (err.status === 404) return null;
        throw err;
      }
    },

    async runPrediction({ type, zone, sensorId, from, to, strategy = 'linear', periods = 6 }) {
      const readings = await api.getReadings({ type, zone, sensorId, from, to });
      if (readings.length < 3) return null;
      const chronological = readings.slice().sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp));
      const values = chronological.map((r) => r.value);
      const fc = strategy === 'ewma' ? forecastEWMA(values, periods) : forecastLinear(values, periods);
      const result = {
        id: `P${Date.now().toString().slice(-4)}`,
        type, zone, strategyUsed: strategy, forecast: fc,
        predictionSummary: strategy === 'linear'
          ? `Regressão linear sobre ${values.length} amostras — declive ${trendOf(values).slope.toFixed(3)}.`
          : `Média móvel exponencial (α=0.35) sobre ${values.length} amostras.`,
        timestamp: new Date().toISOString(),
        _history: chronological,
        _session: true,
      };
      _sessionPredictions.unshift(result);
      return result;
    },

    invalidateCache() { _allReadingsCache = null; },
    getSessionAnalyses() { return _sessionAnalyses.slice(); },
    getSessionPredictions() { return _sessionPredictions.slice(); },
  };

  window.api = api;
})();
