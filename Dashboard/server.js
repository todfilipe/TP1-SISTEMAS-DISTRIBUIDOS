const path = require("path");
const express = require("express");
const { MongoClient, ObjectId } = require("mongodb");

const PORT = Number(process.env.PORT || 3000);
const MONGODB_URI =
  process.env.MONGODB_URI ||
  "mongodb://admin:admin@localhost:27017/urbanodb?authSource=admin";

// Limite de leituras devolvidas ao cliente (a SPA carrega o conjunto e
// filtra/agrega localmente). Ajustável por variavel de ambiente.
const READINGS_LIMIT = Number(process.env.READINGS_LIMIT || 5000);

const app = express();
app.use(express.json());

const mongoClient = new MongoClient(MONGODB_URI);
let database;

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

  const dateFrom = parseDateParam(params.from, "from");
  const dateTo = parseDateParam(params.to, "to");
  if (dateFrom || dateTo) {
    filter.timestamp = {};
    if (dateFrom) filter.timestamp.$gte = dateFrom;
    if (dateTo) filter.timestamp.$lte = dateTo;
  }
  return filter;
}

function toIso(value) {
  if (!value) return null;
  const date = value instanceof Date ? value : new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

function mapReading(doc) {
  return {
    sensorId: doc.sensorId || "",
    zone: doc.zone || "",
    type: doc.type || "",
    value: Number(doc.value),
    unit: doc.unit || "",
    timestamp: toIso(doc.timestamp),
    gatewayId: doc.gatewayId || "",
    originalMessageFormat: doc.originalMessageFormat || "",
  };
}

function mapAnalysis(doc) {
  return {
    id: doc._id ? doc._id.toString() : undefined,
    zone: doc.zone || "",
    type: doc.type || "",
    sensorId: doc.sensorId || undefined,
    windowStart: toIso(doc.windowStart),
    windowEnd: toIso(doc.windowEnd),
    average: doc.average ?? null,
    median: doc.median ?? null,
    standardDeviation: doc.standardDeviation ?? null,
    percentile25: doc.percentile25 ?? null,
    percentile75: doc.percentile75 ?? null,
    percentile95: doc.percentile95 ?? null,
    min: doc.min ?? null,
    max: doc.max ?? null,
    outlierCount: doc.outlierCount ?? 0,
    trendSlope: doc.trendSlope ?? 0,
    trendClassification: doc.trendClassification || "Stable",
    sampleCount: doc.sampleCount ?? 0,
    movingAverageLast: doc.movingAverageLast ?? null,
    alertLevel: doc.alertLevel || "NORMAL",
    createdAt: toIso(doc.createdAt),
  };
}

// ----------------------------- API -----------------------------------

app.get("/api/readings", async (req, res, next) => {
  try {
    const filter = buildReadingFilter(req.query);
    const limit = Math.min(Number(req.query.limit) || READINGS_LIMIT, 50000);
    const readings = await database
      .collection("readings")
      .find(filter)
      .sort({ timestamp: -1 })
      .limit(limit)
      .toArray();
    res.json(readings.map(mapReading));
  } catch (error) {
    next(error);
  }
});

app.get("/api/analyses", async (_req, res, next) => {
  try {
    const analyses = await database
      .collection("analyses")
      .find({})
      .sort({ createdAt: -1 })
      .limit(1000)
      .toArray();
    res.json(analyses.map(mapAnalysis));
  } catch (error) {
    next(error);
  }
});

app.get("/api/analyses/:id", async (req, res, next) => {
  try {
    let doc = null;
    if (ObjectId.isValid(req.params.id)) {
      doc = await database
        .collection("analyses")
        .findOne({ _id: new ObjectId(req.params.id) });
    }
    if (!doc) {
      return res.status(404).json({ error: "Analise nao encontrada." });
    }
    res.json(mapAnalysis(doc));
  } catch (error) {
    next(error);
  }
});

// Metadados de sensores (sensors_metadata se existir; caso contrario
// derivado por agregacao das leituras). O estado operacional e derivado
// por recencia, pois a BD nao o persiste.
app.get("/api/sensors", async (_req, res, next) => {
  try {
    const meta = await database
      .collection("sensors_metadata")
      .find({})
      .toArray();

    let rows;
    if (meta.length > 0) {
      rows = meta.map((d) => ({
        sensorId: d.sensorId,
        zone: d.zone,
        type: d.type,
        firstReadingAt: toIso(d.firstReadingAt),
        lastReadingAt: toIso(d.lastReadingAt),
        totalReadings: Number(d.totalReadings) || 0,
      }));
    } else {
      const agg = await database
        .collection("readings")
        .aggregate([
          {
            $group: {
              _id: { sensorId: "$sensorId", type: "$type", zone: "$zone" },
              firstReadingAt: { $min: "$timestamp" },
              lastReadingAt: { $max: "$timestamp" },
              totalReadings: { $sum: 1 },
            },
          },
        ])
        .toArray();
      rows = agg.map((d) => ({
        sensorId: d._id.sensorId,
        zone: d._id.zone,
        type: d._id.type,
        firstReadingAt: toIso(d.firstReadingAt),
        lastReadingAt: toIso(d.lastReadingAt),
        totalReadings: d.totalReadings,
      }));
    }

    const maxTs = rows.reduce(
      (m, r) => Math.max(m, r.lastReadingAt ? new Date(r.lastReadingAt).getTime() : 0),
      0
    );
    const ACTIVE_WINDOW = 60 * 60 * 1000;
    const LATENT_WINDOW = 24 * 60 * 60 * 1000;
    rows = rows.map((r) => {
      const age = r.lastReadingAt ? maxTs - new Date(r.lastReadingAt).getTime() : Infinity;
      let estado = "ativo";
      if (age > LATENT_WINDOW) estado = "desativado";
      else if (age > ACTIVE_WINDOW) estado = "manutencao";
      return { ...r, estado };
    });

    res.json(rows);
  } catch (error) {
    next(error);
  }
});

app.get("/api/status", async (_req, res, next) => {
  try {
    const [totalReadings, analysisCount] = await Promise.all([
      database.collection("readings").countDocuments({}),
      database.collection("analyses").countDocuments({}),
    ]);
    res.json({ totalReadings, analysisCount, db: database.databaseName });
  } catch (error) {
    next(error);
  }
});

// --------------------------- Estaticos --------------------------------
const PUBLIC_DIR = path.join(__dirname, "public");
app.use(express.static(PUBLIC_DIR));

// SPA fallback (hash router): qualquer rota nao-API devolve o index.
app.get("*", (req, res, next) => {
  if (req.path.startsWith("/api/")) return next();
  res.sendFile(path.join(PUBLIC_DIR, "index.html"));
});

app.use((error, _req, res, _next) => {
  const status = error.status || 500;
  if (status >= 500) console.error(error);
  res.status(status).json({ error: error.message || "Erro inesperado." });
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
