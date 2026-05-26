const databaseName = process.env.MONGO_INITDB_DATABASE || "urbanodb";
const database = db.getSiblingDB(databaseName);

const collections = ["readings", "analyses", "sensors_metadata"];

collections.forEach((collectionName) => {
  if (!database.getCollectionNames().includes(collectionName)) {
    database.createCollection(collectionName);
  }
});

database.readings.createIndex(
  { sensorId: 1, timestamp: -1 },
  { name: "idx_readings_sensorId_timestamp_desc" }
);
database.readings.createIndex(
  { zone: 1, type: 1 },
  { name: "idx_readings_zone_type" }
);

database.analyses.createIndex(
  { type: 1, createdAt: -1 },
  { name: "idx_analyses_type_createdAt_desc" }
);
database.analyses.createIndex(
  { sensorId: 1 },
  { name: "idx_analyses_sensorId" }
);

database.sensors_metadata.createIndex(
  { sensorId: 1 },
  { name: "idx_sensors_metadata_sensorId_unique", unique: true }
);
