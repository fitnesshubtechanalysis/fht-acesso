# FHT Gestão integration

HTTP client: `GestaoAccessClient` → `IGestaoAccessClient`.

Base URL: `AppSettings.GestaoBaseUrl` (Admin → Gestão).

## Endpoints (implemented in `fht-gestao-api`)

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| POST | `/api/v1/access/device-auth` | none | `deviceId` + `deviceSecret` → JWT de dispositivo |
| GET | `/api/v1/units/{unitId}/access/members?updatedSince=&q=` | device ou staff | snapshot de alunos (`id`, `name`, `cpf`, `photoUrl`, `accessAllowed`) |
| POST | `/api/v1/units/{unitId}/access/events` | device ou staff | batch de eventos entry/exit (idempotente por `eventId`) |
| POST | `/api/v1/units/{unitId}/access/members/{memberId}/photo` | device ou staff | JPEG → disco (`PHOTO_STORAGE_DIR`) + `Customer.photoUrl` |
| GET | `/api/v1/media/customers/{customerId}` | device ou staff | serve a foto JPEG |
| POST | `/api/v1/units/{unitId}/access-devices` | staff | cria device e devolve `deviceSecret` **uma vez** |

## Device token

- `tokenKind: "device"`, válido ~24h (totem reautentica perto do expiry / em 401)
- Aceito nas rotas de sync e foto (`requireDeviceOrUserAuth`)
- Staff JWT continua nas rotas legadas `access-events` / `access-devices`

## Offline

Decisão só no cache SQLite. Eventos e fotos vão para `PendingSync` até flush (`access_event` / `member_photo`).

Sync automático a cada **2 min**: auth → flush eventos → flush fotos → pull membros.

## Entrada / saída

Totem usa **toggle de presença**: última passagem allowed = entry → próximo face = exit (e vice-versa). Gestão expõe `present[]` com `enteredAt` + `durationMinutes`.

## Admin (FHT Acesso)

- **Test Auth** — valida credenciais; marca kiosk Online
- **Sync Members Now** — `MemberSyncService` full upsert em SQLite (traz CPF mesmo se o aluno não mudou no Gestão)
- **Flush pending** — envia fila de eventos + fotos

`MemberId` = `Customer.id` (UUID) do Gestão. Motivos financeiros/`reasonCode` nunca aparecem na UI do aluno.
