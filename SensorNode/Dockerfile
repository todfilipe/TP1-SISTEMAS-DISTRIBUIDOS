FROM node:20-alpine

WORKDIR /app/SensorNode

COPY SensorNode/package.json ./
RUN npm install --omit=dev

COPY SensorNode ./
COPY Shared/protocol.js ../Shared/protocol.js

CMD ["npm", "start"]
