using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class KingMechanismSetup
{
    public const string ReferenceBlockName = "Cube.088_Cube.089";
    public const string ExcludedBlockName = "Cube.021_Cube.022";
    public const string ExcludedCenterBlockName = "hidden";
    public const string WhitePrefabPath = "Assets/Prefabs/King.prefab";
    public const string BlackPrefabPath = "Assets/Prefabs/King (1) Variant.prefab";
    private const string RequestPath = "Recovery/KingTeleport/apply-request-v2.json";
    private const string ResultPath = "Recovery/KingTeleport/apply-result.json";
    private const string OperationName = "Set up 25 paired king teleports";

    [Serializable] public sealed class SetupResult
    {
        public string status;
        public string scenePath;
        public string backupPath;
        public string message;
        public int blockCount;
        public int kingCount;
        public int createdKings;
        public int reusedKings;
        public int alignedKings;
        public int relocatedKings;
        public int removedKings;
        public bool sceneSaved;
        public int seed;
    }

    [Serializable] private sealed class SetupRequest
    {
        public string scenePath = "Assets/Scenes/walking sim.unity";
        public int seed = 20261004;
        public bool saveScene = true;
    }

    private static bool processing;
    private static string waitingStatus;

    [InitializeOnLoadMethod]
    private static void ListenForRequest()
    {
        if (!Application.isBatchMode)
            EditorApplication.update += ProcessRequest;
    }

    private static void ProcessRequest()
    {
        if (processing || !File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        try
        {
            SetupRequest request = JsonUtility.FromJson<SetupRequest>(File.ReadAllText(RequestPath));
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                WriteWaiting("WAITING_FOR_EDIT_MODE", "Waiting for Edit Mode; no scene changes made.");
                return;
            }
            Scene scene = SceneManager.GetSceneByPath(request.scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                WriteWaiting("WAITING_FOR_SCENE", "Waiting for the requested scene to be open; no scene changes made.");
                return;
            }
            processing = true;
            string consumedPath = RequestPath.Replace("apply-request", "apply-consumed");
            if (File.Exists(consumedPath)) File.Delete(consumedPath);
            File.Move(RequestPath, consumedPath);
            SetupResult result = Apply(scene, request.seed, true);
            if (request.saveScene)
            {
                // Save the current in-memory scene only after every arrival has
                // passed validation. The SaveAsCopy backup preserves unsaved
                // user work before the upgrade; no scene is reopened/reloaded.
                result.sceneSaved = EditorSceneManager.SaveScene(scene);
                result.message = result.sceneSaved
                    ? result.message.Replace("Save the open scene to keep the upgrade.", "Open scene saved.")
                    : result.message + " The open scene still needs to be saved.";
            }
            File.WriteAllText(ResultPath, JsonUtility.ToJson(result, true));
            Debug.Log("King mechanism: " + result.message);
        }
        catch (Exception exception)
        {
            File.WriteAllText(ResultPath, JsonUtility.ToJson(new SetupResult
            {
                status = "FAILED", message = exception.ToString()
            }, true));
            Debug.LogException(exception);
        }
        finally { processing = false; }
    }

    private static void WriteWaiting(string status, string message)
    {
        if (waitingStatus == status) return;
        waitingStatus = status;
        File.WriteAllText(ResultPath, JsonUtility.ToJson(new SetupResult { status = status, message = message }, true));
    }

    [MenuItem("Tools/Chess/Set Up King Teleports")]
    public static void SetUpOpenScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Set up kings in Edit Mode.");
            return;
        }
        SetupResult result = Apply(SceneManager.GetActiveScene(), 20261004, true);
        Debug.Log("King mechanism: " + result.message);
    }

    public static List<Transform> GetTargetBlocks(Scene scene)
    {
        Transform reference = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                if (candidate.name == ReferenceBlockName && candidate.GetComponent<MeshFilter>() != null)
                {
                    if (reference != null) throw new InvalidOperationException("Multiple reference cube blocks found.");
                    reference = candidate;
                }
        if (reference == null || reference.parent == null)
            throw new InvalidOperationException("Reference cube block was not found in the open scene.");

        // Layer rotations temporarily move pieces out of cubeParent and under
        // rotationPivot. In editors without scene reload, that hierarchy can
        // also remain after leaving Play Mode. Keep their current poses intact.
        List<CubeLayerRotator> rotators = new List<CubeLayerRotator>();
        foreach (GameObject root in scene.GetRootGameObjects())
            rotators.AddRange(root.GetComponentsInChildren<CubeLayerRotator>(true));
        HashSet<Transform> cubeParents = new HashSet<Transform>();
        foreach (CubeLayerRotator rotator in rotators)
        {
            if (rotator.cubeParent == null || rotator.cubeParent.gameObject.scene != scene) continue;
            if (reference.IsChildOf(rotator.cubeParent)
                || (rotator.rotationPivot != null && reference.IsChildOf(rotator.rotationPivot)))
                cubeParents.Add(rotator.cubeParent);
        }
        HashSet<Transform> containers = new HashSet<Transform>();
        containers.Add(reference.parent);
        foreach (CubeLayerRotator rotator in rotators)
        {
            if (!cubeParents.Contains(rotator.cubeParent)) continue;
            containers.Add(rotator.cubeParent);
            if (rotator.rotationPivot != null && rotator.rotationPivot.gameObject.scene == scene)
                containers.Add(rotator.rotationPivot);
        }
        HashSet<Transform> pieces = new HashSet<Transform>();
        foreach (Transform container in containers)
            foreach (Transform candidate in container)
                if (candidate.CompareTag("CubePiece") && candidate.GetComponent<MeshFilter>() != null
                    && candidate.GetComponent<MeshRenderer>() != null)
                    pieces.Add(candidate);

        List<Transform> blocks = new List<Transform>();
        int total = 0;
        int excluded = 0;
        int excludedCenter = 0;
        foreach (Transform candidate in pieces)
        {
            total++;
            if (candidate.name == ExcludedBlockName) { excluded++; continue; }
            if (candidate.name == ExcludedCenterBlockName) { excludedCenter++; continue; }
            if (candidate.GetComponent<MeshFilter>().sharedMesh == null)
                throw new InvalidOperationException("Cube mesh is missing: " + candidate.name);
            blocks.Add(candidate);
        }
        if (total != 27 || excluded != 1 || excludedCenter != 1 || blocks.Count != 25 || !blocks.Contains(reference))
            throw new InvalidOperationException("Expected 27 main blocks with glass and center excluded; found " + total + ".");
        blocks.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return blocks;
    }

    public static SetupResult Apply(Scene scene, int seed = 20261004, bool createBackup = true)
    {
        if (!scene.IsValid() || !scene.isLoaded || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("King setup requires a loaded scene in Edit Mode.");
        List<Transform> blocks = GetTargetBlocks(scene);
        GameObject whitePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WhitePrefabPath);
        GameObject blackPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlackPrefabPath);
        if (whitePrefab == null || blackPrefab == null)
            throw new InvalidOperationException("The black and white king prefabs must exist.");
        Material whiteMaterial = FindMaterial(whitePrefab, "0236622232924154c920c88a92290b5e");
        Material blackMaterial = FindMaterial(blackPrefab, "d623dcfef9c3a7e4a98aff7c4f088b39");
        PlayerMovement player = FindPlayer(scene);

        // Validate before changing any objects, including existing user-created kings.
        foreach (Transform block in blocks)
        {
            FindKing(block, WhitePrefabPath);
            FindKing(block, BlackPrefabPath);
        }
        SetupResult result = new SetupResult { scenePath = scene.path, seed = seed, blockCount = blocks.Count };
        if (createBackup)
        {
            result.backupPath = "Assets/Recovery/KingTeleportBefore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".unity";
            Directory.CreateDirectory("Assets/Recovery");
            if (!EditorSceneManager.SaveScene(scene, result.backupPath, true))
                throw new IOException("Could not back up the current scene; no kings were changed.");
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(OperationName);
        try
        {
            System.Random random = new System.Random(seed);
            RemoveCenterPortals(scene, result);
            foreach (Transform block in blocks)
            {
                MeshCollider surface = block.GetComponent<MeshCollider>();
                if (surface == null || surface.sharedMesh == null || surface.isTrigger || !surface.enabled)
                    throw new InvalidOperationException("King destination needs a solid mesh surface: " + block.name);
                int whiteFace = random.Next(6);
                int blackFace = (whiteFace + 1 + random.Next(5)) % 6;
                KingTeleportTrigger white = AddKing(block, whitePrefab, whiteMaterial, whiteFace, 0.5f, random, result);
                KingTeleportTrigger black = AddKing(block, blackPrefab, blackMaterial, blackFace, 0.59341f, random, result);
                BindPair(white, black, block, player, result, false);
                BindPair(black, white, block, player, result, true);
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            result.status = "APPLIED";
            result.message = blocks.Count + " bidirectional pairs, " + result.kingCount + " king portals. Created " + result.createdKings + ", reused " + result.reusedKings
                + ", aligned " + result.alignedKings + " embedded kings. Material changes removed. Glass and center excluded. Save the open scene to keep the upgrade.";
            result.message += " Positioned " + result.relocatedKings + " kings on verified mesh patches; removed "
                + result.removedKings + " generated center portals.";
            return result;
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }

    private static void RemoveCenterPortals(Scene scene, SetupResult result)
    {
        List<GameObject> generated = new List<GameObject>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (KingTeleportTrigger portal in root.GetComponentsInChildren<KingTeleportTrigger>(true))
                if (portal.TargetSurface != null && portal.TargetSurface.transform.name == ExcludedCenterBlockName
                    && portal.transform.parent == portal.TargetSurface.transform
                    && (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(portal.gameObject) == WhitePrefabPath
                        || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(portal.gameObject) == BlackPrefabPath))
                    generated.Add(portal.gameObject);
        foreach (GameObject king in generated)
        {
            Undo.DestroyObjectImmediate(king);
            result.removedKings++;
        }
    }

    private static PlayerMovement FindPlayer(Scene scene)
    {
        PlayerMovement found = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (PlayerMovement candidate in root.GetComponentsInChildren<PlayerMovement>(true))
                if (candidate.CompareTag("Player"))
                {
                    if (found != null) throw new InvalidOperationException("Multiple players found in the scene.");
                    found = candidate;
                }
        if (found == null) throw new InvalidOperationException("A player is needed to verify the king arrival volume.");
        return found;
    }

    private static Material FindMaterial(GameObject prefab, string expectedGuid)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(expectedGuid));
        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (Material candidate in renderer.sharedMaterials)
                if (candidate == material && material != null) return material;
        throw new InvalidOperationException("King material does not match the prefab: " + prefab.name);
    }

    private static GameObject FindKing(Transform block, string prefabPath)
    {
        GameObject found = null;
        foreach (Transform child in block)
        {
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject) != prefabPath) continue;
            if (found != null) throw new InvalidOperationException("Multiple kings of the same color on " + block.name);
            found = child.gameObject;
        }
        return found;
    }

    private static KingTeleportTrigger AddKing(Transform block, GameObject prefab, Material material, int face, float scale,
        System.Random random, SetupResult result)
    {
        string path = AssetDatabase.GetAssetPath(prefab);
        GameObject king = FindKing(block, path);
        bool created = king == null;
        if (created)
        {
            king = (GameObject)PrefabUtility.InstantiatePrefab(prefab, block);
            Undo.RegisterCreatedObjectUndo(king, OperationName);
            king.name = path == WhitePrefabPath ? "King White" : "King Black";
            result.createdKings++;
        }
        else result.reusedKings++;

        KingMaterialTrigger legacy = king.GetComponent<KingMaterialTrigger>();
        KingTeleportTrigger trigger = king.GetComponent<KingTeleportTrigger>();
        bool alreadyInstalled = (trigger != null && trigger.TargetSurface != null
            && trigger.TargetSurface.transform == block)
            || (legacy != null && legacy.TargetBlock == block.GetComponent<Renderer>() && legacy.KingMaterial == material);
        Bounds visualBounds = MeasureVisualBounds(king.transform);
        // Preserve face selection and subsequent manual adjustments.
        if (!alreadyInstalled && (created || block.name != ReferenceBlockName))
        {
            Undo.RecordObject(king.transform, OperationName);
            king.transform.localScale = Vector3.one * scale;
            PlaceOnFace(king.transform, block.GetComponent<MeshFilter>().sharedMesh.bounds, visualBounds, face, random);
            PrefabUtility.RecordPrefabInstancePropertyModifications(king.transform);
        }
        AlignEmbeddedKing(king.transform, block.GetComponent<MeshFilter>().sharedMesh.bounds, visualBounds, result);

        Undo.RecordObject(king, OperationName);
        king.tag = "Untagged";
        king.layer = 0;
        BoxCollider collider = king.GetComponent<BoxCollider>();
        if (collider == null) collider = Undo.AddComponent<BoxCollider>(king);
        Undo.RecordObject(collider, OperationName);
        collider.isTrigger = true;
        collider.enabled = true;
        visualBounds.Expand(0.08f);
        collider.center = visualBounds.center;
        collider.size = Vector3.Max(visualBounds.size, Vector3.one * 0.15f);

        Rigidbody body = king.GetComponent<Rigidbody>();
        if (body == null) body = Undo.AddComponent<Rigidbody>(king);
        Undo.RecordObject(body, OperationName);
        body.isKinematic = true;
        body.useGravity = false;
        body.detectCollisions = true;
        if (trigger == null) trigger = Undo.AddComponent<KingTeleportTrigger>(king);
        if (legacy != null) Undo.DestroyObjectImmediate(legacy);
        PrefabUtility.RecordPrefabInstancePropertyModifications(king);
        PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
        PrefabUtility.RecordPrefabInstancePropertyModifications(body);
        PrefabUtility.RecordPrefabInstancePropertyModifications(trigger);
        result.kingCount++;
        return trigger;
    }

    private static Vector3 FaceNormal(Transform king, Transform block)
    {
        Vector3 up = block.InverseTransformDirection(king.up);
        int axis = Mathf.Abs(up.x) > Mathf.Abs(up.y) ? 0 : 1;
        if (Mathf.Abs(up.z) > Mathf.Abs(up[axis])) axis = 2;
        Vector3 normal = Vector3.zero;
        normal[axis] = up[axis] < 0f ? -1f : 1f;
        return normal;
    }

    private static void AlignEmbeddedKing(Transform king, Bounds cube, Bounds visual, SetupResult result)
    {
        Vector3 normal = FaceNormal(king, king.parent);
        float plane = Vector3.Dot(cube.center, normal) + Vector3.Dot(cube.extents,
            new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z)));
        Matrix4x4 matrix = Matrix4x4.TRS(king.localPosition, king.localRotation, king.localScale);
        float lowest = float.PositiveInfinity;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = visual.center + Vector3.Scale(visual.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            lowest = Mathf.Min(lowest, Vector3.Dot(matrix.MultiplyPoint3x4(corner), normal));
        }
        float gap = lowest - plane;
        if (gap >= -0.02f) return;
        Undo.RecordObject(king, OperationName);
        king.localPosition += normal * (0.006f - gap);
        PrefabUtility.RecordPrefabInstancePropertyModifications(king);
        result.alignedKings++;
    }

    private static void BindPair(KingTeleportTrigger king, KingTeleportTrigger partner, Transform block,
        PlayerMovement player, SetupResult result, bool avoidPartner)
    {
        MeshCollider surface = block.GetComponent<MeshCollider>();
        Vector3 normal = FaceNormal(king.transform, block);
        Vector3 wanted = block.InverseTransformPoint(king.transform.TransformPoint(king.GetComponent<BoxCollider>().center));
        List<Vector3> candidates = SurfaceCandidates(surface.sharedMesh, normal);
        candidates.Sort((a, b) => (block.TransformPoint(a) - block.TransformPoint(wanted)).sqrMagnitude
            .CompareTo((block.TransformPoint(b) - block.TransformPoint(wanted)).sqrMagnitude));
        Physics.SyncTransforms();
        foreach (Vector3 candidate in candidates)
        {
            Vector3 worldNormal = block.worldToLocalMatrix.transpose.MultiplyVector(normal).normalized;
            Vector3 worldPoint = block.TransformPoint(candidate);
            if (avoidPartner)
            {
                CharacterController controller = player.GetComponent<CharacterController>();
                float playerRadius = controller.radius * Mathf.Max(Mathf.Abs(player.transform.lossyScale.x), Mathf.Abs(player.transform.lossyScale.z));
                float separation = PlanarTriggerRadius(king.GetComponent<BoxCollider>(), worldNormal)
                    + PlanarTriggerRadius(partner.GetComponent<BoxCollider>(), worldNormal) + playerRadius * 2f;
                if ((worldPoint - partner.WorldArrivalPoint).sqrMagnitude < separation * separation) continue;
            }
            // The imported blocks contain holes and recessed surfaces. The mesh
            // bounds are not a floor; only use an outward triangle on this face.
            if (!surface.Raycast(new Ray(worldPoint + worldNormal * 0.05f, -worldNormal), out RaycastHit hit, 0.1f)
                || Vector3.Dot(hit.normal, worldNormal) < 0.99f
                || (hit.point - worldPoint).sqrMagnitude > 0.0001f
                || !HasFootSupport(surface, hit.point, hit.normal, player)) continue;
            Vector3 forward = Quaternion.FromToRotation(player.transform.up, hit.normal) * player.transform.forward;
            if (!player.CanTeleportToSurface(surface, hit.point, hit.normal, forward)) continue;

            Vector3 localPoint = block.InverseTransformPoint(hit.point);
            Vector3 localNormal = block.localToWorldMatrix.transpose.MultiplyVector(hit.normal).normalized;
            PlaceKingOnPatch(king.transform, block, localPoint, localNormal, result);
            Undo.RecordObject(king, OperationName);
            king.Configure(partner, surface, localNormal, localPoint);
            PrefabUtility.RecordPrefabInstancePropertyModifications(king);
            return;
        }
        throw new InvalidOperationException("No safe standing patch on the selected king face: " + block.name + "/" + king.name);
    }

    private static float PlanarTriggerRadius(BoxCollider trigger, Vector3 normal)
    {
        float radius = 0f;
        Vector3 extents = trigger.size * 0.5f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = Vector3.Scale(extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            radius = Mathf.Max(radius, Vector3.ProjectOnPlane(trigger.transform.TransformVector(corner), normal).magnitude);
        }
        return radius;
    }

    private static List<Vector3> SurfaceCandidates(Mesh mesh, Vector3 normal)
    {
        List<Vector3> candidates = new List<Vector3>();
        Bounds bounds = mesh.bounds;
        float plane = Vector3.Dot(bounds.center, normal) + Vector3.Dot(bounds.extents,
            new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z)));
        // The Editor API also reads meshes imported with Read/Write disabled.
        using (Mesh.MeshDataArray dataArray = MeshUtility.AcquireReadOnlyMeshData(mesh))
        {
            Mesh.MeshData data = dataArray[0];
            using (NativeArray<Vector3> vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
            {
                data.GetVertices(vertices);
                for (int submesh = 0; submesh < data.subMeshCount; submesh++)
                {
                    var descriptor = data.GetSubMesh(submesh);
                    if (descriptor.topology != MeshTopology.Triangles) continue;
                    using (NativeArray<int> indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                    {
                        data.GetIndices(indices, submesh, true);
                        for (int triangle = 0; triangle + 2 < indices.Length; triangle += 3)
                        {
                            Vector3 a = vertices[indices[triangle]], b = vertices[indices[triangle + 1]], c = vertices[indices[triangle + 2]];
                            Vector3 triangleNormal = Vector3.Cross(b - a, c - a);
                            Vector3 center = (a + b + c) / 3f;
                            if (triangleNormal.sqrMagnitude < 0.00000001f
                                || Vector3.Dot(triangleNormal.normalized, normal) < 0.99f
                                || Mathf.Abs(Vector3.Dot(center, normal) - plane) > 0.035f) continue;
                            candidates.Add(center);
                            // Sampling the triangle interior gives a second king
                            // room on a partly obstructed face and preserves a
                            // closer user placement than a single centroid can.
                            const int divisions = 8;
                            for (int first = 1; first < divisions - 1; first++)
                                for (int second = 1; second < divisions - first; second++)
                                    candidates.Add((a * first + b * second + c * (divisions - first - second)) / divisions);
                        }
                    }
                }
            }
        }
        return candidates;
    }

    private static bool HasFootSupport(Collider surface, Vector3 point, Vector3 normal, PlayerMovement player)
    {
        CharacterController controller = player.GetComponent<CharacterController>();
        float radius = controller.radius * Mathf.Max(Mathf.Abs(player.transform.lossyScale.x), Mathf.Abs(player.transform.lossyScale.z));
        Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 across = Vector3.Cross(normal, tangent).normalized;
        for (int sample = 0; sample < 8; sample++)
        {
            float angle = sample * Mathf.PI * 0.25f;
            Vector3 foot = point + radius * (tangent * Mathf.Cos(angle) + across * Mathf.Sin(angle));
            if (!surface.Raycast(new Ray(foot + normal * 0.05f, -normal), out RaycastHit hit, 0.1f)
                || Vector3.Dot(hit.normal, normal) < 0.99f
                || (hit.point - foot).sqrMagnitude > 0.0001f) return false;
        }
        return true;
    }

    private static void PlaceKingOnPatch(Transform king, Transform block, Vector3 point, Vector3 normal, SetupResult result)
    {
        Bounds visual = MeasureVisualBounds(king);
        Vector3 oldPosition = king.localPosition;
        Quaternion oldRotation = king.localRotation;
        Undo.RecordObject(king, OperationName);
        Vector3 worldNormal = block.worldToLocalMatrix.transpose.MultiplyVector(normal).normalized;
        king.rotation = Quaternion.FromToRotation(king.up, worldNormal) * king.rotation;
        Matrix4x4 relative = Matrix4x4.TRS(Vector3.zero, king.localRotation, king.localScale);
        Vector3 center = relative.MultiplyPoint3x4(visual.center);
        float lowest = float.PositiveInfinity;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = visual.center + Vector3.Scale(visual.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            lowest = Mathf.Min(lowest, Vector3.Dot(relative.MultiplyPoint3x4(corner), normal));
        }
        king.localPosition = point - Vector3.ProjectOnPlane(center, normal) + normal * (0.006f - lowest);
        if ((king.localPosition - oldPosition).sqrMagnitude > 0.00000001f || Quaternion.Angle(oldRotation, king.localRotation) > 0.01f)
            result.relocatedKings++;
        PrefabUtility.RecordPrefabInstancePropertyModifications(king);
    }

    public static Bounds MeasureVisualBounds(Transform root)
    {
        bool hasBounds = false;
        Bounds bounds = new Bounds();
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Bounds mesh = filter.sharedMesh.bounds;
            Matrix4x4 matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mesh.center + Vector3.Scale(mesh.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 point = matrix.MultiplyPoint3x4(corner);
                if (!hasBounds) { bounds = new Bounds(point, Vector3.zero); hasBounds = true; }
                else bounds.Encapsulate(point);
            }
        }
        if (!hasBounds) throw new InvalidOperationException("King has no visible mesh: " + root.name);
        return bounds;
    }

    private static void PlaceOnFace(Transform king, Bounds cube, Bounds visual, int face, System.Random random)
    {
        int axis = face / 2;
        Vector3 normal = Vector3.zero;
        normal[axis] = (face & 1) == 0 ? 1f : -1f;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal)
            * Quaternion.AngleAxis((float)random.NextDouble() * 360f, Vector3.up);
        king.localRotation = rotation;
        Matrix4x4 matrix = Matrix4x4.TRS(Vector3.zero, rotation, king.localScale);
        Bounds rotated = new Bounds(matrix.MultiplyPoint3x4(visual.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            rotated.Encapsulate(matrix.MultiplyPoint3x4(visual.center + Vector3.Scale(visual.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));

        float baseHeight = Vector3.Dot(rotated.center, normal) - rotated.extents[axis];
        Vector3 position = cube.center + normal * (cube.extents[axis] + 0.006f - baseHeight);
        for (int tangent = 0; tangent < 3; tangent++)
        {
            if (tangent == axis) continue;
            float reach = Mathf.Max(0f, cube.extents[tangent] - rotated.extents[tangent] - 0.12f) * 0.65f;
            position[tangent] += ((float)random.NextDouble() * 2f - 1f) * reach - rotated.center[tangent];
        }
        king.localPosition = position;
    }
}
