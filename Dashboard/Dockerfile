FROM node:20-alpine

WORKDIR /app/Dashboard

COPY Dashboard/package*.json ./
RUN npm install --omit=dev

COPY Dashboard/server.js ./
COPY Dashboard/public ./public
COPY Shared/protocol.js ../Shared/protocol.js

EXPOSE 3000

CMD ["npm", "start"]
