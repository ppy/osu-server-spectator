using System;

namespace osu.Server.Spectator.Hubs
{
    [Serializable]
    public class FrameDataBundle
    {
        public string data { get; set; }

        public FrameDataBundle(string data)
        {
            this.data = data;
        }
    }
}