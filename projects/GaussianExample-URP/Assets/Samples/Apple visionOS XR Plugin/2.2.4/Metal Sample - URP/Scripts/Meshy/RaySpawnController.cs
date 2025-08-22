using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class RaySpawnController : MonoBehaviour
{
    public XRRayInteractor ray;
    public MeshyService meshy;
    [TextArea] public string prompt = "a monster mask";

    // Optional: eigene InputAction (Editor ODER Gerät)
    public InputActionReference spawnAction;

    private Pose lastHitPose;
    private bool hasHit;
    private bool lastPressed;

    void OnEnable() {
        if (spawnAction != null && spawnAction.action != null) {
            spawnAction.action.performed += OnSpawnPerformed;
            spawnAction.action.Enable();
        }
    }
    void OnDisable() {
        if (spawnAction != null && spawnAction.action != null)
            spawnAction.action.performed -= OnSpawnPerformed;
    }

    void Update()
    {
        // 1) XR-Ray-Hit (falls vorhanden)
        if (ray != null && ray.TryGetCurrent3DRaycastHit(out RaycastHit hit))
        {
            var fwd = Vector3.Cross(Camera.main.transform.right, hit.normal).normalized;
            lastHitPose = new Pose(hit.point, Quaternion.LookRotation(fwd, hit.normal));
            hasHit = true;
        }
        else
        {
            hasHit = false;
        }

        // 2) VisionOS: Polling der UI-Press, wenn keine eigene Action gesetzt
        if ((spawnAction == null || spawnAction.action == null) && ray != null && ray.uiPressInput != null)
        {
            bool pressed = ray.uiPressInput.ReadValue() > 0.5f;
            if (pressed && !lastPressed) TriggerSpawn();
            lastPressed = pressed;
        }

#if UNITY_EDITOR
        // 3) Editor-Fallback: Space oder linke Maustaste
        if (spawnAction == null || spawnAction.action == null)
        {
            if ((Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
                (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame))
            {
                // Wenn XR-Ray nichts trifft, versuche einen Physics.Raycast von der Kamera
                if (!hasHit)
                {
                    var cam = Camera.main;
                    if (cam != null && Physics.Raycast(cam.transform.position, cam.transform.forward, out var camHit, 10f))
                    {
                        var fwd2 = Vector3.Cross(cam.transform.right, camHit.normal).normalized;
                        lastHitPose = new Pose(camHit.point, Quaternion.LookRotation(fwd2, camHit.normal));
                        hasHit = true;
                    }
                }
                TriggerSpawn();
            }
        }
#endif
    }

    private void OnSpawnPerformed(InputAction.CallbackContext _)
    {
        TriggerSpawn();
    }

    private void TriggerSpawn()
    {
        if (meshy == null || meshy.isGenerating) return;

        Pose pose;
        if (hasHit)
        {
            pose = lastHitPose;
        }
        else
        {
            // Fallback: 1 m vor der Kamera
            var cam = Camera.main;
            if (cam == null) return;
            pose = new Pose(
                cam.transform.position + cam.transform.forward * 1.0f,
                Quaternion.LookRotation(cam.transform.forward, Vector3.up)
            );
        }

        Debug.Log("Pinch/Click detected → starting Meshy generation");
        meshy.SetPendingSpawnPose(pose);
        meshy.GeneratePreviewModel(prompt);
    }
}
