using System;
using System.Collections.Generic;
using System.Text;

using Newtonsoft.Json.Linq;

namespace Player_Bot
{
    class GuildConfigInfo
    {
        public ulong verifiedRole = 0;
        public ulong hhRole = 0;
        public ulong trustedRole = 0;

        public SortedSet<ulong> publicRoles;
        public SortedSet<ulong> pr2GuildRoles;

        public GuildConfigInfo()
        {
            // nothing
        }
        public GuildConfigInfo(JToken jToken)
        {
            if (jToken["verified_role"] != null)
                verifiedRole = (ulong)jToken["verified_role"];
            if (jToken["hh_role"] != null)
                hhRole = (ulong)jToken["hh_role"];
            if (jToken["trusted_role"] != null)
                trustedRole = (ulong)jToken["trusted_role"];

            publicRoles = new SortedSet<ulong>();
            if (jToken["public_roles"] != null)
            {
                foreach (JToken token in (JArray)jToken["public_roles"])
                    publicRoles.Add((ulong)token);
            }
            pr2GuildRoles = new SortedSet<ulong>();
            if (jToken["pr2_guilds"] != null)
            {
                foreach (JToken token in (JArray)jToken["pr2_guilds"])
                    pr2GuildRoles.Add((ulong)token);
            }
        }

        public JObject GetJObject()
        {
            JObject obj = new JObject();
            obj["verified_role"] = verifiedRole;
            obj["hh_role"] = hhRole;
            obj["trusted_role"] = trustedRole;

            obj["public_roles"] = JArray.FromObject(publicRoles);
            obj["pr2_guilds"] = JArray.FromObject(pr2GuildRoles);

            return obj;
        }
    }
}
