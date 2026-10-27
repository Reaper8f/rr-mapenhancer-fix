using UI.Map;
using UnityEngine;

// Visibility is evaluated only for this mod's canvases. The game's culling
// managers retain their own camera, distance bands and registrations.
public sealed class CanvasCuller : MonoBehaviour
{
    private static readonly Plane[] Planes = new Plane[6];
    private static Camera? cachedCamera;
    private static int cachedFrame = -1;
    private Canvas canvas = null!;
    public float radius = 30f;

    private void Awake() => canvas = GetComponent<Canvas>();

    private void LateUpdate()
    {
        if (canvas == null) return;
        var camera = MapBuilder.Shared?.mapCamera;
        if (camera == null)
        {
            canvas.enabled = true;
            return;
        }
        if (cachedFrame != Time.frameCount || cachedCamera != camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, Planes);
            cachedFrame = Time.frameCount;
            cachedCamera = camera;
        }
        canvas.enabled = GeometryUtility.TestPlanesAABB(Planes,
            new Bounds(transform.position, Vector3.one * (Mathf.Max(0f, radius) * 2f)));
    }

    private void OnDisable()
    {
        if (canvas != null) canvas.enabled = true;
        cachedCamera = null;
        cachedFrame = -1;
    }

    public void RequestUpdateCullingPosition() => cachedFrame = -1;
}