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
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay.Stages;

namespace osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.RankedPlay
{
    [NonController]
    public class RankedPlayMatchController : IMatchController, IMatchmakingMatchController
    {
        public const int PLAYER_HAND_SIZE = 5;
        public const int DECK_SIZE = PLAYER_HAND_SIZE * 2 + 1;

        public MultiplayerPlaylistItem CurrentItem => Room.CurrentPlaylistItem;

        /// <summary>
        /// The active pool.
        /// </summary>
        public uint PoolId { get; set; }

        /// <summary>
        /// Mapping from cards to their associated playlist item.
        /// </summary>
        public readonly Dictionary<RankedPlayCardItem, MultiplayerPlaylistItem> ItemMap = [];

        /// <summary>
        /// Mapping from playlist items to their associated card.
        /// </summary>
        public readonly Dictionary<long, RankedPlayCardItem> CardMap = [];

        public readonly ServerMultiplayerRoom Room;
        public readonly IMultiplayerHubContext Hub;
        public readonly IDatabaseFactory DbFactory;
        public readonly MultiplayerEventLogger EventLogger;
        public readonly RankedPlayRoomState State;

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

            if (room.Playlist.Count < DECK_SIZE)
                throw new InvalidOperationException($"There should be at least {DECK_SIZE} items in the playlist!");

            foreach (var item in Random.Shared.GetItems(room.Playlist.ToArray(), room.Playlist.Count))
            {
                var card = new RankedPlayCardItem();
                deck.Add(card);

                ItemMap[card] = item;
                CardMap[item.ID] = card;
            }

            room.MatchState = State;
            room.Settings.PlaylistItemId = room.Playlist[Random.Shared.Next(0, room.Playlist.Count)].ID;
        }

        async Task IMatchController.Initialise()
        {
            await Hub.NotifyMatchRoomStateChanged(Room);
            await GotoStage(RankedPlayStage.WaitForJoin);
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
                // Expire and let clients know that the current item has finished.
                await db.MarkPlaylistItemAsPlayedAsync(Room.RoomID, CurrentItem.ID);
                Room.Playlist[Room.Playlist.IndexOf(CurrentItem)] = (await db.GetPlaylistItemAsync(Room.RoomID, CurrentItem.ID)).ToMultiplayerPlaylistItem();
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
                await Hub.NotifyRankedPlayCardRevealed(Room, userId, card, ItemMap[card]);
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

        public async Task RemoveCards(int userId, long[] playlistItemIds)
        {
            await RemoveCards(userId, playlistItemIds.Select(i => CardMap[i]).ToArray());
        }

        public MatchStartedEventDetail GetMatchDetails() => new MatchStartedEventDetail
        {
            room_type = database_match_type.ranked_play
        };
    }
}
