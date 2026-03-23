## 3. Protocolo SENSOR ↔ GATEWAY

### 3.1 Estabelecimento de Ligação

| Mensagem | Direção | Descrição |
|---|---|---|
| `HELLO|sensor_id:<id>` | SENSOR → GATEWAY | Apresentação do sensor ao gateway |
| `OK` | GATEWAY → SENSOR | Sensor registado e ativo — ligação aceite |
| `ERRO_NAO_REGISTADO` | GATEWAY → SENSOR | Sensor não consta nos ficheiros CSV |
| `ERRO_INATIVO` | GATEWAY → SENSOR | Sensor em manutenção ou desativado |

**Exemplo:**
```
SENSOR  → GATEWAY: HELLO|sensor_id:S102
GATEWAY → SENSOR:  OK
```

### 3.2 Envio de Medições

| Mensagem | Direção | Descrição |
|---|---|---|
| `DATA\|tipo_dado:<tipo>\|valor:<val>\|timestamp:<ts>\|zona:<zona>` | SENSOR → GATEWAY | Envio de uma medição ambiental |
| `ACK` | GATEWAY → SENSOR | Medição recebida e encaminhada com sucesso |
| `ERRO_TIPO_INVALIDO` | GATEWAY → SENSOR | Tipo de dado não suportado (validado pelo CSV) |
| `ERRO_SENSOR_INATIVO` | GATEWAY → SENSOR | Sensor passou a inativo entretanto |

**Exemplo:**
```
SENSOR  → GATEWAY: DATA|tipo_dado:PM2.5|valor:78|timestamp:2026-03-10T09:15:00|zona:ZONA_ESCOLAR
GATEWAY → SENSOR:  ACK
```

### 3.3 Heartbeat

| Mensagem | Direção | Descrição |
|---|---|---|
| `HEARTBEAT\|sensor_id:<id>` | SENSOR → GATEWAY | Sinal periódico de presença do sensor |
| `ACK` | GATEWAY → SENSOR | Heartbeat recebido — campo last_sync atualizado |

**Exemplo:**
```
SENSOR  → GATEWAY: HEARTBEAT|sensor_id:S102
GATEWAY → SENSOR:  ACK
```

### 3.4 Pedido de Stream de Vídeo

| Mensagem | Direção | Descrição |
|---|---|---|
| `VIDEO_START\|sensor_id:<id>` | SENSOR → GATEWAY | Pedido de início de stream de vídeo |
| `ACK` | GATEWAY → SENSOR | Stream aceite e a ser processada |
| `VIDEO_STOP\|sensor_id:<id>` | SENSOR → GATEWAY | Terminação da stream de vídeo |
| `ACK` | GATEWAY → SENSOR | Stream terminada com sucesso |

### 3.5 Finalização

| Mensagem | Direção | Descrição |
|---|---|---|
| `BYE\|sensor_id:<id>` | SENSOR → GATEWAY | Pedido de encerramento da ligação |
| `OK` | GATEWAY → SENSOR | Ligação terminada corretamente |

**Exemplo:**
```
SENSOR  → GATEWAY: BYE|sensor_id:S102
GATEWAY → SENSOR:  OK
```

---

## 4. Protocolo GATEWAY ↔ SERVIDOR

### 4.1 Estabelecimento de Ligação

| Mensagem | Direção | Descrição |
|---|---|---|
| `HELLO_GW\|gateway_id:<id>` | GATEWAY → SERVIDOR | Identificação do gateway ao servidor |
| `OK` | SERVIDOR → GATEWAY | Gateway aceite — ligação estabelecida |

### 4.2 Envio de Dados

| Mensagem | Direção | Descrição |
|---|---|---|
| `STORE\|tipo_dado:<tipo>\|valor:<val>\|timestamp:<ts>\|zona:<zona>\|sensor_id:<id>` | GATEWAY → SERVIDOR | Envio de uma medição para armazenamento |
| `ACK` | SERVIDOR → GATEWAY | Dado recebido e armazenado com sucesso |
| `ERRO_ARMAZENAMENTO` | SERVIDOR → GATEWAY | Falha no armazenamento do dado |

**Exemplo:**
```
GATEWAY  → SERVIDOR: STORE|tipo_dado:PM2.5|valor:78|timestamp:2026-03-10T09:15:00|zona:ZONA_ESCOLAR|sensor_id:S102
SERVIDOR → GATEWAY:  ACK
```

### 4.3 Finalização

| Mensagem | Direção | Descrição |
|---|---|---|
| `BYE_GW\|gateway_id:<id>` | GATEWAY → SERVIDOR | Encerramento da ligação com o servidor |
| `OK` | SERVIDOR → GATEWAY | Ligação encerrada com sucesso |

## 5. Estados das Entidades

### 5.1 Estados do SENSOR

| Estado | Descrição |
|---|---|
| `DESLIGADO` | Sensor ainda não iniciou comunicação |
| `A_LIGAR` | Enviou HELLO, aguarda resposta do Gateway |
| `ATIVO` | Ligação aceite — pode enviar dados e heartbeat |
| `A_ENVIAR` | Enviou DATA, aguarda ACK do Gateway |
| `A_DESLIGAR` | Enviou BYE, aguarda OK do Gateway |
| `ERRO` | Recebeu resposta de erro — termina ligação |

### 5.2 Estados do GATEWAY

| Estado | Descrição |
|---|---|
| `À_ESPERA` | Aguarda ligação de um sensor |
| `A_VALIDAR` | Recebeu HELLO — a verificar sensor no CSV |
| `ATIVO` | Sensor validado — a receber dados e heartbeats |
| `A_ENCAMINHAR` | Recebeu DATA — a enviar STORE ao Servidor |
| `SENSOR_INATIVO` | Heartbeat não recebido — marca sensor como indisponível |

### 5.3 Estados do SERVIDOR

| Estado | Descrição |
|---|---|
| `À_ESPERA` | Aguarda ligação de um Gateway |
| `ATIVO` | Gateway validado — a receber dados |
| `A_ARMAZENAR` | Recebeu STORE — a guardar dado no ficheiro |

---

## 6. Fluxo de Comunicação

### 6.1 Fluxo SENSOR ↔ GATEWAY (sucesso)

| Passo | Mensagem | Descrição |
|---|---|---|
| 1 | `HELLO\|sensor_id:S102` | Sensor inicia ligação |
| 2 | `OK` | Gateway valida e aceita |
| 3 | `DATA\|tipo_dado:PM2.5\|valor:78\|...` | Sensor envia medição |
| 4 | `ACK` | Gateway confirma e encaminha ao Servidor |
| 5 | `HEARTBEAT\|sensor_id:S102` | Sensor envia sinal de presença (periódico) |
| 6 | `ACK` | Gateway confirma heartbeat |
| 7 | `BYE\|sensor_id:S102` | Sensor termina ligação |
| 8 | `OK` | Gateway confirma encerramento |

### 6.2 Fluxo GATEWAY ↔ SERVIDOR (sucesso)

| Passo | Mensagem | Descrição |
|---|---|---|
| 1 | `HELLO_GW\|gateway_id:GW01` | Gateway inicia ligação |
| 2 | `OK` | Servidor aceita ligação |
| 3 | `STORE\|tipo_dado:PM2.5\|valor:78\|...\|sensor_id:S102` | Gateway envia medição |
| 4 | `ACK` | Servidor confirma armazenamento |
| 5 | `BYE_GW\|gateway_id:GW01` | Gateway termina ligação |
| 6 | `OK` | Servidor confirma encerramento |

### 6.3 Fluxos de Erro

| Situação | Mensagem | Consequência |
|---|---|---|
| Sensor não registado | `ERRO_NAO_REGISTADO` | Ligação encerrada imediatamente |
| Sensor em manutenção | `ERRO_INATIVO` | Ligação encerrada imediatamente |
| Tipo de dado inválido | `ERRO_TIPO_INVALIDO` | Medição ignorada, ligação mantida |
| Sensor sem heartbeat | — | Gateway marca sensor como indisponível no CSV |
| Falha no armazenamento | `ERRO_ARMAZENAMENTO` | Gateway pode retentar o envio |