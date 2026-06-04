from __future__ import annotations

import asyncio
from contextlib import asynccontextmanager
from datetime import datetime, timezone
from typing import Dict, List, Optional

from fastapi import FastAPI, HTTPException, Query
from pydantic import BaseModel, Field

from events import EventManager
from simulator import EnvironmentSimulator
from zones import SENSOR_TYPES, UNITS_BY_TYPE, VALID_ZONES


event_manager = EventManager()
simulator = EnvironmentSimulator(event_manager)
simulation_task: asyncio.Task | None = None


class EventRequest(BaseModel):
    name: str
    zones: List[str]
    duration_seconds: int = Field(alias="durationSeconds", gt=0)
    effects: Dict[str, float]


@asynccontextmanager
async def lifespan(app: FastAPI):
    global simulation_task
    stop_event = asyncio.Event()
    stop_event.clear()
    simulation_task = asyncio.create_task(simulator.run(stop_event))
    try:
        yield
    finally:
        stop_event.set()
        if simulation_task:
            await simulation_task


app = FastAPI(
    title="Environment Service",
    description="Simulador em memoria do mundo fisico para sensores One Health.",
    version="1.0.0",
    lifespan=lifespan,
)


@app.get("/health")
def health() -> dict:
    return {"status": "ok", "timestamp": _now_iso()}


@app.get("/reading")
def reading(
    zone: str = Query(...),
    type: str = Query(...),
    sensorId: Optional[str] = Query(default=None),
) -> dict:
    zone = _validate_zone(zone)
    sensor_type = _validate_type(type)
    value = simulator.reading(zone, sensor_type, sensorId)
    return {
        "zone": zone,
        "type": sensor_type,
        "value": value,
        "unit": UNITS_BY_TYPE[sensor_type],
        "timestamp": _now_iso(),
    }


@app.get("/state")
def state(zone: Optional[str] = Query(default=None)) -> dict:
    if zone:
        zone = _validate_zone(zone)
        return {
            "zone": zone,
            "timestamp": _now_iso(),
            "readings": simulator.state_for_zone(zone),
        }

    return {
        "timestamp": _now_iso(),
        "zones": simulator.all_state(),
    }


@app.post("/events", status_code=201)
def create_event(request: EventRequest) -> dict:
    zones = [_validate_zone(zone) for zone in request.zones]
    effects = {
        _validate_type(sensor_type): float(delta)
        for sensor_type, delta in request.effects.items()
    }

    event = event_manager.add_event(
        name=request.name,
        zones=zones,
        duration_seconds=request.duration_seconds,
        effects=effects,
    )
    return {
        "id": event.id,
        "name": event.name,
        "zones": event.zones,
        "durationSeconds": event.duration_seconds,
        "secondsRemaining": event.seconds_remaining(),
        "effects": event.effects,
    }


@app.get("/events")
def list_events() -> dict:
    return {"events": event_manager.list_active(), "timestamp": _now_iso()}


def _validate_zone(zone: str) -> str:
    normalized = zone.strip().upper()
    if normalized not in VALID_ZONES:
        raise HTTPException(status_code=400, detail=f"Invalid zone: {zone}")
    return normalized


def _validate_type(sensor_type: str) -> str:
    normalized = sensor_type.strip().upper()
    if normalized not in SENSOR_TYPES:
        raise HTTPException(status_code=400, detail=f"Invalid type: {sensor_type}")
    return normalized


def _now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()
