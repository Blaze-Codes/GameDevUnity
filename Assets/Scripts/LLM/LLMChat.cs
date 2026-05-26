using UnityEngine;
using TMPro;
using UnityEngine.Networking;
using System.Text;
using System.Collections;

public class LLMChat : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField inputField;
    public TMP_Text responseText;

    [Header("API")]
    public string apiKey;
    public string apiUrl = "https://api.openai.com/v1/chat/completions";

    public void SendMessageToLLM()
    {
        string prompt = inputField.text;

        if (string.IsNullOrWhiteSpace(prompt))
            return;

        StartCoroutine(SendRequest(prompt));

        // 🔥 IMPORTANT: clear immediately
        inputField.text = "";
        inputField.ActivateInputField();
    }

    IEnumerator SendRequest(string prompt)
    {
        // Simple JSON payload (Chat Completions style)
        string json = "{"
            + "\"model\": \"gpt-4o-mini\","
            + "\"messages\": ["
                + "{\"role\": \"user\", \"content\": \"" + Escape(prompt) + "\"}"
            + "]"
        + "}";

        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        UnityWebRequest request = new UnityWebRequest(apiUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();

        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + apiKey);

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            responseText.text = "Error: " + request.error;
        }
        else
        {
            string result = request.downloadHandler.text;
            responseText.text = ExtractMessage(result);
        }
    }

    string ExtractMessage(string json)
    {
        // ultra-simple parsing (NOT robust, but fine for game jam)
        string marker = "\"content\":\"";
        int start = json.IndexOf(marker);
        if (start == -1) return json;

        start += marker.Length;
        int end = json.IndexOf("\"", start);

        if (end == -1) return json;

        return json.Substring(start, end - start);
    }

    string Escape(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // Optional: press Enter to send
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return))
        {
            SendMessageToLLM();
        }
    }
}
