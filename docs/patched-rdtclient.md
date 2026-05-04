# Patched RDTClient Image

This wrapper normally runs upstream `rogerfar/rdtclient`. The private Bindery
pipeline currently uses a local image built from upstream RDTClient plus
`patches/rdtclient-torbox-materialization-20260504.patch`.

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

`Provider:MaxParallelDownloads` should stay at `1` for this deployment because
TorBox rate limiting was already observed.

## Build

Clone upstream RDTClient, apply the patch, then build the local image:

```sh
git clone https://github.com/rogerfar/rdt-client.git rdt-client
cd rdt-client
git checkout f5ea1e0
git apply ../patches/rdtclient-torbox-materialization-20260504.patch
docker build --platform linux/arm64/v8 \
  -t rdtclient-torbox-lidarr:slot-starvation-20260504 .
```

Then configure this wrapper:

```sh
RDTCLIENT_IMAGE=rdtclient-torbox-lidarr
RDTCLIENT_TAG=slot-starvation-20260504
docker compose up -d
```

## Validation

The deployed image was built on the Oracle arm64 host. Build-time tests passed:

- `RdtClient.Service.Test`: 174 passed
- `RdtClient.Web.Test`: 24 passed

Focused test filter before the full build:

```sh
dotnet test RdtClient.Service.Test/RdtClient.Service.Test.csproj \
  --filter "FullyQualifiedName~TorrentRunnerTest|FullyQualifiedName~TorBoxDebridClientTest|FullyQualifiedName~BezzadDownloaderTest"
```
