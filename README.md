# TP1 — Serviços de Monitorização Urbana para One Health

**Sistemas Distribuídos 2025/2026 · UTAD · ECT · Departamento de Engenharia**
Docentes: Hugo Paredes | Tiago Pinto | Cristiano Pendão

---

## Sobre o Projeto

Sistema distribuído desenvolvido em C# que simula uma infraestrutura de monitorização ambiental urbana no contexto do paradigma **One Health** — a ideia de que a saúde humana, animal e ambiental estão interligadas.

O sistema recolhe dados ambientais (temperatura, humidade, qualidade do ar, ruído, partículas PM2.5/PM10, luminosidade e vídeo) distribuídos por zonas urbanas, agrega-os em janelas de 15 segundos e armazena-os em SQLite para análise epidemiológica.

---

## Arquitetura

```
SENSOR ──────► GATEWAY ──────► SERVIDOR
 (recolha)     (validação +     (SQLite:
                agregação 15s)   medicoes + sensor_status)
```

| Entidade | Responsabilidade |
|----------|-----------------|
| **SENSOR** | Recolhe dados ambientais, envia heartbeat periódico (5s), suporta stream de vídeo |
| **GATEWAY** | Valida sensores (via `sensors.csv`), agrega médias em janelas de 15 s, monitoriza heartbeats, encaminha ao Servidor, tem buffer de retentativa local (volátil) |
| **SERVIDOR** | Armazena medições agregadas e estado dos sensores em SQLite (`data/urbano.db`), trata múltiplas ligações concorrentes |

Comunicação via **sockets TCP**, protocolo **textual linha-a-linha** terminado por `\n`.

Portas por defeito:
- `8080` — Sensor → Gateway (dados/controlo)
- `8081` — Sensor → Gateway (stream de vídeo)
- `9090` — Gateway → Servidor

---

## Protocolo de Comunicação (resumo)

### Sensor ↔ Gateway

| Mensagem | Direção | Descrição |
|----------|---------|-----------|
| `CONNECT <sensor_id>` | SENSOR → GW | Inicia ligação e identifica o sensor |
| `OK_CONNECTED <sensor_id>` | GW → SENSOR | Confirma ligação |
| `REGISTER_TYPES <t1,t2,...>` | SENSOR → GW | Declara tipos de dados a enviar |
| `OK_TYPES_REGISTERED` | GW → SENSOR | Tipos aceites |
| `DATA <tipo> <valor> <zona> <ts>` | SENSOR → GW | Envia medição ambiental |
| `HEARTBEAT <sensor_id>` | SENSOR → GW | Sinal de vida (atualiza `last_sync`) |
| `VIDEO_STREAM <sensor_id>` | SENSOR → GW | Inicia stream de vídeo (porta 8081) |
| `FRAME <n>` / `STREAM_END` | SENSOR → GW | Frames e fim da stream |
| `DISCONNECT <sensor_id>` | SENSOR → GW | Termina comunicação |
| `OK` / `OK_DISCONNECT` | GW → SENSOR | Operação aceite |
| `ERR_NOT_REGISTERED` | GW → SENSOR | Sensor não existe no CSV |
| `ERR_SENSOR_INACTIVE` | GW → SENSOR | Sensor em `manutencao`/`desativado` |
| `ERR_TYPE_NOT_SUPPORTED` | GW → SENSOR | Tipo não autorizado para este sensor |
| `ERR_ALREADY_CONNECTED` | GW → SENSOR | Sensor já tem sessão ativa |
| `ERR_INVALID_DATA` / `ERR_SEQUENCE` | GW → SENSOR | Formato ou sequência inválida |

### Gateway ↔ Servidor

| Mensagem | Direção | Descrição |
|----------|---------|-----------|
| `GW_CONNECT <gw_id>` | GW → SERV | Identifica o gateway |
| `OK_GW_CONNECTED <gw_id>` | SERV → GW | Confirma ligação |
| `FORWARD_AGGREGATED <tipo> <media> <zona> <ts>` | GW → SERV | Média agregada (15 s) de uma zona/tipo |
| `FORWARD <sensor_id> VIDEO <frames> <zona> <ts>` | GW → SERV | Metadados de um stream de vídeo |
| `SENSOR_STATUS <sensor_id> <estado>` | GW → SERV | Notifica mudança de estado |
| `GW_DISCONNECT <gw_id>` | GW → SERV | Desconexão ordenada |
| `OK` / `OK_STATUS_RECEIVED` / `OK_GW_DISCONNECT` | SERV → GW | Confirmações |
| `ERR_INVALID_DATA` / `ERR_STORAGE_FULL` / `ERR_SEQUENCE` | SERV → GW | Erros |

**Estados de sensor**: `ativo` | `manutencao` | `desativado` | `indisponivel` | `desligado`

---

## Estrutura do Repositório

```
TP1/
├── README.md
├── .gitignore
├── relFinalSD_revisto.docx       # (fora do controlo de versão)
│
├── docs/                         # Documentação
│   ├── Protocolo_TP1_2526.pdf    # Enunciado oficial
│   ├── TP1_Roadmap.pdf           # Roadmap/fases
│   ├── sd_rel.pdf                # Relatório do protocolo
│   └── relFinalSD_revisto.docx   # Relatório final
│
├── Sensor/                       # Cliente TCP (net8.0)
│   ├── Program.cs                # Entry point
│   ├── Sensor.cs                 # SensorClient — lógica TCP + heartbeat
│   └── SensorCLI.cs              # Interface de texto do operador
│
├── Gateway/                      # Middleware (net9.0)
│   ├── Program.cs                # Listeners (sensores + vídeo) + agregador
│   ├── DataValidator.cs          # Validação completa de DATA
│   ├── SensorConfig.cs           # Modelo de sensor
│   ├── SensorConfigManager.cs    # Leitura/escrita thread-safe do CSV
│   ├── HeartbeatGateway.cs       # Monitor de timeouts (marca indisponivel)
│   ├── RetryBuffer.cs            # Buffer FIFO em memória (backoff exponencial)
│   └── sensors.csv               # Configuração dos sensores
│
├── Servidor/                     # Armazenamento (net8.0)
│   ├── Program.cs                # Entry point
│   ├── Servidor.cs               # ServidorTCP — aceita gateways concorrentes
│   └── DataStore.cs              # SQLite (Dapper) — tabelas medicoes + sensor_status
│
└── data/                         # Runtime (SQLite)
    └── urbano.db                 # Base de dados gerada na 1ª execução
```

---

## Como Executar

### Pré-requisitos

- .NET 8.0 SDK (para Sensor/Servidor) e .NET 9.0 SDK (para Gateway)
- Pacotes NuGet: `Dapper`, `Microsoft.Data.Sqlite` (restaurados automaticamente)

### 1. Iniciar o Servidor

```bash
cd Servidor
dotnet run               # escuta na porta 9090
# Escreve 'sair' no stdin para encerrar
```

### 2. Iniciar o Gateway

```bash
cd Gateway
dotnet run <gateway_id> [server_ip] [server_port] [video_port] [sensor_port]
# Exemplo: dotnet run GW1 127.0.0.1 9090 8081 8080
```

O Gateway lê `Gateway/sensors.csv` no arranque. Sensores não listados recebem `ERR_NOT_REGISTERED`.

### 3. Iniciar um Sensor

```bash
cd Sensor
dotnet run [gateway_ip] [gateway_port]
# Exemplo: dotnet run 127.0.0.1 8080
```

A CLI pede o `sensor_id` e os tipos de dados. Após o handshake, aceita os comandos `DATA`, `HEARTBEAT`, `VIDEO`, `DISCONNECT`, `AJUDA`.

### Formato de `Gateway/sensors.csv`

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

- **Agregação temporal (15 s):** o Gateway não encaminha DATA ponto-a-ponto — calcula a média por `(tipo, zona)` e envia `FORWARD_AGGREGATED`.
- **Self-healing Gateway↔Servidor:** em falha de ligação, o Gateway arranca uma thread de reconexão com `GW_CONNECT` + ressincronização de `SENSOR_STATUS` dos sensores ativos.
- **Buffer de retentativa** (em memória, até 1000 mensagens, backoff 5→60 s). ⚠ Volátil — mensagens perdem-se em crash do Gateway.
- **Heartbeat monitor:** sensor sem atividade há mais de 15 s é marcado `indisponivel` e o Servidor é notificado. Se voltar a enviar DATA/HEARTBEAT regressa a `ativo`.
- **Mutex inter-processos** para o `sensors.csv` (`GatewayConfigMutex`) e para o `video_metadata.log` (`GatewayVideoLogMutex`).
- **SQLite em modo WAL** para concorrência nativa no Servidor (tabelas `medicoes` + `sensor_status`).

---

## Documentação Adicional

- [`docs/Protocolo_TP1_2526.pdf`](./docs/Protocolo_TP1_2526.pdf) — Enunciado oficial
- [`docs/TP1_Roadmap.pdf`](./docs/TP1_Roadmap.pdf) — Roadmap/fases
- [`docs/sd_rel.pdf`](./docs/sd_rel.pdf) — Relatório do protocolo
- [`docs/relFinalSD_revisto.docx`](./docs/relFinalSD_revisto.docx) — Relatório final
