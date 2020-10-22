using System;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using osu.Game.Online.Spectator;

namespace osu.Server.Spectator.Hubs
{
    [UsedImplicitly]
    [Authorize]
    public class SpectatorHub : Hub<ISpectatorClient>, ISpectatorServer
    {
        private readonly IDistributedCache cache;

        public SpectatorHub(IDistributedCache cache)
        {
            this.cache = cache;
        }

        public async Task BeginPlaySession(SpectatorState state)
        {
            await updateUserState(state);

            Console.WriteLine($"User {Context.UserIdentifier} beginning play session ({state})");

            // let's broadcast to every player temporarily. probably won't stay this way.
            await Clients.All.UserBeganPlaying(Context.UserIdentifier, state);
        }

        public async Task SendFrameData(FrameDataBundle data)
        {
            Console.WriteLine($"Receiving frame data ({data.Frames.First()})..");
            await Clients.Group(GetGroupId(Context.UserIdentifier)).UserSentFrames(Context.UserIdentifier, data);
        }

        public async Task EndPlaySession(SpectatorState state)
        {
            Console.WriteLine($"User {Context.UserIdentifier} ending play session ({state})");

            await cache.RemoveAsync(GetStateId(Context.UserIdentifier));
            await Clients.All.UserFinishedPlaying(Context.UserIdentifier, state);
        }

        public async Task StartWatchingUser(string userId)
        {
            Console.WriteLine($"User {Context.UserIdentifier} watching {userId}");

            // send the user's state if exists
            var state = await getStateFromUser(userId);

            if (state != null)
            {
                await Clients.Caller.UserBeganPlaying(userId, state);
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupId(userId));
        }

        public async Task EndWatchingUser(string userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupId(userId));
        }

        public override Task OnConnectedAsync()
        {
            Console.WriteLine($"User {Context.UserIdentifier} connected!");
            return base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            Console.WriteLine($"User {Context.UserIdentifier} disconnected!");

            var state = await getStateFromUser(Context.UserIdentifier);

            if (state != null)
            {
                // clean up user on disconnection
                await EndPlaySession(state);
            }

            await base.OnDisconnectedAsync(exception);
        }

        private async Task updateUserState(SpectatorState state)
        {
            await cache.SetStringAsync(GetStateId(Context.UserIdentifier), JsonConvert.SerializeObject(state));
        }

        private async Task<SpectatorState?> getStateFromUser(string userId)
        {
            var jsonString = await cache.GetStringAsync(GetStateId(userId));

            if (jsonString == null)
                return null;

            // todo: error checking logic?
            var state = JsonConvert.DeserializeObject<SpectatorState>(jsonString);

            return state;
        }

        public static string GetStateId(string userId) => $"state:{userId}";

        public static string GetGroupId(string userId) => $"watch:{userId}";
    }
}
