# Rollback

This wrapper keeps RDTClient state isolated so rollback is straightforward.

## Stop The Service

```sh
docker compose down
```

## Preserve State

Before destructive cleanup, back up:

- `/srv/rdtclient/config`
- `/srv/rdtclient/downloads`

Do not publish those directories. They can contain credentials, history, or
download metadata.

## Lidarr Rollback

If RDTClient should no longer be used:

1. Disable the `RDTClient TorBox` download client in Lidarr.
2. Reassign affected indexers to another download client.
3. Confirm Lidarr queue is clean.
4. Stop the RDTClient container.

Do not remove downloaded files until Lidarr has either imported or explicitly
discarded them.
