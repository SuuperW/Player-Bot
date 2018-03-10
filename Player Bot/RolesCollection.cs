using System;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;

namespace Player_Bot
{
    class RolesCollection
    {
        public SortedSet<ulong> publicRoles;
        public SortedSet<ulong> pr2GuildRoles;

        public RolesCollection(JObject obj)
        {
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

            System.IO.File.WriteAllText(path, obj.ToString());
        }
    }
}
