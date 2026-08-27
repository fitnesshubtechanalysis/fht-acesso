# FHT Acesso — Fluxo operacional

## Modos (`AccessOperatingMode`)

| Modo | RecognitionEnabled |
|------|-------------------|
| Automatic | sim |
| Attendant | não |
| Enrollment | não |
| Maintenance | não |

Motor (`AutomaticAccessEngine`) só processa faces em **Automatic**. Fechar a janela **não** encerra o motor (System Tray).

## UI pública

Idle/propaganda → reconhecimento → liberado / não reconhecido / negado → idle.

Não reconhecido (implantação): mensagem simples pedindo recepção — **não** abre cadastro sozinho.

## Modo atendente

Tray / Ctrl+Shift+A → PIN → dashboard → cadastrar facial / buscar / liberação manual.

Timeout de ociosidade (~5 min) retorna a Automatic.

## Peças-chave

- `OperatingModeService`, `AccessStateMachine`, `CameraCoordinator`, `RecognitionSessionGuard`
- `AutomaticAccessEngine` + `AttendantSessionService`
- `PublicKioskView` / `AttendantShellView` / `TrayIconService`
