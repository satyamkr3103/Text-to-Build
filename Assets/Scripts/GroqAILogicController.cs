using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System;
using System.Text;

public class GroqAILogicController : MonoBehaviour
{
    [Header("API Settings")]
    [Tooltip("Enter your Groq API Key here")]
    public string groqApiKey = "YOUR_GROQ_API_KEY";
    private string apiUrl = "https://api.groq.com/openai/v1/chat/completions";

    [Header("Model Settings")]
    public string modelName = "llama-3.3-70b-versatile";

    public void SendPrompt(string userPrompt, string contextInfo, Action<string> onSuccess, Action<string> onError)
    {
        StartCoroutine(PostRequest(userPrompt, contextInfo, onSuccess, onError));
    }

    private IEnumerator PostRequest(string prompt, string contextInfo, Action<string> onSuccess, Action<string> onError)
    {
        string systemPrompt = $@"You are a game AI that modifies dynamic obstacles. 
The user can request ANY object to spawn or modify.

If the object is common (box, ladder, platform), use simple shapes.
If the object is complex (dragon, car), still generate a valid object representation.

Always spread multiple items horizontally and ensure valid placement. 
Current Context: {contextInfo}

You must return ONLY a JSON object with the following root fields:
- 'totalEnergyCost': float representing cumulative cost. Big/Complex/Multiple items = High Energy (50-150). Small/Single items = Low Energy (5-15).
- 'items': an array of objects representing the blocks. If the user asks for 10 boxes, this array MUST contain 10 objects. IF REMOVING, cap the array to a maximum of 3 items.

Each item in the 'items' array MUST contain:
- 'action': 'place', 'modify', or 'remove'. If removing, the position 'x' and 'y' must roughly match the target object's location.
- 'requestedSpawnName': EXACT noun (e.g., 'Dragon', 'Car', 'Sword', 'Wall'). If not specified, default to 'GenericBlock'.
- 'shape': closest basic geometric primitive ('cube', 'sphere', 'cylinder', 'capsule').
- 'position': an object with 'x' and 'y' floats. CRITICAL: 
   1. If placing an object to rest ON TOP of the floor (like a box, spring, character): Set X to EXACT 'Nearest Safe Open Space' coordinate. Set Y to PlayerY.
   2. If placing a block to BECOME the floor (extend the base layer, fill a gap, ""in the ground""): Set X to EXACT 'Nearest Gap' coordinate. Set Y to PlayerY - 1.0. 
   3. MULTIPLE ITEMS: If generating more than 1 item, YOU MUST increment the X coordinate for every new item by +1.0 so they spread out horizontally side-by-side. NEVER stack them on the exact same X coordinate.
   FATAL RULE: DO NOT write raw math in the JSON (e.g. NEVER write ""x"": -8 + 3). You MUST output the final float number (e.g. ""x"": -5.0).
- 'color': hex color code (e.g., '#FF0000').
- 'bounciness': float 0.0 to 1.0.
- 'scaleMultiplier': float 0.5 to 10.0.
- 'material': material description (e.g., 'metal', 'wood', 'glass').
- 'isLadder': boolean. Set to true ONLY if user asks for a ladder, vine, rope, or climbable object. CRITICAL: Force requestedSpawnName to 'ladder', 'vine', or 'rope' depending on what they asked for.
- 'isBouncy': boolean. Set to true ONLY if user asks for a spring, trampoline, jump pad, or rubber block. CRITICAL: Force requestedSpawnName to 'trampoline', shape to 'halfslab', and color to '#FFFF00'.
- 'isHazard': boolean. Set to true ONLY if user asks for spikes, lava, fire, trap, acid, or danger. CRITICAL: Force requestedSpawnName to 'fire' or 'spike'.
- 'isConveyor': boolean. Set to true ONLY if user asks for a conveyor belt, treadmill, escalator, zipline, or speed boost. CRITICAL: Force requestedSpawnName to 'conveyor' or 'zipline', shape to 'halfslab'.
- 'isWater': boolean. Set to true ONLY if user asks for water, liquid, pool, or floating zone. CRITICAL: Force requestedSpawnName to 'water', shape to 'cube', and color to '#0000FF'.
Do not include markdown blocks or any other text. Output strict JSON only.";

        string jsonPayload = $@"{{
            ""model"": ""{modelName}"",
            ""messages"": [
                {{
                    ""role"": ""system"",
                    ""content"": ""{systemPrompt.Replace("\r", "").Replace("\n", " ").Replace("\"", "\\\"")}""
                }},
                {{
                    ""role"": ""user"",
                    ""content"": ""{prompt.Replace("\r", "").Replace("\n", " ").Replace("\"", "\\\"")}""
                }}
            ],
            ""temperature"": 0.1,
            ""response_format"": {{ ""type"": ""json_object"" }}
        }}";

        using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + groqApiKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                onError?.Invoke(request.error + "\n" + request.downloadHandler.text);
            }
            else
            {
                try
                {
                    string jsonResponse = request.downloadHandler.text;
                    
                    GroqResponse responseObj = JsonUtility.FromJson<GroqResponse>(jsonResponse);
                    if(responseObj != null && responseObj.choices != null && responseObj.choices.Length > 0)
                    {
                        string content = responseObj.choices[0].message.content;
                        onSuccess?.Invoke(content);
                    }
                    else
                    {
                        onError?.Invoke("Unexpected API response format.");
                    }
                }
                catch(Exception e)
                {
                    onError?.Invoke("Failed to parse response: " + e.Message);
                }
            }
        }
    }

    [Serializable]
    private class GroqResponse
    {
        public Choice[] choices;
    }

    [Serializable]
    private class Choice
    {
        public Message message;
    }

    [Serializable]
    private class Message
    {
        public string content;
    }
}