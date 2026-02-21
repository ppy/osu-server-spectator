// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee
{
    [PublicAPI]
    public static class ThrowHelper
    {
        /// <summary>
        /// Error 1: You are restricted.
        /// </summary>
        [DoesNotReturn]
        public static void ThrowUserRestricted()
            => throw new RefereeHubException(1, "You are restricted.");

        /// <summary>
        /// Error 2: You are not a referee in the specified room.
        /// </summary>
        [DoesNotReturn]
        public static void ThrowUserNotReferee()
            => throw new RefereeHubException(2, "You are not a referee in the specified room.");

        /// <summary>
        /// Error 3: You cannot join this room.
        /// </summary>
        [DoesNotReturn]
        public static void ThrowRoomNotJoinable()
            => throw new RefereeHubException(3, "You cannot join this room.");

        /// <summary>
        /// Error 4: The specified room does not exist.
        /// </summary>
        [DoesNotReturn]
        public static void ThrowRoomDoesNotExist()
            => throw new RefereeHubException(4, "The specified room does not exist.");

        /// <summary>
        /// Error 5: The specified user is not in the room.
        /// </summary>
        [DoesNotReturn]
        public static void ThrowUserNotInRoom()
            => throw new RefereeHubException(5, "The specified user is not in the room.");

        /// <summary>
        /// Error 6: The specified beatmap does not exist.
        /// </summary>
        [DoesNotReturn]
        public static void ThrowBeatmapDoesNotExist()
            => throw new RefereeHubException(6, "The specified beatmap does not exist.");

        /// <summary>
        /// Error 7: Cannot perform this operation with the current match type.
        /// </summary>
        [DoesNotReturn]
        public static void ThrowIncorrectMatchType()
            => throw new RefereeHubException(7, "Cannot perform this operation with the current match type.");

        /// <summary>
        /// Error 8: No active match start countdown.
        /// </summary>
        [DoesNotReturn]
        public static void ThrowNoActiveCountdown()
            => throw new RefereeHubException(8, "No active match start countdown.");

        /// <summary>
        /// Error 9: Cannot perform this operation in the current room state.
        /// </summary>
        [DoesNotReturn]
        public static void ThrowRoomStateInvalidForOperation()
            => throw new RefereeHubException(9, "Cannot perform this operation in the current room state.");

        /// <summary>
        /// Error 10: Invalid ruleset.
        /// </summary>
        [DoesNotReturn]
        public static void ThrowInvalidRuleset()
            => throw new RefereeHubException(10, "Invalid ruleset.");

        /// <summary>
        /// Error 11: Invalid beatmap and ruleset combination.
        /// </summary>
        [DoesNotReturn]
        public static void ThrowInvalidBeatmapRulesetCombination()
            => throw new RefereeHubException(11, "Invalid beatmap and ruleset combination.");

        /// <summary>
        /// Error 12: Invalid mods. (More details provided after newline.)
        /// </summary>
        [DoesNotReturn]
        public static void ThrowInvalidMods(string details)
            => throw new RefereeHubException(12, $"Invalid mods.\nDetails: {details}");

        /// <summary>
        /// Error 13: Cannot specify allowed mods in freestyle.
        /// </summary>
        [DoesNotReturn]
        public static void ThrowNoAllowedModsInFreestyle()
            => throw new RefereeHubException(13, "Cannot specify allowed mods in freestyle.");

        /// <summary>
        /// Error 14: The specified playlist item does not exist.
        /// </summary>
        [DoesNotReturn]
        public static void ThrowPlaylistItemDoesNotExist()
            => throw new RefereeHubException(14, "The specified playlist item does not exist.");
    }
}
