(function (root, factory) {
  const constants = factory();

  if (typeof module === "object" && module.exports) {
    module.exports = constants;
  }

  root.ProtocolConstants = constants;
})(typeof globalThis !== "undefined" ? globalThis : this, function () {
  const SENSOR_TYPES = Object.freeze([
    "TEMP",
    "HUM",
    "AR",
    "RUIDO",
    "PM2.5",
    "PM10",
    "LUZ",
    "VIDEO"
  ]);

  const ZONES = Object.freeze([
    "ZONA_CENTRO",
    "ZONA_ESCOLAR",
    "ZONA_INDUSTRIAL",
    "ZONA_RESIDENCIAL",
    "ZONA_PARQUE"
  ]);

  const SENSOR_STATES = Object.freeze([
    "ativo",
    "manutencao",
    "desativado",
    "indisponivel",
    "desligado"
  ]);

  const UNITS_BY_TYPE = Object.freeze({
    TEMP: "C",
    HUM: "%",
    AR: "ug/m3",
    RUIDO: "dB",
    "PM2.5": "ug/m3",
    PM10: "ug/m3",
    LUZ: "lux",
    VIDEO: "frame"
  });

  const ZONE_LABELS = Object.freeze({
    ZONA_CENTRO: "Centro",
    ZONA_ESCOLAR: "Escolar",
    ZONA_INDUSTRIAL: "Industrial",
    ZONA_RESIDENCIAL: "Residencial",
    ZONA_PARQUE: "Parque"
  });

  function isValidSensorType(type) {
    return SENSOR_TYPES.includes(String(type || "").trim().toUpperCase());
  }

  function isValidZone(zone) {
    return ZONES.includes(String(zone || "").trim().toUpperCase());
  }

  function isValidSensorState(state) {
    return SENSOR_STATES.includes(String(state || "").trim().toLowerCase());
  }

  return Object.freeze({
    SENSOR_TYPES,
    ZONES,
    SENSOR_STATES,
    UNITS_BY_TYPE,
    ZONE_LABELS,
    isValidSensorType,
    isValidZone,
    isValidSensorState
  });
});
