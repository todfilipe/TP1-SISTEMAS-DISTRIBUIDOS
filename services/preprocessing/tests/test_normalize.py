"""
Testes mínimos do serviço de Pré-Processamento.
Cobrem: conversão F->C, parsing JSON/XML/CSV e rejeição de valores fora do range.

Para correr (a partir da raiz do repositório):  pytest services/
"""
import os
import sys
import importlib.util

# Pasta do serviço (services/preprocessing) — necessária para os stubs locais
SERVICE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, SERVICE_DIR)

# Carregar server.py com nome único para evitar colisão com o do analysis
_spec = importlib.util.spec_from_file_location(
    "preproc_server", os.path.join(SERVICE_DIR, "server.py")
)
server = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(server)

import preprocessing_pb2  # noqa: E402


def _normalize(**kwargs):
    servicer = server.PreprocessingServiceServicer()
    request = preprocessing_pb2.RawReading(**kwargs)
    return servicer.Normalize(request, None)


def test_conversao_fahrenheit_para_celsius():
    # 98.6 F == 37.0 C (dentro do range válido [-50, 60])
    resp = _normalize(sensorId="S1", type="TEMP", value=98.6, unit="F")
    assert resp.unit == "C"
    assert abs(resp.value - 37.0) < 0.01
    assert resp.isValid is True


def test_conversao_kelvin_para_celsius():
    # 273.15 K == 0 C
    resp = _normalize(sensorId="S1", type="TEMP", value=273.15, unit="K")
    assert resp.unit == "C"
    assert abs(resp.value - 0.0) < 0.001


def test_parse_json():
    payload = '{"sensorId":"S9","type":"HUM","value":55.5,"unit":"%","timestamp":"2026-01-01T00:00:00"}'
    resp = _normalize(rawFormat=payload)
    assert resp.sensorId == "S9"
    assert resp.type == "HUM"
    assert abs(resp.value - 55.5) < 0.001
    assert resp.isValid is True


def test_parse_xml():
    payload = "<reading><sensorId>S7</sensorId><type>TEMP</type><value>21.0</value><unit>C</unit></reading>"
    resp = _normalize(rawFormat=payload)
    assert resp.sensorId == "S7"
    assert resp.type == "TEMP"
    assert abs(resp.value - 21.0) < 0.001


def test_parse_csv():
    # sensorId, type, value, unit, timestamp, zone
    payload = "S3,TEMP,18.5,C,2026-01-01T00:00:00,ZONA_CENTRO"
    resp = _normalize(rawFormat=payload)
    assert resp.sensorId == "S3"
    assert resp.type == "TEMP"
    assert abs(resp.value - 18.5) < 0.001
    assert resp.zone == "ZONA_CENTRO"


def test_rejeita_valor_fora_de_range():
    # Temperatura impossível -> isValid False
    resp = _normalize(sensorId="S1", type="TEMP", value=999.0, unit="C")
    assert resp.isValid is False


def test_propaga_zona():
    resp = _normalize(sensorId="S1", type="TEMP", value=20.0, unit="C", zone="ZONA_CENTRO")
    assert resp.zone == "ZONA_CENTRO"
