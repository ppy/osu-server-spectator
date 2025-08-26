# Run this script to use a local copy of osu rather than fetching it from nuget.
# It expects the osu directory to be at the same level as the osu-tools directory

dotnet remove "osu.Game.Rulesets.Sentakki" package ppy.osu.Game
dotnet add "osu.Game.Rulesets.Sentakki/osu.Game.Rulesets.Sentakki.csproj" reference "osu/osu.Game/osu.Game.csproj"

