// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics.CodeAnalysis;

namespace osu.Server.Spectator.Hubs.Referee
{
    public static class ThrowHelper
    {
        [DoesNotReturn]
        public static void ThrowUserRestricted()
            => throw new RefereeHubException(1, "You are restricted.");

        [DoesNotReturn]
        public static void ThrowUserNotReferee()
            => throw new RefereeHubException(2, "You are not a referee in the specified room.");

        [DoesNotReturn]
        public static void ThrowRoomNotJoinable()
            => throw new RefereeHubException(3, "You cannot join this room.");

        [DoesNotReturn]
        public static void ThrowRoomDoesNotExist()
            => throw new RefereeHubException(4, "The specified room does not exist.");

        [DoesNotReturn]
        public static void ThrowUserNotInRoom()
            => throw new RefereeHubException(5, "The specified user is not in the room.");
    }
}
