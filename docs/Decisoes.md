# Registo de Decisões de Desenvolvimento — TP1 Sistemas Distribuídos

**Projeto:** Serviços de Monitorização Urbana para One Health  
**UC:** Sistemas Distribuídos 2025/2026 · UTAD · ECT

---

> Este ficheiro regista as decisões técnicas e arquitecturais tomadas ao longo do desenvolvimento.  
> Para cada nova decisão, copia o template no final deste ficheiro e preenche todos os campos.

---

## Índice

| DEC-001 | Uso de SQLite com modo WAL no Servidor | `Servidor` | ✅ Aceite | 2026-05-15 |
| DEC-002 | Introdução de gRPC e Configuração de Compilação de Stubs | `Geral` | ✅ Aceite | 2026-05-20 |
| DEC-003 | Serviço de Pré-Processamento em Python | `Preprocessing` | ✅ Aceite | 2026-05-20 |
| DEC-004 | Serviço de Análise em Python | `Analysis` | ✅ Aceite | 2026-05-20 |
| DEC-005 | Integração do Cliente gRPC no Gateway com Polly | `Gateway` | ✅ Aceite | 2026-05-20 |
| DEC-006 | Integração do Cliente gRPC no Servidor para Serviço de Análise | `Servidor` | ✅ Aceite | 2026-05-20 |
| DEC-007 | Instalação e Configuração do RabbitMQ | `Geral` | ✅ Aceite | 2026-05-21 |
| DEC-008 | Comunicação Pub/Sub baseada em Modelo de Tópicos (RabbitMQ) | `Geral` | ✅ Aceite | 2026-05-21 |
| DEC-009 | Configuração Centralizada e Resiliência do Sensor (.NET) | `Sensor` | ✅ Aceite | 2026-05-21 |
| DEC-010 | Integração do Consumidor RabbitMQ e Resiliência no Gateway | `Gateway` | ✅ Aceite | 2026-05-21 |
| DEC-011 | Arquitetura Multi-Gateway e Desacoplamento de Tópicos | `Geral` | ✅ Aceite | 2026-05-21 |


---

## Template — como usar

1. Copia o bloco abaixo
2. Substitui `XXX` pelo número sequencial (`DEC-001`, `DEC-002`, …)
3. Preenche todos os campos
4. Adiciona uma linha ao **Índice** acima

---

## Template

```markdown
## DEC-XXX

**Título:** <título curto e descritivo>

**Componente:** `Sensor` | `Gateway` | `Servidor` | `Protocolo` | `GUI` | `Geral`

**Estado:** 🔄 Em discussão | ✅ Aceite | ❌ Rejeitada | 🔁 Revista

**Data:** AAAA-MM-DD

**Autores:** <nomes>

### Contexto

<Descreve o problema ou necessidade que levou a esta decisão. Inclui restrições do enunciado se relevante.>

### Opções consideradas

| Opção | Prós | Contras |
|-------|------|---------|
| Opção A | … | … |
| Opção B | … | … |

### Decisão

<Qual foi a opção escolhida e como foi implementada.>

### Justificação

<Porquê esta opção e não outra. Liga ao enunciado, a limitações técnicas, ou a escolhas de equipa.>

### Consequências

- ✅ <benefício direto>
- ⚠️ <trade-off aceitável>
- ❌ <limitação conhecida>
```

---

## Exemplo preenchido — DEC-001

> *Podes apagar este bloco depois de criar a tua primeira decisão real.*

**Título:** Uso de SQLite com modo WAL no Servidor

**Componente:** `Servidor`

**Estado:** ✅ Aceite

**Data:** 2026-05-15

**Autores:** Equipa

### Contexto

O Servidor precisa de aceitar múltiplos Gateways em simultâneo, cada um na sua própria thread. Era necessário um mecanismo de persistência que suportasse escritas concorrentes sem risco de corrupção.

### Opções consideradas

| Opção | Prós | Contras |
|-------|------|---------|
| SQLite modo WAL | Leve, sem servidor externo, concorrência nativa | Não escala para múltiplos processos em máquinas distintas |
| PostgreSQL | Escala horizontalmente, ACID completo | Overhead de instalação, fora do âmbito do TP |
| Ficheiros JSON/CSV | Simples de implementar | Sem suporte a concorrência, risco de corrupção |

### Decisão

Utilizar **SQLite em modo WAL** (`PRAGMA journal_mode=WAL`), gerido via Dapper.

### Justificação

O TP corre num único servidor. WAL permite múltiplos leitores e um escritor simultâneos, suficiente para o cenário pedido. Elimina qualquer dependência externa e simplifica a entrega.

### Consequências

- ✅ Sem servidor de base de dados externo para configurar
- ✅ Leituras não bloqueiam escritas graças ao WAL
- ⚠️ Dados podem perder-se em crash a meio de uma transação (aceitável no TP)
- ❌ Não escalaria para um cenário multi-servidor real

---

## DEC-002

**Título:** Introdução de gRPC e Configuração de Compilação de Stubs

**Componente:** `Geral`

**Estado:** ✅ Aceite

**Data:** 2026-05-20

**Autores:** Equipa

### Contexto

Para o desenvolvimento do TP2, é necessária a integração de chamadas RPC entre o Gateway e serviços de pré-processamento de dados, assim como entre o Servidor e serviços de análise e previsão. Esta infraestrutura necessita de suporte gRPC multiplataforma e multilinguagem (C# e Python).

### Opções consideradas

| Opção | Prós | Contras |
|-------|------|---------|
| gRPC (Protobuf) | Excelente desempenho, tipagem forte estruturada, compilação de stubs automática, suporte nativo a C# e Python | Necessidade de setup de compilação adicional |
| JSON-RPC / HTTP REST | Simples de testar sem stubs pré-compilados | Menor desempenho, sem tipagem forte out-of-the-box para modelos complexos |

### Decisão

Adoptou-se o uso de **gRPC com Protocol Buffers (proto3)**. Foi criada uma estrutura de ficheiros `.proto` na pasta de raiz (`proto/`) contendo as definições dos serviços `PreprocessingService` e `AnalysisService`. Adicionou-se o pacote `Grpc.Tools` ao C# para compilação automática no build do MSBuild, e configurou-se a compilação Python via `grpc_tools.protoc`.

### Justificação

O gRPC permite interoperabilidade e alto desempenho na comunicação distribuída entre o ecossistema C# (Gateway e Servidor) e potenciais scripts ou serviços externos em Python, cumprindo diretamente os requisitos da Fase 1 do TP2.

### Consequências

- ✅ Comunicação tipada e de alto desempenho
- ✅ Compilação automática integrada no build do C# para evitar inconsistência de código
- ✅ Facilidade em gerar stubs atualizados para Python

---

## DEC-003

**Título:** Serviço de Pré-Processamento em Python

**Componente:** `Preprocessing`

**Estado:** ✅ Aceite

**Data:** 2026-05-20

**Autores:** Equipa

### Contexto

O Gateway necessita de invocar um serviço externo via gRPC para realizar a normalização, conversão de escalas (Fahrenheit/Kelvin para Celsius) e validação de ranges de dados meteorológicos e ambientais antes de agregar a informação. O serviço necessita de ser isolado e portável, sendo disponibilizado como um contentor Docker.

### Opções consideradas

| Opção | Prós | Contras |
|-------|------|---------|
| Python com gRPC e Docker | Altamente flexível para tarefas de transformação de dados e parsing, facilidade de implementação, isolamento completo com Docker | Requer gestão adicional de contentores no deploy |
| C# Library local ou outro serviço C# | Evita comunicação em rede / inter-processos | Dificulta a divisão de responsabilidades e impede o uso de ferramentas nativas e rápidas de processamento e normalização que podem ser executadas em Python |

### Decisão

Foi desenvolvido o serviço de pré-processamento em Python na pasta `services/preprocessing/` escutando na porta `50051`. A lógica de normalização foi implementada para lidar com:
1. Parsing de cargas brutas em formatos JSON, XML ou CSV presentes no campo `rawFormat`.
2. Conversão automática das unidades Fahrenheit (`F`) e Kelvin (`K`) para Celsius (`C`) quando aplicável a medições de temperatura.
3. Validação dos limites regulamentares para cada tipo de medição (Temperatura, Humidade, Qualidade do ar, Ruído, Luminosidade).
O serviço foi totalmente dockerizado através de um `Dockerfile` self-contained.

### Justificação

O uso de Python facilita o processamento e normalização de dados e atende à recomendação de diversificação tecnológica e interoperabilidade do trabalho prático.

### Consequências

- ✅ Separação clara de responsabilidades entre Gateway (encaminhamento e agregação) e Preprocessing (validação física e normalização)
- ✅ Portabilidade total através de Docker
- ✅ Flexibilidade no suporte a múltiplos formatos de dados de sensores (JSON, XML, CSV)

---

## DEC-004

**Título:** Serviço de Análise em Python

**Componente:** `Analysis`

**Estado:** ✅ Aceite

**Data:** 2026-05-20

**Autores:** Equipa

### Contexto

O Servidor necessita de invocar um serviço especializado para realizar análises estatísticas históricas, deteção de anomalias (outliers) e previsões a curto prazo. O serviço necessita de aceder à base de dados SQLite (`urbano.db`) para analisar as leituras registadas e ser isolado através de um contentor Docker.

### Opções consideradas

| Opção | Prós | Contras |
|-------|------|---------|
| Python com gRPC e Docker | Facilidade na implementação de cálculos matemáticos e estatísticos (médias móveis, desvio padrão, regressão linear) utilizando algoritmos nativos leves sem dependências pesadas, portabilidade total via Docker | Necessita de montagem de volume no Docker para aceder ao ficheiro SQLite local do Servidor |
| C# Serviços Locais | Acesso directo à base de dados sem volumes | Dificulta a modularização e impede a interoperabilidade entre tecnologias |

### Decisão

Foi desenvolvido o serviço de análise de dados em Python na pasta `services/analysis/` escutando na porta `50052`. A lógica implementa:
1. Conectividade directa à base de dados SQLite do Servidor (`urbano.db`) com caminhos parametrizáveis.
2. Cálculo de **médias móveis** de janela configurável (por omissão 5 períodos).
3. Deteção de **outliers** utilizando a métrica Z-score (limiar superior a 2.0 desvios padrão).
4. **Análise de tendências** através do cálculo do declive da reta de regressão linear (Classificação: Crescente, Decrescente, Estável).
5. **Previsão a curto prazo** utilizando projeção por regressão linear de mínimos quadrados sobre o tempo.
O serviço foi totalmente dockerizado.

### Justificação

O serviço em Python fornece uma divisão limpa na arquitetura, isolando a computação analítica do armazenamento, facilitando futuras evoluções para bibliotecas científicas mais complexas se necessário.

### Consequências

- ✅ Análises matemáticas desacopladas da aplicação Servidor principal
- ✅ Suporte nativo a regressão linear e estatística básica leve e rápida sem bibliotecas pesadas externas
- ✅ Portabilidade total através de Docker com montagem de volume para a base de dados SQLite

---

## DEC-005

**Título:** Integração do Cliente gRPC no Gateway com Polly

**Componente:** `Gateway`

**Estado:** ✅ Aceite

**Data:** 2026-05-20

**Autores:** Equipa

### Contexto

Para garantir que as leituras recebidas dos sensores são limpas, válidas e com unidades consistentes (por exemplo, Celsius) antes de serem agregadas e transmitidas ao Servidor Central, o Gateway deve invocar o `PreprocessingService.Normalize` via gRPC para cada leitura recebida. Esta comunicação deve ser resiliente a falhas temporárias na rede ou indisponibilidade momentânea do serviço gRPC.

### Opções consideradas

| Opção | Prós | Contras |
|-------|------|---------|
| gRPC com Retry Exponencial via Polly | Tratamento nativo e performante de falhas transientes, tempos de espera parametrizados com jitter para evitar congestionamento (Thundering Herd), suporte a timeout nativo | Requer a introdução da dependência Polly no projeto C# |
| Implementação manual de Retry / Fallback | Sem dependências externas adicionais | Propenso a erros, código complexo para gerir jitter e limites de timeout concorrentes |

### Decisão

O Gateway C# foi integrado com o cliente gRPC do serviço de Pré-processamento:
1. Adicionados os pacotes `Grpc.Net.Client`, `Google.Protobuf` e `Polly` (v8) ao projeto `Gateway.csproj`.
2. Implementada uma pipeline de resiliência `ResiliencePipeline<NormalizedReading>` baseada em Polly com:
   - Política de Retry Exponencial (3 tentativas, factor de backoff com jitter, atraso inicial de 1 segundo).
   - Filtro de falhas de comunicação gRPC (`RpcException` com códigos de erro `Unavailable`, `DeadlineExceeded`, `Internal`).
   - Política de Timeout máximo de 5 segundos por pedido.
3. Antes de adicionar as leituras válidas de dados à fila local de agregação global (`leiturasPendentes`), o Gateway invoca o método de normalização do serviço gRPC. Se a resposta indicar que o dado é inválido ou se o serviço gRPC falhar consecutivamente, a leitura é rejeitada com os códigos de erro correspondentes (ex: `ERR_INVALID_DATA` ou `ERR_PREPROCESSING_FAILED`).

### Justificação

A biblioteca Polly é o padrão da indústria em .NET para tratamento de falhas transientes, garantindo robustez de forma declarativa e limpa. A validação gRPC pré-agregação previne dados inconsistentes de poluírem a base de dados do Servidor.

### Consequências

- ✅ Garantia de consistência física e qualidade de dados antes da agregação (ex: Fahrenheit convertido em Celsius).
- ✅ Resiliência e tolerância a falhas na comunicação inter-serviços.
- ✅ Separação clara: o Gateway delega a validação de intervalos físicos e conversão de unidades ao serviço especializado Python.

---

## DEC-006

**Título:** Integração do Cliente gRPC no Servidor para Serviço de Análise

**Componente:** `Servidor`

**Estado:** ✅ Aceite

**Data:** 2026-05-20

**Autores:** Equipa

### Contexto

Para apoiar a tomada de decisões de saúde pública ambiental na arquitetura "One Health", o Servidor Central deve conseguir requerer análises estatísticas históricas dos dados recolhidos (como médias móveis, regressão linear, outliers e previsões). O cálculo destas métricas foi delegado ao microsserviço especializado `AnalysisService` (Python) via gRPC, necessitando que o Servidor aja como cliente gRPC.

### Opções consideradas

| Opção | Prós | Contras |
|-------|------|---------|
| CLI Interativa e Integrada no Servidor C# | Facilidade de utilização directa pelos administradores do servidor, controlo total do fluxo no próprio terminal sem necessidade de APIs REST complexas para testes | Bloqueia temporariamente o input do terminal principal se não for assíncrono (mitigado com Threads dedicadas de leitura) |
| API Web Dedicada (ASP.NET Minimal API) | Facilidade de expansão para integração com frontends Web | Aumenta a complexidade de configuração e o consumo de recursos na Fase 1 |

### Decisão

O Servidor C# foi integrado com o cliente gRPC do serviço de Análise:
1. Referenciado o ficheiro `analysis.proto` no projeto `Servidor.csproj` gerando automaticamente os stubs do cliente gRPC em C# na compilação.
2. Inicializado o canal gRPC e o `AnalysisServiceClient` apontando para `http://localhost:50052` (configurável pela variável de ambiente `ANALYSIS_SERVICE_URL`).
3. Modificado o loop de leitura de input (`LerInput`) do Servidor para suportar comandos avançados:
   - `analisar`: permite submeter um pedido de análise interativamente (perguntando o tipo, zona, sensor opcional, data de início e fim) ou em linha única (`analisar <tipo> <zona> <sensorId> <dateFrom> <dateTo>`).
   - `prever`: permite submeter um pedido de previsão interativamente ou em linha única (`prever <tipo> <zona> <periodos>`).
   - `historico`: imprime a lista em memória de todas as análises efetuadas no ciclo de vida atual do processo.
   - `ajuda`: exibe a lista de comandos disponíveis.
4. Ao receber a resposta gRPC de análise, os dados são exibidos de forma formatada e guardados na lista em memória (`HistoricoAnalises`) e acrescentados cronologicamente no ficheiro de registo persistente local `analysis_history.log`.

### Justificação

Uma interface CLI enriquecida e interativa permite testar de forma rápida o pipeline completo gRPC (Servidor C# <-> Análise Python) em qualquer terminal, e o registo persistente local garante auditabilidade prévia à inclusão de bases de dados NoSQL complexas.

### Consequências

- ✅ Interoperabilidade gRPC validada e funcional entre C# (.NET 8) e Python.
- ✅ CLI de administração enriquecida no Servidor Central.
- ✅ Registo cronológico local de análises executadas para auditoria simples (`analysis_history.log`).

---

## DEC-007

**Título:** Instalação e Configuração do RabbitMQ

**Componente:** `Geral`

**Estado:** ✅ Aceite

**Data:** 2026-05-21

**Autores:** Equipa

### Contexto

Para implementar a segunda parte do trabalho prático (TP2), é necessário transitar de uma comunicação direta baseada em sockets TCP customizados entre Sensores e Gateways para um modelo de Publicação/Subscrição (Pub/Sub) utilizando RabbitMQ. O serviço RabbitMQ deve ser facilmente implantável, isolado e idempotente na criação de utilizadores, vhosts e permissões.

### Opções consideradas

| Opção | Prós | Contras |
|-------|------|---------|
| Docker Compose com configuração declarativa (definitions.json) | Configuração idempotente no arranque do contentor, portátil, sem necessidade de scripts pós-inicialização ou chamadas adicionais à API | Palavras-passe são especificadas no JSON em texto limpo para o import inicial (mitigado ao encriptar em produção) |
| Script bash / powershell com CLI `rabbitmqctl` | Flexibilidade total de scripting | Menos portátil, depende de o container estar em execução e saudável antes de correr o script, propenso a falhas temporais |
| Criação manual no Painel Web Admin | Sem complexidade de ficheiros adicionais | Não é idempotente, requer intervenção humana em cada novo deployment |

### Decisão

Adoptou-se a utilização do Docker Compose para orquestrar o RabbitMQ com a imagem `rabbitmq:3-management`. O carregamento das definições é feito de forma idempotente e nativa no arranque do serviço configurando:
1. Um ficheiro `rabbitmq.conf` mapeado que ativa o carregamento automático: `management.load_definitions = /etc/rabbitmq/definitions.json`.
2. Um ficheiro `definitions.json` contendo a declaração de utilizadores (`admin` e `guest`), virtual hosts (`/` e `onehealth`), e as respetivas permissões de leitura, escrita e configuração.

### Justificação

O uso do `definitions.json` garante um setup idempotente e imediato. Qualquer instância recém-criada do contentor terá a mesma configuração exata de utilizadores e vhosts, o que simplifica o desenvolvimento, a integração com futuros sensores e gateways, e evita erros manuais.

### Consequências

- ✅ Configuração idempotente e automatizada de todo o ambiente RabbitMQ.
- ✅ Acesso imediato à interface web de administração no arranque.
- ✅ Isolamento e facilidade de execução local através de contentores Docker.
- ⚠️ Necessidade de manter o ficheiro `definitions.json` seguro (credenciais em texto limpo no repositório de desenvolvimento).

---

## DEC-008

**Título:** Comunicação Pub/Sub baseada em Modelo de Tópicos (RabbitMQ)

**Componente:** `Sensor` | `Gateway` | `Protocolo`

**Estado:** ✅ Aceite

**Data:** 2026-05-21

**Autores:** Equipa

### Contexto

Para implementar a Fase 2 do TP2, foi necessário substituir o protocolo ponto-a-ponto baseado em sockets TCP customizados por uma arquitetura desacoplada e assíncrona baseada em filas de mensagens (Message Broker). O modelo escolhido deve permitir que múltiplos Gateways subscrevam de forma flexível a dados de sensores por área geográfica (Zona) e/ou tipo de sensor, de modo a processar e encaminhar a informação de forma independente.

### Opções consideradas

| Opção | Prós | Contras |
|-------|------|---------|
| RabbitMQ Topic Exchange (`sensors.exchange`) | Suporte nativo a filtragem avançada por tópicos com wildcards (`*` e `#`), robusto, persistência nativa (`delivery_mode=2`), suporte a múltiplas subscrições independentes por Gateway | Requer dependência de bibliotecas do RabbitMQ Client em C# e manutenção de infraestrutura do broker |
| RabbitMQ Fanout Exchange | Simples, sem overhead de routing keys | Todas as mensagens vão para todas as filas; impossibilita filtragem eficiente no broker |
| RabbitMQ Direct Exchange | Encaminhamento preciso sem wildcards | Inflexível; exige que cada Gateway saiba exatamente todos os IDs dos sensores individuais para fazer bind de cada fila |

### Decisão

Foi adotado o uso de um **Topic Exchange** denominado `sensors.exchange` no RabbitMQ para a comunicação entre Sensores e Gateways.
As decisões de design e implementação incluem:
1. **Routing Key**: Definida no formato `<zona>.<tipoSensor>.<idSensor>` (ex: `ZONA_CENTRO.TEMP.sensor-01`).
   - Para heartbeats e desconexões, o tipo de sensor é substituído por `HEARTBEAT` e `DISCONNECT` respetivamente (ex: `ZONA_CENTRO.HEARTBEAT.sensor-01`, `ZONA_CENTRO.DISCONNECT.sensor-01`).
2. **Mensagem**: Carga útil serializada em formato JSON, contendo os campos: `{ sensorId, zone, type, value, unit, timestamp, raw }`. As mensagens são enviadas com propriedade persistente (`delivery_mode=2`).
3. **Sensores**: Simulam localmente as respostas do protocolo TCP (`OK_CONNECTED`, `OK_TYPES_REGISTERED`) de modo a manter a CLI interativa e o fluxo de controlo intactos, mas publicam as leituras, heartbeats e desconexões reais no Topic Exchange do RabbitMQ. Streams de vídeo de alto débito mantêm-se por sockets TCP dedicados na porta `8081`.
4. **Gateways**: Criam uma fila exclusiva no arranque e efetuam bindings dinâmicos a partir das zonas registadas em `sensors.csv` utilizando o wildcard `<zona>.#` (ou padrões customizados passados via argumentos da linha de comandos). Consumidores assíncronos processam as mensagens de forma assíncrona desacoplada do broker.

### Justificação

O Topic Exchange do RabbitMQ oferece o equilíbrio perfeito entre flexibilidade e desempenho. A filtragem baseada em routing keys estruturadas permite que um Gateway subscreva apenas os dados da sua área geográfica de interesse de forma limpa e declarativa (sem processar leituras irrelevantes), em conformidade com o enunciado do TP2.

### Consequências

- ✅ Desacoplamento completo entre Sensores e Gateways (comunicação assíncrona).
- ✅ Filtragem robusta e eficiente do tráfego diretamente no Broker através de wildcards (`*` e `#`).
- ✅ Manutenção da compatibilidade com o pipeline gRPC de pré-processamento e normalização no Gateway.
- ✅ Vídeo streaming de alto débito preservado sobre sockets TCP para evitar overhead indevido no broker.
- ⚠️ Necessidade de tratamento idempotente de estados de conexão/desconexão e monitorização de heartbeats no Gateway devido ao caráter stateless da ligação Pub/Sub.

---

## DEC-009

**Título:** Configuração Centralizada e Resiliência do Sensor (.NET)

**Componente:** `Sensor`

**Estado:** ✅ Aceite

**Data:** 2026-05-21

**Autores:** Equipa

### Contexto

Na Fase 2 do TP2 (Tarefa 2.3), é necessário afastar os valores configurados manualmente no código-fonte e passar a definir as propriedades do sensor (como ID, Zona, Tipo, Intervalo de publicação) e detalhes do broker RabbitMQ de forma externa (via `appsettings.json`). Adicionalmente, exige-se que o Sensor seja resiliente a falhas temporárias do broker de mensagens RabbitMQ, recuperando a ligação automaticamente com uma estratégia de retry baseada em backoff exponencial.

### Opções consideradas

| Opção | Prós | Contras |
|-------|------|---------|
| Configuração baseada em JSON (`appsettings.json`) e reconexão manual com backoff exponencial no Sensor C# | Padronizado no ecossistema .NET, fácil de modificar pelo utilizador final. A lógica de reconexão manual dá controlo fino sobre o ciclo de tentativas e backoff. | Introdução de lógica extra no cliente para controlo do estado do broker. |
| Variáveis de Ambiente | Fácil de configurar em ambientes Docker | Menos intuitivo para execução interativa direta em ambientes locais Windows sem scripts adicionais. |
| Sem reconexão (Terminar em caso de falha) | Código extremamente simples e curto | Falta de resiliência exigida; qualquer falha temporária do RabbitMQ crasharia permanentemente o Sensor. |

### Decisão

Adoptou-se a utilização de:
1. Configuração via `appsettings.json` utilizando os pacotes `Microsoft.Extensions.Configuration` e `Microsoft.Extensions.Configuration.Json`.
2. Implementação do método thread-safe `EnsureConnection()` no `SensorClient` que gere o estado de ligação ao RabbitMQ.
3. Se a ligação falhar ou for perdida, ativa-se um ciclo de reconexão que utiliza atraso com backoff exponencial (iniciando em 2 segundos e duplicando sucessivamente até ao limite máximo de 30 segundos).
4. O loop de publicação contínua (`StartAutomaticPublishing`) e o envio automático de heartbeats verificam a ligação a cada envio, bloqueando a escrita de mensagens de dados até que a ligação seja restabelecida.

### Justificação

O uso de `appsettings.json` está totalmente alinhado com o padrão moderno de configuração do .NET. O backoff exponencial evita sobrecarregar o broker (congestionamento) quando este está a reiniciar ou a passar por instabilidade, ao mesmo tempo que garante que os sensores recuperam o envio de leituras de forma totalmente transparente e autónoma.

### Consequências

- ✅ Centralização das configurações do Sensor e RabbitMQ num ficheiro estruturado.
- ✅ Autonomia completa do Sensor em caso de falhas temporárias do Broker RabbitMQ.
- ✅ Prevenção de perda permanente de ligação.
- ⚠️ Durante a quebra da ligação, as mensagens de dados não publicadas podem perder-se se o buffer local não as reter (aceitável dado que não há requisito de buffer local persistente na memória do sensor).

---

## DEC-010

**Título:** Integração do Consumidor RabbitMQ e Resiliência no Gateway

**Componente:** `Gateway`

**Estado:** ✅ Aceite

**Data:** 2026-05-21

**Autores:** Equipa

### Contexto

Para o desenvolvimento da Fase 2 do TP2 (Tarefa 2.4), o Gateway deve atuar como consumidor assíncrono das leituras publicadas pelos sensores no RabbitMQ. Exige-se que as configurações de ligação (host, porta, credenciais, exchange e os padrões de bindings) sejam extraídas para o ficheiro `appsettings.json`, e que o consumo das mensagens seja resiliente contra falhas temporárias (por exemplo, quando o serviço de pré-processamento gRPC em Python se encontra inalcançável).

### Opções consideradas

| Opção | Prós | Contras |
|-------|------|---------|
| Confirmação Manual (manual ACK/NACK) e Filtro de Poison Messages | Total resiliência das mensagens. Se o gRPC estiver em baixo, mensagens são mantidas na fila com requeue. Se a mensagem for inválida/malformada (poison message), é descartada imediatamente para evitar loops infinitos. | Complexidade adicional no controlo de erros e requeue manual na receção do broker. |
| Auto-ACK (Confirmação automática no broker) | Implementação extremamente simples e curta no Gateway. | Perda imediata de mensagens em caso de indisponibilidade temporária do serviço de pré-processamento gRPC ou quebras inesperadas no Gateway. |

### Decisão

Adoptou-se a utilização de **Confirmação Manual** (`autoAck: false`) no `EventingBasicConsumer` do RabbitMQ e a reestruturação da lógica de consumo do Gateway:
1. **appsettings.json**: Configurações gerais e padrões de bindings. Se a secção de bindings estiver vazia, o Gateway faz fallback para obter as zonas ativas a partir de `sensors.csv`.
2. **ACK Manual**: Enviado ao broker quando a mensagem é processada com sucesso ou quando é classificada como lixo ("poison message" — JSON inválido, falta de `sensorId` ou leituras que falham fisicamente na validação).
3. **NACK com Requeue**: Enviado ao broker com `requeue: true` se o serviço gRPC Preprocessing estiver offline (retornando `null` após a pipeline de retentativas Polly). Para evitar consumo e loops CPU demasiado agressivos durante a inatividade do gRPC, introduz-se um atraso de 1 segundo antes do requeue no handler do consumidor.

### Justificação

A confirmação manual garante que nenhuma leitura de sensor válida se perde quando ocorrem falhas temporárias de comunicação entre o Gateway e o microsserviço de pré-processamento gRPC. O descarte de poison messages assegura que o sistema não entra em ciclos infinitos de consumo da mesma mensagem defeituosa.

### Consequências

- ✅ Tolerância a falhas na rede/microsserviços com garantia de entrega (at least once).
- ✅ Prevenção de loops infinitos causados por mensagens corruptas (poison messages).
- ✅ Configuração centralizada e modularizada, compatível com execução CLI legada.

---

## DEC-011

**Título:** Arquitetura Multi-Gateway e Desacoplamento de Tópicos

**Componente:** `Geral`

**Estado:** ✅ Aceite

**Data:** 2026-05-21

**Autores:** Equipa

### Contexto

O sistema precisa de demonstrar o arranque e execução concorrente de dois ou mais Gateways com bindings/subscrições distintos no RabbitMQ (por exemplo, Gateway A subscreve `ZONA_CENTRO.#` e Gateway B subscreve `ZONA_ESCOLAR.#`). Deve ser demonstrado o desacoplamento de tópicos (os sensores publicam sem conhecimento prévio ou dependência de qual Gateway ou quantos Gateways estão a escutar). Adicionalmente, múltiplos Gateways a correr em simultâneo na mesma máquina física partilham recursos, especificamente ficheiros locais (`sensors.csv` e `video_metadata.log`), o que pode originar condições de corrida e bloqueio de ficheiros (file locks).

### Opções consideradas

| Opção | Prós | Contras |
|-------|------|---------|
| Opção A (Sem controlo de concorrência) | Sem código adicional. | Erros intermitentes de "File in use by another process" ao atualizar `sensors.csv` e `video_metadata.log`. |
| Opção B (Mutexes nomeados do SO) | Resolução robusta de concorrência inter-processos em Windows/Linux, sem colisões de escrita. | Pequeno overhead de espera bloqueante ao aceder a ficheiros. |

### Decisão

Implementou-se o suporte a múltiplos Gateways através de:
1. **Passagem de Parâmetros pela CLI**: Possibilidade de indicar ID do Gateway, IP/Porta do Servidor, Porta de Vídeo exclusiva (para evitar conflitos de porta socket TCP de vídeo stream, p.ex. `8081` vs `8082`) e Routing Keys de binding por parâmetro.
2. **Mutexes Nomeados**: Proteção de escritas concorrentes em `sensors.csv` usando o Mutex `GatewayConfigMutex` no `SensorConfigManager`, e em `video_metadata.log` usando o Mutex `GatewayVideoLogMutex` no processamento de vídeo.
3. **Bindings Distintos no Broker**: Demonstração de execução com o Gateway A (vinculado a `ZONA_CENTRO.#`) e o Gateway B (vinculado a `ZONA_ESCOLAR.#`), comprovando o isolamento. Os sensores enviam as mensagens para a `sensors.exchange` usando routing keys apropriadas e o broker faz a distribuição correta.

### Justificação

Os Mutexes Nomeados oferecem o mecanismo de sincronização necessário para processos independentes no mesmo sistema operativo. O isolamento de portas TCP e de filas RabbitMQ exclusivas permite que os Gateways operem concorrentemente sem conflitos de rede ou de brokers.

### Consequências

- ✅ Execução concorrente fiável de múltiplos Gateways sem conflitos de socket port ou colisões de escrita em ficheiro.
- ✅ Desacoplamento perfeito: os sensores publicam apenas para o exchange, desconhecendo a topologia ou número de Gateways subscritores.
- ⚠️ Gateways concorrentes na mesma máquina necessitam de portas TCP de stream de vídeo distintas.



