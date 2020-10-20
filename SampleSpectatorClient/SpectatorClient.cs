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
            connection.On<int>(nameof(ISpectatorClient.UserBeganPlaying), ((ISpectatorClient)this).UserBeganPlaying);
            connection.On<int>(nameof(ISpectatorClient.UserFinishedPlaying), ((ISpectatorClient)this).UserFinishedPlaying);
        }

        Task ISpectatorClient.UserBeganPlaying(int beatmapId)
        {
            Console.WriteLine($"{connection.ConnectionId} Received user playing event {beatmapId}");
            return Task.CompletedTask;
        }

        Task ISpectatorClient.UserFinishedPlaying(int beatmapId)
        {
            Console.WriteLine($"{connection.ConnectionId} Received user finished event {beatmapId}");
            return Task.CompletedTask;
        }

        public void BeginPlaying(int beatmapId)
        {
            connection.SendAsync(nameof(ISpectatorServer.BeginPlaySession), beatmapId);
        }

        public void EndPlaying(int beatmapId)
        {
            connection.SendAsync(nameof(ISpectatorServer.EndPlaySession), beatmapId);
        }
    }
}