using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

using Discord.WebSocket;

namespace Player_Bot
{
    class BotCommand
    {
        public delegate Task<bool> CommandDelegate(SocketMessage msg, params string[] args);

        public CommandDelegate Delegate { get; private set; }
        public string Name { get => Delegate.Method.Name; }

        public double MinDelay { get; set; }
        public double MinDelayPerUser { get; set; }

        public BotCommand(CommandDelegate commandDelegate, double minDelayPerUser = 2, double minDelay = -1)
        {
            Delegate = commandDelegate;
            MinDelayPerUser = minDelayPerUser;
            MinDelay = minDelay;
        }
    }
}
