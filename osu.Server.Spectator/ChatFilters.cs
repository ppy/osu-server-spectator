// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Immutable;
using System.Text;
using System.Threading.Tasks;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;

namespace osu.Server.Spectator
{
    public class ChatFilters
    {
        private readonly IDatabaseFactory factory;
        private ImmutableArray<chat_filter>? filters;

        public ChatFilters(IDatabaseFactory factory)
        {
            this.factory = factory;
        }

        public async Task<string> FilterAsync(string input)
        {
            if (filters == null)
            {
                using var db = factory.GetInstance();
                filters = (await db.GetAllChatFiltersAsync()).ToImmutableArray();
            }

            var stringBuilder = new StringBuilder(input);

            foreach (var filter in filters)
                stringBuilder.Replace(filter.match, filter.replacement);

            return stringBuilder.ToString();
        }
    }
}
