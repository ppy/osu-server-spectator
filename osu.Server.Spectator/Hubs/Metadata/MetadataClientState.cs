// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Users;

namespace osu.Server.Spectator.Hubs.Metadata
{
    public class MetadataClientState : ClientState
    {
        public UserActivity? UserActivity { get; set; }

        public UserStatus? UserStatus { get; set; }

        public string? VersionHash { get; set; }

        public MetadataClientState(in string connectionId, in int userId, in string? versionHash)
            : base(in connectionId, in userId)
        {
            VersionHash = versionHash;
        }

        public UserPresence ToUserPresence() => new UserPresence
        {
            Activity = UserActivity,
            Status = UserStatus,
        };
    }
}
