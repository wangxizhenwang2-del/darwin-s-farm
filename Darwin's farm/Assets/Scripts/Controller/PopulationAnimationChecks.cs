#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Exercises the real presenter and movement in a disposable Play session.
public sealed class PopulationAnimationChecks : MonoBehaviour
{
    public static string Result { get; private set; } = "Not run";

    public static void Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run in Play mode.");
        new GameObject("PopulationAnimationChecks").AddComponent<PopulationAnimationChecks>();
    }

    private static void Require(bool valid, string message)
    {
        if (valid) return;
        Result = "FAILED: " + message;
        throw new InvalidOperationException(Result);
    }

    private IEnumerator Start()
    {
        Result = "Running: waiting for initial population";
        var movement = FindFirstObjectByType<PopulationMovementController>();
        var presenter = FindFirstObjectByType<PopulationVisualPresenter>();
        var clock = FindFirstObjectByType<SimulationTime>();
        if (!clock.IsPaused) clock.TogglePause();
        float deadline = Time.realtimeSinceStartup + 20f;
        while (movement.Populations.Count == 0 && Time.realtimeSinceStartup < deadline) yield return null;
        Require(movement.Populations.Count > 0, "No initial population.");
        WhiteboxPopulation population = movement.Populations[0];
        PopulationData data = population.EcologicalPopulation;
        Require(data != null, "Initial population has no ecological binding.");
        SpeciesData originalSpecies = data.species;
        string originalLineage = data.lineageName;
        string identity = population.UniqueId;
        var peacock = UnityEditor.AssetDatabase.LoadAssetAtPath<SpeciesData>(
            "Assets/BiowebV46/Species/孔雀-f0686f45.asset");
        Require(peacock != null && peacock.PopulationAnimation != null, "Peacock has no animation set.");
        var walkClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(
            "Assets/Art/Animals/Animations/PeacockWalk/PeacockWalk.anim");
        Require(walkClip != null && walkClip.isLooping && Mathf.Approximately(walkClip.length, 1f) &&
            Mathf.Approximately(walkClip.frameRate, 8f), "Walk must loop eight frames in exactly one second.");

        population.PauseWandering(true);
        data.species = peacock;
        data.lineageName = peacock.SpeciesName;
        presenter.SyncNow();
        SpriteRenderer image = population.Leader.GetComponentInChildren<SpriteRenderer>();
        Animator animator = image.GetComponent<Animator>();
        var driver = image.GetComponent<PopulationAnimationDriver>();
        Require(animator != null && animator.enabled, "Presenter did not enable the species Animator.");
        Vector3 scale = image.transform.localScale;
        Vector3 localPosition = image.transform.localPosition;
        yield return new WaitForSeconds(0.3f);
        Require(image.sprite == peacock.PopulationAnimation.IdleSprite &&
            animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Stopped animal did not show Idle.");

        Result = "Running: walking and repeated appearance sync";
        population.PauseWandering(false);
        var frames = new HashSet<Sprite>();
        int movingFrames = 0;
        float distance = 0f;
        Vector3 previous = population.Leader.position;
        deadline = Time.realtimeSinceStartup + 10f;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            presenter.SyncNow(); // Stronger than the normal 0.25 s sync interval.
            Vector3 current = population.Leader.position;
            Vector3 travel = current - previous;
            travel.y = 0f;
            distance += travel.magnitude;
            previous = current;
            if (driver.IsWalking)
            {
                movingFrames++;
                frames.Add(image.sprite);
            }
            Require(image.transform.localScale == scale && image.transform.localPosition == localPosition,
                "Animation changed the billboard's scale or anchor.");
            Vector3 offset = population.Leader.position - population.leader.groundPosition;
            Require(Mathf.Abs(offset.x) < 0.0001f && Mathf.Abs(offset.z) < 0.0001f,
                "Animation reintroduced lateral terrain jitter.");
            Require(population.UniqueId == identity, "Species change replaced the population.");
        }
        Require(distance > 2f && movingFrames > 20 && frames.Count == 8,
            $"Walk did not progress through all frames: distance={distance:F2}, movingFrames={movingFrames}, sprites={frames.Count}.");

        Result = "Running: stop, inherited appearance and static fallback";
        population.PauseWandering(true);
        yield return new WaitForSeconds(0.3f);
        Require(!driver.IsWalking && animator.GetCurrentAnimatorStateInfo(0).IsName("Idle") &&
            image.sprite == peacock.PopulationAnimation.IdleSprite, "Stopping did not return to Idle.");
        Vector3 stopped = population.Leader.position;
        yield return new WaitForSeconds(0.5f);
        Require(population.Leader.position == stopped, "Animation moved a stopped proxy.");

        data.species = null;
        data.lineageName = peacock.SpeciesName + "-陆生";
        presenter.SyncNow();
        Require(animator.enabled && animator.runtimeAnimatorController == peacock.PopulationAnimation.Controller,
            "A niche descendant lost its ancestor's animation.");

        data.species = originalSpecies;
        data.lineageName = originalLineage;
        presenter.SyncNow();
        yield return null;
        Require(!animator.enabled && animator.runtimeAnimatorController == null && image.sprite == originalSpecies.Sprite,
            "Changing back to a static species retained the old animation.");
        population.PauseWandering(false);
        deadline = Time.realtimeSinceStartup + 1f;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            Require(image.sprite == originalSpecies.Sprite, "Disabled Animator overwrote the static appearance.");
        }

        CheckFacingAndRecovery(peacock);
        Result = $"PASS: live Walk showed all 8 sprites across {movingFrames} moving frames and {distance:F2}m; repeated presenter sync, Idle/start/stop, stable anchors/ground XZ, inherited animation, static fallback, facing, vertical-only movement and teleport reset.";
        Debug.Log(Result);
        Destroy(gameObject);
    }

    private static void CheckFacingAndRecovery(SpeciesData peacock)
    {
        var fixture = new GameObject("AnimationDirectionFixture");
        fixture.transform.position = Vector3.one * 10000f;
        var cameraObject = new GameObject("AnimationDirectionCamera");
        try
        {
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            var image = fixture.AddComponent<SpriteRenderer>();
            var driver = fixture.AddComponent<PopulationAnimationDriver>();
            driver.Bind(image, peacock.PopulationAnimation, peacock.Sprite, Color.white, Vector3.zero);
            driver.SampleMotion(new Vector3(0.18f, 0f, 0f), camera, 0.1f);
            Require(driver.IsWalking && image.flipX, "Rightward movement did not face right.");
            driver.SampleMotion(Vector3.zero, camera, 0.1f);
            Require(driver.IsWalking && !image.flipX, "Leftward movement did not face left.");
            driver.SampleMotion(Vector3.up * 3f, camera, 0.1f);
            Require(!driver.IsWalking, "Vertical terrain adjustment triggered Walk.");
            driver.SampleMotion(Vector3.one * 100f, camera, 0.1f);
            Require(!driver.IsWalking && driver.MoveSpeed == 0f, "Teleport triggered Walk.");
        }
        finally
        {
            Destroy(fixture);
            Destroy(cameraObject);
        }
    }
}
#endif
