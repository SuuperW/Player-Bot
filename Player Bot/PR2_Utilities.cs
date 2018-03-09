using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

namespace Player_Bot
{
    static class PR2_Utilities
    {
        private static HttpClient httpClient = new HttpClient();

        private static char[] validUsernameChars = new char[] { ' ', '!', '-', '.', ':', ';', '=', '?', '~' };
        public static bool IsUsernameValid(string username)
        {
            if (username == null || username.Length < 1 || username.Length > 20)
            {
                return false;
            }

            for (int i = 0; i < username.Length; i++)
            {
                if (!char.IsLetterOrDigit(username[i]) && !validUsernameChars.Contains(username[i]))
                    return false;
            }

            return true;
        }

        public async static Task<JObject> ViewPlayer(string username)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            values.Add("name", username);
            FormUrlEncodedContent content = new FormUrlEncodedContent(values);
            HttpResponseMessage response = await httpClient.PostAsync("http://pr2hub.com/get_player_info_2.php", content);

            return JObject.Parse(await response.Content.ReadAsStringAsync());
        }

        public async static Task<JObject> GetArtifactHint()
        {
            HttpResponseMessage response = await httpClient.GetAsync("http://pr2hub.com/files/artifact_hint.txt");

            return JObject.Parse(await response.Content.ReadAsStringAsync());
        }
    }
}
