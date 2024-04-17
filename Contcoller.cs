using CompileTools2._0;
using ServerLibrary;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Timers;

namespace CompileServer
{
    internal class Contcoller
    {
        const string SendGroupMsgUrl = "http://172.16.4.16:5700/send_group_msg";
        const string SendUserMsgUrl = "http://172.16.4.16:5700/send_private_msg";
        readonly ServerLibrary.HttpListenerServer server;
        readonly Dictionary<Work, ProjectConfig> Init_Dic;
        private static readonly Dictionary<string, Func<ProjectConfig, bool>> TaskDic = new();
        //public CdnSftpClient.FileSystemContcoller synCdnController;
        public Contcoller()
        {
            Init_Dic = new();
            var init = new Init();
            foreach (var item in init.Init_Dic)
            {
                var work = new Work(item.Value);
                Init_Dic[work] = item.Value;
                work.RunningEvent += StopTimer;
                work.DoneEvent += StartTimer;
            }
            server = new(8848)
            {
                ResponseContentType = "application/json;charset=UTF-8"
            };


            server.RequestEvent += HandMasg;
            TaskDic.Add("更新db", BotUpdateDb);
            TaskDic.Add(@"跨([\d\u4E00\u4E8C\u4E09\u56DB\u4E94\u516D\u4E03\u516B\u4E5D\u96F6０-９]+)天", BotSetLastDay);
            TaskDic.Add(@"跨([\d\u4E00\u4E8C\u4E09\u56DB\u4E94\u516D\u4E03\u516B\u4E5D\u96F6０-９]+)小时", BotSetLastHour);
            TaskDic.Add(@"开服([\d\u4E00\u4E8C\u4E09\u56DB\u4E94\u516D\u4E03\u516B\u4E5D\u96F6０-９]+)天", BotSetOpenTime);
            TaskDic.Add(@"开服时间", BotGetOpenTime);
            TaskDic.Add(@"恢复时间", BotSynTime);
            TaskDic.Add(@"sj", BotGetTime);
            TaskDic.Add(@"ReStart", BotReStart);
            //TimerTask.AddTimer(Restarttimer, 09, 30, timerName: "BotReStart1");
            //synCdnController = new CdnSftpClient.FileSystemContcoller(new CdnSftpClient.Config());
        }
        void StopTimer()
        {
            foreach (var work in Init_Dic.Keys)
            {
                work.timer.Stop();
            }
        }
        void StartTimer()
        {
            foreach (var work in Init_Dic.Keys)
            {
                work.timer.Start();
            }
        }
        void Restarttimer(object? sender, ElapsedEventArgs e)
        {
            BotReStart(Init_Dic.First().Value);
            var timer = (sender as System.Timers.Timer);
            if(timer is not null)
            {
                timer.Interval = 24 * 60 * 60 * 1000;
                TimerTask.NextRunLog(Init_Dic.First().Value.ProjectName, timer.Interval);
            }
        }
        public readonly static string[] TaskStrings = new string[] {"更新db","跨n天", "跨n小时", "开服n天(基于服务器天数)", "开服时间", "恢复时间" ,"sj"};
        private static string OMUpdateDb
        {
            get
            {
                var obj = new
                {
                    msg = "update",
                };
                return JsonSerializer.Serialize(obj);
            }
        }
        public struct SendMessage
        {
            public SendMessage(string msg, string val, long time = 0)
            {
                Msg = msg;
                Val = val;
                Time = time;
            }
            [JsonPropertyName("msg")]
            public string Msg { get; set; }
            [JsonPropertyName("data")]
            public string Val { get; set; }
            [JsonPropertyName("time")]
            public long Time { get; set; }

            public readonly string GetJsonString()
            {
                return JsonSerializer.Serialize(this);
            }
        }
        private bool BotGetOpenTime(ProjectConfig project)
        {
            _ = ServerLibrary.Common.ACurlAsync($"http://{project.ServerHost}", new SendMessage("getOpenTime", project.GroupId.ToString()).GetJsonString());
            return true;
        }
        private bool BotSetOpenTime(ProjectConfig project)
        {
            int val = (int)project.Obj;
            if (val > 300)
            {
                var json = new ServerLibrary.Send_group_msg(project.GroupId, "不可以那么多").GetJsonString();
                _ = ServerLibrary.Common.ACurlAsync(SendGroupMsgUrl, json);
                return false;
            }
            else
            {
                ServerLibrary.TaskExtensions.SafeFireAndForget(Func(), e => Console.WriteLine(e.StackTrace));
                async Task Func()
                {
                    string json = await ServerLibrary.Common.ACurlAsync($"http://{project.ServerHost}", new SendMessage("getTime", project.GroupId.ToString()).GetJsonString());
                    long serverTime = JsonSerializer.Deserialize<SendMessage>(json).Time;
                    DateTime setOpenTime = ServerLibrary.UnixTime.GetDateTime(serverTime).AddDays(-(val)).AddDays(1);
                    long sendTime = ServerLibrary.UnixTime.GetTimeStamp(setOpenTime);
                    ServerLibrary.TaskExtensions.SafeFireAndForget(ServerLibrary.Common.ACurlAsync($"http://{project.ServerHost}", new SendMessage("setOpenTime", project.GroupId.ToString(), sendTime).GetJsonString()), e => Console.WriteLine(e.StackTrace));
                }
                return true;
            }
        }
        private bool BotGetTime(ProjectConfig project)
        {
            ServerLibrary.TaskExtensions.SafeFireAndForget(Func(), e=>Console.WriteLine(e.StackTrace));
            async Task Func()
            {
                string json = await ServerLibrary.Common.ACurlAsync($"http://{project.ServerHost}", new SendMessage("getTime", project.GroupId.ToString()).GetJsonString());
                var time = JsonSerializer.Deserialize<SendMessage>(json).Time;
                var botMessage = new ServerLibrary.Send_group_msg(project.GroupId, ServerLibrary.UnixTime.GetDateTime(time).ToString()).GetJsonString();
                ServerLibrary.TaskExtensions.SafeFireAndForget(ServerLibrary.Common.ACurlAsync(SendGroupMsgUrl, botMessage), e => Console.WriteLine(e.StackTrace));
            }
            return true;
        }
        private bool BotSynTime(ProjectConfig project)
        {
            DateTime lastHour = DateTime.Now;
            _ = ServerLibrary.Common.ACurlAsync($"http://{project.ServerHost}", new SendMessage("setTime", project.GroupId.ToString(), ServerLibrary.UnixTime.GetTimeStamp(lastHour)).GetJsonString());
            return true;
        }
        private bool BotSetLastHour(ProjectConfig project)
        {
            int val = (int)project.Obj;
            if (val > 300)
            {
                var json = new ServerLibrary.Send_group_msg(project.GroupId, "不可以那么多").GetJsonString();
                _ = ServerLibrary.Common.ACurlAsync(SendGroupMsgUrl, json);
                return false;
            }
            else
            {
                ServerLibrary.TaskExtensions.SafeFireAndForget(Func(), e => Console.WriteLine(e.StackTrace));
                async Task Func()
                {
                    string json = await ServerLibrary.Common.ACurlAsync($"http://{project.ServerHost}", new SendMessage("getTime", project.GroupId.ToString()).GetJsonString());
                    long serverTime = JsonSerializer.Deserialize<SendMessage>(json).Time;
                    DateTime addLaterTime = ServerLibrary.UnixTime.GetDateTime(serverTime).AddHours(val);
                    long sendTime = ServerLibrary.UnixTime.GetTimeStamp(addLaterTime);
                    ServerLibrary.TaskExtensions.SafeFireAndForget(ServerLibrary.Common.ACurlAsync($"http://{project.ServerHost}", new SendMessage("setTime", project.GroupId.ToString(), sendTime).GetJsonString()), e => Console.WriteLine(e.StackTrace));
                }
                return true;
            }
        }
        private bool BotSetLastDay(ProjectConfig project)
        {
            int val = (int)project.Obj;
            if(val>30)
            {
                var json = new ServerLibrary.Send_group_msg(project.GroupId, "不可以那么多").GetJsonString();
                _ = ServerLibrary.Common.ACurlAsync(SendGroupMsgUrl, json);
                return false;
            }
            else
            {
                ServerLibrary.TaskExtensions.SafeFireAndForget(Func(), e => Console.WriteLine(e.StackTrace));
                async Task Func()
                {
                    string json = await ServerLibrary.Common.ACurlAsync($"http://{project.ServerHost}", new SendMessage("getTime", project.GroupId.ToString()).GetJsonString());
                    long serverTime = JsonSerializer.Deserialize<SendMessage>(json).Time;
                    DateTime addLaterTime = ServerLibrary.UnixTime.GetDateTime(serverTime).AddDays(val);
                    long sendTime = ServerLibrary.UnixTime.GetTimeStamp(addLaterTime);
                    ServerLibrary.TaskExtensions.SafeFireAndForget(ServerLibrary.Common.ACurlAsync($"http://{project.ServerHost}", new SendMessage("setTime", project.GroupId.ToString(), sendTime).GetJsonString()), e => Console.WriteLine(e.StackTrace));
                }
                return true;
            }
        }
        private bool BotUpdateDb(ProjectConfig project)
        {
            _ = ServerLibrary.Common.ACurlAsync(SendGroupMsgUrl, new ServerLibrary.Send_group_msg(project.GroupId, "开始更新").GetJsonString());
            if (project.ProjectName is "SGFZ")
            {
                ServerLibrary.TaskExtensions.SafeFireAndForget(ServerLibrary.Common.ACurlAsync($"http://{project.ServerHost}", new SendMessage("setTime", project.GroupId.ToString(), ServerLibrary.UnixTime.Now).GetJsonString()), e => Console.WriteLine(e.StackTrace));
                _ = ServerLibrary.Common.ACurlAsync($"http://{project.ServerHost}", OMUpdateDb);
            }
            else
            {
                ServerLibrary.TaskExtensions.SafeFireAndForget(ServerLibrary.Common.ACurlAsync($"http://{project.ServerHost}", new SendMessage("setTime", project.GroupId.ToString(), ServerLibrary.UnixTime.Now).GetJsonString()), e => Console.WriteLine(e.StackTrace));
                _ = ServerLibrary.Common.ACurlAsync($"http://{project.ServerHost}", new SendMessage("updateDb", project.GroupId.ToString()).GetJsonString());
            }
            return true;
        }
        private static bool BotHelp(ProjectConfig project)
        {
            string str = JsonSerializer.Serialize(TaskStrings, ServerLibrary.Common.JsonSerializerEncoder).Replace("\"", "");
            var botMessage = JsonSerializer.Serialize(new ServerLibrary.Send_group_msg(project.GroupId, str), ServerLibrary.Common.JsonSerializerEncoder);
            _ = ServerLibrary.Common.ACurlAsync(SendGroupMsgUrl, $"{ System.Web.HttpUtility.UrlDecode(botMessage, Encoding.Default)}");
            return true;
        }
        private bool BotReStart(ProjectConfig project)
        {
            var botStart = "D:\\qq\\Qsign-Onekey-1.1.9-bitterest\\go-cqhttp.bat";
            if (!File.Exists(botStart))
            {
                return false;
            }
            Common.CloseCmdWindow("go-cqhttp");
            if(Common.Bat(botStart))
            {
                ServerLibrary.TaskExtensions.SafeFireAndForget(Func(), e => Console.WriteLine(e.StackTrace));
            }

            async Task Func()
            {
                Task.Delay(3000).GetAwaiter().GetResult();
                string json = await ServerLibrary.Common.ACurlAsync($"http://{project.ServerHost}", new SendMessage("gettime", project.GroupId.ToString()).GetJsonString());
                if(long.TryParse(JsonSerializer.Deserialize<SendMessage>(json).Val,out var time))
                {
                    var botMessage = new ServerLibrary.Send_group_msg(project.GroupId, ServerLibrary.UnixTime.GetDateTime(time).ToString()).GetJsonString();
                    ServerLibrary.TaskExtensions.SafeFireAndForget(ServerLibrary.Common.ACurlAsync(SendGroupMsgUrl, botMessage), e => Console.WriteLine(e.StackTrace));
                }
            }
            return true;
        }

        private static bool BotReStart(long userId)
        {
            var botStart = "D:\\Qsign-Onekey-1.1.9-bitterest\\go-cqhttp.bat";

            if (!File.Exists(botStart))
            {
                return false;
            }

            Common.CloseCmdWindow("go-cqhttp");
            if (Common.Bat(botStart))
            {
                Task.Delay(3000).GetAwaiter().GetResult();
                var botMessage = new ServerLibrary.Send_private_msg(userId, "正在重启").GetJsonString();
                ServerLibrary.TaskExtensions.SafeFireAndForget(ServerLibrary.Common.ACurlAsync(SendUserMsgUrl, botMessage), e => Console.WriteLine(e.StackTrace));
                botMessage = new ServerLibrary.Send_private_msg(userId, userId.ToString()).GetJsonString();
                ServerLibrary.TaskExtensions.SafeFireAndForget(ServerLibrary.Common.ACurlAsync(SendUserMsgUrl, botMessage), e => Console.WriteLine(e.StackTrace));
                return true;
            }
            return false;
        }

        private void HandMasg(string RequestStr, HttpListenerContext httpContext)
        {
            var message = ServerLibrary.Common.TryDeserialize<ServerLibrary.BotMessage>(RequestStr);
            if (message.group_id != 0)
            {
                Logic(message);
            }
            var obj = new
            {
                key = 200,
                val = 200,
            };
            server.ResponseEvent?.Invoke(JsonSerializer.Serialize(obj), httpContext);
        }
        const string pattern = @"\[CQ\:at\,qq\=(\d*?)\]";
        private void Logic(ServerLibrary.BotMessage botMessage)
        {
            if(botMessage.user_id== 1019696939|| botMessage.message is null)
            {
                return;
            }
            Match match = Regex.Match(botMessage.message, pattern);
            if (match.Success)
            {
                var str = match.Groups[1].Value;
                if (str != null)
                {
                    if (str == botMessage.self_id.ToString())
                    {

                        KeyValuePair<Work, ProjectConfig> work = Init_Dic.FirstOrDefault(x => x.Value.GroupId == botMessage.group_id);
                        Match isBt2 = Regex.Match(botMessage.message.ToLower(), "bt2");
                        if (isBt2.Success)
                        {
                            work = Init_Dic.FirstOrDefault(x => x.Value.ProjectName == "BT2");
                        }
                        Match isWSFOP = Regex.Match(botMessage.message.ToLower(), "wsfop");
                        if (isWSFOP.Success)
                        {
                            work = Init_Dic.FirstOrDefault(x => x.Value.ProjectName.ToLower() == "wsfop");
                        }
                        if (work.Key is null)
                        {
                            BotReStart(botMessage.user_id);
                            return;
                        }
                        var project = work.Value;
                        var fun = RegexMatchTask(botMessage.message.ToLower(), out var valMatch);
                        if (valMatch is not null)
                        {
                            string numTxt = Regex.Replace(valMatch.Groups[1].Value, "[零一二三四五六七八九]", match => (Array.IndexOf(Program.strs1, match.Value)).ToString());
                            string numTxt2 = Regex.Replace(valMatch.Groups[1].Value, "[０１２３４５６７８９]", match => (Array.IndexOf(Program.strs2, match.Value)).ToString());
                            if (int.TryParse(numTxt, out int day) || int.TryParse(numTxt2, out day))
                            {
                                project.Obj = day;
                            }
                        }
                        fun.Invoke(project);
                    }
                }
            }
        }
        private static Func<ProjectConfig, bool> RegexMatchTask(string message,out Match? m)
        {
            m = null;
            foreach (var task in TaskDic)
            {
                foreach (var match in Regex.Matches(message, task.Key).Cast<Match>())
                {
                    if (match.Success)
                    {
                        m = match;
                        return task.Value;
                    }
                }
            }
            return BotHelp;
        }
    }
}
