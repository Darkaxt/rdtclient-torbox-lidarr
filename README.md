# RDTClient TorBox For Lidarr

This repository is a sanitized deployment wrapper for using upstream
RDTClient as a private qBittorrent-compatible TorBox bridge for Lidarr.

It is not a fork of RDTClient and does not contain a TorBox API key,
RDTClient database, logs, downloads, or live Lidarr configuration.

## Layout

- `compose.yaml` runs upstream `rogerfar/rdtclient` by default, or a local
  patched image via `RDTCLIENT_IMAGE` / `RDTCLIENT_TAG`.
- `.env.example` contains non-secret deployment defaults.
- `patches/` contains source patches applied to upstream RDTClient for this
  private deployment.
- `scripts/check-health.sh` verifies the private HTTP bind.
- `docs/lidarr-wiring.md` describes the Lidarr download-client setup.
- `docs/patched-rdtclient.md` describes the private TorBox materialization and
  slot-recovery patches.
- `docs/rollback.md` covers safe rollback and cleanup boundaries.

## Quick Start

1. Copy `.env.example` to `.env` and adjust paths, UID/GID, timezone, and bind.
2. Start the container:

   ```sh
   docker compose up -d
   ```

   To run the patched local image built from `patches/`, set:

  ```sh
  RDTCLIENT_IMAGE=rdtclient-torbox-lidarr
  RDTCLIENT_TAG=torbox-stale-child-recovery-20260505
  ```

3. Open RDTClient only from the host or a trusted tunnel and configure your own
   TorBox API key in RDTClient. Do not commit that key.
4. In Lidarr, add a qBittorrent-compatible download client that points to the
   RDTClient container over the private Docker network.

## Safety Defaults

- The compose file binds RDTClient to `127.0.0.1`.
- Download state lives under `/srv/rdtclient/downloads` by default.
- Persistent app state lives under `/srv/rdtclient/config` by default.
- No Google Drive or final music library path is mounted into RDTClient.
- Lidarr should be the only service importing completed downloads.

## Secret Handling

Never commit:

- `.env`
- RDTClient admin password files
- TorBox API keys
- Lidarr API keys
- `db/`, `logs/`, or `downloads/`
- exported Lidarr/Prowlarr config JSON
