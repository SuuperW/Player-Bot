using System;
using System.Collections.Generic;
using System.Text;

using Newtonsoft.Json.Linq;

namespace Player_Bot
{
    class GuildConfigInfo
    {
        public ulong verifiedRole;
        public ulong hhRole;

        public GuildConfigInfo()
        {
            // nothing
        }
        public GuildConfigInfo(JToken jToken)
        {
            verifiedRole = (ulong)jToken["verified_role"];
            hhRole = (ulong)jToken["hh_role"];
        }

        public JObject GetJObject()
        {
            JObject obj = new JObject();
            obj["verified_role"] = verifiedRole;
            obj["hh_role"] = hhRole;

            return obj;
        }
    }
}
