using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System;
using System.Collections.Generic;

public class BlockManager : MonoBehaviour
{
    [System.Serializable]
    public class PrefabMapping
    {
        public string name;
        public GameObject prefab;
    }

    public static BlockManager Instance { get; private set; }

    [Tooltip("Maximum allowed blocks to prevent performance issues and abuse.")]
    public int maxBlocks = 50;

    [Tooltip("Default material to apply to procedural blocks if none is specified.")]
    public Material defaultProceduralMaterial;

    public List<PrefabMapping> prefabList;
    private Dictionary<string, GameObject> prefabDict;

    private int currentBlockCount = 0;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // 🔥 INIT PREFAB DICTIONARY
        prefabDict = new Dictionary<string, GameObject>();

        foreach (var item in prefabList)
        {
            prefabDict[item.name.ToLower()] = item.prefab;
        }
    }

    public bool CanPlaceBlock()
    {
        return currentBlockCount < maxBlocks;
    }

    public void PlaceBlockAsync(SingleModificationRequest requestData, Action<string> onSuccess, Action<string> onError)
    {
        if (!CanPlaceBlock())
        {
            onError?.Invoke("Max blocks reached! Cannot place any more.");
            return;
        }

        StartCoroutine(DownloadAndSpawnRoutine(requestData, onSuccess, onError));
    }

    private IEnumerator DownloadAndSpawnRoutine(SingleModificationRequest req, Action<string> onSuccess, Action<string> onError)
    {
        if (req.isLadder && req.position != null)
        {
            // Always snap to the left face of the nearest RIGHTWARD wall.
            int groundMask = LayerMask.GetMask("Ground");
            if (groundMask == 0) groundMask = Physics2D.AllLayers; // fallback

            Vector2 startPos = new Vector2(req.position.x, req.position.y);
            // Cast only to the RIGHT — we want to hug the right wall on its left side
            RaycastHit2D hitRight = Physics2D.Raycast(startPos, Vector2.right, 10f, groundMask);

            if (hitRight.collider != null)
            {
                // Pull ladder 1.0 unit left of the wall face so it doesn't clip inside the wall.
                req.position.x = hitRight.point.x - 1.0f;
            }

            // Now snap the ladder BASE to the floor below.
            // A fallback ladder is 3 units tall, so its center needs to be 1.5 units above the floor.
            float ladderHalfHeight = 1.5f;
            Vector2 downOrigin = new Vector2(req.position.x, req.position.y + 1f); // cast from slightly above player Y
            RaycastHit2D floorHit = Physics2D.Raycast(downOrigin, Vector2.down, 20f);
            if (floorHit.collider != null)
            {
                req.position.y = floorHit.point.y + ladderHalfHeight;
            }
        }

        string searchTerm = string.IsNullOrEmpty(req.requestedSpawnName) ? "cube" : req.requestedSpawnName.ToLower().Replace(" ", "-");
        string name = req.requestedSpawnName.ToLower();

        // 🔥 1. TRY PREFAB FIRST
        foreach (var key in prefabDict.Keys)
        {
            if (name.Contains(key))
            {
                GameObject obj = Instantiate(prefabDict[key]);

                obj.transform.position = new Vector3(req.position.x, req.position.y, 0);

                SetupSpawnedObject(obj, req);

                currentBlockCount++;
                onSuccess?.Invoke("");
                yield break;
            }
        }
        // Procedural intercept for specific dynamic objects: skip images, build natively
        if (searchTerm == "spring" || searchTerm == "trampoline" || searchTerm == "water" || searchTerm == "conveyor"
            || searchTerm == "vine" || searchTerm == "rope" || searchTerm == "zipline")
        {
            if (searchTerm == "spring" || searchTerm == "trampoline")
            {
                req.shape = "halfslab";
                if (string.IsNullOrEmpty(req.color)) req.color = "#FED439"; // Yellow 
            }
            else if (searchTerm == "water")
            {
                req.shape = "cube";
                if (string.IsNullOrEmpty(req.color)) req.color = "#00BFFF"; // Blue
            }
            else if (searchTerm == "conveyor")
            {
                req.shape = "halfslab";
                if (string.IsNullOrEmpty(req.color)) req.color = "#808080"; // Gray
            }
            else if (searchTerm == "vine")
            {
                req.shape = "cube";
                req.isLadder = true; // vines are climbable!
                if (string.IsNullOrEmpty(req.color)) req.color = "#228B22"; // Forest Green
            }
            else if (searchTerm == "rope")
            {
                req.shape = "cylinder";
                req.isLadder = true; // ropes are climbable!
                if (string.IsNullOrEmpty(req.color)) req.color = "#C19A6B"; // Tan/Rope color
            }
            else if (searchTerm == "zipline")
            {
                req.shape = "halfslab";
                req.isConveyor = true; // ziplines push you sideways
                if (string.IsNullOrEmpty(req.color)) req.color = "#B0C4DE"; // Steel Blue
            }
            SpawnFallbackShape(req, onSuccess);
            yield break;
        }

        // 1. First, check if the user asked for a predefined interactive Unity component (like Ladder, Spring, Spike, Checkpoint)
        GameObject interactivePrefab = Resources.Load<GameObject>($"Interactive/{req.requestedSpawnName}");
        if (interactivePrefab != null)
        {
            Vector3 spawnPos = req.position != null ? new Vector3(req.position.x, req.position.y, 0f) : Vector3.zero;
            GameObject spawnedInteractive = Instantiate(interactivePrefab, spawnPos, Quaternion.identity);
            spawnedInteractive.name = $"AI_{req.requestedSpawnName}";

            // Still allow the AI to scale or color the interactive prefab if it has a DynamicObstacle script
            DynamicObstacle dynObs = spawnedInteractive.GetComponent<DynamicObstacle>();
            if (dynObs == null) dynObs = spawnedInteractive.AddComponent<DynamicObstacle>();
            dynObs.ApplyModifications(JsonUtility.ToJson(req));

            currentBlockCount++;
            onSuccess?.Invoke("");
            yield break;
        }

        // 2. If it's not a special interactive component, fallback to grabbing a 2D Sprite from the Icons8 CDN
        // Upgraded from "color" to "fluency" style for more realistic, 3D-like visuals
        string url = $"https://img.icons8.com/fluency/256/{searchTerm}.png";

        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.Log($"Failed to download sprite '{searchTerm}' from Icons8. Falling back to primitive shape.");
                SpawnFallbackShape(req, onSuccess);
            }
            else
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(www);
                Sprite newSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);

                GameObject newGameObj = new GameObject($"AI_{searchTerm}");

                Vector3 spawnPosition = req.position != null ? new Vector3(req.position.x, req.position.y, 0f) : Vector3.zero;
                newGameObj.transform.position = spawnPosition;

                SpriteRenderer sr = newGameObj.AddComponent<SpriteRenderer>();
                sr.sprite = newSprite;

                // Add a polygon collider for the visible sprite
                newGameObj.AddComponent<PolygonCollider2D>();

                // Only add a box collider for special interaction cases
                if (req.isLadder || req.isHazard)
                {
                    BoxCollider2D bc = newGameObj.AddComponent<BoxCollider2D>();
                    bc.size = sr.sprite.bounds.size;
                    bc.offset = sr.sprite.bounds.center;
                    bc.size *= 0.95f;

                    if (req.isLadder)
                    {
                        bc.isTrigger = true;
                        try { newGameObj.tag = "Ladder"; } catch (UnityEngine.UnityException) { }
                    }
                }

                DynamicObstacle dynObs = newGameObj.AddComponent<DynamicObstacle>();
                dynObs.ApplyModifications(JsonUtility.ToJson(req));

                // Add physics, drag, and lifetime components
                Rigidbody2D rb = newGameObj.AddComponent<Rigidbody2D>();
                rb.gravityScale = 0f;
                rb.freezeRotation = true;
                rb.isKinematic = true; // keep physics off while object is being dragged
                newGameObj.AddComponent<DraggableObject>();
                newGameObj.AddComponent<BlockLifetime>();
                newGameObj.AddComponent<SnapToGround>();
                newGameObj.AddComponent<SnapToEdge>();

                // Push object up until it's no longer inside another collider
                NudgeOutOfOverlap(newGameObj);

                currentBlockCount++;
                onSuccess?.Invoke("");
            }
        }
    }
    void SetupSpawnedObject(GameObject obj, SingleModificationRequest req)
    {
        // Collider safety
        if (obj.GetComponent<Collider2D>() == null)
            obj.AddComponent<BoxCollider2D>();

        // Rigidbody
        Rigidbody2D rb = obj.GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = obj.AddComponent<Rigidbody2D>();

        rb.gravityScale = 0;
        rb.freezeRotation = true;
        rb.isKinematic = true;

        // Core systems
        obj.AddComponent<DynamicObstacle>().ApplyModifications(JsonUtility.ToJson(req));
        obj.AddComponent<DraggableObject>();
        obj.AddComponent<BlockLifetime>();
        obj.AddComponent<SnapToGround>();
        obj.AddComponent<SnapToEdge>();

        NudgeOutOfOverlap(obj);
    }
    private void SpawnFallbackShape(SingleModificationRequest req, Action<string> onSuccess)
    {
        PrimitiveType pt = PrimitiveType.Cube;
        bool isHalfSlab = false;

        if (!string.IsNullOrEmpty(req.shape))
        {
            string s = req.shape.ToLower();
            if (s.Contains("sphere")) pt = PrimitiveType.Sphere;
            else if (s.Contains("cylinder")) pt = PrimitiveType.Cylinder;
            else if (s.Contains("capsule")) pt = PrimitiveType.Capsule;
            else if (s.Contains("halfslab") || s.Contains("half-slab") || s.Contains("slab"))
            {
                pt = PrimitiveType.Cube;
                isHalfSlab = true;
            }
        }

        GameObject generatedShape = GameObject.CreatePrimitive(pt);
        Vector3 spawnPosition = req.position != null ? new Vector3(req.position.x, req.position.y, 0f) : Vector3.zero;
        generatedShape.transform.position = spawnPosition;

        if (isHalfSlab)
        {
            // Make it 3 units wide and half-unit tall
            generatedShape.transform.localScale = new Vector3(3f, 0.5f, 1f);
        }
        else if (req.isLadder)
        {
            // Make ladder fallback 3 units tall. Y position is already floor-snapped by the pre-spawn raycast.
            generatedShape.transform.localScale = new Vector3(1f, 3f, 1f);
        }
        else if (req.isWater)
        {
            // Make water a 3x3 pool
            generatedShape.transform.localScale = new Vector3(3f, 3f, 1f);
            generatedShape.transform.position += new Vector3(0, 1f, 0);
        }

        generatedShape.name = $"AI_Fallback_{pt}";

        // Physics cleanup - DestroyImmediate is required here so we don't conflict with the 2D Collider we add on the very next line
        DestroyImmediate(generatedShape.GetComponent<Collider>());
        if (pt == PrimitiveType.Sphere || pt == PrimitiveType.Capsule)
        {
            // Solid collider so player can stand on top
            CircleCollider2D cc = generatedShape.AddComponent<CircleCollider2D>();
            if (req.isLadder)
            {
                // Add a second circle as the trigger zone for climb detection
                CircleCollider2D ccTrigger = generatedShape.AddComponent<CircleCollider2D>();
                ccTrigger.isTrigger = true;
                ccTrigger.radius = cc.radius * 0.9f; // slightly inset so climbing zone is inner
            }
        }
        else
        {
            if (req.isLadder)
            {
                // Pure trigger — player walks through and climbs
                BoxCollider2D triggerBox = generatedShape.AddComponent<BoxCollider2D>();
                triggerBox.isTrigger = true;
            }
            else
            {
                generatedShape.AddComponent<BoxCollider2D>();
            }
        }

        DynamicObstacle dynObs = generatedShape.AddComponent<DynamicObstacle>();
        dynObs.ApplyModifications(JsonUtility.ToJson(req));

        // Add physics, drag, and lifetime components
        generatedShape.AddComponent<Rigidbody2D>();
        Rigidbody2D rb = generatedShape.GetComponent<Rigidbody2D>();

        rb.gravityScale = 0f; // optional (prevents falling during drag)
        rb.freezeRotation = true; //  IMPORTANT
        rb.isKinematic = true; // draggable state: disable physics response
        generatedShape.AddComponent<DraggableObject>();
        generatedShape.AddComponent<BlockLifetime>();
        generatedShape.AddComponent<SnapToGround>();
        generatedShape.AddComponent<SnapToEdge>();

        // Push object up until it's no longer inside another collider
        NudgeOutOfOverlap(generatedShape);

        currentBlockCount++;
        onSuccess?.Invoke("");
    }

    /// <summary>
    /// After spawning, incrementally pushes the object upward until its footprint
    /// no longer overlaps any other collider. Prevents objects spawning inside each other.
    /// </summary>
    private void NudgeOutOfOverlap(GameObject obj)
    {
        Collider2D col = obj.GetComponent<Collider2D>();
        if (col == null) return;

        int maxAttempts = 20;
        float nudgeStep = 0.25f;

        for (int i = 0; i < maxAttempts; i++)
        {
            // Get the axis-aligned bounds of this object's collider
            Bounds b = col.bounds;
            Vector2 center = new Vector2(b.center.x, b.center.y);
            Vector2 halfExtents = new Vector2(b.extents.x * 0.9f, b.extents.y * 0.9f); // slightly inset so edge-touching is OK

            // Check for any overlapping collider that ISN'T this object itself
            Collider2D hit = Physics2D.OverlapBox(center, halfExtents * 2f, 0f);
            if (hit == null || hit.gameObject == obj)
                break; // we're clear!

            // Still overlapping — nudge up
            obj.transform.position += new Vector3(0f, nudgeStep, 0f);
        }
    }
}
