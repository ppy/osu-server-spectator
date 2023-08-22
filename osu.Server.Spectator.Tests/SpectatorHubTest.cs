// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using osu.Game.Beatmaps;
using osu.Game.Online.Spectator;
using osu.Game.Replays.Legacy;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using osu.Server.Spectator.Entities;
using osu.Server.Spectator.Hubs;
using osu.Server.Spectator.Hubs.Spectator;
using osu.Server.Spectator.Storage;
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

        private readonly ScoreUploader scoreUploader;
        private readonly Mock<IScoreStorage> mockScoreStorage;
        private readonly Mock<IDatabaseAccess> mockDatabase;

        public SpectatorHubTest()
        {
            if (Directory.Exists(SpectatorHub.REPLAYS_PATH))
                Directory.Delete(SpectatorHub.REPLAYS_PATH, true);

            // not used for now, but left here for potential future usage.
            MemoryDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

            var clientStates = new EntityStore<SpectatorClientState>();

            mockDatabase = new Mock<IDatabaseAccess>();
            mockDatabase.Setup(db => db.GetUsernameAsync(streamer_id)).ReturnsAsync(() => "user");

            mockDatabase.Setup(db => db.GetBeatmapAsync(It.IsAny<int>()))
                        .ReturnsAsync((int id) => new database_beatmap
                        {
                            approved = BeatmapOnlineStatus.Ranked,
                            checksum = (id == beatmap_id ? "d2a97fb2fa4529a5e857fe0466dc1daf" : string.Empty)
                        });

            var databaseFactory = new Mock<IDatabaseFactory>();
            databaseFactory.Setup(factory => factory.GetInstance()).Returns(mockDatabase.Object);

            mockScoreStorage = new Mock<IScoreStorage>();
            scoreUploader = new ScoreUploader(databaseFactory.Object, mockScoreStorage.Object);

            var mockScoreProcessedSubscriber = new Mock<IScoreProcessedSubscriber>();

            hub = new SpectatorHub(cache, clientStates, databaseFactory.Object, scoreUploader, mockScoreProcessedSubscriber.Object);
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

            await hub.BeginPlaySession(0, new SpectatorState
            {
                BeatmapID = beatmap_id,
                RulesetID = 0,
            });

            // check all other users were informed that streaming began
            mockClients.Verify(clients => clients.All, Times.Once);
            mockReceiver.Verify(clients => clients.UserBeganPlaying(streamer_id, It.Is<SpectatorState>(m => m.Equals(state))), Times.Once());

            var data = new FrameDataBundle(
                new FrameHeader(new ScoreInfo(), new ScoreProcessorStatistics()),
                new[] { new LegacyReplayFrame(1234, 0, 0, ReplayButtonState.None) });

            // check streaming data is propagating to watchers
            await hub.SendFrameData(data);
            mockReceiver.Verify(clients => clients.UserSentFrames(streamer_id, data));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ReplayDataIsSaved(bool savingEnabled)
        {
            AppSettings.SaveReplays = savingEnabled;

            Mock<IHubCallerClients<ISpectatorClient>> mockClients = new Mock<IHubCallerClients<ISpectatorClient>>();
            Mock<ISpectatorClient> mockReceiver = new Mock<ISpectatorClient>();
            mockClients.Setup(clients => clients.All).Returns(mockReceiver.Object);
            mockClients.Setup(clients => clients.Group(SpectatorHub.GetGroupId(streamer_id))).Returns(mockReceiver.Object);

            Mock<HubCallerContext> mockContext = new Mock<HubCallerContext>();

            mockContext.Setup(context => context.UserIdentifier).Returns(streamer_id.ToString());
            hub.Context = mockContext.Object;
            hub.Clients = mockClients.Object;

            mockDatabase.Setup(db => db.GetScoreIdFromToken(1234)).Returns(Task.FromResult<long?>(456));

            await hub.BeginPlaySession(1234, state);
            await hub.SendFrameData(new FrameDataBundle(
                new FrameHeader(new ScoreInfo(), new ScoreProcessorStatistics()),
                new[] { new LegacyReplayFrame(1234, 0, 0, ReplayButtonState.None) }));
            await hub.EndPlaySession(state);

            await scoreUploader.Flush();

            if (savingEnabled)
                mockScoreStorage.Verify(s => s.WriteAsync(It.Is<Score>(score => score.ScoreInfo.OnlineID == 456)), Times.Once);
            else
                mockScoreStorage.Verify(s => s.WriteAsync(It.IsAny<Score>()), Times.Never);
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
                await hub.BeginPlaySession(0, state);

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
            await hub.BeginPlaySession(0, new SpectatorState
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
            await hub.BeginPlaySession(0, new SpectatorState
            {
                BeatmapID = beatmap_id,
                RulesetID = 0,
                State = SpectatedUserState.Playing
            });

            // Forcefully terminate the connection.
            await hub.OnDisconnectedAsync(new Exception());

            mockReceiver.Verify(clients => clients.UserFinishedPlaying(streamer_id, It.Is<SpectatorState>(m => m.State == SpectatedUserState.Quit)), Times.Once());
        }

        [Theory]
        [InlineData(BeatmapOnlineStatus.LocallyModified, false)]
        [InlineData(BeatmapOnlineStatus.None, false)]
        [InlineData(BeatmapOnlineStatus.Graveyard, false)]
        [InlineData(BeatmapOnlineStatus.WIP, false)]
        [InlineData(BeatmapOnlineStatus.Pending, false)]
        [InlineData(BeatmapOnlineStatus.Ranked, true)]
        [InlineData(BeatmapOnlineStatus.Approved, true)]
        [InlineData(BeatmapOnlineStatus.Qualified, true)]
        [InlineData(BeatmapOnlineStatus.Loved, true)]
        public async Task ScoresAreOnlySavedOnRankedBeatmaps(BeatmapOnlineStatus status, bool saved)
        {
            AppSettings.SaveReplays = true;

            Mock<IHubCallerClients<ISpectatorClient>> mockClients = new Mock<IHubCallerClients<ISpectatorClient>>();
            Mock<ISpectatorClient> mockReceiver = new Mock<ISpectatorClient>();
            mockClients.Setup(clients => clients.All).Returns(mockReceiver.Object);
            mockClients.Setup(clients => clients.Group(SpectatorHub.GetGroupId(streamer_id))).Returns(mockReceiver.Object);

            Mock<HubCallerContext> mockContext = new Mock<HubCallerContext>();

            mockContext.Setup(context => context.UserIdentifier).Returns(streamer_id.ToString());
            hub.Context = mockContext.Object;
            hub.Clients = mockClients.Object;

            mockDatabase.Setup(db => db.GetScoreIdFromToken(1234)).Returns(Task.FromResult<long?>(456));
            mockDatabase.Setup(db => db.GetBeatmapAsync(beatmap_id)).Returns(Task.FromResult(new database_beatmap
            {
                approved = status,
                checksum = "checksum"
            })!);

            await hub.BeginPlaySession(1234, state);
            await hub.SendFrameData(new FrameDataBundle(
                new FrameHeader(new ScoreInfo(), new ScoreProcessorStatistics()),
                new[] { new LegacyReplayFrame(1234, 0, 0, ReplayButtonState.None) }));
            await hub.EndPlaySession(state);

            await scoreUploader.Flush();

            if (saved)
                mockScoreStorage.Verify(s => s.WriteAsync(It.Is<Score>(score => score.ScoreInfo.OnlineID == 456)), Times.Once);
            else
                mockScoreStorage.Verify(s => s.WriteAsync(It.Is<Score>(score => score.ScoreInfo.OnlineID == 456)), Times.Never);
        }
    }
}
