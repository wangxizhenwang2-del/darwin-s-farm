using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Darwin.Rendering
{
    /// <summary>Visual topology for adjacent, unit-scale terrain tiles. Rebuilds only when inputs change.</summary>
    [ExecuteAlways, DisallowMultipleComponent, DefaultExecutionOrder(1100)]
    [AddComponentMenu("Rendering/Moebius River Network")]
    public sealed class MoebiusRiverNetwork : MonoBehaviour
    {
        [Header("Connections")]
        public bool autoRefresh = true;
        [Min(.001f)] public float positionTolerance = .06f;
        [Min(.001f)] public float heightTolerance = .03f;
        [Header("Rounded River Ends")]
        [Min(.1f)] public float capInset = .65f;
        [Min(.1f)] public float bankRounding = .65f;
        public Material bankMaterial;
        public Material foundationMaterial;
        [Min(.1f)] public float foundationDepth = .6f;
        [Tooltip("Disable when land is already rendered by the map terrain system.")]
        public bool generateLandFoundations = true;
        [Header("Existing Waterfall Style")]
        public GameObject waterfallPrefab;
        public Material impactMaterial;
        [Range(.05f, 1.5f)] public float lipRadius = .55f;

        public int JoinedCount { get; private set; }
        public int ClosedCount { get; private set; }
        public int WaterfallCount { get; private set; }
        public int RebuildCount { get; private set; }
        public string LastError { get; private set; }
        public GameObject GeneratedRoot => generated;
        public bool TryGetWater(MoebiusRiverTile tile, out MeshRenderer renderer) => waterRenderers.TryGetValue(tile, out renderer);

        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        sealed class TileGeometry
        {
            public GameObject root;
            public int signature;
            public readonly List<UnityEngine.Object> resources = new List<UnityEngine.Object>();
        }
        readonly Dictionary<MoebiusRiverTile, TileGeometry> geometry = new Dictionary<MoebiusRiverTile, TileGeometry>();
        TileGeometry currentGeometry;
        int geometrySettings;
        readonly Dictionary<MoebiusRiverTile, MeshRenderer> waterRenderers = new Dictionary<MoebiusRiverTile, MeshRenderer>();
        MoebiusRiverTile[] tiles = Array.Empty<MoebiusRiverTile>();
        GameObject generated;
        bool dirty = true, building;
        int lastSignature;
#if UNITY_EDITOR
        double nextEditorCheck;
#endif
        void OnEnable()
        {
            dirty = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += EditorTick;
#endif
        }
        void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorTick;
#endif
            building = true; Clear(); building = false;
        }
        void OnValidate() => RequestRebuild();
        void OnTransformChildrenChanged()
        {
            if (building) return;
            // Destroy() removes last frame's generated root later in Play Mode. That is not
            // a topology edit; otherwise each deferred removal would schedule another rebuild.
            int count = 0;
            foreach (var tile in GetComponentsInChildren<MoebiusRiverTile>())
            {
                if (!tile.isActiveAndEnabled || tile.GetComponentInParent<MoebiusRiverNetwork>() != this) continue;
                count++;
                if (Array.IndexOf(tiles, tile) < 0) { RequestRebuild(); return; }
            }
            if (count != tiles.Length) RequestRebuild();
        }
        void LateUpdate() { if (autoRefresh) RefreshIfChanged(); }
#if UNITY_EDITOR
        void EditorTick()
        {
            if (!this) { UnityEditor.EditorApplication.update -= EditorTick; return; }
            if (Application.isPlaying || !autoRefresh || !isActiveAndEnabled || UnityEditor.EditorApplication.isCompiling ||
                UnityEditor.EditorUtility.IsPersistent(this) || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded) return;
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            if (now < nextEditorCheck) return; nextEditorCheck = now + .12;
            RefreshIfChanged();
        }
#endif
        public void RequestRebuild() { if (!building) dirty = true; }
        void RefreshIfChanged()
        {
            if (building) return;
            if (dirty || Signature() != lastSignature) RebuildNow();
        }
        int Signature()
        {
            unchecked
            {
                int hash = 17;
                foreach (var tile in tiles)
                {
                    if (!tile) { hash = hash * 31; continue; }
                    hash = hash * 31 + tile.transform.localToWorldMatrix.GetHashCode();
                    hash = hash * 31 + tile.waterLevel.GetHashCode(); hash = hash * 31 + tile.landSurfaceHeight.GetHashCode();
                    hash = hash * 31 + tile.foundationTop.GetHashCode();
                    hash = hash * 31 + tile.isActiveAndEnabled.GetHashCode();
                    hash = hash * 31 + (tile.profile ? tile.profile.GetInstanceID() : 0);
                    hash = hash * 31 + (tile.originalWater ? tile.originalWater.GetInstanceID() : 0);
                }
                return hash * 31 + transform.localToWorldMatrix.GetHashCode();
            }
        }

        [ContextMenu("Rebuild Connections")]
        public bool RebuildNow() => RebuildInternal(false);
        public bool RebuildChanged() => RebuildInternal(true);
        bool RebuildInternal(bool incremental)
        {
            if (building || !isActiveAndEnabled) return false;
            building = true;
            try
            {
                if (!incremental) Clear();
                var candidates = GetComponentsInChildren<MoebiusRiverTile>(); var active = new List<MoebiusRiverTile>();
                foreach (var tile in candidates) if (tile.isActiveAndEnabled && tile.GetComponentInParent<MoebiusRiverNetwork>() == this) active.Add(tile);
                tiles = active.ToArray();
                if (tiles.Length == 0) { Clear(); LastError = null; return true; }
                if (!bankMaterial || !foundationMaterial) throw new InvalidOperationException("Assign the network's bank and foundation materials.");
                foreach (var tile in tiles)
                    if ((tile.transform.lossyScale - Vector3.one).sqrMagnitude > .0001f || Vector3.Dot(tile.transform.up, Vector3.up) < .9999f)
                        throw new InvalidOperationException("River tile wrappers need unit world scale and horizontal terrain. Scale the model inside the wrapper instead.");
                if (!generated) generated = NewObject("Generated River Connections", transform);
                var allEnds = new List<RiverEnd>(); var byTile = new Dictionary<MoebiusRiverTile, List<RiverEnd>>();
                float floor = transform.position.y;
                foreach (var tile in tiles)
                {
                    floor = Mathf.Min(floor, tile.transform.position.y);
                    var ends = new List<RiverEnd>(); byTile.Add(tile, ends);
                    if (!tile.IsRiver) continue;
                    foreach (var port in tile.profile.ports)
                    {
                        var end = Describe(tile, port); ends.Add(end); allEnds.Add(end);
                    }
                }
                int settingsSignature = HashCode.Combine(capInset, bankRounding, foundationDepth, lipRadius,
                    bankMaterial, foundationMaterial, waterfallPrefab, impactMaterial);
                settingsSignature = HashCode.Combine(settingsSignature, floor);
                settingsSignature = HashCode.Combine(settingsSignature, generateLandFoundations);
                var affected = new HashSet<MoebiusRiverTile>();
                var signatures = new Dictionary<MoebiusRiverTile, int>();
                foreach (var tile in tiles)
                {
                    int signature = HashCode.Combine(tile.transform.localToWorldMatrix, tile.profile,
                        tile.waterLevel, tile.foundationTop, tile.landSurfaceHeight, tile.originalWater);
                    foreach (var end in byTile[tile])
                        signature = HashCode.Combine(signature, end.kind, end.neighbour,
                            end.neighbour ? end.neighbour.transform.localToWorldMatrix : Matrix4x4.identity,
                            end.neighbour ? end.neighbour.WorldWaterHeight : 0, end.port.center, end.port.width,
                            end.neighbour ? end.neighbour.profile : null);
                    foreach (var end in byTile[tile]) signature = HashCode.Combine(signature, end.bankHeight, end.lipRadius);
                    signatures[tile] = signature;
                    if (!geometry.TryGetValue(tile, out var cached) || cached.signature != signature || geometrySettings != settingsSignature)
                        affected.Add(tile);
                }
                // Impact overlays retain the receiving mesh, so include upstream owners
                // whenever a receiving surface is regenerated.
                bool expanded;
                do
                {
                    expanded = false;
                    foreach (var end in allEnds)
                        if (end.kind == RiverEndKind.Falling && affected.Contains(end.neighbour) && affected.Add(end.tile)) expanded = true;
                } while (expanded);
                foreach (var old in new List<MoebiusRiverTile>(geometry.Keys))
                    if (!byTile.ContainsKey(old) || affected.Contains(old)) ClearTile(old);
                geometrySettings = settingsSignature;
                foreach (var tile in tiles)
                {
                    if (!affected.Contains(tile)) continue;
                    currentGeometry = new TileGeometry { root = NewObject(tile.name + " Connections", generated.transform), signature = signatures[tile] };
                    geometry.Add(tile, currentGeometry);
                    var ends = byTile[tile]; var inset = Vector4.zero;
                    foreach (var end in ends) if (end.kind == RiverEndKind.Falling) inset[(int)end.port.side] = .09f;
                    if (tile.IsRiver || generateLandFoundations)
                        OwnMesh("Terrain Foundation", tile, RiverConnectionGeometry.Foundation((tile.IsRiver ? tile.profile.tileSize : 17) * .5f,
                            floor - foundationDepth - tile.transform.position.y, tile.IsRiver && tile.profile.fullTileWater ? tile.waterLevel : tile.foundationTop, inset,
                            includeTop: !tile.IsRiver), foundationMaterial);
                    if (!tile.IsRiver) continue;
                    var material = Own(new Material(tile.profile.waterMaterial) { name = tile.name + " Connected Water", hideFlags = HideFlags.DontSave });
                    material.SetFloat("_AnimationPhase", 0); material.SetFloat("_EnableWaterfallImpact", 0);
                    material.SetVector("_ShoreWorldRect", tile.profile.shoreRect); material.SetFloat("_ShoreDistanceRange", tile.profile.shoreRange);
                    material.SetFloat("_EnableShoreMask", 1); material.SetFloat("_ShoreObjectSpace", 1);
                    material.SetTexture("_ShorelineMask", Own(RiverConnectionGeometry.Shore(tile.profile, ends)));
                    float half = tile.profile.tileSize * .5f; var rectangle = new Vector4(-half, -half, half, half); var fade = Vector4.zero;
                    foreach (var end in ends)
                    {
                        int side = (int)end.port.side;
                        if (end.kind == RiverEndKind.Falling) fade[side] = .65f;
                        if (end.kind != RiverEndKind.Joined) continue;
                        // Both meshes end at the same plane/height, including small accepted
                        // placement errors. Flatten displacement there without stopping flow.
                        fade[side] = RiverConnectionGeometry.JoinBlendWidth;
                        if (end.port.side == RiverSide.North) rectangle.w = end.joinCenter.z;
                        else if (end.port.side == RiverSide.East) rectangle.z = end.joinCenter.x;
                        else if (end.port.side == RiverSide.South) rectangle.y = end.joinCenter.z;
                        else rectangle.x = end.joinCenter.x;
                    }
                    material.SetVector("_RiverWaveFadeRect", rectangle); material.SetVector("_RiverWaveFadeWidths", fade);
                    var renderer = OwnMesh("Connected Water", tile, RiverConnectionGeometry.Water(tile.profile, tile.waterLevel, ends), material);
                    renderer.shadowCastingMode = ShadowCastingMode.Off; waterRenderers.Add(tile, renderer);
                    if (tile.originalWater) tile.originalWater.forceRenderingOff = true;
                    foreach (var end in ends) if (end.kind == RiverEndKind.Closed && !tile.profile.fullTileWater)
                    {
                        var cap = OwnMesh("Capsule Bank " + end.port.side, tile, RiverConnectionGeometry.Cap(end, tile.waterLevel, bankRounding), bankMaterial);
                        cap.sharedMaterials = new[] { bankMaterial, foundationMaterial };
                    }
                }
                JoinedCount = ClosedCount = WaterfallCount = 0;
                foreach (var end in allEnds)
                {
                    if (end.kind == RiverEndKind.Closed) ClosedCount++;
                    if (end.kind == RiverEndKind.Joined) JoinedCount++;
                    else if (end.kind == RiverEndKind.Falling)
                    {
                        if (affected.Contains(end.tile)) { currentGeometry = geometry[end.tile]; CreateWaterfall(end); }
                        WaterfallCount++;
                    }
                }
                JoinedCount /= 2; LastError = null; RebuildCount++; return true;
            }
            catch (Exception error) { LastError = error.Message; Clear(); return false; }
            finally { currentGeometry = null; lastSignature = Signature(); dirty = false; building = false; }
        }

        RiverEnd Describe(MoebiusRiverTile tile, RiverPort port)
        {
            var normal = tile.transform.TransformDirection(port.Normal);
            var desired = tile.transform.position + normal * tile.profile.tileSize;
            MoebiusRiverTile neighbour = null;
            foreach (var candidate in tiles)
            {
                if (candidate == tile) continue;
                var delta = candidate.transform.position - desired; delta.y = 0;
                if (delta.sqrMagnitude < positionTolerance * positionTolerance) { neighbour = candidate; break; }
            }
            var result = new RiverEnd { tile = tile, neighbour = neighbour, port = port, kind = RiverEndKind.Closed,
                capInset = capInset, bankHeight = Mathf.Max(tile.profile.bankHeight, neighbour ? neighbour.WorldBankHeight - tile.transform.position.y : tile.profile.bankHeight) };
            if (!neighbour || !neighbour.IsRiver) return result;
            if (RiverConnectionRules.Match(tile, port, neighbour, out var other, positionTolerance))
            {
                if (tile.profile.fullTileWater && !neighbour.profile.fullTileWater)
                {
                    var center = tile.transform.InverseTransformPoint(neighbour.transform.TransformPoint(other.Center));
                    var narrow = port; narrow.center = new Vector2(center.x, center.z); narrow.width = other.width; result.port = narrow;
                }
                float drop = tile.WorldWaterHeight - neighbour.WorldWaterHeight;
                result.kind = Mathf.Abs(drop) <= heightTolerance ? RiverEndKind.Joined : drop > 0 ? RiverEndKind.Falling : RiverEndKind.Receiving;
                if (result.kind == RiverEndKind.Joined)
                {
                    var position = tile.transform.TransformPoint(port.Center);
                    var common = (position + neighbour.transform.TransformPoint(other.Center)) * .5f;
                    common.y = (tile.WorldWaterHeight + neighbour.WorldWaterHeight) * .5f;
                    result.joinCenter = tile.transform.InverseTransformPoint(common);
                    result.joinWidth = (port.width + other.width) * .5f;
                    if (tile.profile.fullTileWater || neighbour.profile.fullTileWater)
                    {
                        common = position + normal * (Vector3.Dot(neighbour.transform.TransformPoint(other.Center) - position, normal) * .5f);
                        common.y = (tile.WorldWaterHeight + neighbour.WorldWaterHeight) * .5f;
                        result.joinCenter = tile.transform.InverseTransformPoint(common);
                        result.joinWidth = port.width;
                        result.port = port;
                    }
                }
                result.lipRadius = Mathf.Min(lipRadius, Mathf.Abs(drop) * .4f); return result;
            }
            return result;
        }

        void CreateWaterfall(RiverEnd end)
        {
            if (!waterfallPrefab || !impactMaterial) throw new InvalidOperationException("Assign the existing waterfall prefab and impact material.");
            var normal = end.tile.transform.TransformDirection(end.port.Normal);
            // Put the curved overhang beyond the upper tile's perimeter ledge, not inside its ground mesh.
            var center = end.tile.transform.TransformPoint(end.port.Center) + normal * end.lipRadius; center.y = end.neighbour.WorldWaterHeight;
            float height = end.tile.WorldWaterHeight - center.y, width = end.port.width;
            var root = Instantiate(waterfallPrefab, currentGeometry.root.transform); root.name = "Waterfall " + end.tile.name + " to " + end.neighbour.name;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
            root.transform.SetPositionAndRotation(center, Quaternion.LookRotation(-normal, Vector3.up)); root.transform.localScale = Vector3.one;
            var sheet = root.transform.Find("Waterfall Sheet");
            if (!sheet) throw new InvalidOperationException("The waterfall prefab needs its existing Waterfall Sheet child.");
            sheet.localPosition = Vector3.up * (height * .5f); sheet.localRotation = Quaternion.identity; sheet.localScale = Vector3.one;
            sheet.GetComponent<MeshFilter>().sharedMesh = Own(RiverConnectionGeometry.Sheet(width, height, end.lipRadius));
            var source = sheet.GetComponent<MeshRenderer>().sharedMaterial;
            var material = Own(new Material(source) { name = "Connected Waterfall Body", hideFlags = HideFlags.DontSave });
            float length = height - end.lipRadius + end.lipRadius * Mathf.PI * .5f;
            material.SetVector("_SheetSize", new Vector4(width, height, 0, 0)); material.SetFloat("_SurfaceLength", length);
            material.SetFloat("_LipStartV", (height - end.lipRadius) / length);
            material.SetColor("_UpstreamColor", waterRenderers[end.tile].sharedMaterial.GetColor("_WaterColor"));
            sheet.GetComponent<MeshRenderer>().sharedMaterial = material;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == "Impact Foam" || child.name == "Downstream Outflow") child.gameObject.SetActive(false);
                // Keep the rear cyan accents hidden when rebuilding connected waterfalls.
                if (child.name == "Splash Cloud Layer 1" || child.name == "Splash Cloud Layer 2") child.gameObject.SetActive(false);
                if (!child.name.StartsWith("Splash Cloud Layer", StringComparison.Ordinal)) continue;
                float widthScale = width / 5.8f, heightScale = Mathf.Clamp(height / 2, .12f, 1);
                var p = child.localPosition; p.x *= widthScale; child.localPosition = p;
                var s = child.localScale; s.x *= widthScale; s.y *= heightScale; child.localScale = s;
            }
            // Separate cutout impact meshes reuse the receiving geometry and existing impact HLSL.
            // This permits several incoming falls without overwriting a single water-material impact line.
            var receiving = waterRenderers[end.neighbour];
            var impact = NewObject("Impact Foam and Ripples", currentGeometry.root.transform);
            impact.transform.SetPositionAndRotation(end.neighbour.transform.position + Vector3.up * .05f, end.neighbour.transform.rotation);
            impact.AddComponent<MeshFilter>().sharedMesh = receiving.GetComponent<MeshFilter>().sharedMesh;
            var impactRenderer = impact.AddComponent<MeshRenderer>(); impactRenderer.shadowCastingMode = ShadowCastingMode.Off;
            var impactCopy = Own(new Material(impactMaterial) { name = "Connected Waterfall Impact", hideFlags = HideFlags.DontSave });
            var right = sheet.right * (width * .5f);
            impactCopy.SetVector("_ImpactPointA", center - right); impactCopy.SetVector("_ImpactPointB", center + right);
            impactCopy.SetVector("_ImpactFlowDirection", new Vector4(normal.x, normal.z, 0, 0));
            impactCopy.SetFloat("_EnableWaterfallImpact", 1); impactCopy.SetFloat("_ImpactRadius", .9f); impactCopy.SetFloat("_ImpactMaxDistance", 4);
            impactCopy.SetFloat("_ImpactRippleCount", 5); impactCopy.SetFloat("_ImpactSpawnInterval", .9f); impactCopy.SetFloat("_ImpactLifetime", 4.4f);
            impactCopy.SetFloat("_ImpactPhase", WaterfallCount * .67f);
            impactCopy.SetColor("_SurfaceColor", receiving.sharedMaterial.GetColor("_WaterColor")); impactRenderer.sharedMaterial = impactCopy;
        }

        MeshRenderer OwnMesh(string name, MoebiusRiverTile tile, Mesh mesh, Material material)
        {
            var go = NewObject(name, currentGeometry.root.transform); go.transform.SetPositionAndRotation(tile.transform.position, tile.transform.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = Own(mesh); var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.receiveShadows = true; return renderer;
        }
        T Own<T>(T value) where T : UnityEngine.Object { (currentGeometry != null ? currentGeometry.resources : owned).Add(value); return value; }
        static GameObject NewObject(string name, Transform parent)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave }; go.transform.SetParent(parent, false); return go;
        }
        static void Release(UnityEngine.Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        void Clear()
        {
            foreach (var tile in new List<MoebiusRiverTile>(geometry.Keys)) ClearTile(tile);
            foreach (var tile in tiles) if (tile && tile.originalWater) tile.originalWater.forceRenderingOff = false;
            if (generated) { generated.SetActive(false); Release(generated); generated = null; }
            foreach (var value in owned) Release(value); owned.Clear(); waterRenderers.Clear();
            JoinedCount = ClosedCount = WaterfallCount = 0;
        }
        void ClearTile(MoebiusRiverTile tile)
        {
            if (!geometry.TryGetValue(tile, out var data)) return;
            if (tile && tile.originalWater) tile.originalWater.forceRenderingOff = false;
            if (data.root) { data.root.SetActive(false); Release(data.root); }
            foreach (var resource in data.resources) Release(resource);
            geometry.Remove(tile); waterRenderers.Remove(tile);
        }
    }
}
