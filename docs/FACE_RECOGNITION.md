# Face recognition

## Engine

`LocalHistogramFaceService` (`FHT.Access.Face`):

- Haar crops the face for matching even without the SFace ONNX model
- SFace cosine cutoff defaults to `0.35` (and spatial/histogram to `0.50`) when settings still have the old `0.92` threshold
- Cadastro recusa frame sem rosto detectado
- Plano vigente libera a catraca mesmo com título em aberto (dívida fica como revisão, não bloqueio)

Not production-grade biometrics — suitable for MVP / lab. Swap `IFaceRecognitionService` later without changing the access flow.

## API

- `EnrollAsync(memberId, imageBgrOrJpeg)`
- `IdentifyAsync(...)` → `FaceMatchResult?`
- `RemoveAsync(memberId)`

## Admin

**Face** tab: pick local member → enroll from current webcam JPEG → **Identify test** reports score.
Kiosk calls identify every N frames while Idle (via `AccessFlowService`).
