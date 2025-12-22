// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using OpenSkillSharp.Models;
using OpenSkillSharp.Rating;
using OpenSkillSharp.Util;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Online.RankedPlay;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking
{
    [NonController]
    public class RankedPlayMatchController : IMatchController, IMatchmakingMatchController
    {
        /// <summary>
        /// Duration users are given to enter the room before it's disbanded.
        /// This is not expected to run to completion because users have already indicated
        /// their intention to join the match.
        /// </summary>
        private const int stage_waiting_for_clients_join_time = 60;

        private const int stage_round_warmup_time = 15;
        private const int stage_discard_time = 30;
        private const int stage_finish_discard_time = 10;
        private const int stage_select_time = 30;
        private const int stage_gameplay_warmup_time = 5;
        private const int stage_gameplay_time = 0;
        private const int stage_round_end_time = 10;
        private const int stage_room_end_time = 120;

        private const int room_size = 2;
        private const int player_hand_size = 5;
        private const int deck_size = player_hand_size * 2 + 1;

        public MultiplayerPlaylistItem CurrentItem => room.CurrentPlaylistItem;

        public uint PoolId { get; set; }

        private readonly ServerMultiplayerRoom room;
        private readonly IMultiplayerHubContext hub;
        private readonly IDatabaseFactory dbFactory;
        private readonly MultiplayerEventLogger eventLogger;
        private readonly RankedPlayRoomState state;

        private readonly List<RankedPlayCardItem> deck = [];
        private readonly Dictionary<RankedPlayCardItem, MultiplayerPlaylistItem> itemMap = [];

        /// <summary>
        /// Whether a user has already discarded cards from their hand.
        /// </summary>
        private readonly HashSet<int> userCardsDiscarded = [];

        private RankedPlayCardItem? activeCard;
        private bool statsUpdatePending = true;

        public RankedPlayMatchController(ServerMultiplayerRoom room, IMultiplayerHubContext hub, IDatabaseFactory dbFactory, MultiplayerEventLogger eventLogger)
        {
            this.room = room;
            this.hub = hub;
            this.dbFactory = dbFactory;
            this.eventLogger = eventLogger;

            if (room.Playlist.Count < deck_size)
                throw new InvalidOperationException($"There should be at least {deck_size} items in the playlist!");

            foreach (var item in room.Playlist)
            {
                var card = new RankedPlayCardItem();
                deck.Add(card);
                itemMap[card] = item;
            }

            Random.Shared.Shuffle(CollectionsMarshal.AsSpan(deck));

            room.MatchState = state = new RankedPlayRoomState();

            // Needs to be set to something...
            room.Settings.PlaylistItemId = room.Playlist[Random.Shared.Next(0, room.Playlist.Count)].ID;
        }

        public async Task Initialise()
        {
            await hub.NotifyMatchRoomStateChanged(room);
            await startCountdown(TimeSpan.FromSeconds(stage_waiting_for_clients_join_time), closeRoom);
        }

        public Task<bool> UserCanJoin(int userId)
        {
            // Todo: Implement this, somehow...
            return Task.FromResult(true);
        }

        public Task HandleSettingsChanged()
        {
            return Task.CompletedTask;
        }

        public async Task HandleGameplayCompleted()
        {
            using (var db = dbFactory.GetInstance())
            {
                // Expire and let clients know that the current item has finished.
                await db.MarkPlaylistItemAsPlayedAsync(room.RoomID, CurrentItem.ID);
                room.Playlist[room.Playlist.IndexOf(CurrentItem)] = (await db.GetPlaylistItemAsync(room.RoomID, CurrentItem.ID)).ToMultiplayerPlaylistItem();
                await hub.NotifyPlaylistItemChanged(room, CurrentItem, true);
            }

            // Remove the active card from play.
            if (activeCard != null)
            {
                await removeCards(state.ActiveUserId, [activeCard]);
                activeCard = null;
            }

            await stageResults();
        }

        public Task HandleUserRequest(MultiplayerRoomUser user, MatchUserRequest request)
        {
            return Task.CompletedTask;
        }

        public async Task HandleUserJoined(MultiplayerRoomUser user)
        {
            await eventLogger.LogMatchmakingUserJoinAsync(room.RoomID, user.UserID);

            // Populate the user's initial hand.
            await addCards(user.UserID, player_hand_size);

            if (room.Users.Count == room_size)
                await stageRoundWarmup(room);
        }

        public async Task HandleUserLeft(MultiplayerRoomUser user)
        {
            await closeRoom(room);
        }

        public Task AddPlaylistItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        public Task EditPlaylistItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        public Task RemovePlaylistItem(long playlistItemId, MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        public async Task HandleUserStateChanged(MultiplayerRoomUser user)
        {
            switch (state.Stage)
            {
                case RankedPlayStage.FinishCardPlay:
                    if (allUsersReady())
                        await stageGameplayWarmup(room);
                    break;
            }
        }

        public void SkipToNextStage(out Task countdownTask)
        {
            if (!AppSettings.MatchmakingRoomAllowSkip)
                throw new InvalidStateException("Skipping matchmaking rounds is not allowed.");

            countdownTask = room.SkipToEndOfCountdown(room.FindCountdownOfType<RankedPlayStageCountdown>());
        }

        public async Task DiscardCards(MultiplayerRoomUser user, RankedPlayCardItem[] cards)
        {
            if (state.Stage != RankedPlayStage.CardDiscard)
                return;

            if (!userCardsDiscarded.Add(user.UserID))
                throw new InvalidStateException("Cards have already been discarded.");

            if (!cards.All(state.Users[user.UserID].Hand.Contains))
                throw new InvalidStateException("One or more cards were not in the hand.");

            await removeCards(user.UserID, cards);
            await addCards(user.UserID, cards.Length);

            // When both users have finished discarding their cards,
            // wait for animations to complete before transitioning the stage.
            if (userCardsDiscarded.Count == room.Users.Count)
            {
                await room.StopCountdown(room.FindCountdownOfType<RankedPlayStageCountdown>());
                await startCountdown(TimeSpan.FromSeconds(3), stageFinishDiscard);
            }
        }

        public async Task PlayCard(MultiplayerRoomUser user, RankedPlayCardItem card)
        {
            if (state.Stage != RankedPlayStage.CardPlay)
                return;

            if (user.UserID != state.ActiveUserId)
                throw new InvalidStateException("Not the active player.");

            if (!state.Users[user.UserID].Hand.Contains(card))
                throw new InvalidStateException("Card not in the hand.");

            await stageFinishSelection(card);
        }

        /// <summary>
        /// Start of a round.
        /// </summary>
        private async Task stageRoundWarmup(ServerMultiplayerRoom _)
        {
            state.CurrentRound++;
            state.DamageMultiplier = computeDamageMultiplier(state.CurrentRound);
            state.ActiveUserId = state.CurrentRound == 1
                ? Random.Shared.GetItems(state.Users.Keys.ToArray(), 1).Single()
                : state.Users.Keys.Concat(state.Users.Keys).SkipWhile(u => u != state.ActiveUserId).Skip(1).First();

            await changeStage(RankedPlayStage.RoundWarmup);
            await returnUsersToRoom(room);

            if (state.CurrentRound == 1)
                await startCountdown(TimeSpan.FromSeconds(stage_round_warmup_time), stageCardDiscard);
            else
                await stageCardSelect(room);
        }

        /// <summary>
        /// Users are discarding cards from their hand.
        /// </summary>
        private async Task stageCardDiscard(ServerMultiplayerRoom _)
        {
            await changeStage(RankedPlayStage.CardDiscard);
            await startCountdown(TimeSpan.FromSeconds(stage_discard_time), stageFinishDiscard);
        }

        private async Task stageFinishDiscard(ServerMultiplayerRoom _)
        {
            await changeStage(RankedPlayStage.FinishCardDiscard);
            await startCountdown(TimeSpan.FromSeconds(stage_finish_discard_time), stageCardSelect);
        }

        /// <summary>
        /// The active user is playing a card.
        /// </summary>
        private async Task stageCardSelect(ServerMultiplayerRoom _)
        {
            if (state.ActiveUser.Hand.Count == 0)
                await addCards(state.ActiveUserId, 1);

            await changeStage(RankedPlayStage.CardPlay);
            await startCountdown(TimeSpan.FromSeconds(stage_select_time), _ => stageFinishSelection(state.ActiveUser.Hand.First()));
        }

        /// <summary>
        /// A selected card is locked in to play.
        /// </summary>
        private async Task stageFinishSelection(RankedPlayCardItem card)
        {
            activeCard = card;
            await hub.NotifyRankedPlayCardRevealed(room, null, card, itemMap[card]);
            await hub.NotifyRankedPlayCardPlayed(room, card);

            room.Settings.PlaylistItemId = itemMap[card].ID;
            await hub.NotifySettingsChanged(room, true);
            await eventLogger.LogMatchmakingGameplayBeatmapAsync(room.RoomID, room.Settings.PlaylistItemId);

            await changeStage(RankedPlayStage.FinishCardPlay);
            // Event flow continues at HandleUserStateChanged();
        }

        /// <summary>
        /// Gameplay preparation.
        /// </summary>
        private async Task stageGameplayWarmup(ServerMultiplayerRoom _)
        {
            await changeStage(RankedPlayStage.GameplayWarmup);
            await startCountdown(TimeSpan.FromSeconds(stage_gameplay_warmup_time), stageGameplay);
        }

        /// <summary>
        /// Gameplay.
        /// </summary>
        private async Task stageGameplay(ServerMultiplayerRoom _)
        {
            await changeStage(RankedPlayStage.Gameplay);
            await startCountdown(TimeSpan.FromSeconds(stage_gameplay_time), hub.StartMatch);
            // Event flow continues at HandleGameplayCompleted();
        }

        /// <summary>
        /// Users are viewing results.
        /// </summary>
        private async Task stageResults()
        {
            // Collect all scores from the database.
            List<SoloScore> scores = [];
            using (var db = dbFactory.GetInstance())
                scores.AddRange(await db.GetAllScoresForPlaylistItem(CurrentItem.ID));

            // Add dummy scores for all users that did not play the map.
            foreach ((int userId, _) in state.Users)
            {
                if (scores.All(s => s.user_id != userId))
                    scores.Add(new SoloScore { user_id = (uint)userId });
            }

            // If all players have 0 resulting score, each shall take 1 point of damage (before multipliers).
            int maxTotalScore = (int)Math.Max(1, scores.Select(s => s.total_score).Max());
            bool anyPlayerDefeated = false;

            foreach (var score in scores)
            {
                double damage = maxTotalScore - (int)score.total_score;
                damage *= state.DamageMultiplier;
                damage = Math.Ceiling(damage);

                var userInfo = state.Users[(int)score.user_id];
                userInfo.Life = Math.Max(0, userInfo.Life - (int)damage);

                anyPlayerDefeated |= userInfo.Life == 0;
            }

            // Todo: This only works for 2 players. This will need to be adjusted if we ever have more.
            if (anyPlayerDefeated)
                await updateUserStats();

            await changeStage(RankedPlayStage.Results);
            await startCountdown(TimeSpan.FromSeconds(stage_round_end_time), anyPlayerDefeated ? stageRoomEnd : stageRoundWarmup);
        }

        /// <summary>
        /// The room is closing.
        /// </summary>
        private async Task stageRoomEnd(ServerMultiplayerRoom _)
        {
            await changeStage(RankedPlayStage.Ended);
            await startCountdown(TimeSpan.FromSeconds(stage_room_end_time), closeRoom);
        }

        /// <summary>
        /// Closes the room.
        /// </summary>
        private async Task closeRoom(ServerMultiplayerRoom _)
        {
            await updateUserStats();
            await stageRoomEnd(room);
        }

        /// <summary>
        /// Draws a number of cards for a given user, placing them in their hand.
        /// </summary>
        /// <param name="userId">The user to draw cards for.</param>
        /// <param name="count">The maximum number of cards to draw from the deck.</param>
        private async Task addCards(int userId, int count)
        {
            RankedPlayCardItem[] cards = deck.Take(count).ToArray();
            deck.RemoveRange(0, cards.Length);

            foreach (var card in cards)
            {
                state.Users[userId].Hand.Add(card);
                await hub.NotifyRankedPlayCardAdded(room, userId, card);
                await hub.NotifyRankedPlayCardRevealed(room, userId, card, itemMap[card]);
            }

            await hub.NotifyMatchRoomStateChanged(room);
        }

        /// <summary>
        /// Discards cards, removing them from a user's hand.
        /// </summary>
        /// <param name="userId">The user to discard cards from.</param>
        /// <param name="cards">The cards to discard.</param>
        private async Task removeCards(int userId, RankedPlayCardItem[] cards)
        {
            foreach (var card in cards)
            {
                state.Users[userId].Hand.Remove(card);
                await hub.NotifyRankedPlayCardRemoved(room, userId, card);
            }

            await hub.NotifyMatchRoomStateChanged(room);
        }

        /// <summary>
        /// Retrieves the damage multiplier for a given round.
        /// </summary>
        /// <param name="round">The round.</param>
        private static double computeDamageMultiplier(int round)
        {
            double[] multipliers = [1, 1, 2, 2, 5, 5, 10, 10, 100, 100];
            return multipliers[Math.Clamp(round - 1, 0, multipliers.Length - 1)];
        }

        /// <summary>
        /// Updates the MMR for users upon closure of the match.
        /// </summary>
        private async Task updateUserStats()
        {
            if (!statsUpdatePending)
                return;

            // Check if the match has started.
            if (state.CurrentRound == 0)
                return;

            using (var db = dbFactory.GetInstance())
            {
                PlackettLuce model = new PlackettLuce
                {
                    Mu = 1500,
                    Sigma = 350,
                    Beta = 175,
                    Tau = 3.5
                };

                List<matchmaking_user_stats> stats = [];
                List<ITeam> teams = [];
                List<double> ranks = [];

                foreach ((int rankIndex, (int userId, _)) in state.Users.OrderByDescending(u => u.Value.Life).Index())
                {
                    matchmaking_user_stats userStats = await db.GetMatchmakingUserStatsAsync(userId, PoolId) ?? new matchmaking_user_stats
                    {
                        user_id = (uint)userId,
                        pool_id = PoolId
                    };

                    stats.Add(userStats);
                    teams.Add(new Team { Players = [model.Rating(userStats.EloData.Rating.Mu, userStats.EloData.Rating.Sig)] });
                    ranks.Add(rankIndex);
                }

                ITeam[] newRatings = model.Rate(teams, ranks).ToArray();

                for (int i = 0; i < stats.Count; i++)
                {
                    stats[i].EloData.ContestCount++;
                    stats[i].EloData.Rating = new EloRating(newRatings[i].Players.Single().Mu, newRatings[i].Players.Single().Sigma);
                    await db.UpdateMatchmakingUserStatsAsync(stats[i]);
                }
            }

            statsUpdatePending = false;
        }

        /// <summary>
        /// Forces users back to an idle state.
        /// </summary>
        private async Task returnUsersToRoom(ServerMultiplayerRoom _)
        {
            foreach (var user in room.Users.Where(u => u.State != MultiplayerUserState.Idle))
            {
                await hub.ChangeAndBroadcastUserState(room, user, MultiplayerUserState.Idle);
                await hub.UpdateRoomStateIfRequired(room);
            }
        }

        /// <summary>
        /// Changes the stage of the match.
        /// </summary>
        private async Task changeStage(RankedPlayStage stage)
        {
            state.Stage = stage;
            await hub.NotifyMatchRoomStateChanged(room);
        }

        /// <summary>
        /// Starts a countdown for the current stage of the match.
        /// </summary>
        /// <param name="duration">The length of the stage.</param>
        /// <param name="continuation">A continuation task to run after the countdown completes.</param>
        private async Task startCountdown(TimeSpan duration, Func<ServerMultiplayerRoom, Task> continuation)
        {
            await room.StartCountdown(new RankedPlayStageCountdown
            {
                Stage = state.Stage,
                TimeRemaining = duration
            }, continuation);
        }

        /// <summary>
        /// Determines whether all users in the match are in a ready state.
        /// </summary>
        private bool allUsersReady()
        {
            return room.Users.All(u => u.State == MultiplayerUserState.Ready);
        }

        public MatchStartedEventDetail GetMatchDetails() => new MatchStartedEventDetail
        {
            room_type = database_match_type.ranked_play
        };
    }
}
