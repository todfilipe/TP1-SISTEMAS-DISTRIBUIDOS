TP2 SD - Roadmap

TP2 ù Sistemas DistribuÝdos
Serviþos de Anßlise e MonitorizaþÒo Urbana para One Health

Roadmap de Desenvolvimento

(com justificaþÒo de decis§es arquiteturais)

Disciplina

Docentes

Data limite

Sistemas DistribuÝdos ù UTAD | ECT | DE 2025/2026

Hugo Paredes | Tiago Pinto | Cristiano PendÒo

29 de maio de 2026 (apresentaþÒo na PL seguinte)

Stack escolhida

.NET 8/9 (C#) + Python 3.11 + RabbitMQ + MongoDB + gRPC

Entrega

Moodle ù relat¾rio (4 pßgs) + c¾digo (GitHub/GitLab)

Nota sobre os critÚrios de avaliaþÒo

Ap¾s conversa com o docente, fica claro que um dos critÚrios principais de avaliaþÒo Ú a
capacidade de identificar as melhores ferramentas para cada problema e justificar as escolhas.
Este roadmap foi estruturado com isso em mente: cada decisÒo tecnol¾gica Ú acompanhada de (a)
alternativas consideradas, (b) trade-offs avaliados, e (c) justificaþÒo concreta no contexto do TP2. Hß
ainda um apÛndice final com frases prontas para o relat¾rio e respostas defensivas para perguntas
que possam surgir na apresentaþÒo.

1. VisÒo Geral

O TP2 evolui a infraestrutura monolÝtica do TP1 (sockets TCP + protocolo binßrio manual + SQLite)
para uma arquitetura distribuÝda desacoplada, introduzindo trÛs grandes alteraþ§es:

1.  SubstituiþÒo da comunicaþÒo direta Sensor?Gateway por um padrÒo Publish/Subscribe

baseado em RabbitMQ.

2.  ExternalizaþÒo de PrÚ-Processamento e Anßlise para serviþos invocados via RPC (gRPC), em

linguagem distinta (Python), demonstrando interoperabilidade.

3.  PersistÛncia em base de dados NoSQL (MongoDB) e interface de visualizaþÒo para

parametrizaþÒo de anßlises.

Diagrama l¾gico do sistema

[Sensores] --PUB--> [RabbitMQ (topic)] --SUB--> [Gateway(s)] --gRPC--> [PrÚ-Proc
Python]

                                                  |

                                                  v

                                              [Servidor .NET] <--gRPC--> [Anßlise
Python]

Pagina 1 de 15

TP2 SD - Roadmap

                                                  |

                                                  v

                                              [MongoDB] ---> [Interface CLI/Web]

PrincÝpios orientadores

ò  Desacoplamento ù publisher nÒo conhece subscriber; cliente RPC nÒo conhece localizaþÒo

do servidor.

ò  Heterogeneidade ù m·ltiplas linguagens e tecnologias colaboram via contratos bem

definidos (Proto, JSON).

ò  Escalabilidade horizontal ù m·ltiplos Gateways podem coexistir, cada um com

responsabilidades distintas.

ò  PersistÛncia e auditoria ù dados e anßlises sobrevivem a reinÝcio de qualquer componente.

2. Stack Tecnol¾gica

A stack mantÚm continuidade com o TP1 (.NET para Sensores/Gateways/Servidor) e introduz Python
para os serviþos RPC e tecnologias distribuÝdas standard da ind·stria.

Componente

Tecnologia

Sensor

Gateway

Servidor

.NET 8 (C#) ù reutiliza simulador do TP1; usa RabbitMQ.Client para
publicaþÒo.

.NET 9 (C#) ù subscritor RabbitMQ, cliente gRPC do PrÚ-
Processamento.

.NET 8 (C#) ù recetor de dados agregados, cliente gRPC da Anßlise,
persiste em MongoDB.

PrÚ-Processamento

Python 3.11 + grpcio ù normalizaþÒo de escalas/formatos.

Anßlise / PrevisÒo

Python 3.11 + grpcio + pandas + scikit-learn ù estatÝstica e modelos.

Pub/Sub

BD

Interface

RabbitMQ 3.13 (Docker) ù exchange topic com routing key
hierßrquica.

MongoDB 7 (Docker) ù coleþ§es readings, analyses,
sensors_metadata.

CLI em .NET (Spectre.Console); Web em ASP.NET Core Razor Pages
(opcional).

ContainerizaþÒo

Docker Compose ù orquestra brokers, BD e serviþos Python.

3. Decis§es Arquiteturais e Justificaþ§es

Esta secþÒo documenta as cinco decis§es tecnol¾gicas estruturantes do projeto, comparando
alternativas e justificando a escolha no contexto especÝfico do TP2.

3.1 ù RPC: gRPC vs alternativas

O protocolo nÒo fixa a tecnologia de RPC. Foram avaliadas trÛs alternativas:

Pagina 2 de 15

TP2 SD - Roadmap

Tecnologia

Pontos fortes

Pontos fracos

Veredicto

gRPC

Multi-linguagem;
HTTP/2; contratos
.proto tipados;
streams; ecossistema
maduro.

Curva de
aprendizagem do
.proto; debug binßrio
menos trivial.

Escolhida ù alinha-se com
Python+.NET, contratos
explÝcitos = boa nota tÚcnica.

JSON-RPC over
HTTP

Simples; debug fßcil;
sem ferramentas
extra.

Sem tipagem forte;
sem streams;
performance inferior.

Descartada ù demasiado
pr¾xima de uma API REST,
perde valor demonstrativo.

Java RMI / .NET
Remoting

IntegraþÒo nativa.

Descartada ù bloqueia o uso
de Python (valorizaþÒo).

Mono-linguagem
(Java?Java ou
.NET?.NET);
ferramentas em fim
de vida.

XML-RPC

RabbitMQ RPC
pattern

Standard antigo;
simples.

XML pesado; pouco
uso atual.

Descartada ù anacr¾nica.

Reaproveita o broker.  Mistura

responsabilidades
(broker vira
sÝncrono); dificulta o
ensino.

Descartada ù confunde os
dois padr§es do protocolo.

"O gRPC foi escolhido por suportar nativamente as duas linguagens da nossa stack (C# e Python),
por permitir a definiþÒo formal dos contratos em ficheiros .proto que tornam o protocolo de
comunicaþÒo explÝcito e versionßvel, e pela eficiÛncia do transporte HTTP/2 binßrio, adequado a
fluxos de dados de telemetria."

3.2 ù Pub/Sub: estrutura de routing keys

O protocolo permite t¾picos "por tipo de dado", "por zona" ou ambos. Foram avaliadas trÛs
modelaþ§es:

ModelaþÒo

S¾ por tipo

Routing key

Anßlise

pm25, temperatura, à

S¾ por zona

vilareal, porto, à

Hierßrquica
(escolhida)

zona.tipo.idSensor

Exemplo de bindings com a OpþÒo C

Simples mas forþa cada Gateway a tratar todas as
zonas de um tipo. NÒo permite especializaþÒo
geogrßfica.

Cada Gateway gere uma zona mas mistura todos
os tipos. NÒo permite especializaþÒo funcional.

Aproveita o topic exchange com wildcards (*, #).
Permite subscriþÒo por zona (vilareal.#), por tipo
(*.pm25.#), ou combinaþÒo. Cumpre literalmente
o "e/ou" do enunciado.

Pagina 3 de 15

TP2 SD - Roadmap

Gateway

GW-VilaReal

Binding

vilareal.#

Subscreve

Tudo da zona de Vila Real

GW-PoluiþÒo

*.pm25.# + *.no2.#

PM2.5 e NO? de todas as zonas

GW-Porto-Ar

porto.pm25.#

S¾ PM2.5 do Porto

GW-Master

#

Tudo (auditoria/debug)

"Optou-se por uma routing key hierßrquica zona.tipo.idSensor sobre um topic exchange do
RabbitMQ. Esta soluþÒo evidencia o desacoplamento Sensor?Gateway (os Sensores publicam
sem conhecer os Gateways) e aproveita os wildcards * e # para permitir m·ltiplos perfis de
Gateway sem alteraþ§es no c¾digo dos publishers ù bastando reconfigurar bindings."

3.3 ù Base de Dados: MongoDB vs PostgreSQL

O protocolo permite "relacional ou NoSQL". Foram avaliadas as duas alternativas dominantes:

CritÚrio

MongoDB (escolhida)

PostgreSQL

Heterogeneidade de
dados

Documentos com schemas flexÝveis
ù cada tipo de sensor tem o seu
shape.

Tabela genÚrica (EAV) ou m·ltiplas
tabelas ù perde tipagem ou
multiplica esquemas.

IngestÒo write-
intensive

Fluxo de dados

Time-series

Queries tÝpicas

Relaþ§es fortes

Otimizado para writes; sharding
nativo.

Boa, mas requer mais cuidado com
Ýndices/locking.

JSON-nativo ù alinha com
RabbitMQ + gRPC + interface.

Requer parsing/mapping ORM em
cada step.

Coleþ§es time-series nativas
(Mongo 5+).

Requer extensÒo TimescaleDB.

Filtros por sensor+intervalo: Ýndice
{sensorId:1, ts:-1} + agregaþ§es.

WINDOW functions poderosas, mas
overhead para casos simples.

Pouco eficiente ù mas o domÝnio
quase nÒo tem.

Forte ù vantagem nÒo aplicßvel aqui.

"Optou-se por MongoDB pela natureza heterogÚnea dos dados produzidos pelos sensores, pela
ingestÒo write-intensive caracterÝstica de cenßrios IoT, e pelo alinhamento com o fluxo JSON jß
existente entre RabbitMQ, gRPC e a interface. A ausÛncia de relaþ§es fortes no domÝnio elimina a
vantagem clßssica das bases relacionais."

3.4 ù Linguagem dos serviþos RPC: Python

O protocolo valoriza heterogeneidade tecnol¾gica. A escolha entre manter tudo em .NET ou
introduzir Python foi avaliada assim:

RazÒo

JustificaþÒo

Anßlise de dados

Python tem o ecossistema dominante (pandas, numpy, scikit-learn,
statsmodels). Implementar regressÒo/outliers em C# seria reinventar a
roda.

Pagina 4 de 15

TP2 SD - Roadmap

RazÒo

JustificaþÒo

Heterogeneidade

O protocolo refere explicitamente a inclusÒo de "diferentes tecnologias e
linguagens" como fator de valorizaþÒo.

DemonstraþÒo de
interoperabilidade

gRPC entre C# e Python valida o argumento dos contratos .proto cross-
language.

Comunidade

Tutoriais e exemplos acadÚmicos abundantes para gRPC+Python.

PrÚ-Proc + Anßlise em
Python

DecisÒo tomada por consistÛncia: ambos os serviþos partilham bibliotecas
e padrÒo; mais fßcil de manter.

3.5 ù OrquestraþÒo: Docker Compose

A montagem manual de RabbitMQ + MongoDB + dois serviþos Python Ú frßgil e dificulta a
reproduþÒo. Docker Compose foi escolhido por:

ò  Um ·nico comando (docker compose up) arranca toda a infraestrutura ù facilita avaliaþÒo.
ò  Vers§es fixas (rabbitmq:3.13, mongo:7) ù elimina "works on my machine".
ò  Healthchecks asseguram ordem de arranque (Servidor s¾ inicia ap¾s Mongo estar pronto).
ò  Volumes nomeados garantem persistÛncia entre restarts.
ò  ╔ o padrÒo de mercado para prot¾tipos distribuÝdos ù boa demonstraþÒo tÚcnica.

3.6 ù Decis§es Operacionais e de ┬mbito

Para alÚm das decis§es tecnol¾gicas estruturantes (3.1 a 3.5), o grupo tomou ù ap¾s reflexÒo e
discussÒo informada com o docente ù um conjunto de decis§es operacionais que delimitam o
Ômbito do projeto. O docente indicou explicitamente que estas decis§es devem ser tomadas pelo
grupo, justificadas, e defendidas na apresentaþÒo.

Tema

DecisÒo

JustificaþÒo para defender

ReutilizaþÒo do
TP1

RefatoraþÒo mÝnima: preservar
simulaþÒo e agregaþÒo; eliminar
sockets diretos; substituir apenas
os pontos de comunicaþÒo.

O protocolo do TP2 exige "manter
funcionalidades anteriores". Reduz risco e
preserva trabalho jß validado. Permite focar
esforþo no que Ú novo (Pub/Sub, RPC, BD).

Profundidade da
Anßlise

EstatÝstica descritiva (mÚdia,
desvio, mediana, percentis) +
outliers (z-score) + tendÛncia
(regressÒo linear) + previsÒo
(EWMA). Sem redes neuronais.

Cobre os exemplos explÝcitos do enunciado
("estatÝstica, deteþÒo de padr§es, previsÒo de
riscos"). ML profundo distrairia do Ômbito SD;
demonstra mesmo assim uso do ecossistema
Python (pandas, numpy, scikit-learn).

Formatos no
PrÚ-
Processamento

Suportar os trÛs formatos
referidos no enunciado: JSON,
XML e CSV. Cada Sensor pode
publicar num formato distinto.

O protocolo refere-os explicitamente.
Demonstrar os 3 prova generalidade do
mecanismo de normalizaþÒo ù diferencia do
TP1 que s¾ usava formato binßrio fixo.

Interface

CLI (Spectre.Console) obrigat¾ria
e completa; Web (ASP.NET
Razor) opcional como valorizaþÒo
caso o cronograma permita.

CLI Ú mais demo-friendly em apresentaþÒo ao
vivo (headless, scriptßvel, sem dependÛncia
de browser). Prioriza-se uma CLI funcional
sobre uma Web meia-construÝda.

Pagina 5 de 15

TP2 SD - Roadmap

Tema

DecisÒo

JustificaþÒo para defender

Multi-linguagem

.NET
(Sensores/Gateways/Servidor) +
Python (PrÚ-Proc e Anßlise) + 1
Sensor em Node.js como extra
de valorizaþÒo.

Duas linguagens jß cumprem o requisito do
enunciado; um ·nico Sensor em Node.js (com
amqplib) acrescenta uma terceira a custo
baixo e demonstra interoperabilidade real
entre 3 stacks distintas no mesmo broker.

Grupo (3
membros)

ApresentaþÒo

EstratÚgia de
avaliaþÒo

Escalabilidade
da demo

PersistÛncia e
resiliÛncia

Seguranþa

DivisÒo por fase: M-
A=Sensores+RabbitMQ+Pub/Sub;
M-B=Serviþos Python+gRPC; M-
C=Servidor+MongoDB+CLI.
Docker/relat¾rio/testes
partilhados.

?15 minutos: ?5 slides
(arquitetura, decis§es com
tabelas comparativas, liþ§es
aprendidas) + demo ao vivo (?5
min) + Q&A. Todos os membros
intervÛm.

Quatro frentes paralelas:
relat¾rio com tabelas
comparativas + c¾digo limpo e
comentado + demo robusta com
docker compose up + Q&A
defensivo preparado (ver
ApÛndice B).

Demo ao vivo: 2û3 Gateways +
4û6 Sensores em paralelo. Teste
de carga separado: simulaþÒo de
100 sensores.

Mensagens RabbitMQ durable +
ack manual +
AutomaticRecoveryEnabled no
.NET client + retry com Polly no
gRPC.

Credenciais via varißveis de
ambiente; utilizador dedicado
em RabbitMQ e MongoDB (nÒo
usar guest/root). Sem TLS no
MVP.

Paraleliza trabalho (?1 semana/fase mas em
paralelo); cada membro domina uma fase e
apresenta-a; pair-review cruzado entre
membros garante qualidade e conhecimento
partilhado.

Formato tÝpico de aula PL. Foco mßximo em
demo e justificaþ§es ù que Ú o critÚrio
avaliado. Slides curtos evitam leitura passiva e
libertam tempo para Q&A.

Distribui o esforþo pelos vetores avaliados;
nenhum fica fraco. A capacidade de defender
as escolhas Ú cobrada explicitamente pelo
docente.

M·ltiplos componentes em paralelo provam
escalabilidade real (nÒo s¾ te¾rica) e validam
a routing key hierßrquica (3.2). 100 sensores
no teste de carga validam throughput sem
encher o ecrÒ na demo.

One Health Ú monitorizaþÒo contÝnua ù
perder leituras Ú inaceitßvel. Custo de
implementaþÒo mÝnimo, ganho grande em
narrativa de robustez para o relat¾rio.

Boa prßtica mÝnima sem complexidade. TLS
exigiria gestÒo de certificados e CA ù
desproporcional ao Ômbito do TP2, que nÒo
menciona seguranþa como requisito.

4. Faseamento do Trabalho

O desenvolvimento segue as 3 fases propostas no protocolo, com sub-tarefas, critÚrios de aceitaþÒo
claros, e ù para cada fase ù uma subsecþÒo de "Alternativas consideradas" que reforþa a defesa
das escolhas perante o docente.

Pagina 6 de 15

TP2 SD - Roadmap

FASE 1  ù ImplementaþÒo das chamadas RPC (gRPC)
DuraþÒo estimada: ? 1 semana

Objetivo: implementar comunicaþÒo RPC entre Gateway?PrÚ-Proc e Servidor?Anßlise, mantendo
temporariamente a comunicaþÒo por sockets do TP1 entre Sensor?Gateway. Permite validar o RPC
isoladamente.

Tarefa 1.1 ù Definir contratos .proto

ò  Criar diretoria proto/ na raiz do reposit¾rio (partilhada entre projetos).
ò  preprocessing.proto: rpc Normalize(RawReading) returns (NormalizedReading); inclui

sensorId, type, value, unit, timestamp, rawFormat (JSON/XML/CSV).
ò  analysis.proto: rpc Analyze(AnalysisRequest) returns (AnalysisResult); rpc

Predict(PredictionRequest) returns (PredictionResult); requests parametrizßveis por janela
temporal, tipo, sensor.

ò  Gerar stubs C# via Grpc.Tools no .csproj; stubs Python via python -m grpc_tools.protoc.
ò  Versionar os .proto desde o inÝcio ù qualquer alteraþÒo afeta ambos os lados.

Tarefa 1.2 ù Serviþo de PrÚ-Processamento (Python)

ò  Criar services/preprocessing/ com servidor gRPC.
ò
ò  Parsing de JSON, XML (xml.etree) e CSV (csv stdlib) ù demonstrar os 3 formatos do

Implementar conversÒo de escalas (Fahrenheit?Celsius, Pa?hPa).

enunciado.

ò  ValidaþÒo de ranges (descartar leituras impossÝveis: PM2.5 negativo, temperatura > 80║C).
ò  Porta 50051. Dockerfile com Python 3.11-slim. Healthcheck.

Tarefa 1.3 ù Serviþo de Anßlise (Python)

services/analysis/ com servidor gRPC, biblioteca pandas + numpy.

ò
ò  Anßlises base: mÚdia, desvio padrÒo, mediana, percentis sobre janela temporal.
ò  DeteþÒo de outliers via z-score (limiar configurßvel).
ò  Anßlise de tendÛncia (slope de regressÒo linear simples).
ò  PrevisÒo simples: extrapolaþÒo linear ou mÚdia m¾vel exponencial (EWMA) sobre N pontos

futuros.

ò  Porta 50052. Dockerfile, healthcheck.

Tarefa 1.4 ù Cliente gRPC no Gateway (C#)

ò  Adicionar Grpc.Net.Client, Google.Protobuf, Grpc.Tools ao Gateway.csproj.
ò  Configurar canal persistente (GrpcChannel.ForAddress("http://preprocessing:50051")) ù

reutilizßvel.

ò  Antes de agregar/encaminhar, chamar Normalize() para cada leitura.
ò  Polly para retry exponencial em caso de falha do serviþo.

Tarefa 1.5 ù Cliente gRPC no Servidor (C#)

ò  Expor endpoint local (CLI ou API) que recebe pedidos de anßlise parametrizada.
ò
Invocar AnalysisService.Analyze() com janela temporal, tipo, sensor.
ò  Para jß, armazenar resultado em mem¾ria (MongoDB chega na Fase 3).

Alternativas consideradas e descartadas

Pagina 7 de 15

TP2 SD - Roadmap

ò  REST/JSON-RPC em vez de gRPC ù descartado por nÒo ter contratos fortes e perder valor

demonstrativo (jß visto em outras disciplinas).

ò  ComunicaþÒo sÝncrona pelo pr¾prio RabbitMQ (RPC pattern) ù descartado por confundir os

dois padr§es e dificultar a separaþÒo conceptual no relat¾rio.

ò  Tudo em .NET (sem Python) ù descartado por nÒo aproveitar a valorizaþÒo por

heterogeneidade nem o ecossistema de anßlise.

CritÚrios de aceitaþÒo ù Fase 1

ò  Stubs gerados a partir dos .proto compilam em ambas as linguagens sem warnings.
ò  ╔ possÝvel iniciar serviþos Python (docker compose up preprocessing analysis) e clientes C#

invocam funþ§es remotas com sucesso.
Logs cross-language coerentes (sensorId aparece nas duas pontas).
LatÛncia tÝpica < 50ms em localhost para uma chamada de Normalize().

ò
ò

FASE 2  ù ComunicaþÒo Pub/Sub entre Sensores e Gateways
DuraþÒo estimada: ? 1 semana

Objetivo: substituir os sockets diretos Sensor?Gateway por mensagens via RabbitMQ usando a
estrutura hierßrquica decidida em 3.2.

Tarefa 2.1 ù Configurar RabbitMQ

ò  Adicionar rabbitmq:3.13-management ao docker-compose.yml (portas 5672 e 15672).
ò  definitions.json idempotente: cria utilizador, vhost, exchange sensors.exchange (type=topic,

durable=true).

ò  Validar painel web em http://localhost:15672.

Tarefa 2.2 ù Modelo de t¾picos (decisÒo em 3.2)

ò  Exchange: sensors.exchange  (type: topic, durable: true)
ò  Routing key: <zona>.<tipo>.<idSensor>  (ex: vilareal.pm25.s07)
ò  Mensagem (JSON): { sensorId, zone, type, value, unit, timestamp, rawFormat

}

ò  Delivery mode: 2 (persistente)

Tarefa 2.3 ù Publisher no Sensor (.NET)

ò  Adicionar RabbitMQ.Client ao Sensor.csproj.
ò  Remover socket TCP do TP1. Substituir por BasicPublish() no sensors.exchange.
ò  Configurar via appsettings.json: host, porta, credenciais, zona, tipoSensor, idSensor,

intervaloPublicacao.

ò  ReconexÒo automßtica com backoff exponencial (AutomaticRecoveryEnabled = true do .NET

client).

ò  Suportar diferentes formatos de payload (JSON/XML/CSV) ù campo rawFormat indica o

formato bruto antes do PrÚ-Proc.

Tarefa 2.4 ù Subscriber no Gateway (.NET)

ò  Remover servidor TCP. Adicionar AsyncEventingBasicConsumer.
ò  Configurar bindings por zona/tipo via appsettings.json (lista de routing patterns).
ò  Para cada mensagem: invocar prÚ-processamento (gRPC) ? agregar ? encaminhar para

Servidor.

Pagina 8 de 15

TP2 SD - Roadmap

ò  Ack manual ap¾s sucesso; nack + requeue em caso de erro transit¾rio; descartar (sem

requeue) em erro permanente.

Tarefa 2.5 ù M·ltiplos Gateways

ò  Demonstrar arranque de ? 2 Gateways com bindings distintos (ex: GW-VilaReal vs GW-

PoluiþÒo).

ò  Validar pela visualizaþÒo das queues no painel RabbitMQ (mensagens distribuÝdas conforme

bindings).

Alternativas consideradas e descartadas

ò  Apache Kafka ù overkill para o Ômbito do TP2; arranque mais pesado; menos didßtico para

Pub/Sub clßssico.

ò  Redis Pub/Sub ù nÒo tem mensagens persistentes (fire-and-forget); inadequado para

auditoria/recuperaþÒo.

ò  MQTT ù apropriado para IoT-puro mas distancia-se do Ômbito "distribuÝdo urbano" do

protocolo, que pede RabbitMQ explicitamente.

ò  Direct exchange ù perderia a flexibilidade dos wildcards; force-fit do modelo "s¾ por tipo"

ou "s¾ por zona".

CritÚrios de aceitaþÒo ù Fase 2

ò  Os sensores publicam sem conhecer os gateways (zero referÛncias a IPs/portos de

gateways).

ò  Adicionar/remover Gateways em runtime nÒo exige alterar sensores.
ò  Mensagens persistentes (sobrevivem a restart do broker) ù validar parando e arrancando o

container.

ò  Painel RabbitMQ mostra exchange, queues e taxa de throughput coerentes.

FASE 3  ù Funcionalidades Adicionais ù BD + Interface
DuraþÒo estimada: ? 1 semana

Objetivo: persistir leituras agregadas e resultados de anßlises em MongoDB; oferecer interface para
consulta e despoletamento de novas anßlises.

Tarefa 3.1 ù Configurar MongoDB

ò  mongo:7 no docker-compose.yml (porta 27017). Volume nomeado mongo_data.
ò  Coleþ§es: readings (leituras agregadas), analyses (resultados), sensors_metadata.
ò
ò  Avaliar uso de coleþÒo time-series para readings (Mongo 5+).

═ndices: {sensorId: 1, timestamp: -1} em readings; {type: 1, createdAt: -1} em analyses.

Tarefa 3.2 ù Camada de acesso a dados (.NET)

ò  Adicionar MongoDB.Driver ao Servidor.csproj.
ò  Repositories/{ReadingsRepository,AnalysesRepository}.cs com operaþ§es CRUD assÝncronas.
ò  Definir POCOs com [BsonElement] para mapeamento limpo (sem Document genÚrico).
ò  Connection string em appsettings.json; user/password via varißveis de ambiente.

Tarefa 3.3 ù PersistÛncia no fluxo principal

ò  No Servidor: gravar cada batch agregado recebido dos Gateways em readings.
ò  Ap¾s cada anßlise (via gRPC), gravar resultado em analyses com referÛncia aos parÔmetros.

Pagina 9 de 15

TP2 SD - Roadmap

ò  Validar consistÛncia: o que se lÛ via interface Ú exatamente o que foi publicado pelos

sensores (rastreabilidade).

Tarefa 3.4 ù Interface CLI (Spectre.Console)

ò  Comando list-readings --sensor X --from Y --to Z ù tabela colorida com filtros.
ò  Comando analyze --type pm25 --zone vilareal --from Y --to Z ù dispara anßlise via gRPC,

mostra resultado e guarda.

ò  Comando list-analyses ù hist¾rico de anßlises com IDs e parÔmetros.
ò  Comando show-sensor --id X ù mostra metadados e ·ltimas N leituras.

Tarefa 3.5 ù Interface Web (opcional, valorizaþÒo)

ò  ASP.NET Core Razor Pages ù porta 5000.
ò  Pßgina /readings com filtros e paginaþÒo; /analyses; /new-analysis com formulßrio.
ò  Grßficos com Chart.js para sÚries temporais.

Alternativas consideradas e descartadas

ò  PostgreSQL ù descartado pela anßlise em 3.3 (heterogeneidade, write-intensive,

alinhamento JSON).

ò  SQLite (continuidade com TP1) ù descartado por nÒo ser servidor de BD (single-file nÒo

ò

encaixa em sistema distribuÝdo).
Interface s¾ Web (sem CLI) ù descartado por CLI ser mais demonstrativa em apresentaþÒo
ao vivo e mais simples para demo headless.

CritÚrios de aceitaþÒo ù Fase 3

ò  Dados sobrevivem a restart de todos os componentes (verificar via docker compose down

&& up).

ò  Consultas com filtros temporais devolvem em < 1s para 100k leituras.
ò
Interface permite parametrizar e visualizar anßlises sem editar c¾digo.

5. Tarefas Transversais

Estas tarefas correm em paralelo com as trÛs fases e sÒo fundamentais para a qualidade da entrega.

5.1 ù Reposit¾rio e gestÒo de issues

Issue por sub-tarefa do roadmap; labels (fase-1, fase-2, fase-3, docs, infra).

ò  Criar reposit¾rio no GitHub/GitLab no inÝcio do projeto.
ò
ò  Branch por feature; PR com revisÒo entre membros (pair-review) antes de merge para main.
ò  README.md com instruþ§es de arranque (docker compose up) e arquitetura.

DivisÒo de tarefas pelos 3 membros

Membro

Responsabilidade principal

Detalhes

M-A

Sensores + RabbitMQ + Pub/Sub

Refatorar Sensor (.NET) para publisher; 1 Sensor
em Node.js (extra); configurar
exchange/queues/bindings; suporte aos 3
formatos (JSON/XML/CSV) no payload.

Pagina 10 de 15

TP2 SD - Roadmap

Membro

Responsabilidade principal

Detalhes

M-B

Serviþos Python + gRPC

M-C

Servidor + MongoDB + Interface
CLI

Todos

Transversal

Definir .proto; implementar PrÚ-Processamento
(normalizaþÒo, parsing); implementar Anßlise
(estatÝstica, outliers, regressÒo, EWMA);
Dockerfiles.

Refatorar Servidor (.NET) para subscriber + cliente
gRPC; repositories MongoDB; CLI Spectre.Console;
Web (se houver tempo).

docker-compose.yml; relat¾rio tÚcnico (cada um
escreve a secþÒo da sua fase); testes de
integraþÒo; preparaþÒo da demo e Q&A.

5.2 ù ConfiguraþÒo e arranque (Docker Compose)

ò  Um ·nico docker-compose.yml: rabbitmq, mongo, preprocessing, analysis.
ò
.env.example versionado; .env real ignorado pelo Git.
ò  Healthchecks; depends_on com condition: service_healthy.

5.3 ù Testes

ò  Unitßrios nos serviþos Python (pytest) ù normalizaþÒo, anßlise.
ò

IntegraþÒo ponta-a-ponta: docker compose up ? publicar X mensagens ? validar aparecem
em MongoDB.

ò  Script de carga: N sensores em paralelo; medir throughput e perdas.

5.4 ù Relat¾rio tÚcnico (4 pßginas mßximo)

ò  SecþÒo 1 ù Protocolo: JSON schema das mensagens Pub/Sub, .proto, fluxos.
ò  SecþÒo 2 ù ImplementaþÒo: aproveitar as justificaþ§es desta secþÒo 3 deste roadmap.
ò  Anexo ù link para reposit¾rio + screenshots das issues.
ò  Escrever em paralelo (nÒo deixar para a vÚspera).

5.5 ù ApresentaþÒo

ò  Demo ao vivo: arranque com docker compose, geraþÒo de dados, painel RabbitMQ, anßlise

via CLI.

ò  Slides curtos (?10): arquitetura, decis§es (com tabelas comparativas), demo, liþ§es.

6. Cronograma Sugerido

Calendßrio proposto considerando a data limite de 29 de maio de 2026:

PerÝodo

Foco

Entregßveis

15û17 mai

Setup

18û20 mai

Fase 1

Reposit¾rio, docker-compose esqueleto, .proto definidos,
esboþo do relat¾rio.

Serviþos Python (PrÚ-Proc + Anßlise) operacionais; clientes
gRPC em Gateway e Servidor.

Pagina 11 de 15

TP2 SD - Roadmap

PerÝodo

Foco

Entregßveis

21û23 mai

Fase 2

RabbitMQ + Sensor publisher + Gateway subscriber +
m·ltiplos Gateways.

24û26 mai

Fase 3

MongoDB + reposit¾rios + CLI; Web (se houver tempo).

27û28 mai

Polish

Testes integraþÒo, screenshots, fechar issues, finalizar
relat¾rio.

29 mai

Entrega

SubmissÒo Moodle + preparaþÒo da apresentaþÒo.

7. Riscos e Mitigaþ§es

Risco

MitigaþÒo

Curva de gRPC + Python

Comeþar Fase 1 cedo; tutorial oficial; manter contratos simples;
gerar stubs no inÝcio.

Conflitos no driver MongoDB

Fixar versÒo MongoDB.Driver no .csproj; testar com Mongo 7.

RabbitMQ ù mensagens
perdidas

Painel web para diagn¾stico; mensagens persistentes; ack
manual.

Falta de tempo para Web UI

CLI cumpre requisitos; Web Ú valorizaþÒo.

Conflitos no Git entre membros

Branches feature/fase-X-tarefa-Y; PRs revisados; merges
frequentes.

Relat¾rio deixado para o fim

Atualizar secþ§es no fecho de cada tarefa.

Docker no Windows lento

Usar WSL2; configurar mem¾ria mÝnima 4GB.

ApÛndice A ù Frases prontas para o relat¾rio

Excertos curtos para integrar nas secþ§es de ImplementaþÒo e JustificaþÒo do relat¾rio (4 pßginas).

Sobre gRPC

O gRPC foi escolhido por suportar nativamente as duas linguagens da nossa stack (C# e Python),
por permitir a definiþÒo formal dos contratos em ficheiros .proto que tornam o protocolo de
comunicaþÒo explÝcito e versionßvel, e pela eficiÛncia do transporte HTTP/2 binßrio, adequado a
fluxos de telemetria. Alternativas como JSON-RPC ou XML-RPC foram descartadas pela ausÛncia
de tipagem forte e pelo menor valor demonstrativo no contexto de Sistemas DistribuÝdos.

Sobre o RabbitMQ e routing hierßrquica

Adotou-se uma routing key hierßrquica do tipo zona.tipo.idSensor sobre um topic exchange do
RabbitMQ. Esta estrutura permite que cada Gateway subscreva por zona geogrßfica, por tipo de
dado, ou pela combinaþÒo de ambos, usando wildcards (*, #). O resultado Ú o desacoplamento
total Sensor?Gateway: os Sensores publicam sem conhecer os destinatßrios, e a topologia de
subscriþÒo pode ser reconfigurada em runtime sem alterar publishers ù uma propriedade
nuclear de sistemas distribuÝdos escalßveis.

Sobre o MongoDB

Pagina 12 de 15

TP2 SD - Roadmap

Optou-se por MongoDB pela natureza heterogÚnea dos dados produzidos pelos sensores (cada
tipo tem metadados pr¾prios), pela ingestÒo write-intensive caracterÝstica de cenßrios IoT, pelo
alinhamento com o fluxo JSON jß existente entre RabbitMQ, gRPC e a interface, e pela existÛncia
de coleþ§es time-series nativas. A ausÛncia de relaþ§es fortes no domÝnio elimina a vantagem
clßssica das bases relacionais.

Sobre Python

Os serviþos de PrÚ-Processamento e Anßlise foram implementados em Python para aproveitar o
ecossistema dominante de anßlise de dados (pandas, numpy, scikit-learn) e para demonstrar
interoperabilidade real entre linguagens via contratos gRPC. Esta heterogeneidade tecnol¾gica Ú
tambÚm um dos fatores de valorizaþÒo explicitamente referidos no enunciado do TP2.

Sobre Docker Compose

Toda a infraestrutura (broker RabbitMQ, base de dados MongoDB, serviþos Python) Ú
orquestrada por Docker Compose, garantindo reprodutibilidade, isolamento de vers§es e
arranque em um ·nico comando. Esta Ú a abordagem standard da ind·stria para sistemas
distribuÝdos em ambiente de desenvolvimento.

ApÛndice B ù Perguntas defensivas (Q&A)

PossÝveis perguntas do docente na apresentaþÒo e respostas a preparar.

Pergunta

Resposta a preparar

Porque nÒo usaram gRPC tambÚm
entre Sensor e Gateway?

E se o RabbitMQ falhar? O sistema
para?

Porque o cenßrio Sensor?Gateway Ú de muitos publishers
para muitos subscribers, sem necessidade de resposta ù
encaixa em Pub/Sub. RPC Ú apropriado quando hß um par
cliente-servidor com resposta esperada (Gateway?PrÚ-Proc,
Servidor?Anßlise).

Os Sensores tÛm reconexÒo automßtica com backoff; o
broker em produþÒo seria clusterizado. Para o Ômbito do TP2,
a persistÛncia das mensagens garante que nada se perde
durante o downtime; ap¾s restart, o backlog Ú processado.

Porque routing hierarquica e nao
fanout para todos os Gateways?

Fanout entregaria a todos os Gateways todas as mensagens -
desperdicio de banda e processamento. Topic com wildcards
filtra na origem, permitindo Gateways especializados.

Como garantem que nao se perde
uma leitura?

E a escalabilidade? Aguenta 1000
sensores?

Quatro mecanismos: (1) mensagem persistente; (2) ack
manual apos processamento no Gateway; (3) gravacao
atomica em MongoDB; (4) retry no cliente gRPC se Pre-Proc
falhar.

RabbitMQ aguenta centenas de milhares de msg/s; podemos
escalar horizontalmente Gateways subscrevendo as mesmas
queues (round-robin). Bottleneck seria o Servidor - resolvido
com sharding por zona.

Porque nao usaram um ORM como
Entity Framework?

MongoDB.Driver ja oferece mapeamento POCO via atributos;
um ORM adicional acrescentaria complexidade sem beneficio
para o dominio simples (sem joins, sem migrations).

Pagina 13 de 15

TP2 SD - Roadmap

Pergunta

Resposta a preparar

Como testaram o sistema?

Porque mantiveram codigo do TP1
em vez de reescrever?

Porque tres formatos no Pre-
Processamento? Nao bastava um?

Porque a interface e CLI e nao Web?

Porque incluiram um Sensor em
Node.js?

Porque nao implementaram TLS /
autenticacao forte?

E se quisessem escalar para uma
cidade inteira?

Testes unitarios em Python (pytest); script de integracao que
publica N mensagens e verifica que aparecem em MongoDB;
teste de carga com simulacao de 100 sensores em paralelo.

Refatoracao minima preserva trabalho ja validado e cumpre
o requisito explicito do enunciado de manter funcionalidades
anteriores. Eliminamos apenas os sockets diretos,
substituindo pelos pontos de comunicacao Pub/Sub e RPC.

O enunciado refere explicitamente JSON, XML e CSV.
Suportar os tres prova que o mecanismo de normalizacao e
generalizavel - nao esta acoplado a um formato especifico.
Cada Sensor publica no formato mais natural a sua origem.

CLI e mais robusta para demo ao vivo (headless, scriptavel,
sem dependencia de browser) e suficiente para parametrizar
todas as analises exigidas. Web foi considerada como
valorizacao, implementada apenas se o cronograma o
permitiu.

O enunciado valoriza explicitamente a inclusao de diferentes
tecnologias e linguagens. Ja temos .NET + Python; um Sensor
em Node.js com amqplib acrescenta uma terceira stack a
custo muito baixo (cerca de 30 linhas) e demonstra
interoperabilidade real entre 3 linguagens distintas via
RabbitMQ.

O ambito do TP2 nao menciona seguranca como requisito;
implementar TLS exigiria gestao de certificados e CA,
desproporcional ao MVP. Aplicamos boa pratica minima:
credenciais via variaveis de ambiente e utilizadores
dedicados (nao guest/root) no RabbitMQ e MongoDB.

A arquitetura escala horizontalmente: (1) multiplos Gateways
consumindo as mesmas queues distribuem carga em round-
robin; (2) Servidor pode ser sharded por zona; (3) MongoDB
suporta sharding nativo; (4) RabbitMQ pode ser clusterizado.
A demo prova o principio com 100 sensores.

Apendice C - Checklist final (antes de submeter)

ò  Repositorio com README detalhado e instrucoes de arranque.
ò  docker compose up arranca todos os servicos sem erros.
ò  Sensores publicam e Gateways recebem via RabbitMQ (validado no painel).
ò  Gateways invocam Pre-Processamento (Python) com sucesso via gRPC.
ò  Servidor invoca Analise (Python) e armazena resultados em MongoDB.
ò
ò
ò  Relatorio tecnico entregue em PDF com protocolo, implementacao, justificacoes e anexo.

Interface CLI permite consultar dados e disparar analises parametrizadas.
Issues criadas e fechadas correspondem as tarefas reais executadas.

Pagina 14 de 15

ò  Apresentacao preparada com demo ao vivo + slides com tabelas comparativas.
ò  Q&A defensivo (Apendice B) revisto pelos 3 membros.

TP2 SD - Roadmap

Pagina 15 de 15



