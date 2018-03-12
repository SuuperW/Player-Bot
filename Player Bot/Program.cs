using System;
using System.Threading.Tasks;
using System.Linq;
using System.Text;

namespace Player_Bot
{
    class Program
    {
        static PlayerBot bot;

        static async Task Main(string[] args)
        {
            try
            {
                string logStr = args.FirstOrDefault((s) => s.StartsWith("log"));
                if (logStr != null)
                {
                    int.TryParse(logStr.Substring(3), out int logLevel);
                    bot = new PlayerBot(logLevel);
                }
                else
                    bot = new PlayerBot();

                bot.Connected += Connected;
                bot.Disconnected += Disconnected;
                await ConnectBot();

                if (args.Contains("bg"))
                    await Task.Delay(-1);
                else
                {
                    string userInput = "";
                    while (userInput != "e")
                    {
                        userInput = Console.ReadLine();

                        if (userInput == "connect")
                        {
                            if (!bot.IsConnected)
                                await ConnectBot();
                        }
                        else if (userInput == "dc")
                        {
                            if (bot.IsConnected)
                                await bot.Disconnect();
                        }
                    }

                    if (bot.IsConnected)
                        await bot.Disconnect();
                }
            }
            catch (Exception ex)
            {
                StringBuilder errorText = new StringBuilder();
                errorText.Append(ex.Message);
                errorText.Append("\n\n");
                errorText.Append(ex.StackTrace);
                while (ex.InnerException != null)
                {
                    ex = ex.InnerException;
                    errorText.Append("\n\n\n");
                    errorText.Append(ex.Message);
                    errorText.Append("\n\n");
                    errorText.Append(ex.StackTrace);
                }

                System.IO.File.WriteAllText("error.txt", errorText.ToString());
            }
        }

        static async Task ConnectBot()
        {
            Console.WriteLine("Connecting...");
            bool success = false;
            while (!success)
            {
                try { await bot.ConnectAndStart(); success = true; }
                catch (Exception ex)
                {
                    Console.WriteLine("Connection error. (" + ex.Message + ") Retrying in 5 seconds...");
                    await Task.Delay(5000);
                }
            }
        }

        static void Connected()
        {
            Console.WriteLine("Connected.");
        }
        static void Disconnected()
        {
            Console.WriteLine("Disconnected.");
        }
    }
}
