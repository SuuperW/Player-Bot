using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Discord;
using Discord.WebSocket;

namespace Player_Bot
{
    class CommandHistory
    {
        const double ticksPerSecond = 10000000;

        private SortedList<ulong, SortedDictionary<string, long>> history = new SortedList<ulong, SortedDictionary<string, long>>();
        private SortedDictionary<string, long> commandHistory = new SortedDictionary<string, long>();
        private SortedDictionary<ulong, long> userHistory = new SortedDictionary<ulong, long>();

        public void AddCommand(string command, ulong userID)
        {
            if (!history.ContainsKey(userID))
                history.Add(userID, new SortedDictionary<string, long>());

            history[userID][command] = commandHistory[command] = userHistory[userID] = DateTime.Now.Ticks;
        }

        private double TimeSinceLastUse(string command)
        {
            if (!commandHistory.ContainsKey(command))
                return double.PositiveInfinity;
            return (DateTime.Now.Ticks - commandHistory[command]) / ticksPerSecond;
        }
        private double TimeSinceLastUse(string command, ulong userID)
        {
            if (!history.ContainsKey(userID) || !history[userID].ContainsKey(command))
                return double.PositiveInfinity;
            return (DateTime.Now.Ticks - history[userID][command]) / ticksPerSecond;
        }
        public double TimeSinceLastCommand(ulong userID)
        {
            if (!userHistory.ContainsKey(userID))
                return double.PositiveInfinity;
            return (DateTime.Now.Ticks - userHistory[userID]) / ticksPerSecond;
        }

        /// <summary>
        /// Determines if the given command can be executed at this time by the specified user.
        /// If not, it returns a message to be sent to the user. If the command is allowed at this time, returns null.
        /// </summary>
        public string GetWaitMessage(BotCommand command, ulong userID)
        {
            string message = null;
            if (TimeSinceLastUse(command.Name) < command.MinDelay)
                message = ", I've been getting too many of those commands lately. Please try again later.";
            else if (TimeSinceLastUse(command.Name, userID) < command.MinDelayPerUser)
                message = ", you may only use that command once every " + command.MinDelayPerUser + " seconds.";
            return message;
        }
    }
}
