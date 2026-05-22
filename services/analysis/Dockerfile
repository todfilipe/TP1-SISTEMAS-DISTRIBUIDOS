FROM python:3.12-slim

WORKDIR /app

# Copy requirements and install dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy the rest of the application files (including stubs and server script)
COPY . .

# Expose gRPC port
EXPOSE 50052

# Healthcheck: confirma que a porta gRPC 50052 está aberta (teste TCP leve).
# Permite que o compose use depends_on: condition: service_healthy.
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 \
    CMD python -c "import socket,sys; s=socket.socket(); s.settimeout(2); sys.exit(s.connect_ex(('localhost',50052)))"

# Start the gRPC server
CMD ["python", "server.py"]
