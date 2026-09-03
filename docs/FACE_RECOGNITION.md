# Face recognition

## Engine

`LocalHistogramFaceService` (`FHT.Access.Face`):

- Haar crops the face for matching even without the SFace ONNX model
- SFace cosine cutoff defaults to `0.32` (spatial ~`0.42`) when settings still have the old `0.92` threshold
- Approach trigger: nearby centered face (≥ ~5% of frame, with 90° probes for portrait mounts)
- Kiosk loop uses **local IdentifyOnly** (no HTTP); online enrich runs **once** after a match (1.5s timeout)
- **Exit lane** uses `LaneRecognitionProfile.Exit`: shorter settle (700 ms), 22 identify attempts, Haar min face 20 px @ 960 px detect width, full 1080p JPEG pipeline
- After **missed passage** (timeout), no person cooldown — totem resets in ~1.5 s for re-recognition
- Identify **never** uses a center-crop fallback when no face is detected
- Turnstile release requires `score >= 0.30`
- Target latency: approach ~0.3s + settle ~0.3s + identify ≤ ~1s + UI floor ~1.2s
- Cadastro recusa frame sem rosto detectado
- Plano vigente libera a catraca mesmo com título em aberto (dívida fica como revisão, não bloqueio)

Not production-grade biometrics — suitable for MVP / lab. Swap `IFaceRecognitionService` later without changing the access flow.

## API

- `EnrollAsync(memberId, imageBgrOrJpeg)`
- `IdentifyAsync(...)` → `FaceMatchResult?`
- `RemoveAsync(memberId)`

## Admin

**Face** tab: pick local member → **Cadastrar facial** / **Remover facial** (memory + SQLite) → **Identify test** reports score.
Kiosk calls identify every N frames while Idle (via `AccessFlowService`).
