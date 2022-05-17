using LEGOModelImporter;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.LEGO.Utilities;
using UnityEngine;

namespace Unity.LEGO.Behaviours.Actions
{
    public class SpawnAction : RepeatableAction
    {
        public enum SpawnMethod
        {
            BuildFromAroundModel,
            BuildFromSpawnAction,
            PopIn,
            Appear
        }

        public enum SpawnShape
        {
            Box,
            Sphere,
            Point
        }

        public enum SpawnOrientation
        {
            Specific,
            Random,
            RandomXAxis,
            RandomYAxis,
            RandomZAxis
        }

        [SerializeField, Tooltip("The LEGO model to spawn. Only prefabs are allowed.")]
        GameObject m_Model = default;

        [HideInInspector, SerializeField]
        AudioClip m_BrickSnapAudio = default;

        [SerializeField, Tooltip("The effect used when the LEGO model has been spawned.")]
        ParticleSystem m_Effect = null;

        [SerializeField, Tooltip("Build the spawned LEGO model with bricks appearing from around the model.\nOr\nBuild the spawned LEGO model with bricks appearing from the Spawn Action.\nOr\nPop the spawned LEGO model in.\nOr\nMake the spawned LEGO model appear with no effects.")]
        SpawnMethod m_SpawnMethod = SpawnMethod.BuildFromAroundModel;

        [SerializeField, Tooltip("Spawn the LEGO model within a box.\nOr\nSpawn the LEGO model within a sphere.\nOr\nSpawn the LEGO model on a specific point.")]
        SpawnShape m_SpawnAreaShape = SpawnShape.Box;

        [SerializeField, Tooltip("The center of the spawn area.")]
        Vector3 m_SpawnAreaCenter = Vector3.up;

        [SerializeField, Tooltip("The size of the box spawn area.")]
        Vector3 m_SpawnAreaSize = Vector3.one * 15.0f;

        [SerializeField, Tooltip("The radius of the sphere spawn area.")]
        float m_SpawnAreaRadius = 15.0f;

        [SerializeField, Tooltip("Spawn the LEGO model on the ground below the spawn area.")]
        bool m_SnapToGround = true;

        [SerializeField, Tooltip("Spawn the LEGO model with a specific orientation.\nOr\nSpawn the LEGO model with a randomized orientation.\nOr\nSpawn the LEGO model with only one rotation axis value randomized.")]
        SpawnOrientation m_SpawnOrientationType = SpawnOrientation.Specific;

        [SerializeField, Tooltip("The orientation of the spawned LEGO model in degrees.")]
        Vector3 m_SpawnOrientation = Vector3.zero;

        [SerializeField, Tooltip("Check for collisions when spawning the LEGO model.")]
        bool m_Collide = true;

        [SerializeField, Tooltip("The time in seconds to build the spawned LEGO model.")]
        float m_BuildTime = 4.0f;

        enum State
        {
            Spawning,
            Building,
            PoppingIn,
            Waiting
        }

        State m_State;

        AnimationCurve m_SpawnCurve = new AnimationCurve( new Keyframe[] { new Keyframe(0.0f, 0.0f, 0.0f, 0.0f), new Keyframe(1.0f, 1.0f, 2.0f, 2.0f) } );
        AnimationCurve m_AnimationTimeCurve = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 0.5f);

        const float k_StartingBrickEpsilon = 0.1f;

        const int k_MaxSpawnAttempts = 10;
        const float k_MaxBrickScaleTime = 0.5f;
        const float k_BrickScaleFlyInTimeScale = 0.3f;
        const float k_BrickFlyInFromAroundModelPositionOffset = 4.0f;
        const float k_BrickFlyInTimeScale = 1.5f; // It's important that m_AnimationTimeCurve.Evaluate(1.0f) * (k_BrickFlyInTimeScale + k_BrickSnapPauseTimeScale) is less than 1 to have enough time to spawn.
        const float k_BrickFlyInOffset = 7.0f;
        const float k_BrickSnapPauseTimeScale = 0.3f;
        const float k_NudgeTime = 0.2f;
        const float k_FinishedGestureTime = 0.6f;
        const float k_PopInTime = 0.5f;

        const float kSnapEpsilon = 0.01f;

        const int k_MinParticleBurst = 10;
        const int k_MaxParticleBurst = 100;
        const float k_ParticleBurstPerModuleVolume = 0.5f;
        const float k_ParticleBurstPerModuleArea = 0.15f;

        LayerMask m_LayerMask;

        float m_Time;

        float m_SpawnTime;

        float m_BuildTimePerBrick;
        int m_NextBrickSpawnIndex;
        bool m_HasPlayedFinishedGestureAudio;

        Bounds m_CurrentBounds;

        Vector3 m_ModelSpawnPosition;

        ParticleSystem m_ParticleSystem;

        GameObject m_ModelCopy;

        class BrickCopy
        {
            public Brick Brick;
            public Brick ConnectedToBrick;
            public Vector3 ModelGroupLocalPositionInModel;
            public Vector3 ConnectionOffset;
            public bool IsStartingBrick;
        }

        class BrickCopyYPositionComparer : IComparer<BrickCopy>
        {
            public int Compare(BrickCopy a, BrickCopy b)
            {
                var pointA = a.Brick.transform.TransformPoint(a.Brick.totalBounds.center);
                var pointB = b.Brick.transform.TransformPoint(b.Brick.totalBounds.center);

                var compareY = pointA.y.CompareTo(pointB.y);
                if (compareY != 0)
                {
                    return compareY;
                }

                // Make sure that two bricks with the same y-value are still separable.
                var compareX = pointA.x.CompareTo(pointB.x);
                if (compareX != 0)
                {
                    return compareX;
                }
                return pointA.z.CompareTo(pointB.z);
            }
        }

        List<BrickCopy> m_BrickCopies = new List<BrickCopy>();
        List<Brick> m_AnimatedBricks = new List<Brick>();

        // Shader animation variables.
        List<List<Material>> m_DeformedMaterials = new List<List<Material>>();
        
        static readonly int s_DeformMatrix1ID = Shader.PropertyToID("_DeformMatrix1");
        static readonly int s_DeformMatrix2ID = Shader.PropertyToID("_DeformMatrix2");
        static readonly int s_DeformMatrix3ID = Shader.PropertyToID("_DeformMatrix3");

        protected override void Reset()
        {
            base.Reset();

            m_IconPath = "Assets/LEGO/Gizmos/LEGO Behaviour Icons/Spawn Action.png";
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            
            m_BuildTime = Mathf.Max(m_BuildTime, 2.0f);
            m_Pause = Mathf.Max(0.25f, m_Pause);
        }

        protected override void Start()
        {
            base.Start();

            if (IsPlacedOnBrick())
            {
                // Add particle system.
                if (m_Effect)
                {
                    m_ParticleSystem = Instantiate(m_Effect, transform);
                    m_ParticleSystem.Stop();
                }

                m_LayerMask = ~LayerMask.GetMask("Environment");

                m_ParticleSystem = GetComponentInChildren<ParticleSystem>();

                if (m_Model && (m_Model.GetComponent<Model>() || m_Model.GetComponent<ModelGroup>() || m_Model.GetComponent<Brick>()))
                {
                    SetupModelCopy();
                }
            }
        }

        protected void Update()
        {
            if (m_Active && m_ModelCopy)
            {
                m_Time += Time.deltaTime;

                // Trying to spawn the model.
                if (m_State == State.Spawning)
                {
                    if (SpawnModel())
                    {
                        if (m_SpawnMethod == SpawnMethod.BuildFromAroundModel || m_SpawnMethod == SpawnMethod.BuildFromSpawnAction)
                        {
                            m_State = State.Building;
                        }
                        else if (m_SpawnMethod == SpawnMethod.PopIn)
                        {
                            m_State = State.PoppingIn;
                        }
                        else
                        {
                            m_State = State.Waiting;
                        }
                    }
                    else
                    {
                        m_Time -= Time.deltaTime;
                    }
                }

                // Building the model.
                if (m_State == State.Building)
                {
                    // Spawn bricks.
                    var spawnValue = Mathf.Clamp01(m_Time / m_SpawnTime);

                    var bricksThatShouldBeSpawned = Mathf.Floor(m_SpawnCurve.Evaluate(spawnValue) * m_BrickCopies.Count);
                    var animationSpeed = m_BuildTimePerBrick * m_AnimationTimeCurve.Evaluate(spawnValue);

                    while (m_NextBrickSpawnIndex < bricksThatShouldBeSpawned)
                    {
                        StartCoroutine(AnimationBrickHandler(m_NextBrickSpawnIndex, brickFlyInTime: animationSpeed * k_BrickFlyInTimeScale, snapInPlacePause: animationSpeed * k_BrickSnapPauseTimeScale));

                        m_NextBrickSpawnIndex++;
                    }

                    // Do the final gesture and play audio.
                    if (m_Time >= m_BuildTime - k_FinishedGestureTime)
                    {
                        var clampedTime = Mathf.Min(1.0f, (m_Time - (m_BuildTime - k_FinishedGestureTime)) / k_FinishedGestureTime);
                        m_ModelCopy.transform.position = MathUtility.QuadraticBezier(m_ModelSpawnPosition, m_ModelSpawnPosition + Vector3.up, m_ModelSpawnPosition, clampedTime);

                        if (!m_HasPlayedFinishedGestureAudio)
                        {
                            PlayAudio();
                            m_HasPlayedFinishedGestureAudio = true;
                        }
                    }

                    if (m_Time >= m_BuildTime)
                    {
                        m_Time -= m_BuildTime;

                        m_ModelCopy.SetActive(false);

                        InstantiateModel(m_ModelCopy.transform.position, m_ModelCopy.transform.rotation);

                        if (m_SnapToGround)
                        {
                            var position = new Vector3(m_CurrentBounds.center.x, m_CurrentBounds.min.y, m_CurrentBounds.center.z);
                            PlayParticleEffect(ParticleSystemShapeType.Rectangle, m_CurrentBounds.size, position);
                        }

                        m_State = State.Waiting;
                    }
                }

                // Popping the model in.
                if (m_State == State.PoppingIn)
                {
                    m_ModelCopy.transform.localScale = Vector3.one * MathUtility.EaseOutBack(m_Time, k_PopInTime);

                    if (m_Time >= k_PopInTime)
                    {
                        m_Time -= k_PopInTime;

                        m_ModelCopy.SetActive(false);

                        InstantiateModel(m_ModelCopy.transform.position, m_ModelCopy.transform.rotation);

                        m_State = State.Waiting;
                    }

                }

                // Waiting for next model spawn.
                if (m_State == State.Waiting)
                {
                    if (m_Time >= m_Pause)
                    {
                        m_Time -= m_Pause;
                        m_State = State.Spawning;
                        m_HasPlayedFinishedGestureAudio = false;
                        m_Active = m_Repeat;
                    }
                }
            }
        }

        void SetupModelCopy()
        {
            m_ModelCopy = InstantiateModel(Vector3.zero, Quaternion.identity);
            m_ModelCopy.SetActive(false);

            // Find and disable LEGO behaviours on copy object.
            foreach (var behaviour in m_ModelCopy.GetComponentsInChildren<LEGOBehaviour>(true))
            {
                behaviour.enabled = false;
            }

            // Setup for building if enabled.
            if (m_SpawnMethod == SpawnMethod.BuildFromAroundModel || m_SpawnMethod == SpawnMethod.BuildFromSpawnAction)
            {
                // Find bricks and activate all knobs and tubes.
                var bricks = m_ModelCopy.GetComponentsInChildren<Brick>(true);

                foreach (var child in bricks.Select(brick => brick.GetComponentsInChildren<Knob>(true)).SelectMany(children => children))
                {
                    child.gameObject.SetActive(true);
                }
                foreach (var child in bricks.Select(brick => brick.GetComponentsInChildren<Tube>(true)).SelectMany(children => children))
                {
                    child.gameObject.SetActive(true);
                }

                // Find the GameObject to place animation bricks in the hierarchy, create it if it doesn't exist.
                var copyParent = GameObject.Find("Spawn Action Brick Copies");
                if (!copyParent)
                {
                    copyParent = new GameObject("Spawn Action Brick Copies");
                }

                // Determine the build order of the bricks in the model.
                m_BrickCopies = DetermineBuildOrder(m_ModelCopy, bricks);

                // Instantiate animation bricks to be used for the build animation.
                foreach (var brick in m_BrickCopies)
                {
                    var animatedBrick = Instantiate(brick.Brick.gameObject, Vector3.zero, brick.Brick.transform.rotation, copyParent.transform);
                    animatedBrick.SetActive(false);

                    // Add velocity tracker to track animation brick velocity in case the action is destroyed.
                    animatedBrick.AddComponent<VelocityTracker>();

                    // Disable all colliders on animation brick.
                    foreach (var brickCollider in animatedBrick.GetComponentsInChildren<Collider>(true))
                    {
                        brickCollider.enabled = false;
                    }

                    // Apply shader to be used for scaling the animation brick when spawned.
                    SetDeformedShader(animatedBrick, out var deformedMaterials);
                    m_DeformedMaterials.Add(deformedMaterials);

                    m_AnimatedBricks.Add(animatedBrick.GetComponent<Brick>());

                    // Deactivate brick copy.
                    brick.Brick.gameObject.SetActive(false);
                }
            }
        }

        static List<BrickCopy> DetermineBuildOrder(GameObject modelCopy, Brick[] modelCopyBricks)
        {
            var result = new List<BrickCopy>(); // Will contain all bricks and required information in build order.

            var startingBricks = new List<List<Brick>>(); // Will contain starting bricks for each model group.

            // 1. Find starting bricks in each model group.
            foreach (var modelGroup in modelCopy.GetComponentsInChildren<ModelGroup>())
            {
                var modelGroupBricks = modelCopyBricks.Where(brick => brick.transform.parent.gameObject == modelGroup.gameObject).ToList();

                var lowestBrickPoint = modelGroupBricks.Min(brick => brick.transform.TransformPoint(brick.totalBounds.center).y) + k_StartingBrickEpsilon;
                var candidateStartingBricks = modelGroupBricks.Where(brick => brick.transform.TransformPoint(brick.totalBounds.center).y <= lowestBrickPoint).ToList();

                // Find and remove all connected starting bricks. 
                var bricksToRemove = new List<Brick>();
                foreach (var startingBrick in candidateStartingBricks)
                {
                    bricksToRemove.AddRange(FindConnectedStartingBricks(startingBrick, startingBrick, candidateStartingBricks, new List<Brick>()));
                }

                startingBricks.Add(candidateStartingBricks.Except(bricksToRemove).ToList());
            }

            // 2. Sort model groups by the y-position of the first starting brick in each model group.
            startingBricks = startingBricks.OrderBy(bricks => bricks.First().transform.TransformPoint(bricks.First().totalBounds.center).y).ToList();

            // 3. For each model group, add all the bricks to the final build order starting with the starting bricks.
            foreach (var bricks in startingBricks)
            {
                // Add all the starting bricks and shuffle them.
                var modelGroupResult = bricks
                    .Select(brick => new BrickCopy { Brick = brick, ModelGroupLocalPositionInModel = brick.transform.parent.localPosition, IsStartingBrick = true })
                    .OrderBy(brickCopy => Random.value)
                    .ToList();

                // Create a sorted set that sorts by the y-position of bricks. Everything added to this set will be automatically sorted. The first element will always be the lowest brick.
                var connectedBricksSet = new SortedSet<BrickCopy>(new BrickCopyYPositionComparer());

                // Fill in the immediately connected bricks into the sorted set.
                foreach (var brick in bricks)
                {
                    foreach (var connectedBrick in brick.GetConnectedBricks(false))
                    {
                        connectedBricksSet.Add(new BrickCopy { Brick = connectedBrick, ConnectedToBrick = brick, ModelGroupLocalPositionInModel = brick.transform.parent.localPosition });
                    }
                }

                // Repeatedly add the lowest connected brick to the model group result until all bricks in the model group have been added.
                while (connectedBricksSet.Count > 0)
                {
                    var currentBrick = connectedBricksSet.First();
                    connectedBricksSet.Remove(currentBrick);

                    modelGroupResult.Add(currentBrick);

                    // Add all connected bricks that are not already in the result to the sorted set.
                    foreach (var brick in currentBrick.Brick.GetConnectedBricks(false))
                    {
                        if (modelGroupResult.All(brickCopy => brick != brickCopy.Brick))
                        {
                            connectedBricksSet.Add(new BrickCopy { Brick = brick, ConnectedToBrick = currentBrick.Brick, ModelGroupLocalPositionInModel = brick.transform.parent.localPosition });
                        }
                    }
                }

                result.AddRange(modelGroupResult);
            }

            // 4. Find connection directions for each brick that has a connected brick.
            foreach (var brickCopy in result.Where(brickCopy => brickCopy.ConnectedToBrick))
            {
                foreach (var part in brickCopy.Brick.parts)
                {
                    foreach (var field in part.connectivity.planarFields)
                    {
                        var connections = field.GetConnectedConnections();
                        foreach (var connection in connections)
                        {
                            var currentConnection = field.GetConnection(connection);
                            if (currentConnection.field.connectivity.part.brick == brickCopy.ConnectedToBrick)
                            {
                                brickCopy.ConnectionOffset = currentConnection.GetPreconnectOffset();
                                goto FoundConnectionOffset;
                            }
                        }
                    }

                    foreach (var field in part.connectivity.axleFields)
                    {
                        var connections = field.connectedTo;
                        foreach (var connection in connections)
                        {
                            if (connection.field.connectivity.part.brick == brickCopy.ConnectedToBrick)
                            {
                                brickCopy.ConnectionOffset = connection.field.feature.GetPreconnectOffset(field.feature);
                                goto FoundConnectionOffset;
                            }
                        }
                    }
                }
            FoundConnectionOffset: { }
            }

            return result;
        }

        static ICollection<Brick> FindConnectedStartingBricks(Brick initialBrick, Brick currentBrick, ICollection<Brick> candidateStartingBricks, ICollection<Brick> connectedStartingBricks)
        {
            foreach (var connectedBrick in currentBrick.GetConnectedBricks(false))
            {
                if (candidateStartingBricks.Contains(connectedBrick) && !connectedStartingBricks.Contains(connectedBrick) && connectedBrick != initialBrick)
                {
                    connectedStartingBricks.Add(connectedBrick);
                    connectedStartingBricks = FindConnectedStartingBricks(initialBrick, connectedBrick, candidateStartingBricks, connectedStartingBricks);
                }
            }

            return connectedStartingBricks;
        }

        void SetDeformedShader(GameObject brickObject, out List<Material> deformedMaterials)
        {
            deformedMaterials = new List<Material>();
            var renderers = brickObject.GetComponentsInChildren<MeshRenderer>();

            // Change the shader of all scoped part renderers.
            foreach (var partRenderer in renderers)
            {
                // The renderQueue value is reset when changing the shader, so transfer it.
                var renderQueue = partRenderer.material.renderQueue;
                partRenderer.material.shader = Shader.Find("Deformed");
                partRenderer.material.renderQueue = renderQueue;

                deformedMaterials.Add(partRenderer.material);
            }
        }

        bool SpawnModel()
        {
            var rotation = GetModelSpawnOrientation();

            for (var i = 0; i < k_MaxSpawnAttempts; i++)
            {
                Vector3 position;

                if (!GetModelSpawnPosition(out position))
                {
                    continue;
                }

                // Transform model copy and get scoped bounds to find offset and spawn position for current spawn attempt.
                m_ModelCopy.transform.rotation = rotation;
                m_ModelCopy.transform.position = position;
                m_CurrentBounds = GetScopedBounds(m_ModelCopy.GetComponentsInChildren<Brick>(true), out _, out _);

                var offset = position - m_CurrentBounds.center;
                if (m_SnapToGround)
                {
                    offset += Vector3.up * m_CurrentBounds.extents.y;
                }

                m_ModelSpawnPosition = position + offset;
                m_CurrentBounds.center += offset;

                if (!m_Collide || !SpawnCollisionCheck())
                {
                    m_ModelCopy.transform.position = m_ModelSpawnPosition;

                    if (m_SpawnMethod == SpawnMethod.BuildFromAroundModel || m_SpawnMethod == SpawnMethod.BuildFromSpawnAction)
                    {
                        SetupModelBuilding();
                    }
                    else if (m_SpawnMethod == SpawnMethod.PopIn)
                    {
                        SetupModelPopIn();

                        PlayAudio();

                        PlayParticleEffect(ParticleSystemShapeType.Box, m_CurrentBounds.size, m_CurrentBounds.center);
                    }
                    else
                    {
                        InstantiateModel(m_ModelSpawnPosition, rotation);

                        PlayAudio();
                    }

                    return true;
                }
            }

            return false;
        }

        GameObject InstantiateModel(Vector3 position, Quaternion rotation)
        {
            var modelGO = Instantiate(m_Model, position, rotation);

            // Wrap root brick in model group.
            var rootBrick = modelGO.GetComponent<Brick>();
            if (rootBrick)
            {
                var modelGroupWrapperGO = new GameObject("ModelGroup wrapper");
                modelGroupWrapperGO.transform.position = position;
                modelGroupWrapperGO.transform.rotation = rotation;
                modelGroupWrapperGO.AddComponent<ModelGroup>();
                modelGO.transform.parent = modelGroupWrapperGO.transform;
                modelGO = modelGroupWrapperGO;
            }

            // Wrap root model group in model.
            var rootModelGroup = modelGO.GetComponent<ModelGroup>();
            if (rootModelGroup)
            {
                var modelWrapperGO = new GameObject("Model wrapper");
                modelWrapperGO.transform.position = position;
                modelWrapperGO.transform.rotation = rotation;
                modelWrapperGO.AddComponent<Model>();
                modelGO.transform.parent = modelWrapperGO.transform;
                modelGO = modelWrapperGO;
            }

            return modelGO;
        }

        void SetupModelBuilding()
        {
            // Calculate the time during which all bricks must spawn in order for the model to be finished after the build time.
            m_SpawnTime = m_BuildTime - k_NudgeTime - k_FinishedGestureTime;
            m_BuildTimePerBrick = m_SpawnTime / m_BrickCopies.Count;

            var lastBrickFlyInTime = m_BuildTimePerBrick * m_AnimationTimeCurve.Evaluate(1.0f) * k_BrickFlyInTimeScale;
            var lastBrickSnapTime = m_BuildTimePerBrick * m_AnimationTimeCurve.Evaluate(1.0f) * k_BrickSnapPauseTimeScale;
            m_SpawnTime -= lastBrickFlyInTime + lastBrickSnapTime;

            m_ModelCopy.SetActive(true);

            foreach (var brick in m_BrickCopies)
            {
                brick.Brick.gameObject.SetActive(false);
            }

            m_NextBrickSpawnIndex = 0;
        }

        void SetupModelPopIn()
        {
            m_ModelCopy.SetActive(true);

            foreach (var brick in m_BrickCopies)
            {
                brick.Brick.gameObject.SetActive(true);
            }

            m_ModelCopy.transform.localScale = Vector3.zero;

        }

        IEnumerator AnimationBrickHandler(int index, float brickFlyInTime, float snapInPlacePause)
        {
            var time = 0.0f;

            var animatedBrick = m_AnimatedBricks[index];
            var brickCopy = m_BrickCopies[index];

            var brickStartRotation = Random.rotation;
            var brickTargetPosition = brickCopy.IsStartingBrick ? brickCopy.Brick.transform.position : brickCopy.Brick.transform.position + m_ModelCopy.transform.TransformVector(brickCopy.ConnectionOffset);
            var brickStartPosition = GetBrickSpawnPosition(brickTargetPosition);

            animatedBrick.transform.position = brickStartPosition;
            animatedBrick.transform.rotation = brickStartRotation;
            animatedBrick.gameObject.SetActive(true);

            // Find point halfway between start and end position, then move it in offset direction.
            var offset = m_SnapToGround ? Vector3.up * k_BrickFlyInOffset :  m_ModelCopy.transform.TransformVector(brickCopy.ConnectionOffset.normalized * k_BrickFlyInOffset);
            var middlePoint = brickStartPosition + (brickTargetPosition - brickStartPosition) * 0.5f + offset;

            while (true)
            {
                time += Time.deltaTime;

                // Animate brick.
                if (animatedBrick.gameObject.activeSelf)
                {
                    ScaleAnimation(animatedBrick, m_DeformedMaterials[index], time, Mathf.Min(k_MaxBrickScaleTime, brickFlyInTime * k_BrickScaleFlyInTimeScale));

                    var easedTime = MathUtility.EaseOutSine(time, brickFlyInTime);
                    animatedBrick.transform.position = MathUtility.QuadraticBezier(brickStartPosition, middlePoint, brickTargetPosition, easedTime);
                    animatedBrick.transform.rotation = Quaternion.Slerp(brickStartRotation, brickCopy.Brick.transform.rotation, easedTime);

                    if (time >= brickFlyInTime + snapInPlacePause)
                    {
                        brickCopy.Brick.gameObject.SetActive(true);
                        animatedBrick.gameObject.SetActive(false);

                        if (!brickCopy.IsStartingBrick)
                        {
                            PlaySnapAudio();
                        }
                    }
                }

                // Perform model nudge.
                if (!animatedBrick.gameObject.activeSelf)
                {
                    var nudgeTime = time - (brickFlyInTime + snapInPlacePause);

                    if (!brickCopy.IsStartingBrick)
                    {
                        brickCopy.Brick.transform.parent.localPosition = Vector3.Lerp(brickCopy.ModelGroupLocalPositionInModel - brickCopy.ConnectionOffset, brickCopy.ModelGroupLocalPositionInModel, nudgeTime / k_NudgeTime);
                    }

                    if (nudgeTime >= k_NudgeTime)
                    {
                        break;
                    }
                }

                yield return new WaitForEndOfFrame();
            }
        }

        void PlayAudio()
        {
            var audioSource = PlayAudio(false, true, false, false, false);
            audioSource.transform.position = m_CurrentBounds.center;
        }

        void PlaySnapAudio()
        {
            var existingAudio = m_Audio;
            var existingAudioVolume = m_AudioVolume;

            m_Audio = m_BrickSnapAudio;
            m_AudioVolume = 1.0f;

            var audioSource = PlayAudio(false, true, false, false, false, Random.Range(0.9f, 1.1f));
            audioSource.transform.position = m_CurrentBounds.center;

            m_Audio = existingAudio;
            m_AudioVolume = existingAudioVolume;
        }

        void PlayParticleEffect(ParticleSystemShapeType shape, Vector3 scale, Vector3 position)
        {
            if (m_ParticleSystem)
            {
                var shapeModule = m_ParticleSystem.shape;
                shapeModule.shapeType = shape;

                int burstParticleCount;
                // Scale particle burst with volume or area depending on shape parameter.
                if (shape == ParticleSystemShapeType.Box)
                {
                    m_ParticleSystem.transform.rotation = Quaternion.identity;
                    shapeModule.scale = scale;

                    var volume = scale.x * scale.y * scale.z;
                    burstParticleCount = Mathf.RoundToInt(Mathf.Clamp(k_ParticleBurstPerModuleVolume * volume / LEGOModuleVolume, k_MinParticleBurst, k_MaxParticleBurst));
                }
                else
                {
                    // Rectangle - we need to rotate it to make it match the bottom of the bounds.
                    m_ParticleSystem.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                    shapeModule.scale = Quaternion.Euler(90f, 0f, 0f) * scale;

                    var area = scale.x * scale.z;
                    burstParticleCount = Mathf.RoundToInt(Mathf.Clamp(k_ParticleBurstPerModuleArea * area / (LEGOHorizontalModule * LEGOHorizontalModule), k_MinParticleBurst, k_MaxParticleBurst));
                }

                m_ParticleSystem.transform.position = position;
                m_ParticleSystem.Emit(burstParticleCount);
            }
        }

        bool SpawnCollisionCheck()
        {
            // Do not check collision against the environment as we want to be able to spawn on slopes.
            return Physics.CheckBox(m_CurrentBounds.center, m_CurrentBounds.extents - Vector3.one * Mathf.Epsilon, Quaternion.identity, m_LayerMask, QueryTriggerInteraction.Ignore);
        }

        Vector3 GetBrickSpawnPosition(Vector3 targetPosition)
        {
            switch (m_SpawnMethod)
            {
                case SpawnMethod.BuildFromAroundModel:
                    var randomPoint = Vector3.Scale(Random.onUnitSphere, m_CurrentBounds.extents);
                    randomPoint += randomPoint.normalized * k_BrickFlyInFromAroundModelPositionOffset;
                    randomPoint = m_CurrentBounds.center + randomPoint;
                    randomPoint.y = m_SnapToGround ? m_CurrentBounds.min.y : randomPoint.y;
                    return randomPoint;
                case SpawnMethod.BuildFromSpawnAction:
                    var bounds = GetScopedBounds(m_ScopedBricks, out _, out _);
                    return bounds.ClosestPoint(targetPosition);
                default:
                    return GetBrickCenter();
            }
        }

        void ScaleAnimation(Brick brick, List<Material> materials, float time, float totalTime)
        {
            var scale = Vector3.Lerp(Vector3.zero, Vector3.one, MathUtility.EaseOutSine(time, totalTime));
            var worldPivot = brick.transform.TransformPoint(brick.totalBounds.center);
            var deformMatrix = Matrix4x4.Translate(worldPivot) * Matrix4x4.Scale(scale) * Matrix4x4.Translate(-worldPivot);

            foreach (var material in materials)
            {
                material.SetVector(s_DeformMatrix1ID, deformMatrix.GetRow(0));
                material.SetVector(s_DeformMatrix2ID, deformMatrix.GetRow(1));
                material.SetVector(s_DeformMatrix3ID, deformMatrix.GetRow(2));
            }
        }

        bool GetModelSpawnPosition(out Vector3 position)
        {
            var offset = Vector3.zero;

            switch (m_SpawnAreaShape)
            {
                case SpawnShape.Box:
                    offset = Vector3.Scale(new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f)), m_SpawnAreaSize - Vector3.one * Mathf.Epsilon);
                    if (m_SnapToGround)
                    {
                        var upLocal = transform.InverseTransformDirection(Vector3.up);
                        MathUtility.IntersectRayAABB(offset, upLocal, Vector3.zero, m_SpawnAreaSize, out offset);
                    }
                    break;
                case SpawnShape.Sphere:
                    offset = (m_SnapToGround ? Random.onUnitSphere : Random.insideUnitSphere) * (m_SpawnAreaRadius - Mathf.Epsilon);
                    if (m_SnapToGround)
                    {
                        var upLocal = transform.InverseTransformDirection(Vector3.up);
                        MathUtility.IntersectRaySphere(offset, upLocal, Vector3.zero, m_SpawnAreaRadius, out offset);
                    }
                    break;
            }

            position = transform.TransformPoint(m_SpawnAreaCenter + offset + m_BrickPivotOffset);

            if (m_SnapToGround)
            {
                RaycastHit hit;
                if (Physics.Raycast(position, Vector3.down, out hit, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    position = hit.point + Vector3.up * kSnapEpsilon;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        Quaternion GetModelSpawnOrientation()
        {
            switch (m_SpawnOrientationType)
            {
                case SpawnOrientation.Specific:
                    return Quaternion.Euler(m_SpawnOrientation);
                case SpawnOrientation.Random:
                    return Random.rotation;
                case SpawnOrientation.RandomXAxis:
                    return Quaternion.Euler(new Vector3(Random.Range(0, 360), m_SpawnOrientation.y, m_SpawnOrientation.z));
                case SpawnOrientation.RandomYAxis:
                    return Quaternion.Euler(new Vector3(m_SpawnOrientation.x, Random.Range(0, 360), m_SpawnOrientation.z));
                case SpawnOrientation.RandomZAxis:
                    return Quaternion.Euler(new Vector3(m_SpawnOrientation.x, m_SpawnOrientation.y, Random.Range(0, 360)));
                default:
                    return Quaternion.identity;
            }
        }

        protected override void OnDrawGizmos()
        {
            base.OnDrawGizmos();

            if (!m_Model || (!m_Model.GetComponent<Model>() && !m_Model.GetComponent<ModelGroup>() && !m_Model.GetComponent<Brick>()))
            {
                var gizmoBounds = GetGizmoBounds();

                Gizmos.DrawIcon(gizmoBounds.center + Vector3.up, "Assets/LEGO/Gizmos/LEGO Behaviour Icons/Warning.png");
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (m_State == State.Building)
            {
                // Attach rigid bodies and velocity to bricks currently being animated.
                for (var i = 0; i < m_AnimatedBricks.Count; i++)
                {
                    if (m_AnimatedBricks[i] && m_BrickCopies[i].Brick)
                    {
                        if (m_AnimatedBricks[i].gameObject.activeSelf)
                        {
                            m_BrickCopies[i].Brick.gameObject.SetActive(true);
                            m_BrickCopies[i].Brick.transform.position = m_AnimatedBricks[i].transform.position;
                            m_BrickCopies[i].Brick.transform.rotation = m_AnimatedBricks[i].transform.rotation;

                            var currentBrickVelocity = m_AnimatedBricks[i].GetComponent<VelocityTracker>().GetVelocity();

                            var rigidBody = m_BrickCopies[i].Brick.gameObject.AddComponent<Rigidbody>();
                            rigidBody.velocity = currentBrickVelocity;
                            rigidBody.WakeUp();

                            Destroy(m_AnimatedBricks[i].gameObject);
                        }
                    }
                }
            }
            else if (m_State == State.PoppingIn)
            {
                if (m_ModelCopy)
                {
                    m_ModelCopy.transform.localScale = Vector3.one;
                }
            }
        }
    }
}
