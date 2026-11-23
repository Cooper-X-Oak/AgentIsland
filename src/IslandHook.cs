using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AgentIsland
{
    internal static class IslandHookProgram
    {
        private static int Main(string[] args)
        {
            string eventArg = "";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--event" && i + 1 < args.Length)
                {
                    eventArg = args[i + 1];
                    i++;
                }
            }

            try
            {
                Console.InputEncoding = Encoding.UTF8;
                Console.OutputEncoding = Encoding.UTF8;

                string rawInput = Console.In.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(rawInput))
                {
                    ProcessPayload(eventArg, rawInput);
                }
            }
            catch
            {
                // Never block or break the host AI agent loop
            }

            WriteSafeResponse(eventArg);
            return 0;
        }

        private static void WriteSafeResponse(string eventArg)
        {
            if (string.Equals(eventArg, "AskQuestion", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(eventArg, "PreToolUse", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(eventArg, "Stop", StringComparison.OrdinalIgnoreCase))
            {
                Console.Write("{\"decision\":\"allow\"}");
            }
            else
            {
                Console.Write("{}");
            }
        }

        private static void ProcessPayload(string eventArg, string rawJson)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            var root = serializer.DeserializeObject(rawJson) as Dictionary<string, object>;
            if (root == null) return;

            string convId = GetString(root, "conversationId");
            if (string.IsNullOrEmpty(convId)) convId = "default";

            string transcriptPath = GetString(root, "transcriptPath");
            string hookEvent = GetString(root, "hookEventName");
            if (string.IsNullOrEmpty(eventArg)) eventArg = hookEvent;

            string project = ExtractProjectName(root);
            string lastUserInput = CleanSnippet(GetString(root, "lastUserInput"), 90);
            string kind = null;
            string title = "";
            string summary = "";

            if (string.Equals(eventArg, "PreInvocation", StringComparison.OrdinalIgnoreCase))
            {
                kind = "working";
                title = "思考中";
                summary = string.IsNullOrEmpty(lastUserInput) ? "正在执行任务" : lastUserInput;
            }
            else if (string.Equals(eventArg, "AskQuestion", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(eventArg, "PreToolUse", StringComparison.OrdinalIgnoreCase))
            {
                kind = "needs_decision";
                title = "等你拍板";
                summary = ExtractQuestionSummary(root);
                if (string.IsNullOrEmpty(summary))
                {
                    summary = string.IsNullOrEmpty(lastUserInput) ? "弹出选择卡，需要你的决定" : lastUserInput;
                }
            }
            else if (string.Equals(eventArg, "Stop", StringComparison.OrdinalIgnoreCase))
            {
                string reason = GetString(root, "terminationReason");
                string err = CleanSnippet(GetString(root, "error"), 110);

                if (string.Equals(reason, "USER_CANCELED", StringComparison.OrdinalIgnoreCase))
                {
                    kind = "cleared";
                    title = "已取消";
                    summary = "";
                }
                else if (string.Equals(reason, "ERROR", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(err))
                {
                    kind = "error";
                    title = "报错卡住";
                    summary = string.IsNullOrEmpty(err) ? "执行遇到异常，请检查会话" : err;
                }
                else
                {
                    kind = "needs_reply";
                    if (string.Equals(reason, "MAX_INVOCATIONS", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(reason, "MAX_TOKEN_BUDGET_EXCEEDED", StringComparison.OrdinalIgnoreCase))
                    {
                        title = "触顶暂停";
                    }
                    else
                    {
                        title = "等你回复";
                    }
                    summary = string.IsNullOrEmpty(lastUserInput)
                        ? "本轮回复已完成，等你下一步指示"
                        : ("关于「" + lastUserInput + "」已就绪，等你回复");
                }
            }

            if (string.IsNullOrEmpty(kind)) return;

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
                { "source", "antigravity" },
                { "kind", kind },
                { "conversationId", convId },
                { "project", project },
                { "title", title },
                { "summary", summary },
                { "lastUserInput", lastUserInput },
                { "transcriptPath", transcriptPath },
                { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            };

            string fileName = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string tmpPath = Path.Combine(inboxDir, fileName + ".tmp");
            string finalPath = Path.Combine(inboxDir, fileName + ".json");

            string outJson = serializer.Serialize(evt);
            File.WriteAllText(tmpPath, outJson, new UTF8Encoding(false));
            File.Move(tmpPath, finalPath);
        }

        private static string ExtractProjectName(Dictionary<string, object> root)
        {
            string candidate = null;
            object wsObj;
            if (root.TryGetValue("workspacePaths", out wsObj))
            {
                var arr = wsObj as object[];
                if (arr != null && arr.Length > 0 && arr[0] != null)
                {
                    candidate = arr[0].ToString();
                }
                else
                {
                    var list = wsObj as ArrayList;
                    if (list != null && list.Count > 0 && list[0] != null)
                    {
                        candidate = list[0].ToString();
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(candidate))
            {
                candidate = GetString(root, "cwd");
            }
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return "Antigravity";
            }

            candidate = candidate.TrimEnd('\\', '/');
            int idx = candidate.LastIndexOfAny(new[] { '\\', '/' });
            if (idx >= 0 && idx + 1 < candidate.Length)
            {
                return candidate.Substring(idx + 1);
            }
            return candidate;
        }

        private static string ExtractQuestionSummary(Dictionary<string, object> root)
        {
            object tcObj;
            if (!root.TryGetValue("toolCall", out tcObj)) return "";
            var tc = tcObj as Dictionary<string, object>;
            if (tc == null) return "";

            object argsObj;
            if (!tc.TryGetValue("args", out argsObj)) return "";
            var args = argsObj as Dictionary<string, object>;
            if (args == null) return "";

            object qObj;
            if (!args.TryGetValue("questions", out qObj)) return "";

            IEnumerable enumerable = qObj as IEnumerable;
            if (enumerable == null) return "";

            var parts = new List<string>();
            foreach (object item in enumerable)
            {
                var qDict = item as Dictionary<string, object>;
                if (qDict != null)
                {
                    string qText = CleanSnippet(GetString(qDict, "question"), 96);
                    if (!string.IsNullOrEmpty(qText))
                    {
                        parts.Add(qText);
                    }
                }
                if (parts.Count >= 2) break;
            }
            return string.Join(" / ", parts.ToArray());
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            object val;
            if (dict != null && dict.TryGetValue(key, out val) && val != null)
            {
                return val.ToString();
            }
            return "";
        }

        private static string CleanSnippet(string text, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
            while (normalized.Contains("  "))
            {
                normalized = normalized.Replace("  ", " ");
            }
            if (normalized.Length > maxLen)
            {
                return normalized.Substring(0, maxLen - 1) + "…";
            }
            return normalized;
        }
    }
}
