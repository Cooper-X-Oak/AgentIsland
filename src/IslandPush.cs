using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AgentIsland
{
    internal static class IslandPushProgram
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string source = "cli";
            string project = "外部通知";
            string title = "新消息";
            string summary = "";
            string kindArg = "reply";
            string id = "";

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "-h" || a == "--help")
                {
                    PrintUsage();
                    return 0;
                }
                if (i + 1 >= args.Length) continue;

                switch (a)
                {
                    case "--source":
                    case "-s":
                        source = args[++i];
                        break;
                    case "--project":
                    case "-p":
                        project = args[++i];
                        break;
                    case "--title":
                    case "-t":
                        title = args[++i];
                        break;
                    case "--body":
                    case "--summary":
                    case "-b":
                        summary = args[++i];
                        break;
                    case "--kind":
                    case "-k":
                        kindArg = args[++i];
                        break;
                    case "--id":
                        id = args[++i];
                        break;
                }
            }

            string mappedKind = MapKind(kindArg);
            if (string.IsNullOrEmpty(id))
            {
                id = source + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            if (string.IsNullOrEmpty(project) || project == "外部通知")
            {
                if (!string.IsNullOrEmpty(source) && source != "cli") project = source;
            }

            try
            {
                string customInbox = Environment.GetEnvironmentVariable("ISLAND_INBOX_DIR");
                string inboxDir = !string.IsNullOrWhiteSpace(customInbox)
                    ? customInbox
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "AgentIsland",
                        "inbox"
                    );
                Directory.CreateDirectory(inboxDir);

                var evt = new Dictionary<string, object>
                {
                    { "source", source },
                    { "kind", mappedKind },
                    { "conversationId", id },
                    { "project", project },
                    { "title", title },
                    { "summary", string.IsNullOrEmpty(summary) ? title : summary },
                    { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
                };

                var serializer = new JavaScriptSerializer();
                string fileName = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                string tmpPath = Path.Combine(inboxDir, fileName + ".tmp");
                string finalPath = Path.Combine(inboxDir, fileName + ".json");

                File.WriteAllText(tmpPath, serializer.Serialize(evt), new UTF8Encoding(false));
                File.Move(tmpPath, finalPath);
                Console.WriteLine("PUSHED:" + id);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PUSH_ERROR:" + ex.Message);
                return 1;
            }
        }

        private static string MapKind(string k)
        {
            if (string.IsNullOrEmpty(k)) return "needs_reply";
            switch (k.Trim().ToLowerInvariant())
            {
                case "decision":
                case "approval":
                case "warn":
                case "warning":
                    return "needs_decision";
                case "error":
                case "fail":
                case "failed":
                    return "error";
                case "working":
                case "running":
                    return "working";
                case "clear":
                case "cleared":
                case "done":
                    return "cleared";
                case "health":
                    return "health_test";
                default:
                    return "needs_reply";
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("AgentIsland Push CLI");
            Console.WriteLine("Usage: island-push.exe --title <title> --body <text> [--project <name>] [--source <src>] [--kind info|decision|error|working|clear|health] [--id <key>]");
        }
    }
}
