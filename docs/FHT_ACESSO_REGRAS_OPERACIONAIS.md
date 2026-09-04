# FHT Acesso — Documento Oficial de Regras Operacionais

**Produto:** FHT Acesso (totem facial + catraca Toletus)  
**Integração:** FHT Gestão  
**Versão do app:** 1.0.9  
**Release:** https://github.com/fitnesshubtechanalysis/fht-acesso/releases/tag/v1.0.9  
**Data:** 04/09/2026  
**Classificação:** Interno — operação de academia / piloto  

PDF oficial: [`FHT_ACESSO_REGRAS_OPERACIONAIS_v1.0.9.pdf`](./FHT_ACESSO_REGRAS_OPERACIONAIS_v1.0.9.pdf)

---

## 1. Objetivo

Formaliza as regras de negócio e operação do FHT Acesso 1.0.9, conforme requisitos validados com a operação (recepção, catraca livre e dual-câmera). Referência oficial para instalação na academia e para evolução futura do controle de acesso rigoroso.

## 2. Checklist de conformidade (v1.0.9)

| # | Requisito | Status | Como está na 1.0.9 |
|---|-----------|--------|--------------------|
| 1 | Cadastro facial — Capturar funcional | **ATENDE** | Hit-test do preview corrigido; enroll Haar; conflito de rosto duplicado tratado |
| 2 | Leitura com catraca livre — registra entrada/saída sem bloquear presença | **ATENDE** | `freeGateMode=true`: libera e registra; presença não impede nova passagem |
| 3 | Trava de câmeras: uma lane lendo não abre a outra | **ATENDE** | `CanLaneTakeUi` / `SetActiveLane` |
| 4 | Saída: não reconhecer gente longe | **ATENDE** | Perfil `ExitDistance` + ROI estreita |
| 5 | Entrada: não abrir com qualquer movimento | **ATENDE** | Movimento na ROI **e** rosto próximo (`ApproachPresence`) |
| 6 | Nome correto na tela de sucesso | **ATENDE** | Nome amarrado à lane ativa |
| 7 | Horário real enviado à API | **ATENDE** | `occurredAt` no momento da passagem + `passageConfirmed` |

> **Atenção:** não confundir `exitMode` com `freeGateMode`.
>
> - `freeGateMode=true` → catraca livre / sem bloqueio de presença (piloto)
> - `exitMode=facial` → liga câmera de saída + dual lane
> - `exitMode=free` → saída sem facial (só entrada facial)

## 3. Regras oficiais

### 3.1 Cadastro facial (atendente)

- Selecionar o aluno e capturar olhando para a câmera de entrada.
- Exige detecção de rosto; rosto já cadastrado em outro aluno → recusa com mensagem clara.
- Depois do cadastro, o aluno aproxima-se do totem para leitura automática.

### 3.2 Leitura / reconhecimento (totem)

- Inicia só com movimento na zona central **e** rosto próximo.
- Quem só passa ao fundo/de lado não deve abrir a câmera.
- Na saída, o match exige rosto ainda mais próximo.

### 3.3 Catraca livre e registro (piloto)

- Com `freeGateMode=true`, não bloqueia por “já entrou / já saiu”.
- Cada facial + giro gera registro de entrada ou saída na Gestão.
- Objetivo: acumular histórico real antes de ligar presença rigorosa.
- Autorização por plano/matrícula continua válida.

### 3.4 Dual-câmera e exclusão mútua

- Enquanto uma lane está em reconhecimento/resultado, a outra não toma a tela.
- Evita nome errado e “pisca-pisca” entre câmeras.

### 3.5 Identidade na tela de sucesso

- Nome = aluno reconhecido na lane ativa daquele ciclo.
- Proibido sobrescrever com reconhecimento paralelo da outra câmera.

### 3.6 Timestamp e sync com a Gestão

- Horário = momento real da passagem confirmada (`occurredAt` UTC).
- Só eventos com passagem confirmada alimentam presença/KPIs.
- Não reutilizar timestamps antigos de movimentos “presos”.

## 4. Configuração recomendada (piloto dual facial + catraca livre)

| Chave | Valor | Motivo |
|-------|-------|--------|
| `freeGateMode` | `true` | Registra sem bloquear por presença |
| `exitMode` | `facial` | Ativa saída facial + dual lane |
| `webcamIndex` | câmera entrada | Leitura de quem entra |
| `webcamIndexExit` | câmera saída (≠ entrada) | Leitura de quem sai |
| `useFakeTurnstile` | `false` | Toletus real |

Após instalar a 1.0.9, revisar `%ProgramData%\FHT\Access\appsettings.json`. Não apagar `access.db`.

## 5. Instalação / atualização

- Setup: `FHT.Acesso-win-Setup.exe` da release v1.0.9
- Update automático: canal Gestão → GitHub Releases (Velopack)
- Dados preservados em ProgramData

## 6. Evolução futura

Desligar `freeGateMode` só depois que o histórico de eventos reais estiver confiável na Gestão.

## 7. Aprovação

| Papel | Nome | Data | Assinatura |
|-------|------|------|------------|
| Produto / FHT | | | |
| Operação academia | | | |
| Tecnologia | | | |
