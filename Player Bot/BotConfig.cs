using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

using Newtonsoft.Json.Linq;
using Discord;

namespace Player_Bot
{
    class BotConfig
    {
        public int loggingLevel = 2;
        public ulong loggingChannel = 0;

        public SortedSet<ulong> hhChannels;
        public SortedList<ulong, GuildConfigInfo> guilds;

        public BotConfig(string path)
        {
            JObject obj;
            if (File.Exists(path))
                obj = JObject.Parse(File.ReadAllText(path));
            else
                obj = new JObject();

            if (obj.ContainsKey("logging_level"))
                loggingLevel = (int)obj["logging_level"];
            if (obj.ContainsKey("logging_channel"))
                loggingChannel = (ulong)obj["logging_channel"];

            hhChannels = new SortedSet<ulong>();
            if (obj.ContainsKey("hh_channels"))
            {
                foreach (JToken token in (JArray)obj["hh_channels"])
                    hhChannels.Add((ulong)token);
            }

            guilds = new SortedList<ulong, GuildConfigInfo>();
            if (obj.ContainsKey("guilds"))
            {
                foreach (JToken token in (JArray)obj["guilds"])
                    guilds.Add((ulong)token["id"], new GuildConfigInfo(token));
            }
        }

        public void Save(string path)
        {
            JObject obj = new JObject();
            obj["logging_level"] = loggingLevel;
            obj["logging_channel"] = loggingChannel;

            obj["hh_channels"] = JArray.FromObject(hhChannels);

            JArray array = new JArray();
            for (int i = 0; i < guilds.Count; i++)
            {
                JObject jGuild = guilds.Values[i].GetJObject();
                jGuild["id"] = guilds.Keys[i];
                array.Add(jGuild);
            }
            obj["guilds"] = array;


            File.WriteAllText(path, obj.ToString());
        }
    }
}
