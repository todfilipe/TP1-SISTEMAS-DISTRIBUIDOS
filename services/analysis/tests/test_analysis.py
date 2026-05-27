"""
Testes mínimos do serviço de Análise.
Cobrem: média correta, deteção de outlier conhecido e tendência crescente/decrescente.

Para correr (a partir da raiz do repositório):  pytest services/
"""
import os
import sys
import importlib.util

# Pasta do serviço (services/analysis) — necessária para os stubs locais
SERVICE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, SERVICE_DIR)

# Carregar server.py com nome único para evitar colisão com o do preprocessing
_spec = importlib.util.spec_from_file_location(
    "analysis_server", os.path.join(SERVICE_DIR, "server.py")
)
server = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(server)

import analysis_pb2  # noqa: E402


def _analyze(values, tipo="TEMP"):
    servicer = server.AnalysisServiceServicer()
    request = analysis_pb2.AnalysisRequest(type=tipo)
    for i, v in enumerate(values):
        request.readings.add(value=float(v), timestamp=f"2026-01-01T00:00:{i:02d}", type=tipo)
    return servicer.Analyze(request, None)


def test_media_correta():
    resp = _analyze([10.0, 20.0, 30.0])
    assert abs(resp.mean - 20.0) < 0.001
    assert abs(resp.computedAverage - 20.0) < 0.001
    assert resp.sampleCount == 3


def test_mediana_e_percentis():
    resp = _analyze([10.0, 20.0, 30.0, 40.0, 50.0])
    assert abs(resp.median - 30.0) < 0.001
    assert abs(resp.percentile25 - 20.0) < 0.001
    assert abs(resp.percentile75 - 40.0) < 0.001
    assert abs(resp.percentile95 - 48.0) < 0.001
    assert "median=30.00" in resp.resultSummary


def test_deteta_outlier_conhecido():
    # Série estável com um valor obviamente fora
    valores = [10.0, 10.1, 9.9, 10.0, 10.2, 9.8, 100.0]
    resp = _analyze(valores)
    assert resp.outliersCount >= 1


def test_tendencia_crescente():
    resp = _analyze([1.0, 2.0, 3.0, 4.0, 5.0])
    assert resp.trend == "Increasing"
    assert resp.trendSlope > 0


def test_tendencia_decrescente():
    resp = _analyze([5.0, 4.0, 3.0, 2.0, 1.0])
    assert resp.trend == "Decreasing"
    assert resp.trendSlope < 0


def test_previsao_linear():
    servicer = server.AnalysisServiceServicer()
    req = analysis_pb2.PredictionRequest(type="TEMP", periodsToPredict=3, strategy="linear")
    for i, v in enumerate([1.0, 2.0, 3.0, 4.0]):
        req.readings.add(value=v, timestamp=f"t{i}", type="TEMP")
    resp = servicer.Predict(req, None)
    assert resp.strategyUsed == "linear"
    assert len(resp.forecast) == 3
    # Continuação da reta y=x+1 -> próximos ~5,6,7
    assert resp.forecast[0] > 4.0


def test_previsao_ewma():
    servicer = server.AnalysisServiceServicer()
    req = analysis_pb2.PredictionRequest(type="TEMP", periodsToPredict=2, strategy="ewma")
    for i, v in enumerate([10.0, 10.0, 10.0, 10.0]):
        req.readings.add(value=v, timestamp=f"t{i}", type="TEMP")
    resp = servicer.Predict(req, None)
    assert resp.strategyUsed == "ewma"
    assert len(resp.forecast) == 2
    assert abs(resp.forecast[0] - 10.0) < 0.001
