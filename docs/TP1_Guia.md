# Guia de Contexto para IA — TP1 Sistemas Distribuídos
## Serviços de Monitorização Urbana para One Health · UTAD 2025/2026

---

## CONTEXTO GERAL DO PROJETO

```
Estou a desenvolver um Trabalho Prático (TP1) para a UC de Sistemas Distribuídos
da Licenciatura em Engenharia Informática na UTAD (2025/2026).

O projeto chama-se "Serviços de Monitorização Urbana para One Health".

OBJETIVO: Implementar um sistema distribuído em C# usando sockets TCP que simula
uma infraestrutura de monitorização ambiental urbana.

ARQUITETURA — 3 entidades:
  - SENSOR: recolhe dados ambientais e envia para o Gateway
  - GATEWAY: valida, agrega e encaminha dados dos Sensores para o(s) Servidor(es)
  - SERVIDOR: armazena e processa dados dos Gateways (pode haver mais do que um,
    cada um responsável por determinados tipos de dados)

LINGUAGEM: C# com sockets TCP (TcpListener / TcpClient)
PROTOCOLO: textual, linha-a-linha (mensagens terminam em \n), codificação UTF-8
EQUIPA: 3 elementos

PRAZO DE ENTREGA: 17 de Abril de 2026 (Moodle + apresentação na aula PL)

IDENTIFICAÇÃO:
  - gateway_id: parâmetro de arranque do processo (ex: GW01)
  - sensor_id: pré-configurado no firmware, registado no CSV do gateway
```

---

## FASE 1 — Desenho do Protocolo (semana 16–20 Mar) ✅ CONCLUÍDA

### Contexto para IA

```
A Fase 1 está concluída. O protocolo foi definido, documentado e testado
manualmente a 3 (simulação). O documento de especificação é o sd_rel_v2.docx.

RESUMO DO PROTOCOLO DEFINIDO:

SENSOR → GATEWAY:
  CONNECT <sensor_id>                          → OK_CONNECTED <sensor_id>
  REGISTER_TYPES <t1,t2,...>                   → OK_TYPES_REGISTERED
  DATA <tipo> <valor> <zona> <timestamp>       → OK / ERR_*
  HEARTBEAT <sensor_id>                        → OK
  VIDEO_STREAM <sensor_id>                     → OK_VIDEO_STARTED / ERR_VIDEO_UNAVAILABLE
  DISCONNECT <sensor_id>                       → OK_DISCONNECT

GATEWAY → SERVIDOR:
  GW_CONNECT <gateway_id>                      → OK_GW_CONNECTED <gw_id>
  FORWARD <sensor_id> <tipo> <valor> <zona> <ts> → OK / ERR_INVALID_DATA / ERR_STORAGE_FULL
  SENSOR_STATUS <sensor_id> <estado>           → OK_STATUS_RECEIVED
  GW_DISCONNECT <gateway_id>                   → OK_GW_DISCONNECT

ERROS ESPECÍFICOS (GW → SENSOR):
  ERR_NOT_REGISTERED    — sensor_id não existe no CSV
  ERR_SENSOR_INACTIVE   — sensor em manutenção ou desativado
  ERR_TYPE_NOT_SUPPORTED — tipo de dado não consta nos tipos do sensor
  ERR_INVALID_DATA      — mensagem malformada ou dados inválidos
  ERR_VIDEO_UNAVAILABLE — sensor não suporta VIDEO
  ERR_SEQUENCE          — mensagem fora da sequência esperada

MECANISMOS DEFINIDOS:
  - Heartbeat: intervalo 5s, timeout 15s → estado "indisponivel"
  - Timeout de handshake: 10s para CONNECT + REGISTER_TYPES
  - Buffer local GW: até 1000 medições quando ligação ao Servidor falha
  - Retentativa GW→Servidor: a cada 5s com backoff exponencial (máx 60s)
  - 5 estados do sensor: ativo, manutencao, desativado, indisponivel, desligado

REGRAS DE VALIDAÇÃO:
  - Timestamp ISO 8601, máx 60s no futuro
  - Zona deve ser uma das 5 definidas
  - Valores negativos só aceites para TEMP
  - IDs alfanuméricos não vazios (S101, GW01)

VIDEO_STREAM: handshake definido; stream real em porta separada (8081).
  Processamento edge no Gateway será detalhado na Fase 3.
```

---

## FASE 2 — Implementação Básica em C# (semana 23–27 Mar)

### Contexto para IA — SERVIDOR básico

```
Estou a implementar o SERVIDOR do TP1 de Sistemas Distribuídos em C#.

O SERVIDOR deve:
1. Abrir um socket TCP (TcpListener na porta 9090) e aguardar ligação de Gateways
2. Receber GW_CONNECT <gateway_id> e responder OK_GW_CONNECTED <gw_id>
3. Receber mensagens de texto linha-a-linha (protocolo textual v2)
4. Processar: FORWARD (armazenar dados), SENSOR_STATUS (registar estado)
5. Responder com OK, OK_STATUS_RECEIVED, ERR_INVALID_DATA ou ERR_STORAGE_FULL
6. Receber GW_DISCONNECT <gateway_id> e responder OK_GW_DISCONNECT
7. (Fase 4) Tratar múltiplas ligações concorrentes com threads

PROTOCOLO DEFINIDO (mensagens que o servidor recebe do gateway):
  GW_CONNECT <gateway_id>
  FORWARD <sensor_id> <tipo_dado> <valor> <zona> <timestamp>
  SENSOR_STATUS <sensor_id> <estado>
  GW_DISCONNECT <gateway_id>

RESPOSTAS DO SERVIDOR:
  OK_GW_CONNECTED <gw_id>
  OK
  OK_STATUS_RECEIVED
  OK_GW_DISCONNECT
  ERR_INVALID_DATA
  ERR_STORAGE_FULL

NOTA: O servidor pode processar um ou vários tipos de dados ambientais.
No cenário avançado (para 20/20), o Gateway pode encaminhar dados para
diferentes servidores conforme o tipo de dado, usando uma configuração
de routing (ex: Servidor A → TEMP,HUM; Servidor B → PM2.5,RUIDO).
```

### Contexto para IA — GATEWAY básico

```
Estou a implementar o GATEWAY do TP1 de Sistemas Distribuídos em C#.

O GATEWAY recebe o gateway_id como parâmetro de arranque (ex: GW01).

O GATEWAY deve:
1. Ligar-se ao Servidor via TcpClient (porta 9090) e enviar GW_CONNECT <gw_id>
2. Aguardar OK_GW_CONNECTED antes de aceitar sensores
3. Aceitar ligação de SENSOR(es) via TcpListener (porta 8080)
4. Receber CONNECT <sensor_id> — verificar registo no CSV
5. Receber REGISTER_TYPES — validar contra tipos no CSV
6. Aplicar timeout de handshake: 10s para CONNECT + REGISTER_TYPES
7. Validar estado do sensor e tipos de dados suportados
8. Atualizar last_sync após receber dados ou heartbeat
9. Monitorizar heartbeats (timeout 15s → marcar indisponivel)
10. Encaminhar dados válidos para o Servidor via FORWARD
11. Reportar alterações de estado via SENSOR_STATUS
12. (Fase 3) Implementar buffer local quando ligação ao Servidor falha
13. (Fase 4) Usar threads para múltiplos sensores simultâneos

FICHEIRO CSV DOS SENSORES (formato):
  sensor_id:estado:zona:[tipos_dados]:last_sync
  Ex: S101:ativo:ZONA_CENTRO:[TEMP,HUM,RUIDO]:2026-03-10T08:45:00

ESTADOS DO SENSOR:
  ativo        — funcionamento normal, dados aceites
  manutencao   — temporariamente indisponível, dados rejeitados (ERR_SENSOR_INACTIVE)
  desativado   — removido/desligado, dados rejeitados (ERR_SENSOR_INACTIVE)
  indisponivel — atribuído pelo GW quando heartbeat expira (timeout 15s)
  desligado    — sensor desconectou-se via DISCONNECT, pode reconectar

PROTOCOLO — mensagens recebidas do SENSOR:
  CONNECT <sensor_id>              → OK_CONNECTED <sensor_id> / ERR_NOT_REGISTERED
  REGISTER_TYPES <tipo1,tipo2,...> → OK_TYPES_REGISTERED / ERR_TYPE_NOT_SUPPORTED
  DATA <tipo_dado> <valor> <zona> <timestamp> → OK / ERR_*
  HEARTBEAT <sensor_id>            → OK
  VIDEO_STREAM <sensor_id>         → OK_VIDEO_STARTED / ERR_VIDEO_UNAVAILABLE
  DISCONNECT <sensor_id>           → OK_DISCONNECT

COMPORTAMENTO EM FALHA GW→SERVIDOR:
  - Buffer local: até 1000 medições pendentes (configurável)
  - Sensores continuam a enviar dados normalmente e recebem OK
  - Retentativa: a cada 5s com backoff exponencial (máx 60s)
  - Se buffer cheio: descartar medições mais antigas (FIFO) + log
```

### Contexto para IA — SENSOR básico

```
Estou a implementar o SENSOR do TP1 de Sistemas Distribuídos em C#.

O SENSOR deve:
1. Receber o IP do Gateway como parâmetro de inicialização
2. Ligar-se ao Gateway via TcpClient (porta 8080)
3. Implementar interface de texto simples para o utilizador simular envio de dados
4. Enviar sequência obrigatória: CONNECT → REGISTER_TYPES → DATA (repetir) → DISCONNECT
5. Respeitar a sequência: não enviar DATA antes de REGISTER_TYPES (evitar ERR_SEQUENCE)
6. Enviar HEARTBEAT periodicamente (a cada 5s, em thread separada)
7. Opcionalmente solicitar VIDEO_STREAM
8. Tratar todas as respostas: OK_CONNECTED, OK_TYPES_REGISTERED, OK, OK_DISCONNECT,
   e todos os ERR_* (exibir ao utilizador)

TIPOS DE DADOS: TEMP, HUM, AR, RUIDO, PM2.5, PM10, LUZ, VIDEO
ZONAS: ZONA_CENTRO, ZONA_ESCOLAR, ZONA_INDUSTRIAL, ZONA_RESIDENCIAL, ZONA_PARQUE

REGRAS DE VALIDAÇÃO (aplicadas pelo GW, mas o sensor deve respeitar):
  - Timestamp em ISO 8601 (AAAA-MM-DDTHH:MM:SS), máx 60s no futuro
  - Valores negativos só para TEMP; restantes ≥ 0
  - Zona deve ser uma das 5 definidas
```

### Perguntas úteis para copiar

```
[Pergunta 1] Com base no contexto acima, escreve em C# a classe [SERVIDOR/GATEWAY/SENSOR]
completa e funcional para a Fase 2, usando o protocolo v2 com todas as mensagens e
respostas definidas (GW_CONNECT, OK_CONNECTED, ERR_SEQUENCE, etc.).

[Pergunta 2] Como implemento leitura/escrita linha-a-linha num socket TCP em C#
usando StreamReader e StreamWriter sobre NetworkStream?

[Pergunta 3] Implementa o tratamento de erros completo: o que faz o Gateway quando
o Servidor envia ERR_INVALID_DATA em resposta a um FORWARD? E quando a ligação TCP
ao Servidor cai durante a operação?
```

---

## FASE 3 — Operação SENSOR com CSV e Heartbeat (7–10 Abr)

### Contexto para IA

```
Estou na Fase 3 do TP1. Preciso de implementar no GATEWAY:

1. LEITURA DO CSV: Carregar ficheiro de configuração de sensores
   Formato: sensor_id:estado:zona:[tipos_dados]:last_sync

2. VALIDAÇÃO COMPLETA ao receber dados de um sensor (por esta ordem):
   a) Verificar se sensor_id existe no CSV → ERR_NOT_REGISTERED
   b) Verificar se estado == "ativo" → ERR_SENSOR_INACTIVE
   c) Verificar se tipo_dado está na lista → ERR_TYPE_NOT_SUPPORTED
   d) Validar formato da mensagem (nº parâmetros, timestamp, zona, valor) → ERR_INVALID_DATA
   e) Se válido: atualizar last_sync com timestamp atual + FORWARD ao servidor

3. HEARTBEAT:
   - O sensor envia HEARTBEAT <sensor_id> a cada 5s (configurável)
   - O gateway atualiza last_sync no CSV a cada heartbeat
   - Timeout: 15s (3× intervalo) sem heartbeat → estado "indisponivel"
   - Ao mudar estado: enviar SENSOR_STATUS <sensor_id> indisponivel ao servidor

4. TIMEOUT DE HANDSHAKE:
   - O sensor deve completar CONNECT + REGISTER_TYPES em 10s
   - Se expirar: gateway fecha a ligação TCP e regista evento em log

5. BUFFER LOCAL (quando ligação GW→Servidor falha):
   - Armazenar até 1000 medições pendentes (configurável)
   - Sensores continuam a enviar normalmente e recebem OK
   - Retentativa: a cada 5s com backoff exponencial (máx 60s)
   - Buffer cheio: descartar FIFO (mais antigas) + log

6. ARMAZENAMENTO NO SERVIDOR:
   - Guardar dados em ficheiros separados por tipo de dado
   - Ex: TEMP.csv, HUM.csv, PM2.5.csv
   - Formato: sensor_id,zona,valor,timestamp

7. STREAM DE VÍDEO (processamento edge no Gateway):
   - Sensor solicita VIDEO_STREAM → Gateway responde OK_VIDEO_STARTED
   - Stream transmitido em porta separada (8081)
   - Gateway simula processamento edge: regista metadados (sensor_id, início,
     duração), pode gerar alertas fictícios ou log de eventos

8. TRATAMENTO DE ERROS DO SERVIDOR:
   - Se Servidor responde ERR_INVALID_DATA a um FORWARD: logar o erro,
     não propagar ao sensor (o sensor já recebeu OK)
   - Se Servidor responde ERR_STORAGE_FULL: ativar buffer local

FUNCIONALIDADE EXTRA (pontuação adicional):
   - Armazenar dados em base de dados relacional (SQLite)
   - NuGet: Microsoft.Data.Sqlite
   - Tabela: Medicoes(id INTEGER PRIMARY KEY, sensor_id TEXT, zona TEXT,
                       tipo_dado TEXT, valor REAL, timestamp TEXT)
   - Implementar consultas básicas: média por zona, últimas N medições,
     alertas (valores acima de limiar)
```

### Perguntas úteis para copiar

```
[Pergunta 1] Como faço para ler, atualizar e escrever um ficheiro CSV em C#
de forma segura (thread-safe), com o formato sensor_id:estado:zona:[tipos_dados]:last_sync?

[Pergunta 2] Como implemento um mecanismo de heartbeat em C# onde uma thread
monitoriza um dicionário de últimas atividades de cada sensor e marca como
indisponível após timeout de 15s? Ao mudar estado, deve enviar SENSOR_STATUS ao servidor.

[Pergunta 3] Implementa o buffer local do Gateway em C# usando uma Queue<string>
com capacidade máxima de 1000, que armazena FORWARDs quando o Servidor está
indisponível e os reenvia quando a ligação é restabelecida.

[Pergunta 4] Como usar SQLite em C# com Microsoft.Data.Sqlite para inserir
medições ambientais e fazer consultas como "média de temperatura na ZONA_ESCOLAR
nas últimas 24h"?
```

---

## FASE 4 — Concorrência com Threads e Mutexes (13–17 Abr)

### Contexto para IA

```
Estou na Fase 4 do TP1. Preciso de implementar atendimento concorrente em C#.

NO SERVIDOR:
- Cada ligação de Gateway deve ser tratada numa thread separada
- O acesso a cada ficheiro de dados deve ser protegido por um mutex/lock
- Deve suportar múltiplos Gateways em simultâneo (testar com 2+ Gateways)
- Objetivo: consistência dos dados mesmo com escritas concorrentes

NO GATEWAY:
- Cada ligação de Sensor deve ser tratada numa thread separada
- O acesso ao ficheiro CSV de sensores deve ser protegido por mutex/lock
- O acesso ao buffer local deve ser thread-safe
- Objetivo: múltiplos Sensores podem enviar dados em simultâneo

PADRÃO EM C#:
  // Thread por cliente:
  TcpClient client = listener.AcceptTcpClient();
  Thread t = new Thread(() => HandleClient(client));
  t.Start();

  // Lock por ficheiro:
  private static readonly object _tempFileLock = new object();
  lock (_tempFileLock) {
      File.AppendAllText("TEMP.csv", linha + "\n");
  }

  // Dictionary de locks por tipo de dado:
  private static Dictionary<string, object> _fileLocks = new();

CENÁRIOS DE TESTE OBRIGATÓRIOS:
  1. 3+ Sensores a enviar dados ao mesmo Gateway em simultâneo
  2. 2+ Gateways a enviar dados ao mesmo Servidor em simultâneo
  3. Verificar que não há corrupção nos ficheiros CSV
  4. Verificar que heartbeat timeout funciona com múltiplos sensores
  5. Simular falha de ligação GW→Servidor durante operação
```

### Perguntas úteis para copiar

```
[Pergunta 1] Como implemento em C# um servidor TCP que aceita múltiplas ligações
simultâneas usando uma thread por cliente, com lock por ficheiro de output?

[Pergunta 2] Qual é a diferença entre lock, Mutex e SemaphoreSlim em C#?
Qual devo usar para proteger acesso a ficheiros entre threads?

[Pergunta 3] Como posso testar que o meu servidor C# trata corretamente
múltiplas ligações simultâneas sem race conditions nos ficheiros?

[Pergunta 4] Implementa um teste automático em C# que lança 3 Sensores e 2
Gateways em simultâneo e verifica que os dados chegam ao Servidor sem corrupção.
```

---

## RELATÓRIO — Guia de Redação

### Contexto para IA

```
Preciso de escrever o relatório do TP1 de Sistemas Distribuídos (UTAD).

REQUISITOS DO RELATÓRIO:
- Máximo 3 páginas (excluindo anexos)
- Descrever as opções de implementação tomadas
- Descrever o protocolo definido para SENSOR/GATEWAY/SERVIDOR
- Código fonte em repositório Git (link no relatório)

ESTRUTURA SUGERIDA (para caber em 3 páginas):
  1. Introdução (5–8 linhas): contexto One Health, objetivo do sistema
  2. Protocolo (1 página): tabela de mensagens completa (v2), diagrama de estados
  3. Implementação (1 página): escolhas de design, estrutura do código,
     mecanismo de concorrência, buffer local, tratamento de erros,
     heartbeat, validação de dados
  4. Conclusão (5–8 linhas): resultado, dificuldades, funcionalidades extra
  Anexos: diagramas detalhados, excertos de código, esquema SQLite
```

---

## ESTRUTURA DE FICHEIROS SUGERIDA

```
TP1_OneHealth/
├── Sensor/
│   ├── Sensor.cs          ← Lógica principal do Sensor
│   ├── SensorCLI.cs       ← Interface de texto com o utilizador
│   └── Program.cs         ← Entry point (recebe IP do Gateway)
│
├── Gateway/
│   ├── Gateway.cs         ← Servidor para Sensores + cliente para Servidor
│   ├── SensorRegistry.cs  ← Leitura/escrita do CSV de sensores (thread-safe)
│   ├── HeartbeatMonitor.cs← Monitorização de heartbeats + timeout
│   ├── DataBuffer.cs      ← Buffer local para quando Servidor está indisponível
│   └── Program.cs         ← Entry point (recebe gateway_id + IP do Servidor)
│
├── Servidor/
│   ├── Servidor.cs        ← Servidor para Gateways (multi-thread)
│   ├── DataStore.cs       ← Armazenamento em ficheiros CSV / SQLite (thread-safe)
│   └── Program.cs         ← Entry point (recebe tipos de dados aceites)
│
├── config/
│   └── sensors.csv        ← Ficheiro de configuração do Gateway
│
└── data/                  ← Ficheiros de output do Servidor
    ├── TEMP.csv
    ├── HUM.csv
    ├── PM2.5.csv
    └── ...
```

---

## REFERÊNCIAS RÁPIDAS

### Formato do protocolo v2 (completo)

| Mensagem | Direção | Formato | Resposta Esperada |
|----------|---------|---------|-------------------|
| CONNECT | S → GW | `CONNECT <sensor_id>` | OK_CONNECTED / ERR_NOT_REGISTERED |
| REGISTER_TYPES | S → GW | `REGISTER_TYPES <t1,t2,...>` | OK_TYPES_REGISTERED / ERR_TYPE_NOT_SUPPORTED |
| DATA | S → GW | `DATA <tipo> <val> <zona> <ts>` | OK / ERR_* |
| HEARTBEAT | S → GW | `HEARTBEAT <sensor_id>` | OK |
| VIDEO_STREAM | S → GW | `VIDEO_STREAM <sensor_id>` | OK_VIDEO_STARTED / ERR_VIDEO_UNAVAILABLE |
| DISCONNECT | S → GW | `DISCONNECT <sensor_id>` | OK_DISCONNECT |
| GW_CONNECT | GW → SV | `GW_CONNECT <gateway_id>` | OK_GW_CONNECTED |
| FORWARD | GW → SV | `FORWARD <sid> <t> <v> <z> <ts>` | OK / ERR_INVALID_DATA / ERR_STORAGE_FULL |
| SENSOR_STATUS | GW → SV | `SENSOR_STATUS <sid> <estado>` | OK_STATUS_RECEIVED |
| GW_DISCONNECT | GW → SV | `GW_DISCONNECT <gateway_id>` | OK_GW_DISCONNECT |

### Tipos de dados suportados

| Código | Descrição | Unidade típica | Exemplo |
|--------|-----------|----------------|---------|
| TEMP | Temperatura | °C | 22.5 |
| HUM | Humidade relativa | % | 65.0 |
| AR | Qualidade do ar (AQI) | índice | 42 |
| RUIDO | Nível de ruído | dB | 72 |
| PM2.5 | Partículas finas | µg/m³ | 18.3 |
| PM10 | Partículas grossas | µg/m³ | 35.7 |
| LUZ | Luminosidade | lux | 450 |
| VIDEO | Stream de vídeo | — | stream_url |

### Portas TCP

| Ligação | Porta | Notas |
|---------|-------|-------|
| SENSOR → GATEWAY | 8080 | Dados e heartbeat |
| GATEWAY → SERVIDOR | 9090 | FORWARD e SENSOR_STATUS |
| VIDEO STREAM | 8081 | Porta separada para streaming |

### Estados do Sensor

| Estado | Descrição | Quem define | Transição |
|--------|-----------|-------------|-----------|
| ativo | Funcionamento normal | CSV inicial / manual | → indisponivel (heartbeat timeout) |
| manutencao | Temporariamente indisponível | Manual no CSV | → ativo (manual) |
| desativado | Removido ou desligado | Manual no CSV | Permanente |
| indisponivel | Heartbeat expirou (15s) | Gateway automático | → ativo (heartbeat recebido) |
| desligado | DISCONNECT voluntário | Gateway ao receber DISCONNECT | → ativo (novo CONNECT) |

### Dependências NuGet

| Package | Uso |
|---------|-----|
| `Microsoft.Data.Sqlite` | SQLite (funcionalidade extra) |

---

## CHECKLIST GERAL DE ENTREGA

- [ ] Protocolo v2 documentado (mensagens + estados + diagramas) ✅ Fase 1
- [ ] SENSOR funcional com interface de texto e heartbeat
- [ ] GATEWAY com validação CSV, heartbeat monitor, timeout handshake 10s
- [ ] GATEWAY com buffer local e retentativa quando Servidor indisponível
- [ ] GATEWAY com processamento edge de vídeo (pelo menos metadados/log)
- [ ] SERVIDOR com armazenamento por tipo de dado em ficheiros CSV
- [ ] Concorrência: threads + locks no GATEWAY e SERVIDOR
- [ ] Teste com 3+ sensores + 2+ gateways em simultâneo sem corrupção
- [ ] [EXTRA] Armazenamento em SQLite com consultas básicas
- [ ] Relatório ≤ 3 páginas com protocolo v2 + opções de implementação
- [ ] Código no repositório Git (link no relatório)
- [ ] Submissão no Moodle até **17 de Abril de 2026**
- [ ] Preparação para apresentação na aula PL seguinte

---

*Atualizado em 2026-03-20 · Protocolo v2 · TP1 Sistemas Distribuídos UTAD 2025/2026*
