using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Render the actual deer over two different backgrounds through the current
// URP renderer. A solid body must not inherit background colors or ink edges.
public static class AirSpriteRenderingChecks
{
    public static string RunChecks()
    {
        SpeciesData species = AssetDatabase.LoadAssetAtPath<SpeciesData>(
            "Assets/BiowebV46/Species/鹿飞-a61486cd.asset");
        Material material = Resources.Load<Material>("Population/AirPopulationSprite");
        if (species == null || material == null) throw new InvalidOperationException("Missing air artwork/material.");
        GameObject fixture = new GameObject("Air sprite render fixture");
        Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Material backgroundMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        RenderTexture target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
        target.antiAliasing = 1;
        Color32[] ReadCamera(Camera camera)
        {
            RenderPipeline.SubmitRenderRequest(camera,
                new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture previous = RenderTexture.active;
            Texture2D readback = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                readback.Apply();
                return readback.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }
        try
        {
            ImageConversion.LoadImage(source, File.ReadAllBytes(AssetDatabase.GetAssetPath(species.Sprite.texture)));
            var cameraObject = new GameObject("Isolated render camera");
            cameraObject.transform.SetParent(fixture.transform);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = 1.25f;
            camera.aspect = 1f;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.requiresDepthTexture = true;
            data.renderPostProcessing = false;
            var animal = new GameObject("Test deer");
            animal.transform.SetParent(fixture.transform);
            animal.layer = 31;
            var sprite = animal.AddComponent<SpriteRenderer>();
            sprite.sprite = species.Sprite;
            float scale = 2f / species.Sprite.bounds.size.y;
            animal.transform.localScale = Vector3.one * scale;
            var background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.transform.SetParent(fixture.transform);
            background.layer = 31;
            background.transform.localPosition = Vector3.forward;
            background.transform.localScale = Vector3.one * 4;
            background.GetComponent<Renderer>().sharedMaterial = backgroundMaterial;

            // A central background depth edge used to leak through the default
            // transparent sprite during the Moebius full-screen ink pass.
            var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.transform.SetParent(fixture.transform);
            step.layer = 31;
            step.transform.localPosition = new Vector3(0.65f, 0, 0.8f);
            step.transform.localScale = new Vector3(1.3f, 4, 0.6f);
            step.GetComponent<Renderer>().sharedMaterial = backgroundMaterial;

            var texture = source.GetPixels32();
            int Compare(Material candidate, out int samples, out float mean, bool depthEdge = false)
            {
                sprite.sharedMaterial = candidate;
                step.SetActive(!depthEdge);
                backgroundMaterial.SetColor("_BaseColor", depthEdge ? Color.gray : new Color(0.9f, 0.05f, 0.05f, 1));
                Color32[] red = ReadCamera(camera);
                step.SetActive(true);
                backgroundMaterial.SetColor("_BaseColor", depthEdge ? Color.gray : new Color(0.05f, 0.05f, 0.9f, 1));
                Color32[] blue = ReadCamera(camera);
                int maximum = 0;
                long total = 0;
                samples = 0;
                Sprite asset = species.Sprite;
                // Sample only body interiors; anti-aliased silhouette pixels
                // naturally include their background at the cutout boundary.
                for (int y = 8; y < source.height - 8; y += 4)
                    for (int x = 8; x < source.width - 8; x += 4)
                    {
                        if (texture[y * source.width + x].a < 245 ||
                            texture[(y - 8) * source.width + x].a < 245 ||
                            texture[(y + 8) * source.width + x].a < 245 ||
                            texture[y * source.width + x - 8].a < 245 ||
                            texture[y * source.width + x + 8].a < 245) continue;
                        Vector3 world = new Vector3((x + 0.5f - asset.pivot.x) / asset.pixelsPerUnit * scale,
                            (y + 0.5f - asset.pivot.y) / asset.pixelsPerUnit * scale, 0);
                        Vector3 viewport = camera.WorldToViewportPoint(world);
                        int pixel = Mathf.Clamp((int)(viewport.y * 256), 0, 255) * 256 +
                            Mathf.Clamp((int)(viewport.x * 256), 0, 255);
                        Color32 a = red[pixel], b = blue[pixel];
                        int delta = Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));
                        maximum = Mathf.Max(maximum, delta);
                        total += delta;
                        samples++;
                    }
                mean = total / (float)Mathf.Max(1, samples);
                return maximum;
            }
            Material original = sprite.sharedMaterial;
            int baseline = Compare(original, out int oldSamples, out float oldMean);
            int fixedMaximum = Compare(material, out int samples, out float mean);
            int oldDepth = Compare(original, out _, out float oldDepthMean, true);
            int newDepth = Compare(material, out _, out float newDepthMean, true);
            if (samples < 500 || fixedMaximum > 2 || mean > 0.1f)
                throw new InvalidOperationException($"Body still blends with its background: samples={samples}; max={fixedMaximum}; mean={mean}.");
            if (newDepth > 2 || newDepthMean > 0.1f)
                throw new InvalidOperationException($"Background depth ink leaks through body: max={newDepth}; mean={newDepthMean}.");
            if (material.shader.name != "Darwin/Population Air Sprite" || material.renderQueue > 2500 ||
                material.FindPass("DepthOnly") < 0 || material.FindPass("DepthNormalsOnly") < 0)
                throw new InvalidOperationException("Air sprite does not provide opaque depth/normal passes.");
            var errors = ShaderUtil.GetShaderMessages(material.shader)
                .Where(message => message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToArray();
            if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors.Select(error => error.message)));
            string report = $"PASS: actual 鹿飞 sprite rendered through URP over red/blue backgrounds and a depth edge.\n" +
                $"Body interior samples={samples}; opaque material max channel difference={fixedMaximum}/255; mean={mean:F4}/255.\n" +
                $"Default transparent material comparison: samples={oldSamples}; max={baseline}/255; mean={oldMean:F4}/255.\n" +
                $"Background depth edge on/off: opaque max={newDepth}/255, mean={newDepthMean:F4}; default transparent max={oldDepth}/255, mean={oldDepthMean:F4}.\n" +
                "Alpha-cutout forward, depth and depth-normal passes present; shader compile errors=0.\n";
            Directory.CreateDirectory("Artifacts/AirPopulationFlight");
            File.WriteAllText("Artifacts/AirPopulationFlight/sprite-opacity-check.txt", report);
            return report;
        }
        finally
        {
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(backgroundMaterial);
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }
}
