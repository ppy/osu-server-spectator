// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Server.Spectator.Database.Models
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [Serializable]
    [Table("solo_scores")]
    public class SoloScore
    {
        public ulong id { get; set; }

        public int user_id { get; set; }

        public int beatmap_id { get; set; }

        public int ruleset_id { get; set; }

        public string data
        {
            get => JsonConvert.SerializeObject(ScoreInfo);
            set
            {
                var scoreInfo = JsonConvert.DeserializeObject<SoloScoreInfo>(value);

                if (scoreInfo == null)
                    return;

                ScoreInfo = scoreInfo;

                ScoreInfo.ID = id;
                ScoreInfo.BeatmapID = beatmap_id;
                ScoreInfo.UserID = user_id;
                ScoreInfo.RulesetID = ruleset_id;
            }
        }

        [JsonIgnore]
        public bool preserve { get; set; }

        [JsonIgnore]
        public bool has_replay { get; set; }

        [JsonIgnore]
        public SoloScoreInfo ScoreInfo = new SoloScoreInfo();

        [JsonIgnore]
        public DateTimeOffset created_at { get; set; }

        [JsonIgnore]
        public DateTimeOffset updated_at { get; set; }

        public override string ToString() => $"score_id: {id} user_id: {user_id}";
    }
}
