# FHT Acesso — Instalação e piloto assistido

Guia para instalar o totem **sem levar código-fonte** para o computador da academia.  
Leia na ordem: **Preparar pacote** → **Instalar no PC** → **Configurar** → **Testar piloto**.

---

## 1. O que você leva para a academia

| Leva | Não leva |
|------|----------|
| Pasta `publish\win-x64` (ZIP ou pendrive) | Repositório git / `.cs` / SDK |
| Credenciais do dispositivo (Gestão) | `access.db` do seu PC de dev |
| IP da catraca Toletus | Faciais de teste do ambiente local |

O app grava dados em **`%LOCALAPPDATA%\FHT\Access\`** (banco, config, logs, faciais).  
Na primeira instalação na academia, use **config nova e banco vazio**.

---

## 2. Requisitos do PC do totem

- Windows 10 ou 11 (64 bits)
- Webcam USB (testada antes de instalar)
- Rede com acesso ao **Gestão API** (HTTPS ou LAN estável)
- Catraca Toletus LiteNet3 na mesma rede (IP fixo recomendado)
- Tela em modo retrato ou paisagem conforme layout do totem
- ~500 MB livres em disco (pacote ~300 MB + dados)

**Não precisa** instalar .NET SDK se usar o pacote **self-contained** (seção 3).

---

## 3. Gerar o pacote (no seu PC de desenvolvimento)

Abra PowerShell na pasta do projeto `fht-acesso`:

```powershell
cd c:\Projetos\FHT\fht-acesso

dotnet publish src\FHT.Access.App\FHT.Access.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o publish\win-x64
```

Saída: pasta **`publish\win-x64`** contendo:

- `FHT.Access.App.exe` — executável principal
- `models\` — modelos de reconhecimento facial (Haar + SFace)
- DLLs e runtime .NET embutidos

Compacte em ZIP, copie para pendrive ou nuvem.

### Pacote menor (opcional)

Se o PC da academia já tiver **.NET 8 Desktop Runtime** instalado:

```powershell
dotnet publish src\FHT.Access.App\FHT.Access.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -o publish\win-x64-slim
```

---

## 4. Instalar no PC da academia

1. Crie a pasta `C:\FHT\Access\`
2. Extraia **todo** o conteúdo de `win-x64` para essa pasta
3. Confirme que existem:
   - `C:\FHT\Access\FHT.Access.App.exe`
   - `C:\FHT\Access\models\` (com `.xml` e `.onnx`)
4. Execute `FHT.Access.App.exe` uma vez (cria `%LOCALAPPDATA%\FHT\Access\`)

### Iniciar com o Windows

- Atalho na pasta **Inicial do Windows** apontando para `C:\FHT\Access\FHT.Access.App.exe`, **ou**
- Agendador de Tarefas → disparo “Ao iniciar o sistema”

O app fica na bandeja (tray). Clique no ícone para abrir a janela do totem.

---

## 5. Configurar (`appsettings.json`)

Arquivo: **`%LOCALAPPDATA%\FHT\Access\appsettings.json`**

Exemplo para piloto com catraca real:

```json
{
  "gestaoBaseUrl": "https://SUA-API-GESTAO/",
  "unitId": "UUID-DA-UNIDADE",
  "deviceId": "UUID-DO-DISPOSITIVO",
  "deviceSecret": "SEGREDO-GERADO-NO-GESTAO",
  "useFakeTurnstile": false,
  "turnstileNetwork": "",
  "turnstileIp": "192.168.1.100",
  "turnstileSerial": "",
  "webcamIndex": 0,
  "faceMatchThreshold": 0.35,
  "adminPin": "1234",
  "attendantIdleMinutes": 5,
  "kioskPortrait": true
}
```

| Campo | Descrição |
|-------|-----------|
| `gestaoBaseUrl` | URL base da API Gestão (com `/` no final) |
| `unitId` | UUID da unidade (ex.: Unidade Centro) |
| `deviceId` / `deviceSecret` | Credenciais do dispositivo criado no Gestão |
| `useFakeTurnstile` | **`false`** na academia (catraca real) |
| `turnstileIp` | IP da placa Toletus na rede local |
| `webcamIndex` | `0` = primeira câmera; se errada, teste `1` |
| `faceMatchThreshold` | `0.35` (não usar `0.92`) |
| `adminPin` | PIN do admin/atendente — **troque antes do piloto** |

Reinicie o app após editar o JSON.

---

## 6. Credenciais no Gestão

No **fht-gestao-api** / painel Gestão:

1. Crie um **Access Device** para a unidade (tipo catraca/face)
2. Guarde `deviceId` e `deviceSecret` (secret só aparece na criação)
3. Confirme que a API responde: `POST /api/v1/access/device-auth`

O totem autentica sozinho e faz sync a cada **2 minutos** (alunos + eventos).

---

## 7. Primeiro sync e cadastros

1. Abra o totem → **Ctrl+Shift+A** (ou canto inferior direito) → PIN admin
2. Aba **Sync** / Gestão:
   - **Sync members** (full na primeira vez)
   - **Flush pending** se houver eventos pendentes
3. Aguarde “Último sync” recente no dashboard do atendente

### Cadastro facial (piloto)

- Use o fluxo **Atendente → Buscar aluno → Cadastrar facial**
- Após capturar, o totem volta ao modo automático em ~2 s
- **Um rosto = um aluno** (não cadastre o mesmo rosto em perfis de teste diferentes)

Faciais ficam no SQLite local (`access.db`), não sobem para o Gestão hoje.

---

## 8. Regras de liberação (confiança na catraca)

A catraca decide com base no **Gestão**, atualizado antes de cada tentativa:

| Situação do aluno | Totem |
|-------------------|--------|
| Plano vigente (`accessAllowed: true`) | Libera (mesmo com dívida) |
| Sem matrícula / vencido / inativo | Não libera → “Procure a recepção” |
| Bloqueado / suspenso | Não libera |
| Rosto não reconhecido | Tela com botões (tentar, cadastrar, chamar atendente) |
| Liberação manual (recepção) | Libera (auditada) |

**Importante:** se o totem **libera** alguém sem matrícula, verifique se o rosto foi cadastrado em outro aluno **com** matrícula (ex.: teste Jorge vs Fernanda).

Logs: `%LOCALAPPDATA%\FHT\Access\logs\access-YYYYMMDD.log`  
Procure linhas `Face identify:` com `allowed=` e `reason=`.

---

## 9. Roteiro de teste no dia da instalação

Faça **com recepção presente**:

| # | Teste | Resultado esperado |
|---|--------|-------------------|
| 1 | Aluno vigente com facial | Nome + “Entrada registrada” (~4 s) + catraca abre |
| 2 | Aluno sem matrícula com facial | “Não foi possível liberar. Procure a recepção.” |
| 3 | Rosto desconhecido | “Não reconhecemos você” + botões de ajuda |
| 4 | Liberação manual | Catraca abre + evento no Gestão |
| 5 | Desligar rede 2 min e liberar vigente | Deve usar cache local; eventos enfileiram e sobem depois |
| 6 | Sync | Admin mostra último sync recente; eventos aparecem no Gestão |

Alunos úteis para teste (Unidade Centro, conferir no Gestão antes):

- **Jorge Neto** — plano vigente, facial cadastrada → deve liberar
- **Fernanda Lima da Silva** — sem matrícula → deve recusar (se facial nela)

---

## 10. Piloto assistido — checklist completo

### Antes de ir à academia

- [ ] Gestão API acessível pela rede da academia (não `localhost`)
- [ ] Dispositivo de acesso criado no Gestão (unitId + deviceId + secret)
- [ ] Pacote `publish\win-x64` gerado e testado no seu PC
- [ ] PIN admin alterado (`adminPin`)
- [ ] IP da catraca anotado; ping OK na rede da academia
- [ ] Webcam escolhida e `webcamIndex` definido
- [ ] Limpar faciais de teste no PC de dev (ou **não** copiar `access.db`)

### No dia — instalação (30–45 min)

- [ ] Copiar pasta para `C:\FHT\Access\`
- [ ] Editar `appsettings.json` (Gestão + catraca + `useFakeTurnstile: false`)
- [ ] Executar app; confirmar câmera e tela cheia
- [ ] Sync full de membros
- [ ] Conectar catraca (Admin → Catraca ou auto-connect)
- [ ] Cadastrar 2–3 alunos reais (1 vigente, 1 sem matrícula para validar negação)

### Durante a semana de piloto

- [ ] Recepção treinada: manual, cadastro facial, quando chamar TI
- [ ] Verificar logs diários (`Face identify`, `Auto-sync`)
- [ ] Confirmar eventos de entrada no Gestão
- [ ] Anotar falsos positivos/negativos de reconhecimento
- [ ] Backup semanal de `%LOCALAPPDATA%\FHT\Access\access.db`

### Critérios para sair do piloto

- [ ] Catraca abre de forma consistente com vigente
- [ ] Sem matrícula **nunca** libera automaticamente
- [ ] Sync estável (online/offline)
- [ ] Recepção domina fluxo manual e cadastro
- [ ] Nenhum rosto duplicado em perfis errados

---

## 11. Solução de problemas

| Sintoma | O que verificar |
|---------|-----------------|
| “Não reconhece” sempre | Totem em modo automático? `RecognitionEnabled` — sair do atendimento; `faceMatchThreshold` ≤ 0.35 |
| Libera quem não deveria | Log `Face identify:` — qual nome? Rosto cadastrado no aluno errado |
| Gestão offline | URL, firewall, `device-auth`; totem funciona offline com cache antigo |
| Catraca não abre | `useFakeTurnstile`, IP, cabo/rede, Admin → status catraca |
| Busca lenta | Normal se rede lenta; busca local primeiro; Gestão só se não achar |
| App não abre | Antivirus bloqueando; executar como usuário normal; ver `boot.log` em `%LOCALAPPDATA%\FHT\Access\` |

Atalhos:

- **Ctrl+Shift+A** — modo atendente / admin
- **Tray** — reabrir janela ou sair

---

## 12. Atualizar versão depois

1. No PC de dev: gerar novo `publish\win-x64`
2. Fechar FHT Acesso na academia
3. Substituir arquivos em `C:\FHT\Access\` (**não** apagar `%LOCALAPPDATA%\FHT\Access\`)
4. Reabrir o app — config e faciais permanecem

---

## Referências

- [ARCHITECTURE.md](ARCHITECTURE.md)
- [FHT_GESTAO_INTEGRATION.md](FHT_GESTAO_INTEGRATION.md)
- [OFFLINE_SYNC.md](OFFLINE_SYNC.md)
- [TOLETUS.md](TOLETUS.md)
- [FACE_RECOGNITION.md](FACE_RECOGNITION.md)
