using UnityEngine;

public class DraggableObject : MonoBehaviour
{
    private bool isDragging = true;

    private GameObject ghost;
    private SpriteRenderer ghostRenderer;
    private SpriteRenderer originalRenderer;
    private Rigidbody2D rb;
    private Collider2D ownCollider;

    public Color validColor = new Color(0, 1, 0, 0.5f);   // green transparent
    public Color invalidColor = new Color(1, 0, 0, 0.5f); // red transparent

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        ownCollider = GetComponent<Collider2D>();
        originalRenderer = GetComponent<SpriteRenderer>();

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.freezeRotation = true;
        }

        if (ownCollider != null)
            ownCollider.enabled = false;

        CreateGhost();
    }

    void CreateGhost()
    {
        ghost = new GameObject("GhostPreview");

        ghostRenderer = ghost.AddComponent<SpriteRenderer>();

        if (originalRenderer != null)
        {
            ghostRenderer.sprite = originalRenderer.sprite;
            ghostRenderer.sortingOrder = originalRenderer.sortingOrder + 1;
            originalRenderer.enabled = false; // hide real object while dragging
        }

        ghostRenderer.color = validColor;
    }

    void Update()
    {
        if (!isDragging) return;
        if (Camera.main == null) return;

        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mousePos.z = 0;

        ghost.transform.position = mousePos;

        bool valid = SnapPreview();
        ghostRenderer.color = valid ? validColor : invalidColor;

        // Left click = place object at ghost position
        if (Input.GetMouseButtonDown(0))
        {
            PlaceObject();
        }
    }

    bool SnapPreview()
    {
        Vector3 originalPos = transform.position;
        transform.position = ghost.transform.position;

        SnapToEdge edge = GetComponent<SnapToEdge>();
        if (edge != null)
            edge.Snap();

        SnapToGround ground = GetComponent<SnapToGround>();
        if (ground != null)
        {
            bool snapped = ground.Snap();
            if (!snapped)
            {
                transform.position = originalPos;
                return false;
            }
        }

        ghost.transform.position = transform.position;
        transform.position = originalPos;
        return true;
    }

    void PlaceObject()
    {
        isDragging = false;
        transform.position = ghost.transform.position;

        if (originalRenderer != null)
            originalRenderer.enabled = true;

        if (ownCollider != null)
            ownCollider.enabled = true;

        Destroy(ghost);

        if (rb != null)
            rb.bodyType = RigidbodyType2D.Static;

        BlockLifetime lifetime = GetComponent<BlockLifetime>();
        if (lifetime != null)
            lifetime.StartLifetime();
    }

    bool IsValidPlacement()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(ghost.transform.position, 0.3f);

        foreach (Collider2D hit in hits)
        {
            if (hit == null || hit.transform == transform) continue;
            return false;
        }

        return true;
    }
}