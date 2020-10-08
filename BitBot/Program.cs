using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Discord;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;

namespace BitBot
{
    class Program
    {

        private static HttpListener listener = new HttpListener();
        private static Thread listenThread;

        private static BlockingCollection<string> typeQueue = new BlockingCollection<string>();
        private static BlockingCollection<string> messageQueue = new BlockingCollection<string>();

        private readonly DiscordSocketClient client;

        //Bot Token goes here.
        private readonly string botToken = "";
        //Discord Guild ID here.
        private readonly ulong guildID = 0;
        //Channel ID Here.
        private readonly ulong channelID = 0;

        static void Main(string[] args)
        {

            Console.WriteLine("Bot Started!");

            listener.Prefixes.Add("http://*:9107/");
            listener.Start();

            listenThread = new Thread(new ParameterizedThreadStart(ListenerStart));
            listenThread.IsBackground = true;
            listenThread.Start();

            new Program().MainAsync().GetAwaiter().GetResult();

        }

        public Program()
        {

            client = new DiscordSocketClient();

            //client.Log += LogAsync;

        }

        public async Task MainAsync()
        {

            await client.LoginAsync(TokenType.Bot, botToken);
            await client.StartAsync();

            while (true)
            {
                if (messageQueue.Count != 0)
                {

                    var channel = client.GetGuild(guildID).GetTextChannel(channelID);
                    if (channel == null)
                    {
                        continue;
                    }

                    try
                    {

                        switch (typeQueue.Take())
                        {
                            case "repo:push":

                                JObject details = JObject.Parse(messageQueue.Take());

                                var authorURL = details["actor"]["links"]["html"]["href"].Value<string>();
                                authorURL = authorURL.Replace("{", "%7B");
                                authorURL = authorURL.Replace("}", "%7D");

                                var author = new EmbedAuthorBuilder
                                {
                                    Name = details["actor"]["nickname"].Value<string>(),
                                    Url = authorURL,
                                    IconUrl = details["actor"]["links"]["avatar"]["href"].Value<string>()
                                };

                                string amount;
                                amount = details["push"]["changes"][0]["commits"].Count() == 1 ? " new commit!" : " new commits!";

                                string commitMessage = "";
                                string commitSummary = "";
                                var regex = new Regex(@"[^ \n]");

                                if (details["push"]["changes"][0]["commits"].Count() == 1)
                                {

                                    commitSummary = details["push"]["changes"][0]["commits"][0]["summary"]["raw"].Value<string>();
                                    if (commitSummary.Contains("[HIDDEN]"))
                                    {
                                        commitSummary = regex.Replace(commitSummary, "•");
                                    }

                                    commitMessage = commitMessage + "[" 
                                                                  + details["push"]["changes"][0]["commits"][0]["hash"].Value<string>().Substring(0, 7) 
                                                                  + "]" 
                                                                  + "(https://bitbucket.org/" 
                                                                  + details["repository"]["full_name"].Value<string>()
                                                                  + "/commits/"
                                                                  + details["push"]["changes"][0]["commits"][0]["hash"].Value<string>()
                                                                  + ")"
                                                                  + " - "
                                                                  + commitSummary;
                                }
                                else
                                {
                                    for (int i = 0; i < details["push"]["changes"][0]["commits"].Count(); i++)
                                    {

                                        commitSummary = details["push"]["changes"][0]["commits"][i]["summary"]["raw"].Value<string>();
                                        if (commitSummary.Contains("[HIDDEN]"))
                                        {
                                            commitSummary = regex.Replace(commitSummary, "•");
                                        }

                                        commitMessage = commitMessage + "["
                                                                      + details["push"]["changes"][0]["commits"][i]["hash"].Value<string>().Substring(0, 7)
                                                                      + "]"
                                                                      + "(https://bitbucket.org/"
                                                                      + details["repository"]["full_name"].Value<string>()
                                                                      + "/commits/"
                                                                      + details["push"]["changes"][0]["commits"][i]["hash"].Value<string>()
                                                                      + ")"
                                                                      + " - "
                                                                      + commitSummary + "";

                                    }
                                }

                                var footer = new EmbedFooterBuilder
                                {
                                    Text = "CommitBot"
                                };

                                var embed = new EmbedBuilder
                                {
                                    
                                    Author = author,
                                    Title = ""
                                        + details["repository"]["name"].Value<string>()
                                        + "/"
                                        + details["push"]["changes"][0]["new"]["name"].Value<string>()
                                        + " - "
                                        + details["push"]["changes"][0]["commits"].Count()
                                        + amount
                                        + "",
                                    Description = commitMessage,
                                    Color = Color.Blue,
                                    Timestamp = DateTimeOffset.Now,
                                    Footer = footer

                                };

                                await channel.SendMessageAsync("", false, embed.Build());
                                break;
                            default:
                                //await channel.SendMessageAsync("Invalid Message!");
                                break;
                        }

                        //string message = messageQueue.Take();
                        //await channel.SendMessageAsync(message);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
            }

            //await Task.Delay(Timeout.Infinite);

        }

        private Task LogAsync(LogMessage log)
        {
            Console.WriteLine(log.ToString());
            return Task.CompletedTask;
        }

        private static void ListenerStart(object s)
        {
            while (true)
            {
                ProcessRequest();
            }
        }

        private static void ProcessRequest()
        {

            var result = listener.BeginGetContext(callback, listener);
            result.AsyncWaitHandle.WaitOne();

        }

        private static void callback(IAsyncResult result)
        {

            var ctx = listener.EndGetContext(result);
            Thread.Sleep(1000);
            var data = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding).ReadToEnd();
            var cleanedData = HttpUtility.UrlDecode(data);
            //Console.WriteLine(cleanedData);

#nullable enable
            string? type = null;
#nullable disable

            var items = ctx.Request.Headers.AllKeys.SelectMany(ctx.Request.Headers.GetValues, (k, v) => new { key = k, value = v });
            foreach (var item in items)
            {
                if (item.key == "X-Event-Key")
                {
                    type = item.value;
                    typeQueue.Add(type);
                }
            }

            if (type == null)
            {
                typeQueue.Add("Invalid");
            }


            messageQueue.Add(cleanedData);

            ctx.Response.StatusCode = 200;
            ctx.Response.StatusDescription = "OK";

            ctx.Response.Close();

        }

    }
}
 