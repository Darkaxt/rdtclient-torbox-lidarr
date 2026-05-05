# Patched RDTClient Image

This wrapper normally runs upstream `rogerfar/rdtclient`. The private Bindery
pipeline currently uses a local image built from upstream RDTClient plus
these wrapper patches, applied in order:

1. `patches/rdtclient-torbox-materialization-20260504.patch`
2. `patches/rdtclient-slot-recovery-20260505.patch`
3. `patches/rdtclient-torbox-delete-control-id-20260505.patch`

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
docker build --platform linux/arm64/v8 \
  -t rdtclient-torbox-lidarr:torbox-stale-child-recovery-20260505 .
```

Then configure this wrapper:

```sh
RDTCLIENT_IMAGE=rdtclient-torbox-lidarr
RDTCLIENT_TAG=torbox-stale-child-recovery-20260505
docker compose up -d
```

## Validation

The deployed image was built on the Oracle arm64 host. Build-time tests passed:

- `RdtClient.Service.Test`: 184 passed in the deployed patch-stack image build
- `RdtClient.Web.Test`: 24 passed
- `TorrentRunnerSlotRecoveryTest`: proves stale started Bezzad child downloads
  free the active download slot by retrying or failing cleanly.
- `TorBoxDebridClientTest.Delete_CallsTorrentsControl_WhenTypeIsTorrent`: proves
  torrent delete resolves and uses TorBox's numeric control id.
- `QBittorrentTest.TorrentInfo_ShouldNotReportComplete_WhenProviderFinishedButNoLocalDownloadsExist`:
  proves provider-only completion is not exposed to Bindery as qB completion.

Focused test filter before the full build:

```sh
dotnet test RdtClient.Service.Test/RdtClient.Service.Test.csproj \
  --filter "FullyQualifiedName~TorrentRunnerSlotRecoveryTest|FullyQualifiedName~Delete_WhenDownloadIsActive_RemovesActiveDownloadBeforeDeletingData|FullyQualifiedName~Delete_WhenTorBoxRemoteDeleteFails_StillDeletesLocalData|FullyQualifiedName~TorrentRunnerTest|FullyQualifiedName~TorBoxDebridClientTest|FullyQualifiedName~BezzadDownloaderTest"
```
