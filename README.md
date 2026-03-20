# TP1 — Serviços de Monitorização Urbana para One Health

**Sistemas Distribuídos 2025/2026 · UTAD · ECT · Departamento de Engenharia**
Docentes: Hugo Paredes | Tiago Pinto | Cristiano Pendão

---

## Sobre o Projeto

Sistema distribuído desenvolvido em C# que simula uma infraestrutura de monitorização ambiental urbana no contexto do paradigma **One Health** — a ideia de que a saúde humana, animal e ambiental estão interligadas.

O sistema recolhe dados ambientais (temperatura, humidade, qualidade do ar, ruído, partículas PM2.5/PM10, luminosidade e vídeo) distribuídos por zonas urbanas, agrega-os e disponibiliza-os para análise epidemiológica.

---

## Arquitetura

```
SENSOR ──────► GATEWAY ──────► SERVIDOR
 (recolha)     (validação/      (armazenamento/
                agregação)       processamento)
```

| Entidade | Responsabilidade |
|----------|-----------------|
| **SENSOR** | Recolhe dados ambientais, envia heartbeat periódico, suporta stream de vídeo |
| **GATEWAY** | Valida sensores (via CSV), agrega dados, monitoriza heartbeats, encaminha para o Servidor |
| **SERVIDOR** | Armazena dados por tipo de dado, trata múltiplas ligações concorrentes |

Comunicação via **sockets TCP**, protocolo **textual linha-a-linha**.

---

## Protocolo de Comunicação (resumo)

| Mensagem | Direção | Descrição |
|----------|---------|-----------|
| `CONNECT <sensor_id>` | SENSOR → GW | Inicia ligação e identifica o sensor |
| `REGISTER_TYPES <tipos>` | SENSOR → GW | Declara tipos de dados recolhidos |
| `DATA <tipo> <valor> <zona> <ts>` | SENSOR → GW | Envia medição ambiental |
| `HEARTBEAT <sensor_id>` | SENSOR → GW | Sinal de vida periódico |
| `VIDEO_STREAM <sensor_id>` | SENSOR → GW | Solicita stream de vídeo |
| `DISCONNECT <sensor_id>` | SENSOR → GW | Termina comunicação |
| `FORWARD <sensor_id> <tipo> <valor> <zona> <ts>` | GW → SERV | Encaminha medição válida |
| `OK` | GW/SERV → remetente | Operação aceite |
| `ERR_NOT_REGISTERED` | GW → SENSOR | Sensor não registado |
| `ERR_SENSOR_INACTIVE` | GW → SENSOR | Sensor em manutenção/desativado |
| `ERR_TYPE_NOT_SUPPORTED` | GW → SENSOR | Tipo de dado não suportado |

---

## Estrutura do Repositório

```
TP1_OneHealth/
├── Sensor/
│   ├── Program.cs           # Entry point — recebe IP do Gateway como argumento
│   ├── Sensor.cs            # Lógica de comunicação TCP
│   └── SensorCLI.cs         # Interface de texto com o utilizador
│
├── Gateway/
│   ├── Program.cs
│   ├── Gateway.cs           # Servidor para Sensores + cliente para Servidor
│   ├── SensorRegistry.cs    # Leitura/escrita do ficheiro CSV de sensores
│   └── HeartbeatMonitor.cs  # Monitorização de timeouts de heartbeat
│
├── Servidor/
│   ├── Program.cs
│   ├── Servidor.cs          # Servidor TCP para Gateways (threads + locks)
│   └── DataStore.cs         # Armazenamento em ficheiros por tipo de dado
│
├── config/
│   └── sensors.csv          # Configuração dos sensores do Gateway
│
├── data/                    # Ficheiros de output do Servidor (gerados em runtime)
│   ├── TEMP.csv
│   ├── HUM.csv
│   └── ...
│
├── Protocolo_TP1_2526.pdf   # Enunciado original
├── TP1_Roadmap.docx         # Roadmap do grupo com fases e checklists
├── TP1_Guia_IA.md           # Guia de contexto para uso com IA
└── README.md
```

---

## Como Executar

### Pré-requisitos
- .NET 8.0 SDK ou superior
- (Opcional) SQLite para funcionalidade extra

### 1. Iniciar o Servidor
```bash
cd Servidor
dotnet run
# Aguarda ligações na porta 9090
```

### 2. Iniciar o Gateway
```bash
cd Gateway
dotnet run
# Aguarda ligações de Sensores na porta 8080
# Liga ao Servidor em localhost:9090
```

### 3. Iniciar um Sensor
```bash
cd Sensor
dotnet run <IP_DO_GATEWAY>
# Exemplo: dotnet run 127.0.0.1
```

### Ficheiro de configuração do Gateway (`config/sensors.csv`)
```
sensor_id:estado:zona:[tipos_dados]:last_sync
S101:ativo:ZONA_CENTRO:[TEMP,HUM,RUIDO]:2026-03-10T08:45:00
S102:ativo:ZONA_ESCOLAR:[PM2.5,TEMP]:2026-03-10T09:00:00
S103:manutencao:ZONA_INDUSTRIAL:[AR,PM10]:2026-03-09T18:30:00
```

Estados possíveis: `ativo` | `manutencao` | `desativado`

---

## Fases de Desenvolvimento

| Fase | Semana | Descrição |
|------|--------|-----------|
| 1 | 16–20 Mar | Desenho e teste do protocolo de comunicação |
| 2 | 23–27 Mar | Implementação básica SENSOR / GATEWAY / SERVIDOR |
| 3 | 7–10 Abr | Gateway com CSV + heartbeat + armazenamento no Servidor |
| 4 | 13–17 Abr | Concorrência com threads e mutexes + relatório |

**Entrega:** 17 de Abril de 2026 via Moodle
**Apresentação:** Aula PL seguinte à data de entrega

---

## Funcionalidade Extra

A possibilidade de armazenar dados numa **base de dados relacional** (SQLite) em substituição dos ficheiros CSV no Servidor é considerada funcionalidade extra com pontuação adicional.

```sql
CREATE TABLE Medicoes (
    id        INTEGER PRIMARY KEY AUTOINCREMENT,
    sensor_id TEXT NOT NULL,
    zona      TEXT NOT NULL,
    tipo_dado TEXT NOT NULL,
    valor     REAL NOT NULL,
    timestamp TEXT NOT NULL
);
```

---

## Equipa

| Elemento | Responsabilidade principal |
|----------|---------------------------|
| Elemento A | Servidor (TCP + armazenamento + threads) |
| Elemento B | Gateway (CSV + validação + heartbeat + threads) |
| Elemento C | Sensor (interface CLI + heartbeat + vídeo) |

---

## Documentação Adicional

- [`TP1_Roadmap.docx`](./TP1_Roadmap.docx) — Roadmap detalhado com tarefas, checklists e calendário
- [`TP1_Guia_IA.md`](./TP1_Guia_IA.md) — Contexto e prompts prontos para uso com IA durante o desenvolvimento
- [`Protocolo_TP1_2526.pdf`](./Protocolo_TP1_2526.pdf) — Enunciado oficial do trabalho
