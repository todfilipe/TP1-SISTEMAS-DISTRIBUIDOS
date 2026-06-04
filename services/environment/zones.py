VALID_ZONES = [
    "ZONA_CENTRO",
    "ZONA_ESCOLAR",
    "ZONA_INDUSTRIAL",
    "ZONA_RESIDENCIAL",
    "ZONA_PARQUE",
]

SENSOR_TYPES = ["TEMP", "HUM", "RUIDO", "PM2.5", "PM10", "LUZ", "AR"]

UNITS_BY_TYPE = {
    "TEMP": "C",
    "HUM": "%",
    "RUIDO": "dB",
    "PM2.5": "ug/m3",
    "PM10": "ug/m3",
    "LUZ": "lux",
    "AR": "ug/m3",
}

VALID_RANGES = {
    "TEMP": (-50.0, 60.0),
    "HUM": (0.0, 100.0),
    "RUIDO": (0.0, 150.0),
    "PM2.5": (0.0, 1000.0),
    "PM10": (0.0, 1000.0),
    "LUZ": (0.0, 100000.0),
    "AR": (0.0, 1000.0),
}

RANDOM_WALK_SIGMA = {
    "TEMP": 0.05,
    "HUM": 0.1,
    "RUIDO": 0.5,
    "PM2.5": 0.2,
    "PM10": 0.3,
    "LUZ": 5.0,
    "AR": 0.02,
}

MEASUREMENT_SIGMA = {
    "TEMP": 0.3,
    "HUM": 1.0,
    "RUIDO": 1.5,
    "PM2.5": 0.5,
    "PM10": 0.8,
    "LUZ": 10.0,
    "AR": 0.05,
}

ZONE_PROFILES = {
    "ZONA_INDUSTRIAL": {
        "TEMP": +2.0,
        "RUIDO": +20.0,
        "PM2.5": +15.0,
        "PM10": +25.0,
        "AR": +1.5,
        "HUM": -5.0,
        "LUZ": -50.0,
    },
    "ZONA_PARQUE": {
        "TEMP": -1.0,
        "RUIDO": -15.0,
        "PM2.5": -4.0,
        "PM10": -8.0,
        "AR": -1.0,
        "HUM": +8.0,
        "LUZ": +100.0,
    },
    "ZONA_CENTRO": {
        "TEMP": +1.5,
        "RUIDO": +10.0,
        "PM2.5": +8.0,
        "PM10": +12.0,
        "AR": +0.8,
        "HUM": -3.0,
        "LUZ": -30.0,
    },
    "ZONA_ESCOLAR": {
        "TEMP": 0.0,
        "RUIDO": 0.0,
        "PM2.5": +2.0,
        "PM10": +3.0,
        "AR": +0.2,
        "HUM": 0.0,
        "LUZ": 0.0,
    },
    "ZONA_RESIDENCIAL": {
        "TEMP": 0.0,
        "RUIDO": -8.0,
        "PM2.5": -2.0,
        "PM10": -3.0,
        "AR": -0.3,
        "HUM": +2.0,
        "LUZ": +20.0,
    },
}

