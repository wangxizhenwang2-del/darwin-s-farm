using System;
using UnityEngine;

public enum PopulationMovementState { Waiting, Moving, Blocked }

[Serializable]
public sealed class PopulationMovementSettings
{
    [Min(0.05f)] public float leaderSize = 0.8f;
    [Min(0.05f)] public float followerSize = 0.45f;
    [Min(0.01f)] public float speed = 1.8f;
    [Min(0.01f)] public float followerSpeed = 2.4f;
    [Min(0.01f)] public float spacing = 1.2f;
    [Min(0f)] public float pauseSeconds = 1.2f;
    [Min(0.1f)] public float retrySeconds = 1f;
    [Min(0.01f)] public float arrivalDistance = 0.08f;
    [Min(0.1f)] public float turnSpeed = 8f;
    [Min(0.1f)] public float labelHeight = 0.6f;
    [Min(0.1f)] public float populationGap = 0.3f;
    [Min(0f)] public float memberGap = 0.08f;
    [Min(0f)] public float playerGap = 0.1f;
    [Min(0f)] public float wanderEdgeMargin = 0.8f;
    [Min(0.1f)] public float replanSeconds = 0.5f;
    [Min(0.2f)] public float blockedRetargetSeconds = 1.5f;
    [Min(0.1f)] public float formationSlack = 0.75f;
    [Range(1, 64)] public int targetAttempts = 24;
}

// Movement data deliberately stays independent from the ecological simulator's
// migration/birth/death rules. Visible cubes represent count ranges, not individuals.
public sealed class WhiteboxPopulation : MonoBehaviour
{
    public static int VisibleMemberCount(int count) => count > 1000 ? 4 : count > 500 ? 3 : count > 100 ? 2 : 1;

    internal sealed class Member : PopulationFormationMotion.Body
    {
        public Transform visual;
        public SpriteRenderer sprite;
    }

    [SerializeField] private string uniqueId;
    [SerializeField] private Vector2Int tileId;
    [SerializeField, Min(0)] private int populationCount;
    [SerializeField] private PopulationMovementState movementState;
    [SerializeField] private Vector3 targetPosition;
    public string UniqueId => uniqueId;
    public Vector2Int TileId => tileId;
    public int Count { get => populationCount; set { populationCount = Mathf.Max(0, value); RefreshLabel(); SyncVisibleMembers(true); } }
    public PopulationMovementState MovementState => movementState;
    public Vector3 TargetPosition => targetPosition;
    public Transform Leader => members == null ? null : members[0].visual;
    public Sprite DisplaySprite => members == null ? null : members[0].sprite.sprite;
    public PopulationData EcologicalPopulation { get; private set; }

    public void BindEcologicalPopulation(PopulationData data, Sprite sprite, Color tint)
    {
        EcologicalPopulation = data;
        if (data == null || members == null) return;
        Sprite selected = sprite != null ? sprite : controller.FallbackSprite;
        foreach (Member member in members)
        {
            PopulationSpriteDisplay.Configure(member.sprite, selected, tint);
            member.visual.GetComponent<MeshRenderer>().enabled = selected == null;
        }
    }

    internal Member[] members;
    internal PopulationTileSpace space;
    internal MapTileInstance tile;
    private PopulationMovementController controller;
    private TextMesh label;
    private float chooseAt;
    private float nextMemberRetryAt;
    private int displayedCount = -1;
    private int activeMemberCount;
    private PopulationFormationMotion motion;
    [SerializeField] private bool waitingForFollowers;
    [SerializeField] private bool wanderingPaused;

    internal void Initialize(PopulationMovementController owner, MapTileInstance ownedTile,
        PopulationTileSpace ownedSpace, int count, Vector3[] positions, Color color)
    {
        controller = owner; tile = ownedTile; space = ownedSpace;
        uniqueId = Guid.NewGuid().ToString("N"); tileId = tile.Coordinate;
        populationCount = Mathf.Max(0, count);
        activeMemberCount = VisibleMemberCount(populationCount);
        name = $"Population_{tileId.x}_{tileId.y}_{uniqueId.Substring(0, 8)}";
        members = new Member[4];
        for (int i = 0; i < members.Length; i++)
        {
            float size = i == 0 ? controller.Settings.leaderSize : controller.Settings.followerSize;
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = i == 0 ? "Leader" : $"Follower_{i}";
            cube.transform.SetParent(transform, false);
            cube.transform.localScale = Vector3.one * size;
            // Do not feed display cubes into terrain physics, construction or baking.
            Collider collider = cube.GetComponent<Collider>();
            collider.enabled = false; Destroy(collider);
            Renderer renderer = cube.GetComponent<Renderer>();
            if (tile.Core != null) renderer.sharedMaterial = tile.Core.sharedMaterial;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color); properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
            renderer.enabled = controller.FallbackSprite == null;
            SpriteRenderer sprite = PopulationSpriteDisplay.Create(cube.transform,
                controller.FallbackSprite, Color.white);
            members[i] = new Member { visual = cube.transform, sprite = sprite,
                groundPosition = positions[i], size = size };
            Place(members[i], positions[i], Vector3.forward, 1f);
            if (i >= activeMemberCount) cube.SetActive(false);
        }
        GameObject text = new GameObject("PopulationCount");
        text.transform.SetParent(transform, false);
        label = text.AddComponent<TextMesh>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
        label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
        label.fontSize = 64; label.characterSize = 0.075f; label.color = Color.white;
        targetPosition = positions[0]; movementState = PopulationMovementState.Waiting;
        chooseAt = Time.time + UnityEngine.Random.Range(0f, Mathf.Max(0.1f, controller.Settings.pauseSeconds));
        Vector3 heading = activeMemberCount > 1 ? positions[0] - positions[1] : Vector3.forward;
        heading.y = 0f;
        for (int i = 0; i < activeMemberCount; i++)
            Place(members[i], members[i].groundPosition, heading.normalized, 1f);
        motion = new PopulationFormationMotion(members, controller.Settings,
            (index, from, to) => space.CanTraverse(from, to, members[index].size) &&
                controller.HasSeparation(this, members[index], from, to), PlanRoute,
            (index, position, direction) => Place(members[index], position, direction, 1f));
        motion.SetActiveCount(activeMemberCount);
        RefreshLabel();
    }

    private void SyncVisibleMembers(bool force)
    {
        if (members == null || motion == null) return;
        int desired = VisibleMemberCount(populationCount);
        if (desired == activeMemberCount) return;
        if (desired < activeMemberCount)
        {
            for (int i = desired; i < activeMemberCount; i++) members[i].visual.gameObject.SetActive(false);
            activeMemberCount = desired;
            motion.SetActiveCount(activeMemberCount);
            Wait(false);
            return;
        }
        if (!force && Time.time < nextMemberRetryAt) return;
        bool changed = false;
        while (activeMemberCount < desired && TryActivateFollower(activeMemberCount))
        {
            activeMemberCount++;
            changed = true;
        }
        if (changed)
        {
            motion.SetActiveCount(activeMemberCount);
            Wait(false);
        }
        if (activeMemberCount < desired) nextMemberRetryAt = Time.time + 0.5f;
    }

    private bool TryActivateFollower(int index)
    {
        Member member = members[index];
        Member predecessor = members[index - 1];
        float spacing = motion.Spacing(index);
        float backward = Mathf.Atan2(-predecessor.heading.x, -predecessor.heading.z);
        for (int direction = 0; direction < 16; direction++)
        {
            int offset = direction == 0 ? 0 : (direction + 1) / 2 * (direction % 2 == 1 ? 1 : -1);
            float angle = backward + offset * Mathf.PI * 0.125f;
            Vector3 candidate = predecessor.groundPosition +
                new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * spacing;
            if (!space.CanTraverse(candidate, candidate, member.size) ||
                !space.CanTraverse(predecessor.groundPosition, candidate, member.size) ||
                !controller.HasSeparation(this, member, candidate, candidate) ||
                !Place(member, candidate, predecessor.heading, 1f)) continue;
            member.visual.gameObject.SetActive(true);
            return true;
        }
        return false;
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
        SyncVisibleMembers(false);
        if (label == null || Leader == null) return;
        label.transform.position = Leader.position + Vector3.up *
            (members[0].size * 0.5f + controller.Settings.labelHeight);
        Camera camera = controller.ViewCamera != null ? controller.ViewCamera : Camera.main;
        if (camera != null) label.transform.rotation = camera.transform.rotation;
        if (camera != null)
            foreach (Member member in members)
                PopulationSpriteDisplay.FaceCamera(member.sprite, camera);
    }

    internal void Tick(float deltaTime)
    {
        if (members == null || tile == null || motion == null) return;
        SyncVisibleMembers(false);
        bool valid = true;
        for (int i = 0; i < activeMemberCount; i++)
        {
            Member member = members[i];
            bool placed = Place(member, member.groundPosition, member.heading, 1f);
            member.visual.gameObject.SetActive(placed);
            valid &= placed;
        }
        if (label != null) label.gameObject.SetActive(members[0].visual.gameObject.activeSelf);
        if (!valid) { Wait(true); return; }
        if (!wanderingPaused && !motion.HasLeaderRoute && Time.time >= chooseAt) ChooseTarget();
        bool hadRoute = motion.HasLeaderRoute;
        motion.Tick(deltaTime, Time.time);
        waitingForFollowers = motion.WaitingForFollowers;
        if (hadRoute && !motion.HasLeaderRoute) Wait(false);
        else if (motion.HasLeaderRoute)
        {
            movementState = waitingForFollowers ? PopulationMovementState.Waiting : PopulationMovementState.Moving;
            if (!waitingForFollowers && members[0].blockedSeconds >= Mathf.Max(0.2f, controller.Settings.blockedRetargetSeconds))
                Wait(true);
        }
    }

    private bool PlanRoute(int index, Vector3 from, Vector3 target, out Vector3[] path) =>
        space.TryPath(from, target, members[index].size, out path,
            (a, b) => motion.Allowed(index, a, b));

    private void ChooseTarget()
    {
        Member leader = members[0];
        Bounds bounds = space.Bounds;
        for (int pass = 0; pass < 2; pass++)
            for (int attempt = 0; attempt < Mathf.Clamp(controller.Settings.targetAttempts, 1, 64); attempt++)
            {
                Vector3 candidate = new Vector3(UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
                    leader.groundPosition.y, UnityEngine.Random.Range(bounds.min.z, bounds.max.z));
                if (Vector2.Distance(PopulationCollisionMath.Flat(candidate), PopulationCollisionMath.Flat(leader.groundPosition)) < 1f ||
                    !space.CanWanderAt(candidate, leader.size, pass == 0 ? controller.Settings.wanderEdgeMargin : 0f)) continue;
                if (!motion.SetLeaderTarget(candidate, Time.time)) continue;
                targetPosition = leader.goal; movementState = PopulationMovementState.Moving;
                return;
            }
        Wait(true);
    }

    private void Wait(bool blocked)
    {
        motion?.CancelLeaderRoute();
        movementState = blocked ? PopulationMovementState.Blocked : PopulationMovementState.Waiting;
        chooseAt = Time.time + Mathf.Max(0.1f, blocked ? controller.Settings.retrySeconds : controller.Settings.pauseSeconds);
    }

    internal void PauseWandering(bool paused)
    {
        wanderingPaused = paused;
        if (paused) Wait(false);
        else chooseAt = Time.time;
    }

    internal bool ValidateFormation(out string problem)
    {
        problem = null;
        if (members == null || members.Length != 4 || motion == null)
        { problem = "Missing runtime formation; recreate the debug populations after recompilation."; return false; }
        if (activeMemberCount != VisibleMemberCount(populationCount))
        { problem = "Waiting for safe space to display the additional population cubes."; return false; }
        if (motion.ActiveCount != activeMemberCount)
        { problem = "Movement member count is out of sync with visible cubes."; return false; }
        if (label == null || !label.gameObject.activeInHierarchy || label.text != Count.ToString())
        { problem = "Count label is hidden or out of sync."; return false; }
        for (int i = activeMemberCount; i < members.Length; i++)
            if (members[i].visual.gameObject.activeInHierarchy)
            { problem = $"Member {i} should be hidden for count {populationCount}."; return false; }
        for (int i = 0; i < activeMemberCount; i++)
        {
            if (!members[i].visual.gameObject.activeInHierarchy ||
                !space.CanTraverse(members[i].groundPosition, members[i].groundPosition, members[i].size))
            { problem = $"Member {i} is hidden or outside valid terrain."; return false; }
            if (!controller.HasSeparation(this, members[i], members[i].groundPosition))
            { problem = $"Member {i} overlaps another member/player."; return false; }
            if (i == 0) continue;
            float gap = Vector2.Distance(PopulationCollisionMath.Flat(members[i].groundPosition),
                PopulationCollisionMath.Flat(members[i - 1].groundPosition));
            if (gap > motion.Spacing(i) + controller.Settings.formationSlack + 0.01f)
            { problem = $"Member {i} lost its predecessor: gap={gap:F2}."; return false; }
        }
        return true;
    }

    internal bool RequestReverse()
    {
        if (members == null || tile == null || motion == null) return false;
        Member leader = members[0];
        float backwardYaw = Mathf.Atan2(-leader.heading.x, -leader.heading.z);
        for (int angleIndex = 0; angleIndex < 5; angleIndex++)
            for (int distanceIndex = 0; distanceIndex < 3; distanceIndex++)
            {
                float angle = angleIndex == 0 ? 0f : ((angleIndex + 1) / 2 * 30f) * (angleIndex % 2 == 1 ? 1f : -1f);
                float yaw = backwardYaw + angle * Mathf.Deg2Rad;
                Vector3 target = leader.groundPosition + new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * (3f + distanceIndex * 1.5f);
                if (!motion.SetLeaderTarget(target, Time.time)) continue;
                targetPosition = leader.goal; movementState = PopulationMovementState.Moving;
                return true;
            }
        return false;
    }

    private bool Place(Member member, Vector3 position, Vector3 heading, float turn)
    {
        if (!space.TryPose(position, member.size, out Vector3 center, out Vector3 normal) ||
            !space.TryGround(position, out RaycastHit ground)) return false;
        Vector3 nextHeading = PopulationCollisionMath.SmoothHeading(member.heading, heading, turn);
        Vector3 forward = Vector3.ProjectOnPlane(nextHeading, normal).normalized;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.Cross(normal, Vector3.right).normalized;
        Quaternion rotation = Quaternion.LookRotation(forward, normal);
        // A single tangent plane can intersect a slope seam. Lift only as much
        // as the sampled bottom face requires; planar slopes need no correction.
        float support = 0f;
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                Vector3 bottom = center + rotation * new Vector3(x, -1f, z) * (member.size * 0.5f);
                if (!space.TryGround(bottom, out RaycastHit contact)) return false;
                support = Mathf.Max(support, contact.point.y - bottom.y);
            }
        center.y += support;
        member.groundPosition = ground.point; member.heading = nextHeading;
        member.visual.SetPositionAndRotation(center, rotation);
        return true;
    }

    internal void NavigationChanged() { space.Rebuild(); Wait(true); }
    private void OnDestroy()
    {
        if (controller != null) controller.Unregister(this);
        // Visuals and label are children and are destroyed with this entity.
        members = null; space = null; tile = null; label = null; controller = null; motion = null;
    }
}
