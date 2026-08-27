# Piloto — Tolerância e saída livre (Arquitetura do Corpo)

Decisões da reunião com a Lygia integradas ao fluxo existente.

## Comportamento

| Situação | 1ª tentativa real | 2ª tentativa (sem regularizar) |
|----------|-------------------|--------------------------------|
| Aluno regular (plano + financeiro OK) | Entrada normal | Entrada normal |
| Plano vencido / sem plano / pendência / revisão | Libera + mensagem discreta + atividade Relacionamento | Recepção obrigatória |
| Suspenso / bloqueado | Negado (sem tolerância) | Negado |

## Saída (fase piloto)

- Saída **livre** — sem facial na saída
- `exitMode: free` em `%ProgramData%\FHT\Access\appsettings.json`
- Presença é **estimada** (entrada confirmada); expira no fim do dia / job local
- Não calcula permanência individual nem ocupação exata

## Tolerância

- Consumida **somente após passagem física confirmada** (LiteNet3)
- Timeout sem giro **não** consome tolerância
- Reconhecimentos repetidos da câmera **não** contam como nova tentativa (session guard + cooldown)
- Histórico persiste no Gestão (`access_tolerance_occurrences`) — reiniciar o app não renova tolerância

## API Gestão (novos endpoints)

- `GET /units/:unitId/access/policy`
- `POST /units/:unitId/access/evaluate`
- `POST /units/:unitId/access/tolerance/consume`
- `POST /units/:unitId/access/tolerance/blocked-attempt`

Member sync inclui: `operationalStatus`, `financialStatus`, `accessDecisionKind`, `toleranceUsed`, etc.

## Evolução futura (não implementado)

- Segunda câmera na saída para registro individual e permanência real
- WhatsApp automático
- Ocupação em tempo real
