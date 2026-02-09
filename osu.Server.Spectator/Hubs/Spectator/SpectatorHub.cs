// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using osu.Game.Beatmaps;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Database;
using osu.Game.Online.Spectator;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Server.Spectator.Authentication;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Extensions;

namespace osu.Server.Spectator.Hubs.Spectator
{
    [Authorize(ConfigureJwtBearerOptions.LAZER_CLIENT_SCHEME)]
    public class SpectatorHub : StatefulUserHub<ISpectatorClient, SpectatorClientState>, ISpectatorServer
    {
        /// <summary>
        /// Minimum beatmap status to save replays for.
        /// </summary>
        private const BeatmapOnlineStatus min_beatmap_status_for_replays = BeatmapOnlineStatus.Ranked;

        /// <summary>
        /// Maximum beatmap status to save replays for.
        /// </summary>
        private const BeatmapOnlineStatus max_beatmap_status_for_replays = BeatmapOnlineStatus.Loved;

        private readonly IDatabaseFactory databaseFactory;
        private readonly ScoreUploader scoreUploader;
        private readonly IScoreProcessedSubscriber scoreProcessedSubscriber;

        public SpectatorHub(
            ILoggerFactory loggerFactory,
            EntityStore<SpectatorClientState> users,
            IDatabaseFactory databaseFactory,
            ScoreUploader scoreUploader,
            IScoreProcessedSubscriber scoreProcessedSubscriber)
            : base(loggerFactory, users)
        {
            this.databaseFactory = databaseFactory;
            this.scoreUploader = scoreUploader;
            this.scoreProcessedSubscriber = scoreProcessedSubscriber;
        }

        public async Task BeginPlaySession(long? scoreToken, SpectatorState state)
        {
            int userId = Context.GetUserId();

            using (var usage = await GetOrCreateLocalUserState())
            {
                var clientState = (usage.Item ??= new SpectatorClientState(Context.ConnectionId, userId));

                if (clientState.State != null)
                {
                    // Previous session never received EndPlaySession call.
                    // Should probably be handled in some way.
                }

                clientState.State = state;
                clientState.ScoreToken = scoreToken;

                if (state.RulesetID == null)
                    return;

                if (state.BeatmapID == null)
                    return;

                using (var db = databaseFactory.GetInstance())
                {
                    database_beatmap? beatmap = await db.GetBeatmapAsync(state.BeatmapID.Value);
                    string? username = await db.GetUsernameAsync(userId);

                    if (string.IsNullOrEmpty(username))
                        throw new ArgumentException(nameof(username));

                    if (string.IsNullOrEmpty(beatmap?.checksum))
                        return;

                    clientState.Beatmap = beatmap;
                    clientState.Score = new Score
                    {
                        ScoreInfo =
                        {
                            APIMods = state.Mods.ToArray(),
                            User = new APIUser
                            {
                                Id = userId,
                                Username = username,
                            },
                            Ruleset = LegacyHelper.GetRulesetFromLegacyID(state.RulesetID.Value).RulesetInfo,
                            BeatmapInfo = new BeatmapInfo
                            {
                                OnlineID = state.BeatmapID.Value,
                                MD5Hash = beatmap.checksum,
                                Status = beatmap.approved
                            },
                            MaximumStatistics = state.MaximumStatistics
                        }
                    };
                }
            }

            // let's broadcast to every player temporarily. probably won't stay this way.
            await Clients.Group(GetGroupId(userId)).UserBeganPlaying(userId, state);
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

                score.ScoreInfo.Accuracy = data.Header.Accuracy;
                score.ScoreInfo.Statistics = data.Header.Statistics;
                score.ScoreInfo.MaxCombo = data.Header.MaxCombo;
                score.ScoreInfo.Combo = data.Header.Combo;
                score.ScoreInfo.TotalScore = data.Header.TotalScore;

                // null here means the frame bundle is from an old client that can't send mod data
                // can be removed (along with making property non-nullable on `FrameDataBundle`) 20250407
                if (data.Header.Mods != null)
                    score.ScoreInfo.APIMods = data.Header.Mods;

                score.Replay.Frames.AddRange(data.Frames);

                await Clients.Group(GetGroupId(Context.GetUserId())).UserSentFrames(Context.GetUserId(), data);
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
                    // Note that this *intentionally* skips the `endPlaySession()` call at the end of method.
                    if (score == null || scoreToken == null || usage.Item?.Beatmap == null)
                        return;

                    await processScore(usage.Item!);
                }
                finally
                {
                    if (usage.Item != null)
                    {
                        usage.Item.State = null;
                        usage.Item.Beatmap = null;
                        usage.Item.Score = null;
                        usage.Item.ScoreToken = null;
                    }
                }
            }

            await endPlaySession(Context.GetUserId(), state);
        }

        private async Task processScore(SpectatorClientState item)
        {
            Debug.Assert(item.Score != null && item.ScoreToken != null && item.Beatmap != null);

            Score score = item.Score;
            long scoreToken = item.ScoreToken.Value;

            // Do nothing with scores on unranked beatmaps.
            var status = score.ScoreInfo.BeatmapInfo!.Status;
            if (status < min_beatmap_status_for_replays || status > max_beatmap_status_for_replays)
                return;

            // if the user never hit anything, further processing that depends on the score existing can be waived because the client won't have submitted the score anyway.
            // see: https://github.com/ppy/osu/blob/a47ccb8edd2392258b6b7e176b222a9ecd511fc0/osu.Game/Screens/Play/SubmittingPlayer.cs#L281
            if (!score.ScoreInfo.Statistics.Any(s => s.Key.IsHit() && s.Value > 0))
                return;

            score.ScoreInfo.Date = DateTimeOffset.UtcNow;
            // this call is a little expensive due to reflection usage, so only run it at the end of score processing
            // even though in theory the rank could be recomputed after every replay frame.
            score.ScoreInfo.Rank = StandardisedScoreMigrationTools.ComputeRank(score.ScoreInfo);

            await scoreUploader.EnqueueAsync(scoreToken, score, item.Beatmap);
            await scoreProcessedSubscriber.RegisterForSingleScoreAsync(Context.ConnectionId, Context.GetUserId(), scoreToken);
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

            using (var state = await GetOrCreateLocalUserState())
            {
                var clientState = state.Item ??= new SpectatorClientState(Context.ConnectionId, Context.GetUserId());
                clientState.WatchedUsers.Add(userId);
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupId(userId));

            int watcherId = Context.GetUserId();
            string? watcherUsername;
            using (var db = databaseFactory.GetInstance())
                watcherUsername = await db.GetUsernameAsync(watcherId);

            if (watcherUsername == null)
                return;

            var watcher = new SpectatorUser
            {
                OnlineID = watcherId,
                Username = watcherUsername,
            };

            await Clients.User(userId.ToString()).UserStartedWatching([watcher]);
        }

        public async Task EndWatchingUser(int userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupId(userId));

            using (var state = await GetOrCreateLocalUserState())
            {
                var clientState = state.Item ??= new SpectatorClientState(Context.ConnectionId, Context.GetUserId());
                clientState.WatchedUsers.Remove(userId);
            }

            int watcherId = Context.GetUserId();

            await Clients.User(userId.ToString()).UserEndedWatching(watcherId);
        }

        public override async Task OnConnectedAsync()
        {
            // for now, send *all* player states to users on connect.
            // we don't want this for long, but while the lazer user base is small it should be okay.
            foreach (var kvp in GetAllStates())
                await Clients.Caller.UserBeganPlaying((int)kvp.Key, kvp.Value.State!);

            await base.OnConnectedAsync();
        }

        protected override async Task CleanUpState(ItemUsage<SpectatorClientState> state)
        {
            Debug.Assert(state.Item != null);

            if (state.Item.State != null)
                await endPlaySession(state.Item.UserId, state.Item.State);

            foreach (int watchedUserId in state.Item.WatchedUsers)
                await Clients.User(watchedUserId.ToString()).UserEndedWatching(state.Item.UserId);

            await base.CleanUpState(state);
        }

        public static string GetGroupId(int userId) => $"watch:{userId}";

        private async Task endPlaySession(int userId, SpectatorState state)
        {
            // Ensure that the state is no longer playing (e.g. if client crashes).
            if (state.State == SpectatedUserState.Playing)
                state.State = SpectatedUserState.Quit;

            await Clients.Group(GetGroupId(userId)).UserFinishedPlaying(userId, state);
        }
    }
}
