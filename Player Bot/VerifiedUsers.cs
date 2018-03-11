using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Newtonsoft.Json.Linq;

namespace Player_Bot
{
    class VerifiedUsers
    {
        SortedList<ulong, SortedSet<string>> members;
        public SortedList<ulong, int> pendingVerification;

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
            pendingVerification = new SortedList<ulong, int>();
            foreach (JObject discordUser in (JArray)obj["discord_users"])
            {
                SortedSet<string> pr2Names = new SortedSet<string>();
                if (discordUser.ContainsKey("pr2_accounts"))
                {
                    foreach (JToken pr2User in (JArray)discordUser["pr2_accounts"])
                        pr2Names.Add((string)pr2User);
                }

                members.Add((ulong)discordUser["id"], pr2Names);
                if (discordUser.ContainsKey("verification_code"))
                {
                    pendingVerification.Add((ulong)discordUser["id"], (int)discordUser["verification_code"]);
                }
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
                members[discordID].RemoveWhere((n) => n.ToLower() == pr2Name.ToLower());
        }

        public bool IsMemberVerifiedAs(ulong discordID, string pr2Name)
        {
            return members.ContainsKey(discordID) && members[discordID].Contains(pr2Name);
        }
        public List<string> GetPR2Usernames(ulong discordID)
        {
            if (members.ContainsKey(discordID))
                return members[discordID].ToList();
            else
                return null;
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
            for (int i = 0; i < pendingVerification.Count; i++)
            {
                if (members.ContainsKey(pendingVerification.Keys[i]))
                    array.First((t) => (ulong)t["id"] == pendingVerification.Keys[i])["verification_code"] = pendingVerification.Values[i];
                else
                {
                    JObject jMember = new JObject();
                    jMember["id"] = pendingVerification.Keys[i];
                    jMember["verification_code"] = pendingVerification.Values[i];
                    array.Add(jMember);
                }
            }
            obj["discord_users"] = array;

            File.WriteAllText(path, obj.ToString());
        }
    }
}
