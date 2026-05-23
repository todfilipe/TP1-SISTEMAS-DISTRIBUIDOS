# Guia de Defesa do Projeto — Alterações Efetuadas no TP2

Este guia técnico detalha as alterações arquiteturais e implementações de código efetuadas na transição do **TP1** para o **TP2**, servindo de suporte de estudo para a defesa do trabalho prático de **Sistemas Distribuídos (2025/2026)**.

---

## 1. Visão Geral da Transição (TP1 ➔ TP2)

| Característica | TP1 (Sockets Diretos) | TP2 (Sistemas Distribuídos Modernos) |
| :--- | :--- | :--- |
| **Comunicação Sensor ➔ Gateway** | Ligação TCP síncrona direta (Sockets linha-a-linha). | **Assíncrona via RabbitMQ** (Topic Exchange, desacoplamento). |
| **Normalização de Dados** | Validação simples em C# dentro do Gateway. | **RPC gRPC** com microsserviço Python dedicado (`preprocessing`). |
| **Análise de Dados** | Inexistente (armazenamento puro em SQLite). | **RPC gRPC** com microsserviço Python de estatísticas (`analysis`). |
| **Resiliência de Rede** | Reconexão básica síncrona. | **Polly** (Retry Exponencial + Jitter + Timeout) e NACK com *requeue*. |
| **Configuração** | Variáveis rígidas no código (*hardcoded*). | Centralizada em ficheiros estruturados **`appsettings.json`**. |
| **Escalabilidade** | Gateway único. | **Multi-Gateway** concorrente com **Mutexes de SO** nomeados. |

---

## 2. Fase 1: Serviços RPC (gRPC)

O gRPC foi introduzido para desacoplar a computação física e matemática do Gateway e do Servidor central, recorrendo a serviços especializados em Python.

### 2.1. Contratos e Compilação Automática (Tarefa 1.1)
*   **Criação da pasta [proto/](file:///c:/Users/Utilizador/source/repos/TP1-SISTEMAS-DISTRIBUIDOS/proto):** Concentra as definições de interface neutras de linguagem.
*   **Contrato de Normalização ([preprocessing.proto](file:///c:/Users/Utilizador/source/repos/TP1-SISTEMAS-DISTRIBUIDOS/proto/preprocessing.proto)):**
    *   Serviço `PreprocessingService` com o método RPC `Normalize` (`RawReading` ➔ `NormalizedReading`).
*   **Contrato de Estatísticas ([analysis.proto](file:///c:/Users/Utilizador/source/repos/TP1-SISTEMAS-DISTRIBUIDOS/proto/analysis.proto)):**
    *   Serviço `AnalysisService` com métodos RPC `Analyze` e `Predict`.
*   **Mecanismo de Compilação:**
    *   **C#:** Inclusão do pacote `Grpc.Tools` no [Gateway.csproj](file:///c:/Users/Utilizador/source/repos/TP1-SISTEMAS-DISTRIBUIDOS/Gateway/Gateway.csproj) e [Servidor.csproj](file:///c:/Users/Utilizador/source/repos/TP1-SISTEMAS-DISTRIBUIDOS/Servidor/Servidor.csproj). A diretiva `<Protobuf Include="..." />` gera os stubs fortemente tipados em tempo de compilação do MSBuild.
    *   **Python:** Geração manual/automatizada via script [build.sh](file:///c:/Users/Utilizador/source/repos/TP1-SISTEMAS-DISTRIBUIDOS/proto/build.sh) utilizando a ferramenta `grpc_tools.protoc`.
*   *Decisão Associada:* **DEC-002** (Uso de gRPC para interoperabilidade C#/Python).

### 2.2. Serviço de Pré-Processamento (Tarefa 1.2)
Implementado em Python na pasta [services/preprocessing/](file:///c:/Users/Utilizador/source/repos/TP1-SISTEMAS-DISTRIBUIDOS/services/preprocessing) (porta `50051`).
*   **Parsing Automático de Formatos:** Deteta e extrai valores e metadados caso os sensores publiquem no formato bruto JSON, XML ou CSV.
*   **Conversão F/K ➔ C:** Converte a escala de temperatura de Fahrenheit (`F`) ou Kelvin (`K`) para Celsius (`C`) automaticamente.
*   **Validação Regulamentar de Ranges:**
    *   `TEMP` (-50 a 60 °C) \| `HUM` (0 a 100%)
    *   `AR`, `PM2.5`, `PM10` (0 a 1000 µg/m³)
    *   `RUIDO` (0 a 150 dB) \| `LUZ` (0 a 100000 lux)
*   **Contentorização:** Criação de um `Dockerfile` baseado em `python:3.12-slim` para isolar a dependência do gRPC.
*   *Decisão Associada:* **DEC-003** (Isolamento do pré-processamento de transformação física).

### 2.3. Serviço de Análise (Tarefa 1.3)
Implementado em Python na pasta [services/analysis/](file:///c:/Users/Utilizador/source/repos/TP1-SISTEMAS-DISTRIBUIDOS/services/analysis) (porta `50052`).
*   **Ligação ao SQLite do Servidor:** Consultas SQL na tabela `medicoes` do SQLite (`data/urbano.db`). O caminho da BD é parametrizável por variável de ambiente (`DB_PATH`).
*   **Lógica Estatística:**
    *   **Média Móvel:** Janela configurável de $k=5$ períodos.
    *   **Deteção de Outliers:** Utiliza a técnica de Z-score ($|z| > 2.0$ desvios padrão).
    *   **Tendência:** Declive da reta obtido por regressão linear (Estável, Crescente ou Decrescente).
    *   **Previsões:** Regressão linear por mínimos quadrados projetando os próximos $N$ períodos.
    *   **Nível de Alerta:** Classificação dinâmica em `NORMAL`, `WARNING` ou `CRITICAL` com base no último valor comparado com limiares físicos de poluição e ruído.
*   *Decisão Associada:* **DEC-004** (Desacoplamento estatístico do Servidor Principal).

### 2.4. Integração do Cliente gRPC no Gateway com Polly (Tarefa 1.4)
*   **Pipeline Polly (Resiliência):**
    *   Integração do pacote `Polly` (v8) no Gateway C#.
    *   **Retry Exponencial com Jitter:** 3 tentativas focadas em falhas de rede (`RpcException` com códigos `Unavailable`, `DeadlineExceeded`, `Internal`).
    *   **Timeout:** Limite máximo de 5 segundos de espera por resposta para evitar que as threads dos sensores fiquem bloqueadas indefinidamente.
*   **Isolamento de Escopo:** Envolvimento da chamada gRPC no `switch` do sensor dentro de um bloco `{ ... }` local em `Program.cs` para evitar conflito de escopo.
*   *Decisão Associada:* **DEC-005** (Integração gRPC no Gateway com resiliência baseada em Polly).

### 2.5. Integração do Cliente gRPC no Servidor Central (Tarefa 1.5)
*   **Exposição CLI do Servidor:**
    *   Comando `analisar`: executa cálculos históricos via gRPC.
    *   Comando `prever`: projeta séries temporais futuras via gRPC.
    *   Comando `historico`: lista as análises guardadas em memória.
*   **Auditoria local:** Armazenamento dos resultados na lista de memória e persistência textual em `analysis_history.log`.
*   *Decisão Associada:* **DEC-006** (Cliente gRPC integrado no Servidor e logs de auditoria).

---

## 3. Fase 2: Comunicação Pub/Sub (RabbitMQ)

O paradigma de comunicação do sistema foi atualizado de ligações diretas síncronas para um modelo **Publish/Subscribe assíncrono** com tratamento distribuído de filas de mensagens.

### 3.1. Configuração do Broker (Tarefa 2.1)
*   **Idempotência de Configuração:**
    *   Uso de [docker-compose.yml](file:///c:/Users/Utilizador/source/repos/TP1-SISTEMAS-DISTRIBUIDOS/docker-compose.yml).
    *   Mapeamento do [definitions.json](file:///c:/Users/Utilizador/source/repos/TP1-SISTEMAS-DISTRIBUIDOS/rabbitmq/definitions.json) e [rabbitmq.conf](file:///c:/Users/Utilizador/source/repos/TP1-SISTEMAS-DISTRIBUIDOS/rabbitmq/rabbitmq.conf).
    *   Criação automática dos utilizadores administradores (`admin` / `admin`), do virtual host (`onehealth`) e do Topic Exchange (`sensors.exchange`).
*   *Decisão Associada:* **DEC-007** (Instalação e Configuração idempotente do RabbitMQ).

### 3.2. Integração Pub/Sub nos Componentes .NET (Tarefa 2.2)
*   Adição da biblioteca `RabbitMQ.Client` (v6.8.1).
*   **Modelo de Tópicos (Topic Exchange):**
    *   As mensagens são encaminhadas de forma flexível utilizando a routing key: `<zona>.<tipo>.<id>`.
*   **Handshake Síncrono Simulado:**
    *   Para não reescrever a consola do sensor, a lógica C# simula as respostas `OK_CONNECTED` e `OK_TYPES_REGISTERED` localmente dependendo apenas do estado da ligação com o broker.
*   **Isolamento do Canal de Vídeo:**
    *   O stream de dados de vídeo é mantido via socket TCP direto (porta `8081`) para não entupir a rede do broker.
*   *Decisão Associada:* **DEC-008** (Comunicação Pub/Sub via Topic Exchange).

### 3.3. Configuração Centralizada e Resiliência no Sensor (Tarefa 2.3)
*   **Ficheiro [appsettings.json](file:///c:/Users/Utilizador/source/repos/TP1-SISTEMAS-DISTRIBUIDOS/Sensor/appsettings.json) no Sensor:**
    *   Substituição das variáveis *hardcoded* pela biblioteca `Microsoft.Extensions.Configuration`.
*   **Mecanismo `EnsureConnection()`:**
    *   Thread-safe: verifica se a ligação ou o canal caíram.
    *   **Backoff Exponencial:** Se o broker ficar offline, o sensor tenta reconectar a cada 2s, 4s, 8s, 16s... até um limite máximo de 30s.
*   **Loop de Publicação:** Método `StartAutomaticPublishing()` envia dados gerados periodicamente.
*   *Decisão Associada:* **DEC-009** (Configuração centralizada e auto-reconexão com backoff no Sensor).

### 3.4. Consumo com ACK/NACK Manual no Gateway (Tarefa 2.4)
*   **Bindings Dinâmicos:** Se a secção de bindings do `appsettings.json` estiver vazia, o Gateway lê o [sensors.csv](file:///c:/Users/Utilizador/source/repos/TP1-SISTEMAS-DISTRIBUIDOS/Gateway/sensors.csv) no arranque e associa bindings do tipo `<zona>.#`.
*   **Controlo de Fluxo Resiliente (`autoAck: false`):**
    *   **Manual ACK:** Enviado ao broker apenas se a leitura for validada localmente, normalizada por gRPC e enfileirada para agregação. Também envia ACK para descartar *poison messages* (mensagens com JSON inválido ou ID inexistente) de forma a não travar a fila.
    *   **Manual NACK com Requeue:** Se o microsserviço gRPC de pré-processamento estiver temporariamente indisponível, o Gateway envia um `BasicNack(requeue: true)` para manter a leitura em fila e voltar a tentar mais tarde. É aplicado um `Thread.Sleep(1000)` antes do Nack para mitigar loopsCPU rápidos em caso de quedas prolongadas.
*   *Decisão Associada:* **DEC-010** (Consumo resiliente no Gateway com ACK/NACK manual).

### 3.5. Suporte a Multi-Gateway (Tarefa 2.5)
*   **Parametrização por CLI:** Possibilidade de lançar Gateways independentes especificando o ID, portas e bindings pela linha de comandos:
    `dotnet run <gatewayId> <servidorIp> <servidorPort> <videoPort> <bindings...>`
*   **Sincronização por Mutexes do Sistema Operativo:**
    *   Múltiplas instâncias do Gateway executadas na mesma máquina podem tentar escrever nos mesmos ficheiros locais em simultâneo.
    *   **`GatewayConfigMutex`:** Protege de condições de corrida as escritas/leituras concorrentes no ficheiro partilhado [sensors.csv](file:///c:/Users/Utilizador/source/repos/TP1-SISTEMAS-DISTRIBUIDOS/Gateway/sensors.csv).
    *   **`GatewayVideoLogMutex`:** Garante exclusão mútua ao atualizar o registo centralizado de vídeo `video_metadata.log`.
*   **Argumento `--auto` no Sensor:**
    *   Permite lançar sensores simulados de diferentes tipos e zonas rapidamente pela linha de comandos:
        `dotnet run --auto <sensorId> <zona> <tipo> <intervaloSeconds>`
*   *Decisão Associada:* **DEC-011** (Arquitetura Multi-Gateway e sincronização com Mutexes de SO).
