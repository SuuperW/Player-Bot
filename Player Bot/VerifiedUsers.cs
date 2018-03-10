using System;
using System.Collections.Generic;
using System.IO;

using Newtonsoft.Json.Linq;

namespace Player_Bot
{
    class VerifiedUsers
    {
        SortedList<ulong, SortedSet<string>> members;

        public VerifiedUsers(string path)
        {
            JObject obj;
            if (File.Exists(path))
                obj = JObject.Parse(File.ReadAllText(path));
            else
                obj = new JObject();

            if (!obj.ContainsKey("discord_users"))
                obj["discord_users"] = new JArray();

            members = new SortedList<ulong, SortedSet<string>>();
            foreach (JToken discordUser in (JArray)obj["discord_users"])
            {
                SortedSet<string> pr2Names = new SortedSet<string>();
                foreach (JToken pr2User in (JArray)discordUser["pr2_accounts"])
                    pr2Names.Add((string)pr2User);

                members.Add((ulong)discordUser["id"], pr2Names);
            }
        }

        public void VerifyMember(ulong discordID, string pr2Name)
        {
            if (!members.ContainsKey(discordID))
                members.Add(discordID, new SortedSet<string>());

            members[discordID].Add(pr2Name);
        }
        public void UnverifyMember(ulong discordID, string pr2Name)
        {
            if (members.ContainsKey(discordID))
                members[discordID].Remove(pr2Name);
        }

        public void Save(string path)
        {
            JObject obj = new JObject();
            JArray array = new JArray();
            for (int i = 0; i < members.Count; i++)
            {
                JObject jMember = new JObject();
                jMember["id"] = members.Keys[i];
                jMember["pr2_accounts"] = JArray.FromObject(members.Values[i]);
                array.Add(jMember);
            }
            obj["discord_users"] = array;

            File.WriteAllText(path, obj.ToString());
        }
    }
}
