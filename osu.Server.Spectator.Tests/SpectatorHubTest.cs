// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using osu.Game.Online.Spectator;
using osu.Game.Replays.Legacy;
using osu.Game.Scoring;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class SpectatorHubTest
    {
        private readonly SpectatorHub hub;

        private const int streamer_id = 1234;
        private const int beatmap_id = 88;
        private const int watcher_id = 8000;

        private static readonly SpectatorState state = new SpectatorState
        {
            BeatmapID = beatmap_id,
            RulesetID = 0,
        };

        public SpectatorHubTest()
        {
            if (Directory.Exists(SpectatorHub.REPLAYS_PATH))
                Directory.Delete(SpectatorHub.REPLAYS_PATH, true);

            // not used for now, but left here for potential future usage.
            MemoryDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

            var clientStates = new EntityStore<SpectatorClientState>();

            var databaseFactory = new Mock<IDatabaseFactory>();
            var database = new Mock<IDatabaseAccess>();
            databaseFactory.Setup(factory => factory.GetInstance()).Returns(database.Object);
            database.Setup(db => db.GetUsernameAsync(streamer_id)).ReturnsAsync(() => "user");
            database.Setup(db => db.GetBeatmapChecksumAsync(beatmap_id)).ReturnsAsync(() => "d2a97fb2fa4529a5e857fe0466dc1daf");

            hub = new SpectatorHub(cache, clientStates, databaseFactory.Object);
        }

        [Fact]
        public async Task NewUserConnectsAndStreamsData()
        {
            Mock<IHubCallerClients<ISpectatorClient>> mockClients = new Mock<IHubCallerClients<ISpectatorClient>>();
            Mock<ISpectatorClient> mockReceiver = new Mock<ISpectatorClient>();
            mockClients.Setup(clients => clients.All).Returns(mockReceiver.Object);
            mockClients.Setup(clients => clients.Group(SpectatorHub.GetGroupId(streamer_id))).Returns(mockReceiver.Object);

            Mock<HubCallerContext> mockContext = new Mock<HubCallerContext>();

            mockContext.Setup(context => context.UserIdentifier).Returns(streamer_id.ToString());
            hub.Context = mockContext.Object;
            hub.Clients = mockClients.Object;

            await hub.BeginPlaySession(new SpectatorState
            {
                BeatmapID = beatmap_id,
                RulesetID = 0,
            });

            // check all other users were informed that streaming began
            mockClients.Verify(clients => clients.All, Times.Once);
            mockReceiver.Verify(clients => clients.UserBeganPlaying(streamer_id, It.Is<SpectatorState>(m => m.Equals(state))), Times.Once());

            var data = new FrameDataBundle(new ScoreInfo(), new[] { new LegacyReplayFrame(1234, 0, 0, ReplayButtonState.None) });

            // check streaming data is propagating to watchers
            await hub.SendFrameData(data);
            mockReceiver.Verify(clients => clients.UserSentFrames(streamer_id, data));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ReplayDataIsSaved(bool savingEnabled)
        {
            Environment.SetEnvironmentVariable("SAVE_REPLAYS", savingEnabled ? "1" : "0");

            Mock<IHubCallerClients<ISpectatorClient>> mockClients = new Mock<IHubCallerClients<ISpectatorClient>>();
            Mock<ISpectatorClient> mockReceiver = new Mock<ISpectatorClient>();
            mockClients.Setup(clients => clients.All).Returns(mockReceiver.Object);
            mockClients.Setup(clients => clients.Group(SpectatorHub.GetGroupId(streamer_id))).Returns(mockReceiver.Object);

            Mock<HubCallerContext> mockContext = new Mock<HubCallerContext>();

            mockContext.Setup(context => context.UserIdentifier).Returns(streamer_id.ToString());
            hub.Context = mockContext.Object;
            hub.Clients = mockClients.Object;

            await hub.BeginPlaySession(state);
            await hub.SendFrameData(new FrameDataBundle(new ScoreInfo(), new[] { new LegacyReplayFrame(1234, 0, 0, ReplayButtonState.None) }));

            Assert.False(Directory.Exists(SpectatorHub.REPLAYS_PATH));

            await hub.EndPlaySession(state);

            if (savingEnabled)
            {
                Assert.True(Directory.Exists(SpectatorHub.REPLAYS_PATH));

                var files = Directory.GetFiles(SpectatorHub.REPLAYS_PATH, "*", SearchOption.AllDirectories);
                Assert.Single(files);
                Assert.EndsWith($"-{streamer_id}-0-{beatmap_id}.osr", files.Single());
            }
            else
            {
                Assert.False(Directory.Exists(SpectatorHub.REPLAYS_PATH));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task NewUserBeginsWatchingStream(bool ongoing)
        {
            string connectionId = Guid.NewGuid().ToString();

            Mock<IHubCallerClients<ISpectatorClient>> mockClients = new Mock<IHubCallerClients<ISpectatorClient>>();
            Mock<ISpectatorClient> mockCaller = new Mock<ISpectatorClient>();

            mockClients.Setup(clients => clients.Caller).Returns(mockCaller.Object);
            mockClients.Setup(clients => clients.All).Returns(mockCaller.Object);

            Mock<IGroupManager> mockGroups = new Mock<IGroupManager>();

            Mock<HubCallerContext> streamerContext = new Mock<HubCallerContext>();
            streamerContext.Setup(context => context.UserIdentifier).Returns(streamer_id.ToString());

            Mock<HubCallerContext> watcherContext = new Mock<HubCallerContext>();
            watcherContext.Setup(context => context.UserIdentifier).Returns(watcher_id.ToString());
            watcherContext.Setup(context => context.ConnectionId).Returns(connectionId);

            hub.Clients = mockClients.Object;
            hub.Groups = mockGroups.Object;

            if (ongoing)
            {
                hub.Context = streamerContext.Object;
                await hub.BeginPlaySession(state);

                mockCaller.Verify(clients => clients.UserBeganPlaying(streamer_id, It.Is<SpectatorState>(m => m.Equals(state))), Times.Once);
            }

            hub.Context = watcherContext.Object;

            await hub.StartWatchingUser(streamer_id);

            mockGroups.Verify(groups => groups.AddToGroupAsync(connectionId, SpectatorHub.GetGroupId(streamer_id), default));

            mockCaller.Verify(clients => clients.UserBeganPlaying(streamer_id, It.Is<SpectatorState>(m => m.Equals(state))), Times.Exactly(ongoing ? 2 : 0));
        }

        [Fact]
        public async Task MaliciousUserCannotFinishWithPlayingState()
        {
            Mock<IHubCallerClients<ISpectatorClient>> mockClients = new Mock<IHubCallerClients<ISpectatorClient>>();
            Mock<ISpectatorClient> mockReceiver = new Mock<ISpectatorClient>();
            mockClients.Setup(clients => clients.All).Returns(mockReceiver.Object);
            mockClients.Setup(clients => clients.Group(SpectatorHub.GetGroupId(streamer_id))).Returns(mockReceiver.Object);

            Mock<HubCallerContext> mockContext = new Mock<HubCallerContext>();

            mockContext.Setup(context => context.UserIdentifier).Returns(streamer_id.ToString());
            hub.Context = mockContext.Object;
            hub.Clients = mockClients.Object;

            // Begin play.
            await hub.BeginPlaySession(new SpectatorState
            {
                BeatmapID = beatmap_id,
                RulesetID = 0,
                State = SpectatedUserState.Playing
            });

            // End play, but set a playing state.
            await hub.EndPlaySession(new SpectatorState
            {
                BeatmapID = beatmap_id,
                RulesetID = 0,
                State = SpectatedUserState.Playing
            });

            mockReceiver.Verify(clients => clients.UserFinishedPlaying(streamer_id, It.Is<SpectatorState>(m => m.State == SpectatedUserState.Quit)), Times.Once());
        }

        [Fact]
        public async Task DisconnectedUserSendsQuitState()
        {
            Mock<IHubCallerClients<ISpectatorClient>> mockClients = new Mock<IHubCallerClients<ISpectatorClient>>();
            Mock<ISpectatorClient> mockReceiver = new Mock<ISpectatorClient>();
            mockClients.Setup(clients => clients.All).Returns(mockReceiver.Object);
            mockClients.Setup(clients => clients.Group(SpectatorHub.GetGroupId(streamer_id))).Returns(mockReceiver.Object);

            Mock<HubCallerContext> mockContext = new Mock<HubCallerContext>();

            mockContext.Setup(context => context.UserIdentifier).Returns(streamer_id.ToString());
            hub.Context = mockContext.Object;
            hub.Clients = mockClients.Object;

            // Begin play.
            await hub.BeginPlaySession(new SpectatorState
            {
                BeatmapID = beatmap_id,
                RulesetID = 0,
                State = SpectatedUserState.Playing
            });

            // Forcefully terminate the connection.
            await hub.OnDisconnectedAsync(new Exception());

            mockReceiver.Verify(clients => clients.UserFinishedPlaying(streamer_id, It.Is<SpectatorState>(m => m.State == SpectatedUserState.Quit)), Times.Once());
        }
    }
}
