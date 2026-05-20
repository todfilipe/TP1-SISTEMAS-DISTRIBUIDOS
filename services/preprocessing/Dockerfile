FROM python:3.12-slim

WORKDIR /app

# Copy requirements and install dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy the rest of the application files (including stubs and server script)
COPY . .

# Expose gRPC port
EXPOSE 50051

# Start the gRPC server
CMD ["python", "server.py"]
