// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Internal;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Elo;
using osu.Server.Spectator.Hubs.Multiplayer.Matchmaking.Queue;
using Xunit;

namespace osu.Server.Spectator.Tests.Matchmaking
{
    public class MatchmakingQueueTest
    {
        private readonly MatchmakingQueue queue = new MatchmakingQueue(new matchmaking_pool());

        [Fact]
        public void EmptyUpdate()
        {
            queue.Pool.lobby_size = 1;

            var bundle = queue.Update();
            Assert.Empty(bundle.FormedGroups);
            Assert.Empty(bundle.CompletedGroups);
            Assert.Empty(bundle.AddedUsers);
            Assert.Empty(bundle.RemovedUsers);
        }

        [Fact]
        public void SingleUserRoom()
        {
            queue.Pool.lobby_size = 1;

            var bundle = queue.Add(new MatchmakingQueueUser("1"));
            Assert.Single(bundle.AddedUsers);
            Assert.Equal("1", bundle.AddedUsers[0].Identifier);

            bundle = queue.Update();
            Assert.Single(bundle.FormedGroups);
            Assert.Single(bundle.FormedGroups[0].Users);

            bundle = queue.MarkInvitationAccepted(new MatchmakingQueueUser("1"));
            Assert.Single(bundle.CompletedGroups);
            Assert.Single(bundle.CompletedGroups[0].Users);
        }

        [Fact]
        public void MultipleUserRoom()
        {
            queue.Pool.lobby_size = 2;

            var bundle = queue.Add(new MatchmakingQueueUser("1"));
            Assert.Single(bundle.AddedUsers);

            bundle = queue.Update();
            Assert.Empty(bundle.FormedGroups);

            bundle = queue.Add(new MatchmakingQueueUser("2"));
            Assert.Single(bundle.AddedUsers);

            bundle = queue.Update();
            Assert.Single(bundle.FormedGroups);
            Assert.Equal(2, bundle.FormedGroups[0].Users.Length);

            bundle = queue.MarkInvitationAccepted(new MatchmakingQueueUser("1"));
            Assert.Empty(bundle.CompletedGroups);

            bundle = queue.MarkInvitationAccepted(new MatchmakingQueueUser("2"));
            Assert.Single(bundle.CompletedGroups);
            Assert.Equal(2, bundle.CompletedGroups[0].Users.Length);
        }

        [Fact]
        public void DeclineInvitation()
        {
            queue.Pool.lobby_size = 2;

            queue.Add(new MatchmakingQueueUser("1"));
            queue.Add(new MatchmakingQueueUser("2"));

            var bundle = queue.Update();
            Assert.Single(bundle.FormedGroups);

            bundle = queue.MarkInvitationDeclined(new MatchmakingQueueUser("1"));
            Assert.Single(bundle.RemovedUsers);
            Assert.Equal("1", bundle.RemovedUsers[0].Identifier);
            Assert.Single(bundle.DeclinedUsers);
            Assert.Equal("1", bundle.DeclinedUsers[0].Identifier);
            Assert.Single(bundle.AddedUsers);
            Assert.Equal("2", bundle.AddedUsers[0].Identifier);
        }

        [Fact]
        public async Task InviteTimeout()
        {
            queue.Pool.lobby_size = 2;
            queue.InviteTimeout = TimeSpan.FromSeconds(1);

            queue.Add(new MatchmakingQueueUser("1"));
            queue.Add(new MatchmakingQueueUser("2"));
            queue.Update();
            queue.MarkInvitationAccepted(new MatchmakingQueueUser("1"));

            await Task.Delay(TimeSpan.FromSeconds(2));

            var bundle = queue.Update();
            Assert.Single(bundle.RemovedUsers);
            Assert.Equal("2", bundle.RemovedUsers[0].Identifier);
            Assert.Single(bundle.DeclinedUsers);
            Assert.Equal("2", bundle.DeclinedUsers[0].Identifier);
            Assert.Single(bundle.AddedUsers);
            Assert.Equal("1", bundle.AddedUsers[0].Identifier);
        }

        [Fact]
        public void UsersAtDifferentRatingsEventuallyFindEachOther()
        {
            CustomSystemClock clock = new CustomSystemClock();

            queue.Pool.lobby_size = 2;
            queue.Clock = clock;
            queue.RatingInitialSearchRadius = 100;
            queue.RatingSearchRadiusIncreaseTime = 10;

            queue.Add(new MatchmakingQueueUser("1")
            {
                Rating = new EloRating(1300, 350)
            });

            queue.Add(new MatchmakingQueueUser("2")
            {
                Rating = new EloRating(1700, 350)
            });

            var bundle = queue.Update();
            Assert.Empty(bundle.FormedGroups);

            clock.UtcNow += TimeSpan.FromSeconds(10);
            bundle = queue.Update();
            Assert.Empty(bundle.FormedGroups);

            clock.UtcNow += TimeSpan.FromSeconds(10);
            bundle = queue.Update();
            Assert.Single(bundle.FormedGroups);
        }

        [Fact]
        public void UserWaitingForALongTimeMatchesQuickly()
        {
            CustomSystemClock clock = new CustomSystemClock();

            queue.Pool.lobby_size = 2;
            queue.Clock = clock;
            queue.RatingInitialSearchRadius = 100;
            queue.RatingSearchRadiusIncreaseTime = 10;

            queue.Add(new MatchmakingQueueUser("1")
            {
                Rating = new EloRating(1300, 350)
            });

            clock.UtcNow += TimeSpan.FromSeconds(20);

            queue.Add(new MatchmakingQueueUser("2")
            {
                Rating = new EloRating(1700, 350)
            });

            var bundle = queue.Update();
            Assert.Single(bundle.FormedGroups);
        }

        [Fact]
        public void TemporarilyBannedUserExcludedFromQueue()
        {
            CustomSystemClock clock = new CustomSystemClock();

            queue.Pool.lobby_size = 2;
            queue.Clock = clock;
            queue.BanDuration = TimeSpan.FromMinutes(1);

            queue.Add(new MatchmakingQueueUser("1"));
            queue.Add(new MatchmakingQueueUser("2")
            {
                QueueBanStartTime = clock.UtcNow
            });

            var bundle = queue.Update();
            Assert.Empty(bundle.FormedGroups);

            clock.UtcNow += TimeSpan.FromSeconds(30);

            bundle = queue.Update();
            Assert.Empty(bundle.FormedGroups);

            clock.UtcNow += TimeSpan.FromSeconds(45);

            bundle = queue.Update();
            Assert.Single(bundle.FormedGroups);
        }

        private class CustomSystemClock : ISystemClock
        {
            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        }
    }
}
