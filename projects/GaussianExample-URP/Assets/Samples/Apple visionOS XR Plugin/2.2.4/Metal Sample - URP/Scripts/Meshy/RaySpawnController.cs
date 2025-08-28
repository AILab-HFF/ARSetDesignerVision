using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class RaySpawnController : MonoBehaviour
{
    public XRRayInteractor ray;
    public MeshyService meshy;
    [TextArea] public string prompt = "a monster mask";

    public InputActionReference spawnAction;

    private Pose lastHitPose;
    private bool hasHit;
    private bool lastPressed;

    void OnEnable()
    {
        if (spawnAction != null && spawnAction.action != null)
        {
            spawnAction.action.performed += OnSpawnPerformed;  // Press
            spawnAction.action.canceled  += OnSpawnCanceled;   // Release
            spawnAction.action.Enable();
        }
    }

    void OnDisable()
    {
        if (spawnAction != null && spawnAction.action != null)
        {
            spawnAction.action.performed -= OnSpawnPerformed;
            spawnAction.action.canceled  -= OnSpawnCanceled;
        }
    }

    void Update()
    {
        // 1 XR Ray Hit falls vorhanden
        if (ray != null && ray.TryGetCurrent3DRaycastHit(out RaycastHit hit))
        {
            var cam = Camera.main;
            var fwd = cam ? Vector3.Cross(cam.transform.right, hit.normal).normalized : Vector3.forward;
            lastHitPose = new Pose(hit.point, Quaternion.LookRotation(fwd, hit.normal));
            hasHit = true;
        }
        else
        {
            hasHit = false;
        }

        // 2 VisionOS Polling wenn keine eigene Action gesetzt
        if ((spawnAction == null || spawnAction.action == null) && ray != null && ray.uiPressInput != null)
        {
            bool pressed = ray.uiPressInput.ReadValue() > 0.5f;

            // Rising edge → Spawn
            if (pressed && !lastPressed)
                TriggerSpawn();

            // Falling edge → Platzieren
            if (!pressed && lastPressed)
                meshy.StopFollowingCurrent(true);

            lastPressed = pressed;
        }

#if UNITY_EDITOR
        // 3 Editor Fallback
        if (spawnAction == null || spawnAction.action == null)
        {
            // Press
            if ((Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
                (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame))
            {
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

            // Release
            if ((Keyboard.current != null && Keyboard.current.spaceKey.wasReleasedThisFrame) ||
                (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame))
            {
                meshy.StopFollowingCurrent(true);
            }
        }
#endif
    }

    private void OnSpawnPerformed(InputAction.CallbackContext _)
    {
        TriggerSpawn();
    }

    private void OnSpawnCanceled(InputAction.CallbackContext _)
    {
        // Loslassen → Hand Follow beenden und Objekt im Raum belassen
        meshy.StopFollowingCurrent(true);
    }

    private void TriggerSpawn()
    {
        if (meshy == null || meshy.isGenerating) return;

        // origin immer ermitteln
        Transform origin = null;
        if (ray != null)
        {
            origin = ray.rayOriginTransform != null && ray.rayOriginTransform.gameObject.activeInHierarchy
                ? ray.rayOriginTransform
                : ray.transform;
        }

        Pose pose;

        if (hasHit)
        {
            pose = lastHitPose; // initial auf Hit Punkt setzen
        }
        else if (origin != null)
        {
            pose = new Pose(origin.position, Quaternion.LookRotation(origin.forward, Vector3.up));
        }
        else
        {
            var cam = Camera.main;
            if (cam == null) return;
            pose = new Pose(
                cam.transform.position + cam.transform.forward * 1.0f,
                Quaternion.LookRotation(cam.transform.forward, Vector3.up)
            );
        }

        if (origin != null)
            meshy.SetPendingSpawnPose(pose, origin, true);  // folgt während gedrückt
        else
            meshy.SetPendingSpawnPose(pose);

        meshy.GeneratePreviewModel(prompt);
    }
}
