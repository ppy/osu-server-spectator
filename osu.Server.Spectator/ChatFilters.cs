// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator
{
    public class ChatFilters
    {
        private readonly IDatabaseFactory factory;

        private bool filtersInitialised;
        private Regex? blockRegex;

        private readonly List<(string match, string replacement)> nonWhitespaceDelimitedReplaces = new List<(string, string)>();
        private readonly List<(Regex match, string replacement)> whitespaceDelimitedReplaces = new List<(Regex, string)>();

        public ChatFilters(IDatabaseFactory factory)
        {
            this.factory = factory;
        }

        public async Task<string> FilterAsync(string input)
        {
            if (!filtersInitialised)
                await initialiseFilters();

            if (blockRegex?.Match(input).Success == true)
                throw new InvalidStateException(string.Empty);

            // this is a touch inefficient due to string allocs,
            // but there's no way for `StringBuilder` to do case-insensitive replaces on strings
            // or any replaces on regexes at all...

            foreach (var filter in nonWhitespaceDelimitedReplaces)
                input = input.Replace(filter.match, filter.replacement, StringComparison.OrdinalIgnoreCase);

            foreach (var filter in whitespaceDelimitedReplaces)
                input = filter.match.Replace(input, filter.replacement);

            return input;
        }

        private async Task initialiseFilters()
        {
            using var db = factory.GetInstance();
            var allFilters = await db.GetAllChatFiltersAsync();

            var blockingFilters = allFilters.Where(f => f.block).ToArray();
            if (blockingFilters.Length > 0)
                blockRegex = new Regex(string.Join('|', blockingFilters.Select(singleFilterRegex)), RegexOptions.Compiled | RegexOptions.IgnoreCase);

            foreach (var nonBlockingFilter in allFilters.Where(f => !f.block))
            {
                if (nonBlockingFilter.whitespace_delimited)
                {
                    whitespaceDelimitedReplaces.Add((
                        new Regex(singleFilterRegex(nonBlockingFilter), RegexOptions.Compiled | RegexOptions.IgnoreCase),
                        nonBlockingFilter.replacement));
                }
                else
                {
                    nonWhitespaceDelimitedReplaces.Add((nonBlockingFilter.match, nonBlockingFilter.replacement));
                }
            }

            filtersInitialised = true;
        }

        private static string singleFilterRegex(chat_filter filter)
        {
            string term = Regex.Escape(filter.match);
            if (filter.whitespace_delimited)
                term = $@"\b{term}\b";
            return term;
        }
    }
}
