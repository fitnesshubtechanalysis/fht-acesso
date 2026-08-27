# FHT Access

Desktop kiosk for gym/unit access control: face recognition → local decision → turnstile (Toletus LiteNet3) → sync with **fht-gestao-api**.

## Projects

| Project | Role |
|---------|------|
| `FHT.Access.App` | .NET 8 WPF kiosk + admin shell |
| `FHT.Access.Application` | Access flow, sync, recognition orchestration |
| `FHT.Access.Domain` | Entities, enums, abstractions |
| `FHT.Access.Infrastructure` | SQLite, settings JSON, Gestão HTTP client, logging |
| `FHT.Access.Face` | Local histogram face engine (OpenCvSharp) |
| `FHT.Access.Toletus` | Real + `FakeTurnstile` adapters |
| `FHT.Access.Tests` | Unit tests |

## Docs

- **[Instalação na academia](docs/INSTALACAO.md)** — publish, config, catraca, piloto
- [Architecture](docs/ARCHITECTURE.md)
- [Toletus / catraca](docs/TOLETUS.md)
- [Face recognition](docs/FACE_RECOGNITION.md)
- [Gestão integration](docs/FHT_GESTAO_INTEGRATION.md)
- [Offline sync](docs/OFFLINE_SYNC.md)
- [Docs index](docs/README.md)

## Run

```bash
dotnet build FHT.Access.sln
dotnet run --project src/FHT.Access.App
dotnet test tests/FHT.Access.Tests
```

Settings & DB default to `%LocalAppData%\FHT\Access\`.

**Admin:** `Ctrl+Shift+A` or tap bottom-right corner → PIN (default `1234`).
