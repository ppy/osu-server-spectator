using osu.Game.Online.Multiplayer;

namespace osu.Server.Spectator.Extensions
{
    public static class MultiplayerUserStateExtensions
    {
        public static bool IsGameplayState(this MultiplayerUserState state)
        {
            switch (state)
            {
                default:
                    return false;

                case MultiplayerUserState.WaitingForLoad:
                case MultiplayerUserState.Loaded:
                case MultiplayerUserState.ReadyForGameplay:
                case MultiplayerUserState.Playing:
                    return true;
            }
        }
    }
}