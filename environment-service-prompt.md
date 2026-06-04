# Prompt de Implementação — Environment Service

## Contexto do projeto

Este projeto é um sistema distribuído de monitorização ambiental urbana chamado **TP1 - One Health**. O pipeline atual é:

```
sensor → RabbitMQ → Gateway → Preprocessing (gRPC) → Servidor → MongoDB
```

Os sensores estão implementados em dois lugares:
- `Sensor/` — C# (.NET 8), modos automático e CLI
- `SensorNode/` — Node.js, corre em Docker

Atualmente ambos geram valores aleatórios com ranges hardcoded por tipo:
```
TEMP:   15.0 + random(0-15.0)
HUM:    45.0 + random(0-35.0)
RUIDO:  45.0 + random(0-45.0)
PM2.5:  5.0  + random(0-35.0)
PM10:   10.0 + random(0-80.0)
LUZ:    100.0 + random(0-800.0)
AR:     0.5  + random(0-4.0)
```

Os serviços existentes estão em `services/preprocessing/` e `services/analysis/`, ambos em Python com gRPC. O orquestrador é o `docker-compose.yml` na raiz. Todas as variáveis de ambiente estão no `.env`.

As zonas válidas são: `ZONA_CENTRO`, `ZONA_ESCOLAR`, `ZONA_INDUSTRIAL`, `ZONA_RESIDENCIAL`, `ZONA_PARQUE`.

Os tipos válidos e as suas unidades são:

| Tipo  | Unidade | Range válido |
|-------|---------|-------------|
| TEMP  | C       | -50 a 60    |
| HUM   | %       | 0 a 100     |
| RUIDO | dB      | 0 a 150     |
| PM2.5 | µg/m³   | 0 a 1000    |
| PM10  | µg/m³   | 0 a 1000    |
| LUZ   | lux     | 0 a 100000  |
| AR    | µg/m³   | 0 a 1000    |

---

## O que deve ser criado

Um novo microserviço chamado **Environment Service** que representa o "mundo físico" da cidade. Os sensores passam a consultar este serviço por HTTP em vez de gerar valores aleatórios. O resto do pipeline não muda.

---

## Decisões técnicas já tomadas

- **Linguagem:** Python com FastAPI
- **Deploy:** novo container Docker adicionado ao `docker-compose.yml` existente
- **Localização dos ficheiros:** `services/environment/`
- **Atualização interna:** a cada 1 segundo (background loop)
- **Sensores consultam:** a cada `intervalSeconds` (5s por defeito), sem alteração no timing do pipeline
- **Eventos:** injetáveis via endpoint HTTP desde o início
- **Fallback nos sensores:** se o Environment Service estiver offline, os sensores voltam à geração aleatória atual

---

## Estrutura de ficheiros a criar

```
services/environment/
├── server.py           ← aplicação FastAPI principal
├── simulator.py        ← lógica de simulação do mundo
├── zones.py            ← perfis e configuração de cada zona
├── events.py           ← gestão de eventos ativos
├── Dockerfile
└── requirements.txt
```

---

## Endpoints HTTP obrigatórios

```
GET  /health
     → { "status": "ok", "timestamp": "..." }

GET  /reading?zone=ZONA_CENTRO&type=TEMP&sensorId=S101
     → { "zone": "ZONA_CENTRO", "type": "TEMP", "value": 22.4, "unit": "C", "timestamp": "..." }
     O sensorId é opcional. Se fornecido, aplica ruído gaussiano específico do sensor.
     Se não fornecido, devolve o valor base da zona sem ruído.

GET  /state?zone=ZONA_CENTRO
     → estado atual de todos os tipos para uma zona
     → { "zone": "ZONA_CENTRO", "timestamp": "...", "readings": { "TEMP": 22.4, "HUM": 58.1, ... } }

GET  /state
     → estado atual de todas as zonas (sem parâmetro)

POST /events
     → injeta um evento temporário
     Body:
     {
       "name": "chuva",
       "zones": ["ZONA_CENTRO", "ZONA_PARQUE"],
       "durationSeconds": 300,
       "effects": {
         "HUM": 18,
         "TEMP": -3,
         "LUZ": -250,
         "PM10": -10,
         "RUIDO": 4
       }
     }
     Os efeitos são deltas aplicados por cima do valor simulado normal.

GET  /events
     → lista eventos ativos com tempo restante
```

---

## Lógica de simulação — o que o `simulator.py` deve implementar

### Estado interno

O simulador mantém em memória, para cada combinação `(zona, tipo)`, um valor atual que evolui ao longo do tempo. Este estado é atualizado num background loop a cada 1 segundo.

### Padrão temporal base (ciclo de 24h)

Cada tipo segue um padrão temporal baseado na hora real do sistema (`datetime.now()`).

**TEMP** — curva sinusoidal diária:
- Mínimo às 6h da manhã
- Máximo às 15h da tarde
- Formula base: `base_temp + amplitude * sin((hora - 6) * π / 9)` quando hora entre 6-15, senão decrescente

**LUZ** — segue o ciclo solar:
- Zero entre 21h e 6h
- Crescente das 6h às 12h, decrescente das 12h às 21h
- Pico ao meio-dia

**RUIDO** — padrão de tráfego urbano:
- Baixo entre 0h-6h (base baixa)
- Pico manhã: 7h-9h
- Moderado: 9h-17h
- Pico tarde: 17h-19h
- Descida gradual: 19h-23h

**HUM** — inversamente correlada com TEMP:
- Quando TEMP sobe, HUM desce proporcionalmente
- Tem também variação suave independente

**PM2.5 / PM10** — correlacionados com RUIDO (proxy de tráfego):
- Seguem o mesmo padrão de picos de tráfego, com algum desfasamento temporal

**AR** — índice geral de qualidade do ar, combinação de PM2.5 e PM10 normalizados

### Perfis por zona (offsets sobre o padrão base)

```python
ZONE_PROFILES = {
    "ZONA_INDUSTRIAL": {
        "TEMP":  +2.0,
        "RUIDO": +20.0,
        "PM2.5": +15.0,
        "PM10":  +25.0,
        "AR":    +1.5,
        "HUM":   -5.0,
        "LUZ":   -50.0
    },
    "ZONA_PARQUE": {
        "TEMP":  -1.0,
        "RUIDO": -15.0,
        "PM2.5": -4.0,
        "PM10":  -8.0,
        "AR":    -1.0,
        "HUM":   +8.0,
        "LUZ":   +100.0
    },
    "ZONA_CENTRO": {
        "TEMP":  +1.5,
        "RUIDO": +10.0,
        "PM2.5": +8.0,
        "PM10":  +12.0,
        "AR":    +0.8,
        "HUM":   -3.0,
        "LUZ":   -30.0
    },
    "ZONA_ESCOLAR": {
        "TEMP":  0.0,
        "RUIDO": 0.0,   # mas com pico 8h-9h e 12h-13h e 17h-18h
        "PM2.5": +2.0,
        "PM10":  +3.0,
        "AR":    +0.2,
        "HUM":   0.0,
        "LUZ":   0.0
    },
    "ZONA_RESIDENCIAL": {
        "TEMP":  0.0,
        "RUIDO": -8.0,
        "PM2.5": -2.0,
        "PM10":  -3.0,
        "AR":    -0.3,
        "HUM":   +2.0,
        "LUZ":   +20.0
    }
}
```

### Evolução contínua (random walk com mean reversion)

A cada tick de 1 segundo, o valor de cada `(zona, tipo)` sofre um pequeno delta aleatório, mas é atraído de volta para o valor esperado pelo padrão temporal. Isto evita que os valores derivem para fora do range realista ao longo do tempo:

```
delta = gaussian(mean=0, std=small_sigma)
pull_to_target = (target_value - current_value) * 0.05
new_value = current_value + delta + pull_to_target
new_value = clamp(new_value, min_valid, max_valid)
```

Os valores de `small_sigma` por tipo (desvio padrão do ruído por segundo):

| Tipo  | Sigma  |
|-------|--------|
| TEMP  | 0.05   |
| HUM   | 0.1    |
| RUIDO | 0.5    |
| PM2.5 | 0.2    |
| PM10  | 0.3    |
| LUZ   | 5.0    |
| AR    | 0.02   |

### Ruído de medição do sensor (aplicado no endpoint `/reading`)

Quando `sensorId` é fornecido, adicionar ruído gaussiano pequeno ao valor base da zona para simular imperfeição de medição. Dois sensores na mesma zona devem ler valores ligeiramente diferentes.

Sigma do ruído de medição por tipo:

| Tipo  | Sigma de medição |
|-------|-----------------|
| TEMP  | 0.3°C           |
| HUM   | 1.0%            |
| RUIDO | 1.5 dB          |
| PM2.5 | 0.5 µg/m³       |
| PM10  | 0.8 µg/m³       |
| LUZ   | 10.0 lux        |
| AR    | 0.05            |

O `sensorId` pode ser usado como seed para tornar o ruído determinístico para o mesmo sensor (mesmo sensor, condições iguais → variação consistente).

---

## Lógica de eventos — o que o `events.py` deve implementar

- Guardar lista de eventos ativos em memória com `expiry_time = now + durationSeconds`
- A cada tick do simulador, verificar eventos expirados e removê-los
- Ao calcular o valor de `(zona, tipo)`, somar os efeitos de todos os eventos ativos que incluam essa zona e tipo
- O delta dos efeitos é aditivo e imediato (não gradual, por simplicidade)
- O endpoint `GET /events` devolve cada evento com `secondsRemaining`

---

## Dockerfile

```dockerfile
FROM python:3.12-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY . .
CMD ["uvicorn", "server:app", "--host", "0.0.0.0", "--port", "8001"]
```

---

## requirements.txt

```
fastapi
uvicorn[standard]
numpy
```

---

## Adição ao docker-compose.yml

Adicionar o serviço `environment` à rede `sd-net` existente, porta `8001`, antes do serviço `sensor-node`. O `sensor-node` deve ter `depends_on: environment`.

Variável de ambiente a adicionar ao `.env`:
```
ENVIRONMENT_SERVICE_URL=http://environment:8001
```

---

## Modificação no SensorNode (Node.js)

No ficheiro `SensorNode/index.js`, na função que gera o valor do sensor:

1. Tentar fazer `GET http://environment:8001/reading?zone={zone}&type={type}&sensorId={sensorId}`
2. Se a resposta for 200, usar o `value` devolvido
3. Se o serviço estiver offline (timeout, connection refused) ou devolver erro, usar a geração aleatória atual como fallback
4. O timeout da chamada HTTP deve ser curto: 1000ms

---

## Modificação no Sensor C# (opcional)

No ficheiro `Sensor/Sensor.cs`, na função `GenerateValue` (ou equivalente onde estão os ranges hardcoded), aplicar a mesma lógica: tentar `HttpClient.GetAsync` ao Environment Service com fallback.

---

## Notas finais

- O Environment Service **não persiste nada em base de dados** — estado em memória apenas
- O serviço **não conhece o RabbitMQ nem o Gateway** — é completamente isolado
- Os sensores continuam a publicar no RabbitMQ com o mesmo formato de mensagem — o pipeline a jusante não muda
- O endpoint `/docs` do FastAPI deve funcionar automaticamente com documentação interativa
- Ao arrancar, o simulador deve inicializar o estado de todas as zonas com valores coerentes para a hora atual do sistema
