# Patched RDTClient Image

This wrapper normally runs upstream `rogerfar/rdtclient`. The private Bindery
pipeline currently uses a local image built from upstream RDTClient plus
these wrapper patches, applied in order:

1. `patches/rdtclient-torbox-materialization-20260504.patch`
2. `patches/rdtclient-slot-recovery-20260505.patch`
3. `patches/rdtclient-torbox-delete-control-id-20260505.patch`
4. `patches/rdtclient-requeue-missing-materialized-files-20260505.patch`
5. `patches/rdtclient-qb-complete-requires-materialized-downloads-20260505.patch`
6. `patches/rdtclient-include-regex-disables-zip-20260508.patch`
7. `patches/rdtclient-torbox-unrestrict-retry-20260526.patch`

## Patch Purpose

The patch fixes three TorBox/RDTClient behaviors observed with the Bindery Hobbit
validation run:

- Provider queue starvation: old TorBox rows stuck in `queued`, `stalled`, or
  `checking` with no visible files and no transfer speed no longer occupy the
  single conservative provider slot forever.
- TorBox torrent creation: torrent file and magnet uploads explicitly send
  `allowZip=false` so large selective torrents are not allowed to fall back to
  provider-side zip packaging during creation.
- Host materialization: the internal Bezzad downloader promotes completed
  `<filename>.download` files to the final filename before RDTClient marks the
  download complete.
- Local slot recovery: deleting torrents removes active Bezzad download/unpack
  clients before provider cleanup, TorBox remote deletion is best-effort, and
  stale started Bezzad downloads are reset or failed so the single download slot
  is not held forever.
- TorBox remote cleanup: RDTClient stores TorBox torrent hashes as `RdId`, but
  TorBox delete/control calls require the numeric torrent id, so delete now
  resolves the numeric id from the hash, the current list, or the queued list
  before calling `control=delete`.
- qBittorrent completion signal: RDTClient no longer reports a TorBox torrent as
  qB-complete just because local RDT state has a completion timestamp or because
  the provider reached 100% before local file materialization. qB `progress=1`,
  `pausedUP`, file progress, and `completion_on` are now reserved for successful
  local materialization, or for explicit `DownloadNone` torrents.
- Stale completed re-adds: when the same torrent hash is added again and RDT has
  completed download rows but the local materialized files are missing, RDT resets
  those child downloads and clears torrent completion so the qB-compatible path
  materializes the files again instead of returning a stale complete row.
- qB false-complete guard: local byte counters alone no longer make qB
  `progress=1` / `pausedUP`; every selected child download must have a successful
  local completion timestamp before Bindery sees the torrent as complete.
- Include-regex materialization: when Bindery sends a filtered torrent with
  `IncludeRegex`, TorBox file downloads are requested individually even if
  `PreferZippedDownloads` is enabled, preventing cached audiobook package rows
  from becoming provider-side zip downloads.
- Selected-file unrestrict retry: transient TorBox per-file
  `RequestDownloadAsync`/unrestrict failures are retried before RDT marks the
  selected child downloads and parent torrent terminal. TorBox include-regex
  torrents with a zero configured download retry budget now get a default budget
  of three attempts. Re-adding an already-terminal selected-file torrent also
  resets completed child rows that failed before a local download started, so
  explicit retries do not immediately reuse stale qB-terminal state. If a
  re-add later creates successful duplicate child rows for the same selected
  files, the old pre-start failures no longer poison parent completion.

`Provider:MaxParallelDownloads` should stay at `1` for this deployment because
TorBox rate limiting was already observed.

## Build

Clone upstream RDTClient, apply the patch, then build the local image:

```sh
git clone https://github.com/rogerfar/rdt-client.git rdt-client
cd rdt-client
git checkout f5ea1e0
git apply ../patches/rdtclient-torbox-materialization-20260504.patch
git apply ../patches/rdtclient-slot-recovery-20260505.patch
git apply ../patches/rdtclient-torbox-delete-control-id-20260505.patch
git apply ../patches/rdtclient-requeue-missing-materialized-files-20260505.patch
git apply ../patches/rdtclient-qb-complete-requires-materialized-downloads-20260505.patch
git apply ../patches/rdtclient-include-regex-disables-zip-20260508.patch
git apply ../patches/rdtclient-torbox-unrestrict-retry-20260526.patch
docker build --platform linux/arm64/v8 \
  -t rdtclient-torbox-lidarr:torbox-unrestrict-retry-20260526 .
```

Then configure this wrapper:

```sh
RDTCLIENT_IMAGE=rdtclient-torbox-lidarr
RDTCLIENT_TAG=torbox-unrestrict-retry-20260526
docker compose up -d
```

## Validation

The deployed image was built on the Oracle arm64 host. Build-time tests passed:

- `RdtClient.Service.Test`: 183 passed in the deployed patch-stack image build
- `RdtClient.Web.Test`: 24 passed
- `TorrentRunnerSlotRecoveryTest`: proves stale started Bezzad child downloads
  free the active download slot by retrying or failing cleanly.
- `TorBoxDebridClientTest.Delete_CallsTorrentsControl_WhenTypeIsTorrent`: proves
  torrent delete resolves and uses TorBox's numeric control id.
- `QBittorrentTest.TorrentInfo_ShouldNotReportComplete_WhenProviderFinishedButNoLocalDownloadsExist`:
  proves provider-only completion is not exposed to Bindery as qB completion.
- `TorrentsTest.AddMagnetToDebridQueue_WhenExistingCompletedTorrentHasNoLocalFiles_ShouldResetItForMaterialization`:
  proves a stale completed hash is requeued when its materialized files are gone.
- `QBittorrentTest.TorrentInfo_ShouldNotReportComplete_WhenLocalBytesAreFullButChildDownloadsAreNotCompleted`:
  proves full local byte counters without child completion timestamps are not
  exposed as qB completion.
- `TorBoxDebridClientTest.GetDownloadInfos_IgnoresPreferZip_WhenIncludeRegexIsSet`:
  proves filtered torrents with `IncludeRegex` return individual TorBox file
  links rather than `/zip`.
- `TorrentRunnerSlotRecoveryTest.TryRequeuePreStartUnrestrictFailure_WhenTorBoxRequestDownloadIsTransient_RequeuesDownloadAndClearsTorrentCompletion`:
  proves transient TorBox selected-file unrestrict failures are requeued instead
  of completed with errors.
- `TorrentsTest.AddMagnetToDebridQueue_WhenTorBoxIncludeRegexHasZeroDownloadRetries_ShouldStoreDefaultSelectedFileRetryBudget`:
  proves new TorBox include-regex torrents get a retry budget when the configured
  budget is zero.
- `TorrentsTest.AddMagnetToDebridQueue_WhenExistingSelectedFileTorrentFailedBeforeDownloadStarted_ShouldResetItForMaterialization`:
  proves explicit re-adds reset stale selected-file rows that failed before
  local materialization began.
- `FilteredTorrentMergeTests.UpdateComplete_WhenSelectedFilePreStartFailureWasSupersededBySuccessfulDuplicate_DoesNotPoisonParent`:
  proves successful duplicate child rows override old pre-start selected-file
  failures during parent completion accounting.

Focused test filter before the full build:

```sh
dotnet test RdtClient.Service.Test/RdtClient.Service.Test.csproj \
  --filter "FullyQualifiedName~TorrentRunnerSlotRecoveryTest|FullyQualifiedName~AddMagnetToDebridQueue_WhenExistingCompletedTorrentHasNoLocalFiles_ShouldResetItForMaterialization|FullyQualifiedName~Delete_WhenDownloadIsActive_RemovesActiveDownloadBeforeDeletingData|FullyQualifiedName~Delete_WhenTorBoxRemoteDeleteFails_StillDeletesLocalData|FullyQualifiedName~TorrentRunnerTest|FullyQualifiedName~TorBoxDebridClientTest|FullyQualifiedName~BezzadDownloaderTest"
```
