using UnityEngine;
using TMPro;
using UnityEngine.Networking;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using Platformer.Mechanics;

public class LLMChat : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField inputField;
    public TMP_Text responseText;

    [Header("API")]
    public string apiKey;
    public string apiUrl = "https://api.cerebras.ai/v1/chat/completions";
    public string model = "llama3.1-8b";

    [Header("Game References")]
    public PlayerController[] players;
    public VirtualPlayerInput virtualInput;

    private readonly List<string> messages = new List<string>();

    void Start()
    {
        if (players == null || players.Length == 0)
            players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);

        inputField.onSubmit.AddListener(_ => SendMessageToLLM());

        messages.Add(Msg("system",
            "You are an AI controlling a player in a 2D platformer. " +
            "You can move left/right and jump using the provided tools. " +
            "Use get_player_info to observe the game state. " +
            "Movement persists until you change it — call move with 0 to stop. " +
            "Always respond to the user with a short text message after using tools."));
    }

    public void SendMessageToLLM()
    {
        string prompt = inputField.text;
        if (string.IsNullOrWhiteSpace(prompt)) return;

        messages.Add(Msg("user", prompt));
        responseText.text = "Thinking...";
        StartCoroutine(ChatLoop());

        inputField.text = "";
        inputField.ActivateInputField();
    }

    IEnumerator ChatLoop()
    {
        const int maxRoundtrips = 10;
        for (int i = 0; i < maxRoundtrips; i++)
        {
            var request = new UnityWebRequest(apiUrl, "POST");
            byte[] body = Encoding.UTF8.GetBytes(BuildRequestJson());
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                responseText.text = "Error: " + request.error + "\n" + request.downloadHandler.text;
                yield break;
            }

            string json = request.downloadHandler.text;

            if (TryExtractToolCalls(json, out var toolCalls))
            {
                messages.Add(BuildAssistantToolCallMsg(toolCalls));
                foreach (var tc in toolCalls)
                {
                    string result = ExecuteTool(tc.name, tc.arguments);
                    messages.Add(ToolResultMsg(tc.id, result));
                }
                responseText.text = "Using tools...";
                continue;
            }

            string content = ExtractContent(json);

            // Fallback: model put tool call as plain text in content — execute and stop
            if (TryParseTextToolCall(content, out string toolName, out string toolArgs, out string remainder))
            {
                messages.Add(Msg("assistant", content));
                ExecuteTool(toolName, toolArgs);
                responseText.text = string.IsNullOrWhiteSpace(remainder) ? "Done." : remainder.Trim();
                yield break;
            }

            messages.Add(Msg("assistant", content));
            responseText.text = content;
            yield break;
        }

        responseText.text += "\n(max tool rounds reached)";
    }

    // --- Request building ---

    string BuildRequestJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\"model\":\"").Append(model).Append("\",");
        sb.Append("\"messages\":[").Append(string.Join(",", messages)).Append("],");
        sb.Append("\"tools\":").Append(ToolDefinitions()).Append(',');
        sb.Append("\"parallel_tool_calls\":false");
        sb.Append("}");
        return sb.ToString();
    }

    static string ToolDefinitions()
    {
        return @"[
{""type"":""function"",""function"":{
    ""name"":""move"",
    ""strict"":true,
    ""description"":""Move the controlled player horizontally for a duration then stop."",
    ""parameters"":{""type"":""object"",""properties"":{
        ""direction"":{""type"":""number"",""description"":""-1 for left, 0 to stop, 1 for right""},
        ""duration"":{""type"":""number"",""description"":""Seconds to move (default 0.5, max 5.0)""}
    },""required"":[""direction""],""additionalProperties"":false}
}},
{""type"":""function"",""function"":{
    ""name"":""jump"",
    ""strict"":true,
    ""description"":""Make the controlled player jump. Only works when grounded."",
    ""parameters"":{""type"":""object"",""properties"":{
        ""hold_duration"":{""type"":""number"",""description"":""Seconds to hold jump for height control (default 0.3, max 1.0)""}
    },""required"":[],""additionalProperties"":false}
}},
{""type"":""function"",""function"":{
    ""name"":""get_player_info"",
    ""strict"":true,
    ""description"":""Get a player's position, health status, speed, and jump power"",
    ""parameters"":{""type"":""object"",""properties"":{
        ""player_index"":{""type"":""integer"",""description"":""Player index (0 or 1)""}
    },""required"":[""player_index""],""additionalProperties"":false}
}}
]";
    }

    // --- Tool execution ---

    string ExecuteTool(string name, string arguments)
    {
        var args = JsonUtility.FromJson<ToolArgs>(arguments);

        switch (name)
        {
            case "move":
                if (virtualInput == null) return "No virtual input assigned";
                float dur = Mathf.Clamp(args.duration > 0 ? args.duration : 0.5f, 0.1f, 5f);
                virtualInput.SetMove(args.direction, dur);
                return "Moving " + (args.direction < 0 ? "left" : args.direction > 0 ? "right" : "stopped") + " for " + dur + "s";

            case "jump":
                if (virtualInput == null) return "No virtual input assigned";
                virtualInput.Jump();
                return "Jumping";

            case "get_player_info":
                if (args.player_index < 0 || args.player_index >= players.Length)
                    return "Invalid player_index. Valid range: 0 to " + (players.Length - 1);
                var player = players[args.player_index];
                var pos = player.transform.position;
                return string.Format(
                    "Position: ({0:F1}, {1:F1}), Alive: {2}, Speed: {3}, Jump Power: {4}, Grounded: {5}",
                    pos.x, pos.y, player.health.IsAlive, player.maxSpeed, player.jumpTakeOffSpeed, player.IsGrounded);

            default:
                return "Unknown tool: " + name;
        }
    }

    // --- Response parsing (manual, JsonUtility can't handle nested tool_calls) ---

    struct ParsedToolCall
    {
        public string id, name, arguments;
    }

    bool TryExtractToolCalls(string json, out List<ParsedToolCall> calls)
    {
        calls = new List<ParsedToolCall>();

        int idx = json.IndexOf("\"tool_calls\"");
        if (idx == -1) return false;

        int arrStart = json.IndexOf('[', idx);
        if (arrStart == -1) return false;
        int arrEnd = FindMatchingBracket(json, arrStart, '[', ']');
        if (arrEnd == -1) return false;

        string arr = json.Substring(arrStart, arrEnd - arrStart + 1);
        int pos = 0;
        while (true)
        {
            int objStart = arr.IndexOf('{', pos);
            if (objStart == -1) break;
            int objEnd = FindMatchingBracket(arr, objStart, '{', '}');
            if (objEnd == -1) break;

            string obj = arr.Substring(objStart, objEnd - objStart + 1);
            string id = ExtractStringField(obj, "id");
            string name = ExtractStringField(obj, "name");
            string arguments = ExtractArgumentsField(obj);

            if (!string.IsNullOrEmpty(name))
                calls.Add(new ParsedToolCall { id = id ?? ("call_" + calls.Count), name = name, arguments = arguments ?? "{}" });

            pos = objEnd + 1;
        }
        return calls.Count > 0;
    }

    bool TryParseTextToolCall(string content, out string name, out string arguments, out string remainder)
    {
        name = null;
        arguments = null;
        remainder = null;
        if (string.IsNullOrEmpty(content)) return false;

        // Find a JSON object with "name" field in the content
        int braceStart = content.IndexOf('{');
        if (braceStart == -1 || !content.Contains("\"name\"")) return false;

        int braceEnd = FindMatchingBracket(content, braceStart, '{', '}');
        if (braceEnd == -1) return false;

        string obj = content.Substring(braceStart, braceEnd - braceStart + 1);
        name = ExtractStringField(obj, "name");
        if (string.IsNullOrEmpty(name)) return false;

        arguments = ExtractArgumentsField(obj) ?? "{}";
        remainder = content.Substring(braceEnd + 1);
        return true;
    }

    string ExtractContent(string json)
    {
        int idx = json.IndexOf("\"content\"");
        if (idx == -1) return "";
        int colon = json.IndexOf(':', idx + 9);
        if (colon == -1) return "";

        int c = colon + 1;
        while (c < json.Length && json[c] == ' ') c++;
        if (c >= json.Length || json[c] != '"') return "";

        int start = c + 1;
        int end = FindClosingQuote(json, start);
        return end == -1 ? "" : Unescape(json.Substring(start, end - start));
    }

    string ExtractStringField(string json, string field)
    {
        string marker = "\"" + field + "\"";
        int idx = json.IndexOf(marker);
        if (idx == -1) return null;
        int colon = json.IndexOf(':', idx + marker.Length);
        if (colon == -1) return null;
        int qStart = json.IndexOf('"', colon + 1);
        if (qStart == -1) return null;
        int qEnd = FindClosingQuote(json, qStart + 1);
        return qEnd == -1 ? null : Unescape(json.Substring(qStart + 1, qEnd - qStart - 1));
    }

    string ExtractArgumentsField(string json)
    {
        string marker = "\"arguments\"";
        int idx = json.IndexOf(marker);
        if (idx == -1) return null;
        int colon = json.IndexOf(':', idx + marker.Length);
        if (colon == -1) return null;

        int c = colon + 1;
        while (c < json.Length && json[c] == ' ') c++;
        if (c >= json.Length) return null;

        if (json[c] == '{')
        {
            int end = FindMatchingBracket(json, c, '{', '}');
            return end == -1 ? null : json.Substring(c, end - c + 1);
        }
        if (json[c] == '"')
        {
            int start = c + 1;
            int end = FindClosingQuote(json, start);
            return end == -1 ? null : Unescape(json.Substring(start, end - start));
        }
        return null;
    }

    int FindClosingQuote(string s, int start)
    {
        for (int i = start; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '"') return i;
        }
        return -1;
    }

    int FindMatchingBracket(string s, int start, char open, char close)
    {
        int depth = 0;
        bool inStr = false;
        for (int i = start; i < s.Length; i++)
        {
            if (s[i] == '\\' && inStr) { i++; continue; }
            if (s[i] == '"') { inStr = !inStr; continue; }
            if (inStr) continue;
            if (s[i] == open) depth++;
            else if (s[i] == close) depth--;
            if (depth == 0) return i;
        }
        return -1;
    }

    string Unescape(string s)
    {
        return s.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    // --- JSON message helpers ---

    string Msg(string role, string content)
    {
        return "{\"role\":\"" + role + "\",\"content\":\"" + Escape(content) + "\"}";
    }

    string BuildAssistantToolCallMsg(List<ParsedToolCall> toolCalls)
    {
        var sb = new StringBuilder();
        sb.Append("{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[");
        for (int i = 0; i < toolCalls.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var tc = toolCalls[i];
            sb.Append("{\"id\":\"").Append(tc.id).Append("\",");
            sb.Append("\"type\":\"function\",");
            sb.Append("\"function\":{\"name\":\"").Append(tc.name).Append("\",");
            sb.Append("\"arguments\":\"").Append(Escape(tc.arguments)).Append("\"}}");
        }
        sb.Append("]}");
        return sb.ToString();
    }

    string ToolResultMsg(string toolCallId, string content)
    {
        return "{\"role\":\"tool\",\"tool_call_id\":\"" + toolCallId + "\",\"content\":\"" + Escape(content) + "\"}";
    }

    string Escape(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    [System.Serializable]
    private class ToolArgs { public int player_index; public float direction; public float duration; public float hold_duration; }
}
