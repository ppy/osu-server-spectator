// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Online.RankedPlay;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay
{
    [NonController]
    public class RankedPlayMatchController : IMatchController, IMatchmakingMatchController
    {
        public const int PLAYER_HAND_SIZE = 5;
        public const int DECK_SIZE = PLAYER_HAND_SIZE * 2 + 1;

        public MultiplayerPlaylistItem CurrentItem => Room.CurrentPlaylistItem;

        public uint PoolId { get; private set; }

        public readonly ServerMultiplayerRoom Room;
        public readonly IMultiplayerHubContext Hub;
        public readonly IDatabaseFactory DbFactory;
        public readonly MultiplayerEventLogger EventLogger;
        public readonly RankedPlayRoomState State;

        /// <summary>
        /// The card that was last activated by any user.
        /// </summary>
        public RankedPlayCardItem? LastActivatedCard { get; private set; }

        /// <summary>
        /// Mapping of cards to their associated effect.
        /// </summary>
        private readonly Dictionary<RankedPlayCardItem, MultiplayerPlaylistItem> cardToEffectMap = [];

        /// <summary>
        /// Cards that may be drawn from the deck.
        /// </summary>
        private readonly List<RankedPlayCardItem> deck = [];

        private RankedPlayStageImplementation stageImplementation;

        public RankedPlayMatchController(ServerMultiplayerRoom room, IMultiplayerHubContext hub, IDatabaseFactory dbFactory, MultiplayerEventLogger eventLogger)
        {
            Room = room;
            Hub = hub;
            DbFactory = dbFactory;
            EventLogger = eventLogger;
            State = new RankedPlayRoomState();
            stageImplementation = new EmptyStage(this);

            room.MatchState = State;
        }

        async Task IMatchController.Initialise()
        {
            await Hub.NotifyMatchRoomStateChanged(Room);
            await GotoStage(RankedPlayStage.WaitForJoin);
        }

        async Task IMatchmakingMatchController.Initialise(uint poolId, MatchmakingQueueUser[] users, MatchmakingBeatmapSelector beatmapSelector)
        {
            PoolId = poolId;

            // Build the deck.
            matchmaking_pool_beatmap[] beatmaps = beatmapSelector.GetAppropriateBeatmaps(users.Select(u => u.Rating).ToArray());

            if (beatmaps.Length < DECK_SIZE)
                throw new InvalidOperationException($"There should be at least {DECK_SIZE} beatmaps, but only {beatmaps.Length} were selected.");

            foreach (var beatmap in Random.Shared.GetItems(beatmaps, beatmaps.Length))
            {
                var card = new RankedPlayCardItem();
                cardToEffectMap[card] = beatmap.ToPlaylistItem();
                deck.Add(card);
            }

            State.StarRating = beatmaps.Select(b => b.difficultyrating).DefaultIfEmpty(0).Average();

            // Create an initial playlist item for the room. Clients require this to operate correctly.
            using (var db = DbFactory.GetInstance())
            {
                MultiplayerPlaylistItem initialItem = new MultiplayerPlaylistItem();
                initialItem.ID = await db.AddPlaylistItemAsync(new multiplayer_playlist_item(Room.RoomID, initialItem));

                Room.Playlist.Add(initialItem);
                Room.Settings.PlaylistItemId = initialItem.ID;
            }

            // Create the user states.
            foreach (var user in users)
            {
                State.Users[user.UserId] = new RankedPlayUserInfo
                {
                    Rating = (int)Math.Round(user.Rating.Mu)
                };
            }

            await Hub.NotifyMatchRoomStateChanged(Room);
        }

        Task<bool> IMatchController.UserCanJoin(int userId)
        {
            return Task.FromResult(State.Users.ContainsKey(userId));
        }

        Task IMatchController.HandleSettingsChanged()
        {
            return Task.CompletedTask;
        }

        async Task IMatchController.HandleGameplayCompleted()
        {
            using (var db = DbFactory.GetInstance())
            {
                await db.MarkPlaylistItemAsPlayedAsync(Room.RoomID, CurrentItem.ID);

                multiplayer_playlist_item newItem = await db.GetPlaylistItemAsync(Room.RoomID, CurrentItem.ID);
                CurrentItem.Expired = newItem.expired;
                CurrentItem.PlayedAt = newItem.played_at;

                await Hub.NotifyPlaylistItemChanged(Room, CurrentItem, true);
            }

            await stageImplementation.HandleGameplayCompleted();
        }

        Task IMatchController.HandleUserRequest(MultiplayerRoomUser user, MatchUserRequest request)
        {
            return Task.CompletedTask;
        }

        async Task IMatchController.HandleUserJoined(MultiplayerRoomUser user)
        {
            await EventLogger.LogMatchmakingUserJoinAsync(Room.RoomID, user.UserID);
            await stageImplementation.HandleUserJoined(user);
        }

        async Task IMatchController.HandleUserLeft(MultiplayerRoomUser user)
        {
            await GotoStage(RankedPlayStage.Ended);
        }

        Task IMatchController.AddPlaylistItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        Task IMatchController.EditPlaylistItem(MultiplayerPlaylistItem item, MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        Task IMatchController.RemovePlaylistItem(long playlistItemId, MultiplayerRoomUser user)
        {
            return Task.CompletedTask;
        }

        async Task IMatchController.HandleUserStateChanged(MultiplayerRoomUser user)
        {
            await stageImplementation.HandleUserStateChanged(user);
        }

        public void SkipToNextStage(out Task countdownTask)
        {
            if (!AppSettings.MatchmakingRoomAllowSkip)
                throw new InvalidStateException("Skipping matchmaking rounds is not allowed.");

            countdownTask = Room.SkipToEndOfCountdown(Room.FindCountdownOfType<RankedPlayStageCountdown>());
        }

        public async Task DiscardCards(MultiplayerRoomUser user, RankedPlayCardItem[] cards)
        {
            await stageImplementation.HandleDiscardCards(user, cards);
        }

        public async Task PlayCard(MultiplayerRoomUser user, RankedPlayCardItem card)
        {
            await stageImplementation.HandlePlayCard(user, card);
        }

        public async Task GotoStage(RankedPlayStage stage)
        {
            stageImplementation = stage switch
            {
                RankedPlayStage.WaitForJoin => new WaitForJoinStage(this),
                RankedPlayStage.RoundWarmup => new RoundWarmupStage(this),
                RankedPlayStage.CardDiscard => new CardDiscardStage(this),
                RankedPlayStage.FinishCardDiscard => new FinishCardDiscardStage(this),
                RankedPlayStage.CardPlay => new CardPlayStage(this),
                RankedPlayStage.FinishCardPlay => new FinishCardPlayStage(this),
                RankedPlayStage.GameplayWarmup => new GameplayWarmupStage(this),
                RankedPlayStage.Gameplay => new GameplayStage(this),
                RankedPlayStage.Results => new ResultsStage(this),
                RankedPlayStage.Ended => new EndedStage(this),
                _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
            };

            await stageImplementation.Enter();
        }

        /// <summary>
        /// Draws a number of cards for a given user, placing them in their hand.
        /// </summary>
        /// <param name="userId">The user to draw cards for.</param>
        /// <param name="count">The maximum number of cards to draw from the deck.</param>
        public async Task AddCards(int userId, int count)
        {
            RankedPlayCardItem[] cards = deck.Take(count).ToArray();
            deck.RemoveRange(0, cards.Length);

            foreach (var card in cards)
            {
                State.Users[userId].Hand.Add(card);
                await Hub.NotifyRankedPlayCardAdded(Room, userId, card);
                await Hub.NotifyRankedPlayCardRevealed(Room, userId, card, cardToEffectMap[card]);
            }

            await Hub.NotifyMatchRoomStateChanged(Room);
        }

        /// <summary>
        /// Discards cards, removing them from a user's hand.
        /// </summary>
        /// <param name="userId">The user to discard cards from.</param>
        /// <param name="cards">The cards to discard.</param>
        public async Task RemoveCards(int userId, RankedPlayCardItem[] cards)
        {
            foreach (var card in cards)
            {
                State.Users[userId].Hand.Remove(card);
                await Hub.NotifyRankedPlayCardRemoved(Room, userId, card);
            }

            await Hub.NotifyMatchRoomStateChanged(Room);
        }

        /// <summary>
        /// Activates the card, placing its effect on the room.
        /// </summary>
        public async Task ActivateCard(RankedPlayCardItem card)
        {
            MultiplayerPlaylistItem effect = cardToEffectMap[card];

            await Hub.NotifyRankedPlayCardRevealed(Room, null, card, effect);
            await Hub.NotifyRankedPlayCardPlayed(Room, card);

            // Todo: If we ever have cards with non-"play beatmap" effects, then
            //       this is the first responder to perform any relevant actions.

            using (var db = DbFactory.GetInstance())
            {
                if (CurrentItem.Expired)
                {
                    effect.ID = await db.AddPlaylistItemAsync(new multiplayer_playlist_item(Room.RoomID, effect));

                    Room.Playlist.Add(effect);
                    await Hub.NotifyPlaylistItemAdded(Room, effect);
                }
                else
                {
                    effect.ID = CurrentItem.ID;

                    Room.Playlist[Room.Playlist.IndexOf(CurrentItem)] = effect;
                    await db.UpdatePlaylistItemAsync(new multiplayer_playlist_item(Room.RoomID, effect));
                    await Hub.NotifyPlaylistItemChanged(Room, effect, true);
                }
            }

            Room.Settings.PlaylistItemId = effect.ID;
            await Hub.NotifySettingsChanged(Room, true);

            LastActivatedCard = card;
        }

        public MatchStartedEventDetail GetMatchDetails() => new MatchStartedEventDetail
        {
            room_type = database_match_type.ranked_play
        };
    }
}
