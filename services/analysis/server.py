import os
import sys
import sqlite3
import math
import logging
from datetime import datetime
from concurrent import futures

import grpc

# Add current directory to path to ensure local stub imports work
sys.path.append(os.path.dirname(os.path.abspath(__file__)))

import analysis_pb2
import analysis_pb2_grpc

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    handlers=[
        logging.StreamHandler(sys.stdout)
    ]
)
logger = logging.getLogger("analysis_service")


def get_db_path():
    """
    Determines the best path for the SQLite database.
    """
    db_env = os.environ.get("DB_PATH")
    if db_env:
        return db_env
        
    # Search in common paths
    possible_paths = [
        # local host path relative to services/analysis
        os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", "data", "urbano.db")),
        # docker mount path
        "/app/data/urbano.db",
        # fallback
        "data/urbano.db"
    ]
    
    for path in possible_paths:
        if os.path.exists(path):
            logger.info(f"Found database at path: {path}")
            return path
            
    # Default fallback
    logger.info(f"Using default database path: {possible_paths[0]}")
    return possible_paths[0]


def query_medicoes(db_path, reading_type=None, zone=None, sensor_id=None, date_from=None, date_to=None):
    """
    Queries medicoes from the SQLite database with filters.
    """
    if not os.path.exists(db_path):
        logger.warning(f"Database file not found at {db_path}")
        return []
        
    try:
        conn = sqlite3.connect(db_path)
        cursor = conn.cursor()
        
        # Check if table exists
        cursor.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='medicoes';")
        if not cursor.fetchone():
            logger.warning("Table 'medicoes' does not exist in the database.")
            conn.close()
            return []
            
        query = "SELECT valor, timestamp, sensor_id, zona, tipo_dado FROM medicoes WHERE 1=1"
        params = []
        
        if reading_type and reading_type.strip():
            query += " AND UPPER(tipo_dado) = ?"
            params.append(reading_type.upper().strip())
        if zone and zone.strip():
            query += " AND UPPER(zona) = ?"
            params.append(zone.upper().strip())
        if sensor_id and sensor_id.strip():
            query += " AND UPPER(sensor_id) = ?"
            params.append(sensor_id.upper().strip())
        if date_from and date_from.strip():
            query += " AND timestamp >= ?"
            params.append(date_from.strip())
        if date_to and date_to.strip():
            query += " AND timestamp <= ?"
            params.append(date_to.strip())
            
        query += " ORDER BY timestamp ASC"
        
        cursor.execute(query, params)
        rows = cursor.fetchall()
        conn.close()
        
        return [{"value": r[0], "timestamp": r[1], "sensor_id": r[2], "zona": r[3], "type": r[4]} for r in rows]
    except Exception as e:
        logger.error(f"Error querying SQLite: {e}")
        return []


def compute_moving_averages(values, window_size=5):
    """
    Computes moving averages for a list of numeric values.
    """
    n = len(values)
    if n == 0:
        return []
    if n < window_size:
        avg = sum(values) / n
        return [avg] * n
        
    moving_avgs = []
    for i in range(n):
        if i < window_size - 1:
            # Partial window
            subset = values[:i+1]
        else:
            subset = values[i - window_size + 1 : i + 1]
        moving_avgs.append(sum(subset) / len(subset))
    return moving_avgs


def detect_outliers(values, threshold=2.0):
    """
    Detects outliers in values using the standard Z-score.
    Returns: (list of bools indicating if outlier, list of z-scores, outliers_count)
    """
    n = len(values)
    if n < 2:
        return [False] * n, [0.0] * n, 0
        
    mean = sum(values) / n
    variance = sum((x - mean) ** 2 for x in values) / (n - 1)
    std_dev = math.sqrt(variance)
    
    if std_dev == 0:
        return [False] * n, [0.0] * n, 0
        
    outliers = []
    z_scores = []
    outliers_count = 0
    
    for x in values:
        z = (x - mean) / std_dev
        z_scores.append(z)
        is_out = abs(z) > threshold
        outliers.append(is_out)
        if is_out:
            outliers_count += 1
            
    return outliers, z_scores, outliers_count


def analyze_trend(values):
    """
    Performs a simple linear regression trend analysis where index is the independent variable.
    Returns: (slope, intercept, trend_description)
    """
    n = len(values)
    if n < 2:
        return 0.0, 0.0, "Stable (Not enough data)"
        
    x_mean = (n - 1) / 2.0
    y_mean = sum(values) / n
    
    num = 0.0
    den = 0.0
    for i, y in enumerate(values):
        num += (i - x_mean) * (y - y_mean)
        den += (i - x_mean) ** 2
        
    if den == 0:
        return 0.0, y_mean, "Stable (No variance)"
        
    slope = num / den
    intercept = y_mean - slope * x_mean
    
    # Classify slope
    if abs(slope) < 0.005:
        trend = "Stable"
    elif slope > 0:
        trend = "Increasing"
    else:
        trend = "Decreasing"
        
    return slope, intercept, trend


def get_alert_level(reading_type, value):
    """
    Determines alert level (NORMAL, WARNING, CRITICAL) based on reading type and value.
    """
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


class AnalysisServiceServicer(analysis_pb2_grpc.AnalysisServiceServicer):
    def Analyze(self, request, context):
        logger.info(
            f"Analyze request received: type='{request.type}', zone='{request.zone}', "
            f"sensorId='{request.sensorId}', dateFrom='{request.dateFrom}', dateTo='{request.dateTo}'"
        )
        
        db_path = get_db_path()
        data = query_medicoes(
            db_path,
            reading_type=request.type,
            zone=request.zone,
            sensor_id=request.sensorId,
            date_from=request.dateFrom,
            date_to=request.dateTo
        )
        
        timestamp_now = datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ")
        
        if not data:
            logger.info("No data found for the analysis request.")
            return analysis_pb2.AnalysisResult(
                resultSummary="No data found matching criteria in SQLite database.",
                computedAverage=0.0,
                alertLevel="NORMAL",
                timestamp=timestamp_now
            )
            
        values = [d["value"] for d in data]
        n = len(values)
        
        # 1. Compute Average
        avg = sum(values) / n
        
        # 2. Moving Averages
        window = 5
        moving_avgs = compute_moving_averages(values, window_size=window)
        moving_avg_str = f"Last moving avg ({window} periods): {moving_avgs[-1]:.2f}" if moving_avgs else "N/A"
        
        # 3. Z-score Outliers
        _, _, outliers_count = detect_outliers(values)
        
        # 4. Trend Analysis
        slope, _, trend = analyze_trend(values)
        
        # 5. Alert Level
        # Check last value for alert status
        last_val = values[-1]
        alert_level = get_alert_level(request.type or data[0]["type"], last_val)
        
        summary = (
            f"Analyzed {n} readings of type '{request.type or 'ALL'}'. "
            f"Mean: {avg:.2f}. "
            f"Trend: {trend} (slope={slope:.4f}). "
            f"Outliers: {outliers_count} detected (Z-score threshold 2.0). "
            f"{moving_avg_str}. "
            f"Last value: {last_val:.2f}."
        )
        
        logger.info(f"Analyze finished. Summary: {summary}")
        
        return analysis_pb2.AnalysisResult(
            resultSummary=summary,
            computedAverage=avg,
            alertLevel=alert_level,
            timestamp=timestamp_now
        )

    def Predict(self, request, context):
        logger.info(
            f"Predict request received: type='{request.type}', zone='{request.zone}', "
            f"periodsToPredict={request.periodsToPredict}"
        )
        
        db_path = get_db_path()
        data = query_medicoes(
            db_path,
            reading_type=request.type,
            zone=request.zone
        )
        
        timestamp_now = datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ")
        
        if not data:
            logger.info("No data found for predicting.")
            return analysis_pb2.PredictionResult(
                predictionSummary="No historical data found in SQLite database to train the forecast model.",
                timestamp=timestamp_now
            )
            
        values = [d["value"] for d in data]
        n = len(values)
        
        periods = request.periodsToPredict if request.periodsToPredict > 0 else 5
        
        if n < 2:
            logger.warning("Not enough historical data points to fit regression line.")
            val = values[0] if values else 0.0
            forecast = [val] * periods
            summary = (
                f"Prediction based on single data point. "
                f"Forecast for next {periods} periods is constant: {forecast}. "
                f"Cannot compute trend."
            )
        else:
            slope, intercept, trend = analyze_trend(values)
            forecast = []
            for step in range(periods):
                pred_x = n + step
                pred_y = slope * pred_x + intercept
                forecast.append(pred_y)
                
            forecast_str = ", ".join([f"{f:.2f}" for f in forecast])
            summary = (
                f"Trained linear regression model on {n} historical data points. "
                f"Historical trend is {trend} (slope={slope:.4f}). "
                f"Forecasted values for the next {periods} periods: [{forecast_str}]."
            )
            
        logger.info(f"Predict finished. Summary: {summary}")
        
        return analysis_pb2.PredictionResult(
            predictionSummary=summary,
            timestamp=timestamp_now
        )


def serve():
    port = "50052"
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
    analysis_pb2_grpc.add_AnalysisServiceServicer_to_server(
        AnalysisServiceServicer(), server
    )
    server.add_insecure_port(f"[::]:{port}")
    logger.info(f"Starting Analysis Service gRPC server on port {port}...")
    server.start()
    logger.info("Server started successfully. Awaiting requests...")
    server.wait_for_termination()


if __name__ == "__main__":
    serve()
