// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Server.Spectator.Hubs.Multiplayer;
using Xunit;

namespace osu.Server.Spectator.Tests.Multiplayer
{
    /// <summary>
    /// Tests covering propagation of events through <see cref="ServerMultiplayerRoom"/> to the <see cref="MultiplayerHub"/> via callbacks.
    /// </summary>
    public class MatchTypeRoomEventHookTests : MultiplayerTest
    {
        [Fact]
        public async Task NewUserJoinedTriggersRulesetHook()
        {
            var hub = new Mock<IMultiplayerHubContext>();
            var room = new ServerMultiplayerRoom(1, hub.Object, DatabaseFactory.Object)
            {
                Playlist =
                {
                    new MultiplayerPlaylistItem
                    {
                        BeatmapID = 3333,
                        BeatmapChecksum = "3333"
                    },
                }
            };

            await room.Initialise();

            Mock<IMatchController> typeImplementation = new Mock<IMatchController>();
            await room.ChangeMatchType(typeImplementation.Object);

            await room.AddUser(new MultiplayerRoomUser(1));

            typeImplementation.Verify(m => m.HandleUserJoined(It.IsAny<MultiplayerRoomUser>()), Times.Once());
        }

        [Fact]
        public async Task UserLeavesTriggersRulesetHook()
        {
            var hub = new Mock<IMultiplayerHubContext>();
            var room = new ServerMultiplayerRoom(1, hub.Object, DatabaseFactory.Object)
            {
                Playlist =
                {
                    new MultiplayerPlaylistItem
                    {
                        BeatmapID = 3333,
                        BeatmapChecksum = "3333"
                    },
                }
            };

            await room.Initialise();

            var user = new MultiplayerRoomUser(1);

            await room.AddUser(user);

            Mock<IMatchController> typeImplementation = new Mock<IMatchController>();
            await room.ChangeMatchType(typeImplementation.Object);

            await room.RemoveUser(user);
            typeImplementation.Verify(m => m.HandleUserLeft(It.IsAny<MultiplayerRoomUser>()), Times.Once());
        }

        [Fact]
        public async Task TypeChangeTriggersInitialJoins()
        {
            var hub = new Mock<IMultiplayerHubContext>();
            var room = new ServerMultiplayerRoom(1, hub.Object, DatabaseFactory.Object)
            {
                Playlist =
                {
                    new MultiplayerPlaylistItem
                    {
                        BeatmapID = 3333,
                        BeatmapChecksum = "3333"
                    },
                }
            };

            await room.Initialise();

            // join a number of users initially to the room
            for (int i = 0; i < 5; i++)
                await room.AddUser(new MultiplayerRoomUser(i));

            // change the match type
            Mock<IMatchController> typeImplementation = new Mock<IMatchController>();
            await room.ChangeMatchType(typeImplementation.Object);

            // ensure the match type received hook events for all already joined users.
            typeImplementation.Verify(m => m.HandleUserJoined(It.IsAny<MultiplayerRoomUser>()), Times.Exactly(5));
        }
    }
}
