# Registo de Decisões de Desenvolvimento — TP1 Sistemas Distribuídos

**Projeto:** Serviços de Monitorização Urbana para One Health  
**UC:** Sistemas Distribuídos 2025/2026 · UTAD · ECT

---

> Este ficheiro regista as decisões técnicas e arquitecturais tomadas ao longo do desenvolvimento.  
> Para cada nova decisão, copia o template no final deste ficheiro e preenche todos os campos.

---

## Índice

| ID | Título | Componente | Estado | Data |
|----|--------|------------|--------|------|
| — | *(sem decisões ainda)* | — | — | — |

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
