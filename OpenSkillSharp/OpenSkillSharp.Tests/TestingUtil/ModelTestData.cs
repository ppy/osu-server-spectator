using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using OpenSkillSharp.Rating;

namespace OpenSkillSharp.Tests.TestingUtil;

public class ModelTestData
{
    private static readonly JsonSerializerOptions _serializerOptions = new() { PropertyNameCaseInsensitive = true };

    [JsonPropertyName("normal")] public Dictionary<string, List<TestData>> NormalData { get; init; } = null!;

    [JsonPropertyName("ranks")] public Dictionary<string, List<TestData>> RanksData { get; init; } = null!;

    [JsonPropertyName("scores")] public Dictionary<string, List<TestData>> ScoresData { get; init; } = null!;

    [JsonPropertyName("margins")] public Dictionary<string, List<TestData>> MarginsData { get; init; } = null!;

    [JsonPropertyName("limit_sigma")] public Dictionary<string, List<TestData>> LimitSigmaData { get; init; } = null!;

    [JsonPropertyName("ties")] public Dictionary<string, List<TestData>> TiesData { get; init; } = null!;

    [JsonPropertyName("weights")] public Dictionary<string, List<TestData>> WeightsData { get; init; } = null!;

    [JsonPropertyName("balance")] public Dictionary<string, List<TestData>> BalanceData { get; init; } = null!;

    public TestData Model { get; init; } = null!;

    [JsonIgnore] public IList<ITeam> Normal => ConvertDictionaryData(NormalData);

    [JsonIgnore] public IList<ITeam> Ranks => ConvertDictionaryData(RanksData);

    [JsonIgnore] public IList<ITeam> Scores => ConvertDictionaryData(ScoresData);

    [JsonIgnore] public IList<ITeam> Margins => ConvertDictionaryData(MarginsData);

    [JsonIgnore] public IList<ITeam> LimitSigma => ConvertDictionaryData(LimitSigmaData);

    [JsonIgnore] public IList<ITeam> Ties => ConvertDictionaryData(TiesData);

    [JsonIgnore] public IList<ITeam> Weights => ConvertDictionaryData(WeightsData);

    [JsonIgnore] public IList<ITeam> Balance => ConvertDictionaryData(BalanceData);

    public static ModelTestData FromJson(string model)
    {
        string? asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string dataPath = Path.Combine(asmDir ?? "", "Models", "Data", $"{model}.json");

        if (!File.Exists(dataPath))
        {
            throw new FileNotFoundException($"Could not find data file at: {dataPath}");
        }

        string json = File.ReadAllText(dataPath);
        return JsonSerializer.Deserialize<ModelTestData>(json, _serializerOptions)!;
    }

    private static IList<ITeam> ConvertDictionaryData(Dictionary<string, List<TestData>> data)
    {
        return data.Select(kvp => new Team
        {
            Players = kvp.Value.Select(d => new OpenSkillSharp.Rating.Rating { Mu = d.Mu, Sigma = d.Sigma })
        }).Cast<ITeam>().ToList();
    }

    public class TestData
    {
        public double Mu { get; set; }

        public double Sigma { get; set; }
    }
}