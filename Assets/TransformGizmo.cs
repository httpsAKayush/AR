using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

public class TransformGizmo : MonoBehaviour
{
    [Header("Target")]
    public Transform bodyRoot;

    [Header("Gizmo Settings")]
    public float gizmoRadius = 0.3f;
    public float ringThickness = 0.01f;
    public float arrowLength = 0.4f;
    public float handleSize = 0.03f;

    [Header("Controller Reference")]
    public Transform rightControllerTransform;   // assign in Inspector — reliable, no Find()

    [Header("Left Joystick Rotation")]
    public float joystickRotateSpeed = 90f;      // degrees/sec at full stick deflection

    // Axis color key:
    //   matX (red)   -> X axis  (RingX: rotate around X)
    //   matY (green) -> Y axis  (RingY: rotate around Y | ScaleHandle: uniform scale, vertical arrow)
    //   matZ (blue)  -> Z axis  (RingZ: rotate around Z)
    private Material matX, matY, matZ, matW, matHighlight;

    private GameObject ringX, ringY, ringZ;
    private GameObject gizmoRotationRoot;

    private GameObject scaleHandle;   // single vertical handle, drives uniform scale on all 3 axes
    private GameObject gizmoScaleRoot;

    private bool showRotation = false;
    private bool showScale = false;
    private GameObject activeHandle = null;
    private Vector3 lastControllerPos;
    private Vector3 lastControllerDir;
    private bool triggerWasPressed = false;

    private InputDevice leftDevice;
    private InputDevice rightDevice;
    private LineRenderer rayLine;

    private bool aWasPressed = false;
    private bool xWasPressed = false;

    void Start()
    {
        CreateMaterials();
        CreateRotationGizmo();
        CreateScaleGizmo();

        rayLine = gameObject.AddComponent<LineRenderer>();
        rayLine.startWidth = 0.003f;
        rayLine.endWidth = 0.001f;
        rayLine.material = matW;
        rayLine.enabled = false;

        gizmoRotationRoot.SetActive(false);
        gizmoScaleRoot.SetActive(false);
    }

    void Update()
    {
        if (!leftDevice.isValid)
            leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (!rightDevice.isValid)
            rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        HandleButtonToggles();
        UpdateGizmoPositions();
        HandleRayInteraction();
        HandleLeftJoystickRotation();   // <-- new: left stick rotates model around Y axis (green)

#if UNITY_EDITOR
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            if (UnityEngine.InputSystem.Keyboard.current.digit1Key.wasPressedThisFrame) ToggleRotation();
            if (UnityEngine.InputSystem.Keyboard.current.digit2Key.wasPressedThisFrame) ToggleScale();
        }
#endif
    }

    // Left joystick X-axis -> continuous Y-axis (green) rotation, independent of the ring gizmo.
    // Same pattern as AnatomyController's right-stick rotate, just mirrored to the left hand.
    void HandleLeftJoystickRotation()
    {
        if (bodyRoot == null) return;

        Vector2 leftStick = Vector2.zero;
        leftDevice.TryGetFeatureValue(CommonUsages.primary2DAxis, out leftStick);

        if (Mathf.Abs(leftStick.x) > 0.1f)
        {
            bodyRoot.Rotate(Vector3.up, leftStick.x * joystickRotateSpeed * Time.deltaTime, Space.World);
        }
    }

    void HandleButtonToggles()
    {
        bool aPressed = false;
        rightDevice.TryGetFeatureValue(CommonUsages.primaryButton, out aPressed);
        if (aPressed && !aWasPressed) ToggleRotation();
        aWasPressed = aPressed;

        bool xPressed = false;
        leftDevice.TryGetFeatureValue(CommonUsages.primaryButton, out xPressed);
        if (xPressed && !xWasPressed) ToggleScale();
        xWasPressed = xPressed;
    }

    void ToggleRotation()
    {
        showRotation = !showRotation;
        if (showRotation) showScale = false;
        gizmoRotationRoot.SetActive(showRotation);
        gizmoScaleRoot.SetActive(showScale);
        activeHandle = null;
    }

    void ToggleScale()
    {
        showScale = !showScale;
        if (showScale) showRotation = false;
        gizmoScaleRoot.SetActive(showScale);
        gizmoRotationRoot.SetActive(showRotation);
        activeHandle = null;
    }

    void UpdateGizmoPositions()
    {
        if (bodyRoot == null) return;
        gizmoRotationRoot.transform.position = bodyRoot.position;
        gizmoRotationRoot.transform.rotation = Quaternion.identity;
        gizmoScaleRoot.transform.position = bodyRoot.position;
        gizmoScaleRoot.transform.rotation = Quaternion.identity;
    }

    void HandleRayInteraction()
    {
        if (!showRotation && !showScale)
        {
            rayLine.enabled = false;
            return;
        }

        if (rightControllerTransform == null) return;

        Vector3 rayOrigin = rightControllerTransform.position;
        Vector3 rayDir = rightControllerTransform.forward;

        rayLine.enabled = true;
        rayLine.SetPosition(0, rayOrigin);
        rayLine.SetPosition(1, rayOrigin + rayDir * 1.5f);

        bool triggerPressed = false;
        rightDevice.TryGetFeatureValue(CommonUsages.triggerButton, out triggerPressed);

        if (triggerPressed && !triggerWasPressed)
        {
            activeHandle = GetRayHitHandle(rayOrigin, rayDir);
            if (activeHandle != null)
            {
                lastControllerPos = rightControllerTransform.position;
                lastControllerDir = rayDir;
                HighlightHandle(activeHandle);
            }
        }

        if (triggerPressed && activeHandle != null)
        {
            Vector3 controllerDelta = rightControllerTransform.position - lastControllerPos;
            ApplyTransform(activeHandle, controllerDelta);
            lastControllerPos = rightControllerTransform.position;
        }

        if (!triggerPressed && triggerWasPressed)
        {
            if (activeHandle != null) ResetHandleColor(activeHandle);
            activeHandle = null;
        }

        triggerWasPressed = triggerPressed;
    }

    GameObject GetRayHitHandle(Vector3 origin, Vector3 dir)
    {
        Ray ray = new Ray(origin, dir);
        float closest = float.MaxValue;
        GameObject hit = null;

        List<GameObject> handles = showRotation
            ? new List<GameObject> { ringX, ringY, ringZ }
            : new List<GameObject> { scaleHandle };   // only the single uniform-scale handle now

        foreach (var handle in handles)
        {
            if (handle == null) continue;
            Collider[] cols = handle.GetComponentsInChildren<Collider>();
            foreach (var col in cols)
            {
                RaycastHit info;
                if (col.Raycast(ray, out info, 2f))
                {
                    if (info.distance < closest)
                    {
                        closest = info.distance;
                        hit = handle;
                    }
                }
            }
        }
        return hit;
    }

    void ApplyTransform(GameObject handle, Vector3 delta)
    {
        if (showRotation)
        {
            float speed = 200f;
            if (handle == ringX)              // red — rotate around X
                bodyRoot.Rotate(Vector3.right, -delta.y * speed, Space.World);
            else if (handle == ringY)         // green — rotate around Y
                bodyRoot.Rotate(Vector3.up, delta.x * speed, Space.World);
            else if (handle == ringZ)         // blue — rotate around Z (sign flipped — was inverted before)
                bodyRoot.Rotate(Vector3.forward, delta.x * speed, Space.World);
        }
        else if (showScale)
        {
            // Single vertical (green/Y) handle drives uniform scale on all 3 axes together.
            float speed = 2f;
            if (handle == scaleHandle)
            {
                bodyRoot.localScale += Vector3.one * delta.y * speed;
                bodyRoot.localScale = Vector3.Max(bodyRoot.localScale, Vector3.one * 0.05f);
            }
        }
    }

    void HighlightHandle(GameObject handle)
    {
        foreach (var mr in handle.GetComponentsInChildren<MeshRenderer>())
            mr.material = matHighlight;
    }

    void ResetHandleColor(GameObject handle)
    {
        Material mat = null;
        if (handle == ringX) mat = matX;               // red
        else if (handle == ringY || handle == scaleHandle) mat = matY;  // green
        else if (handle == ringZ) mat = matZ;           // blue
        else mat = matW;

        foreach (var mr in handle.GetComponentsInChildren<MeshRenderer>())
            mr.material = mat;
    }

    void CreateMaterials()
    {
        matX = CreateUnlitMaterial(new Color(1f, 0.2f, 0.2f));    // red   — X axis
        matY = CreateUnlitMaterial(new Color(0.2f, 1f, 0.2f));    // green — Y axis / uniform scale
        matZ = CreateUnlitMaterial(new Color(0.2f, 0.4f, 1f));    // blue  — Z axis
        matW = CreateUnlitMaterial(new Color(1f, 1f, 1f, 0.8f));  // white — ray line
        matHighlight = CreateUnlitMaterial(new Color(1f, 0.9f, 0f));
    }

    Material CreateUnlitMaterial(Color color)
    {
        Material m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.color = color;
        return m;
    }

    void CreateRotationGizmo()
    {
        gizmoRotationRoot = new GameObject("GizmoRotation");

        ringX = CreateRing("RingX", matX);   // red — X axis
        ringX.transform.SetParent(gizmoRotationRoot.transform);
        ringX.transform.localRotation = Quaternion.Euler(0, 90, 0);

        ringY = CreateRing("RingY", matY);   // green — Y axis
        ringY.transform.SetParent(gizmoRotationRoot.transform);
        ringY.transform.localRotation = Quaternion.Euler(90, 0, 0);

        ringZ = CreateRing("RingZ", matZ);   // blue — Z axis
        ringZ.transform.SetParent(gizmoRotationRoot.transform);
        ringZ.transform.localRotation = Quaternion.Euler(0, 0, 0);
    }

    GameObject CreateRing(string name, Material mat)
    {
        GameObject root = new GameObject(name);
        int segments = 16;

        GameObject meshObj = new GameObject(name + "_mesh");
        meshObj.transform.SetParent(root.transform);
        meshObj.transform.localPosition = Vector3.zero;
        meshObj.transform.localRotation = Quaternion.identity;
        MeshFilter mf = meshObj.AddComponent<MeshFilter>();
        MeshRenderer mr = meshObj.AddComponent<MeshRenderer>();
        mr.material = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mf.mesh = BuildRingMesh(gizmoRadius, ringThickness, 48, 8);

        root.AddComponent<MeshFilter>().mesh = BuildRingMesh(gizmoRadius, ringThickness, 48, 8);
        MeshRenderer rootMr = root.AddComponent<MeshRenderer>();
        rootMr.material = mat;
        rootMr.enabled = false;

        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            Vector3 pos = new Vector3(Mathf.Cos(angle) * gizmoRadius, Mathf.Sin(angle) * gizmoRadius, 0);

            GameObject colObj = new GameObject("col_" + i);
            colObj.transform.SetParent(root.transform);
            colObj.transform.localPosition = pos;
            SphereCollider sc = colObj.AddComponent<SphereCollider>();
            sc.radius = ringThickness * 4f;
        }

        return root;
    }

    Mesh BuildRingMesh(float radius, float tube, int ringSegs, int tubeSegs)
    {
        Mesh mesh = new Mesh();
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        for (int i = 0; i <= ringSegs; i++)
        {
            float u = (float)i / ringSegs * Mathf.PI * 2f;
            Vector3 center = new Vector3(Mathf.Cos(u) * radius, Mathf.Sin(u) * radius, 0);
            Vector3 outward = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0);

            for (int j = 0; j <= tubeSegs; j++)
            {
                float v = (float)j / tubeSegs * Mathf.PI * 2f;
                Vector3 vert = center + (outward * Mathf.Cos(v) + Vector3.forward * Mathf.Sin(v)) * tube;
                verts.Add(vert);
            }
        }

        for (int i = 0; i < ringSegs; i++)
        {
            for (int j = 0; j < tubeSegs; j++)
            {
                int a = i * (tubeSegs + 1) + j;
                int b = a + tubeSegs + 1;
                tris.Add(a); tris.Add(b); tris.Add(a + 1);
                tris.Add(b); tris.Add(b + 1); tris.Add(a + 1);
            }
        }

        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        return mesh;
    }

    // Only one handle now: a vertical (green) arrow. Drag up/down -> scales
    // bodyRoot uniformly on X/Y/Z together. No more per-axis scale, no center cube.
    void CreateScaleGizmo()
    {
        gizmoScaleRoot = new GameObject("GizmoScale");

        scaleHandle = CreateArrow("ScaleHandle", matY);   // green, vertical
        scaleHandle.transform.SetParent(gizmoScaleRoot.transform);
        scaleHandle.transform.localRotation = Quaternion.identity;
        scaleHandle.transform.localPosition = new Vector3(0, arrowLength * 0.5f, 0);
    }

    public void SetTarget(Transform newTarget)
    {
        bodyRoot = newTarget;
    }

    GameObject CreateArrow(string name, Material mat)
    {
        GameObject go = new GameObject(name);

        GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        shaft.transform.SetParent(go.transform);
        shaft.transform.localScale = new Vector3(ringThickness * 2f, arrowLength * 0.5f, ringThickness * 2f);
        shaft.transform.localPosition = Vector3.zero;
        shaft.GetComponent<MeshRenderer>().material = mat;
        Destroy(shaft.GetComponent<CapsuleCollider>());

        GameObject head = CreateCube(name + "_head", mat, handleSize);
        head.transform.SetParent(go.transform);
        head.transform.localPosition = new Vector3(0, arrowLength * 0.5f, 0);

        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.size = new Vector3(handleSize, arrowLength, handleSize);
        bc.center = Vector3.zero;

        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>().material = mat;

        return go;
    }

    GameObject CreateCube(string name, Material mat, float size)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.localScale = Vector3.one * size;
        go.GetComponent<MeshRenderer>().material = mat;
        return go;
    }
}