// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using Moq;
using osu.Game.Online.Multiplayer;
using osu.Server.Spectator.Database;
using osu.Server.Spectator.Database.Models;
using Xunit;

namespace osu.Server.Spectator.Tests
{
    public class ChatFiltersTest
    {
        private readonly Mock<IDatabaseFactory> factoryMock;
        private readonly Mock<IDatabaseAccess> databaseMock;

        public ChatFiltersTest()
        {
            factoryMock = new Mock<IDatabaseFactory>();
            databaseMock = new Mock<IDatabaseAccess>();

            factoryMock.Setup(factory => factory.GetInstance()).Returns(databaseMock.Object);
        }

        [Theory]
        [InlineData("bad phrase", "good phrase")]
        [InlineData("WHAT HAPPENS IF I SAY BAD THING IN CAPS", "WHAT HAPPENS IF I SAY good THING IN CAPS")]
        [InlineData("thing is bad", "thing is good")]
        [InlineData("look at this badness", "look at this goodness")]
        public async Task TestPlainFilterReplacement(string input, string expectedOutput)
        {
            databaseMock.Setup(db => db.GetAllChatFiltersAsync()).ReturnsAsync([
                new chat_filter { match = "bad", replacement = "good" },
                new chat_filter { match = "fullword", replacement = "okay", whitespace_delimited = true },
                new chat_filter { match = "absolutely forbidden", replacement = "", block = true }
            ]);

            var filters = new ChatFilters(factoryMock.Object);

            Assert.Equal(expectedOutput, await filters.FilterAsync(input));
        }

        [Theory]
        [InlineData("fullword at the start", "okay at the start")]
        [InlineData("FULLWORD IN CAPS!!", "okay IN CAPS!!")]
        [InlineData("at the end is fullword", "at the end is okay")]
        [InlineData("middle is where the fullword is", "middle is where the okay is")]
        [InlineData("anotherfullword is not replaced", "anotherfullword is not replaced")]
        [InlineData("fullword fullword2", "okay great")]
        [InlineData("fullwordfullword2", "fullwordfullword2")]
        [InlineData("i do a delimiter/inside", "i do a nice try")]
        public async Task TestWhitespaceDelimitedFilterReplacement(string input, string expectedOutput)
        {
            databaseMock.Setup(db => db.GetAllChatFiltersAsync()).ReturnsAsync([
                new chat_filter { match = "bad", replacement = "good" },
                new chat_filter { match = "fullword", replacement = "okay", whitespace_delimited = true },
                new chat_filter { match = "fullword2", replacement = "great", whitespace_delimited = true },
                new chat_filter { match = "delimiter/inside", replacement = "nice try", whitespace_delimited = true },
                new chat_filter { match = "absolutely forbidden", replacement = "", block = true }
            ]);

            var filters = new ChatFilters(factoryMock.Object);

            Assert.Equal(expectedOutput, await filters.FilterAsync(input));
        }

        [Theory]
        [InlineData("absolutely forbidden")]
        [InlineData("sPoNGeBoB SaYS aBSolUtElY FoRbIdDeN")]
        [InlineData("this is absolutely forbidden full stop!!!")]
        public async Task TestBlockingFilter(string input)
        {
            databaseMock.Setup(db => db.GetAllChatFiltersAsync()).ReturnsAsync([
                new chat_filter { match = "bad", replacement = "good" },
                new chat_filter { match = "fullword", replacement = "okay", whitespace_delimited = true },
                new chat_filter { match = "absolutely forbidden", replacement = "", block = true }
            ]);

            var filters = new ChatFilters(factoryMock.Object);

            await Assert.ThrowsAsync<InvalidStateException>(() => filters.FilterAsync(input));
        }

        [Fact]
        public async Task TestLackOfBlockingFilters()
        {
            databaseMock.Setup(db => db.GetAllChatFiltersAsync()).ReturnsAsync([
                new chat_filter { match = "bad", replacement = "good" },
                new chat_filter { match = "fullword", replacement = "okay", whitespace_delimited = true },
            ]);

            var filters = new ChatFilters(factoryMock.Object);

            await filters.FilterAsync("this should be completely fine"); // should not throw
        }
    }
}
