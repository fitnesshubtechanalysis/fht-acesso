# Offline sync

## Queue

Every access event is written to SQLite **and** enqueued in `PendingSync` (`kind = access_event`) by `AccessEventService`.

## Flush

Runs from:

- Admin **Flush pending**
- `BackgroundSyncService` every 2 minutes (also pulls members)

`OfflineSyncService.FlushAsync(unitId)`:

1. Load pending rows
2. Deserialize → `AccessEventDto` list
3. `IGestaoAccessClient.AcknowledgeEventsAsync`
4. On success: remove pending, mark events `Synced`, update `LastEventsSyncAt`
5. On failure: `MarkAttemptAsync` with error (retry later)

## Admin

**Sync** tab: pending count, last members/events sync times, **Flush pending**.

Kiosk **Online/Offline** heuristic: recent sync timestamps + configured Gestão base URL (see `StudentKioskViewModel.RefreshOnlineStatus`).
