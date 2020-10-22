using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using osu.Game.Online.Spectator;

namespace SampleSpectatorClient
{
    public class SpectatorClient : ISpectatorClient
    {
        private readonly HubConnection connection;

        private readonly List<string> watchingUsers = new List<string>();

        public SpectatorClient(HubConnection connection)
        {
            this.connection = connection;

            // this is kind of SILLY
            // https://github.com/dotnet/aspnetcore/issues/15198
            connection.On<string, SpectatorState>(nameof(ISpectatorClient.UserBeganPlaying), ((ISpectatorClient)this).UserBeganPlaying);
            connection.On<string, FrameDataBundle>(nameof(ISpectatorClient.UserSentFrames), ((ISpectatorClient)this).UserSentFrames);
            connection.On<string, SpectatorState>(nameof(ISpectatorClient.UserFinishedPlaying), ((ISpectatorClient)this).UserFinishedPlaying);
        }

        Task ISpectatorClient.UserBeganPlaying(string userId, SpectatorState state)
        {
            if (connection.ConnectionId != userId)
            {
                if (watchingUsers.Contains(userId))
                {
                    Console.WriteLine($"{connection.ConnectionId} received began playing for already watched user {userId}");
                }
                else
                {
                    Console.WriteLine($"{connection.ConnectionId} requesting watch other user {userId}");
                    WatchUser(userId);
                    watchingUsers.Add(userId);
                }
            }
            else
            {
                Console.WriteLine($"{connection.ConnectionId} Received user playing event for self {state}");
            }

            return Task.CompletedTask;
        }

        Task ISpectatorClient.UserFinishedPlaying(string userId, SpectatorState state)
        {
            Console.WriteLine($"{connection.ConnectionId} Received user finished event {state}");
            return Task.CompletedTask;
        }

        Task ISpectatorClient.UserSentFrames(string userId, FrameDataBundle data)
        {
            Console.WriteLine($"{connection.ConnectionId} Received frames from {userId}: {data.Frames.First()}");
            return Task.CompletedTask;
        }

        public Task BeginPlaying(SpectatorState state) => connection.SendAsync(nameof(ISpectatorServer.BeginPlaySession), state);

        public Task SendFrames(FrameDataBundle data) => connection.SendAsync(nameof(ISpectatorServer.SendFrameData), data);

        public Task EndPlaying(SpectatorState state) => connection.SendAsync(nameof(ISpectatorServer.EndPlaySession), state);

        public Task WatchUser(string userId) => connection.SendAsync(nameof(ISpectatorServer.StartWatchingUser), userId);
    }
}
