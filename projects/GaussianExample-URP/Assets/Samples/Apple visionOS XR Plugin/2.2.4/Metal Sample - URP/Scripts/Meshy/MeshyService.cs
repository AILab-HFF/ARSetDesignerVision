using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using System.IO;
using System;  // Add this to use Uri
using GLTFast;  // Import glTFast for GLB support
using GLTFast.Loading;
using GLTFast.Logging;
using System.Threading.Tasks; // Add this directive
//using dotenv.net;  // Add this directive
//using dotenv.net.Utilities;


public class MeshyService : MonoBehaviour
{

    private string apiKey = "msy_dummy_api_key_for_test_mode_12345678";  // Replace with your Meshy API key
    private const string textTo3DUrl = "https://api.meshy.ai/openapi/v2/text-to-3d"; // Endpoint for creating the 3D task

    // The directory to store downloaded models in the standalone build
    private string modelDirectory;

    public GameObject meshyObjectPrefab;  // A prefab that will act as the parent for the model

    // oben in MeshyService
    private GameObject currentlyFollowing = null;

    public bool isGenerating = false;

    // Event to notify when the task generation state changes
    public delegate void GeneratingStateChangedHandler(bool isGenerating);
    public event GeneratingStateChangedHandler OnGeneratingStateChanged;


    // Observer pattern event
    public delegate void MeshyTaskCompletedHandler(string taskId);
    public event MeshyTaskCompletedHandler OnMeshyTaskCompleted;  // Define the event

    // MeshyService.cs
    private Pose pendingSpawnPose = new Pose(Vector3.zero, Quaternion.identity);
    private bool hasPendingSpawnPose = false;

    // Neu
    private Transform pendingParent = null;
    private bool followParent = false;


    // Neu Überladung mit Parent
    public void SetPendingSpawnPose(Pose pose, Transform parent, bool follow)
    {
        pendingSpawnPose = pose;
        hasPendingSpawnPose = true;
        pendingParent = parent;
        followParent = follow;
    }


    // MeshyService.cs – Felder
    [SerializeField] float scaleMultiplier = 0.2f; // 0.2 = 5x kleiner, 0.1 = 10x kleiner


    public void SetPendingSpawnPose(Pose pose)
    {
        pendingSpawnPose = pose;
        hasPendingSpawnPose = true;
    }

    void Start()
    {
        // try
        // {
        //     // Load environment variables from the .env file using DotEnvOptions
        //     DotEnv.Load(new DotEnvOptions(envFilePaths: new[] { "./.env" })); // Use constructor to specify file paths

        //     Debug.Log("Successfully loaded .env file");

        //     // Retrieve the API key from the environment variables
        //     apiKey = EnvReader.GetStringValue("MESHY_API_KEY");

        //     if (!string.IsNullOrEmpty(apiKey))
        //     {
        //         Debug.Log("API Key: " + apiKey);  // Log the API key for debugging
        //     }
        //     else
        //     {
        //         Debug.LogError("API Key not found in .env file.");
        //     }
        // }
        // catch (Exception e)
        // {
        //     Debug.LogError("Error loading .env or retrieving API key: " + e.Message);
        // }

        // Continue with the rest of your code...
        modelDirectory = Path.Combine(Application.persistentDataPath, "DownloadedModels");
        if (!Directory.Exists(modelDirectory))
        {
            Directory.CreateDirectory(modelDirectory);
        }
    }

    // Function to create a 3D task and start the generation process
    public IEnumerator CreateTextTo3DTask(string prompt, string artStyle = "realistic", bool shouldRemesh = true)
    {
        isGenerating = true;
        OnGeneratingStateChanged?.Invoke(isGenerating);

        var requestBody = new
        {
            mode = "preview",  // 'preview' for model generation, 'refine' for texture
            prompt = prompt,
            art_style = artStyle,
            should_remesh = shouldRemesh
        };

        string jsonBody = JsonConvert.SerializeObject(requestBody);

        UnityWebRequest request = new UnityWebRequest(textTo3DUrl, "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(jsonToSend);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + apiKey);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string responseText = request.downloadHandler.text;
            TextTo3DResponse response = JsonConvert.DeserializeObject<TextTo3DResponse>(responseText);

            Debug.Log("Meshy Task created successfully! Task ID: " + response.result);

            // Now, check the status of the task to get the download URL
            StartCoroutine(RetrieveTaskStatus(response.result));
        }
        else
        {
            Debug.LogError("Meshy Error creating the 3D task: " + request.error);
        }
    }

    // Retrieve the status of the task after creation
    public IEnumerator RetrieveTaskStatus(string taskId)
    {
        string statusUrl = "https://api.meshy.ai/openapi/v2/text-to-3d/" + taskId;
        UnityWebRequest request = UnityWebRequest.Get(statusUrl);
        request.SetRequestHeader("Authorization", "Bearer " + apiKey);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string responseText = request.downloadHandler.text;
            TextTo3DTaskStatus status = JsonConvert.DeserializeObject<TextTo3DTaskStatus>(responseText);

            // Log task progress
            Debug.Log($"Meshy Task Status: {status.status} - Progress: {status.progress}%");

            // If preview task succeeded, download the preview model and trigger refine task
            if (status.status == "SUCCEEDED")
            {
                Debug.Log("Meshy Model generation succeeded! Downloading preview model...");

                // Download the preview model (GLB file)
                StartCoroutine(DownloadModel(status.model_urls.glb, "preview_model.glb", true));  // 'true' indicates this is the preview model

                // Trigger the event to notify observers that the task is complete
                OnMeshyTaskCompleted?.Invoke(taskId);  // Notify observer to refine the task
            }
            else
            {
                Debug.Log("Meshy Task in progress. Current status: " + status.status);
            }
        }
        else
        {
            Debug.LogError("Meshy Error retrieving task status: " + request.error);
        }
    }

    // Function to create a refine task based on the preview task
    public IEnumerator CreateRefineTask(string previewTaskId)
    {
        Debug.Log("Creating refine task based on preview task ID: " + previewTaskId);

        var requestBody = new
        {
            mode = "refine",  // Refining the preview model with textures
            preview_task_id = previewTaskId,
            enable_pbr = true,  // Enable Physically-Based Rendering (textures)
        };

        string jsonBody = JsonConvert.SerializeObject(requestBody);

        UnityWebRequest request = new UnityWebRequest(textTo3DUrl, "POST");
        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(jsonToSend);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + apiKey);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string responseText = request.downloadHandler.text;
            TextTo3DResponse response = JsonConvert.DeserializeObject<TextTo3DResponse>(responseText);

            Debug.Log("Meshy Refine Task created successfully! Task ID: " + response.result);

            // Check refine task status
            StartCoroutine(RetrieveRefineTaskStatus(response.result));
        }
        else
        {
            Debug.LogError("Meshy Error creating refine task: " + request.error);
        }
    }

    // Retrieve the status of the refine task after creation
    public IEnumerator RetrieveRefineTaskStatus(string refineTaskId)
    {
        Debug.Log("Checking the status of the refine task ID: " + refineTaskId);

        string statusUrl = "https://api.meshy.ai/openapi/v2/text-to-3d/" + refineTaskId;
        UnityWebRequest request = UnityWebRequest.Get(statusUrl);
        request.SetRequestHeader("Authorization", "Bearer " + apiKey);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            string responseText = request.downloadHandler.text;
            TextTo3DTaskStatus status = JsonConvert.DeserializeObject<TextTo3DTaskStatus>(responseText);

            // Log refine task progress
            Debug.Log($"Meshy Refine Task Status: {status.status} - Progress: {status.progress}%");

            // If refine task succeeded, download the final model
            if (status.status == "SUCCEEDED")
            {
                Debug.Log("Meshy Refine Task succeeded! Retrieving refined model...");
                StartCoroutine(DownloadModel(status.model_urls.glb, "refined_model.glb", false)); // Download the refined GLB model
            }
            else
            {
                Debug.Log("Meshy Refine Task in progress. Current status: " + status.status);
            }
        }
        else
        {
            Debug.LogError("Meshy Error retrieving refine task status: " + request.error);
        }
    }

    // Download the GLB model and load it into the scene using glTFast
    public IEnumerator DownloadModel(string modelUrl, string modelFileName, bool isPreview)
    {
        Debug.Log("Downloading model from: " + modelUrl); // Debug: Log the URL

        UnityWebRequest request = UnityWebRequest.Get(modelUrl);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            // Save the file to the specified folder
            string modelFilePath = Path.Combine(modelDirectory, modelFileName);  // Use appropriate extension for the format (e.g., .glb)
            File.WriteAllBytes(modelFilePath, request.downloadHandler.data);

            Debug.Log($"Meshy Model downloaded and saved to: {modelFilePath}");

            // If it's the preview model, spawn it in the scene
            if (isPreview)
            {
                Debug.Log("Spawning the preview model in the scene.");
                isGenerating = false;
                OnGeneratingStateChanged?.Invoke(isGenerating);
                //SpawnGLBModel(modelFilePath, "PreviewModel");
            }
            else
            {
                Debug.Log("Spawning the refined model in the scene.");
                SpawnGLBModel(modelFilePath, "RefinedModel");
                isGenerating = false;
                OnGeneratingStateChanged?.Invoke(isGenerating);
            }
        }
        else
        {
            Debug.LogError("Meshy Error downloading the model: " + request.error);
        }
    }

    private void SpawnGLBModel(string modelPath, string modelName)
    {
        if (meshyObjectPrefab == null)
        {
            Debug.LogError("MeshyObject Prefab is not assigned!");
            return;
        }

        Transform defaultParent = null;
        var views = GameObject.Find("-----Views-----");
        if (views != null) defaultParent = views.transform;
        else
        {
            var xrOrigin = GameObject.Find("XR Origin");
            if (xrOrigin != null) defaultParent = xrOrigin.transform;
        }

        GameObject meshyObject = Instantiate(meshyObjectPrefab);
        meshyObject.name = modelName;
        meshyObject.SetActive(false);
        meshyObject.transform.localScale = Vector3.one * scaleMultiplier;

        Transform targetParent = followParent && pendingParent != null ? pendingParent : defaultParent;

        if (followParent && pendingParent != null)
        {
            if (currentlyFollowing != null)
            {
                var stable = GetStableParent();
                Debug.Log($"stop follow: {currentlyFollowing.name} → {(stable ? stable.name : "SceneRoot")}");
                currentlyFollowing.transform.SetParent(stable, true); // stabiler Parent!
                currentlyFollowing = null;
            }

            meshyObject.transform.SetParent(pendingParent, true);
            currentlyFollowing = meshyObject;
            Debug.Log($"now following: {meshyObject.name} → {pendingParent.name}");
        }
        else
        {
            if (targetParent != null)
                meshyObject.transform.SetParent(targetParent, true);
        }

        if (hasPendingSpawnPose)
        {
            meshyObject.transform.SetPositionAndRotation(
                pendingSpawnPose.position,
                pendingSpawnPose.rotation
            );
        }
        else
        {
            var cam = Camera.main;
            if (cam != null)
            {
                meshyObject.transform.position = cam.transform.position + cam.transform.forward * 1.0f;
                meshyObject.transform.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
            }
            else
            {
                meshyObject.transform.position = Vector3.zero;
                meshyObject.transform.rotation = Quaternion.identity;
            }
        }

        var materializeMaterial = Resources.Load<Material>("Materials/MaterializeMaterial");
        if (materializeMaterial != null)
        {
            foreach (var r in meshyObject.GetComponentsInChildren<MeshRenderer>())
                r.material = materializeMaterial;
        }

        // Flags zurücksetzen für den nächsten Spawn
        pendingParent = null;
        followParent = false;

        var gltfImport = new GLTFast.GltfImport();
        StartCoroutine(LoadGLBModelAsync(gltfImport, modelPath, meshyObject));
    }






    //private IEnumerator LoadGLBModelAsync(GltfImport gltfImport, string modelPath, GameObject parent)
    private IEnumerator LoadGLBModelAsync(GLTFast.GltfImport gltfImport, string modelPath, GameObject parent)
    {
        // Wait for the WebRequest to load the GLB model
        yield return StartCoroutine(LoadGLTFWithWebRequest(gltfImport, modelPath));

        // Once loaded, instantiate the model as a child of the parent (mesh object)
        yield return StartCoroutine(InstantiateGLBModelAsync(gltfImport, parent.transform));

        // After the model is instantiated, apply the custom shader and animate MaterializeStrength
        ApplyCustomShaderToModel(parent);
        Debug.Log("Model loaded, shader applied, and instantiated successfully.");
    }

    // speichert Shader-Property-Namen, die wir kopieren wollen
    static readonly int ID_Materialize = Shader.PropertyToID("_MaterializeStrength");
    static readonly int ID_MeshyTex = Shader.PropertyToID("_MeshyTexture");
    static readonly int ID_Cull = Shader.PropertyToID("_Cull"); // URP: 0=Off, 2=Back (default)

    private void ApplyCustomShaderToModel(GameObject root)
    {
        var effectTemplate = Resources.Load<Material>("Materials/MaterializeMaterial");
        if (effectTemplate == null)
        {
            Debug.LogError("MaterializeMaterial not found in Resources/Materials!");
            root.SetActive(true);
            return;
        }

        // Alle Renderer einsammeln (MeshRenderer + SkinnedMeshRenderer)
        var renderers = root.GetComponentsInChildren<Renderer>(true);

        // 1) Originale pro Renderer/Slot merken
        var originals = new System.Collections.Generic.Dictionary<Renderer, Material[]>();
        foreach (var r in renderers)
            originals[r] = r.sharedMaterials;

        // 2) Effekt-Mats pro Slot bauen (Textur + Culling übernehmen)
        var allEffectMats = new System.Collections.Generic.List<Material>();
        foreach (var r in renderers)
        {
            var srcMats = r.sharedMaterials;
            if (srcMats == null || srcMats.Length == 0) continue;

            var newMats = new Material[srcMats.Length];
            for (int i = 0; i < srcMats.Length; i++)
            {
                var src = srcMats[i];
                var m = new Material(effectTemplate);

                // Textur aus irgendeiner belegten Texture-Property ziehen
                string prop;
                var baseMap = FindAnyTextureOnMaterial(src, out prop);
                if (baseMap != null)
                {
                    m.SetTexture(ID_MeshyTex, baseMap);
                    // Debug.Log($"[{r.name}] using texture from '{src.shader?.name}' prop '{prop}'");
                }

                // Culling von der Quelle übernehmen (Double-Sided beibehalten)
                if (src != null && src.HasProperty(ID_Cull))
                {
                    m.SetFloat(ID_Cull, src.GetFloat(ID_Cull));
                }
                else
                {
                    // glTF doubleSided -> häufig Cull Off; wenn nicht ermittelbar, lieber aus
                    if (m.HasProperty(ID_Cull)) m.SetFloat(ID_Cull, 0f); // Off
                }

                // Effekt startet „zu“ (1)
                m.SetFloat(ID_Materialize, 1f);

                newMats[i] = m;
                allEffectMats.Add(m);
            }

            r.materials = newMats; // alle Slots ersetzen
        }

        // Sichtbar schalten und ALLE Materialien gemeinsam animieren
        root.SetActive(true);
        StartCoroutine(AnimateAndRestore(allEffectMats, originals, 1.0f)); // 1s Dauer (anpassen)
    }

    private System.Collections.IEnumerator AnimateAndRestore(
        System.Collections.Generic.List<Material> mats,
        System.Collections.Generic.Dictionary<Renderer, Material[]> originals,
        float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            float v = Mathf.Lerp(1f, 0f, t / duration);
            for (int i = 0; i < mats.Count; i++)
                if (mats[i] != null) mats[i].SetFloat(ID_Materialize, v);

            t += Time.deltaTime;
            yield return null;
        }

        // sicherstellen, dass alle auf 0 sind
        for (int i = 0; i < mats.Count; i++)
            if (mats[i] != null) mats[i].SetFloat(ID_Materialize, 0f);

        // OPTIONAL: nach der kompletten Materialize-Animation
        // die Original-PBR-Materialien wieder einsetzen (volle, „normale“ Anzeige)
        foreach (var kvp in originals)
            if (kvp.Key != null) kvp.Key.materials = kvp.Value;
    }

    // bleibt wie bei dir – sucht irgendeine belegte Textur
    private Texture FindAnyTextureOnMaterial(Material m, out string propName)
    {
        propName = null;
        if (m == null || m.shader == null) return null;

        if (m.HasProperty("_BaseMap"))
        {
            var tex = m.GetTexture("_BaseMap");
            if (tex != null) { propName = "_BaseMap"; return tex; }
        }
        if (m.HasProperty("_MainTex"))
        {
            var tex = m.GetTexture("_MainTex");
            if (tex != null) { propName = "_MainTex"; return tex; }
        }

        int count = m.shader.GetPropertyCount();
        for (int i = 0; i < count; i++)
        {
            if (m.shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture)
            {
                string name = m.shader.GetPropertyName(i);
                var tex = m.GetTexture(name);
                if (tex != null) { propName = name; return tex; }
            }
        }
        return null;
    }




    // Coroutine to animate MaterializeStrength from 1 to 0 over 2 seconds
    private IEnumerator AnimateMaterializeStrength(Material material)
    {
        float duration = 2f;  // Duration of the animation in seconds
        float startValue = 1f;  // Starting value of MaterializeStrength
        float endValue = 0f;  // Ending value of MaterializeStrength
        float elapsedTime = 0f;  // Track elapsed time

        // Animate over 2 seconds
        while (elapsedTime < duration)
        {
            // Calculate the current interpolation value
            float currentStrength = Mathf.Lerp(startValue, endValue, elapsedTime / duration);

            // Set the value in the shader
            material.SetFloat("_MaterializeStrength", currentStrength);

            // Update elapsed time
            elapsedTime += Time.deltaTime;

            // Yield to the next frame
            yield return null;
        }

        // Ensure the final value is set to 0
        material.SetFloat("_MaterializeStrength", endValue);
    }


    //private IEnumerator LoadGLTFWithWebRequest(GltfImport gltfImport, string modelPath)
    private IEnumerator LoadGLTFWithWebRequest(GLTFast.GltfImport gltfImport, string modelPath)
    {
        Uri modelUri = new Uri(modelPath);  // Ensure the path is correct for loading

        // Wait for the WebRequest to load and check for completion
        Task<bool> loadTask = gltfImport.Load(modelUri.ToString());

        // Wait until the task is completed
        yield return new WaitUntil(() => loadTask.IsCompleted);

        if (loadTask.Result)
        {
            Debug.Log("GLTF model loaded successfully");
        }
        else
        {
            Debug.LogError("Failed to load GLTF model");
        }
    }

    //private IEnumerator InstantiateGLBModelAsync(GltfImport gltfImport, Transform parent)
    private IEnumerator InstantiateGLBModelAsync(GLTFast.GltfImport gltfImport, Transform parent)
    {
        Task instantiateTask = gltfImport.InstantiateMainSceneAsync(parent);
        yield return new WaitUntil(() => instantiateTask.IsCompleted);
        if (instantiateTask.Status == TaskStatus.RanToCompletion)
        {
            // The instantiated model is now a child of the MeshyObject prefab
            GameObject modelObject = parent.GetChild(0).gameObject;
            modelObject.transform.localPosition = Vector3.zero;  // Adjust position if necessary
            modelObject.transform.localScale = new Vector3(1, 1, 1);  // Adjust scale if necessary

            // Optionally, focus the camera on the model
            //Camera.main.transform.position = new Vector3(0, 1, -5);  // Adjust camera position
            //Camera.main.transform.LookAt(modelObject.transform.position);  // Ensure the camera looks at the model
        }
        else
        {
            Debug.LogError("Failed to instantiate the model.");
        }
    }

    // Data structure to handle responses from Meshy API
    [System.Serializable]
    public class TextTo3DResponse
    {
        public string result;
    }

    [System.Serializable]
    public class TextTo3DTaskStatus
    {
        public string id;
        public string status;
        public int progress;
        public ModelUrls model_urls;

        [System.Serializable]
        public class ModelUrls
        {
            public string obj;
            public string glb;
            public string fbx;
            public string usdz;
            public string mtl;
        }
    }

    // Debugging function to create a task and immediately check status and download the model
    public void DebugMeshyTask()
    {
        string prompt = "a monster mask";
        StartCoroutine(CreateTextTo3DTask(prompt));
    }

    public void GeneratePreviewModel(string prompt)
    {
        StartCoroutine(CreateTextTo3DTask(prompt));
    }

    public void StopFollowingCurrent(bool keepWorldPose = true)
    {
        if (currentlyFollowing != null)
        {
            var stable = GetStableParent();
            currentlyFollowing.transform.SetParent(stable, keepWorldPose);
            currentlyFollowing = null;
        }
    }
    
    // MeshyService.cs – unter deine Felder
    private Transform GetStableParent()
    {
        var views = GameObject.Find("-----Views-----");
        if (views != null) return views.transform;
        var xrOrigin = GameObject.Find("XR Origin");
        if (xrOrigin != null) return xrOrigin.transform;
        return null; // notfalls Szene-Root
    }


}
