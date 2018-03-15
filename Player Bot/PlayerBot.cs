using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Linq;

using Discord;
using Discord.Rest;
using Discord.WebSocket;

using Newtonsoft.Json.Linq;

namespace Player_Bot
{
    class PlayerBot
    {
        const string secretsPath = "files/secrets.txt";
        const string helpPath = "helpTopics.txt";
        const string outputPath = "files/output.xml";
        const string errorPath = "files/error.txt";
        const string verifiedPath = "files/verifiedUsers.txt";
        const string configPath = "files/config.txt";

        JObject secrets;
        string bot_token;
        string bot_name_discrim;

        string pr2_username;
        string pr2_password;
        string pr2_token;

        BotConfig config;

        int tempFileID = -1;
        private string GetTempFileName()
        {
            tempFileID++;
            return "temp/" + tempFileID;
        }

        DiscordSocketClient socketClient;

        ulong BotID { get => socketClient.CurrentUser.Id; }
        public bool IsConnected { get => socketClient != null && socketClient.ConnectionState >= ConnectionState.Connected; }

        SpecialUsersCollection specialUsers;
        VerifiedUsers verifiedUsers;
        CommandHistory commandHistory = new CommandHistory();

        public PlayerBot(int loggingLevel = -1)
        {
            if (!File.Exists(secretsPath))
                throw new FileNotFoundException("PlayerBot could not find secrets.txt. Please see README for info on how to set this up.");
            if (!File.Exists(helpPath))
                throw new FileNotFoundException("PlayerBot count not find helpTopics.txt. Please don't run a bot without a working help command.");

            secrets = JObject.Parse(File.ReadAllText(secretsPath));
            bot_token = secrets["bot_token"].ToString();
            pr2_username = (string)secrets["pr2_username"];
            pr2_password = (string)secrets["pr2_password"];
            if (secrets.ContainsKey("pr2_token"))
                pr2_token = (string)secrets["pr2_token"];

            config = new BotConfig(configPath);
            PR2_Utilities.HHStarted += ReportNewHH;

            specialUsers = new SpecialUsersCollection("files/specialUsers.txt");
            verifiedUsers = new VerifiedUsers(verifiedPath);

            helpStrings = new SortedDictionary<string, string>();
            JObject helpJson = JObject.Parse(File.ReadAllText(helpPath));
            foreach (KeyValuePair<string, JToken> item in helpJson)
                helpStrings[item.Key] = item.Value.ToString();


            InitializeBotCommandsList();
            CreateHelpTopicsList();

            // Delete any temp files that still exist from last time the bot was run.
            if (Directory.Exists("temp"))
                Directory.Delete("temp", true);
            Directory.CreateDirectory("temp");
        }

        public event Action Connected;
        public event Action Disconnected;
        public async Task ConnectAndStart()
        {
#pragma warning disable CS4014
            AppendToLog("<begin_login time='" + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString() + "'></begin_login>\n", 2);
#pragma warning restore CS4014

            socketClient = await ConnectSocketClient();

            socketClient.MessageReceived += SocketClient_MessageReceived;

            await socketClient.SetGameAsync("PR2");
        }

        public async Task Disconnect()
        {
#pragma warning disable CS4014
            AppendToLog("<disconnect time='" + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString() + "'></disconnect>\n", 2);
#pragma warning restore CS4014

            await socketClient.GetUser(specialUsers.Owner).SendMessageAsync("I'm diconnecting now.");

            await socketClient.SetStatusAsync(UserStatus.Invisible);
            // Wait, just to verify that the status update has time to go through.
            await socketClient.GetDMChannelAsync(socketClient.CurrentUser.Id);

            await socketClient.StopAsync();
        }

        async Task<DiscordRestClient> ConnectRestClient()
        {
            DiscordRestClient ret = new DiscordRestClient(new DiscordRestConfig());
            await ret.LoginAsync(TokenType.Bot, bot_token);

            return ret;
        }
        async Task<DiscordSocketClient> ConnectSocketClient()
        {
            DiscordSocketClient client = new DiscordSocketClient();
            client.Ready += () =>
            {
                bot_name_discrim = socketClient.CurrentUser.Username + "#" + socketClient.CurrentUser.Discriminator;
                Connected?.Invoke();
                AppendToLog("<ready time='" + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString() + "'></ready>\n", 2);
                if (config.hhChannels.Count != 0)
                    PR2_Utilities.BeginWatchingHH();
                return null;
            };
            client.Disconnected += (e) => { Disconnected?.Invoke(); return null; };

            await client.LoginAsync(TokenType.Bot, bot_token);
            await client.StartAsync();

            return client;
        }

        async Task<IUserMessage> SendMessage(IMessageChannel channel, string text)
        {
            Task logTask = AppendToLog("<send_message time='" + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString() +
              "' channel='" + channel.Name + "'>\n" + text + "\n</send_message>\n");
            Task<IUserMessage> ret = channel.SendMessageAsync(text);

            await logTask;
            return await ret;
        }
        async Task<IUserMessage> SendFile(IMessageChannel channel, Stream fileStream, string fileName, string text = null)
        {
            Task logTask = AppendToLog("<send_file time='" + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString() +
              "' channel='" + channel.Name + "' file=' " + fileName + "'>\n" + text + "\n</send_file>\n");
            Task<IUserMessage> ret = channel.SendFileAsync(fileStream, fileName, text);

            await logTask;
            return await ret;
        }
        async Task<IUserMessage> SendFile(IMessageChannel channel, string fileName, string text = null)
        {
            Task logTask = AppendToLog("<send_file time='" + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString() +
              "' channel='" + channel.Name + "' file=' " + fileName + "'>\n" + text + "\n</send_file>\n");
            Task<IUserMessage> ret = channel.SendFileAsync(fileName, text);

            await logTask;
            return await ret;
        }
        async Task EditMessage(IUserMessage message, string text)
        {
            Task logTask = AppendToLog("<edit_message time='" + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString() +
              "' channel='" + message.Channel.Name + "'>\n" + text + "\n</edit_message>\n");
            Task ret = message.ModifyAsync((p) => p.Content = text);

            await logTask;
            await ret;
        }
        Task AppendToLog(string text, int priority = 1)
        {
            if (priority >= config.loggingLevel)
            {
                if (config.loggingChannel != 0 && IsConnected)
                    (socketClient.GetChannel(config.loggingChannel) as IMessageChannel).SendMessageAsync(text);
                return File.AppendAllTextAsync(outputPath, text);
            }
            else
                return Task.CompletedTask;
        }
        Task LogError(Exception ex)
        {
            StringBuilder errorStr = new StringBuilder();
            while (ex != null)
            {
                errorStr.Append(ex.GetType().ToString());
                errorStr.Append("\n");
                errorStr.Append(ex.Message);
                errorStr.Append("\n\n");
                errorStr.Append(ex.StackTrace);
                errorStr.Append("\n\n\n");
                ex = ex.InnerException;
            }
            errorStr.Length -= 3;

            File.WriteAllText(errorPath, errorStr.ToString());
            return AppendToLog("<error time='" + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString() +
              "'>\n" + errorStr.ToString() + "\n</receive_command>\n");
        }

        private void SaveSecrets()
        {
            secrets["pr2_token"] = pr2_token;
            File.WriteAllText(secretsPath, secrets.ToString());
        }

        private async Task SocketClient_MessageReceived(SocketMessage msg)
        {
            try
            {
                if (msg.Author.Id != BotID)
                    await HandleMessage(msg);
            }
            catch (Exception ex)
            {
                await LogError(ex);
            }
        }
        private async Task HandleMessage(SocketMessage msg)
        {
            if (specialUsers.IsUserBanned(msg.Author.Id))
            {
                if (commandHistory.TimeSinceLastCommand(msg.Author.Id) > 30)
                {
                    await bannedCommand.Delegate(msg, null);
                    commandHistory.AddCommand(bannedCommand.Name, msg.Author.Id);
                }
                return;
            }

            try
            {
                BotCommand command = MessageToCommand(msg, out string[] args);
                if (command != null)
                {
                    await AppendToLog("<receive_command time='" + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString() +
                      "' channel='" + msg.Channel.Name + "'>\n" + msg.Content + "\n</receive_command>\n");
                    if (msg.Author.Id != specialUsers.Owner)
                    {
                        string waitMessage = commandHistory.GetWaitMessage(command, msg.Author.Id);
                        if (waitMessage != null)
                            command = new BotCommand(async (m, a) => { await SendMessage(msg.Channel, GetUsername(msg.Author) + waitMessage); return true; });
                    }

                    if (await command.Delegate(msg, args))
                        commandHistory.AddCommand(command.Name, msg.Author.Id);
                }
            }
            catch (Discord.Net.HttpException ex)
            {
                if (ex.Message.Contains("50007")) // can't send messages to this user
                {
                    if (!msg.Author.IsBot)
                    {
                        await SendMessage(msg.Channel, GetUsername(msg.Author) + ", I attempted to send you a DM " +
                          "but was unable to. Please ensure that you can receive DMs from me.");
                    }
                }
                else if (ex.Message == "The server responded with error 403: Forbidden")
                {
                    await SendMessage(msg.Channel, GetUsername(msg.Author) + ", it seems I don't have the necessary permissions to do that.");
                }
            }
            catch (Exception ex)
            {
                await LogError(ex);

                await SendFile(await socketClient.GetUser(specialUsers.Owner).GetOrCreateDMChannelAsync(), errorPath, "I've encountered an error.");
                await SendMessage(msg.Channel, GetUsername(msg.Author) +
                    ", I have encountered an error and don't know what to do with it. :(\n" +
                    "Error details have been sent to my owner.");
            }
        }

        private BotCommand MessageToCommand(SocketMessage msg, out string[] args)
        {
            BotCommand command = null;
            args = null;

            if (IsMessageProperCommand(msg))
            {
                args = ParseCommand(msg.Content);
                string commandStr = args[0].ToLower();

                if (everybodyBotCommands.ContainsKey(commandStr))
                    command = everybodyBotCommands[commandStr];
                else if (specialUsers.Owner == msg.Author.Id && ownerBotCommands.ContainsKey(commandStr))
                    command = ownerBotCommands[commandStr];
                else
                {
                    SocketGuildUser user = msg.Author as SocketGuildUser;
                    GuildConfigInfo guildConfig = user == null ? null : config.guilds[user.Guild.Id];
                    if (user != null && guildConfig != null)
                    {
                        if (trustedBotCommands.ContainsKey(commandStr) && (user.Guild.OwnerId == user.Id || user.Roles.Any((r) => r.Id == guildConfig.trustedRole)))
                            command = trustedBotCommands[commandStr];
                        else if (guildOwnerBotCommands.ContainsKey(commandStr) && user.Guild.OwnerId == user.Id)
                            command = guildOwnerBotCommands[commandStr];
                    }
                }

                if (command == null)
                {
                    command = everybodyBotCommands["help"];
                    args = new string[] { "help", "_invalid_command" };
                }
            }
            // If the bot is mentioned, send a help message.
            else if (msg.MentionedUsers.FirstOrDefault((u) => u.Id == socketClient.CurrentUser.Id) != null)
            {
                command = everybodyBotCommands["help"];
                args = new string[] { "help", "_hello" };
            }

            return command;
        }
        private bool IsMessageProperCommand(SocketMessage msg)
        {
            // a / followed by a non-whitespace char
            return msg.Content.Length > 1 && msg.Content.StartsWith('/') && !char.IsWhiteSpace(msg.Content[1]);
        }
        private string[] ParseCommand(string msg)
        {
            int index = 1;
            List<string> list = new List<string>();

            do
            {
                while (msg.Length > index && char.IsWhiteSpace(msg[index]))
                    index++;

                int quote = msg.IndexOf('"', index);
                int space = index;
                while (msg.Length > space && !char.IsWhiteSpace(msg[space]))
                    space++;

                int newIndex = 0;
                if (quote != -1 && (quote < space || space == -1))
                {
                    newIndex = msg.IndexOf('"', quote + 1);
                    if (newIndex == -1)
                        newIndex = msg.Length;
                    list.Add(msg.Substring(quote + 1, newIndex - quote - 1));
                    newIndex++;
                }
                else
                {
                    if (space == -1)
                        newIndex = msg.Length;
                    else
                        newIndex = space;
                    list.Add(msg.Substring(index, newIndex - index));
                }

                index = newIndex;
            } while (index < msg.Length);

            return list.ToArray();
        }
        private string CombineLastArgs(string[] args, int lastIndex)
        {
            return String.Join(' ', args, lastIndex, args.Length - lastIndex);
        }

        private string GetUsername(SocketUser user)
        {
            SocketGuildUser guildUser = user as SocketGuildUser;
            if (guildUser != null && guildUser.Nickname != null)
                return guildUser.Nickname;
            else
                return user.Username;
        }

        #region "Bot Commands"
        private SortedList<string, BotCommand> everybodyBotCommands;
        private SortedList<string, BotCommand> trustedBotCommands;
        private SortedList<string, BotCommand> guildOwnerBotCommands;
        private SortedList<string, BotCommand> ownerBotCommands;
        private BotCommand bannedCommand;

        private void InitializeBotCommandsList()
        {
            everybodyBotCommands = new SortedList<string, BotCommand>
            {
                { "help", new BotCommand(SendHelpMessage, 1) },
                { "commands", new BotCommand(SendCommandsList, 1) },
                { "view", new BotCommand(ViewUserInfo) },
                { "hint", new BotCommand(GetArtifactHint) },
                { "role", new BotCommand(ToggleRole) },
                { "verify", new BotCommand(VerifySelf) },
                { "verified", new BotCommand(GetPR2Usernames, 1) },
                { "hh", new BotCommand(GetHHStatus) },
                { "exp", new BotCommand(GetExpRequired) },
                { "roles", new BotCommand(GetAvailableRoles) }
           };

            trustedBotCommands = new SortedList<string, BotCommand>
            {
                { "toggle_public_role", new BotCommand(TogglePublicRole, -1) },
                { "toggle_guild_role", new BotCommand(ToggleGuildRole, -1) },
                { "verify_member", new BotCommand(VerifyMember, -1) },
                { "unverify_member", new BotCommand(UnverifyMember, -1) },
                { "toggle_hh_reporting", new BotCommand(ToggleHHReporting, -1) },
                { "set_hh_role", new BotCommand(SetHHRole, -1) },
                { "set_verified_role", new BotCommand(SetVerifiedRole, -1) },
                { "todo", new BotCommand(GetConfigTodo, 10) },
                { "clean_config", new BotCommand(CleanConfig, 10) }
            };

            guildOwnerBotCommands = new SortedList<string, BotCommand>
            {
                { "set_trusted_role", new BotCommand(SetTrustedRole, -1) }
            };

            ownerBotCommands = new SortedList<string, BotCommand>
            {
                { "gtfo", new BotCommand(GTFO) },
                { "ban_user", new BotCommand(BanUser) },
                { "unban_user", new BotCommand(UnbanUser) },
                { "get_log", new BotCommand(GetLog) },
                { "get_error", new BotCommand(GetError) },
                { "log_here", new BotCommand(LogToChannel) },
                { "set_logging_level", new BotCommand(SetLoggingLevel) },
                { "clear_log", new BotCommand(ClearLog) }
            };

            bannedCommand = new BotCommand(SendBannedMessage);
        }
        private void CreateHelpTopicsList()
        {
            helpStrings.Add("topics", "");
            StringBuilder str = new StringBuilder("Here is the list of available help topics:```");
            foreach (string topic in helpStrings.Keys)
            {
                if (!topic.StartsWith('_'))
                    str.Append("\n" + topic);
            }
            str.Append("```");

            helpStrings["topics"] = str.ToString();
        }

        #region "help"
        private SortedDictionary<string, string> helpStrings;
        private async Task<bool> SendHelpMessage(SocketMessage msg, params string[] args)
        {
            string helpTopic = args.Length > 1 ? args[1] : "_default";

            if (helpStrings.ContainsKey(helpTopic))
            {
                await SendMessage(msg.Channel, helpStrings[helpTopic].Replace("@me", "@" + bot_name_discrim));
            }
            else
            {
                await SendMessage(msg.Channel, "I could not find the help topic you gave me. To see a list of available help topics, use the command `help topics`.");
                return false;
            }

            return true;
        }
        private async Task<bool> SendCommandsList(SocketMessage msg, params string[] args)
        {
            StringBuilder availableCommands = new StringBuilder();
            foreach (KeyValuePair<string, BotCommand> kvp in everybodyBotCommands)
                availableCommands.Append("\n" + kvp.Key); // \n first because first line tells Discord how to format

            SocketGuildUser user = msg.Author as SocketGuildUser;
            GuildConfigInfo guildConfig = user == null ? null : config.guilds[user.Guild.Id];
            if (user != null && guildConfig != null)
            {
                if (user.Guild.OwnerId == user.Id || user.Roles.Any((r) => r.Id == guildConfig.trustedRole))
                {
                    availableCommands.Append("\n\n----- Trusted User Commands -----");
                    foreach (KeyValuePair<string, BotCommand> kvp in trustedBotCommands)
                        availableCommands.Append("\n" + kvp.Key);
                }
                if (user.Guild.OwnerId == user.Id)
                {
                    availableCommands.Append("\n\n----- Server Owner Commands -----");
                    foreach (KeyValuePair<string, BotCommand> kvp in guildOwnerBotCommands)
                        availableCommands.Append("\n" + kvp.Key);
                }
            }

            if (specialUsers.Owner == msg.Author.Id)
            {
                availableCommands.Append("\n\n----- Owner Commands -----");
                foreach (KeyValuePair<string, BotCommand> kvp in ownerBotCommands)
                    availableCommands.Append("\n" + kvp.Key);
            }

            if (user != null) // in a server
                await SendMessage(msg.Channel, GetUsername(msg.Author) + ", I will send you a DM.");
            await SendMessage(await msg.Author.GetOrCreateDMChannelAsync(), "Here are the commands you can use: ```" + availableCommands + "```");
            return true;
        }
        #endregion

        #region "info gets"
        private async Task<bool> ViewUserInfo(SocketMessage msg, params string[] args)
        {
            if (args.Length < 2)
            {
                await SendMessage(msg.Channel, "Who are you trying to look at, " + GetUsername(msg.Author) + "?");
                return false;
            }
            args[1] = CombineLastArgs(args, 1);

            if (!PR2_Utilities.IsUsernameValid(args[1]))
            {
                await SendMessage(msg.Channel, "The username `" + args[1].Replace("`", "\\`") + "` is invalid.");
                return false;
            }

            JObject viewData = await PR2_Utilities.ViewPlayer(args[1]);
            if (viewData.ContainsKey("error"))
            {
                await SendMessage(msg.Channel, "pr2hub returned an error when attempting to get player info for `" + args[1] + "`: `" + viewData["error"] + "`");
            }
            else
            {
                int group = (int)viewData["group"];
                string groupStr = (group >= 0 && group < PR2_Utilities.groups.Length) ? PR2_Utilities.groups[group] : group.ToString();
                string guildStr = (string)viewData["guildName"] == "" ? "" : "\nGuild: " + viewData["guildName"];
                string registerStr = ((DateTime)viewData["registerDate"]).Year < 1990 ? "Age of Heroes" : (string)viewData["registerDate"];
                string lastLoginStr = (string)viewData["status"] == "offline" ? "Last login: " + viewData["loginDate"] : (string)viewData["status"];

                await SendMessage(msg.Channel, "Info for " + args[1] + " (id: " + viewData["userId"] + "):```\nGroup: "
                    + groupStr + "\nRank: " + viewData["rank"] + "\nHats: " + viewData["hats"] + guildStr
                    + "\nRegister Date: " + registerStr + "\n\n" + lastLoginStr + "```");
            }

            return true;
        }
        private async Task<bool> GetArtifactHint(SocketMessage msg, params string[] args)
        {
            JObject hint = await PR2_Utilities.GetArtifactHint();
            string message = "Fred remembers this much: `" + hint["hint"].ToString().Replace("`", "\\`")
                + "`\nThe first person to find this artifact was `" + hint["finder_name"].ToString().Replace("`", "\\`") + "`.";

            await SendMessage(msg.Channel, message);
            return true;
        }
        private async Task<bool> GetHHStatus(SocketMessage msg, params string[] args)
        {
            JObject servers = await PR2_Utilities.GetServers();
            IEnumerable<string> happyServers = servers["servers"].Where((t) => (int)t["happy_hour"] == 1)
                .Select((t) => (string)t["server_name"]);

            if (happyServers.Count() == 0)
            {
                await SendMessage(msg.Channel, "None of the servers are happy right now. So sad.");
            }
            else
            {
                StringBuilder listStr = new StringBuilder("```");
                foreach (string serverName in happyServers)
                    listStr.Append("\n" + serverName);
                listStr.Append("```");

                await SendMessage(msg.Channel, "The following servers are currently happy:" + listStr);
            }

            return true;
        }
        private async Task<bool> GetExpRequired(SocketMessage msg, params string[] args)
        {
            int from = 0;
            int to = 1;
            if (args.Length > 1)
            {
                if (!int.TryParse(args[1], out from))
                {
                    await SendMessage(msg.Channel, GetUsername(msg.Author) + ", I could not parse '" + args[1] + "'.");
                    return true;
                }
                if (args.Length > 2)
                {
                    if (!int.TryParse(args[2], out to))
                    {
                        await SendMessage(msg.Channel, GetUsername(msg.Author) + ", I could not parse '" + args[1] + "'.");
                        return true;
                    }
                }
                else
                    to = from + 1;
            }

            if (to <= from)
            {
                await SendMessage(msg.Channel, GetUsername(msg.Author) + ", you cannot rank down, unfortunately.");
                return true;
            }
            else if (to > 82)
            {
                await SendMessage(msg.Channel, GetUsername(msg.Author) + ", PR2 will fail to properly handle exp values that large, so it is impossible to rank up past 82 by gaining exp.");
                return true;
            }
            else if (from < 0)
            {
                await SendMessage(msg.Channel, GetUsername(msg.Author) + ", how do you plan on reaching a negative rank to begin with?");
                return true;
            }

            long exp = PR2_Utilities.ExpFromRankTo(from, to);
            await SendMessage(msg.Channel, "Going from rank " + from + " to " + to + " requires " + exp + " exp.");
            return true;
        }

        private async Task<bool> GetConfigTodo(SocketMessage msg, params string[] args)
        {
            if (!(msg.Channel is SocketGuildChannel))
            {
                await SendMessage(msg.Channel, "There's nothing to set up outside of servers.");
                return false;
            }

            SocketGuild guild = (msg.Channel as SocketGuildChannel).Guild;
            StringBuilder message = new StringBuilder();
            if (!guild.Channels.Any((c) => config.hhChannels.Contains(c.Id)))
                message.Append("\nThe server does not have a channel set up for HH reporting. To set a channel, use `/toggle_hh_reporting`.");

            GuildConfigInfo guildConfig = config.guilds.ContainsKey(guild.Id) ? config.guilds[guild.Id] : new GuildConfigInfo();
            if (guildConfig.verifiedRole == 0)
                message.Append("\nThe server does not have a 'verified member' role. To set one, use `/set_verified_role`.");
            if (guildConfig.hhRole == 0)
                message.Append("\nThe server does not have a 'hh' role. To set one, use `/set_hh_role`.");
            if (guildConfig.trustedRole == 0)
                message.Append("\nThe server does not have a 'trusted' role. To set one, use `/set_trusted_role`.");

            string str;
            if (message.Length == 0)
                str = "There is nothing left to do to set up the server.";
            else
                str = "You may want to fix the following things (all suggested commands should be done in the server, not here):" + message.ToString();

            await SendMessage(msg.Channel, GetUsername(msg.Author) + ", I will send you a DM.");
            await SendMessage(await msg.Author.GetOrCreateDMChannelAsync(), str);
            return true;
        }
        private async Task<bool> GetAvailableRoles(SocketMessage msg, params string[] args)
        {
            if (!(msg.Channel is SocketGuildChannel))
            {
                await SendMessage(msg.Channel, GetUsername(msg.Author) + ", you are not in a guild; roles don't exist here.");
                return false;
            }

            SocketGuildUser user = msg.Author as SocketGuildUser;
            SocketGuild guild = (msg.Channel as SocketGuildChannel).Guild;
            StringBuilder message = new StringBuilder("The following roles are available to everybody in this server: ");
            foreach (ulong roleID in config.guilds[guild.Id].publicRoles)
            {
                SocketRole role = guild.Roles.FirstOrDefault((r) => r.Id == roleID);
                if (role != null)
                    message.Append(role.Name + ", ");
            }
            if (message[message.Length - 2] == ':')
                message.Append("no roles available");
            else
                message.Length -= 2;

            message.Append("\nThe following roles are available to guild members: ");
            foreach (ulong roleID in config.guilds[guild.Id].pr2GuildRoles)
            {
                SocketRole role = guild.Roles.FirstOrDefault((r) => r.Id == roleID);
                if (role != null)
                    message.Append(role.Name + ", ");
            }
            if (message[message.Length - 2] == ':')
                message.Append("no roles available");
            else
                message.Length -= 2;

            await SendMessage(msg.Channel, message.ToString());
            return true;
        }
        #endregion

        #region "roles"
        private async Task<SocketRole> GetRoleFromArgs(SocketMessage msg, params string[] args)
        {
            if (!(msg.Channel is SocketGuildChannel))
            {
                await SendMessage(msg.Channel, GetUsername(msg.Author) + ", you are not in a guild; roles don't exist here.");
                return null;
            }
            if (args.Length < 2)
            {
                await SendMessage(msg.Channel, GetUsername(msg.Author) + ", you must specify a role name to use this command.");
                return null;
            }

            SocketGuild guild = (msg.Channel as SocketGuildChannel).Guild;
            SocketRole role = guild.Roles.FirstOrDefault((r) => r.Name.ToLower() == args[1].ToLower());
            if (role == null)
            {
                await SendMessage(msg.Channel, "The role `" + args[1] + "` does not exist. (If the role you are trying to use contains spaces, surround the name with quotation marks. E.g., `\"Role Name\"`");
            }

            return role;

        }
        private async Task<bool> TogglePublicRole(SocketMessage msg, params string[] args)
        {
            SocketRole roleToAdd = await GetRoleFromArgs(msg, args);
            if (roleToAdd == null)
                return false;

            SocketGuild guild = (msg.Channel as SocketGuildChannel).Guild;
            if (config.guilds[guild.Id].publicRoles.Contains(roleToAdd.Id))
            {
                config.guilds[guild.Id].publicRoles.Remove(roleToAdd.Id);
                await SendMessage(msg.Channel, "The role `" + roleToAdd.Name + "` is no longer public.");
            }
            else
            {
                config.guilds[guild.Id].publicRoles.Add(roleToAdd.Id);
                await SendMessage(msg.Channel, "The role `" + roleToAdd.Name + "` has been made public.");
            }
            config.Save(configPath);

            return true;
        }
        private async Task<bool> ToggleRole(SocketMessage msg, params string[] args)
        {
            SocketRole role = await GetRoleFromArgs(msg, args);
            if (role == null)
                return true;

            SocketGuildUser user = msg.Author as SocketGuildUser; // should be safe; GetRoleFromArgs verifies that this is a guild
            SocketGuild guild = (msg.Channel as SocketGuildChannel).Guild;
            if (user.Roles.Contains(role))
            {
                if (config.guilds[guild.Id].publicRoles.Contains(role.Id) || config.guilds[guild.Id].pr2GuildRoles.Contains(role.Id))
                {
                    await user.RemoveRoleAsync(role);
                    await SendMessage(msg.Channel, GetUsername(msg.Author) + ", you have been removed from the `" + role.Name + "` role.");
                }
                else
                    await SendMessage(msg.Channel, GetUsername(msg.Author) + ", you may not remove the `" + role.Name + "` role.");
            }
            else if (config.guilds[guild.Id].publicRoles.Contains(role.Id)) // add public role
            {
                await user.AddRoleAsync(role);
                await SendMessage(msg.Channel, GetUsername(msg.Author) + ", you have been given the `" + role.Name + "` role.");
            }
            else if (config.guilds[guild.Id].pr2GuildRoles.Contains(role.Id)) // add guild role?
            {
                List<string> accounts = verifiedUsers.GetPR2Usernames(user.Id);
                string pr2Name;
                if (args.Length < 3)
                    pr2Name = accounts.FirstOrDefault();
                else
                {
                    pr2Name = CombineLastArgs(args, 2);
                    if (!accounts.Contains(pr2Name))
                    {
                        await SendMessage(msg.Channel, GetUsername(msg.Author) + ", you have not verified the PR2 account `" + pr2Name + "`.");
                        return true;
                    }
                }
                if (pr2Name == null)
                {
                    await SendMessage(msg.Channel, GetUsername(msg.Author) + ", you have not verified any PR2 accounts.");
                    return true;
                }

                JObject playerInfo = await PR2_Utilities.ViewPlayer(pr2Name);
                if ((string)playerInfo["guildName"] == role.Name)
                {
                    await user.AddRoleAsync(role);
                    await SendMessage(msg.Channel, GetUsername(msg.Author) + ", you have been given the `" + role.Name + "` role.");
                }
                else if (accounts.Count > 1)
                {
                    await SendMessage(msg.Channel, GetUsername(msg.Author) + ", your account `" + pr2Name + "` is not a member of the `"
                        + role.Name + "` guild. If another of your verified accounts is, specify that account name after the guild name.");
                }
                else
                    await SendMessage(msg.Channel, GetUsername(msg.Author) + ", you are not a member of the `" + role.Name + "` guild.");

            }
            else
            {
                await SendMessage(msg.Channel, GetUsername(msg.Author) + ", the role `" + role.Name + "` is not available.");
                return false;
            }

            return true;
        }

        private async Task<bool> ToggleGuildRole(SocketMessage msg, params string[] args)
        {
            SocketRole roleToAdd = await GetRoleFromArgs(msg, args);
            if (roleToAdd == null)
                return false;

            SocketGuild guild = (msg.Channel as SocketGuildChannel).Guild;
            if (config.guilds[guild.Id].pr2GuildRoles.Contains(roleToAdd.Id))
            {
                config.guilds[guild.Id].pr2GuildRoles.Remove(roleToAdd.Id);
                await SendMessage(msg.Channel, "The role `" + roleToAdd.Name + "` is no longer a guild role.");
            }
            else
            {
                config.guilds[guild.Id].pr2GuildRoles.Add(roleToAdd.Id);
                await SendMessage(msg.Channel, "The role `" + roleToAdd.Name + "` has been made a guild role.");
            }
            config.Save(configPath);

            return true;
        }

        private async Task<bool> SetHHRole(SocketMessage msg, params string[] args)
        {
            args[1] = CombineLastArgs(args, 1);
            SocketRole role = await GetRoleFromArgs(msg, args);
            if (role == null)
                return true;

            ulong guildID = (msg.Channel as SocketGuildChannel).Guild.Id;
            if (!config.guilds.ContainsKey(guildID))
                config.guilds.Add(guildID, new GuildConfigInfo());
            GuildConfigInfo guildConfig = config.guilds[guildID];
            guildConfig.hhRole = role.Id;
            config.Save(configPath);

            await SendMessage(msg.Channel, "The HH role for this server has been set.");
            return true;
        }
        private async Task<bool> SetVerifiedRole(SocketMessage msg, params string[] args)
        {
            args[1] = CombineLastArgs(args, 1);
            SocketRole role = await GetRoleFromArgs(msg, args);
            if (role == null)
                return true;

            ulong guildID = (msg.Channel as SocketGuildChannel).Guild.Id;
            if (!config.guilds.ContainsKey(guildID))
                config.guilds.Add(guildID, new GuildConfigInfo());
            GuildConfigInfo guildConfig = config.guilds[guildID];
            guildConfig.verifiedRole = role.Id;
            config.Save(configPath);

            await SendMessage(msg.Channel, "The verified member role for this server has been set.");
            return true;
        }
        private async Task<bool> SetTrustedRole(SocketMessage msg, params string[] args)
        {
            args[1] = CombineLastArgs(args, 1);
            SocketRole role = await GetRoleFromArgs(msg, args);
            if (role == null)
                return true;

            ulong guildID = (msg.Channel as SocketGuildChannel).Guild.Id;
            if (!config.guilds.ContainsKey(guildID))
                config.guilds.Add(guildID, new GuildConfigInfo());
            GuildConfigInfo guildConfig = config.guilds[guildID];
            guildConfig.trustedRole = role.Id;
            config.Save(configPath);

            await SendMessage(msg.Channel, "The trusted role for this server has been set.");
            return true;
        }
        #endregion

        #region "verification"
        private async Task<bool> VerifyMember(SocketMessage msg, params string[] args)
        {
            if (!(msg.Channel is SocketGuildChannel))
            {
                await SendMessage(msg.Channel, "You must do this in the server, so that my stupid bot brain will understand how to grant the Verified Member role.");
                return false;
            }

            if (args.Length < 3)
            {
                await SendMessage(msg.Channel, GetUsername(msg.Author) + ", the format for this cmmand is `/verify @discordUser pr2_username`.");
                return false;
            }
            args[2] = CombineLastArgs(args, 2);

            if (!PR2_Utilities.IsUsernameValid(args[2]))
            {
                await SendMessage(msg.Channel, "The username `" + args[2].Replace("`", "\\`") + "` is invalid.");
                return false;
            }

            SocketUser user = msg.MentionedUsers.FirstOrDefault();
            if (user == null)
            {
                await SendMessage(msg.Channel, GetUsername(msg.Author) + ", you must mention the Discord member you are verifying.");
                return false;
            }

            await VerifyMember(user as SocketGuildUser, args[2], (msg.Channel as SocketGuildChannel).Guild);
            await SendMessage(msg.Channel, "Discord user " + user.Username + "#" + user.Discriminator + " verified as PR2 user " + args[2] + ".");
            return true;
        }
        private async Task<bool> UnverifyMember(SocketMessage msg, params string[] args)
        {
            if (!(msg.Channel is SocketGuildChannel))
            {
                await SendMessage(msg.Channel, "You must do this in the server, so that my stupid bot brain will understand how to remove the Verified Member role.");
                return false;
            }

            if (args.Length < 3)
            {
                await SendMessage(msg.Channel, GetUsername(msg.Author) + ", the format for this cmmand is `/unverify @discordUser pr2_username`.");
                return false;
            }
            args[2] = CombineLastArgs(args, 2);

            SocketUser user = msg.MentionedUsers.FirstOrDefault();
            if (user == null)
            {
                await SendMessage(msg.Channel, GetUsername(msg.Author) + ", you must mention the Discord member you are un-verifying.");
                return false;
            }

            await UnverifyMember(user as SocketGuildUser, args[2], (msg.Channel as SocketGuildChannel).Guild);
            await SendMessage(msg.Channel, "Discord user " + user.Username + "#" + user.Discriminator + " un-verified as PR2 user " + args[2] + ".");
            return true;
        }
        private async Task<bool> VerifySelf(SocketMessage msg, params string[] args)
        {
            if (args.Length == 1 || !verifiedUsers.pendingVerification.ContainsKey(msg.Author.Id)) // initialize process
            {
                Random r = new Random(Environment.TickCount);
                int verificationCode = r.Next(100000000, int.MaxValue);
                await SendMessage(await msg.Author.GetOrCreateDMChannelAsync(), "To verify your PR2 account, send a PM to `Player Bot` saying `"
                    + verificationCode + "` and nothing else. Then use the command `/verify username` where 'username' is replaced with your PR2 username."
                    + "\nThis verification code is only good for one use. Any previous codes I gave you are now invalid.");
                await SendMessage(msg.Channel, GetUsername(msg.Author) + ", please check your DMs.");

                lock (verifiedUsers.pendingVerification)
                {
                    if (verifiedUsers.pendingVerification.ContainsKey(msg.Author.Id))
                        verifiedUsers.pendingVerification[msg.Author.Id] = verificationCode;
                    else
                        verifiedUsers.pendingVerification.Add(msg.Author.Id, verificationCode);
                }
                verifiedUsers.Save(verifiedPath);
            }
            else // finish process
            {
                if (!(msg.Channel is SocketGuildChannel))
                {
                    await SendMessage(msg.Channel, "You must do this in the server, so that my stupid bot brain will understand how to grant the Verified Member role.");
                    return false;
                }

                args[1] = CombineLastArgs(args, 1);

                JObject messages = await PR2_Utilities.GetPrivateMessages(pr2_token);
                if (PR2_Utilities.IsResponseNotLoggedInError(messages))
                {
                    pr2_token = await PR2_Utilities.ObtainLoginToken(pr2_username, pr2_password);
                    SaveSecrets();
                    messages = await PR2_Utilities.GetPrivateMessages(pr2_token);
                }

                if (messages.ContainsKey("error") || (bool)messages["success"] == false)
                {
                    await SendMessage(msg.Channel, "Error: could not retreive PMs.");
                    return false;
                }

                JToken theMessage = messages["messages"].FirstOrDefault((t) => ((string)t["name"]).ToLower() == args[1].ToLower());
                if (theMessage == null)
                {
                    await SendMessage(msg.Channel, GetUsername(msg.Author) + ", I do not have a PM from you. Make sure you have sent the required PM to the PR2 user `"
                        + pr2_username + "`, and that the PM includes only the verification code.");
                }
                else
                {
                    bool verify;
                    lock (verifiedUsers.pendingVerification)
                    {
                        verify = (string)theMessage["message"] == verifiedUsers.pendingVerification[msg.Author.Id].ToString();
                        if (verify)
                            verifiedUsers.pendingVerification.Remove(msg.Author.Id);
                    }

                    if (verify)
                    {
                        args[1] = (string)theMessage["name"]; // get PR2's official casing
                        await VerifyMember(msg.Author as SocketGuildUser, args[1], (msg.Channel as SocketGuildChannel).Guild);

                        await SendMessage(msg.Channel, GetUsername(msg.Author) + ", you have been verified as PR2 user `" + args[1] + "`.");

                    }
                    else
                        await SendMessage(msg.Channel, GetUsername(msg.Author) + ", I have a PM from you, but it's contents do not exactly match the verificatin code I gave you.");
                }
            }

            return true;
        }
        private async Task VerifyMember(SocketGuildUser user, string pr2Name, SocketGuild guild)
        {
            if (!verifiedUsers.IsMemberVerifiedAs(user.Id, pr2Name))
            {
                verifiedUsers.VerifyMember(user.Id, pr2Name);
                verifiedUsers.Save(verifiedPath);
                // logging
                IMessageChannel channel = guild.Channels.FirstOrDefault((c) => c.Name == "verified-members" && c is IMessageChannel) as IMessageChannel;
                if (channel != null)
                    await SendMessage(channel, user.Mention + " - " + pr2Name);
            }

            // server role
            SocketRole role = guild.Roles.FirstOrDefault((r) => r.Name == "Verified Member");
            if (role != null && !user.Roles.Any((r) => r.Id == role.Id))
                await user.AddRoleAsync(role);
        }
        private async Task UnverifyMember(SocketGuildUser user, string pr2Name, SocketGuild guild)
        {
            verifiedUsers.UnverifyMember(user.Id, pr2Name);
            verifiedUsers.Save(verifiedPath);

            // server role
            SocketRole role = guild.Roles.FirstOrDefault((r) => r.Name == "Verified Member");
            if (role != null)
                await user.RemoveRoleAsync(role);

            // logging
            IMessageChannel channel = guild.Channels.FirstOrDefault((c) => c.Name == "verified-members" && c is IMessageChannel) as IMessageChannel;
            if (channel != null)
            {
                IList<IMessage> messages = (await channel.GetMessagesAsync(999).Flatten()).ToList();
                IMessage theMesage = messages.FirstOrDefault((m) => m.Content.ToLower() == user.Mention + " - " + pr2Name.ToLower());
                if (theMesage != null)
                    await theMesage.DeleteAsync();
            }
        }

        private async Task<bool> GetPR2Usernames(SocketMessage msg, params string[] args)
        {
            ulong id = 0;
            if (args.Length == 1) // self
                id = msg.Author.Id;
            else if (!ulong.TryParse(args[1], out id)) // other Discord user
                await SendMessage(msg.Channel, GetUsername(msg.Author) + ", to get the PR2 usernames for another Discord user, provide their Discord user ID.");

            if (id != 0)
            {
                SocketUser user = socketClient.GetUser(id);
                List<string> names = verifiedUsers.GetPR2Usernames(id);
                if (names != null)
                {
                    StringBuilder nameListStr = new StringBuilder("```");
                    for (int i = 0; i < names.Count; i++)
                        nameListStr.Append("\n" + names[i]);
                    nameListStr.Append("```");

                    await SendMessage(msg.Channel, user.Username + "#" + user.Discriminator + " has verified the following PR2 accounts:" + nameListStr);
                }
                else if (user != null)
                    await SendMessage(msg.Channel, user.Username + "#" + user.Discriminator + " has not verified any PR2 accounts.");
                else
                    await SendMessage(msg.Channel, "Discord user `" + id + "` has not verified any PR2 accounts.");
            }

            return true;
        }
        #endregion

        #region "misc"
        private async Task<bool> ToggleHHReporting(SocketMessage msg, params string[] args)
        {
            ulong channelID = msg.Channel.Id;

            if (!config.hhChannels.Contains(channelID))
            {
                config.hhChannels.Add(channelID);
                await SendMessage(msg.Channel, "This channel will now receive HH reports.");
                if (!PR2_Utilities.watchingHH)
                    PR2_Utilities.BeginWatchingHH();
            }
            else
            {
                config.hhChannels.Remove(channelID);
                await SendMessage(msg.Channel, "This channel will no longer receive HH reports.");
            }

            config.Save(configPath);
            return true;
        }
        private async void ReportNewHH(List<JToken> list)
        {
            if (!IsConnected)
                return;

            StringBuilder genericMessage = new StringBuilder(list.Count + " server");
            genericMessage.Append(list.Count == 1 ? " has" : "s have").Append(" become happy!");
            genericMessage.Append("```\n");
            for (int i = 0; i < list.Count; i++)
            {
                if ((int)list[i]["guild_id"] == 0)
                    genericMessage.Append(list[i]["server_name"] + ", ");
            }
            genericMessage.Length -= 2;
            string baseMessage = genericMessage.ToString();

            foreach (ulong channelID in config.hhChannels)
            {
                IMessageChannel channel = socketClient.GetChannel(channelID) as IMessageChannel;
                SocketGuild guild = (channel as SocketGuildChannel)?.Guild;

                string rolePrefix = guild == null ? "" : "<@&" + config.guilds[guild.Id].hhRole + "> ";
                await SendMessage(channel, rolePrefix + baseMessage + "```");
            }
        }

        private async Task<bool> CleanConfig(SocketMessage msg, params string[] args)
        {
            if (socketClient.GetChannel(config.loggingChannel) == null)
                config.loggingChannel = 0;
            foreach (ulong id in config.hhChannels)
            {
                if (socketClient.GetChannel(id) == null)
                    config.hhChannels.Remove(id);
            }

            for (int i = 0; i < config.guilds.Count; i++)
            {
                SocketGuild guild = socketClient.GetGuild(config.guilds.Keys[i]);
                if (guild == null)
                {
                    config.guilds.RemoveAt(i);
                    i--;
                    continue;
                }

                GuildConfigInfo guildConfig = config.guilds.Values[i];
                if (!guild.Roles.Any((r) => r.Id == guildConfig.hhRole))
                    guildConfig.hhRole = 0;
                if (!guild.Roles.Any((r) => r.Id == guildConfig.trustedRole))
                    guildConfig.trustedRole = 0;
                if (!guild.Roles.Any((r) => r.Id == guildConfig.verifiedRole))
                    guildConfig.verifiedRole = 0;
            }
            config.Save(configPath);

            await SendMessage(msg.Channel, GetUsername(msg.Author) + ", all invalid roles and channels in my config file have been removed.");

            return true;
        }
        #endregion

        #region "owner"
        private async Task<bool> GTFO(SocketMessage msg, params string[] args)
        {
            await SendMessage(msg.Channel, "I'm sorry you feel that way, " + GetUsername(msg.Author) +
              ". :(\nI guess I'll leave now. Bye guys!");
#pragma warning disable CS4014
            Disconnect(); // Do not await because DCing in the middle of the DiscordSocketClient's MessageReceived event causes problems.
#pragma warning restore CS4014
            return true;
        }

        private async Task<bool> BanUser(SocketMessage msg, params string[] args)
        {
            int count = 0;
            foreach (ITag tag in msg.Tags)
            {
                if (tag.Type == TagType.UserMention && tag.Key != BotID)
                {
                    if (specialUsers.BanUser(tag.Key))
                        count++;
                }
            }

            await SendMessage(msg.Channel, count + " user(s) have been banned.");
            return count != 0;
        }
        private async Task<bool> UnbanUser(SocketMessage msg, params string[] args)
        {
            int count = 0;
            foreach (ITag tag in msg.Tags)
            {
                if (tag.Type == TagType.UserMention && tag.Key != BotID)
                {
                    if (specialUsers.UnbanUser(tag.Key))
                        count++;
                }
            }

            await SendMessage(msg.Channel, count + " user(s) have been unbanned.");
            return count != 0;
        }

        private async Task<bool> GetLog(SocketMessage msg, params string[] args)
        {
            await SendFile(await socketClient.GetUser(specialUsers.Owner).GetOrCreateDMChannelAsync(), outputPath);
            return true;
        }
        private async Task<bool> GetError(SocketMessage msg, params string[] args)
        {
            await SendFile(await socketClient.GetUser(specialUsers.Owner).GetOrCreateDMChannelAsync(), errorPath);
            return true;
        }

        private async Task<bool> LogToChannel(SocketMessage msg, params string[] args)
        {
            if (config.loggingChannel == msg.Channel.Id)
            {
                config.loggingChannel = 0;
                await SendMessage(msg.Channel, "Log messages will no longer be sent to this channel.");
            }
            else
            {
                config.loggingChannel = msg.Channel.Id;
                await SendMessage(msg.Channel, "Now sending all log messages to this channel.");
            }
            config.Save(configPath);
            return true;
        }
        private async Task<bool> SetLoggingLevel(SocketMessage msg, params string[] args)
        {
            if (int.TryParse(args[1], out config.loggingLevel))
            {
                await SendMessage(msg.Channel, "Logging level set.");
                config.Save(configPath);
            }
            else
                await SendMessage(msg.Channel, "Could not parse `" + args[1] + "`.");
            return true;
        }
        private async Task<bool> ClearLog(SocketMessage msg, params string[] args)
        {
            File.Delete(outputPath);
            await AppendToLog("<log_cleared time='" + DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString() + "'></log_cleared>\n");
            await SendMessage(msg.Channel, "Log file cleared.");
            return true;
        }
        #endregion

        private async Task<bool> SendBannedMessage(SocketMessage msg, params string[] args)
        {
            await SendMessage(await msg.Author.GetOrCreateDMChannelAsync(), "You have been banned from this bot.");
            return true;
        }

        #endregion
    }
}
