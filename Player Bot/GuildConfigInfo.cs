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
        }

        public JObject GetJObject()
        {
            JObject obj = new JObject();
            obj["verified_role"] = verifiedRole;
            obj["hh_role"] = hhRole;
            obj["trusted_rold"] = trustedRole;

            return obj;
        }
    }
}
