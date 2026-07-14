using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace MetaXR.LofiStudy.ARFoundation
{
    public class CameraFeedSpawner : MonoBehaviour
    {
        [Header("Prefab")]
        public GameObject feedPrefab;

        [Header("Spawn Position")]
        public float spawnDistance  = 1.5f;
        public float verticalOffset = 0f;

        [Header("References")]
        public Transform cameraTransform;
        public ObjectPlacementSelectionUI placementUI;
        public MicrusControlPanel controlPanel;

        [Header("Placers — assign both NearFarInteractor placers here")]
        public XRRayARPlanePlacer[] placers;   // drag both placers in Inspector

        void Awake()
        {
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
        }

        public void SpawnFeedScreen()
        {
            if (feedPrefab == null)
            {
                Debug.LogWarning("[CameraFeedSpawner] No feed prefab assigned.");
                return;
            }

            if (cameraTransform == null)
            {
                Debug.LogWarning("[CameraFeedSpawner] No camera transform found.");
                return;
            }

            // Spawn position — forward from camera
            var forward       = cameraTransform.forward;
            forward.y         = 0;
            forward.Normalize();

            var spawnPosition = cameraTransform.position
                                + forward * spawnDistance
                                + Vector3.up * verticalOffset;

            // Face screen toward player
            var lookDir       = spawnPosition - cameraTransform.position;
            lookDir.y         = 0;
            var spawnRotation = lookDir.sqrMagnitude > 0.001f
                                ? Quaternion.LookRotation(lookDir.normalized, Vector3.up)
                                : Quaternion.identity;

            // Spawn feed prefab
            var instance = Instantiate(feedPrefab, spawnPosition, spawnRotation);

            if (instance.GetComponent<PlacedObjectRootMarker>() == null)
                instance.AddComponent<PlacedObjectRootMarker>();

            ConfigureGrabInteraction(instance);

            if (controlPanel != null)
                controlPanel.BuildControlPanels(instance);

            // ── KEY CHANGE ───────────────────────────────────────────────
            // Disarm all placers so Trigger is no longer consumed by
            // AR plane placement — it becomes free for UI button clicks.
            DisarmAllPlacers();
            // ─────────────────────────────────────────────────────────────

            Debug.Log("[CameraFeedSpawner] Feed screen spawned. Placers disarmed — trigger now controls UI.");
        }

        void DisarmAllPlacers()
        {
            if (placers == null) return;
            foreach (var placer in placers)
            {
                if (placer != null)
                    placer.DisarmPlacement();
            }
        }

        void ConfigureGrabInteraction(GameObject target)
        {
            // Shrink collider to screen area only — don't cover side panels
            var existingCol = target.GetComponentInChildren<BoxCollider>();
            if (existingCol != null)
            {
                existingCol.center = new Vector3(0f, 0.5f, 0f);
                existingCol.size   = new Vector3(1.8f, 1.1f, 0.05f);
            }
            else
            {
                var col    = target.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 0.5f, 0f);
                col.size   = new Vector3(1.8f, 1.1f, 0.05f);
            }

            var rb = target.GetComponent<Rigidbody>();
            if (rb == null) rb = target.AddComponent<Rigidbody>();
            rb.useGravity             = false;
            rb.isKinematic            = true;
            rb.linearVelocity         = Vector3.zero;
            rb.angularVelocity        = Vector3.zero;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            var grab = target.GetComponent<XRGrabInteractable>();
            if (grab == null) grab = target.AddComponent<XRGrabInteractable>();
            grab.throwOnDetach    = false;
            grab.trackPosition    = true;
            grab.trackRotation    = true;
            grab.trackScale       = false; // ← ADD THIS — prevents scale change on grab
            grab.movementType     = XRBaseInteractable.MovementType.Kinematic;
            grab.useDynamicAttach = true;
            grab.attachEaseInTime = 0f;

            var attachObj = new GameObject("GrabAttach");
            attachObj.transform.SetParent(target.transform, false);
            attachObj.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            grab.attachTransform = attachObj.transform;

            if (target.GetComponent<PlacedObjectSurface>() == null)
                target.AddComponent<PlacedObjectSurface>();
        }
    }
}
// using UnityEngine;
// using UnityEngine.XR.Interaction.Toolkit.Interactables;

// namespace MetaXR.LofiStudy.ARFoundation
// {
//     public class CameraFeedSpawner : MonoBehaviour
//     {
//         [Header("Prefab")]
//         public GameObject feedPrefab;

//         [Header("Spawn Position")]
//         public float spawnDistance  = 1.5f;
//         public float verticalOffset = 0f;

//         [Header("References")]
//         public Transform cameraTransform;
//         public ObjectPlacementSelectionUI placementUI;
//         public MicrusControlPanel controlPanel;

//         [Header("Placers — assign both NearFarInteractor placers here")]
//         public XRRayARPlanePlacer[] placers;

//         [Header("Grab Bar Settings")]
//         [Tooltip("Width of the screen quad in world units — used to size the bottom grab bar.")]
//         public float screenWidth  = 1.6f;
//         [Tooltip("Height of the screen quad in world units — used to position the bottom grab bar.")]
//         public float screenHeight = 1.0f;
//         public float grabBarHeight = 0.06f;

//         void Awake()
//         {
//             if (cameraTransform == null && Camera.main != null)
//                 cameraTransform = Camera.main.transform;
//         }

//         public void SpawnFeedScreen()
//         {
//             if (feedPrefab == null)
//             {
//                 Debug.LogWarning("[CameraFeedSpawner] No feed prefab assigned.");
//                 return;
//             }

//             if (cameraTransform == null)
//             {
//                 Debug.LogWarning("[CameraFeedSpawner] No camera transform found.");
//                 return;
//             }

//             var forward = cameraTransform.forward;
//             forward.y   = 0;
//             forward.Normalize();

//             var spawnPosition = cameraTransform.position
//                                 + forward * spawnDistance
//                                 + Vector3.up * verticalOffset;

//             var lookDir       = spawnPosition - cameraTransform.position;
//             lookDir.y         = 0;
//             var spawnRotation = lookDir.sqrMagnitude > 0.001f
//                                 ? Quaternion.LookRotation(lookDir.normalized, Vector3.up)
//                                 : Quaternion.identity;

//             var instance = Instantiate(feedPrefab, spawnPosition, spawnRotation);

//             if (instance.GetComponent<PlacedObjectRootMarker>() == null)
//                 instance.AddComponent<PlacedObjectRootMarker>();

//             ConfigureGrabInteraction(instance);

//             if (controlPanel != null)
//                 controlPanel.BuildControlPanels(instance);

//             DisarmAllPlacers();

//             Debug.Log("[CameraFeedSpawner] Feed screen spawned. Placers disarmed — trigger now controls UI.");
//         }

//         void DisarmAllPlacers()
//         {
//             if (placers == null) return;
//             foreach (var placer in placers)
//             {
//                 if (placer != null)
//                     placer.DisarmPlacement();
//             }
//         }

//         // ── Grab setup ────────────────────────────────────────────────────────
//         // Quest-style: a small, minimal bar at the BOTTOM edge of the screen,
//         // instead of a collider covering the whole feed area. This avoids
//         // blocking the video content and matches the same bottom-bar pattern
//         // used on the control panels.
//         void ConfigureGrabInteraction(GameObject target)
//         {
//             // Remove any old full-screen collider from a previous version of this
//             // setup, so it doesn't sit on top of the new bar and eat grab input.
//             var existingCol = target.GetComponentInChildren<BoxCollider>();
//             if (existingCol != null)
//                 Destroy(existingCol);

//             // Small grab bar object, positioned at the bottom edge of the screen,
//             // in the parent's local space (assumes screen quad is centered at
//             // local Vector3.zero — adjust `screenHeight/2` offset if your quad's
//             // pivot is different).
//             var grabBar = new GameObject("GrabBar");
//             grabBar.transform.SetParent(target.transform, false);
//             // grabBar.transform.localPosition = new Vector3(0f, -(screenHeight / 2f) - (grabBarHeight / 2f) - 0.02f, 0f);
//             grabBar.transform.localPosition = new Vector3(0f, -(screenHeight / 2f) - (grabBarHeight / 2f) - 0.001f, 0f);
//             grabBar.transform.localRotation = Quaternion.identity;
//             grabBar.transform.localScale    = Vector3.one;

//             var box = grabBar.AddComponent<BoxCollider>();
//             box.size   = new Vector3(screenWidth * 0.5f, grabBarHeight, 0.05f);  // shorter than full width — minimal, not a big slab
//             box.center = Vector3.zero;

//             // Optional: a thin visible strip so the user can see where to grab,
//             // without it looking like a button. Uses a simple unlit quad, not UI.
//             var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
//             visual.name = "GrabBarVisual";
//             Destroy(visual.GetComponent<BoxCollider>());   // the real collider is on the parent grabBar
//             visual.transform.SetParent(grabBar.transform, false);
//             visual.transform.localPosition = Vector3.zero;
//             visual.transform.localScale    = new Vector3(screenWidth * 0.5f, grabBarHeight * 0.4f, 0.01f);
//             var visualRenderer = visual.GetComponent<Renderer>();
//             var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
//             mat.color = new Color(0.5f, 0.5f, 0.55f, 0.8f);
//             visualRenderer.material = mat;

//             var rb = target.GetComponent<Rigidbody>();
//             if (rb == null) rb = target.AddComponent<Rigidbody>();
//             rb.useGravity             = false;
//             rb.isKinematic            = true;
//             rb.linearVelocity         = Vector3.zero;
//             rb.angularVelocity        = Vector3.zero;
//             rb.interpolation          = RigidbodyInterpolation.Interpolate;
//             rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

//             var grab = target.GetComponent<XRGrabInteractable>();
//             if (grab == null) grab = target.AddComponent<XRGrabInteractable>();
//             grab.colliders.Clear();
//             grab.colliders.Add(box);          // grab now only triggers from the bottom bar
//             grab.throwOnDetach    = false;
//             grab.trackPosition    = true;
//             grab.trackRotation    = true;
//             grab.trackScale       = false;
//             grab.movementType     = XRBaseInteractable.MovementType.Kinematic;
//             grab.useDynamicAttach = true;
//             grab.attachEaseInTime = 0f;

//             var attachObj = new GameObject("GrabAttach");
//             attachObj.transform.SetParent(target.transform, false);
//             attachObj.transform.localPosition = new Vector3(0f, 0f, -0.05f);
//             grab.attachTransform = attachObj.transform;

//             if (target.GetComponent<PlacedObjectSurface>() == null)
//                 target.AddComponent<PlacedObjectSurface>();
//         }
//     }
// }