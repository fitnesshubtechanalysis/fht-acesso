# Toletus (Catraca LiteNet3)

## Protocolo correto (não é LiteNet2)

A LiteNet3 **não** recebe Release “cego” pelo IP. Fluxo obrigatório:

1. Resolver a NIC Windows ligada à catraca (ex. **Ethernet 2** → `192.168.0.120`)
2. `LiteNetUtil.Search(ipv4DaNic)` + aguardar respostas UDP `:7878`
3. Obter **IP + Serial** da discovery
4. `LiteNet3Board.CreateFromBase(...)`
5. `ConnectAsync("Ethernet 2")` — sobe WebSocket em `ws://192.168.0.120:<porta>/` e envia **SetServer** à placa
6. Esperar **`Connected == true`** (placa conecta de volta no PC)
7. Só então `ReleaseEntry` / `ReleaseExit` / `ReleaseEntryAndExit`

## Abstraction

`ITurnstile` (`Domain`): `ConnectAsync` / `DisconnectAsync` / `ReleaseEntryAsync` / `ReleaseExitAsync`, plus `StateChanged` and `PassageReceived`.

| Class | Uso |
|-------|-----|
| `FakeTurnstile` | Dev/CI |
| `ToletusLiteNetTurnstile` | Hardware via `vendor/litenet3` |

## Config (`appsettings.json`)

| Campo | Exemplo | Notas |
|-------|---------|-------|
| `turnstileNetwork` | `Ethernet 2` | **Nome da NIC Windows** (não use só o IP; IP também resolve, mas o Connect usa o nome) |
| `turnstileIp` | `192.168.0.100` | Filtro opcional na discovery |
| `turnstileSerial` | *(vazio ou da etiqueta)* | Preenchido automaticamente após Connect bem-sucedido |
| `useFakeTurnstile` | `false` | Produção |

PC catraca: `192.168.0.120/24` na Ethernet 2 · Placa: `192.168.0.100`.

## Logs

Arquivo `%ProgramData%\FHT\Access\logs\` — prefixo `[Toletus]`:

- NIC escolhida / IPv4 local
- Placas descobertas (IP, Serial)
- URI WebSocket / porta dinâmica (`ServerUri`)
- SetServer / Connected / timeout completo
- Reconexão automática (backoff 2→30 s) via `TurnstileConnectionSupervisor`

## Admin smoke

1. Network = `Ethernet 2`, IP = `192.168.0.100`, Serial pode ficar vazio
2. **Connect** → State `Connected` (somente com `board.Connected == true`) + serial preenchido
3. **Liberar Entrada/Saída** — habilitados só quando conectado
4. Firewall: permitir entrada TCP na porta dinâmica do `FHT.Access.App` em `192.168.0.120`
5. Rede Windows: **Privada** no adaptador Ethernet da catraca
