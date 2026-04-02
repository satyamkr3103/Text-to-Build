using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Tilemaps;
using TMPro; // Ensure TextMeshPro is imported in your project
using System.Collections.Generic;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }
    public VoiceInputManager voiceManager;
    [Header("UI References")]
    public TMP_InputField promptInput;
    public Button submitButton;
    public Slider energySlider;
    public TMP_Text statusText;

    [Header("System References")]
    public EnergySystem energySystem;
    public PromptGuardrails guardrails;
    public GroqAILogicController aiController;
    public Transform playerTransform; // Assign the Player GameObject in Inspector

    [Header("Runtime State")]
    public List<DynamicObstacle> targetObstacles = new List<DynamicObstacle>();

    // private float lastCalculatedCost = 0f;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        voiceManager.OnSpeechRecognized += HandleVoiceInput;
        submitButton.onClick.AddListener(OnSubmitClicked);
        energySystem.OnEnergyChanged += UpdateEnergyUI;
        statusText.text = "Ready. Click obstacles to select them.";
    }
    void HandleVoiceInput(string text)
    {
        promptInput.text = text;
        OnSubmitClicked();
    }
    public void ToggleTargetObstacle(DynamicObstacle target)
    {
        if (targetObstacles.Contains(target))
        {
            targetObstacles.Remove(target);
            ShowStatus($"Deselected: {target.gameObject.name}. {targetObstacles.Count} total selected.", Color.white);
        }
        else
        {
            targetObstacles.Add(target);
            ShowStatus($"Selected: {target.gameObject.name}. {targetObstacles.Count} total selected.", Color.white);
        }
    }

    void UpdateEnergyUI(float normalizedEnergy)
    {
        energySlider.value = normalizedEnergy;
    }

    void OnSubmitClicked()
    {
        string prompt = promptInput.text;

        // 1. Guardrails
        if (!guardrails.ValidatePrompt(prompt, out string errorMsg))
        {
            ShowStatus(errorMsg, Color.red);
            return;
        }

        // 3. Send to AI
        ShowStatus("Processing with AI...", Color.yellow);
        submitButton.interactable = false;

        string playerPosString = playerTransform != null ? playerTransform.position.ToString() : "(0,0,0)";

        // Scan the environment to dynamically find placement coordinates
        string envInfo = "No clear environment data detected.";
        if (playerTransform != null)
        {
            float playerX = playerTransform.position.x;
            float playerY = playerTransform.position.y;

            // Determine scan direction from player facing --- uses PlayerMovement if available
            float facingDir = 1f; // default: scan right
            PlayerMovement pm = playerTransform.GetComponent<PlayerMovement>();
            if (pm != null) facingDir = pm.FacingDirection;

            float nearestGapX = -999f;
            float nearestSafeX = -999f;

            // Scan in the player's facing direction from 1.5 to 10 units out
            for (float offset = 1.5f; offset <= 10f; offset += 0.5f)
            {
                float candidateX = playerX + facingDir * offset;
                Vector2 candidatePos = new Vector2(candidateX, playerY);

                // === WALL OCCLUSION CHECK ===
                // Cast a horizontal ray from the player toward the candidate position.
                // If it hits something before reaching candidateX, there's a wall in the way — skip this slot!
                float distToCandidate = Mathf.Abs(offset);
                RaycastHit2D wallHit = Physics2D.Raycast(
                    new Vector2(playerX, playerY),
                    new Vector2(facingDir, 0f),
                    distToCandidate - 0.5f // stop just before the candidate
                );
                if (wallHit.collider != null && wallHit.collider.transform != playerTransform)
                {
                    // Wall is blocking this position — stop scanning further, wall is in the way
                    break;
                }

                // === GAP DETECTION (downward raycast) ===
                RaycastHit2D downHit = Physics2D.Raycast(candidatePos, Vector2.down, 3f);

                if (downHit.collider == null && nearestGapX == -999f)
                {
                    nearestGapX = candidateX;
                }

                // === SAFE OPEN SPACE (ground present, space above is empty) ===
                if (downHit.collider != null && nearestSafeX == -999f)
                {
                    Collider2D overlap = Physics2D.OverlapCircle(candidatePos, 0.4f);
                    if (overlap == null || overlap.transform == playerTransform)
                    {
                        nearestSafeX = candidateX;
                    }
                }
            }

            envInfo = $"Player is facing {'r' + (facingDir > 0 ? "ight" : "left")}. Environment Scan (in facing direction): ";
            if (nearestGapX != -999f) envInfo += $"Nearest Gap is exactly at X={nearestGapX:F2}. ";
            if (nearestSafeX != -999f) envInfo += $"Nearest Safe Open Space (in front of player, no wall blocking it) is exactly at X={nearestSafeX:F2}. ";
            if (nearestGapX == -999f && nearestSafeX == -999f)
                envInfo += $"No clear space found in front. Default to placing at X={playerX + facingDir * 2f:F2}.";
        }

        string contextMsg = $"No block selected. Player wants to place a new block. Player is at {playerPosString}. {envInfo}";

        targetObstacles.RemoveAll(obj => obj == null);

        if (targetObstacles.Count > 0)
        {
            contextMsg = $"Player has selected {targetObstacles.Count} block(s). First block is at {targetObstacles[0].transform.position}. Player is at {playerPosString}. {envInfo}";
        }

        aiController.SendPrompt(prompt, contextMsg, OnAIResponseSuccess, OnAIResponseError);
    }

    void OnAIResponseSuccess(string jsonResponse)
    {
        promptInput.text = ""; // clear input

        try
        {
            AIModificationRequest batchData = JsonUtility.FromJson<AIModificationRequest>(jsonResponse);

            if (energySystem == null)
            {
                ShowStatus("EnergySystem reference is missing in UIManager!", Color.red);
                submitButton.interactable = true;
                return;
            }

            // Check if player has enough energy for the LLM's demanded total batch cost
            if (energySystem.currentEnergy < batchData.totalEnergyCost)
            {
                ShowStatus($"AI determined this costs {batchData.totalEnergyCost}E! You only have {Mathf.FloorToInt(energySystem.currentEnergy)}E.", Color.red);
                submitButton.interactable = true;
                return;
            }

            // Consume the LLM's determined batch cost
            energySystem.ConsumeEnergy(batchData.totalEnergyCost);

            if (guardrails == null)
            {
                ShowStatus("PromptGuardrails reference is missing in UIManager!", Color.red);
                submitButton.interactable = true;
                return;
            }

            if (batchData.items == null || batchData.items.Count == 0)
            {
                ShowStatus("AI did not return any valid items to process.", Color.red);
                submitButton.interactable = true;
                return;
            }

            int itemsProcessed = 0;
            int totalItems = batchData.items.Count;
            int totalRemovedThisBatch = 0;

            targetObstacles.RemoveAll(obj => obj == null);
            bool hadTargetObstacles = targetObstacles.Count > 0;

            ShowStatus($"Processing {totalItems} items... (-{batchData.totalEnergyCost} Energy)", Color.yellow);

            float spacing = 1.5f;

            for (int i = 0; i < batchData.items.Count; i++)
            {
                var modData = batchData.items[i];

                if (modData.position != null)
                {
                    modData.position.x += i * spacing; // spread objects
                }
            }

            foreach (SingleModificationRequest modData in batchData.items)
            {
                // Constrain positions to the play area for each item
                guardrails.ClampRequestPosition(modData);

                if (modData.action == "place")
                {
                    if (BlockManager.Instance == null)
                    {
                        ShowStatus("⚠ BlockManager is missing! Cannot place blocks.", Color.red);
                        continue;
                    }

                    // Pre-flight position validation
                    if (modData.position != null)
                    {
                        Vector2 spawnPos = new Vector2(modData.position.x, modData.position.y);
                        string posError = guardrails.ValidateSpawnPosition(spawnPos, playerTransform);
                        if (posError != null)
                        {
                            // Adjust Y to PlayerY as a safe fallback instead of rejecting outright
                            modData.position.y = playerTransform != null ? playerTransform.position.y : 0f;
                            Debug.LogWarning($"Spawn position adjusted. Reason: {posError}");
                            ShowStatus($"⚡ Adjusted spawn: {posError}", new Color(1f, 0.65f, 0f)); // orange
                        }
                    }

                    BlockManager.Instance.PlaceBlockAsync(modData,
                        onSuccess: (msg) =>
                        {
                            itemsProcessed++;
                            if (itemsProcessed >= totalItems)
                            {
                                ShowStatus($"✔ Spawned {totalItems} item(s)!  -{batchData.totalEnergyCost} Energy", new Color(0.2f, 0.9f, 0.2f));
                                submitButton.interactable = true;
                            }
                        },
                        onError: (errorMsg) =>
                        {
                            itemsProcessed++;
                            ShowStatus($"⚠ Couldn't place one item: {errorMsg}", Color.red);
                            if (itemsProcessed >= totalItems) submitButton.interactable = true;
                        });
                }
                else if (modData.action == "modify")
                {
                    if (targetObstacles.Count > 0)
                    {
                        string finalJson = JsonUtility.ToJson(modData);
                        foreach (var obs in targetObstacles)
                        {
                            if (obs == null) continue; // prevent crash

                            obs.ApplyModifications(finalJson);
                            // If the AI told us to turn this into a ladder, run the dedicated setup
                            // so it gets the proper dual-collider (solid + trigger) in-place.
                            if (modData.isLadder) obs.ConvertToLadder();
                        }
                    }
                    else
                    {
                        ShowStatus("💡 Tip: Click a block first to select it, then say what to change!", Color.yellow);
                    }

                    itemsProcessed++;
                    if (itemsProcessed >= totalItems)
                    {
                        ShowStatus($"✔ Modified {targetObstacles.Count} block(s)!  -{batchData.totalEnergyCost} Energy", new Color(0.2f, 0.9f, 0.2f));
                        targetObstacles.Clear();
                        submitButton.interactable = true;
                    }
                }
                else if (modData.action == "remove")
                {
                    if (hadTargetObstacles)
                    {
                        if (targetObstacles.Count > 0)
                        {
                            // Remove specifically selected objects
                            foreach (var obs in targetObstacles)
                            {
                                if (obs != null && totalRemovedThisBatch < 3)
                                {
                                    Destroy(obs.gameObject);
                                    totalRemovedThisBatch++;
                                }
                            }
                            targetObstacles.Clear();
                        }
                    }
                    else if (modData.position != null && totalRemovedThisBatch < 3)
                    {
                        // Attempt to find the nearest object to the requested coordinates
                        Vector2 targetPos = new Vector2(modData.position.x, modData.position.y);

                        // Ignore the player's collider using layers or just by checking the attached script
                        Collider2D[] colliders = Physics2D.OverlapCircleAll(targetPos, 2.0f);
                        foreach (var col in colliders)
                        {
                            DynamicObstacle dynObs = col.GetComponent<DynamicObstacle>();
                            if (dynObs != null)
                            {
                                Destroy(dynObs.gameObject);
                                targetObstacles.Clear();
                                totalRemovedThisBatch++;
                                break; // Only remove one per modData entry
                            }
                            else if (col is TilemapCollider2D)
                            {
                                // We hit the static level map! Hacky but effective way to gouge out a piece of the world:
                                Tilemap tilemap = col.GetComponent<Tilemap>();
                                if (tilemap != null)
                                {
                                    // Convert the precise overlap circle target position into the Grid's integer coordinate system
                                    Vector3Int cellPosition = tilemap.WorldToCell(new Vector3(targetPos.x, targetPos.y, 0));

                                    // Make sure there's actually a tile there before counting it as a "removal"
                                    if (tilemap.HasTile(cellPosition))
                                    {
                                        tilemap.SetTile(cellPosition, null); // Erase the tile!
                                        totalRemovedThisBatch++;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    itemsProcessed++;
                    if (itemsProcessed >= totalItems)
                    {
                        ShowStatus($"✔ Removed block(s)!  -{batchData.totalEnergyCost} Energy", new Color(0.2f, 0.9f, 0.2f));
                        submitButton.interactable = true;
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            ShowStatus("Failed to parse AI response.", Color.red);
            submitButton.interactable = true; // Re-enable button on parse error
            Debug.LogError("Parse Error: " + e.Message + "\nJSON: " + jsonResponse);
        }
    }

    void OnAIResponseError(string error)
    {
        submitButton.interactable = true;
        ShowStatus("✕ Couldn't reach the AI. Check your internet / API key and try again.", Color.red);
        Debug.LogError("AI Error: " + error);
    }

    void ShowStatus(string message, Color color)
    {
        statusText.text = message;
        statusText.color = color;
    }
}
