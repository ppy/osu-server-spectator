# osu-server-spectator [![dev chat](https://discordapp.com/api/guilds/188630481301012481/widget.png?style=shield)](https://discord.gg/ppy)

A server that handles incoming and outgoing spectator data, for active players looking to watch or broadcast to others.

# Testing

To deploy this as part of a full osu! server stack deployment, [this wiki page](https://github.com/ppy/osu/wiki/Testing-web-server-full-stack-with-osu!) will serve as a good reference.

## Environment variables

For advanced testing purposes.

| Envvar name | Description                                                                                                                                                                                                                                    | Default value     |
| :- |:-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|:------------------|
| `SAVE_REPLAYS` | Whether to save received replay frames from clients to replay files. `1` to enable, any other value to disable.                                                                                                                                | `""` (disabled)   |
| `REPLAY_UPLOAD_THREADS` | Number of threads to use when uploading complete replays. Must be positive number.                                                                                                                                                             | `1`               |
| `REPLAYS_PATH` | Local path to store complete replay files (`.osr`) to. Only used if [`FileScoreStorage`](https://github.com/ppy/osu-server-spectator/blob/master/osu.Server.Spectator/Storage/FileScoreStorage.cs) is active.                                  | `./replays/`      |
| `S3_KEY` | An access key ID to use for uploading replays to [AWS S3](https://aws.amazon.com/s3/). Only used if [`S3ScoreStorage`](https://github.com/ppy/osu-server-spectator/blob/master/osu.Server.Spectator/Storage/S3ScoreStorage.cs) is active.      | `""`              |
| `S3_SECRET` | The secret access key to use for uploading replays to [AWS S3](https://aws.amazon.com/s3/). Only used if [`S3ScoreStorage`](https://github.com/ppy/osu-server-spectator/blob/master/osu.Server.Spectator/Storage/S3ScoreStorage.cs) is active. | `""`              |
| `REPLAYS_BUCKET` | The name of the [AWS S3](https://aws.amazon.com/s3/) bucket to upload replays to. Only used if [`S3ScoreStorage`](https://github.com/ppy/osu-server-spectator/blob/master/osu.Server.Spectator/Storage/S3ScoreStorage.cs) is active.           | `""`              |
| `TRACK_BUILD_USER_COUNTS` | Whether to track how many users are on a particular build of the game and report that information to the database at `DB_{HOST,PORT}`. `1` to enable, any other value to disable.                                                              | `""` (disabled)   |
| `CHECK_CLIENT_VERSION` | Whether to check whether users connecting to the server are on a build of the game that supports online play. | `""` (disabled) |
| `SERVER_PORT` | Which port the server should listen on for incoming connections.                                                                                                                                                                               | `80`              |
| `REDIS_HOST` | Connection string to `osu-web` Redis instance.                                                                                                                                                                                                 | `localhost`       |
| `DD_AGENT_HOST` | Hostname under which the [Datadog](https://www.datadoghq.com/) agent host can be found.                                                                                                                                                        | `localhost`       |
| `DB_HOST` | Hostname under which the `osu-web` MySQL instance can be found.                                                                                                                                                                                | `localhost`       |
| `DB_PORT` | Port under which the `osu-web` MySQL instance can be found.                                                                                                                                                                                    | `3306`            |
| `DB_USER` | Username to use when logging into the `osu-web` MySQL instance.                                                                                                                                                                                | `osuweb`          |
| `SENTRY_DSN` | A valid Sentry DSN to use for logging application events.                                                                                                                                                                                      | `null` (required in production) |
| `SHARED_INTEROP_DOMAIN` | The root URL of the osu-web instance to which shared interop calls should be submitted                                                                                                                                                         | `http://localhost:80` |
| `SHARED_INTEROP_SECRET` | The value of the same environment variable that the target osu-web instance specifies in `.env`.                                                                                                                                               | `null` (required) |
| `BANCHO_BOT_USER_ID` | The value of the same environment variable that the target osu-web instance specified in `.env`.                                                                                                                                               | 3 |
| `MATCHMAKING_ROOM_ROUNDS` | The number of rounds in a matchmaking room.                                                                                                                                                                                                    | 5 |
| `MATCHMAKING_ALLOW_SKIP` | Whether users are allowed to skip matchmaking stages.                                                                                                                                                                                          | false | 
| `MATCHMAKING_LOBBY_UPDATE_RATE` | The rate (in seconds) at which to update the matchmaking lobby                                                                                                                                                                                 | 5 |
| `MATCHMAKING_QUEUE_UPDATE_RATE` | The rate (in seconds) at which to update the matchmaking queues                                                                                                                                                                                | 1 |
| `MATCHMAKING_QUEUE_BAN_DURATION` | The duration (in seconds) for which players are temporarily banned from the matchmaking queue after declining an invitation.                                                                                                                  | 60 |
| `MATCHMAKING_RATING_INITIAL_RADIUS` | The initial ELO search radius. | 20 |
| `MATCHMAKING_RATING_RADIUS_INCREASE_TIME` | The amount of time (in seconds) before each doubling of the ELO search radius. | 15 |
| `MATCHMAKING_POOL_SIZE` | The number of beatmaps per matchmaking room. | 50 |
