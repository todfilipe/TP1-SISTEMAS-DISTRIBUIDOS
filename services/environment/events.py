from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from threading import RLock
from typing import Dict, Iterable, List
from uuid import uuid4


@dataclass
class EnvironmentEvent:
    name: str
    zones: List[str]
    duration_seconds: int
    effects: Dict[str, float]
    created_at: datetime = field(default_factory=lambda: datetime.now(timezone.utc))
    id: str = field(default_factory=lambda: uuid4().hex)

    @property
    def expiry_time(self) -> datetime:
        return self.created_at + timedelta(seconds=self.duration_seconds)

    def seconds_remaining(self, now: datetime | None = None) -> int:
        now = now or datetime.now(timezone.utc)
        remaining = (self.expiry_time - now).total_seconds()
        return max(0, int(remaining))

    def is_active(self, now: datetime | None = None) -> bool:
        return self.seconds_remaining(now) > 0


class EventManager:
    def __init__(self) -> None:
        self._events: list[EnvironmentEvent] = []
        self._lock = RLock()

    def add_event(
        self,
        name: str,
        zones: Iterable[str],
        duration_seconds: int,
        effects: Dict[str, float],
    ) -> EnvironmentEvent:
        event = EnvironmentEvent(
            name=name,
            zones=list(zones),
            duration_seconds=duration_seconds,
            effects=dict(effects),
        )
        with self._lock:
            self._events.append(event)
        return event

    def cleanup_expired(self) -> None:
        now = datetime.now(timezone.utc)
        with self._lock:
            self._events = [event for event in self._events if event.is_active(now)]

    def effect_for(self, zone: str, sensor_type: str) -> float:
        self.cleanup_expired()
        with self._lock:
            return sum(
                event.effects.get(sensor_type, 0.0)
                for event in self._events
                if zone in event.zones
            )

    def list_active(self) -> list[dict]:
        self.cleanup_expired()
        now = datetime.now(timezone.utc)
        with self._lock:
            return [
                {
                    "id": event.id,
                    "name": event.name,
                    "zones": event.zones,
                    "durationSeconds": event.duration_seconds,
                    "secondsRemaining": event.seconds_remaining(now),
                    "effects": event.effects,
                    "createdAt": event.created_at.isoformat(),
                    "expiresAt": event.expiry_time.isoformat(),
                }
                for event in self._events
            ]

