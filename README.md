# TP2 — Serviços de Monitorização Urbana para One Health

**Sistemas Distribuídos 2025/2026 · UTAD · ECT · Departamento de Engenharia**  
Docentes: Hugo Paredes | Tiago Pinto | Cristiano Pendão

---

## Sobre o Projeto

Sistema distribuído desenvolvido em C# e Python que simula uma infraestrutura de monitorização ambiental urbana no contexto do paradigma **One Health** — a ideia de que a saúde humana, animal e ambiental estão interligadas.

O sistema recolhe dados ambientais (temperatura, humidade, qualidade do ar, ruído, partículas PM2.5/PM10, luminosidade e vídeo) distribuídos por zonas urbanas, processa-os através de uma pipeline assíncrona com RabbitMQ e microserviços gRPC, e persiste os resultados no **MongoDB** para análise epidemiológica.

---

## Arquitetura

```
SENSOR ──► RabbitMQ ──► GATEWAY ──► gRPC Preprocessing ──► SERVIDOR ──► MongoDB
            (broker)    (consumidor   (normalização)         (TCP 9090)   (urbanodb)
                         + agregação                          + gRPC
                         15 s)                               Analysis)
```

| Componente | Tecnologia | Responsabilidade |
|------------|-----------|-----------------|
| **Sensor** | C# / .NET 8 | Publica leituras e heartbeats em JSON no RabbitMQ; suporta streaming de vídeo via TCP direto |
| **RabbitMQ** | Docker (rabbitmq:3-management) | Topic Exchange `sensors.exchange`; filas duráveis por Gateway com bindings de zona |
| **Gateway** | C# / .NET 9 | Consome mensagens do RabbitMQ com ACK/NACK manual, chama gRPC Preprocessing (Polly), agrega médias em janelas de 15 s e encaminha ao Servidor via TCP |
| **Preprocessing** | Python / Docker | Microserviço gRPC (porta 50051): normaliza unidades, valida ranges, faz parsing JSON/XML/CSV |
| **Servidor** | C# / .NET 8 | Aceita ligações TCP dos Gateways, persiste leituras no **MongoDB** (principal) e SQLite (fallback), expõe CLI com Spectre.Console |
| **Analysis** | Python / Docker | Microserviço gRPC puro (porta 50052): estatísticas (média, desvio, outliers Z-score), tendência (regressão linear) e previsão (linear/EWMA) |
| **MongoDB** | Docker (mongo:7) | Base de dados principal — coleções `readings`, `analyses`, `sensors_metadata` |
| **SQLite** | Ficheiro local | Fallback automático quando o MongoDB está indisponível |
| **Mongo Express** | Docker (mongo-express:1.0.2) | Interface Web de gestão do MongoDB (porta 8081) |

---

## Fluxo de Dados

1. O **Sensor** publica uma mensagem JSON no topic exchange `sensors.exchange` com a routing key `<zona>.<tipo>.<sensorId>`.
2. O **RabbitMQ** encaminha a mensagem para a fila durável do Gateway correspondente (ex: `gateway.GW1`) com base nos bindings configurados (ex: `ZONA_CENTRO.#`).
3. O **Gateway** consome a mensagem com confirmação manual (ACK/NACK):
   - Chama o **Preprocessing gRPC** para normalizar e validar a leitura (Polly: 3 retries com backoff exponencial). Em falha → NACK com requeue.
   - Acumula as leituras válidas numa fila interna e calcula médias por `(tipo, zona)` a cada 15 segundos.
   - Envia `FORWARD_AGGREGATED <tipo> <média> <zona> <timestamp>` ao Servidor via TCP (porta 9090).
4. O **Servidor** recebe o agregado, persiste no **MongoDB** (coleção `readings`) e atualiza os metadados do sensor (coleção `sensors_metadata`). Se o MongoDB estiver indisponível, persiste no SQLite como fallback.
5. A **CLI do Servidor** (Spectre.Console) permite executar análises e previsões: lê as leituras do MongoDB e delega o cálculo ao **Analysis gRPC**.

---

## Estrutura do Repositório

```
TP1/
├── README.md
├── .env.example               # Variáveis de ambiente (copiar para .env)
├── docker-compose.yml         # Orquestra RabbitMQ, MongoDB, Mongo Express e serviços RPC
│
├── docs/                      # Documentação
│   ├── Estado_Atual_TP2.md    # Estado e arquitetura do TP2
│   ├── roadmaptp2.md          # Roadmap detalhado do TP2
│   ├── Protocolo_TP1_2526.pdf # Enunciado TP1
│   └── Protocolo_TP2_2526_v2.pdf # Enunciado TP2
│
├── Sensor/                    # Publicador RabbitMQ (net8.0)
│   ├── Program.cs             # Entry point — modos automático e CLI
│   ├── Sensor.cs              # SensorClient — publicação RabbitMQ + heartbeat
│   ├── SensorCLI.cs           # Interface interativa do operador
│   └── appsettings.json       # Configuração de RabbitMQ e sensor
│
├── Gateway/                   # Middleware (net9.0)
│   ├── Program.cs             # Consumidor RabbitMQ + gRPC + agregador 15 s
│   ├── DataValidator.cs       # Validação completa de leituras
│   ├── SensorConfigManager.cs # Leitura/escrita thread-safe do sensors.csv
│   ├── HeartbeatGateway.cs    # Monitor de timeouts
│   ├── RetryBuffer.cs         # Buffer FIFO (backoff exponencial) para GW→Servidor
│   ├── sensors.csv            # Configuração dos sensores
│   └── appsettings.json       # Configuração de RabbitMQ, Servidor e Preprocessing
│
├── Servidor/                  # Armazenamento central (net8.0)
│   ├── Program.cs             # Entry point
│   ├── Servidor.cs            # ServidorTCP — aceita Gateways, persiste no MongoDB
│   ├── CliHandler.cs          # CLI Spectre.Console (analisar, prever, leituras, ...)
│   ├── DataStore.cs           # SQLite (Dapper) — fallback quando MongoDB indisponível
│   ├── Mongo/                 # Camada MongoDB
│   │   ├── MongoDbContext.cs  # Contexto e ligação ao MongoDB
│   │   └── Repositories/     # ReadingsRepository, AnalysesRepository, SensorsMetadataRepository
│   └── appsettings.json       # URL do Analysis gRPC e connection string do MongoDB
│
├── services/
│   ├── preprocessing/         # Microserviço gRPC Python (porta 50051)
│   └── analysis/              # Microserviço gRPC Python (porta 50052)
│
├── proto/                     # Definições Protobuf partilhadas
│   ├── preprocessing.proto
│   └── analysis.proto
│
├── mongo/
│   └── init/                  # Scripts de inicialização do MongoDB
│       └── 01-init-urbanodb.js
│
├── rabbitmq/
│   ├── rabbitmq.conf          # Configuração do RabbitMQ
│   └── definitions.json       # Definições de vhost, utilizadores e exchanges
│
└── data/                      # SQLite (fallback)
    └── urbano.db              # Gerado automaticamente se o MongoDB não estiver disponível
```

---

## Pré-requisitos

- **Docker** e **Docker Compose v2+** (para RabbitMQ, MongoDB, Mongo Express e serviços Python)
- **.NET 8.0 SDK** (para Sensor e Servidor)
- **.NET 9.0 SDK** (para Gateway)

> Os pacotes NuGet e Python são restaurados automaticamente.

---

## Como Executar (passo a passo)

### 1. Preparar variáveis de ambiente

```powershell
# Na raiz do projeto
cp .env.example .env
# Editar .env se necessário (credenciais do MongoDB, RabbitMQ, etc.)
```

### 2. Arrancar os serviços Docker

```powershell
# Na raiz do projeto
docker compose up -d --build
```

Confirmar que todos os serviços estão saudáveis:

```powershell
docker compose ps
```

Serviços esperados:

| Serviço | Porta | Descrição |
|---------|-------|-----------|
| `rabbitmq` | 5672 / 15672 | Broker AMQP + consola de gestão Web |
| `preprocessing` | 50051 | Microserviço gRPC de normalização |
| `analysis` | 50052 | Microserviço gRPC de análise estatística |
| `mongodb` | 27017 | Base de dados principal |
| `mongo-express` | 8081 | Interface Web do MongoDB |

> A consola do RabbitMQ fica em http://localhost:15672 (credenciais: `admin` / `admin`).  
> O Mongo Express fica em http://localhost:8081 (credenciais: `admin` / `admin`).

### 3. Iniciar o Servidor

Num novo terminal:

```powershell
cd Servidor
dotnet run
```

O Servidor inicia à escuta na porta TCP `9090` e liga-se ao MongoDB (`urbanodb`). A CLI Spectre.Console fica disponível no mesmo terminal.

### 4. Iniciar um Gateway

Num novo terminal:

```powershell
cd Gateway
dotnet run [gateway_id] [server_ip] [server_port] [video_port] [binding_pattern]
```

**Exemplos:**

```powershell
# Gateway GW1 — subscreve toda a ZONA_CENTRO
dotnet run GW1 127.0.0.1 9090 8081 "ZONA_CENTRO.#"

# Gateway GW2 — subscreve ZONA_ESCOLAR
dotnet run GWB 127.0.0.1 9090 8082 "ZONA_ESCOLAR.#"

# Múltiplos padrões separados por vírgula
dotnet run GW1 127.0.0.1 9090 8081 "ZONA_CENTRO.#,ZONA_PARQUE.#"
```

**Argumentos do Gateway:**

| Posição | Argumento | Padrão | Descrição |
|---------|-----------|--------|-----------|
| 1 | `gateway_id` | `GW1` (appsettings) | Identificador único do Gateway |
| 2 | `server_ip` | `127.0.0.1` (appsettings) | IP do Servidor Central |
| 3 | `server_port` | `9090` (appsettings) | Porta TCP do Servidor |
| 4 | `video_port` | `8081` (appsettings) | Porta TCP para streams de vídeo |
| 5 | `binding_pattern` | zonas do sensors.csv | Padrão(ões) de routing key RabbitMQ, separados por vírgula |

> Se não forem passados argumentos, o Gateway lê as configurações do `appsettings.json`.  
> Os bindings também podem ser configurados na secção `Bindings` do `appsettings.json`.

### 5. Iniciar um Sensor

Num novo terminal:

```powershell
cd Sensor
dotnet run [--auto <sensor_id> <zona> <tipo> <intervalo_s>] [--cli]
```

**Modos de execução do Sensor:**

| Modo | Comando | Descrição |
|------|---------|-----------|
| Automático (appsettings) | `dotnet run` | Lê toda a configuração do `appsettings.json` |
| Automático (linha de comando) | `dotnet run --auto S101 ZONA_CENTRO TEMP 5` | Publica dados de forma automática com os parâmetros indicados |
| CLI interativo | `dotnet run --cli` | Interface interativa de texto para controlo manual |

**Exemplos:**

```powershell
# Sensor automático — TEMP na ZONA_CENTRO, a cada 5 segundos
dotnet run --auto S101 ZONA_CENTRO TEMP 5

# Sensor automático — HUM na ZONA_ESCOLAR, a cada 3 segundos
dotnet run --auto S102 ZONA_ESCOLAR HUM 3

# Sensor em modo interativo
dotnet run --cli
```

---

## Comandos da CLI do Servidor

A CLI usa **Spectre.Console** e está disponível no terminal onde o Servidor está a correr.

| Comando | Sintaxe | Descrição |
|---------|---------|-----------|
| `analisar` | `analisar <tipo> <zona> <sensor\|-> <from\|-> <to\|->` | Lê do MongoDB e executa análise estatística via gRPC (interativo se sem argumentos) |
| `prever` | `prever <tipo> <zona> <periodos> [linear\|ewma]` | Lê do MongoDB e projeta períodos futuros via gRPC (interativo se sem argumentos) |
| `historico` | `historico` | Lista análises e previsões realizadas na sessão atual (em memória) |
| `leituras` | `leituras sensor=ID zona=Z tipo=TEMP from=ISO to=ISO limit=50` | Lista leituras persistidas no MongoDB |
| `analises` | `analises tipo=TEMP zona=ZONA_CENTRO limit=50` | Lista análises persistidas no MongoDB |
| `sensor` | `sensor <sensorId>` | Mostra metadados e últimas 10 leituras do sensor no MongoDB |
| `ajuda` | `ajuda` | Mostra a tabela de comandos disponíveis |
| `sair` | `sair` | Encerra o Servidor de forma segura |

**Exemplos de uso:**

```
# Análise em linha (não interativa)
analisar TEMP ZONA_CENTRO S101 2026-05-20T00:00:00 2026-05-20T23:59:59

# Análise interativa (solicita os campos um a um)
analisar

# Previsão — próximos 5 períodos com EWMA
prever TEMP ZONA_CENTRO 5 ewma

# Listar últimas 20 leituras de temperatura
leituras tipo=TEMP limit=20

# Ver metadados do sensor S101
sensor S101
```

> Os comandos `analisar` e `prever` leem exclusivamente do **MongoDB** (coleção `readings`). O serviço de Análise gRPC é puro: recebe os dados no próprio pedido e não acede à base de dados.

---

## Formato de `Gateway/sensors.csv`

```
# sensor_id:estado:zona:[tipos_dados]:last_sync
S101:ativo:ZONA_CENTRO:[TEMP,HUM,RUIDO]:2026-04-17T16:24:03
S102:desligado:ZONA_ESCOLAR:[TEMP,HUM]:2026-04-17T11:30:21
S103:manutencao:ZONA_INDUSTRIAL:[RUIDO]:2026-03-09T14:30:00
```

Zonas válidas: `ZONA_CENTRO`, `ZONA_ESCOLAR`, `ZONA_INDUSTRIAL`, `ZONA_RESIDENCIAL`, `ZONA_PARQUE`.  
Tipos válidos: `TEMP`, `HUM`, `AR`, `RUIDO`, `PM2.5`, `PM10`, `LUZ`, `VIDEO`.

---

## Funcionalidades-chave

- **RabbitMQ Pub/Sub:** Sensores publicam em Topic Exchange com routing keys estruturadas (`<zona>.<tipo>.<sensorId>`). Gateways consomem filas duráveis com ACK/NACK manual.
- **Normalização gRPC (Polly):** O Gateway chama o Preprocessing Service com 3 retries exponenciais. Em falha → NACK com requeue; a mensagem permanece segura no RabbitMQ.
- **Agregação temporal (15 s):** O Gateway não encaminha leituras ponto-a-ponto — calcula a média por `(tipo, zona)` e envia `FORWARD_AGGREGATED`.
- **Persistência dupla MongoDB + SQLite:** O Servidor persiste no MongoDB como destino principal. Se o MongoDB estiver indisponível, ativa automaticamente o SQLite como fallback.
- **Coleções MongoDB:** `readings` (leituras agregadas), `analyses` (resultados de análises gRPC), `sensors_metadata` (metadados e última leitura por sensor).
- **CLI Spectre.Console:** Interface de administração rica com tabelas, painéis e formatação colorida.
- **Self-healing Gateway↔Servidor:** Em falha TCP, o Gateway reconecta automaticamente e ressincroniza o estado dos sensores.
- **Buffer de retentativa:** Até 1000 mensagens em memória com backoff exponencial (5 → 60 s). ⚠ Volátil — mensagens perdem-se em crash do Gateway.
- **Heartbeat monitor:** Sensor sem atividade há mais de 15 s é marcado `indisponivel` e o Servidor é notificado. Volta a `ativo` quando retoma comunicação.
- **Mutex inter-processos** (`GatewayConfigMutex`, `GatewayVideoLogMutex`) para acesso seguro a ficheiros partilhados entre instâncias de Gateway.

---

## Serviços gRPC

| Serviço | Porta | Linguagem | Responsabilidade |
|---------|-------|-----------|-----------------|
| **preprocessing** | `50051` | Python | Normaliza leituras: parsing JSON/XML/CSV, conversão F/K→C, validação de ranges |
| **analysis** | `50052` | Python | Estatísticas (média, desvio padrão, outliers Z-score, percentis), tendência (regressão linear) e previsão (linear/EWMA) |

### Regenerar os stubs gRPC

Os stubs **C#** são gerados automaticamente no `dotnet build`.  
Os stubs **Python** são regenerados com:

```bash
bash proto/build.sh
```

### Correr os testes Python (pytest)

```powershell
pip install -r services/preprocessing/requirements.txt
pip install -r services/analysis/requirements.txt
pytest services/
```

---

## Comandos Docker úteis

```powershell
# Ver estado dos serviços
docker compose ps

# Ver logs de um serviço
docker compose logs -f analysis

# Parar um serviço (teste de resiliência)
docker compose stop preprocessing

# Reiniciar um serviço
docker compose start preprocessing

# Parar tudo
docker compose down

# Parar tudo e apagar volumes (reset completo da BD)
docker compose down -v
```

---

## Documentação Adicional

- [`docs/Estado_Atual_TP2.md`](./docs/Estado_Atual_TP2.md) — Estado e arquitetura do TP2
- [`docs/roadmaptp2.md`](./docs/roadmaptp2.md) — Roadmap detalhado do TP2
- [`docs/Protocolo_TP2_2526_v2.pdf`](./docs/Protocolo_TP2_2526_v2.pdf) — Enunciado oficial TP2
- [`docs/Protocolo_TP1_2526.pdf`](./docs/Protocolo_TP1_2526.pdf) — Enunciado oficial TP1
