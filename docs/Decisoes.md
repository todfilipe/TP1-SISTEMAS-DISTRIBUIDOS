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
