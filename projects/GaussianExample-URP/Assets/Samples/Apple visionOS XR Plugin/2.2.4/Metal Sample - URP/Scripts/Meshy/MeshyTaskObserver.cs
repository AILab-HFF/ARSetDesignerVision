using UnityEngine;

public class MeshyTaskObserver : MonoBehaviour
{
    // Reference to MeshyService (attached to the ObjectPlacer GameObject)
    public MeshyService meshyService;

    void OnEnable()
    {
        // Check if meshyService is assigned and subscribe to the event
        if (meshyService != null)
        {
            meshyService.OnMeshyTaskCompleted += OnMeshyTaskCompleted;
        }
    }

    void OnDisable()
    {
        // Unsubscribe from the event to prevent memory leaks
        if (meshyService != null)
        {
            meshyService.OnMeshyTaskCompleted -= OnMeshyTaskCompleted;
        }
    }

    // This method is called when the Meshy task completes
    private void OnMeshyTaskCompleted(string taskId)
    {
        Debug.Log("Meshy Task completed! Now starting the refine task.");

        // Start the refine task (this calls the refine task method in MeshyService)
        StartCoroutine(meshyService.CreateRefineTask(taskId));
    }
}
