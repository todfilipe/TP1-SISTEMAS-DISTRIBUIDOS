# 🧠 Guia de Contexto para IA — TP1 Sistemas Distribuídos
## Serviços de Monitorização Urbana para One Health · UTAD 2025/2026

---

## 📌 CONTEXTO GERAL DO PROJETO

```
Estou a desenvolver um Trabalho Prático (TP1) para a UC de Sistemas Distribuídos
da Licenciatura em Engenharia Informática na UTAD (2025/2026).

O projeto chama-se "Serviços de Monitorização Urbana para One Health".

OBJETIVO: Implementar um sistema distribuído em C# usando sockets TCP que simula
uma infraestrutura de monitorização ambiental urbana.

ARQUITETURA — 3 entidades:
  - SENSOR: recolhe dados ambientais e envia para o Gateway
  - GATEWAY: valida, agrega e encaminha dados dos Sensores para o Servidor
  - SERVIDOR: armazena e processa dados dos Gateways

LINGUAGEM: C# com sockets TCP (TcpListener / TcpClient)
PROTOCOLO: textual, linha-a-linha (mensagens terminam em \n)
EQUIPA: 3 elementos

PRAZO DE ENTREGA: 17 de Abril de 2026 (Moodle + apresentação na aula PL)
```

---

## 🏗️ FASE 1 — Desenho do Protocolo (semana 16–20 Mar)

### Contexto para IA

```
Estou na Fase 1 do TP1 de Sistemas Distribuídos (C#, sockets TCP).
Preciso de definir um protocolo de comunicação textual para as entidades:
SENSOR → GATEWAY → SERVIDOR

REQUISITOS DO PROTOCOLO:
- Mensagens de início de comunicação (SENSOR identifica-se ao GATEWAY)
- SENSOR declara os tipos de dados que consegue recolher
- SENSOR envia medições ambientais (tipo, valor, zona, timestamp)
- SENSOR envia heartbeat periódico
- SENSOR pode solicitar stream de vídeo
- SENSOR termina a comunicação corretamente
- GATEWAY valida registo do sensor, estado e tipos suportados
- GATEWAY encaminha dados validados para o SERVIDOR
- SERVIDOR confirma receção
- Respostas OK/ERR para cada situação

TIPOS DE DADOS AMBIENTAIS: TEMP, HUM, AR, RUIDO, PM2.5, PM10, LUZ, VIDEO

FORMATO DO FICHEIRO CSV DO GATEWAY:
  sensor_id:estado:zona:[tipos_dados]:last_sync
  Ex: S101:ativo:ZONA_CENTRO:[TEMP,HUM,RUIDO]:2026-03-10T08:45:00
  Estados: ativo | manutencao | desativado
```

### Perguntas úteis para copiar

```
[Pergunta 1] Com base no contexto acima, propõe um protocolo textual completo
com todas as mensagens necessárias para o diálogo SENSOR/GATEWAY/SERVIDOR,
incluindo formato exato de cada mensagem e possíveis respostas.

[Pergunta 2] Cria um diagrama de estados (em texto/ASCII) para cada entidade
(SENSOR, GATEWAY, SERVIDOR) mostrando os estados e as transições do protocolo.

[Pergunta 3] Simula um diálogo completo entre SENSOR, GATEWAY e SERVIDOR
usando o protocolo definido, desde a conexão até à desconexão.
```

---

## 🔧 FASE 2 — Implementação Básica em C# (semana 23–27 Mar)

### Contexto para IA — SERVIDOR básico

```
Estou a implementar o SERVIDOR do TP1 de Sistemas Distribuídos em C#.

O SERVIDOR deve:
1. Abrir um socket TCP (TcpListener) e aguardar ligação do GATEWAY
2. Receber mensagens de texto linha-a-linha (protocolo textual)
3. Processar mensagens: início de comunicação, dados de sensor, fim
4. Responder com OK ou mensagens de erro
5. (Fase 4) Tratar múltiplas ligações concorrentes com threads

PROTOCOLO DEFINIDO (mensagens que o servidor recebe do gateway):
  FORWARD <sensor_id> <tipo_dado> <valor> <zona> <timestamp>
  SENSOR_STATUS <sensor_id> <estado>
  DISCONNECT <gateway_id>

RESPOSTAS DO SERVIDOR:
  OK
  ERR_INVALID_DATA
```

### Contexto para IA — GATEWAY básico

```
Estou a implementar o GATEWAY do TP1 de Sistemas Distribuídos em C#.

O GATEWAY deve:
1. Aceitar ligação de SENSOR(es) via TcpListener
2. Verificar se o sensor está registado (ficheiro CSV)
3. Validar estado do sensor e tipos de dados suportados
4. Atualizar last_sync após receber dados
5. Monitorizar heartbeats (timeout configurável)
6. Encaminhar dados válidos para o SERVIDOR via TcpClient
7. (Fase 4) Usar threads para múltiplos sensores simultâneos

FICHEIRO CSV DOS SENSORES (formato):
  sensor_id:estado:zona:[tipos_dados]:last_sync
  Ex: S101:ativo:ZONA_CENTRO:[TEMP,HUM,RUIDO]:2026-03-10T08:45:00

PROTOCOLO — mensagens recebidas do SENSOR:
  CONNECT <sensor_id>
  REGISTER_TYPES <tipo1,tipo2,...>
  DATA <tipo_dado> <valor> <zona> <timestamp>
  HEARTBEAT <sensor_id>
  VIDEO_STREAM <sensor_id>
  DISCONNECT <sensor_id>
```

### Contexto para IA — SENSOR básico

```
Estou a implementar o SENSOR do TP1 de Sistemas Distribuídos em C#.

O SENSOR deve:
1. Receber o IP do Gateway como parâmetro de inicialização
2. Ligar-se ao Gateway via TcpClient
3. Implementar interface de texto simples para o utilizador simular envio de dados
4. Enviar sequência: CONNECT → REGISTER_TYPES → DATA (repetir) → DISCONNECT
5. Enviar HEARTBEAT periodicamente (ex: a cada 5 segundos, em thread separada)
6. Opcionalmente solicitar VIDEO_STREAM

TIPOS DE DADOS: TEMP, HUM, AR, RUIDO, PM2.5, PM10, LUZ
ZONAS: ZONA_CENTRO, ZONA_ESCOLAR, ZONA_INDUSTRIAL, ZONA_RESIDENCIAL, ZONA_PARQUE
```

### Perguntas úteis para copiar

```
[Pergunta 1] Com base no contexto acima, escreve em C# a classe [SERVIDOR/GATEWAY/SENSOR]
completa e funcional para a Fase 2, com comentários explicativos.

[Pergunta 2] Como implemento leitura/escrita linha-a-linha num socket TCP em C#
usando StreamReader e StreamWriter sobre NetworkStream?

[Pergunta 3] Mostra um exemplo de como enviar e receber mensagens de texto
entre TcpClient e TcpListener em C#, com tratamento de erros.
```

---

## 📂 FASE 3 — Operação SENSOR com CSV e Heartbeat (7–10 Abr)

### Contexto para IA

```
Estou na Fase 3 do TP1. Preciso de implementar no GATEWAY:

1. LEITURA DO CSV: Carregar ficheiro de configuração de sensores
   Formato: sensor_id:estado:zona:[tipos_dados]:last_sync

2. VALIDAÇÃO COMPLETA ao receber dados de um sensor:
   a) Verificar se sensor_id existe no CSV
   b) Verificar se estado == "ativo"
   c) Verificar se o tipo_dado está na lista de tipos do sensor
   d) Se válido: atualizar last_sync com timestamp atual
   e) Se inválido: responder com ERR apropriado

3. HEARTBEAT:
   - O sensor envia HEARTBEAT <sensor_id> periodicamente
   - O gateway atualiza last_sync no CSV
   - Se um sensor não enviar heartbeat por X segundos, marcar como indisponível

4. ARMAZENAMENTO NO SERVIDOR:
   - Guardar dados em ficheiros separados por tipo de dado
   - Ex: TEMP.csv, HUM.csv, PM2.5.csv
   - Formato: sensor_id,zona,valor,timestamp

FUNCIONALIDADE EXTRA (pontuação adicional):
   - Armazenar dados em base de dados relacional (SQLite recomendado)
   - NuGet: Microsoft.Data.Sqlite
   - Tabela: Medicoes(id INTEGER PRIMARY KEY, sensor_id TEXT, zona TEXT,
                       tipo_dado TEXT, valor REAL, timestamp TEXT)
```

### Perguntas úteis para copiar

```
[Pergunta 1] Como faço para ler, atualizar e escrever um ficheiro CSV em C#
de forma segura, com o formato sensor_id:estado:zona:[tipos_dados]:last_sync?

[Pergunta 2] Como implemento um mecanismo de heartbeat em C# onde uma thread
monitoriza um dicionário de últimas atividades de cada sensor e marca como
indisponível após timeout?

[Pergunta 3] Como usar SQLite em C# com Microsoft.Data.Sqlite para inserir
medições ambientais recebidas via socket?
```

---

## 🧵 FASE 4 — Concorrência com Threads e Mutexes (13–17 Abr)

### Contexto para IA

```
Estou na Fase 4 do TP1. Preciso de implementar atendimento concorrente em C#.

NO SERVIDOR:
- Cada ligação de Gateway deve ser tratada numa thread separada
- O acesso a cada ficheiro de dados deve ser protegido por um mutex/lock
- Objetivo: múltiplos Gateways podem enviar dados em simultâneo sem corrupção

NO GATEWAY:
- Cada ligação de Sensor deve ser tratada numa thread separada
- O acesso ao ficheiro CSV de sensores deve ser protegido por mutex/lock
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

  // Alternativa com Dictionary de locks por tipo de dado:
  private static Dictionary<string, object> _fileLocks = new();
```

### Perguntas úteis para copiar

```
[Pergunta 1] Como implemento em C# um servidor TCP que aceita múltiplas ligações
simultâneas usando uma thread por cliente, com lock por ficheiro de output?

[Pergunta 2] Qual é a diferença entre lock, Mutex e SemaphoreSlim em C#?
Qual devo usar para proteger acesso a ficheiros entre threads?

[Pergunta 3] Como posso testar que o meu servidor C# trata corretamente
múltiplas ligações simultâneas sem race conditions nos ficheiros?
```

---

## 📝 RELATÓRIO — Guia de Redação

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
  2. Protocolo (1 página): tabela de mensagens, diagrama de estados simplificado
  3. Implementação (1 página): escolhas de design, estrutura do código,
     mecanismo de concorrência, tratamento de erros
  4. Conclusão (5–8 linhas): resultado, dificuldades, funcionalidades extra
  Anexos: diagramas detalhados, excertos de código relevante
```

### Perguntas úteis para copiar

```
[Pergunta 1] Com base no protocolo e implementação descritos acima,
ajuda-me a redigir a secção "Protocolo de Comunicação" para o relatório,
de forma concisa e técnica (máximo 1 página A4).

[Pergunta 2] Como posso apresentar o diagrama de estados do protocolo
em formato ASCII/texto para incluir no relatório?

[Pergunta 3] Revê este texto do relatório e sugere melhorias de clareza
e rigor técnico: [COLAR TEXTO AQUI]
```

---

## 🐞 DEBUGGING — Problemas Comuns

### Contexto para IA

```
Estou a depurar o TP1 de Sistemas Distribuídos em C# (sockets TCP, protocolo textual).
```

### Snippets de erro comuns e como pedir ajuda

```
[Erro de conexão]
"Estou a ter o erro [COLAR ERRO] quando o Sensor tenta ligar ao Gateway em C#.
O Gateway usa TcpListener na porta [PORTA]. Aqui está o código relevante: [CÓDIGO]"

[Dados corrompidos / race condition]
"Estou a ter race condition ao escrever no ficheiro CSV quando múltiplas threads
acedem em simultâneo. Aqui está o código: [CÓDIGO]. Como corrijo com lock/mutex?"

[Heartbeat não funciona]
"O mecanismo de heartbeat não está a detetar sensores inativos.
Aqui está a implementação: [CÓDIGO]. O que está errado?"

[Mensagem não é recebida]
"O GATEWAY não recebe a mensagem enviada pelo SENSOR.
Código do SENSOR (envio): [CÓDIGO]
Código do GATEWAY (receção): [CÓDIGO]
O que pode estar a falhar?"
```

---

## 📐 ESTRUTURA DE FICHEIROS SUGERIDA

```
TP1_OneHealth/
├── Sensor/
│   ├── Sensor.cs          ← Lógica principal do Sensor
│   ├── SensorCLI.cs       ← Interface de texto com o utilizador
│   └── Program.cs         ← Entry point (recebe IP do Gateway)
│
├── Gateway/
│   ├── Gateway.cs         ← Servidor para Sensores + cliente para Servidor
│   ├── SensorRegistry.cs  ← Leitura/escrita do CSV de sensores
│   ├── HeartbeatMonitor.cs← Monitorização de heartbeats
│   └── Program.cs
│
├── Servidor/
│   ├── Servidor.cs        ← Servidor para Gateways
│   ├── DataStore.cs       ← Armazenamento em ficheiros / SQLite
│   └── Program.cs
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

## 🔑 REFERÊNCIAS RÁPIDAS

### Formato do protocolo (resumo)

| Mensagem | Direção | Formato |
|----------|---------|---------|
| CONNECT | SENSOR→GW | `CONNECT <sensor_id>` |
| REGISTER_TYPES | SENSOR→GW | `REGISTER_TYPES <tipo1,tipo2,...>` |
| DATA | SENSOR→GW | `DATA <tipo> <valor> <zona> <timestamp>` |
| HEARTBEAT | SENSOR→GW | `HEARTBEAT <sensor_id>` |
| VIDEO_STREAM | SENSOR→GW | `VIDEO_STREAM <sensor_id>` |
| DISCONNECT | SENSOR→GW | `DISCONNECT <sensor_id>` |
| FORWARD | GW→SERV | `FORWARD <sensor_id> <tipo> <valor> <zona> <ts>` |
| SENSOR_STATUS | GW→SERV | `SENSOR_STATUS <sensor_id> <estado>` |
| OK | GW/SERV→remetente | `OK` |
| ERR_NOT_REGISTERED | GW→SENSOR | `ERR_NOT_REGISTERED` |
| ERR_SENSOR_INACTIVE | GW→SENSOR | `ERR_SENSOR_INACTIVE` |
| ERR_TYPE_NOT_SUPPORTED | GW→SENSOR | `ERR_TYPE_NOT_SUPPORTED` |
| ERR_INVALID_DATA | GW/SERV→remetente | `ERR_INVALID_DATA` |

### Tipos de dados suportados

| Código | Descrição | Unidade típica |
|--------|-----------|----------------|
| TEMP | Temperatura | °C |
| HUM | Humidade relativa | % |
| AR | Qualidade do ar (AQI) | índice |
| RUIDO | Nível de ruído | dB |
| PM2.5 | Partículas finas | µg/m³ |
| PM10 | Partículas grossas | µg/m³ |
| LUZ | Luminosidade | lux |
| VIDEO | Stream de vídeo | — |

### Portas TCP sugeridas

| Ligação | Porta sugerida |
|---------|---------------|
| SENSOR → GATEWAY | 8080 |
| GATEWAY → SERVIDOR | 9090 |

### Dependências NuGet úteis

| Package | Uso |
|---------|-----|
| `Microsoft.Data.Sqlite` | SQLite (funcionalidade extra) |
| `Newtonsoft.Json` | Serialização JSON (opcional) |

---

## ✅ CHECKLIST GERAL DE ENTREGA

- [ ] Protocolo documentado (mensagens + estados)
- [ ] SENSOR funcional com interface de texto
- [ ] GATEWAY com validação CSV e heartbeat
- [ ] SERVIDOR com armazenamento por tipo de dado
- [ ] Concorrência: threads + locks no GATEWAY e SERVIDOR
- [ ] Teste com múltiplos sensores em simultâneo sem corrupção
- [ ] Relatório ≤ 3 páginas com protocolo + opções de implementação
- [ ] Código no repositório Git (link no relatório)
- [ ] Submissão no Moodle até **17 de Abril de 2026**
- [ ] Preparação para apresentação na aula PL seguinte

---

*Gerado em 2026-03-20 · TP1 Sistemas Distribuídos UTAD 2025/2026*
