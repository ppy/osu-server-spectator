// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API;
using System.Linq;

namespace osu.Server.Spectator.Helpers
{
    public static class GameModeHelper
    {
        public static string GameModeToString(int gameMode)
        {
            return gameMode switch
            {
                0 => "OSU",
                1 => "TAIKO",
                2 => "FRUITS",
                3 => "MANIA",
                10 => "SENTAKKI",
                _ => "Unknown"
            };
        }

        public static string GameModeToStringSpecial(int gameMode, APIMod[] mods)
        {
            if ((gameMode != 0 && gameMode != 1 && gameMode != 2) || (!AppSettings.EnableRX && !AppSettings.EnableAP))
            {
                return GameModeToString(gameMode);
            }

            string[] modAcronyms = mods.Select(m => m.Acronym).ToArray();

            if (AppSettings.EnableAP && modAcronyms.Contains("AP"))
            {
                return "OSUAP";
            }

            if (AppSettings.EnableRX && modAcronyms.Contains("RX"))
            {
                return gameMode switch
                {
                    0 => "OSURX",
                    1 => "TAIKORX",
                    2 => "FRUITSRX",
                    _ => "Unknown"
                };
            }

            return GameModeToString(gameMode);
        }
    }
}