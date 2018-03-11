A Discord bot for doing stuff related to the game Platform Racing 2. (pr2hub.com)

Before running the bot, you must add a file named secrets.txt to the files folder, which should include a JSON object with the following members: bot_token (your Discord bot's token), pr2_username, pr2_password, and (optionally) pr2_token.
You should also add keys.txt, which should include a JSON object with the following members: login_key and login_iv. (These are for encrypting PR2 login requests.)