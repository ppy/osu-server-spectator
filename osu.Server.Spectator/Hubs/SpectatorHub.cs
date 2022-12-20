// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using osu.Game.Beatmaps;
using osu.Game.Online.Spectator;
using osu.Game.Scoring;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;

namespace osu.Server.Spectator.Hubs
{
    public class SpectatorHub : StatefulUserHub<ISpectatorClient, SpectatorClientState>, ISpectatorServer
    {
        public const string REPLAYS_PATH = "replays";

        private readonly IDatabaseFactory databaseFactory;
        private readonly ScoreUploader scoreUploader;
        private readonly IScoreProcessedSubscriber scoreProcessedSubscriber;

        public SpectatorHub(
            IDistributedCache cache,
            EntityStore<SpectatorClientState> users,
            IDatabaseFactory databaseFactory,
            ScoreUploader scoreUploader,
            IScoreProcessedSubscriber scoreProcessedSubscriber)
            : base(cache, users)
        {
            this.databaseFactory = databaseFactory;
            this.scoreUploader = scoreUploader;
            this.scoreProcessedSubscriber = scoreProcessedSubscriber;
        }

        public async Task BeginPlaySession(long? scoreToken, SpectatorState state)
        {
            using (var usage = await GetOrCreateLocalUserState())
            {
                var clientState = (usage.Item ??= new SpectatorClientState(Context.ConnectionId, CurrentContextUserId));

                if (clientState.State != null)
                {
                    // Previous session never received EndPlaySession call.
                    // Should probably be handled in some way.
                }

                clientState.State = state;
                clientState.ScoreToken = scoreToken;

                if (state.RulesetID == null) throw new ArgumentNullException(nameof(state.RulesetID));
                if (state.BeatmapID == null) throw new ArgumentNullException(nameof(state.BeatmapID));

                using (var db = databaseFactory.GetInstance())
                {
                    string? beatmapChecksum = await db.GetBeatmapChecksumAsync(state.BeatmapID.Value);
                    string? username = await db.GetUsernameAsync(CurrentContextUserId);

                    if (string.IsNullOrEmpty(beatmapChecksum))
                        throw new ArgumentException(nameof(state.BeatmapID));

                    clientState.Score = new Score
                    {
                        ScoreInfo =
                        {
                            APIMods = state.Mods.ToArray(),
                            User =
                            {
                                Id = CurrentContextUserId,
                                Username = username,
                            },
                            Ruleset = LegacyHelper.GetRulesetFromLegacyID(state.RulesetID.Value).RulesetInfo,
                            BeatmapInfo = new BeatmapInfo
                            {
                                OnlineID = state.BeatmapID.Value,
                                MD5Hash = beatmapChecksum,
                            },
                            MaximumStatistics = state.MaximumStatistics
                        }
                    };
                }
            }

            // let's broadcast to every player temporarily. probably won't stay this way.
            await Clients.All.UserBeganPlaying(CurrentContextUserId, state);
        }

        public async Task SendFrameData(FrameDataBundle data)
        {
            using (var usage = await GetOrCreateLocalUserState())
            {
                var score = usage.Item?.Score;

                // Score may be null if the BeginPlaySession call failed but the client is still sending frame data.
                // For now it's safe to drop these frames.
                if (score == null)
                    return;

                score.ScoreInfo.Statistics = data.Header.Statistics;
                score.ScoreInfo.MaxCombo = data.Header.MaxCombo;
                score.ScoreInfo.Combo = data.Header.Combo;
                // TODO: TotalScore should probably be populated as well, but needs beatmap max combo.

                score.Replay.Frames.AddRange(data.Frames);

                await Clients.Group(GetGroupId(CurrentContextUserId)).UserSentFrames(CurrentContextUserId, data);
            }
        }

        public async Task EndPlaySession(SpectatorState state)
        {
            using (var usage = await GetOrCreateLocalUserState())
            {
                try
                {
                    Score? score = usage.Item?.Score;
                    long? scoreToken = usage.Item?.ScoreToken;

                    // Score may be null if the BeginPlaySession call failed but the client is still sending frame data.
                    // For now it's safe to drop these frames.
                    if (score == null || scoreToken == null)
                        return;

                    score.ScoreInfo.Date = DateTimeOffset.UtcNow;

                    scoreUploader.Enqueue(scoreToken.Value, score);
                    await scoreProcessedSubscriber.RegisterForNotificationAsync(Context.ConnectionId, CurrentContextUserId, scoreToken.Value);
                }
                finally
                {
                    usage.Destroy();
                }
            }

            await endPlaySession(CurrentContextUserId, state);
        }

        public async Task StartWatchingUser(int userId)
        {
            Log($"Watching {userId}");

            try
            {
                SpectatorState? spectatorState;

                // send the user's state if exists
                using (var usage = await GetStateFromUser(userId))
                    spectatorState = usage.Item?.State;

                if (spectatorState != null)
                    await Clients.Caller.UserBeganPlaying(userId, spectatorState);
            }
            catch (KeyNotFoundException)
            {
                // user isn't tracked.
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupId(userId));
        }

        public async Task EndWatchingUser(int userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupId(userId));
        }

        public override async Task OnConnectedAsync()
        {
            // for now, send *all* player states to users on connect.
            // we don't want this for long, but while the lazer user base is small it should be okay.
            foreach (var kvp in GetAllStates())
                await Clients.Caller.UserBeganPlaying((int)kvp.Key, kvp.Value.State);

            await base.OnConnectedAsync();
        }

        protected override async Task CleanUpState(SpectatorClientState state)
        {
            if (state.State != null)
                await endPlaySession(state.UserId, state.State);

            await base.CleanUpState(state);
        }

        public static string GetGroupId(int userId) => $"watch:{userId}";

        private async Task endPlaySession(int userId, SpectatorState state)
        {
            // Ensure that the state is no longer playing (e.g. if client crashes).
            if (state.State == SpectatedUserState.Playing)
                state.State = SpectatedUserState.Quit;

            await Clients.All.UserFinishedPlaying(userId, state);
        }
    }
}
