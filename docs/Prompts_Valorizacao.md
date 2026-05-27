# Prompts de Valorização — TP2 Sistemas Distribuídos

> Cada prompt é auto-suficiente e pode ser colado diretamente no Claude Code.  
> Stack real do projeto: .NET 8/9 (C#) · Python 3.12 · RabbitMQ · MongoDB · gRPC · Docker Compose

---

## Prompt 1 — Dashboard Web Própria

```
Tens acesso ao projeto de Sistemas Distribuídos TP2 (One Health). A arquitetura é a seguinte:

  Sensor (C# .NET 8) → RabbitMQ (topic exchange "sensors.exchange") → Gateway (C# .NET 9)
  → gRPC Preprocessing (Python, porta 50051) → Servidor TCP (C# .NET 8, porta 9090)
  → MongoDB (porta 27017, database "urbanodb") + gRPC Analysis (Python, porta 50052)

O MongoDB tem as coleções:
- readings: { sensorId, zone, type, value, unit, timestamp, gatewayId, originalMessageFormat, createdAt }
- analyses: { sensorId, zone, type, windowStart, windowEnd, average, standardDeviation, median, outlierCount, trendClassification, createdAt, rawGrpcResultSerialized }
- sensors_metadata: metadados dos sensores com último timestamp observado

O projeto não tem qualquer dashboard web. Quero que cries uma aplicação web standalone em Node.js
(Express + Chart.js via CDN) que fique numa nova pasta /Dashboard/ na raiz do projeto.

A app deve ter as seguintes rotas e comportamentos:
- GET /readings: página HTML com tabela de leituras, filtros por sensor, zona, tipo e intervalo de datas;
  inclui um gráfico Chart.js de série temporal com os valores filtrados.
- GET /analyses: página HTML com histórico de análises — mostra zona, tipo, janela temporal, média, mediana, desvio, tendência.
- GET /new-analysis: formulário HTML para disparar uma análise via POST; campos: tipo (TEMP/HUM/AR/RUIDO/PM2.5/PM10/LUZ), zona (ZONA_CENTRO/ZONA_ESCOLAR/ZONA_INDUSTRIAL/ZONA_RESIDENCIAL/ZONA_PARQUE), sensor (opcional), dateFrom, dateTo, estratégia (linear/ewma); ao submeter, o servidor Node.js deve consultar o MongoDB para obter as leituras e devolver os resultados em JSON (não precisa de invocar o gRPC — só mostrar os dados que já existem no MongoDB).
- GET /status: JSON com contagem de leituras totais, última leitura por zona, contagem de análises.

Configuração de ligação ao MongoDB via variáveis de ambiente com fallback para os valores do docker-compose:
- MONGODB_URI (default: mongodb://admin:admin@localhost:27017/urbanodb?authSource=admin)

Adiciona o serviço "dashboard" ao docker-compose.yml existente na raiz do projeto, com porta 3000
exposta e variável MONGODB_URI apontando para o serviço mongodb da rede sd-net.

O CSS deve ser inline (sem ficheiros externos). Usa Bootstrap 5 via CDN para o layout.
Cria também um Dockerfile para o serviço dentro da pasta /Dashboard/.

Não uses mongoose — usa o driver oficial do MongoDB (mongodb npm package).
```

---

## Prompt 2 — Sensor Extra em Node.js

```
Tens acesso ao projeto de Sistemas Distribuídos TP2 (One Health).

O projeto já tem um Sensor em C# (.NET 8) que publica no RabbitMQ. Quero que cries um Sensor
equivalente em Node.js usando o pacote amqplib, que fique numa nova pasta /SensorNode/ na raiz.

Contexto técnico preciso do RabbitMQ existente:
- Exchange: "sensors.exchange", tipo topic, durable: true
- Virtual host: "onehealth"
- Routing key: "<zone>.<type>.<sensorId>" (ex: "ZONA_CENTRO.TEMP.SNJ01")
- Credenciais default: user=admin, password=admin, porta 5672
- Formato do payload JSON que o Gateway espera (deserializado via JsonSerializer.Deserialize<SensorMessage>):
  {
    "sensorId": "SNJ01",
    "zone": "ZONA_CENTRO",
    "type": "TEMP",
    "value": 22.5,
    "unit": "C",
    "timestamp": "2026-05-27T10:00:00",
    "raw": "JSON",
    "rawFormat": "JSON"
  }
- O campo rawFormat pode ser "JSON", "XML" ou "CSV"
- Tipos válidos: TEMP, HUM, AR, RUIDO, PM2.5, PM10, LUZ
- Zonas válidas: ZONA_CENTRO, ZONA_ESCOLAR, ZONA_INDUSTRIAL, ZONA_RESIDENCIAL, ZONA_PARQUE

Comportamento esperado do SensorNode:
1. Lê configuração de um ficheiro config.json com os campos: sensorId, zone, type, intervalSeconds,
   rabbitHost, rabbitPort, rabbitUser, rabbitPass, rabbitVHost, payloadFormat (JSON/XML/CSV).
   Defaults: sensorId=SNJ01, zone=ZONA_CENTRO, type=TEMP, intervalSeconds=5, payloadFormat=JSON.
2. Liga ao RabbitMQ e declara o exchange (idempotente).
3. Publica leituras com valores aleatórios plausíveis por tipo:
   - TEMP: entre 15 e 40 °C
   - HUM: entre 30 e 90 %
   - AR/PM2.5/PM10: entre 10 e 150 µg/m³
   - RUIDO: entre 30 e 90 dB
   - LUZ: entre 100 e 100000 lux
4. A cada `intervalSeconds`, publica uma leitura DATA.
5. A cada 5 publicações, publica também um HEARTBEAT (payload: { sensorId, zone, type: "HEARTBEAT", value: 0, unit: "", timestamp, raw: "", rawFormat: "JSON" }).
6. Aceita Ctrl+C e publica um DISCONNECT antes de terminar (payload: { ..., type: "DISCONNECT", value: 0 }).
7. Reconexão automática ao RabbitMQ em caso de falha de ligação (backoff 5s).

Cria também um Dockerfile que use node:20-alpine.
Adiciona o serviço "sensor-node" ao docker-compose.yml existente, dependente do serviço rabbitmq,
com variáveis de ambiente para sobrescrever a configuração.

O package.json deve ter um script "start" que execute o sensor.
```

---

## Prompt 3 — Teste de Carga com 100 Sensores

```
Tens acesso ao projeto de Sistemas Distribuídos TP2 (One Health).

Quero um script de teste de carga que simule 100 sensores virtuais a publicar mensagens
concorrentemente no RabbitMQ. Cria o script em Python (usando pika) na pasta /scripts/load_test.py.

Contexto do RabbitMQ:
- Exchange: "sensors.exchange", tipo topic, durable: true, virtual host: "onehealth"
- Credenciais: admin/admin, porta 5672
- Routing key por mensagem: "<zone>.<type>.<sensorId>"
- Payload JSON: { "sensorId": "<id>", "zone": "<zone>", "type": "<type>", "value": <float>,
  "unit": "<unit>", "timestamp": "<ISO8601>", "raw": "JSON", "rawFormat": "JSON" }
- Zonas disponíveis: ZONA_CENTRO, ZONA_ESCOLAR, ZONA_INDUSTRIAL, ZONA_RESIDENCIAL, ZONA_PARQUE
- Tipos disponíveis: TEMP, HUM, AR, RUIDO, LUZ

Comportamento do script:
1. Cria 100 sensores virtuais com IDs "LOAD_S001" a "LOAD_S100", distribuídos pelas 5 zonas
   (20 por zona) e pelos 5 tipos (variando uniformemente).
2. Cada sensor publica num thread separado (usa threading.Thread ou concurrent.futures.ThreadPoolExecutor).
3. Cada sensor publica N mensagens (default: 20, configurável via argumento --messages).
4. Intervalo entre publicações de cada sensor: 0.1s (configurável via --interval).
5. Usa uma única ligação pika partilhada com channel por thread (ou conexões separadas por thread
   — escolhe a abordagem thread-safe correta do pika).
6. Mede e imprime no final:
   - Total de mensagens enviadas
   - Total de mensagens com erro
   - Duração total em segundos
   - Throughput: mensagens/segundo
   - Distribuição por zona e tipo
7. Aceita argumentos de linha de comando: --host, --port, --user, --password, --vhost,
   --sensors (default 100), --messages (default 20), --interval (default 0.1).
8. Imprime progresso a cada 10% das mensagens totais.

Cria também um requirements.txt específico para /scripts/ com apenas "pika".
O script deve funcionar em standalone (sem Docker) e também dentro da rede Docker (com --host rabbitmq).
```

---

## Prompt 4 — Teste de Integração Ponta-a-Ponta

```
Tens acesso ao projeto de Sistemas Distribuídos TP2 (One Health).

A arquitetura completa é:
  Sensor (C# .NET 8) → RabbitMQ → Gateway (C# .NET 9) → gRPC Preprocessing (Python, :50051)
  → Servidor TCP (C# .NET 8, porta 9090) → MongoDB (porta 27017) + gRPC Analysis (Python, :50052)

Quero um script de teste de integração ponta-a-ponta em Python que fique em /scripts/integration_test.py.
O script deve verificar automaticamente que o pipeline completo funciona, sem depender de compilação
.NET (pressupõe que os binários e os contentores já existem).

O script deve executar os seguintes passos em sequência, com verificação explícita de cada um:

PASSO 1 — Verificar pré-requisitos
- Confirmar que o Docker está a correr e que os contentores rabbitmq, preprocessing, analysis,
  mongodb estão healthy (usa o comando "docker inspect --format='{{.State.Health.Status}}'").
- Se algum não estiver healthy, imprimir instrução e abortar.

PASSO 2 — Publicar mensagens de teste via RabbitMQ (pika)
- Publicar 5 leituras de teste no exchange "sensors.exchange" com sensorId "TEST_SENSOR_INT",
  zona "ZONA_CENTRO", tipo "TEMP", valores [20.0, 21.0, 22.0, 23.0, 24.0], formato JSON.
- Guardar o timestamp de publicação.
- Confirmar que as 5 mensagens foram aceites pelo broker (basic_publish com mandatory=False).

PASSO 3 — Verificar serviço gRPC Preprocessing
- Ligar diretamente ao serviço gRPC preprocessing em localhost:50051 (usando grpcio e os stubs
  gerados em proto/preprocessing_pb2.py e proto/preprocessing_pb2_grpc.py).
- Enviar um RawReading de teste e confirmar que devolve NormalizedReading com isValid=True.

PASSO 4 — Verificar serviço gRPC Analysis
- Ligar diretamente ao serviço gRPC analysis em localhost:50052 (usando proto/analysis_pb2.py
  e proto/analysis_pb2_grpc.py).
- Enviar um AnalysisRequest com as 5 leituras de teste e confirmar que devolve AnalysisResult
  com sampleCount=5, mean próximo de 22.0, e campos median/percentile25/percentile75 preenchidos.

PASSO 5 — Aguardar persistência no MongoDB
- Aguardar até 30s que as leituras do TEST_SENSOR_INT apareçam na coleção "readings" do MongoDB
  (usa pymongo: mongodb://admin:admin@localhost:27017/urbanodb?authSource=admin).
- Verificar que existem pelo menos 1 documento com sensorId="TEST_SENSOR_INT" criado após o
  timestamp de publicação do PASSO 2.
- NOTA: este passo requer que o Gateway e o Servidor estejam a correr externamente (não são
  iniciados pelo script); se não encontrar documentos em 30s, reportar como AVISO (não falha).

PASSO 6 — Relatório final
- Imprimir relatório com ✅/❌/⚠️ por passo.
- Código de saída: 0 se todos os passos críticos (1-4) passaram, 1 caso contrário.

Dependências: pika, grpcio, pymongo. Usa sys.path para importar os stubs de ../proto/.
```

---

## Prompt 5 — Percentis Completos no MongoDB (AnalysisDocument)

```
Tens acesso ao projeto de Sistemas Distribuídos TP2 (One Health).

O serviço gRPC Analysis (services/analysis/server.py) já calcula e devolve no AnalysisResult
os seguintes campos adicionais além dos básicos:
  median, percentile25, percentile75, percentile95, min, max, trendSlope, sampleCount, movingAverageLast

O proto em proto/analysis.proto já define todos estes campos no AnalysisResult.

No entanto, o modelo MongoDB em Servidor/Mongo/Models/AnalysisDocument.cs só persiste:
  Average, StandardDeviation, Median, OutlierCount, TrendClassification
— faltam: Percentile25, Percentile75, Percentile95, Min, Max, TrendSlope, SampleCount, MovingAverageLast.

Além disso, o campo AlertLevel (string "NORMAL"/"WARNING"/"CRITICAL") devolvido pelo serviço
de análise também não é persistido.

Quero que faças as seguintes alterações, sem quebrar nada existente:

1. Servidor/Mongo/Models/AnalysisDocument.cs
   Adiciona os campos BsonElement em falta: Percentile25, Percentile75, Percentile95, Min, Max,
   TrendSlope, SampleCount, MovingAverageLast, AlertLevel (todos com tipo e anotações BsonElement
   consistentes com os campos já existentes).

2. Servidor/Servidor.cs — método PersistirAnaliseMongoAsync(...)
   Localiza a construção do objeto AnalysisDocument (já existente no método) e preenche
   os campos recém-adicionados a partir do objeto AnalysisResult devolvido pelo gRPC
   (os nomes dos campos no proto C# são: Percentile25, Percentile75, Percentile95, Min, Max,
   TrendSlope, SampleCount, MovingAverageLast, AlertLevel).

3. Servidor/CliHandler.cs — comando "analises"
   Localiza o método ShowPersistedAnalyses e garante que os novos campos são mostrados
   na tabela Spectre.Console: acrescenta linhas para P25, P75, P95 e AlertLevel.
   (Min, Max, TrendSlope, SampleCount podem ficar opcionais — adiciona apenas se não sobrecarregar o output.)

Não altera o analysis.proto nem o server.py — o serviço já está correto.
Não altera a lógica de negócio do Servidor — apenas o modelo de persistência e o display CLI.
Verifica que o projeto Servidor compila sem erros após as alterações (dotnet build Servidor/).
```

---

## Prompt 6 — Garantia Real de Persistência MongoDB

```
Tens acesso ao projeto de Sistemas Distribuídos TP2 (One Health).

Problema atual em Servidor/Servidor.cs:
O método ProcessarForward (e ProcessarForwardAggregated) funciona assim:
  1. Chama _dataStore.ArmazenarMedicao(...) — persiste em SQLite.
  2. Se SQLite OK → chama PersistirLeituraMongoAsync(...).GetAwaiter().GetResult() — mas se o
     MongoDB falhar (exceção ou timeout), o método retorna false silenciosamente.
  3. Em ambos os casos (MongoDB OK ou falho), o Servidor responde "OK" ao Gateway.

Resultado: o Gateway recebe "OK" mesmo que o MongoDB esteja em baixo. O Gateway não tem forma de
saber que a leitura não foi persistida no store principal. Para nota máxima, o MongoDB deve ser
o store primário e uma falha deve gerar "ERR_STORAGE_FULL" ao Gateway, permitindo que o Gateway
coloque a mensagem no RetryBuffer.

Quero que alteres Servidor/Servidor.cs da seguinte forma:

1. Nos métodos ProcessarForward e ProcessarForwardAggregated, após o SQLite devolver Sucesso,
   invoca PersistirLeituraMongoAsync e verifica o resultado (bool):
   - Se PersistirLeituraMongoAsync devolver true → retorna "OK" (comportamento atual).
   - Se PersistirLeituraMongoAsync devolver false E _readingsRepository != null (ou seja, o MongoDB
     foi configurado mas falhou) → retorna "ERR_STORAGE_FULL".
   - Se _readingsRepository == null (MongoDB não configurado) → retorna "OK" como fallback
     gracioso (mantém retrocompatibilidade para quem corra sem Docker).

2. Adiciona um log de aviso claro quando o MongoDB falha e é devolvido ERR_STORAGE_FULL:
   "[Servidor][ALERTA] MongoDB indisponível — ERR_STORAGE_FULL enviado ao Gateway {gatewayId}.
    Leitura guardada no SQLite como fallback."

3. Não alteras a lógica do DataStore.cs (SQLite) — ele continua como fallback de escrita local.

4. Não alteras o Gateway — ele já trata ERR_STORAGE_FULL corretamente: o RetryBuffer em
   Gateway/RetryBuffer.cs enfileira a mensagem para retentativa automática.

5. Verifica que o projeto Servidor compila sem erros após as alterações (dotnet build Servidor/).

Nota: PersistirLeituraMongoAsync já existe em Servidor.cs e já devolve bool (true/false).
O _readingsRepository é inicializado no construtor via InicializarMongo() e pode ser null se
o MongoDB não estiver acessível no arranque.
```

---

## Prompt 7 — Documentação Totalmente Alinhada com TP2

```
Tens acesso ao projeto de Sistemas Distribuídos TP2 (One Health).

A arquitetura real e atual do projeto é:

  Sensor (C# .NET 8) → RabbitMQ (topic exchange "sensors.exchange", vhost "onehealth")
  → Gateway (C# .NET 9, consome RabbitMQ com ACK/NACK manual, agrega janelas 15s via ConcurrentQueue,
    chama gRPC Preprocessing com Polly 3 retries backoff exponencial)
  → gRPC Preprocessing (Python, porta 50051, Normalize: RawReading→NormalizedReading)
  → Servidor TCP (C# .NET 8, porta 9090, protocolo: GW_CONNECT/FORWARD/FORWARD_AGGREGATED/SENSOR_STATUS/GW_DISCONNECT)
  → MongoDB (porta 27017, db "urbanodb", coleções: readings/analyses/sensors_metadata) [primário]
    + SQLite (fallback automático quando MongoDB indisponível)
  → gRPC Analysis (Python, porta 50052, Analyze+Predict com numpy/pandas/sklearn)

CLI do Servidor (Spectre.Console): analisar, prever, historico, leituras, analises, sensor, ajuda, sair
docker-compose.yml: rabbitmq, preprocessing, analysis, mongodb, mongo-express (porta 8081), [dashboard porta 3000]

Problemas existentes na documentação:
- README.md ainda menciona SQLite como store principal; trechos herdados do TP1.
- docs/Estado_Atual_TP2.md pode estar desatualizado.
- O fluxo de dados documentado não menciona FORWARD_AGGREGATED (agregação a cada 15s).

Quero que faças as seguintes atualizações:

1. README.md — atualiza completamente para refletir a arquitetura TP2 real:
   - Diagrama ASCII do fluxo completo (incluindo FORWARD_AGGREGATED e SQLite como fallback).
   - Tabela de componentes com tecnologia e responsabilidade (como já existe, mas corrigida).
   - Secção "Como arrancar" com os passos exatos:
     a) docker compose up -d  (sobe rabbitmq, preprocessing, analysis, mongodb, mongo-express)
     b) dotnet run --project Servidor/  (porta 9090)
     c) dotnet run --project Gateway/ [GW1]  (consome RabbitMQ, agrega, encaminha)
     d) dotnet run --project Sensor/  (publica no RabbitMQ)
   - Secção "Comandos CLI do Servidor" com todos os comandos e sintaxe.
   - Secção "Variáveis de Ambiente" para sobrescrever configurações (MONGODB_URI,
     ANALYSIS_SERVICE_URL, PREPROCESSING_SERVICE_URL).
   - Remove todas as referências a SQLite como store principal e ao protocolo direto
     Sensor→Gateway (esse fluxo TCP foi substituído pelo RabbitMQ no TP2).

2. docs/Estado_Atual_TP2.md — atualiza o estado de cada fase:
   - Fase 1 (gRPC): ✅ Concluída — preprocessing (50051) e analysis (50052) operacionais.
   - Fase 2 (RabbitMQ): ✅ Concluída — Sensor publica, Gateway consome com ACK/NACK.
   - Fase 3 (MongoDB): ✅ Concluída — readings/analyses/sensors_metadata persistidos.
   - Fase 3.5 (Dashboard): [estado atual] — indica o que foi implementado.
   - Valorização: Node.js Sensor, load test, testes de integração — indica estado.

Mantém o estilo e tom existentes nos documentos. Não crias ficheiros novos — só atualiza os existentes.
```

---

## Prompt 8 — Demo Preparada e Repetível

```
Tens acesso ao projeto de Sistemas Distribuídos TP2 (One Health).

Quero um script de demo repetível para a apresentação/defesa. Cria dois ficheiros:

A) scripts/demo.sh (Linux/Mac) e scripts/demo.bat (Windows)
   Script de arranque da infraestrutura que executa:
   1. docker compose down --volumes 2>/dev/null (limpa estado anterior)
   2. docker compose up -d (sobe rabbitmq, preprocessing, analysis, mongodb, mongo-express)
   3. Aguarda até todos os serviços estarem healthy (polling via "docker inspect", máx 60s)
   4. Imprime confirmação com URLs:
      - RabbitMQ Management: http://localhost:15672 (admin/admin)
      - Mongo Express: http://localhost:8081 (admin/admin)
      - Dashboard: http://localhost:3000 (se existir)
   5. Instrução para o operador: "Agora abre 3 terminais e executa:"
      Terminal 1: dotnet run --project Servidor/
      Terminal 2: dotnet run --project Gateway/ -- GW1
      Terminal 3: dotnet run --project Sensor/

B) scripts/demo_roteiro.md
   Roteiro de defesa em formato de checklist — o apresentador segue passo a passo:

   ## Pré-Demo (5 min antes)
   - [ ] Executar scripts/demo.sh (ou demo.bat)
   - [ ] Verificar RabbitMQ Management em localhost:15672
   - [ ] Verificar Mongo Express em localhost:8081

   ## Demo ao vivo (seguir esta ordem)
   1. [ ] Mostrar arquitetura: explicar o diagrama do README.md
   2. [ ] Terminal 1 — Arrancar Servidor (mostrar banner UrbanoDB, porta 9090)
   3. [ ] Terminal 2 — Arrancar Gateway GW1 (mostrar ligação ao Servidor + binding RabbitMQ)
   4. [ ] Terminal 3 — Arrancar Sensor S101 JSON (mostrar publicação no RabbitMQ)
   5. [ ] RabbitMQ Management — mostrar fila gateway.GW1 a receber mensagens
   6. [ ] Aguardar ~20s — Gateway agrega e envia FORWARD_AGGREGATED ao Servidor
   7. [ ] Servidor CLI — executar: leituras TEMP ZONA_CENTRO
   8. [ ] Servidor CLI — executar: analisar TEMP ZONA_CENTRO
   9. [ ] Mongo Express — mostrar coleção readings com documentos persistidos
   10. [ ] Servidor CLI — executar: analises (mostrar análise persistida com mediana/percentis)
   11. [ ] Terminal extra — Arrancar 2º Sensor S102 XML (mostrar multi-sensor)
   12. [ ] Simular falha do Preprocessing: docker stop preprocessing
       - [ ] Mostrar NACK com requeue no Gateway (mensagens ficam em fila)
       - [ ] docker start preprocessing — mostrar recuperação automática
   13. [ ] [Se implementado] Dashboard — mostrar http://localhost:3000/readings com gráfico
   14. [ ] [Se implementado] Sensor Node.js — mostrar 3ª stack: node SensorNode/sensor.js

   ## Perguntas prováveis e respostas curtas
   - "Por que usaram RabbitMQ topic e não fanout?" → routing key <zona>.<tipo>.<sensor>
     permite bindings seletivos por Gateway — cada GW só recebe a sua zona.
   - "O que acontece se o MongoDB cair?" → SQLite como fallback; Gateway recebe ERR_STORAGE_FULL
     e coloca no RetryBuffer com backoff exponencial.
   - "Como garantem que não perdem mensagens?" → ACK/NACK manual no RabbitMQ + RetryBuffer
     no Gateway + filas duráveis que sobrevivem a reinícios.
   - "O gRPC é síncrono ou assíncrono?" → chamada síncrona bloqueante com Polly (3 retries
     backoff exponencial 1/2/4s + timeout 5s no Gateway, 10s no Servidor).

O script demo.sh deve ser executável (chmod +x) e ter set -e no topo para parar em erro.
O demo.bat deve usar TIMEOUT /T para pausas e verificar ERRORLEVEL.
Ambos devem ter comentários a explicar cada passo.
```
