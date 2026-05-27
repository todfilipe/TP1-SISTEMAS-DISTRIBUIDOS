"""
Serviço gRPC de Análise (porta 50052).

Princípio de desenho: este serviço é *puro* — recebe os dados a analisar
no próprio request (campo `readings`) e devolve resultados estatísticos.
NÃO conhece a estrutura da base de dados do cliente (já não importa sqlite3).
É o cliente (o Servidor) que recolhe as leituras dos seus stores e as envia.

Implementação assente no ecossistema de análise de dados do Python:
  - numpy        -> média, desvio padrão, mínimos/máximos, z-score
  - pandas       -> médias móveis (rolling) e previsão EWMA (ewm)
  - scikit-learn -> regressão linear (tendência e previsão linear)
"""

import os
import sys
import logging
from datetime import datetime
from concurrent import futures

import grpc
import numpy as np
import pandas as pd
from sklearn.linear_model import LinearRegression

# Garantir que os stubs locais (gerados na própria pasta) são importáveis
sys.path.append(os.path.dirname(os.path.abspath(__file__)))

import analysis_pb2
import analysis_pb2_grpc

# Configuração de logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    handlers=[logging.StreamHandler(sys.stdout)],
)
logger = logging.getLogger("analysis_service")


# ─────────────────────────────────────────────────────────────
# Algoritmos de análise (operam sobre listas de valores recebidas)
# ─────────────────────────────────────────────────────────────

def compute_moving_averages(values, window_size=5):
    """
    Calcula médias móveis com janela `window_size` usando pandas.rolling.
    Janelas parciais no início (min_periods=1), tal como na versão manual.
    """
    if len(values) == 0:
        return []
    serie = pd.Series(values, dtype="float64")
    return serie.rolling(window=window_size, min_periods=1).mean().tolist()


def detect_outliers(values, threshold=2.0):
    """
    Deteta outliers via z-score (numpy). Usa desvio padrão amostral (ddof=1).
    Devolve: (lista de bools, lista de z-scores, contagem de outliers)
    """
    n = len(values)
    if n < 2:
        return [False] * n, [0.0] * n, 0

    arr = np.asarray(values, dtype="float64")
    mean = float(np.mean(arr))
    std_dev = float(np.std(arr, ddof=1))

    if std_dev == 0:
        return [False] * n, [0.0] * n, 0

    z_scores = (arr - mean) / std_dev
    outliers = np.abs(z_scores) > threshold
    return outliers.tolist(), z_scores.tolist(), int(np.count_nonzero(outliers))


def analyze_trend(values):
    """
    Regressão linear (scikit-learn) onde o índice é a variável independente.
    Devolve: (slope, intercept, descrição_da_tendência)
    """
    n = len(values)
    if n < 2:
        return 0.0, (float(values[0]) if n else 0.0), "Stable (Not enough data)"

    x = np.arange(n).reshape(-1, 1)
    y = np.asarray(values, dtype="float64")

    model = LinearRegression()
    model.fit(x, y)
    slope = float(model.coef_[0])
    intercept = float(model.intercept_)

    if abs(slope) < 0.005:
        trend = "Stable"
    elif slope > 0:
        trend = "Increasing"
    else:
        trend = "Decreasing"

    return slope, intercept, trend


def forecast_linear(values, periods):
    """Previsão por extrapolação de uma regressão linear (scikit-learn)."""
    n = len(values)
    slope, intercept, trend = analyze_trend(values)
    future_x = np.arange(n, n + periods)
    forecast = (slope * future_x + intercept).tolist()
    return forecast, slope, trend


def forecast_ewma(values, periods, span=5):
    """
    Previsão por média móvel exponencialmente ponderada (pandas.ewm).
    O último valor EWMA é projetado para os próximos períodos (passeio plano),
    o que é típico de modelos de suavização exponencial simples.
    """
    serie = pd.Series(values, dtype="float64")
    ewma_last = float(serie.ewm(span=span, adjust=False).mean().iloc[-1])
    forecast = [ewma_last] * periods
    return forecast, ewma_last


def get_alert_level(reading_type, value):
    """Determina o nível de alerta (NORMAL/WARNING/CRITICAL) por tipo e valor."""
    t = reading_type.upper().strip() if reading_type else ""
    if t == "TEMP":
        if value > 38.0 or value < -15.0:
            return "CRITICAL"
        elif value > 32.0 or value < -5.0:
            return "WARNING"
        return "NORMAL"
    elif t == "HUM":
        if value > 95.0 or value < 10.0:
            return "WARNING"
        return "NORMAL"
    elif t in ["AR", "PM2.5", "PM10"]:
        if value > 200.0:
            return "CRITICAL"
        elif value > 75.0:
            return "WARNING"
        return "NORMAL"
    elif t == "RUIDO":
        if value > 85.0:
            return "CRITICAL"
        elif value > 65.0:
            return "WARNING"
        return "NORMAL"
    elif t == "LUZ":
        if value > 80000.0:
            return "WARNING"
        return "NORMAL"
    return "NORMAL"


def _extract_values(readings):
    """Extrai os valores numéricos das leituras recebidas no request."""
    return [float(r.value) for r in readings]


# ─────────────────────────────────────────────────────────────
# Implementação do serviço gRPC
# ─────────────────────────────────────────────────────────────

class AnalysisServiceServicer(analysis_pb2_grpc.AnalysisServiceServicer):
    def Analyze(self, request, context):
        logger.info(
            f"Analyze request: type='{request.type}', zone='{request.zone}', "
            f"sensorId='{request.sensorId}', readings={len(request.readings)}"
        )

        timestamp_now = datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ")
        values = _extract_values(request.readings)
        n = len(values)

        if n == 0:
            logger.info("Nenhuma leitura recebida no request de análise.")
            return analysis_pb2.AnalysisResult(
                resultSummary="No readings provided in the request.",
                computedAverage=0.0,
                alertLevel="NORMAL",
                timestamp=timestamp_now,
                sampleCount=0,
            )

        arr = np.asarray(values, dtype="float64")

        # 1. Estatísticas básicas (numpy)
        mean = float(np.mean(arr))
        std_dev = float(np.std(arr, ddof=1)) if n > 1 else 0.0
        v_min = float(np.min(arr))
        v_max = float(np.max(arr))
        median = float(np.median(arr))
        percentile25 = float(np.percentile(arr, 25))
        percentile75 = float(np.percentile(arr, 75))
        percentile95 = float(np.percentile(arr, 95))

        # 2. Médias móveis (pandas)
        window = 5
        moving_avgs = compute_moving_averages(values, window_size=window)
        moving_avg_last = float(moving_avgs[-1]) if moving_avgs else mean

        # 3. Outliers por z-score (numpy)
        _, _, outliers_count = detect_outliers(values)

        # 4. Tendência por regressão linear (scikit-learn)
        slope, _, trend = analyze_trend(values)

        # 5. Nível de alerta com base no último valor
        last_val = values[-1]
        reading_type = request.type or (request.readings[0].type if request.readings else "")
        alert_level = get_alert_level(reading_type, last_val)

        summary = (
            f"Analyzed {n} readings of type '{request.type or 'ALL'}'. "
            f"Mean: {mean:.2f} (median={median:.2f}, std={std_dev:.2f}, min={v_min:.2f}, max={v_max:.2f}). "
            f"Trend: {trend} (slope={slope:.4f}). "
            f"Outliers: {outliers_count} (Z-score threshold 2.0). "
            f"Last moving avg ({window} periods): {moving_avg_last:.2f}. "
            f"Last value: {last_val:.2f}."
        )

        logger.info(f"Analyze terminado: {summary}")

        return analysis_pb2.AnalysisResult(
            resultSummary=summary,
            computedAverage=mean,
            alertLevel=alert_level,
            timestamp=timestamp_now,
            mean=mean,
            stdDev=std_dev,
            min=v_min,
            max=v_max,
            outliersCount=outliers_count,
            trendSlope=slope,
            trend=trend,
            movingAverageLast=moving_avg_last,
            sampleCount=n,
            median=median,
            percentile25=percentile25,
            percentile75=percentile75,
            percentile95=percentile95,
        )

    def Predict(self, request, context):
        strategy = (request.strategy or "linear").lower().strip()
        logger.info(
            f"Predict request: type='{request.type}', zone='{request.zone}', "
            f"periods={request.periodsToPredict}, strategy='{strategy}', "
            f"readings={len(request.readings)}"
        )

        timestamp_now = datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ")
        values = _extract_values(request.readings)
        n = len(values)
        periods = request.periodsToPredict if request.periodsToPredict > 0 else 5

        if n == 0:
            logger.info("Nenhuma leitura histórica recebida para previsão.")
            return analysis_pb2.PredictionResult(
                predictionSummary="No historical readings provided to train the forecast model.",
                timestamp=timestamp_now,
                strategyUsed=strategy,
            )

        if n < 2:
            val = values[0]
            forecast = [val] * periods
            summary = (
                f"Prediction based on a single data point. "
                f"Forecast for next {periods} periods is constant: {val:.2f}."
            )
            strategy_used = strategy
        elif strategy == "ewma":
            forecast, ewma_last = forecast_ewma(values, periods)
            strategy_used = "ewma"
            forecast_str = ", ".join(f"{f:.2f}" for f in forecast)
            summary = (
                f"EWMA (span=5) forecast on {n} historical points. "
                f"Last EWMA value: {ewma_last:.2f}. "
                f"Forecast for next {periods} periods: [{forecast_str}]."
            )
        else:
            forecast, slope, trend = forecast_linear(values, periods)
            strategy_used = "linear"
            forecast_str = ", ".join(f"{f:.2f}" for f in forecast)
            summary = (
                f"Linear regression forecast on {n} historical points. "
                f"Historical trend is {trend} (slope={slope:.4f}). "
                f"Forecast for next {periods} periods: [{forecast_str}]."
            )

        logger.info(f"Predict terminado: {summary}")

        return analysis_pb2.PredictionResult(
            predictionSummary=summary,
            timestamp=timestamp_now,
            forecast=forecast,
            strategyUsed=strategy_used,
        )


def serve():
    port = "50052"
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
    analysis_pb2_grpc.add_AnalysisServiceServicer_to_server(
        AnalysisServiceServicer(), server
    )
    server.add_insecure_port(f"[::]:{port}")
    logger.info(f"A iniciar o Analysis Service gRPC na porta {port}...")
    server.start()
    logger.info("Servidor iniciado com sucesso. A aguardar pedidos...")
    server.wait_for_termination()


if __name__ == "__main__":
    serve()
