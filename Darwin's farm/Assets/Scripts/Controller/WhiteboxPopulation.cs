using System;
using UnityEngine;

public enum PopulationMovementState { Waiting, Moving, Blocked }

[Serializable]
public sealed class PopulationMovementSettings
{
    [Min(0.05f)] public float leaderSize = 0.8f;
    [Min(0.01f)] public float speed = 1.8f;
    [Min(0f)] public float pauseSeconds = 1.2f;
    [Min(0.1f)] public float retrySeconds = 1f;
    [Min(0.1f)] public float turnSpeed = 8f;
    [Min(0.1f)] public float labelHeight = 0.6f;
    [Min(0.1f)] public float populationGap = 0.3f;
    [Min(0f)] public float wanderEdgeMargin = 0.8f;
    [Min(0.1f)] public float replanSeconds = 0.5f;
    [Min(0.2f)] public float blockedRetargetSeconds = 1.5f;
    [Range(1, 64)] public int targetAttempts = 24;
    [Header("Air populations")]
    [Min(0.1f)] public float flightSpeed = 3f;
    [Min(0.75f)] public float flightHeight = 1.7f;
    [Min(0f)] public float flightBob = 0.12f;
    [Min(0.2f)] public float flightEdgeMargin = 0.8f;
    [Min(0.5f)] public float flightPatrolRadius = 4.5f;
}

// A single visible animal represents the entire population. Movement never
// changes the ecological simulator's migration, birth, death or count rules.
public sealed class WhiteboxPopulation : MonoBehaviour
{
    internal sealed class Representative : PopulationMotion.Body
    {
        public Transform visual;
        public SpriteRenderer sprite;
    }

    [SerializeField] private string uniqueId;
    [SerializeField] private Vector2Int tileId;
    [SerializeField, Min(0)] private int populationCount;
    [SerializeField] private PopulationMovementState movementState;
    [SerializeField] private Vector3 targetPosition;
    [SerializeField] private bool wanderingPaused;
    public string UniqueId => uniqueId;
    public Vector2Int TileId => tileId;
    public int Count { get => populationCount; set { populationCount = Mathf.Max(0, value); RefreshLabel(); } }
    public PopulationMovementState MovementState => movementState;
    public Vector3 TargetPosition => targetPosition;
    public Transform Leader => leader?.visual;
    public Sprite DisplaySprite => leader?.sprite != null ? leader.sprite.sprite : null;
    public PopulationData EcologicalPopulation { get; private set; }
    public bool IsAir => flight != null;

    internal Representative leader;
    internal PopulationTileSpace space;
    internal MapTileInstance tile;
    private PopulationMovementController controller;
    private TextMesh label;
    private float chooseAt;
    private int displayedCount = -1;
    private PopulationMotion motion;
    private bool terrainRecoveryPending;
    private float terrainRecoveryAt;
    private PopulationAnimationDriver animationDriver;
    private PopulationFrameAnimationController frameAnimation;
    private Sprite boundSprite;
    private Color boundTint;
    private PopulationAnimationSet boundAnimation;
    private PopulationFlight flight;

    public bool HasAppearance(Sprite sprite, Color tint, PopulationAnimationSet animations) =>
        boundSprite == (sprite != null ? sprite : controller.FallbackSprite) &&
        boundTint == tint && boundAnimation == animations;

    public void BindEcologicalPopulation(PopulationData data, Sprite sprite, Color tint,
        PopulationAnimationSet animations = null)
    {
        EcologicalPopulation = data;
        if (data == null || leader == null) return;
        if (data.species != null && !string.IsNullOrEmpty(data.species.AnimalPlayEvent))
        {
            var animalAudio = leader.visual.GetComponent<DarwinWwiseAnimalEmitter>();
            if (animalAudio == null) animalAudio = leader.visual.gameObject.AddComponent<DarwinWwiseAnimalEmitter>();
            animalAudio.Bind(data.species.AnimalSoundBank, data.species.AnimalPlayEvent);
        }
        else
        {
            var animalAudio = leader.visual.GetComponent<DarwinWwiseAnimalEmitter>();
            if (animalAudio != null) Destroy(animalAudio);
        }
        Sprite selected = sprite != null ? sprite : controller.FallbackSprite;
        flight?.SetSprite(selected);
        boundSprite = selected;
        boundTint = tint;
        boundAnimation = animations;
        if (animationDriver == null)
            animationDriver = leader.sprite.gameObject.AddComponent<PopulationAnimationDriver>();
        bool hasFrames = animations != null && animations.HasFrameSequences;
        animationDriver.Bind(leader.sprite, hasFrames || IsAir ? null : animations,
            selected, tint, leader.groundPosition);
        if (hasFrames && frameAnimation == null)
            frameAnimation = leader.sprite.gameObject.AddComponent<PopulationFrameAnimationController>();
        frameAnimation?.Bind(leader.sprite, hasFrames ? animations : null, tint);
        if (IsAir) PopulationSpriteDisplay.ConfigureAir(leader.sprite);
        leader.visual.GetComponent<MeshRenderer>().enabled = leader.sprite.sprite == null;
    }

    internal void Initialize(PopulationMovementController owner, MapTileInstance ownedTile,
        PopulationTileSpace ownedSpace, int count, Vector3 position, Color color,
        bool airborne = false)
    {
        controller = owner; tile = ownedTile; space = ownedSpace;
        uniqueId = Guid.NewGuid().ToString("N"); tileId = tile.Coordinate;
        populationCount = Mathf.Max(0, count);
        if (airborne)
        {
            flight = new PopulationFlight(tile, controller.Settings);
            position = flight.Position;
        }
        name = $"Population_{tileId.x}_{tileId.y}_{uniqueId.Substring(0, 8)}";
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Leader";
        cube.transform.SetParent(transform, false);
        cube.transform.localScale = Vector3.one * controller.Settings.leaderSize;
        // The proxy supplies movement clearance, never physics collision geometry.
        Collider collider = cube.GetComponent<Collider>();
        collider.enabled = false; Destroy(collider);
        Renderer renderer = cube.GetComponent<Renderer>();
        if (tile.Core != null) renderer.sharedMaterial = tile.Core.sharedMaterial;
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", color); properties.SetColor("_Color", color);
        renderer.SetPropertyBlock(properties);
        renderer.enabled = controller.FallbackSprite == null;
        leader = new Representative
        {
            visual = cube.transform,
            sprite = PopulationSpriteDisplay.Create(cube.transform, controller.FallbackSprite, Color.white),
            groundPosition = position,
            size = controller.Settings.leaderSize
        };
        Place(position, Vector3.forward);
        GameObject text = new GameObject("PopulationCount");
        text.transform.SetParent(transform, false);
        label = text.AddComponent<TextMesh>();
        label.font = GameUITheme.Font;
        label.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
        label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
        label.fontSize = 64; label.characterSize = 0.075f; label.color = Color.white;
        targetPosition = position; movementState = PopulationMovementState.Waiting;
        chooseAt = Time.time + UnityEngine.Random.Range(0f, Mathf.Max(0.1f, controller.Settings.pauseSeconds));
        if (!IsAir) motion = new PopulationMotion(leader, controller.Settings,
            (from, to) => space.CanTraverse(from, to, leader.size) && controller.HasSeparation(this, from, to),
            PlanRoute, Place);
        RefreshLabel();
    }

    private void RefreshLabel()
    {
        if (label == null) return;
        populationCount = Mathf.Max(0, populationCount);
        if (displayedCount == populationCount) return;
        displayedCount = populationCount;
        label.text = populationCount.ToString();
    }

    private void LateUpdate()
    {
        RefreshLabel(); // Also supports live Inspector edits.
        if (label == null || Leader == null) return;
        label.transform.position = Leader.position + Vector3.up *
            (leader.size * 0.5f + controller.Settings.labelHeight);
        Camera camera = controller.ViewCamera != null ? controller.ViewCamera : Camera.main;
        if (frameAnimation == null || frameAnimation.AnimationSet == null)
            animationDriver?.SampleMotion(leader.groundPosition, camera, Mathf.Min(Time.deltaTime, 0.2f));
        if (camera == null) return;
        label.transform.rotation = camera.transform.rotation;
        PopulationSpriteDisplay.FaceCamera(leader.sprite, camera);
    }

    internal void Tick(float deltaTime)
    {
        if (IsAir)
        {
            if (leader == null || tile == null) return;
            Vector3 before = leader.groundPosition;
            bool common = frameAnimation != null && frameAnimation.IsCommonLoop;
            bool animated = frameAnimation != null && frameAnimation.AnimationSet != null && !common;
            if (animated && !wanderingPaused &&
                frameAnimation.Phase != PopulationFramePhase.Walk && Time.time >= chooseAt)
            {
                frameAnimation.RequestStart();
                if (frameAnimation.CanBeginWalk)
                    frameAnimation.BeginWalk(controller.Settings.flightSpeed *
                        frameAnimation.WalkCycleSeconds * UnityEngine.Random.Range(2, 5),
                        controller.Settings.flightSpeed);
            }
            PopulationFramePhase airPhaseBefore = animated ? frameAnimation.Phase : PopulationFramePhase.Hold;
            float flightDistance = animated ? frameAnimation.Tick(deltaTime, Time.time) : 0f;
            if (common) frameAnimation.Tick(deltaTime, Time.time);
            flight.Tick(deltaTime, animated ? flightDistance <= 0f : wanderingPaused);
            Place(flight.Position, flight.Position - before);
            if (animated || common)
            {
                frameAnimation.FaceTravel(leader.groundPosition - before, controller.ViewCamera);
                if (animated && airPhaseBefore == PopulationFramePhase.Walk &&
                    frameAnimation.Phase == PopulationFramePhase.Hold)
                    chooseAt = Time.time + UnityEngine.Random.Range(
                        frameAnimation.AnimationSet.MovementHoldMin,
                        frameAnimation.AnimationSet.MovementHoldMax);
            }
            targetPosition = flight.Position;
            movementState = wanderingPaused || animated && flightDistance <= 0f ?
                PopulationMovementState.Waiting : PopulationMovementState.Moving;
            return;
        }
        if (leader == null || tile == null || motion == null) return;
        if (terrainRecoveryPending)
        {
            if (Time.time < terrainRecoveryAt) return;
            if (!space.CanTraverse(leader.groundPosition, leader.groundPosition, leader.size))
            {
                if (!controller.TryRecoverPosition(this, out Vector3 position) || !Place(position, leader.heading))
                { terrainRecoveryAt = Time.time + .5f; Wait(true); return; }
            }
            motion.ResetNavigation();
            terrainRecoveryPending = false;
            animationDriver?.ResetMotion(leader.groundPosition);
            chooseAt = Time.time;
        }
        bool placed = Place(leader.groundPosition, leader.heading);
        leader.visual.gameObject.SetActive(placed);
        if (label != null) label.gameObject.SetActive(placed);
        if (!placed) { terrainRecoveryPending = true; terrainRecoveryAt = Time.time + .5f; Wait(true); return; }
        bool commonLoop = frameAnimation != null && frameAnimation.IsCommonLoop;
        bool frameDriven = frameAnimation != null && frameAnimation.AnimationSet != null && !commonLoop;
        if (!wanderingPaused && !motion.HasRoute && Time.time >= chooseAt)
        {
            if (frameDriven) frameAnimation.RequestStart();
            if (!frameDriven || frameAnimation.CanBeginWalk) ChooseTarget();
        }
        bool hadRoute = motion.HasRoute;
        bool blockedInterrupted = false;
        Vector3 beforeMove = leader.groundPosition;
        if (frameDriven)
        {
            float requested = frameAnimation.Tick(deltaTime, Time.time);
            if (requested > 0f && motion.HasRoute)
            {
                motion.Tick(requested / Mathf.Max(0.0001f, frameAnimation.WalkSpeed),
                    Time.time, frameAnimation.WalkSpeed);
                if (motion.LastMovedDistance + 0.002f < requested)
                { frameAnimation.InterruptBlocked(Time.time); Wait(true); blockedInterrupted = true; }
                else frameAnimation.FaceTravel(leader.groundPosition - beforeMove, controller.ViewCamera);
            }
            if (frameAnimation.Phase != PopulationFramePhase.Walk && motion.HasRoute)
                Wait(false);
        }
        else
        {
            motion.Tick(deltaTime, Time.time);
            if (commonLoop)
            {
                frameAnimation.Tick(deltaTime, Time.time);
                frameAnimation.FaceTravel(leader.groundPosition - beforeMove, controller.ViewCamera);
            }
        }
        if (hadRoute && !motion.HasRoute && !blockedInterrupted) Wait(false);
        else if (motion.HasRoute)
        {
            movementState = PopulationMovementState.Moving;
            if (motion.BlockedSeconds >= Mathf.Max(0.2f, controller.Settings.blockedRetargetSeconds)) Wait(true);
        }
    }

    private bool PlanRoute(Vector3 from, Vector3 target, out Vector3[] path) =>
        space.TryPath(from, target, leader.size, out path, (a, b) => controller.HasSeparation(this, a, b));

    private void ChooseTarget()
    {
        Bounds bounds = space.Bounds;
        for (int pass = 0; pass < 2; pass++)
            for (int attempt = 0; attempt < Mathf.Clamp(controller.Settings.targetAttempts, 1, 64); attempt++)
            {
                Vector3 candidate = new Vector3(UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
                    leader.groundPosition.y, UnityEngine.Random.Range(bounds.min.z, bounds.max.z));
                if (Vector2.Distance(PopulationCollisionMath.Flat(candidate), PopulationCollisionMath.Flat(leader.groundPosition)) < 1f ||
                    !space.CanWanderAt(candidate, leader.size, pass == 0 ? controller.Settings.wanderEdgeMargin : 0f)) continue;
                if (!motion.SetTarget(candidate, Time.time)) continue;
                if (frameAnimation != null && frameAnimation.AnimationSet != null && !frameAnimation.IsCommonLoop &&
                    !frameAnimation.BeginWalk(motion.RouteLength, controller.Settings.speed))
                { motion.CancelRoute(); continue; }
                targetPosition = motion.Goal; movementState = PopulationMovementState.Moving;
                return;
            }
        Wait(true);
    }

    private void Wait(bool blocked)
    {
        motion?.CancelRoute();
        if (blocked && frameAnimation != null && frameAnimation.AnimationSet != null && !frameAnimation.IsCommonLoop)
            frameAnimation.InterruptBlocked(Time.time);
        movementState = blocked ? PopulationMovementState.Blocked : PopulationMovementState.Waiting;
        if (!blocked && frameAnimation != null && frameAnimation.AnimationSet != null && !frameAnimation.IsCommonLoop)
            chooseAt = Time.time + UnityEngine.Random.Range(
                frameAnimation.AnimationSet.MovementHoldMin,
                frameAnimation.AnimationSet.MovementHoldMax);
        else
            chooseAt = Time.time + Mathf.Max(0.1f,
                blocked ? controller.Settings.retrySeconds : controller.Settings.pauseSeconds);
    }

    internal void PauseWandering(bool paused)
    {
        wanderingPaused = paused;
        if (paused && frameAnimation != null && frameAnimation.AnimationSet != null && !frameAnimation.IsCommonLoop)
        {
            if (motion != null && motion.HasRoute)
                targetPosition = motion.PositionAfter(frameAnimation.DistanceUntilWalkBoundary);
            frameAnimation.RequestStop();
        }
        else if (paused) Wait(false);
        else chooseAt = Time.time;
    }

    internal bool ValidateMovement(out string problem)
    {
        problem = null;
        if (IsAir)
        {
            if (leader == null || !flight.Contains(Leader.position))
            { problem = "Flying representative left its own tile airspace."; return false; }
            if (label == null || label.text != Count.ToString())
            { problem = "Air count label is out of sync."; return false; }
            return true;
        }
        if (leader == null || motion == null)
        { problem = "Missing runtime representative; recreate the debug populations after recompilation."; return false; }
        if (label == null || !label.gameObject.activeInHierarchy || label.text != Count.ToString())
        { problem = "Count label is hidden or out of sync."; return false; }
        if (!leader.visual.gameObject.activeInHierarchy ||
            !space.CanTraverse(leader.groundPosition, leader.groundPosition, leader.size))
        { problem = "Representative is hidden or outside valid terrain."; return false; }
        if (!controller.HasSeparation(this, leader.groundPosition))
        { problem = "Representative overlaps another population."; return false; }
        return true;
    }

    internal bool RequestReverse()
    {
        if (leader == null || tile == null || motion == null) return false;
        float backwardYaw = Mathf.Atan2(-leader.heading.x, -leader.heading.z);
        for (int angleIndex = 0; angleIndex < 5; angleIndex++)
            for (int distanceIndex = 0; distanceIndex < 3; distanceIndex++)
            {
                float angle = angleIndex == 0 ? 0f : ((angleIndex + 1) / 2 * 30f) * (angleIndex % 2 == 1 ? 1f : -1f);
                float yaw = backwardYaw + angle * Mathf.Deg2Rad;
                Vector3 target = leader.groundPosition + new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * (3f + distanceIndex * 1.5f);
                if (!motion.SetTarget(target, Time.time)) continue;
                targetPosition = motion.Goal; movementState = PopulationMovementState.Moving;
                return true;
            }
        return false;
    }

    private bool Place(Vector3 position, Vector3 heading)
    {
        Vector3 nextHeading = PopulationCollisionMath.SmoothHeading(leader.heading, heading, 1f);
        if (IsAir)
        {
            leader.groundPosition = position;
            if (heading.sqrMagnitude > 0.00001f) leader.heading = nextHeading;
            leader.visual.SetPositionAndRotation(position, Quaternion.LookRotation(leader.heading, Vector3.up));
            return true;
        }
        if (!space.TryRepresentativePose(position, nextHeading, leader.size,
            out Vector3 ground, out Vector3 center, out Quaternion rotation)) return false;
        leader.groundPosition = ground; leader.heading = nextHeading;
        leader.visual.SetPositionAndRotation(center, rotation);
        return true;
    }

    internal void NavigationChanged()
    {
        if (IsAir) { flight.RefreshCeiling(); return; }
        if (space == null || motion == null) return;
        space.Rebuild();
        terrainRecoveryPending = true;
        terrainRecoveryAt = Time.time;
        motion.ResetNavigation();
        Wait(true);
    }

    private void OnDestroy()
    {
        if (controller != null) controller.Unregister(this);
        leader = null; space = null; tile = null; label = null; controller = null; motion = null;
    }
}
