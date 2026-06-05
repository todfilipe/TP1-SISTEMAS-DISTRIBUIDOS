const path = require("path");
const fs = require("fs");
const express = require("express");
const { MongoClient, ObjectId } = require("mongodb");
const protocol = require("../Shared/protocol");

const PORT = Number(process.env.PORT || 3000);
const MONGODB_URI =
  process.env.MONGODB_URI ||
  "mongodb://admin:admin@localhost:27017/urbanodb?authSource=admin";
const SERVER_API_URL = (process.env.SERVER_API_URL || "http://localhost:9091").replace(/\/+$/, "");

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

function parseSensorsCsvLine(line) {
  const trimmed = String(line || "").trim();
  if (!trimmed || trimmed.startsWith("#")) return [];

  const openBracket = trimmed.indexOf("[");
  const closeBracket = trimmed.indexOf("]");
  if (openBracket < 0 || closeBracket <= openBracket) return [];

  const prefix = trimmed.slice(0, openBracket).split(":");
  if (prefix.length < 3) return [];

  const sensorId = prefix[0].trim();
  const estado = prefix[1].trim();
  const zone = prefix[2].trim();
  const types = trimmed
    .slice(openBracket + 1, closeBracket)
    .split(",")
    .map((type) => type.trim())
    .filter(Boolean);

  return types.map((type) => ({ sensorId, estado, zone, type }));
}

function parseSensorsCsvDeviceLine(line) {
  const trimmed = String(line || "").trim();
  if (!trimmed || trimmed.startsWith("#")) return null;

  const openBracket = trimmed.indexOf("[");
  const closeBracket = trimmed.indexOf("]");
  if (openBracket < 0 || closeBracket <= openBracket) return null;

  const prefix = trimmed.slice(0, openBracket).split(":");
  if (prefix.length < 3) return null;

  const suffix = trimmed.slice(closeBracket + 1).replace(/^:/, "").trim();
  const sensorId = prefix[0].trim();
  const estado = prefix[1].trim();
  const zone = prefix[2].trim();
  const types = trimmed
    .slice(openBracket + 1, closeBracket)
    .split(",")
    .map((type) => type.trim())
    .filter(Boolean);

  return {
    sensorId,
    estado,
    zone,
    types,
    runtime: sensorId.endsWith("99") ? "C#" : "Node.js",
    lastSync: suffix || null,
  };
}

function loadRegisteredSensors() {
  if (!fs.existsSync(SENSORS_CSV_PATH)) return [];
  return fs
    .readFileSync(SENSORS_CSV_PATH, "utf8")
    .split(/\r?\n/)
    .flatMap(parseSensorsCsvLine);
}

function loadRegisteredSensorDevices() {
  if (!fs.existsSync(SENSORS_CSV_PATH)) return [];
  return fs
    .readFileSync(SENSORS_CSV_PATH, "utf8")
    .split(/\r?\n/)
    .map(parseSensorsCsvDeviceLine)
    .filter(Boolean);
}

function gatewayForZone(zone) {
  const gateways = {
    ZONA_CENTRO: {
      gatewayId: "GW_CENTRO",
      service: "gateway-centro",
      binding: "ZONA_CENTRO.#",
      httpPort: 8081,
    },
    ZONA_ESCOLAR: {
      gatewayId: "GW_ESCOLAR",
      service: "gateway-escolar",
      binding: "ZONA_ESCOLAR.#",
      httpPort: 8082,
    },
    ZONA_INDUSTRIAL: {
      gatewayId: "GW_INDUSTRIAL",
      service: "gateway-industrial",
      binding: "ZONA_INDUSTRIAL.#",
      httpPort: 8083,
    },
    ZONA_RESIDENCIAL: {
      gatewayId: "GW_RESIDENCIAL",
      service: "gateway-residencial",
      binding: "ZONA_RESIDENCIAL.#",
      httpPort: 8084,
    },
    ZONA_PARQUE: {
      gatewayId: "GW_PARQUE",
      service: "gateway-parque",
      binding: "ZONA_PARQUE.#",
      httpPort: 8085,
    },
  };
  return gateways[zone] || {
    gatewayId: `GW_${String(zone || "").replace(/^ZONA_/, "")}`,
    service: "",
    binding: `${zone}.#`,
    httpPort: null,
  };
}

function summarizeTypeMatrix(devices) {
  return protocol.ZONES.map((zone) => {
    const inZone = devices.filter((device) => device.zone === zone);
    const typeCounts = {};
    protocol.SENSOR_TYPES
      .filter((type) => type !== "VIDEO")
      .forEach((type) => {
        typeCounts[type] = inZone.filter((device) => device.types.includes(type)).length;
      });
    return {
      zone,
      label: protocol.ZONE_LABELS[zone] || zone,
      sensorCount: inZone.length,
      typeCounts,
    };
  });
}

function buildServiceInventory(devices) {
  const nodeSensors = devices.filter((device) => device.runtime === "Node.js").length;
  const csharpSensors = devices.filter((device) => device.runtime === "C#").length;

  return [
    { name: "RabbitMQ", service: "rabbitmq", role: "Broker AMQP", status: "docker", port: "5672 / 15672" },
    { name: "MongoDB", service: "mongodb", role: "Persistencia", status: "docker", port: "27017" },
    { name: "Servidor", service: "servidor", role: "Orquestrador central", status: "docker", port: "9090 / 9091" },
    { name: "Dashboard", service: "dashboard", role: "Interface web", status: "docker", port: "3000" },
    { name: "Preprocessing", service: "preprocessing", role: "Normalizacao", status: "docker", port: "8000" },
    { name: "Analysis", service: "analysis", role: "Analise estatistica gRPC", status: "docker", port: "50052" },
    { name: "Environment", service: "environment", role: "Dados ambientais externos", status: "docker", port: "8001" },
    { name: "Gateways", service: "gateway-*", role: "1 por zona", status: "docker", port: "8081-8085", count: protocol.ZONES.length },
    { name: "Sensores Node.js", service: "sensor-node-*", role: "Publicadores automaticos", status: "docker", count: nodeSensors },
    { name: "Sensores C#", service: "sensor-csharp-*", role: "Publicadores alternativos", status: "docker", count: csharpSensors },
  ];
}

async function postServidor(path, body) {
  const res = await fetch(`${SERVER_API_URL}${path}`, {
    method: "POST",
    headers: { Accept: "application/json", "Content-Type": "application/json" },
    body: JSON.stringify(body || {}),
  });

  if (!res.ok) {
    const message = await res.text().catch(() => res.statusText);
    const error = new Error(message || res.statusText);
    error.status = res.status;
    throw error;
  }

  return res.json();
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

app.post("/api/analyses", async (req, res, next) => {
  try {
    const params = {
      type: req.body?.type,
      zone: req.body?.zone,
      sensorId: req.body?.sensorId,
      from: req.body?.from,
      to: req.body?.to,
    };
    const analysis = await postServidor("/api/analyses", params);
    res.status(201).json(analysis);
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

// Sensores registados: a fonte de verdade e Gateway/sensors.csv.
// O Mongo e usado apenas para inferir atividade recente por zona/tipo,
// porque as leituras persistidas pelo servidor chegam agregadas por gateway.
app.get("/api/sensors", async (_req, res, next) => {
  try {
    const registered = loadRegisteredSensors();
    if (registered.length === 0) {
      return res.json([]);
    }

    const agg = await database
      .collection("readings")
      .aggregate([
        {
          $group: {
            _id: { zone: "$zone", type: "$type" },
            firstReadingAt: { $min: "$timestamp" },
            lastReadingAt: { $max: "$timestamp" },
            totalReadings: { $sum: 1 },
          },
        },
      ])
      .toArray();

    const activityByZoneType = new Map(
      agg.map((d) => [`${d._id.zone}:${d._id.type}`, d])
    );

    const ACTIVE_WINDOW = 60 * 60 * 1000;
    const LATENT_WINDOW = 24 * 60 * 60 * 1000;
    const now = Date.now();

    const rows = registered.map((sensor) => {
      const activity = activityByZoneType.get(`${sensor.zone}:${sensor.type}`);
      const lastReadingAt = toIso(activity?.lastReadingAt) || new Date(0).toISOString();
      const firstReadingAt = toIso(activity?.firstReadingAt) || lastReadingAt;
      const age = lastReadingAt ? now - new Date(lastReadingAt).getTime() : Infinity;
      let estado = sensor.estado || "ativo";
      if (estado === "ativo") {
        if (age > LATENT_WINDOW) estado = "desativado";
        else if (age > ACTIVE_WINDOW) estado = "manutencao";
      }

      return {
        sensorId: sensor.sensorId,
        zone: sensor.zone,
        type: sensor.type,
        firstReadingAt,
        lastReadingAt,
        totalReadings: Number(activity?.totalReadings) || 0,
        estado,
      };
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

app.get("/api/system", async (_req, res, next) => {
  try {
    const devices = loadRegisteredSensorDevices();
    const logicalSensors = devices.reduce((sum, device) => sum + device.types.length, 0);

    const [zoneTypeActivity, latestDocs, totalReadings, analysisCount] = await Promise.all([
      database
        .collection("readings")
        .aggregate([
          {
            $group: {
              _id: { zone: "$zone", type: "$type" },
              firstReadingAt: { $min: "$timestamp" },
              lastReadingAt: { $max: "$timestamp" },
              totalReadings: { $sum: 1 },
            },
          },
        ])
        .toArray(),
      database
        .collection("readings")
        .aggregate([
          { $sort: { timestamp: -1 } },
          {
            $group: {
              _id: { zone: "$zone", type: "$type" },
              sensorId: { $first: "$sensorId" },
              value: { $first: "$value" },
              unit: { $first: "$unit" },
              timestamp: { $first: "$timestamp" },
              gatewayId: { $first: "$gatewayId" },
            },
          },
        ])
        .toArray(),
      database.collection("readings").countDocuments({}),
      database.collection("analyses").countDocuments({}),
    ]);

    const activityByZoneType = new Map(
      zoneTypeActivity.map((row) => [`${row._id.zone}:${row._id.type}`, row])
    );
    const latestByZoneType = new Map(
      latestDocs.map((row) => [`${row._id.zone}:${row._id.type}`, {
        sensorId: row.sensorId || "",
        zone: row._id.zone || "",
        type: row._id.type || "",
        value: Number(row.value),
        unit: row.unit || "",
        timestamp: toIso(row.timestamp),
        gatewayId: row.gatewayId || "",
      }])
    );

    const zones = protocol.ZONES.map((zone) => {
      const zoneDevices = devices.filter((device) => device.zone === zone);
      const gateway = gatewayForZone(zone);
      const typeSet = new Set(zoneDevices.flatMap((device) => device.types));
      const latest = Array.from(typeSet)
        .map((type) => latestByZoneType.get(`${zone}:${type}`))
        .filter(Boolean)
        .sort((a, b) => new Date(b.timestamp) - new Date(a.timestamp));
      const latestAt = latest[0]?.timestamp || null;
      const totalZoneReadings = Array.from(typeSet).reduce((sum, type) => {
        const activity = activityByZoneType.get(`${zone}:${type}`);
        return sum + (Number(activity?.totalReadings) || 0);
      }, 0);

      return {
        zone,
        label: protocol.ZONE_LABELS[zone] || zone,
        gateway,
        sensorCount: zoneDevices.length,
        logicalSensorCount: zoneDevices.reduce((sum, device) => sum + device.types.length, 0),
        nodeCount: zoneDevices.filter((device) => device.runtime === "Node.js").length,
        csharpCount: zoneDevices.filter((device) => device.runtime === "C#").length,
        types: Array.from(typeSet).sort(),
        totalReadings: totalZoneReadings,
        latestAt,
      };
    });

    const gatewayRows = zones.map((zone) => ({
      ...zone.gateway,
      zone: zone.zone,
      label: zone.label,
      sensorCount: zone.sensorCount,
      logicalSensorCount: zone.logicalSensorCount,
      totalReadings: zone.totalReadings,
      latestAt: zone.latestAt,
    }));

    res.json({
      generatedAt: new Date().toISOString(),
      summary: {
        zones: protocol.ZONES.length,
        gateways: gatewayRows.length,
        physicalSensors: devices.length,
        logicalSensors,
        nodeSensors: devices.filter((device) => device.runtime === "Node.js").length,
        csharpSensors: devices.filter((device) => device.runtime === "C#").length,
        totalReadings,
        analysisCount,
      },
      zones,
      gateways: gatewayRows,
      devices: devices.map((device) => ({
        ...device,
        gateway: gatewayForZone(device.zone),
      })),
      typeMatrix: summarizeTypeMatrix(devices),
      services: buildServiceInventory(devices),
      pipeline: [
        { step: 1, name: "Sensores fisicos", detail: "Node.js e C# publicam varias medicoes por dispositivo." },
        { step: 2, name: "RabbitMQ", detail: "Mensagens entram no exchange por routing key zona.tipo.sensor." },
        { step: 3, name: "Gateway da zona", detail: "Cada gateway consome apenas a sua zona e agrega por tipo." },
        { step: 4, name: "Preprocessamento", detail: "Valores sao normalizados antes de seguir para o servidor." },
        { step: 5, name: "Servidor", detail: "Consome eventos Gateway->Servidor no RabbitMQ e persiste em MongoDB." },
        { step: 6, name: "Dashboard/Analise", detail: "Consulta MongoDB e pede analises ao Servidor, que invoca o gRPC de analise." },
      ],
      queues: {
        exchange: process.env.RABBIT_EXCHANGE || "sensor_data",
        gatewayExchange: process.env.RABBIT_GATEWAY_EXCHANGE || "gateway.exchange",
        gatewayQueue: process.env.RABBIT_GATEWAY_QUEUE || "server.gateway.ingest",
        pattern: "ZONA_*.TIPO.SENSOR_ID",
        persistedSensorId: "AGREGADO_GW_*",
        note: "A BD guarda leituras agregadas por gateway/zona/tipo; os dispositivos fisicos registados vêm do sensors.csv.",
      },
    });
  } catch (error) {
    next(error);
  }
});

app.get("/api/protocol", (_req, res) => {
  res.json({
    sensorTypes: protocol.SENSOR_TYPES,
    zones: protocol.ZONES,
    sensorStates: protocol.SENSOR_STATES,
    unitsByType: protocol.UNITS_BY_TYPE,
    zoneLabels: protocol.ZONE_LABELS
  });
});

// --------------------------- Estaticos --------------------------------
const SHARED_DIR = path.join(__dirname, "..", "Shared");
const PUBLIC_DIR = path.join(__dirname, "public");
const SENSORS_CSV_PATH = path.join(__dirname, "..", "Gateway", "sensors.csv");
app.use("/shared", express.static(SHARED_DIR));
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
