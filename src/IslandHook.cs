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
            string adapter = "antigravity";
            string eventArg = "";
            string positionalPayload = "";

            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--adapter" || args[i] == "-a") && i + 1 < args.Length)
                {
                    adapter = args[++i];
                }
                else if ((args[i] == "--event" || args[i] == "-e") && i + 1 < args.Length)
                {
                    eventArg = args[++i];
                }
                else if (!args[i].StartsWith("-"))
                {
                    positionalPayload = args[i];
                }
            }

            try
            {
                Console.InputEncoding = Encoding.UTF8;
                Console.OutputEncoding = Encoding.UTF8;

                string rawInput = positionalPayload;
                if (string.IsNullOrWhiteSpace(rawInput) && Console.IsInputRedirected)
                {
                    rawInput = Console.In.ReadToEnd();
                }

                if (!string.IsNullOrWhiteSpace(rawInput))
                {
                    string normAdapter = (adapter ?? "antigravity").Trim().ToLowerInvariant();
                    if (normAdapter == "claude" || normAdapter == "claude-code")
                    {
                        ProcessClaudePayload(eventArg, rawInput);
                    }
                    else if (normAdapter == "codex" || normAdapter == "codex-cli")
                    {
                        ProcessCodexPayload(eventArg, rawInput);
                    }
                    else
                    {
                        ProcessAntigravityPayload(eventArg, rawInput);
                    }
                }
            }
            catch
            {
                // Never block or break the host AI agent loop
            }

            WriteSafeResponse(adapter, eventArg);
            return 0;
        }

        private static void WriteSafeResponse(string adapter, string eventArg)
        {
            string normAdapter = (adapter ?? "antigravity").Trim().ToLowerInvariant();
            if (normAdapter == "antigravity")
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
            // Claude Code & Codex CLI: empty stdout + exit 0 ensures side-effect-only behavior
            // and NEVER auto-approves or interferes with PermissionRequest / Stop dialogs.
        }

        // ==================== Antigravity ====================

        private static void ProcessAntigravityPayload(string eventArg, string rawJson)
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

            WriteInboxEvent(serializer, new Dictionary<string, object>
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
            });
        }

        // ==================== Claude Code ====================

        private static void ProcessClaudePayload(string eventArg, string rawJson)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            var root = serializer.DeserializeObject(rawJson) as Dictionary<string, object>;
            if (root == null) return;

            string ev = !string.IsNullOrEmpty(eventArg) ? eventArg : GetString(root, "hook_event_name");
            string sessionId = GetString(root, "session_id");
            if (string.IsNullOrEmpty(sessionId)) sessionId = "default";
            string convId = "cc-" + sessionId;
            string project = ExtractLeafName(GetString(root, "cwd"), "ClaudeCode");

            string kind = null;
            string title = "";
            string summary = "";

            if (string.Equals(ev, "UserPromptSubmit", StringComparison.OrdinalIgnoreCase))
            {
                kind = "working";
                title = "思考中";
                string prompt = CleanSnippet(GetString(root, "prompt"), 80);
                summary = string.IsNullOrEmpty(prompt) ? "Claude Code 正在执行任务" : prompt;
            }
            else if (string.Equals(ev, "PermissionRequest", StringComparison.OrdinalIgnoreCase))
            {
                kind = "needs_approval";
                title = "待批准权限";
                string toolName = GetString(root, "tool_name");
                string detail = ExtractToolInputBrief(root);
                if (!string.IsNullOrEmpty(toolName) && !string.IsNullOrEmpty(detail))
                    summary = toolName + ": " + detail;
                else if (!string.IsNullOrEmpty(toolName))
                    summary = "请求批准工具调用：" + toolName;
                else
                    summary = "Claude Code 等待你的权限批准";
            }
            else if (string.Equals(ev, "Notification", StringComparison.OrdinalIgnoreCase))
            {
                string ntype = GetString(root, "notification_type");
                string msg = CleanSnippet(GetString(root, "message"), 100);
                if (string.Equals(ntype, "permission_prompt", StringComparison.OrdinalIgnoreCase))
                {
                    kind = "needs_approval";
                    title = "待批准权限";
                }
                else
                {
                    kind = "needs_decision";
                    title = "等你介入";
                }
                summary = !string.IsNullOrEmpty(msg) ? msg : ("Claude Code 通知：" + ntype);
            }
            else if (string.Equals(ev, "StopFailure", StringComparison.OrdinalIgnoreCase))
            {
                kind = "error";
                title = "报错卡住";
                string err = CleanSnippet(GetString(root, "error"), 100);
                summary = !string.IsNullOrEmpty(err) ? err : "Claude Code 执行中断或报错";
            }
            else if (string.Equals(ev, "Stop", StringComparison.OrdinalIgnoreCase))
            {
                if (GetBool(root, "stop_hook_active")) return;
                kind = "needs_reply";
                title = "等你回复";
                string lastMsg = CleanSnippet(GetString(root, "last_assistant_message"), 96);
                summary = !string.IsNullOrEmpty(lastMsg) ? lastMsg : "Claude Code 本轮回复已完成";
            }

            if (string.IsNullOrEmpty(kind)) return;

            WriteInboxEvent(serializer, new Dictionary<string, object>
            {
                { "source", "ClaudeCode" },
                { "kind", kind },
                { "conversationId", convId },
                { "project", project },
                { "title", title },
                { "summary", summary },
                { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            });
        }

        // ==================== Codex CLI (hooks.json stdin + legacy notify argv) ====================

        private static void ProcessCodexPayload(string eventArg, string rawJson)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            var root = serializer.DeserializeObject(rawJson) as Dictionary<string, object>;
            if (root == null) return;

            string ev = !string.IsNullOrEmpty(eventArg) ? eventArg : GetString(root, "hook_event_name");
            string legacyType = GetString(root, "type");
            if (string.IsNullOrEmpty(ev) && !string.IsNullOrEmpty(legacyType)) ev = legacyType;

            string threadId = GetString(root, "session_id");
            if (string.IsNullOrEmpty(threadId)) threadId = GetString(root, "thread-id");
            if (string.IsNullOrEmpty(threadId)) threadId = "default";
            string convId = "codex-" + threadId;

            string project = ExtractLeafName(GetString(root, "cwd"), "Codex");
            string kind = null;
            string title = "";
            string summary = "";

            if (string.Equals(ev, "UserPromptSubmit", StringComparison.OrdinalIgnoreCase))
            {
                kind = "working";
                title = "思考中";
                string prompt = CleanSnippet(GetString(root, "prompt"), 80);
                summary = string.IsNullOrEmpty(prompt) ? "Codex 正在执行任务" : prompt;
            }
            else if (string.Equals(ev, "PermissionRequest", StringComparison.OrdinalIgnoreCase))
            {
                kind = "needs_approval";
                title = "待批准命令";
                string toolName = GetString(root, "tool_name");
                string detail = ExtractToolInputBrief(root);
                if (!string.IsNullOrEmpty(toolName) && !string.IsNullOrEmpty(detail))
                    summary = toolName + ": " + detail;
                else if (!string.IsNullOrEmpty(toolName))
                    summary = "Codex 请求批准：" + toolName;
                else
                    summary = "Codex 等待你的权限批准";
            }
            else if (string.Equals(ev, "Interrupt", StringComparison.OrdinalIgnoreCase))
            {
                kind = "cleared";
                title = "已中断";
                summary = "";
            }
            else if (string.Equals(ev, "Stop", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(ev, "agent-turn-complete", StringComparison.OrdinalIgnoreCase))
            {
                if (GetBool(root, "stop_hook_active")) return;
                kind = "needs_reply";
                title = "等你回复";
                string lastMsg = CleanSnippet(GetString(root, "last-assistant-message"), 96);
                if (string.IsNullOrEmpty(lastMsg)) lastMsg = CleanSnippet(GetString(root, "last_assistant_message"), 96);
                summary = !string.IsNullOrEmpty(lastMsg) ? lastMsg : "Codex 本轮任务已完成，等你下一步指示";
            }

            if (string.IsNullOrEmpty(kind)) return;

            WriteInboxEvent(serializer, new Dictionary<string, object>
            {
                { "source", "Codex" },
                { "kind", kind },
                { "conversationId", convId },
                { "project", project },
                { "title", title },
                { "summary", summary },
                { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            });
        }

        // ==================== Helpers ====================

        private static void WriteInboxEvent(JavaScriptSerializer serializer, Dictionary<string, object> evt)
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

            string fileName = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string tmpPath = Path.Combine(inboxDir, fileName + ".tmp");
            string finalPath = Path.Combine(inboxDir, fileName + ".json");

            string outJson = serializer.Serialize(evt);
            File.WriteAllText(tmpPath, outJson, new UTF8Encoding(false));
            File.Move(tmpPath, finalPath);
        }

        private static string ExtractToolInputBrief(Dictionary<string, object> root)
        {
            object tiObj;
            if (!root.TryGetValue("tool_input", out tiObj)) return "";
            var ti = tiObj as Dictionary<string, object>;
            if (ti == null) return CleanSnippet(tiObj != null ? tiObj.ToString() : "", 72);

            string[] keys = new[] { "command", "cmd", "file_path", "path", "description", "url" };
            foreach (string k in keys)
            {
                string v = CleanSnippet(GetString(ti, k), 72);
                if (!string.IsNullOrEmpty(v)) return v;
            }
            return "";
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
            return ExtractLeafName(candidate, "Antigravity");
        }

        private static string ExtractLeafName(string path, string fallback)
        {
            if (string.IsNullOrWhiteSpace(path)) return fallback;
            string candidate = path.TrimEnd('\\', '/');
            int idx = candidate.LastIndexOfAny(new[] { '\\', '/' });
            if (idx >= 0 && idx + 1 < candidate.Length)
            {
                return candidate.Substring(idx + 1);
            }
            return string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;
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

        private static bool GetBool(Dictionary<string, object> dict, string key)
        {
            object val;
            if (dict != null && dict.TryGetValue(key, out val) && val != null)
            {
                if (val is bool) return (bool)val;
                bool parsed;
                if (bool.TryParse(val.ToString(), out parsed)) return parsed;
            }
            return false;
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
