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

        public int[] FriendIds { get; set; } = [];

        public MetadataClientState(in string connectionId, in int userId, in string? versionHash)
            : base(in connectionId, in userId)
        {
            VersionHash = versionHash;
        }

        /// <summary>
        /// Creates a <see cref="UserPresence"/> which represents this user's state as it should be broadcast to other users.
        /// </summary>
        /// <returns>The representative user presence, or <c>null</c> if the user should appear offline.</returns>
        public UserPresence? ToUserPresence()
        {
            switch (UserStatus)
            {
                case null:
                case Game.Users.UserStatus.Offline:
                    return null;

                default:
                    return new UserPresence
                    {
                        Activity = UserActivity,
                        Status = UserStatus,
                    };
            }
        }
    }
}
