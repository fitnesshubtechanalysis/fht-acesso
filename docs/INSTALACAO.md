# FHT Access — Instalação na academia

Guia para instalar o totem **sem levar código-fonte**. Você publica o app no PC de desenvolvimento e copia só a pasta compilada para o computador do acesso.

---

## 1. O que vai para a academia

| Levar | Não levar |
|-------|-----------|
| Pasta `publish\win-x64\` (ZIP ou pendrive) | Repositório git, `.cs`, SDK |
| `FHT.Access.App.exe` + DLLs + `models\` | `access.db` do seu PC de teste |
| Anotações de `deviceId` / `deviceSecret` | Faciais cadastrados em dev |

**Tamanho aproximado:** ~300 MB (inclui .NET 8 + OpenCV + modelos de face).

**Dados na academia** ficam separados do executável:

```
%ProgramData%\FHT\Access\
  appsettings.json    ← configuração (migrada de LocalAppData na 1ª execução)
  appsettings.json.bak
  access.db           ← alunos + presença + faciais locais
  logs\               ← diagnóstico
```

Na primeira execução após a atualização, se existir config em `%LOCALAPPDATA%\FHT\Access\`, ela é copiada para `%ProgramData%`.

---

## 2. Gerar o pacote (PC de desenvolvimento)

Pré-requisito: [.NET 8 SDK](https://dotnet.microsoft.com/download) instalado.

```powershell
cd c:\Projetos\FHT\fht-acesso
.\scripts\publish-academia.ps1
```

Ou manualmente:

```powershell
dotnet publish src\FHT.Access.App\FHT.Access.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o publish\win-x64
```

Compacte `publish\win-x64` em ZIP e transfira.

### Versão “slim” (opcional, ~80 MB)

Só use se o PC da academia já tiver **.NET 8 Desktop Runtime** instalado:

```powershell
dotnet publish src\FHT.Access.App\FHT.Access.App.csproj `
  -c Release -r win-x64 --self-contained false `
  -o publish\win-x64-slim
```

---

## 3. Instalar no PC do totem

1. Extraia para uma pasta fixa, ex.: `C:\FHT\Access\`
2. Execute `FHT.Access.App.exe` uma vez (cria `%ProgramData%\FHT\Access\`)
3. Feche e edite a configuração (passo 4)
4. Abra de novo e valide

### Iniciar com o Windows

- Admin → **Geral** → marcar **Iniciar com o Windows** → **Salvar** (grava em `HKCU\...\Run`), **ou**
- Atalho na pasta **Inicial do Windows** apontando para `C:\FHT\Access\FHT.Access.App.exe`

Em instalação nova, o default já vem com `startWithWindows: true`.

O app aguarda **5–10 s** após o boot (`startupDelaySec`) para a NIC Ethernet estar pronta antes de conectar a catraca.

**Serial / IP da catraca** ficam em `%ProgramData%\FHT\Access\appsettings.json` — ao conectar pela aba Catraca, o discovery grava e persiste automaticamente.

**Instância única:** abrir o `.exe` de novo traz a janela existente à frente (mutex `Global\FHT.Access.SingleInstance`).

O app fica na bandeja do sistema; fechar a janela **não** desliga o reconhecimento.

---

## 4. Configuração (`appsettings.json`)

Arquivo: `%ProgramData%\FHT\Access\appsettings.json`

Exemplo para piloto:

```json
{
  "gestaoBaseUrl": "https://api.gestao.fitnesshubtech.com.br/api/v1/",
  "unitId": "uuid-da-unidade",
  "deviceId": "uuid-do-dispositivo",
  "deviceSecret": "segredo-gerado-uma-vez",
  "useFakeTurnstile": false,
  "turnstileNetwork": "Ethernet 2",
  "turnstileIp": "192.168.0.100",
  "turnstileSerial": "",
  "webcamIndex": 1,
  "webcamIndexExit": 2,
  "exitMode": "facial",
  "faceMatchThreshold": 0.35,
  "passageSuccessDisplaySec": 5,
  "passageReleaseMinDisplaySec": 3,
  "exitProcessFps": 12,
  "exitProcessMaxWidth": 1920,
  "adminPin": "1234",
  "attendantIdleMinutes": 5,
  "kioskPortrait": true
}
```

| Campo | Descrição |
|-------|-----------|
| `gestaoBaseUrl` | URL base da API Gestão (com `/` no final). **Não use localhost na academia.** |
| `unitId` | UUID da unidade (Centro, etc.) |
| `deviceId` / `deviceSecret` | Credenciais do dispositivo de acesso (criadas no Gestão) |
| `useFakeTurnstile` | `true` = simula catraca (dev). **`false` na academia.** |
| `turnstileNetwork` | Nome da NIC Windows ligada à catraca (ex. `Ethernet 2`). O Connect **precisa** do nome; IP do PC também resolve, mas prefira o nome. |
| `turnstileIp` | IP da placa Toletus LiteNet3 (ex. `192.168.0.100`) — filtro na discovery |
| `turnstileSerial` | Opcional; preenchido após Connect (discovery UDP). |
| `webcamIndex` | Câmera **entrada** (`0` = integrada do notebook; `1`/`2` = USB) |
| `webcamIndexExit` | Câmera **saída** (`-1` = desligada; use índice diferente da entrada) |
| `exitMode` | `facial` = saída com reconhecimento (liga a 2ª câmera). `free` = **só entrada facial**; a câmera de saída **não** abre nem reconhece (mesmo com `webcamIndexExit` preenchido). |
| `freeGateMode` | `true` = catraca livre **com** facial: registra entrada/saída **sem** validar presença (reentrada / saída sem entrada). Plano inválido e facial desconhecida seguem iguais. **Passagem física na catraca continua obrigatória** para presença. `false` = trava de presença |
| `faceMatchThreshold` | `0.35` recomendado. Valores altos (>0.7) migram sozinhos para 0.35 |
| `passageSuccessDisplaySec` | Segundos na tela para “Entrada/Saída registrada” (padrão `5`) |
| `passageReleaseMinDisplaySec` | Mínimo em “Pode passar na catraca/saída” (padrão `3`) |
| `exitProcessFps` / `exitProcessMaxWidth` | Mais frames e resolução na saída (~1 m, câmera panorâmica) |
| `adminPin` | PIN do modo atendente (`Ctrl+Shift+A`) — **troque antes do piloto** |

---

## 5. Criar dispositivo no Gestão

No **fht-gestao-api**, com token de staff:

```http
POST /api/v1/units/{unitId}/access-devices
Content-Type: application/json

{
  "name": "Totem Entrada Centro",
  "type": "face_reader"
}
```

A resposta traz `id`, `deviceSecret` e aviso: **o secret só aparece uma vez**. Guarde em local seguro e cole no `appsettings.json` da academia.

Teste de auth (opcional):

```http
POST /api/v1/access/device-auth
{ "deviceId": "...", "deviceSecret": "..." }
```

---

## 6. Primeira sincronização

Com Gestão acessível e credenciais corretas:

1. Abra o totem → **Ctrl+Shift+A** → PIN admin
2. **Configurações** → aba **Gestão**
3. **Test Auth** — deve passar
4. **Sync Members Now** — importa alunos da unidade para o SQLite local
5. Aguarde ou confira **Último sync** no dashboard do atendente

O **sync automático** roda a cada **2 minutos** (alunos + envio de eventos pendentes + fotos de facial).

Cadastro facial também **sobe a foto do aluno** para o Gestão (`Customer.photoUrl`). Offline: fica na fila `pending-photos` até o próximo sync.

Entrada e saída com **duas câmeras** no mesmo PC: configure `webcamIndex`, `webcamIndexExit` e `exitMode: "facial"`. A saída só libera quem está registrado como **dentro** (entrou antes).

---

## 7. Catraca Toletus (produção)

Com `useFakeTurnstile: false`:

1. Placa na mesma rede do PC (ping no `turnstileIp`)
2. `turnstileNetwork` = **Ethernet 2** (nome da NIC), `turnstileIp` = `192.168.0.100`
3. Admin → **Catraca** → **Connect** → estado **Connected** (serial preenchido sozinho)
4. Confira logs `[Toletus]` (discovery, ServerUri, Connected)
5. **Liberar Entrada** / **Liberar Saída** — só após Connected
6. Se timeout: firewall Windows permitindo entrada TCP no `FHT.Access.App` em `192.168.0.120`

O fluxo facial usa o mesmo caminho após reconhecimento + matrícula vigente.

---

## 8. Cadastro facial na academia

1. **Ctrl+Shift+A** → login atendente
2. **Cadastrar facial** → buscar aluno → capturar
3. Após captura, o totem volta ao modo automático em ~2 s
4. Aluno aproxima-se → deve reconhecer

**Remover facial errada** (ex.: teste no aluno errado): Admin → aba **Face** → selecionar aluno → **Remover facial**.

**Regra:** um rosto = um aluno. Não cadastre o mesmo rosto em dois alunos de teste.

---

## 9. Comportamento esperado na catraca

| Situação | Totem |
|----------|-------|
| Plano vigente + facial OK | “Pode passar…” (≥3 s) → “Entrada/Saída registrada” (5 s) |
| Pessoa longe / de passagem no fundo | Ignorado (ROI central + rosto mínimo) — aproximar da câmera |
| Rosto **sem** cadastro | “Não foi possível identificar…” (não libera como outro aluno) |
| Professor / colaborador | Entrada e saída livres — marque no Gestão ou use modalidade `Professor` / `Colaborador` |
| Liberou mas não passou | “Não detectamos passagem…” → reconhece de novo em ~1,5 s |
| Sem matrícula / inativo | “Procure a recepção.” |
| Rosto não cadastrado | “Não foi possível identificar seu rosto.” |
| Saída sem ter entrado (`freeGateMode: false`) | “Você não está registrado como dentro.” |
| Reentrada (`freeGateMode: false`) | “Você já está dentro.” |
| `freeGateMode: true` (piloto) | Sempre registra pela lane (entrada ou saída), sem essas travas |
| Recepção libera manual | Entrada auditada no Gestão |

A decisão usa `accessAllowed` do Gestão, atualizado antes de cada liberação (`RefreshMemberAsync`).

---

## 10. Logs e suporte

| O quê | Onde |
|-------|------|
| Log do dia | `%ProgramData%\FHT\Access\logs\access-YYYYMMDD.log` |
| Boot / crash | `%ProgramData%\FHT\Access\boot.log` |
| Banco local | `%ProgramData%\FHT\Access\access.db` |

Linhas úteis no log:

- `Face identify: Nome score=… allowed=… reason=…`
- `Auto-sync: N aluno(s), N evento(s).`
- `Totem reativado (reconhecimento facial ligado).`

---

## 11. Atualização automática (Velopack)

O totem atualiza **sem intervenção humana** via Velopack. O fluxo:

1. A cada 15 min (e no boot) o app consulta `GET /api/v1/units/:id/access/devices/:id/update` na Gestão.
2. Se houver versão nova **e** o horário for dentro da janela permitida (padrão 20h–5h) — ou se for obrigatória — inicia o processo.
3. A tela do kiosk mostra **faixa de aviso** enquanto aguarda a janela.
4. Na hora: exibe **countdown de 60 s** → pausa reconhecimento → baixa com barra de progresso → reinicia automaticamente.

### Registrar uma versão nova na Gestão

Após publicar o GitHub Release (seção 2), crie ou atualize o registro via API:

```http
POST /api/v1/units/{unitId}/access/app-releases
Authorization: Bearer <token>
Content-Type: application/json

{
  "latestVersion": "1.2.3",
  "downloadUrl": "https://github.com/seu-org/fht-acesso/releases/download/v1.2.3/",
  "mandatory": false,
  "applyAfterHour": 20,
  "applyBeforeHour": 5
}
```

`downloadUrl` deve apontar para o diretório raiz dos assets Velopack (sem `/RELEASES`).
O totem descobre o pacote correto automaticamente via feed Velopack.

### Dados preservados na atualização

Os dados em `%ProgramData%\FHT\Access\` (SQLite, faciais, appsettings) ficam **fora**
da pasta do app — o Velopack substitui só os binários.

### Atualização manual (emergência)

Se o canal automático falhar, ainda é possível:

1. No PC de dev: `.\scripts\publish-academia.ps1 -Version "1.2.3"`
2. Copie `FHT.Acesso-Setup.exe` do `publish\velopack\` para o totem
3. Execute o Setup — ele atualiza sobre a versão existente sem apagar dados

Não apague `access.db` na atualização — só se quiser recomeçar do zero.

---

## 12. Piloto assistido — checklist

Use na ordem. Marque conforme for concluindo.

### Antes de ir à academia

- [ ] Gestão API publicada e estável (URL HTTPS, não localhost)
- [ ] Unidade com alunos sincronizados (EVO/import ok)
- [ ] Dispositivo criado no Gestão (`deviceId` + `deviceSecret` guardados)
- [ ] Pacote publicado (`publish\win-x64`) em ZIP
- [ ] PIN admin alterado (não deixar `1234` em produção)
- [ ] Limpar faciais de teste no PC de dev (opcional; na academia use DB limpo)

### Hardware na academia

- [ ] PC totem com Windows 10/11, câmera USB fixa, boa iluminação
- [ ] Rede: PC ↔ Gestão (internet) e PC ↔ catraca (LAN)
- [ ] IP fixo ou reserva DHCP para a placa Toletus
- [ ] Cabo de rede / Wi‑Fi estável

### Instalação no dia

- [ ] Extrair app em `C:\FHT\Access\`
- [ ] Configurar `appsettings.json` (Gestão + device + `useFakeTurnstile: false`)
- [ ] Configurar catraca (IP, Connect no Admin)
- [ ] Test Auth + Sync Members
- [ ] Atalho / inicialização com Windows
- [ ] Tela em modo retrato se usar `kioskPortrait: true`

### Testes com recepção presente

- [ ] Aluno **com matrícula** + facial cadastrada → libera
- [ ] Aluno **sem matrícula** + facial → recusa (recepção)
- [ ] Rosto desconhecido → tela “não reconhecemos”
- [ ] Liberação manual → passagem + evento no Gestão
- [ ] Desligar internet brevemente → totem continua; eventos sobem ao voltar sync

### Primeira semana (piloto)

- [ ] Recepção treinada: cadastro facial, manual, quando chamar TI
- [ ] Conferir logs diários (`Face identify`, `Auto-sync`)
- [ ] Ajustar `webcamIndex` / iluminação se falhar reconhecimento
- [ ] Lista de alunos vigentes vs. faciais cadastrados

---

## 13. Problemas comuns

| Sintoma | Provável causa | Ação |
|---------|----------------|------|
| “Sistema offline” | Gestão inacessível ou sync antigo | Test Auth, URL, firewall |
| Sempre “não reconhece” | Modo atendente ativo ou facial inexistente | Aguardar volta ao totem; cadastrar facial |
| Reconhece mas não libera | Sem matrícula no Gestão | Normal — recepção ou regularizar plano |
| Libera pessoa errada | Mesmo rosto em dois cadastros | Remover facial duplicado |
| Catraca não abre | IP errado ou fake ainda true | `useFakeTurnstile: false`, ping IP, Connect |
| Busca lenta | Primeira vez sem cache local | Aguardar sync; busca local é rápida depois |

---

## Referências

- [ARCHITECTURE.md](ARCHITECTURE.md)
- [FHT_GESTAO_INTEGRATION.md](FHT_GESTAO_INTEGRATION.md)
- [TOLETUS.md](TOLETUS.md)
- [OFFLINE_SYNC.md](OFFLINE_SYNC.md)
- [OPERATIONAL_FLOW.md](OPERATIONAL_FLOW.md)
