using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
            string annotationPath = ResolveAntigravityAnnotationPath(transcriptPath, convId);
            string sessionName = ReadAntigravityTitle(annotationPath);
            string lastUserInput = CleanSnippet(GetString(root, "lastUserInput"), 90);

            string kind = null;
            string title = "";
            string summary = "";

            if (string.Equals(eventArg, "PreInvocation", StringComparison.OrdinalIgnoreCase))
            {
                kind = "working";
                title = "思考中";
                summary = !string.IsNullOrEmpty(sessionName)
                    ? ("正在处理「" + sessionName + "」")
                    : "正在执行任务";
            }
            else if (string.Equals(eventArg, "AskQuestion", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(eventArg, "PreToolUse", StringComparison.OrdinalIgnoreCase))
            {
                string toolName = ExtractToolCallName(root);
                if (!string.IsNullOrEmpty(toolName) &&
                    !string.Equals(toolName, "ask_question", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(toolName, "request_user_input", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                kind = "needs_decision";
                title = "等你拍板";
                summary = ExtractQuestionSummary(root);
                if (string.IsNullOrEmpty(summary))
                {
                    summary = "弹出选择卡，需要你的决定";
                }
            }
            else if (string.Equals(eventArg, "Stop", StringComparison.OrdinalIgnoreCase))
            {
                string reason = GetString(root, "terminationReason");
                string err = CleanSnippet(GetString(root, "error"), 115);
                bool hasFullyIdleKey = root.ContainsKey("fullyIdle");
                bool isFullyIdle = !hasFullyIdleKey || GetBool(root, "fullyIdle");
                bool isContinuation = GetBool(root, "continuationActive");

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
                    summary = string.IsNullOrEmpty(err) ? "执行遇到异常，请检查会话" : CleanErrorLine(err, 115);
                }
                else if (!isFullyIdle || isContinuation)
                {
                    // Mid-turn pause waiting for background task / subagent / continuation -> stay L0 silent!
                    kind = "working";
                    title = "后台运行中";
                    summary = !string.IsNullOrEmpty(sessionName)
                        ? ("后台任务执行中「" + sessionName + "」")
                        : "后台任务执行中";
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

                    // Extract what the AI actually said / asked instead of echoing user's own prompt
                    string lastReply = ReadLastAssistantReply(serializer, transcriptPath);
                    string distilled = ExtractDecisionSummary(lastReply, 112);
                    summary = !string.IsNullOrEmpty(distilled)
                        ? distilled
                        : "本轮回复已完成，等你下一步指示";
                }
            }

            if (string.IsNullOrEmpty(kind)) return;

            WriteInboxEvent(serializer, new Dictionary<string, object>
            {
                { "source", "antigravity" },
                { "kind", kind },
                { "level", InferNotificationLevel(kind) },
                { "conversationId", convId },
                { "sessionName", sessionName },
                { "project", project },
                { "title", title },
                { "summary", summary },
                { "lastUserInput", lastUserInput },
                { "transcriptPath", transcriptPath },
                { "annotationPath", annotationPath },
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
                summary = "Claude Code 正在执行任务";
            }
            else if (string.Equals(ev, "PermissionRequest", StringComparison.OrdinalIgnoreCase))
            {
                kind = "needs_approval";
                title = "待批准权限";
                string toolName = GetString(root, "tool_name");
                string detail = ExtractToolInputBrief(root);
                if (!string.IsNullOrEmpty(toolName) && !string.IsNullOrEmpty(detail))
                    summary = "请求批准 " + toolName + "：" + detail;
                else if (!string.IsNullOrEmpty(toolName))
                    summary = "请求批准工具调用：" + toolName;
                else
                    summary = "Claude Code 等待你的权限批准";
            }
            else if (string.Equals(ev, "Notification", StringComparison.OrdinalIgnoreCase))
            {
                string ntype = GetString(root, "notification_type");
                string msg = ExtractDecisionSummary(GetString(root, "message"), 110);
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
                string err = CleanErrorLine(GetString(root, "error"), 110);
                summary = !string.IsNullOrEmpty(err) ? err : "Claude Code 执行中断或报错";
            }
            else if (string.Equals(ev, "Stop", StringComparison.OrdinalIgnoreCase))
            {
                if (GetBool(root, "stop_hook_active")) return;
                kind = "needs_reply";
                title = "等你回复";
                string lastMsg = ExtractDecisionSummary(GetString(root, "last_assistant_message"), 112);
                summary = !string.IsNullOrEmpty(lastMsg) ? lastMsg : "Claude Code 本轮回复已完成";
            }

            if (string.IsNullOrEmpty(kind)) return;

            WriteInboxEvent(serializer, new Dictionary<string, object>
            {
                { "source", "ClaudeCode" },
                { "kind", kind },
                { "level", InferNotificationLevel(kind) },
                { "conversationId", convId },
                { "sessionName", project },
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
                summary = "Codex 正在执行任务";
            }
            else if (string.Equals(ev, "PermissionRequest", StringComparison.OrdinalIgnoreCase))
            {
                kind = "needs_approval";
                title = "待批准命令";
                string toolName = GetString(root, "tool_name");
                string detail = ExtractToolInputBrief(root);
                if (!string.IsNullOrEmpty(toolName) && !string.IsNullOrEmpty(detail))
                    summary = "请求批准 " + toolName + "：" + detail;
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
                string rawLast = GetString(root, "last-assistant-message");
                if (string.IsNullOrEmpty(rawLast)) rawLast = GetString(root, "last_assistant_message");
                string lastMsg = ExtractDecisionSummary(rawLast, 112);
                summary = !string.IsNullOrEmpty(lastMsg) ? lastMsg : "Codex 本轮任务已完成，等你下一步指示";
            }

            if (string.IsNullOrEmpty(kind)) return;

            WriteInboxEvent(serializer, new Dictionary<string, object>
            {
                { "source", "Codex" },
                { "kind", kind },
                { "level", InferNotificationLevel(kind) },
                { "conversationId", convId },
                { "sessionName", project },
                { "project", project },
                { "title", title },
                { "summary", summary },
                { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            });
        }

        // ==================== Antigravity Metadata & Transcript Extraction ====================

        private static string ResolveAntigravityAnnotationPath(string transcriptPath, string convId)
        {
            if (string.IsNullOrWhiteSpace(convId) || convId == "default") return "";

            try
            {
                if (!string.IsNullOrWhiteSpace(transcriptPath))
                {
                    string dir = Path.GetDirectoryName(transcriptPath);
                    for (int depth = 0; depth < 6 && !string.IsNullOrEmpty(dir); depth++)
                    {
                        string name = Path.GetFileName(dir);
                        if (string.Equals(name, "brain", StringComparison.OrdinalIgnoreCase))
                        {
                            string appDataRoot = Path.GetDirectoryName(dir);
                            if (!string.IsNullOrEmpty(appDataRoot))
                            {
                                string candidate = Path.Combine(appDataRoot, "annotations", convId + ".pbtxt");
                                if (File.Exists(candidate)) return candidate;
                            }
                            break;
                        }
                        dir = Path.GetDirectoryName(dir);
                    }
                }
            }
            catch { }

            try
            {
                string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userHome))
                {
                    string fallback = Path.Combine(userHome, ".gemini", "antigravity", "annotations", convId + ".pbtxt");
                    if (File.Exists(fallback)) return fallback;
                }
            }
            catch { }

            return "";
        }

        private static string ReadAntigravityTitle(string annotationPath)
        {
            if (string.IsNullOrWhiteSpace(annotationPath) || !File.Exists(annotationPath)) return "";
            try
            {
                string raw = File.ReadAllText(annotationPath, Encoding.UTF8);
                Match m = Regex.Match(raw, @"title\s*:\s*""((?:\\.|[^""\\])*)""");
                if (m.Success)
                {
                    string decoded = UnescapePbtxtString(m.Groups[1].Value);
                    return CleanSnippet(decoded, 42);
                }
            }
            catch { }
            return "";
        }

        private static string UnescapePbtxtString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var bytes = new List<byte>();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    if (next >= '0' && next <= '7' && i + 3 < s.Length &&
                        s[i + 2] >= '0' && s[i + 2] <= '7' &&
                        s[i + 3] >= '0' && s[i + 3] <= '7')
                    {
                        int oct = ((next - '0') << 6) | ((s[i + 2] - '0') << 3) | (s[i + 3] - '0');
                        bytes.Add((byte)(oct & 0xFF));
                        i += 3;
                        continue;
                    }
                    i++;
                    char c = next;
                    if (next == 'n') c = ' ';
                    else if (next == 'r') c = ' ';
                    else if (next == 't') c = ' ';
                    bytes.AddRange(Encoding.UTF8.GetBytes(new[] { c }));
                }
                else
                {
                    bytes.AddRange(Encoding.UTF8.GetBytes(new[] { s[i] }));
                }
            }
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        private static string ReadLastAssistantReply(JavaScriptSerializer serializer, string transcriptPath)
        {
            if (string.IsNullOrWhiteSpace(transcriptPath) || !File.Exists(transcriptPath)) return "";
            try
            {
                using (var fs = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length == 0) return "";
                    long readLen = Math.Min(fs.Length, 65536);
                    fs.Seek(-readLen, SeekOrigin.End);
                    byte[] buf = new byte[readLen];
                    int n = fs.Read(buf, 0, buf.Length);
                    string chunk = Encoding.UTF8.GetString(buf, 0, n);
                    string[] lines = chunk.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    for (int i = lines.Length - 1; i >= 0; i--)
                    {
                        string t = lines[i].Trim();
                        if (!t.StartsWith("{") || !t.EndsWith("}")) continue;
                        if (t.IndexOf("\"PLANNER_RESPONSE\"", StringComparison.Ordinal) < 0) continue;
                        var obj = serializer.DeserializeObject(t) as Dictionary<string, object>;
                        if (obj == null) continue;
                        if (!string.Equals(GetString(obj, "type"), "PLANNER_RESPONSE", StringComparison.OrdinalIgnoreCase)) continue;
                        string content = GetString(obj, "content");
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            return content;
                        }
                    }
                }
            }
            catch { }
            return "";
        }

        private static string ExtractDecisionSummary(string rawText, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return "";

            string text = rawText;
            // Remove fenced code blocks, embedded artifacts, and HTML tags
            text = Regex.Replace(text, @"```[\s\S]*?```", " ");
            text = Regex.Replace(text, @"<agent-embed[\s\S]*?</agent-embed>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", " ");
            // Convert markdown links [label](url) -> label, strip raw URLs
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");
            text = Regex.Replace(text, @"(?:https?|file)://\S+", "");

            string[] rawLines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var cleanLines = new List<string>();
            foreach (string rawLine in rawLines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                // Skip markdown table dividers and horizontal rules
                if (Regex.IsMatch(line, @"^[\|\-:\s\*_]+$")) continue;
                if (line.StartsWith("|") && line.EndsWith("|")) continue;

                // Strip leading markdown headers, bullets, quotes
                line = Regex.Replace(line, @"^(?:[#>\-\*\+•]+\s*|\d+\.\s+)", "");
                line = line.Replace("**", "").Replace("__", "").Replace("`", "");
                line = Regex.Replace(line, @"\s+", " ").Trim();
                if (line.Length < 4) continue;
                cleanLines.Add(line);
            }

            if (cleanLines.Count == 0) return "";

            // Split into sentences to prioritize direct questions asked to the user
            var sentences = new List<string>();
            foreach (string line in cleanLines)
            {
                string[] parts = Regex.Split(line, @"(?<=[。！？?!])");
                foreach (string part in parts)
                {
                    string s = part.Trim();
                    if (s.Length >= 4) sentences.Add(s);
                }
            }

            // Look backwards for the clearest question directed at the user
            string lastQuestion = "";
            for (int i = sentences.Count - 1; i >= 0; i--)
            {
                string s = sentences[i];
                if ((s.EndsWith("？") || s.EndsWith("?")) && s.Length >= 6)
                {
                    lastQuestion = s;
                    break;
                }
            }

            string firstLine = cleanLines[0];
            if (!string.IsNullOrEmpty(lastQuestion))
            {
                // Lead with what the AI is asking so user can decide immediately
                string prefix = "待你回复：" + lastQuestion;
                if (prefix.Length >= maxLen - 14 || firstLine.IndexOf(lastQuestion, StringComparison.Ordinal) >= 0)
                {
                    return CleanSnippet(prefix, maxLen);
                }
                string combined = prefix + "（" + CleanSnippet(firstLine, Math.Max(18, maxLen - prefix.Length - 2)) + "）";
                return CleanSnippet(combined, maxLen);
            }

            // Otherwise combine leading conclusion sentences
            string joined = firstLine;
            if (joined.Length < 46 && cleanLines.Count > 1)
            {
                joined = joined.TrimEnd('。', '；', ':', '：') + " · " + cleanLines[1];
            }
            return CleanSnippet(joined, maxLen);
        }

        private static string CleanErrorLine(string err, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(err)) return "";
            string[] lines = err.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (l.Length >= 4 && !l.StartsWith("at ")) return CleanSnippet(l, maxLen);
            }
            return CleanSnippet(err, maxLen);
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

        private static int InferNotificationLevel(string kind)
        {
            if (kind == "needs_approval") return 3;
            if (kind == "needs_decision" || kind == "error") return 2;
            if (kind == "needs_reply") return 1;
            return 0;
        }

        private static string ExtractToolCallName(Dictionary<string, object> root)
        {
            object tcObj;
            if (!root.TryGetValue("toolCall", out tcObj)) return "";
            var tc = tcObj as Dictionary<string, object>;
            if (tc == null) return "";
            return GetString(tc, "name");
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
                    string qText = CleanSnippet(GetString(qDict, "question"), 76);
                    if (!string.IsNullOrEmpty(qText))
                    {
                        string optPreview = ExtractOptionPreview(qDict);
                        parts.Add(string.IsNullOrEmpty(optPreview) ? qText : (qText + "（" + optPreview + "）"));
                    }
                }
                if (parts.Count >= 2) break;
            }
            return CleanSnippet(string.Join(" / ", parts.ToArray()), 115);
        }

        private static string ExtractOptionPreview(Dictionary<string, object> qDict)
        {
            object optsObj;
            if (!qDict.TryGetValue("options", out optsObj)) return "";
            IEnumerable opts = optsObj as IEnumerable;
            if (opts == null) return "";

            var labels = new List<string>();
            foreach (object o in opts)
            {
                if (o == null) continue;
                string raw = o.ToString().Trim();
                if (raw.StartsWith("(Recommended)", StringComparison.OrdinalIgnoreCase))
                {
                    raw = raw.Substring("(Recommended)".Length).Trim();
                }
                int dashIdx = raw.IndexOf(" — ", StringComparison.Ordinal);
                if (dashIdx > 0) raw = raw.Substring(0, dashIdx).Trim();
                int colonIdx = raw.IndexOf("：", StringComparison.Ordinal);
                if (colonIdx > 0 && colonIdx <= 18) raw = raw.Substring(0, colonIdx).Trim();
                string shortLabel = CleanSnippet(raw, 16);
                if (!string.IsNullOrEmpty(shortLabel)) labels.Add(shortLabel);
                if (labels.Count >= 3) break;
            }
            return string.Join(" / ", labels.ToArray());
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
