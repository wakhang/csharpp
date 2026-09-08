using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TrashBagCharacter
{
    [InitializeOnLoad]
    public static class TrashBagCharacterBuilder
    {
        static TrashBagCharacterBuilder()
        {
            EditorApplication.delayCall += BuildIfRequested;
        }

        private static void BuildIfRequested()
        {
            // Auto-run once if character doesn't exist yet
            if (!File.Exists("Assets/TrashBagCharacter/TrashBagCharacter.prefab"))
            {
                BuildTrashBagCharacter();
            }
        }

        private class AnalyzedClip
        {
            public string SourceUsdzPath;
            public AnimationClip OriginalClip;
            public float DeltaX;
            public float DeltaZ;
            public float AvgTiltX;
            public float AvgTiltZ;
            public string InferredDirection; // "Forward", "Backward", "Left", "Right"
        }

        [MenuItem("TrashBag/Build 3rd Person Character")]
        public static void BuildTrashBagCharacter()
        {
            Debug.Log("================ START BUILDING TRASH BAG CHARACTER ================");

            // 1. Ensure folders exist
            string baseFolder = "Assets/TrashBagCharacter";
            string animFolder = "Assets/TrashBagCharacter/Animations";
            if (!AssetDatabase.IsValidFolder("Assets/TrashBagCharacter"))
                AssetDatabase.CreateFolder("Assets", "TrashBagCharacter");
            if (!AssetDatabase.IsValidFolder("Assets/TrashBagCharacter/Animations"))
                AssetDatabase.CreateFolder("Assets/TrashBagCharacter", "Animations");

            // 2. Find all USDZ assets under VARCO3DImports
            string root = "Assets/VARCO3DImports";
            if (!Directory.Exists(root))
            {
                Debug.LogError("[TrashBag] No Assets/VARCO3DImports folder found!");
                return;
            }

            string[] usdzFiles = Directory.GetFiles(root, "*.usdz", SearchOption.AllDirectories)
                .Select(f => f.Replace('\\', '/'))
                .ToArray();

            if (usdzFiles.Length == 0)
            {
                Debug.LogError("[TrashBag] No USDZ files found under Assets/VARCO3DImports!");
                return;
            }

            Debug.Log($"[TrashBag] Found {usdzFiles.Length} USDZ files.");

            // 3. Analyze each USDZ file and extract its AnimationClip
            List<AnalyzedClip> analyzedClips = new List<AnalyzedClip>();
            string primaryUsdzPath = usdzFiles[0];

            foreach (var usdzPath in usdzFiles)
            {
                Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(usdzPath);
                var clips = allAssets.OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__"))
                    .ToArray();

                if (clips.Length == 0)
                {
                    Debug.LogWarning($"[TrashBag] No clips found in {usdzPath}");
                    continue;
                }

                AnimationClip clip = clips[0];
                var analyzed = AnalyzeClipMotion(clip, usdzPath);
                analyzedClips.Add(analyzed);
                Debug.Log($"[TrashBag] Loaded clip '{clip.name}' from {Path.GetFileName(Path.GetDirectoryName(usdzPath))}: DeltaX={analyzed.DeltaX:F3}, DeltaZ={analyzed.DeltaZ:F3}, TiltX={analyzed.AvgTiltX:F3}, TiltZ={analyzed.AvgTiltZ:F3}");
            }

            if (analyzedClips.Count == 0)
            {
                Debug.LogError("[TrashBag] Could not extract any valid AnimationClips from USDZ files.");
                return;
            }

            // 4. Assign directional roles (Forward, Backward, Left, Right)
            AssignDirections(analyzedClips);

            // 5. Create standalone looping AnimationClips
            AnimationClip clipForward = null;
            AnimationClip clipBackward = null;
            AnimationClip clipLeft = null;
            AnimationClip clipRight = null;

            foreach (var ac in analyzedClips)
            {
                string targetFileName = $"TrashBag_Run_{ac.InferredDirection}.anim";
                string targetPath = $"{animFolder}/{targetFileName}";

                AnimationClip cloned = Object.Instantiate(ac.OriginalClip);
                cloned.name = $"TrashBag_Run_{ac.InferredDirection}";

                // Enable looping
                var settings = AnimationUtility.GetAnimationClipSettings(cloned);
                settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(cloned, settings);

                AssetDatabase.CreateAsset(cloned, targetPath);
                Debug.Log($"[TrashBag] Created looping clip: {targetPath} (assigned to {ac.InferredDirection})");

                switch (ac.InferredDirection)
                {
                    case "Forward": clipForward = cloned; break;
                    case "Backward": clipBackward = cloned; break;
                    case "Left": clipLeft = cloned; break;
                    case "Right": clipRight = cloned; break;
                }
            }

            // Fallbacks in case count was less than 4
            if (clipForward == null) clipForward = analyzedClips[0].OriginalClip;
            if (clipBackward == null) clipBackward = clipForward;
            if (clipLeft == null) clipLeft = clipForward;
            if (clipRight == null) clipRight = clipForward;

            // 6. Create Idle animation (single frame static pose based on bind pose)
            AnimationClip clipIdle = CreateIdleClip(clipForward, $"{animFolder}/TrashBag_Idle.anim");

            // 7. Create AnimatorController with 2D Blend Tree
            string controllerPath = $"{baseFolder}/TrashBag_Controller.controller";
            AnimatorController controller = CreateBlendTreeController(controllerPath, clipIdle, clipForward, clipBackward, clipLeft, clipRight);

            // 8. Create Character GameObject & Prefab
            GameObject characterGO = AssembleCharacterGameObject(primaryUsdzPath, controller);

            // Save Prefab
            string prefabPath = $"{baseFolder}/TrashBagCharacter.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(characterGO, prefabPath, InteractionMode.AutomatedAction);
            Debug.Log($"[TrashBag] Saved Character Prefab at: {prefabPath}");

            // 9. Setup Scene: Ground, Camera, Clean up raw USDZ instances
            SetupSceneWithCharacter(characterGO);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("================ TRASH BAG CHARACTER COMPLETED SUCCESSFULLY! ================");
        }

        private static AnalyzedClip AnalyzeClipMotion(AnimationClip clip, string usdzPath)
        {
            AnalyzedClip result = new AnalyzedClip
            {
                SourceUsdzPath = usdzPath,
                OriginalClip = clip
            };

            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var b in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                if (curve == null || curve.keys.Length < 2) continue;

                float startVal = curve.keys[0].value;
                float endVal = curve.keys[curve.keys.Length - 1].value;
                float delta = endVal - startVal;

                string propLower = b.propertyName.ToLower();
                string pathLower = b.path.ToLower();

                // Look for root / hip position
                if (propLower.Contains("position"))
                {
                    if (propLower.EndsWith(".x")) result.DeltaX += delta;
                    if (propLower.EndsWith(".z")) result.DeltaZ += delta;
                }
                // Look for root / spine / hip tilt
                else if (propLower.Contains("rotation") || propLower.Contains("euler"))
                {
                    if (pathLower.Contains("root") || pathLower.Contains("skel") || pathLower.Contains("hip") || pathLower.Contains("spine") || string.IsNullOrEmpty(b.path))
                    {
                        if (propLower.EndsWith(".x")) result.AvgTiltX += (curve.Evaluate(clip.length * 0.5f) - startVal);
                        if (propLower.EndsWith(".z")) result.AvgTiltZ += (curve.Evaluate(clip.length * 0.5f) - startVal);
                    }
                }
            }

            return result;
        }

        private static void AssignDirections(List<AnalyzedClip> clips)
        {
            if (clips.Count == 4)
            {
                // If translation delta is non-trivial, sort by delta
                bool hasTranslation = clips.Any(c => Mathf.Abs(c.DeltaX) > 0.05f || Mathf.Abs(c.DeltaZ) > 0.05f);
                if (hasTranslation)
                {
                    // Forward = Max DeltaZ
                    var fwd = clips.OrderByDescending(c => c.DeltaZ).First();
                    fwd.InferredDirection = "Forward";
                    var remaining = clips.Where(c => c != fwd).ToList();

                    // Backward = Min DeltaZ
                    var bwd = remaining.OrderBy(c => c.DeltaZ).First();
                    bwd.InferredDirection = "Backward";
                    remaining.Remove(bwd);

                    // Right = Max DeltaX
                    var right = remaining.OrderByDescending(c => c.DeltaX).First();
                    right.InferredDirection = "Right";
                    remaining.Remove(right);

                    // Left = Remaining
                    remaining[0].InferredDirection = "Left";
                    return;
                }

                // If in-place, check tilt or order
                bool hasTilt = clips.Any(c => Mathf.Abs(c.AvgTiltX) > 0.01f || Mathf.Abs(c.AvgTiltZ) > 0.01f);
                if (hasTilt)
                {
                    var fwd = clips.OrderByDescending(c => c.AvgTiltX).First();
                    fwd.InferredDirection = "Forward";
                    var remaining = clips.Where(c => c != fwd).ToList();

                    var bwd = remaining.OrderBy(c => c.AvgTiltX).First();
                    bwd.InferredDirection = "Backward";
                    remaining.Remove(bwd);

                    var right = remaining.OrderByDescending(c => c.AvgTiltZ).First();
                    right.InferredDirection = "Right";
                    remaining.Remove(right);

                    remaining[0].InferredDirection = "Left";
                    return;
                }
            }

            // Fallback: assign sequentially (Forward, Backward, Left, Right)
            string[] dirs = { "Forward", "Backward", "Left", "Right" };
            for (int i = 0; i < clips.Count; i++)
            {
                clips[i].InferredDirection = dirs[i % dirs.Length];
            }
        }

        private static AnimationClip CreateIdleClip(AnimationClip sourceClip, string savePath)
        {
            AnimationClip idleClip = new AnimationClip();
            idleClip.name = "TrashBag_Idle";
            idleClip.frameRate = 30f;

            var bindings = AnimationUtility.GetCurveBindings(sourceClip);
            foreach (var b in bindings)
            {
                var origCurve = AnimationUtility.GetEditorCurve(sourceClip, b);
                if (origCurve != null && origCurve.keys.Length > 0)
                {
                    float firstVal = origCurve.keys[0].value;
                    // Create a static 2-key curve at t=0 and t=1
                    AnimationCurve idleCurve = new AnimationCurve(
                        new Keyframe(0f, firstVal),
                        new Keyframe(1f, firstVal)
                    );
                    AnimationUtility.SetEditorCurve(idleClip, b, idleCurve);
                }
            }

            var settings = AnimationUtility.GetAnimationClipSettings(idleClip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(idleClip, settings);

            AssetDatabase.CreateAsset(idleClip, savePath);
            Debug.Log($"[TrashBag] Created Idle clip at: {savePath}");
            return idleClip;
        }

        private static AnimatorController CreateBlendTreeController(
            string controllerPath,
            AnimationClip idleClip,
            AnimationClip runForward,
            AnimationClip runBackward,
            AnimationClip runLeft,
            AnimationClip runRight)
        {
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            // Add parameters
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

            // 1. Idle State
            AnimatorState idleState = stateMachine.AddState("Idle");
            idleState.motion = idleClip;
            stateMachine.defaultState = idleState;

            // 2. Run Blend Tree State
            BlendTree blendTree;
            AnimatorState runState = controller.CreateBlendTreeInController("Run_BlendTree", out blendTree);
            runState.name = "Run";

            blendTree.blendType = BlendTreeType.SimpleDirectional2D;
            blendTree.blendParameter = "MoveX";
            blendTree.blendParameterY = "MoveZ";

            // Add the 4 running clips at cardinal coordinates
            blendTree.AddChild(runForward, new Vector2(0f, 1f));
            blendTree.AddChild(runBackward, new Vector2(0f, -1f));
            blendTree.AddChild(runLeft, new Vector2(-1f, 0f));
            blendTree.AddChild(runRight, new Vector2(1f, 0f));

            // 3. Transitions between Idle and Run
            AnimatorStateTransition idleToRun = idleState.AddTransition(runState);
            idleToRun.hasExitTime = false;
            idleToRun.duration = 0.15f;
            idleToRun.AddCondition(AnimatorConditionMode.Greater, 0.05f, "Speed");

            AnimatorStateTransition runToIdle = runState.AddTransition(idleState);
            runToIdle.hasExitTime = false;
            runToIdle.duration = 0.15f;
            runToIdle.AddCondition(AnimatorConditionMode.Less, 0.05f, "Speed");

            EditorUtility.SetDirty(controller);
            Debug.Log($"[TrashBag] Created 2D Blend Tree AnimatorController at: {controllerPath}");
            return controller;
        }

        private static GameObject AssembleCharacterGameObject(string baseUsdzPath, AnimatorController controller)
        {
            // Clean up any existing character in scene
            GameObject existing = GameObject.Find("TrashBagCharacter");
            if (existing != null)
                Object.DestroyImmediate(existing);

            // Instantiate base USDZ prefab
            GameObject usdzPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(baseUsdzPath);
            if (usdzPrefab == null)
            {
                Debug.LogError($"[TrashBag] Failed to load prefab from {baseUsdzPath}");
                return null;
            }

            // Create root character GameObject
            GameObject characterRoot = new GameObject("TrashBagCharacter");
            characterRoot.transform.position = Vector3.zero;
            characterRoot.transform.rotation = Quaternion.identity;

            // Instantiate visual model as child
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(usdzPrefab, characterRoot.transform);
            visual.name = "TrashBagModel";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // Compute bounds from SkinnedMeshRenderers
            var smrs = visual.GetComponentsInChildren<SkinnedMeshRenderer>();
            Bounds bounds = new Bounds(visual.transform.position, Vector3.zero);
            bool hasBounds = false;
            foreach (var smr in smrs)
            {
                if (!hasBounds)
                {
                    bounds = smr.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(smr.bounds);
                }
            }

            float height = hasBounds ? Mathf.Max(0.6f, bounds.size.y) : 0.8f;
            float radius = hasBounds ? Mathf.Max(0.2f, Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.75f) : 0.35f;

            // Add CharacterController
            CharacterController cc = characterRoot.AddComponent<CharacterController>();
            cc.height = height;
            cc.radius = radius;
            cc.center = new Vector3(0f, height * 0.5f, 0f);
            cc.stepOffset = 0.3f;
            cc.slopeLimit = 45f;

            // Ensure Animator is configured
            Animator animator = visual.GetComponent<Animator>();
            if (animator == null)
                animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; // We use CharacterController for motion!

            // Add Player Controller script
            TrashBagPlayerController playerController = characterRoot.AddComponent<TrashBagPlayerController>();
            playerController.animator = animator;
            playerController.runSpeed = 5.0f;
            playerController.strafeMode = true;

            return characterRoot;
        }

        private static void SetupSceneWithCharacter(GameObject character)
        {
            Scene scene = SceneManager.GetActiveScene();

            // 1. Remove raw imported USDZ objects that VARCO3D placed as loose objects
            var rootObjects = scene.GetRootGameObjects();
            foreach (var go in rootObjects)
            {
                // Check if it's one of the raw GUID names
                if (go != character && (go.name.Contains("-") && go.name.Length >= 32))
                {
                    Debug.Log($"[TrashBag] Cleaning up loose imported instance: {go.name}");
                    Object.DestroyImmediate(go);
                }
            }

            // 2. Check for ground/floor
            bool hasFloor = false;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.GetComponent<Collider>() != null && go != character)
                {
                    hasFloor = true;
                    break;
                }
            }

            if (!hasFloor)
            {
                GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.transform.position = Vector3.zero;
                ground.transform.localScale = new Vector3(5f, 1f, 5f); // 50m x 50m
                Debug.Log("[TrashBag] Created default Ground Plane.");
            }

            // 3. Setup Camera
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                GameObject camGO = new GameObject("Main Camera");
                mainCam = camGO.AddComponent<Camera>();
                camGO.tag = "MainCamera";
                camGO.AddComponent<AudioListener>();
            }

            TrashBagCameraController camController = mainCam.GetComponent<TrashBagCameraController>();
            if (camController == null)
                camController = mainCam.gameObject.AddComponent<TrashBagCameraController>();

            camController.target = character.transform;
            camController.distance = 2.5f;
            camController.targetOffset = new Vector3(0f, 0.6f, 0f);

            // Connect camera to player controller
            TrashBagPlayerController playerController = character.GetComponent<TrashBagPlayerController>();
            if (playerController != null)
                playerController.playerCamera = mainCam;

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[TrashBag] Scene configured with Character and Follow Camera.");
        }
    }
}
