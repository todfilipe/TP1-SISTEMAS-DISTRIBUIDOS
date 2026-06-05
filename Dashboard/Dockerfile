FROM node:20-alpine

WORKDIR /app/Dashboard

COPY Dashboard/package*.json ./
RUN npm install --omit=dev

COPY Dashboard/server.js ./
COPY Dashboard/public ./public
COPY Shared/protocol.js ../Shared/protocol.js
COPY proto/analysis.proto ../proto/analysis.proto
COPY Gateway/sensors.csv ../Gateway/sensors.csv

EXPOSE 3000

CMD ["npm", "start"]
