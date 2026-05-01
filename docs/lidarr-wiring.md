# Lidarr Wiring

Use this deployment as a private download-client bridge for Lidarr.

## Download Client

Add a qBittorrent-compatible download client in Lidarr:

- Name: `RDTClient TorBox`
- Host: `rdtclient`
- Port: `6500`
- Category: `lidarr`
- Remove completed downloads: enabled
- Priority: lower than any specialized source that should be preferred

The Lidarr container and RDTClient container must share a Docker network.
The provided compose file expects an external network named
`lidarr-stack_default`.

## Paths

Recommended path model:

- RDTClient host downloads: `/srv/rdtclient/downloads`
- RDTClient container path: `/data/downloads`
- Lidarr container path to the same host folder: `/data/downloads`
- Final Lidarr library path: your canonical music library mount

Do not mount the final music library into RDTClient. Let Lidarr import from the
download folder into the canonical library.

## Indexers

Prowlarr or native Lidarr Torznab indexers can use this download client.
Specialized synthetic download clients should be pinned explicitly so generic
torrent releases do not route to the wrong client.
