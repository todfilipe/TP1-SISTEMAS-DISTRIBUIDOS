import os
import sys
import json
import csv
import xml.etree.ElementTree as ET
import io
import logging
from concurrent import futures

import grpc

# Add current directory to path to ensure local stub imports work
sys.path.append(os.path.dirname(os.path.abspath(__file__)))

import preprocessing_pb2
import preprocessing_pb2_grpc

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    handlers=[
        logging.StreamHandler(sys.stdout)
    ]
)
logger = logging.getLogger("preprocessing_service")


def parse_raw_format(raw_format_str):
    """
    Parses raw_format_str which could be JSON, XML or CSV.
    Returns a dict with extracted fields, or empty dict if parsing fails.
    """
    if not raw_format_str:
        return {}
    
    s = raw_format_str.strip()
    if not s:
        return {}
    
    # Try parsing as JSON
    if s.startswith('{'):
        try:
            data = json.loads(s)
            logger.info("Successfully parsed JSON payload from rawFormat")
            return {
                'sensorId': data.get('sensorId') or data.get('sensor_id'),
                'type': data.get('type'),
                'value': data.get('value'),
                'unit': data.get('unit'),
                'timestamp': data.get('timestamp'),
                'zone': data.get('zone') or data.get('zona')
            }
        except Exception as e:
            logger.debug(f"JSON parsing failed: {e}")
            
    # Try parsing as XML
    if s.startswith('<'):
        try:
            root = ET.fromstring(s)
            res = {}
            for child in root:
                res[child.tag] = child.text
            
            # Normalize keys to match properties
            mapped = {
                'sensorId': res.get('sensorId') or res.get('sensor_id'),
                'type': res.get('type'),
                'value': res.get('value'),
                'unit': res.get('unit'),
                'timestamp': res.get('timestamp'),
                'zone': res.get('zone') or res.get('zona')
            }
            # Try to convert value to float
            if mapped['value'] is not None:
                try:
                    mapped['value'] = float(mapped['value'])
                except ValueError:
                    pass
            logger.info("Successfully parsed XML payload from rawFormat")
            return mapped
        except Exception as e:
            logger.debug(f"XML parsing failed: {e}")
            
    # Try parsing as CSV (comma-separated value list)
    if ',' in s:
        try:
            reader = csv.reader(io.StringIO(s))
            row = next(reader)
            if row:
                mapped = {}
                # Match by length of columns:
                # 5+ elements: sensorId, type, value, unit, timestamp
                if len(row) >= 5:
                    mapped['sensorId'] = row[0]
                    mapped['type'] = row[1]
                    try:
                        mapped['value'] = float(row[2])
                    except ValueError:
                        pass
                    mapped['unit'] = row[3]
                    mapped['timestamp'] = row[4]
                # 3 elements: type, value, unit
                elif len(row) == 3:
                    mapped['type'] = row[0]
                    try:
                        mapped['value'] = float(row[1])
                    except ValueError:
                        pass
                    mapped['unit'] = row[2]
                # 2 elements: value, unit
                elif len(row) == 2:
                    try:
                        mapped['value'] = float(row[0])
                    except ValueError:
                        pass
                    mapped['unit'] = row[1]
                
                logger.info(f"Successfully parsed CSV payload ({len(row)} cols) from rawFormat")
                return mapped
        except Exception as e:
            logger.debug(f"CSV parsing failed: {e}")
            
    return {}


class PreprocessingServiceServicer(preprocessing_pb2_grpc.PreprocessingServiceServicer):
    def Normalize(self, request, context):
        logger.info(
            f"Received RawReading: sensorId='{request.sensorId}', type='{request.type}', "
            f"value={request.value}, unit='{request.unit}', timestamp='{request.timestamp}', "
            f"rawFormatLength={len(request.rawFormat) if request.rawFormat else 0}"
        )
        
        sensor_id = request.sensorId
        reading_type = request.type
        value = request.value
        unit = request.unit
        timestamp = request.timestamp
        raw_format = request.rawFormat
        zone = request.zone

        # 1. Parse rawFormat if provided to override/extract empty or default fields
        try:
            parsed = parse_raw_format(raw_format)
            if parsed:
                if parsed.get('sensorId'):
                    sensor_id = str(parsed['sensorId'])
                if parsed.get('type'):
                    reading_type = str(parsed['type'])
                if parsed.get('value') is not None:
                    value = float(parsed['value'])
                if parsed.get('unit'):
                    unit = str(parsed['unit'])
                if parsed.get('timestamp'):
                    timestamp = str(parsed['timestamp'])
                if parsed.get('zone'):
                    zone = str(parsed['zone'])
        except Exception as e:
            logger.error(f"Error overriding fields from parsed rawFormat: {e}")

        # 2. Conversão de escalas (Fahrenheit -> Celsius, Kelvin -> Celsius)
        unit_upper = unit.upper().strip() if unit else ""
        type_upper = reading_type.upper().strip() if reading_type else ""
        
        original_value = value
        original_unit = unit
        
        if type_upper == "TEMP":
            if unit_upper in ["F", "FAHRENHEIT"]:
                value = (value - 32.0) * 5.0 / 9.0
                unit = "C"
                logger.info(f"Converted temperature scale: {original_value} {original_unit} -> {value:.2f} C")
            elif unit_upper in ["K", "KELVIN"]:
                value = value - 273.15
                unit = "C"
                logger.info(f"Converted temperature scale: {original_value} {original_unit} -> {value:.2f} C")

        # 3. Validação de ranges
        is_valid = True
        reason = ""
        
        if type_upper == "TEMP":
            # Temperatura em Celsius: -50.0 a 60.0
            if not (-50.0 <= value <= 60.0):
                is_valid = False
                reason = f"Temperature {value:.2f} C out of range [-50, 60]"
        elif type_upper == "HUM":
            # Humidade em percentagem: 0.0 a 100.0
            if not (0.0 <= value <= 100.0):
                is_valid = False
                reason = f"Humidity {value:.2f}% out of range [0, 100]"
        elif type_upper in ["AR", "PM2.5", "PM10"]:
            # Partículas / Qualidade do Ar: 0.0 a 1000.0
            if not (0.0 <= value <= 1000.0):
                is_valid = False
                reason = f"Air index/particles {value:.2f} out of range [0, 1000]"
        elif type_upper == "RUIDO":
            # Ruído em dB: 0.0 a 150.0
            if not (0.0 <= value <= 150.0):
                is_valid = False
                reason = f"Noise {value:.2f} dB out of range [0, 150]"
        elif type_upper == "LUZ":
            # Luminosidade em Lux: 0.0 a 100000.0
            if not (0.0 <= value <= 100000.0):
                is_valid = False
                reason = f"Light {value:.2f} lux out of range [0, 100000]"
        else:
            # Qualquer outro tipo (ex: VIDEO framecount ou genérico): valor não pode ser negativo
            if value < 0:
                is_valid = False
                reason = f"Value {value:.2f} cannot be negative for type '{reading_type}'"

        if not is_valid:
            logger.warning(f"Reading validation failed: {reason}")
        else:
            logger.info(f"Reading validated successfully: value={value:.2f} {unit}")

        # Build response
        response = preprocessing_pb2.NormalizedReading(
            sensorId=sensor_id,
            type=reading_type,
            value=value,
            unit=unit,
            timestamp=timestamp,
            rawFormat=raw_format,
            isValid=is_valid,
            zone=zone
        )
        return response


def serve():
    port = "50051"
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=10))
    preprocessing_pb2_grpc.add_PreprocessingServiceServicer_to_server(
        PreprocessingServiceServicer(), server
    )
    server.add_insecure_port(f"[::]:{port}")
    logger.info(f"Starting Preprocessing Service gRPC server on port {port}...")
    server.start()
    logger.info("Server started successfully. Awaiting requests...")
    server.wait_for_termination()


if __name__ == "__main__":
    serve()
