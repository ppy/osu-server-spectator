// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using JetBrains.Annotations;

namespace osu.Server.Spectator.Hubs.Referee.Models.Requests
{
    /// <summary>
    /// Changes the current playlist item.
    /// </summary>
    [PublicAPI]
    public class EditCurrentPlaylistItemRequest : EditPlaylistItemRequestParameters
    {
    }
}
