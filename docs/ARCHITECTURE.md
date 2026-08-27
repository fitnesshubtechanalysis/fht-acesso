# Architecture

## Layers

```
FHT.Access.App (WPF)
  └─ ViewModels → Application services
       ├─ FHT.Access.Application  (flow / sync / decision)
       ├─ FHT.Access.Face         (IFaceRecognitionService)
       ├─ FHT.Access.Toletus      (ITurnstile)
       └─ FHT.Access.Infrastructure (SQLite, JSON settings, HTTP, FileLogger)
            └─ FHT.Access.Domain
```

## Startup (DI)

`App.OnStartup` builds a generic host:

1. `AddFhtAccessInfrastructure()` — settings, SQLite, repos, `IGestaoAccessClient`, `FileLogger`
2. `RegisterFake()` **or** `RegisterToletus()` from `AppSettings.UseFakeTurnstile`
3. `LocalHistogramFaceService` as `IFaceRecognitionService`
4. `AddFhtAccessApplication()` — decision, events, turnstile, sync, recognition, flow, device
5. WPF: `WebcamService`, kiosk/admin VMs, `MainWindow`
6. `DatabaseInitializer.EnsureCreated()`

## Access flow (entry)

`AccessFlowService.ProcessEntryAsync(jpeg)`:

1. `RecognitionService.IdentifyAndDecideAsync` (face → member → `AccessDecisionService`)
2. If denied → record event + UI *"Acesso não liberado"* (no finance codes)
3. If allowed → `TurnstileService.ReleaseEntryAsync` → `WaitForPassageAsync` → record allowed event + enqueue pending sync

## Persistence

- SQLite `%LocalAppData%\FHT\Access\access.db` — members, faces, events, pending sync, logs
- `appsettings.json` beside DB — device credentials, turnstile, webcam, PIN, sync cursors

## UI modes

- **Student kiosk** — fullscreen dark, circular webcam, status overlay, cooldown ~3s
- **Admin shell** — tabs Geral / Gestão / Webcam / Face / Catraca / Sync / Rede / Diagnóstico
