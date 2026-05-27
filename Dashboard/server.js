const express = require("express");
const { MongoClient } = require("mongodb");

const PORT = Number(process.env.PORT || 3000);
const MONGODB_URI =
  process.env.MONGODB_URI ||
  "mongodb://admin:admin@localhost:27017/urbanodb?authSource=admin";

const SENSOR_TYPES = ["TEMP", "HUM", "AR", "RUIDO", "PM2.5", "PM10", "LUZ"];
const ZONES = [
  "ZONA_CENTRO",
  "ZONA_ESCOLAR",
  "ZONA_INDUSTRIAL",
  "ZONA_RESIDENCIAL",
  "ZONA_PARQUE",
];
const STRATEGIES = ["linear", "ewma"];

const app = express();
app.use(express.urlencoded({ extended: true }));
app.use(express.json());

const mongoClient = new MongoClient(MONGODB_URI);
let database;

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function formatDate(value) {
  if (!value) return "";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "" : date.toISOString();
}

function formatNumber(value, digits = 2) {
  const number = Number(value);
  return Number.isFinite(number) ? number.toFixed(digits) : "";
}

function toDateInputValue(value) {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  return date.toISOString().slice(0, 16);
}

function parseDateParam(value, fieldName) {
  if (!value) return undefined;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    const error = new Error(`Parametro de data invalido: ${fieldName}`);
    error.status = 400;
    throw error;
  }
  return date;
}

function buildReadingFilter(params) {
  const filter = {};

  if (params.sensorId) filter.sensorId = String(params.sensorId).trim();
  if (params.zone) filter.zone = String(params.zone).trim();
  if (params.type) filter.type = String(params.type).trim();

  const dateFrom = parseDateParam(params.dateFrom, "dateFrom");
  const dateTo = parseDateParam(params.dateTo, "dateTo");
  if (dateFrom || dateTo) {
    filter.timestamp = {};
    if (dateFrom) filter.timestamp.$gte = dateFrom;
    if (dateTo) filter.timestamp.$lte = dateTo;
  }

  return filter;
}

function optionList(values, selected, placeholder) {
  const placeholderOption = placeholder
    ? `<option value="">${escapeHtml(placeholder)}</option>`
    : "";
  return `${placeholderOption}${values
    .map((value) => {
      const isSelected = value === selected ? " selected" : "";
      return `<option value="${escapeHtml(value)}"${isSelected}>${escapeHtml(
        value
      )}</option>`;
    })
    .join("")}`;
}

function pageLayout(title, activeRoute, content, extraScripts = "") {
  const navLink = (href, label) => {
    const active = activeRoute === href ? " active" : "";
    return `<a class="nav-link${active}" href="${href}">${label}</a>`;
  };

  return `<!doctype html>
<html lang="pt">
  <head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>${escapeHtml(title)} - One Health</title>
    <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css" rel="stylesheet">
    <style>
      :root {
        --ink: #1f2933;
        --muted: #64748b;
        --line: #d9e2ec;
        --surface: #ffffff;
        --panel: #f5f7fa;
        --accent: #146c94;
        --accent-2: #2f855a;
      }
      body {
        background: var(--panel);
        color: var(--ink);
        font-size: 0.95rem;
      }
      .navbar {
        border-bottom: 1px solid var(--line);
      }
      .navbar-brand {
        font-weight: 700;
        letter-spacing: 0;
      }
      .page-shell {
        max-width: 1240px;
        margin: 0 auto;
        padding: 24px 16px 48px;
      }
      .section-panel {
        background: var(--surface);
        border: 1px solid var(--line);
        border-radius: 8px;
        padding: 18px;
      }
      .page-title {
        font-size: clamp(1.45rem, 2vw, 2rem);
        font-weight: 700;
        margin: 0 0 16px;
      }
      .chart-wrap {
        position: relative;
        height: 360px;
      }
      .table {
        --bs-table-bg: transparent;
        vertical-align: middle;
      }
      .table thead th {
        color: var(--muted);
        font-size: 0.78rem;
        text-transform: uppercase;
        letter-spacing: 0;
        white-space: nowrap;
      }
      .filter-grid {
        display: grid;
        grid-template-columns: repeat(6, minmax(130px, 1fr));
        gap: 12px;
        align-items: end;
      }
      .summary-kpi {
        display: grid;
        grid-template-columns: repeat(4, minmax(0, 1fr));
        gap: 12px;
      }
      .kpi {
        border: 1px solid var(--line);
        border-radius: 8px;
        padding: 14px;
        background: #fbfcfd;
      }
      .kpi span {
        display: block;
        color: var(--muted);
        font-size: 0.78rem;
        text-transform: uppercase;
      }
      .kpi strong {
        display: block;
        margin-top: 4px;
        font-size: 1.25rem;
      }
      code {
        color: var(--accent);
      }
      @media (max-width: 992px) {
        .filter-grid,
        .summary-kpi {
          grid-template-columns: repeat(2, minmax(0, 1fr));
        }
      }
      @media (max-width: 576px) {
        .filter-grid,
        .summary-kpi {
          grid-template-columns: 1fr;
        }
        .chart-wrap {
          height: 300px;
        }
      }
    </style>
  </head>
  <body>
    <nav class="navbar navbar-expand-lg bg-white">
      <div class="container-fluid page-shell py-0">
        <a class="navbar-brand" href="/readings">One Health Dashboard</a>
        <button class="navbar-toggler" type="button" data-bs-toggle="collapse" data-bs-target="#mainNav" aria-controls="mainNav" aria-expanded="false" aria-label="Alternar navegacao">
          <span class="navbar-toggler-icon"></span>
        </button>
        <div class="collapse navbar-collapse" id="mainNav">
          <div class="navbar-nav ms-auto">
            ${navLink("/readings", "Leituras")}
            ${navLink("/analyses", "Analises")}
            ${navLink("/new-analysis", "Nova analise")}
            ${navLink("/status", "Status JSON")}
          </div>
        </div>
      </div>
    </nav>
    <main class="page-shell">${content}</main>
    <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js"></script>
    ${extraScripts}
  </body>
</html>`;
}

async function getDistinctValues(collectionName, fieldName) {
  return database
    .collection(collectionName)
    .distinct(fieldName, { [fieldName]: { $type: "string", $ne: "" } });
}

function computeStats(readings, strategy) {
  const values = readings
    .map((reading) => Number(reading.value))
    .filter((value) => Number.isFinite(value));

  if (values.length === 0) {
    return {
      count: 0,
      average: null,
      median: null,
      standardDeviation: null,
      min: null,
      max: null,
      outlierCount: 0,
      trendClassification: "SEM_DADOS",
      forecastNext: null,
    };
  }

  const sorted = [...values].sort((a, b) => a - b);
  const average = values.reduce((sum, value) => sum + value, 0) / values.length;
  const variance =
    values.reduce((sum, value) => sum + Math.pow(value - average, 2), 0) /
    values.length;
  const standardDeviation = Math.sqrt(variance);
  const middle = Math.floor(sorted.length / 2);
  const median =
    sorted.length % 2 === 0
      ? (sorted[middle - 1] + sorted[middle]) / 2
      : sorted[middle];
  const outlierCount =
    standardDeviation === 0
      ? 0
      : values.filter((value) => Math.abs(value - average) > standardDeviation * 2)
          .length;

  const trend = classifyTrend(values);
  const forecastNext =
    strategy === "ewma" ? forecastEwma(values) : forecastLinear(values);

  return {
    count: values.length,
    average,
    median,
    standardDeviation,
    min: sorted[0],
    max: sorted[sorted.length - 1],
    outlierCount,
    trendClassification: trend,
    forecastNext,
  };
}

function classifyTrend(values) {
  if (values.length < 2) return "ESTAVEL";
  const first = values[0];
  const last = values[values.length - 1];
  const delta = last - first;
  const tolerance = Math.max(Math.abs(first) * 0.02, 0.01);
  if (delta > tolerance) return "CRESCENTE";
  if (delta < -tolerance) return "DECRESCENTE";
  return "ESTAVEL";
}

function forecastLinear(values) {
  if (values.length === 1) return values[0];
  const n = values.length;
  const xMean = (n - 1) / 2;
  const yMean = values.reduce((sum, value) => sum + value, 0) / n;
  let numerator = 0;
  let denominator = 0;
  values.forEach((value, index) => {
    numerator += (index - xMean) * (value - yMean);
    denominator += Math.pow(index - xMean, 2);
  });
  const slope = denominator === 0 ? 0 : numerator / denominator;
  const intercept = yMean - slope * xMean;
  return intercept + slope * n;
}

function forecastEwma(values) {
  const alpha = 0.35;
  return values.slice(1).reduce((previous, value) => {
    return alpha * value + (1 - alpha) * previous;
  }, values[0]);
}

app.get("/", (_req, res) => {
  res.redirect("/readings");
});

app.get("/readings", async (req, res, next) => {
  try {
    const filter = buildReadingFilter(req.query);
    const [readings, sensorIds, zones, types] = await Promise.all([
      database
        .collection("readings")
        .find(filter)
        .sort({ timestamp: 1 })
        .limit(1000)
        .toArray(),
      getDistinctValues("readings", "sensorId"),
      getDistinctValues("readings", "zone"),
      getDistinctValues("readings", "type"),
    ]);

    const chartRows = readings.map((reading) => ({
      label: formatDate(reading.timestamp),
      value: Number(reading.value),
      sensorId: reading.sensorId,
      unit: reading.unit,
    }));

    const rows = readings
      .slice()
      .reverse()
      .map(
        (reading) => `<tr>
          <td><code>${escapeHtml(reading.sensorId)}</code></td>
          <td>${escapeHtml(reading.zone)}</td>
          <td>${escapeHtml(reading.type)}</td>
          <td>${formatNumber(reading.value)}</td>
          <td>${escapeHtml(reading.unit)}</td>
          <td>${escapeHtml(formatDate(reading.timestamp))}</td>
          <td>${escapeHtml(reading.gatewayId)}</td>
          <td>${escapeHtml(reading.originalMessageFormat)}</td>
        </tr>`
      )
      .join("");

    const content = `
      <h1 class="page-title">Leituras</h1>
      <section class="section-panel mb-3">
        <form class="filter-grid" method="get" action="/readings">
          <div>
            <label class="form-label" for="sensorId">Sensor</label>
            <select class="form-select" id="sensorId" name="sensorId">
              ${optionList(sensorIds.sort(), req.query.sensorId, "Todos")}
            </select>
          </div>
          <div>
            <label class="form-label" for="zone">Zona</label>
            <select class="form-select" id="zone" name="zone">
              ${optionList((zones.length ? zones : ZONES).sort(), req.query.zone, "Todas")}
            </select>
          </div>
          <div>
            <label class="form-label" for="type">Tipo</label>
            <select class="form-select" id="type" name="type">
              ${optionList((types.length ? types : SENSOR_TYPES).sort(), req.query.type, "Todos")}
            </select>
          </div>
          <div>
            <label class="form-label" for="dateFrom">Desde</label>
            <input class="form-control" id="dateFrom" name="dateFrom" type="datetime-local" value="${escapeHtml(
              toDateInputValue(req.query.dateFrom)
            )}">
          </div>
          <div>
            <label class="form-label" for="dateTo">Ate</label>
            <input class="form-control" id="dateTo" name="dateTo" type="datetime-local" value="${escapeHtml(
              toDateInputValue(req.query.dateTo)
            )}">
          </div>
          <div class="d-flex gap-2">
            <button class="btn btn-primary w-100" type="submit">Filtrar</button>
            <a class="btn btn-outline-secondary" href="/readings">Limpar</a>
          </div>
        </form>
      </section>
      <section class="section-panel mb-3">
        <div class="d-flex justify-content-between align-items-center gap-3 mb-2">
          <h2 class="h5 mb-0">Serie temporal</h2>
          <span class="text-secondary">${readings.length} leituras</span>
        </div>
        <div class="chart-wrap">
          <canvas id="readingsChart"></canvas>
        </div>
      </section>
      <section class="section-panel">
        <div class="table-responsive">
          <table class="table table-hover table-sm">
            <thead>
              <tr>
                <th>Sensor</th>
                <th>Zona</th>
                <th>Tipo</th>
                <th>Valor</th>
                <th>Un.</th>
                <th>Timestamp</th>
                <th>Gateway</th>
                <th>Formato</th>
              </tr>
            </thead>
            <tbody>
              ${rows || '<tr><td colspan="8" class="text-secondary">Sem leituras para os filtros selecionados.</td></tr>'}
            </tbody>
          </table>
        </div>
      </section>`;

    const scripts = `
      <script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.3/dist/chart.umd.min.js"></script>
      <script>
        const chartRows = ${JSON.stringify(chartRows)};
        const ctx = document.getElementById("readingsChart");
        new Chart(ctx, {
          type: "line",
          data: {
            labels: chartRows.map((row) => row.label),
            datasets: [{
              label: "Valor",
              data: chartRows.map((row) => row.value),
              borderColor: "#146c94",
              backgroundColor: "rgba(20, 108, 148, 0.12)",
              tension: 0.25,
              pointRadius: chartRows.length > 120 ? 0 : 2,
              fill: true
            }]
          },
          options: {
            maintainAspectRatio: false,
            interaction: { intersect: false, mode: "index" },
            plugins: {
              legend: { display: false },
              tooltip: {
                callbacks: {
                  afterLabel: (context) => {
                    const row = chartRows[context.dataIndex];
                    return row ? "Sensor: " + row.sensorId + (row.unit ? " | " + row.unit : "") : "";
                  }
                }
              }
            },
            scales: {
              x: { ticks: { maxRotation: 0, autoSkip: true, maxTicksLimit: 10 } },
              y: { beginAtZero: false }
            }
          }
        });
      </script>`;

    res.send(pageLayout("Leituras", "/readings", content, scripts));
  } catch (error) {
    next(error);
  }
});

app.get("/analyses", async (_req, res, next) => {
  try {
    const analyses = await database
      .collection("analyses")
      .find({})
      .sort({ createdAt: -1 })
      .limit(500)
      .toArray();

    const rows = analyses
      .map(
        (analysis) => `<tr>
          <td>${escapeHtml(analysis.zone)}</td>
          <td>${escapeHtml(analysis.type)}</td>
          <td>${escapeHtml(analysis.sensorId || "-")}</td>
          <td>${escapeHtml(formatDate(analysis.windowStart))}</td>
          <td>${escapeHtml(formatDate(analysis.windowEnd))}</td>
          <td>${formatNumber(analysis.average)}</td>
          <td>${formatNumber(analysis.median)}</td>
          <td>${formatNumber(analysis.standardDeviation)}</td>
          <td>${escapeHtml(analysis.trendClassification)}</td>
          <td>${escapeHtml(formatDate(analysis.createdAt))}</td>
        </tr>`
      )
      .join("");

    const content = `
      <h1 class="page-title">Historico de analises</h1>
      <section class="section-panel">
        <div class="table-responsive">
          <table class="table table-hover table-sm">
            <thead>
              <tr>
                <th>Zona</th>
                <th>Tipo</th>
                <th>Sensor</th>
                <th>Janela inicio</th>
                <th>Janela fim</th>
                <th>Media</th>
                <th>Mediana</th>
                <th>Desvio</th>
                <th>Tendencia</th>
                <th>Criada em</th>
              </tr>
            </thead>
            <tbody>
              ${rows || '<tr><td colspan="10" class="text-secondary">Sem analises registadas.</td></tr>'}
            </tbody>
          </table>
        </div>
      </section>`;

    res.send(pageLayout("Analises", "/analyses", content));
  } catch (error) {
    next(error);
  }
});

app.get("/new-analysis", async (_req, res, next) => {
  try {
    const sensorIds = await getDistinctValues("readings", "sensorId");
    const content = `
      <h1 class="page-title">Nova analise</h1>
      <section class="section-panel">
        <form method="post" action="/new-analysis" class="row g-3">
          <div class="col-md-4">
            <label class="form-label" for="type">Tipo</label>
            <select class="form-select" id="type" name="type" required>
              ${optionList(SENSOR_TYPES, "", "Selecionar")}
            </select>
          </div>
          <div class="col-md-4">
            <label class="form-label" for="zone">Zona</label>
            <select class="form-select" id="zone" name="zone" required>
              ${optionList(ZONES, "", "Selecionar")}
            </select>
          </div>
          <div class="col-md-4">
            <label class="form-label" for="sensorId">Sensor opcional</label>
            <select class="form-select" id="sensorId" name="sensorId">
              ${optionList(sensorIds.sort(), "", "Todos")}
            </select>
          </div>
          <div class="col-md-4">
            <label class="form-label" for="dateFrom">Desde</label>
            <input class="form-control" id="dateFrom" name="dateFrom" type="datetime-local" required>
          </div>
          <div class="col-md-4">
            <label class="form-label" for="dateTo">Ate</label>
            <input class="form-control" id="dateTo" name="dateTo" type="datetime-local" required>
          </div>
          <div class="col-md-4">
            <label class="form-label" for="strategy">Estrategia</label>
            <select class="form-select" id="strategy" name="strategy" required>
              ${optionList(STRATEGIES, "linear")}
            </select>
          </div>
          <div class="col-12">
            <button class="btn btn-primary" type="submit">Executar analise</button>
          </div>
        </form>
      </section>`;

    res.send(pageLayout("Nova analise", "/new-analysis", content));
  } catch (error) {
    next(error);
  }
});

app.post("/new-analysis", async (req, res, next) => {
  try {
    const { type, zone, sensorId, strategy = "linear" } = req.body;
    if (!SENSOR_TYPES.includes(type)) {
      return res.status(400).json({ error: "Tipo invalido." });
    }
    if (!ZONES.includes(zone)) {
      return res.status(400).json({ error: "Zona invalida." });
    }
    if (!STRATEGIES.includes(strategy)) {
      return res.status(400).json({ error: "Estrategia invalida." });
    }

    const filter = buildReadingFilter({
      type,
      zone,
      sensorId,
      dateFrom: req.body.dateFrom,
      dateTo: req.body.dateTo,
    });

    const readings = await database
      .collection("readings")
      .find(filter)
      .sort({ timestamp: 1 })
      .limit(5000)
      .toArray();

    const sanitizedReadings = readings.map((reading) => ({
      id: reading._id?.toString(),
      sensorId: reading.sensorId,
      zone: reading.zone,
      type: reading.type,
      value: reading.value,
      unit: reading.unit,
      timestamp: formatDate(reading.timestamp),
      gatewayId: reading.gatewayId,
      originalMessageFormat: reading.originalMessageFormat,
    }));

    res.json({
      filter: {
        type,
        zone,
        sensorId: sensorId || null,
        dateFrom: formatDate(filter.timestamp?.$gte),
        dateTo: formatDate(filter.timestamp?.$lte),
        strategy,
      },
      stats: computeStats(readings, strategy),
      readings: sanitizedReadings,
    });
  } catch (error) {
    next(error);
  }
});

app.get("/status", async (_req, res, next) => {
  try {
    const readingsCollection = database.collection("readings");
    const analysesCollection = database.collection("analyses");
    const [totalReadings, analysisCount, latestByZone] = await Promise.all([
      readingsCollection.countDocuments({}),
      analysesCollection.countDocuments({}),
      readingsCollection
        .aggregate([
          { $sort: { timestamp: -1 } },
          {
            $group: {
              _id: "$zone",
              sensorId: { $first: "$sensorId" },
              type: { $first: "$type" },
              value: { $first: "$value" },
              unit: { $first: "$unit" },
              timestamp: { $first: "$timestamp" },
            },
          },
          { $sort: { _id: 1 } },
        ])
        .toArray(),
    ]);

    res.json({
      totalReadings,
      analysisCount,
      latestReadingByZone: latestByZone.map((reading) => ({
        zone: reading._id,
        sensorId: reading.sensorId,
        type: reading.type,
        value: reading.value,
        unit: reading.unit,
        timestamp: formatDate(reading.timestamp),
      })),
    });
  } catch (error) {
    next(error);
  }
});

app.use((error, req, res, _next) => {
  const status = error.status || 500;
  if (status >= 500) {
    console.error(error);
  }

  if (req.path === "/new-analysis" && req.method === "POST") {
    return res
      .status(status)
      .json({ error: error.message || "Erro inesperado." });
  }

  res.status(status).send(
    pageLayout(
      "Erro",
      "",
      `<section class="section-panel">
        <h1 class="h4">Erro</h1>
        <p class="mb-0">${escapeHtml(error.message || "Erro inesperado.")}</p>
      </section>`
    )
  );
});

async function start() {
  await mongoClient.connect();
  database = mongoClient.db();
  app.listen(PORT, "0.0.0.0", () => {
    console.log(`Dashboard listening on http://0.0.0.0:${PORT}`);
  });
}

process.on("SIGINT", async () => {
  await mongoClient.close();
  process.exit(0);
});

process.on("SIGTERM", async () => {
  await mongoClient.close();
  process.exit(0);
});

start().catch((error) => {
  console.error("Failed to start dashboard:", error);
  process.exit(1);
});
