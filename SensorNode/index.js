const amqp = require("amqplib");
const fs = require("fs");
const path = require("path");
const {
  UNITS_BY_TYPE,
  isValidSensorType,
  isValidZone
} = require("../Shared/protocol");

const EXCHANGE_NAME = "sensors.exchange";
const EXCHANGE_TYPE = "topic";
const RECONNECT_DELAY_MS = 5000;

const VALID_PAYLOAD_FORMATS = new Set(["JSON", "XML", "CSV"]);

const DEFAULT_CONFIG = {
  sensorId: "SNJ01",
  zone: "ZONA_CENTRO",
  type: "TEMP",
  intervalSeconds: 5,
  rabbitHost: "localhost",
  rabbitPort: 5672,
  rabbitUser: "admin",
  rabbitPass: "admin",
  rabbitVHost: "onehealth",
  payloadFormat: "JSON"
};

function readConfigFile() {
  const configPath = path.join(__dirname, "config.json");

  if (!fs.existsSync(configPath)) {
    return {};
  }

  try {
    return JSON.parse(fs.readFileSync(configPath, "utf8"));
  } catch (error) {
    console.warn(`[CONFIG] Falha ao ler config.json. A usar defaults. Erro: ${error.message}`);
    return {};
  }
}

function firstEnv(...names) {
  for (const name of names) {
    if (process.env[name] !== undefined && process.env[name] !== "") {
      return process.env[name];
    }
  }

  return undefined;
}

function toInt(value, fallback) {
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

function normalizeConfig() {
  const fileConfig = readConfigFile();
  const config = {
    ...DEFAULT_CONFIG,
    ...fileConfig,
    sensorId: firstEnv("SENSOR_ID", "SENSOR_NODE_ID", "SENSORID") ?? fileConfig.sensorId ?? DEFAULT_CONFIG.sensorId,
    zone: firstEnv("SENSOR_ZONE", "SENSOR_NODE_ZONE", "ZONE") ?? fileConfig.zone ?? DEFAULT_CONFIG.zone,
    type: firstEnv("SENSOR_TYPE", "SENSOR_NODE_TYPE", "TYPE") ?? fileConfig.type ?? DEFAULT_CONFIG.type,
    intervalSeconds:
      firstEnv("INTERVAL_SECONDS", "SENSOR_INTERVAL_SECONDS", "SENSOR_NODE_INTERVAL_SECONDS") ??
      fileConfig.intervalSeconds ??
      DEFAULT_CONFIG.intervalSeconds,
    rabbitHost:
      firstEnv("RABBIT_HOST", "SENSOR_NODE_RABBIT_HOST", "RABBITMQ_HOST") ??
      fileConfig.rabbitHost ??
      DEFAULT_CONFIG.rabbitHost,
    rabbitPort:
      firstEnv("RABBIT_PORT", "SENSOR_NODE_RABBIT_PORT", "RABBITMQ_PORT") ??
      fileConfig.rabbitPort ??
      DEFAULT_CONFIG.rabbitPort,
    rabbitUser:
      firstEnv("RABBIT_USER", "SENSOR_NODE_RABBIT_USER", "RABBITMQ_USER") ??
      fileConfig.rabbitUser ??
      DEFAULT_CONFIG.rabbitUser,
    rabbitPass:
      firstEnv("RABBIT_PASS", "SENSOR_NODE_RABBIT_PASS", "RABBITMQ_PASS", "RABBITMQ_PASSWORD") ??
      fileConfig.rabbitPass ??
      DEFAULT_CONFIG.rabbitPass,
    rabbitVHost:
      firstEnv("RABBIT_VHOST", "SENSOR_NODE_RABBIT_VHOST", "RABBITMQ_VHOST", "RABBIT_VIRTUAL_HOST") ??
      fileConfig.rabbitVHost ??
      DEFAULT_CONFIG.rabbitVHost,
    payloadFormat:
      firstEnv("PAYLOAD_FORMAT", "SENSOR_PAYLOAD_FORMAT", "SENSOR_NODE_PAYLOAD_FORMAT") ??
      fileConfig.payloadFormat ??
      DEFAULT_CONFIG.payloadFormat
  };

  config.sensorId = String(config.sensorId).trim() || DEFAULT_CONFIG.sensorId;
  config.zone = String(config.zone).trim().toUpperCase();
  config.type = String(config.type).trim().toUpperCase();
  config.intervalSeconds = toInt(config.intervalSeconds, DEFAULT_CONFIG.intervalSeconds);
  config.rabbitPort = toInt(config.rabbitPort, DEFAULT_CONFIG.rabbitPort);
  config.payloadFormat = String(config.payloadFormat).trim().toUpperCase();

  if (!isValidZone(config.zone)) {
    console.warn(`[CONFIG] Zona invalida '${config.zone}'. A usar ${DEFAULT_CONFIG.zone}.`);
    config.zone = DEFAULT_CONFIG.zone;
  }

  if (!isValidSensorType(config.type)) {
    console.warn(`[CONFIG] Tipo invalido '${config.type}'. A usar ${DEFAULT_CONFIG.type}.`);
    config.type = DEFAULT_CONFIG.type;
  }

  if (!VALID_PAYLOAD_FORMATS.has(config.payloadFormat)) {
    console.warn(`[CONFIG] payloadFormat invalido '${config.payloadFormat}'. A usar ${DEFAULT_CONFIG.payloadFormat}.`);
    config.payloadFormat = DEFAULT_CONFIG.payloadFormat;
  }

  return config;
}

function timestampNow() {
  return new Date().toISOString().slice(0, 19);
}

function randomBetween(min, max, decimals = 1) {
  const factor = 10 ** decimals;
  return Math.round((min + Math.random() * (max - min)) * factor) / factor;
}

function generateValue(type) {
  switch (type) {
    case "TEMP":
      return randomBetween(15, 40);
    case "HUM":
      return randomBetween(30, 90);
    case "AR":
    case "PM2.5":
    case "PM10":
      return randomBetween(10, 150);
    case "RUIDO":
      return randomBetween(30, 90);
    case "LUZ":
      return randomBetween(100, 100000);
    default:
      return randomBetween(0, 100);
  }
}

function getUnit(type) {
  return UNITS_BY_TYPE[type] || "";
}

function escapeXml(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&apos;");
}

function escapeCsv(value) {
  const text = String(value);
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

function buildRawPayload(config, type, value, unit, timestamp) {
  if (config.payloadFormat === "XML") {
    return `<reading><sensorId>${escapeXml(config.sensorId)}</sensorId><zone>${escapeXml(config.zone)}</zone><type>${escapeXml(type)}</type><value>${escapeXml(value)}</value><unit>${escapeXml(unit)}</unit><timestamp>${escapeXml(timestamp)}</timestamp></reading>`;
  }

  if (config.payloadFormat === "CSV") {
    return [
      config.sensorId,
      type,
      value,
      unit,
      timestamp,
      config.zone
    ].map(escapeCsv).join(",");
  }

  return JSON.stringify({
    sensorId: config.sensorId,
    zone: config.zone,
    type,
    value,
    unit,
    timestamp
  });
}

class SensorNode {
  constructor(config) {
    this.config = config;
    this.connection = null;
    this.channel = null;
    this.publishTimer = null;
    this.reconnectTimer = null;
    this.connecting = false;
    this.stopping = false;
    this.publishCount = 0;
  }

  async start() {
    console.log(`[SENSOR NODE] ID=${this.config.sensorId}, Zona=${this.config.zone}, Tipo=${this.config.type}, Intervalo=${this.config.intervalSeconds}s, Payload=${this.config.payloadFormat}`);
    await this.connectWithRetry();
    this.publishTimer = setInterval(() => {
      this.publishReading().catch((error) => {
        console.error(`[DATA] Falha inesperada: ${error.message}`);
      });
    }, this.config.intervalSeconds * 1000);
    await this.publishReading();
  }

  async connectWithRetry() {
    if (this.connecting || this.stopping) {
      return;
    }

    this.connecting = true;

    while (!this.stopping && !this.channel) {
      try {
        console.log(`[RABBITMQ] A ligar a ${this.config.rabbitHost}:${this.config.rabbitPort} vhost=${this.config.rabbitVHost}...`);
        const connection = await amqp.connect({
          protocol: "amqp",
          hostname: this.config.rabbitHost,
          port: this.config.rabbitPort,
          username: this.config.rabbitUser,
          password: this.config.rabbitPass,
          vhost: this.config.rabbitVHost
        });

        connection.on("error", (error) => {
          if (!this.stopping) {
            console.error(`[RABBITMQ] Erro de ligacao: ${error.message}`);
          }
        });

        connection.on("close", () => {
          this.connection = null;
          this.channel = null;
          if (!this.stopping) {
            console.warn(`[RABBITMQ] Ligacao fechada. Nova tentativa em ${RECONNECT_DELAY_MS / 1000}s.`);
            this.scheduleReconnect();
          }
        });

        const channel = await connection.createChannel();
        await channel.assertExchange(EXCHANGE_NAME, EXCHANGE_TYPE, {
          durable: true,
          autoDelete: false
        });

        this.connection = connection;
        this.channel = channel;
        console.log("[RABBITMQ] Ligacao estabelecida e exchange declarado.");
      } catch (error) {
        this.connection = null;
        this.channel = null;
        console.error(`[RABBITMQ] Falha na ligacao: ${error.message}`);
        await this.sleep(RECONNECT_DELAY_MS);
      }
    }

    this.connecting = false;
  }

  scheduleReconnect() {
    if (this.reconnectTimer || this.connecting || this.stopping) {
      return;
    }

    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      this.connectWithRetry().catch((error) => {
        console.error(`[RABBITMQ] Falha no ciclo de reconexao: ${error.message}`);
      });
    }, RECONNECT_DELAY_MS);
  }

  async publishReading() {
    const value = generateValue(this.config.type);
    const unit = getUnit(this.config.type);
    const timestamp = timestampNow();
    const message = {
      sensorId: this.config.sensorId,
      zone: this.config.zone,
      type: this.config.type,
      value,
      unit,
      timestamp,
      raw: buildRawPayload(this.config, this.config.type, value, unit, timestamp),
      rawFormat: this.config.payloadFormat
    };

    const published = this.publishMessage(message);

    if (published) {
      this.publishCount += 1;
      console.log(`[DATA] ${this.routingKey(this.config.type)} -> ${value} ${unit}`);

      if (this.publishCount % 5 === 0) {
        await this.publishControl("HEARTBEAT");
      }
    }
  }

  async publishControl(type) {
    const message = {
      sensorId: this.config.sensorId,
      zone: this.config.zone,
      type,
      value: 0,
      unit: "",
      timestamp: timestampNow(),
      raw: "",
      rawFormat: "JSON"
    };

    const published = this.publishMessage(message);
    if (published) {
      console.log(`[${type}] ${this.routingKey(type)} publicado.`);
    }
  }

  publishMessage(message) {
    if (!this.channel) {
      console.warn("[RABBITMQ] Canal indisponivel. Mensagem ignorada ate reconectar.");
      this.scheduleReconnect();
      return false;
    }

    try {
      const body = Buffer.from(JSON.stringify(message), "utf8");
      this.channel.publish(EXCHANGE_NAME, this.routingKey(message.type), body, {
        persistent: true,
        contentType: "application/json"
      });
      return true;
    } catch (error) {
      console.error(`[RABBITMQ] Falha ao publicar: ${error.message}`);
      this.channel = null;
      this.scheduleReconnect();
      return false;
    }
  }

  routingKey(type) {
    return `${this.config.zone}.${type}.${this.config.sensorId}`;
  }

  async shutdown(signal) {
    if (this.stopping) {
      return;
    }

    this.stopping = true;
    console.log(`\n[SENSOR NODE] Recebido ${signal}. A publicar DISCONNECT e terminar...`);

    if (this.publishTimer) {
      clearInterval(this.publishTimer);
    }

    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
    }

    try {
      await this.publishControl("DISCONNECT");
      await this.close();
    } catch (error) {
      console.error(`[SENSOR NODE] Erro ao terminar: ${error.message}`);
    } finally {
      process.exit(0);
    }
  }

  async close() {
    try {
      if (this.channel) {
        await this.channel.close();
      }
    } catch {
      // Ignorar erros no encerramento.
    }

    try {
      if (this.connection) {
        await this.connection.close();
      }
    } catch {
      // Ignorar erros no encerramento.
    }

    this.channel = null;
    this.connection = null;
  }

  sleep(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }
}

const sensor = new SensorNode(normalizeConfig());

process.once("SIGINT", () => {
  sensor.shutdown("SIGINT");
});

process.once("SIGTERM", () => {
  sensor.shutdown("SIGTERM");
});

sensor.start().catch((error) => {
  console.error(`[SENSOR NODE] Erro fatal: ${error.message}`);
  process.exit(1);
});
