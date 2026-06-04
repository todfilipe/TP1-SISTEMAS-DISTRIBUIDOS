from __future__ import annotations

import asyncio
import hashlib
import math
import random
from datetime import datetime
from threading import RLock

from events import EventManager
from zones import (
    MEASUREMENT_SIGMA,
    RANDOM_WALK_SIGMA,
    SENSOR_TYPES,
    VALID_RANGES,
    VALID_ZONES,
    ZONE_PROFILES,
)


class EnvironmentSimulator:
    def __init__(self, events: EventManager) -> None:
        self._events = events
        self._lock = RLock()
        self._rng = random.Random()
        self._state: dict[str, dict[str, float]] = {}
        self._initialize_state()

    def _initialize_state(self) -> None:
        now = datetime.now()
        with self._lock:
            self._state = {
                zone: {
                    sensor_type: self._clamp(sensor_type, self._target_value(zone, sensor_type, now))
                    for sensor_type in SENSOR_TYPES
                }
                for zone in VALID_ZONES
            }

    async def run(self, stop_event: asyncio.Event) -> None:
        while not stop_event.is_set():
            self.tick()
            try:
                await asyncio.wait_for(stop_event.wait(), timeout=1.0)
            except asyncio.TimeoutError:
                pass

    def tick(self) -> None:
        now = datetime.now()
        self._events.cleanup_expired()

        with self._lock:
            for zone in VALID_ZONES:
                for sensor_type in SENSOR_TYPES:
                    current = self._state[zone][sensor_type]
                    target = self._target_value(zone, sensor_type, now)
                    noise = self._rng.gauss(0.0, RANDOM_WALK_SIGMA[sensor_type])
                    pull_to_target = (target - current) * 0.05
                    self._state[zone][sensor_type] = self._clamp(
                        sensor_type,
                        current + noise + pull_to_target,
                    )

    def reading(self, zone: str, sensor_type: str, sensor_id: str | None = None) -> float:
        with self._lock:
            value = self._state[zone][sensor_type]

        if sensor_id:
            value += self._sensor_bias(sensor_id, sensor_type)

        return round(self._clamp(sensor_type, value), 2)

    def state_for_zone(self, zone: str) -> dict[str, float]:
        with self._lock:
            return {
                sensor_type: round(value, 2)
                for sensor_type, value in self._state[zone].items()
            }

    def all_state(self) -> dict[str, dict[str, float]]:
        with self._lock:
            return {
                zone: {
                    sensor_type: round(value, 2)
                    for sensor_type, value in readings.items()
                }
                for zone, readings in self._state.items()
            }

    def _target_value(self, zone: str, sensor_type: str, now: datetime) -> float:
        hour = now.hour + now.minute / 60.0 + now.second / 3600.0
        zone_offset = ZONE_PROFILES[zone].get(sensor_type, 0.0)

        if sensor_type == "TEMP":
            value = self._temperature_target(hour)
        elif sensor_type == "LUZ":
            value = self._light_target(hour)
        elif sensor_type == "RUIDO":
            value = self._noise_target(hour, zone)
        elif sensor_type == "HUM":
            temp = self._temperature_target(hour) + ZONE_PROFILES[zone].get("TEMP", 0.0)
            value = 72.0 - ((temp - 18.0) * 1.35) + 3.0 * math.sin(hour * math.pi / 12.0)
        elif sensor_type == "PM2.5":
            value = 5.0 + self._traffic_intensity((hour - 0.4) % 24.0) * 28.0
        elif sensor_type == "PM10":
            value = 10.0 + self._traffic_intensity((hour - 0.6) % 24.0) * 55.0
        elif sensor_type == "AR":
            pm25 = 5.0 + self._traffic_intensity((hour - 0.4) % 24.0) * 28.0
            pm10 = 10.0 + self._traffic_intensity((hour - 0.6) % 24.0) * 55.0
            value = 0.4 + (pm25 / 35.0) + (pm10 / 90.0)
        else:
            value = 0.0

        return self._clamp(sensor_type, value + zone_offset + self._events.effect_for(zone, sensor_type))

    def _temperature_target(self, hour: float) -> float:
        min_temp = 14.0
        max_temp = 28.0

        if 6.0 <= hour <= 15.0:
            progress = (hour - 6.0) / 9.0
            return min_temp + (max_temp - min_temp) * math.sin(progress * math.pi / 2.0)

        hours_after_peak = (hour - 15.0) % 24.0
        progress = min(hours_after_peak / 15.0, 1.0)
        return max_temp - (max_temp - min_temp) * (1.0 - math.cos(progress * math.pi)) / 2.0

    def _light_target(self, hour: float) -> float:
        peak = 900.0
        if hour < 6.0 or hour >= 21.0:
            return 0.0
        if hour <= 12.0:
            return peak * math.sin(((hour - 6.0) / 6.0) * math.pi / 2.0)
        return peak * math.sin(((21.0 - hour) / 9.0) * math.pi / 2.0)

    def _noise_target(self, hour: float, zone: str) -> float:
        value = 35.0 + self._traffic_intensity(hour) * 38.0

        if zone == "ZONA_ESCOLAR":
            school_peaks = [
                self._peak(hour, 8.5, 0.5),
                self._peak(hour, 12.5, 0.5),
                self._peak(hour, 17.5, 0.5),
            ]
            value += max(school_peaks) * 12.0

        return value

    def _traffic_intensity(self, hour: float) -> float:
        morning_peak = self._peak(hour, 8.0, 1.2)
        evening_peak = self._peak(hour, 18.0, 1.4)

        if 0.0 <= hour < 6.0:
            baseline = 0.12
        elif 9.0 <= hour < 17.0:
            baseline = 0.5
        elif 19.0 <= hour < 23.0:
            baseline = max(0.18, 0.45 - (hour - 19.0) * 0.07)
        else:
            baseline = 0.28

        return min(1.0, baseline + morning_peak * 0.72 + evening_peak * 0.78)

    def _peak(self, hour: float, center: float, half_width: float) -> float:
        distance = abs(hour - center)
        return max(0.0, 1.0 - distance / half_width)

    def _sensor_bias(self, sensor_id: str, sensor_type: str) -> float:
        key = f"{sensor_id}:{sensor_type}".encode("utf-8")
        seed = int.from_bytes(hashlib.sha256(key).digest()[:8], byteorder="big", signed=False)
        rng = random.Random(seed)
        return rng.gauss(0.0, MEASUREMENT_SIGMA[sensor_type])

    def _clamp(self, sensor_type: str, value: float) -> float:
        min_value, max_value = VALID_RANGES[sensor_type]
        return max(min_value, min(max_value, value))
