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
        const string outputPath = "files/output.xml";
        const string errorPath = "files/error.txt";

        JObject secrets;
        string bot_token;
        string bot_name_discrim;
        const string secretsPath = "files/secrets.txt";

        int loggingLevel;
        IMessageChannel loggingChannel = null;

        int tempFileID = -1;
        private string GetTempFileName()
        {
            tempFileID++;
            return "temp/" + tempFileID;
        }

        DiscordSocketClient socketClient;

        ulong BotID { get => socketClient.CurrentUser.Id; }
        public bool IsConnected { get => socketClient.ConnectionState >= ConnectionState.Connected; }

        SpecialUsersCollection specialUsers;
        CommandHistory commandHistory = new CommandHistory();

        public PlayerBot(int loggingLevel = 2)
        {
            this.loggingLevel = loggingLevel;

            if (!File.Exists(secretsPath))
                throw new FileNotFoundException("PlayerBot could not find secrets.txt. Please see README for info on how to set this up.");
            secrets = JObject.Parse(File.ReadAllText("files/secrets.txt"));
            bot_token = secrets["bot_token"].ToString();

            specialUsers = new SpecialUsersCollection("files/specialUsers.txt");

            helpStrings = new SortedDictionary<string, string>();
            JObject helpJson = JObject.Parse(File.ReadAllText("files/helpTopics.txt"));
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
            loggingChannel = null;

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
            if (priority >= loggingLevel)
            {
                if (loggingChannel != null)
                    loggingChannel.SendMessageAsync(text);
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
                            command = new BotCommand(async (m, a) => { await SendMessage(msg.Channel, msg.Author.Username + waitMessage); return true; });
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
                        await SendMessage(msg.Channel, msg.Author.Username + ", I attempted to send you a DM " +
                          "but was unable to. Please ensure that you can receive DMs from me.");
                    }
                }
            }
            catch (Exception ex)
            {
                await LogError(ex);

                await SendFile(await socketClient.GetUser(specialUsers.Owner).GetOrCreateDMChannelAsync(), errorPath, "I've encountered an error.");
                await SendMessage(msg.Channel, msg.Author.Username +
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
                else if (specialUsers.IsUserTrusted(msg.Author.Id) && trustedBotCommands.ContainsKey(commandStr))
                    command = trustedBotCommands[commandStr];
                else if (specialUsers.Owner == msg.Author.Id && ownerBotCommands.ContainsKey(commandStr))
                    command = ownerBotCommands[commandStr];
                else
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

        #region "Bot Commands"
        private SortedList<string, BotCommand> everybodyBotCommands;
        private SortedList<string, BotCommand> trustedBotCommands;
        private SortedList<string, BotCommand> ownerBotCommands;
        private BotCommand bannedCommand;

        private void InitializeBotCommandsList()
        {
            everybodyBotCommands = new SortedList<string, BotCommand>
            {
                { "help", new BotCommand(SendHelpMessage) },
                { "commands", new BotCommand(SendCommandsList) },
            };

            trustedBotCommands = new SortedList<string, BotCommand>
            {
            };

            ownerBotCommands = new SortedList<string, BotCommand>
            {
                { "add_trusted_user", new BotCommand(AddTrustedUser) },
                { "remove_trusted_user", new BotCommand(RemoveTrustedUser) },
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
            if (specialUsers.IsUserTrusted(msg.Author.Id))
            {
                availableCommands.Append("\n\n----- Trusted User Commands -----");
                foreach (KeyValuePair<string, BotCommand> kvp in trustedBotCommands)
                    availableCommands.Append("\n" + kvp.Key);
            }
            if (specialUsers.Owner == msg.Author.Id)
            {
                availableCommands.Append("\n\n----- Owner Commands -----");
                foreach (KeyValuePair<string, BotCommand> kvp in ownerBotCommands)
                    availableCommands.Append("\n" + kvp.Key);
            }

            await SendMessage(await msg.Author.GetOrCreateDMChannelAsync(), "Here are the commands you can use: ```" + availableCommands + "```");
            return true;
        }
        #endregion

        #region "owner"
        private async Task<bool> AddTrustedUser(SocketMessage msg, params string[] args)
        {
            int count = 0;
            foreach (ITag tag in msg.Tags)
            {
                if (tag.Type == TagType.UserMention && tag.Key != BotID)
                {
                    if (specialUsers.AddTrustedUser(tag.Key))
                        count++;
                }
            }

            await SendMessage(msg.Channel, "Added " + count + " user(s) to trusted user list.");
            return count != 0;
        }
        private async Task<bool> RemoveTrustedUser(SocketMessage msg, params string[] args)
        {
            int count = 0;
            foreach (ITag tag in msg.Tags)
            {
                if (tag.Type == TagType.UserMention && tag.Key != BotID)
                {
                    if (specialUsers.RemoveTrustedUser(tag.Key))
                        count++;
                }
            }

            await SendMessage(msg.Channel, "Removed " + count + " user(s) from trusted user list.");
            return count != 0;
        }

        private async Task<bool> GTFO(SocketMessage msg, params string[] args)
        {
            await SendMessage(msg.Channel, "I'm sorry you feel that way, " + msg.Author.Username +
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
            if (loggingChannel != null && loggingChannel.Id == msg.Channel.Id)
            {
                loggingChannel = null;
                await SendMessage(msg.Channel, "Log messages will no longer be sent to this channel.");
            }
            else
            {
                loggingChannel = msg.Channel;
                await SendMessage(msg.Channel, "Now sending all log messages to this channel.");
            }
            return true;
        }
        private async Task<bool> SetLoggingLevel(SocketMessage msg, params string[] args)
        {
            if (int.TryParse(args[1], out loggingLevel))
                await SendMessage(msg.Channel, "Logging level set.");
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
