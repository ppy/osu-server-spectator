using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using osu.Server.Spectator.Hubs;

namespace SampleSpectatorClient
{
    public class SpectatorClient : ISpectatorClient
    {
        private readonly HubConnection connection;

        public SpectatorClient(HubConnection connection)
        {
            this.connection = connection;

            // this is kind of SILLY
            // https://github.com/dotnet/aspnetcore/issues/15198
            connection.On<string, int>(nameof(ISpectatorClient.UserBeganPlaying), ((ISpectatorClient)this).UserBeganPlaying);
            connection.On<string, FrameDataBundle>(nameof(ISpectatorClient.UserSentFrames), ((ISpectatorClient)this).UserSentFrames);
            connection.On<string, int>(nameof(ISpectatorClient.UserFinishedPlaying), ((ISpectatorClient)this).UserFinishedPlaying);
        }

        Task ISpectatorClient.UserBeganPlaying(string userId, int beatmapId)
        {
            Console.WriteLine($"{connection.ConnectionId} Received user playing event {beatmapId}");

            if (connection.ConnectionId != userId)
            {
                Console.WriteLine($"{connection.ConnectionId} watching other user {userId}");
                WatchUser(userId);
            }

            return Task.CompletedTask;
        }

        Task ISpectatorClient.UserFinishedPlaying(string userId, int beatmapId)
        {
            Console.WriteLine($"{connection.ConnectionId} Received user finished event {beatmapId}");
            return Task.CompletedTask;
        }

        Task ISpectatorClient.UserSentFrames(string userId, FrameDataBundle data)
        {
            Console.WriteLine($"{connection.ConnectionId} Received frames from {userId}: {data.data}");
            return Task.CompletedTask;
        }

        public Task BeginPlaying(int beatmapId) => connection.SendAsync(nameof(ISpectatorServer.BeginPlaySession), beatmapId);

        public Task SendFrames(FrameDataBundle data) => connection.SendAsync(nameof(ISpectatorServer.SendFrameData), data);

        public Task EndPlaying(int beatmapId) => connection.SendAsync(nameof(ISpectatorServer.EndPlaySession), beatmapId);

        private Task WatchUser(string userId) => connection.SendAsync(nameof(ISpectatorServer.StartWatchingUser), userId);
    }
}