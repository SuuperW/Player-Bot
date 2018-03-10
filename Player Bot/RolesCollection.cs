using System;
using System.Collections.Generic;
using System.IO;

using Newtonsoft.Json.Linq;

namespace Player_Bot
{
    class RolesCollection
    {
        public SortedSet<ulong> publicRoles;
        public SortedSet<ulong> pr2GuildRoles;

        public RolesCollection(string path)
        {
            JObject obj;
            if (File.Exists(path))
                obj = JObject.Parse(File.ReadAllText(path));
            else
                obj = new JObject();

            if (!obj.ContainsKey("public"))
                obj["public"] = new JArray();
            if (!obj.ContainsKey("pr2_guilds"))
                obj["pr2_guilds"] = new JArray();

            publicRoles = new SortedSet<ulong>();
            foreach (JToken token in (JArray)obj["public"])
                publicRoles.Add((ulong)token);
            pr2GuildRoles = new SortedSet<ulong>();
            foreach (JToken token in (JArray)obj["pr2_guilds"])
                pr2GuildRoles.Add((ulong)token);
        }

        public void Save(string path)
        {
            JObject obj = new JObject();
            obj["public"] = JArray.FromObject(publicRoles);
            obj["pr2_guilds"] = JArray.FromObject(pr2GuildRoles);

            File.WriteAllText(path, obj.ToString());
        }
    }
}
