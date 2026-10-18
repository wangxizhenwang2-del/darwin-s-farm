using UnityEngine;

// Samples the movement proxy, but animates only the billboard's sprite.
[DisallowMultipleComponent]
public sealed class PopulationAnimationDriver : MonoBehaviour
{
    private static readonly int IsMovingParameter = Animator.StringToHash("IsMoving");
    private static readonly int WalkSpeedParameter = Animator.StringToHash("WalkSpeed");
    private const float StartSpeed = 0.08f;
    private const float StopSpeed = 0.03f;
    private const float StopDelay = 0.08f;

    private SpriteRenderer image;
    private Animator animator;
    private PopulationAnimationSet animationSet;
    private Vector3 previousGround;
    private bool sampled;
    private float stationaryTime;
    public bool IsWalking { get; private set; }
    public float MoveSpeed { get; private set; }

    public void Bind(SpriteRenderer renderer, PopulationAnimationSet animations,
        Sprite fallback, Color tint, Vector3 groundPosition)
    {
        image = renderer;
        animationSet = animations != null && animations.IsUsable ? animations : null;
        if (animator != null)
        {
            animator.enabled = false;
            // Detach before assigning the fallback: removing a controller restores
            // its previous sprite binding and would otherwise overwrite it.
            animator.runtimeAnimatorController = null;
        }
        PopulationSpriteDisplay.Configure(image,
            animationSet != null ? animationSet.IdleSprite : fallback, tint);
        image.flipX = false;
        ResetMotion(groundPosition);
        if (animationSet == null)
        {
            return;
        }

        if (animator == null) animator = gameObject.AddComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.Normal;
        animator.runtimeAnimatorController = animationSet.Controller;
        animator.enabled = true;
        animator.Rebind();
        animator.SetBool(IsMovingParameter, false);
        animator.SetFloat(WalkSpeedParameter, 1f);
        animator.Play("Idle", 0, 0f);
        animator.Update(0f);
    }

    public void ResetMotion(Vector3 groundPosition)
    {
        previousGround = groundPosition;
        sampled = true;
        stationaryTime = 0f;
        MoveSpeed = 0f;
        IsWalking = false;
        if (animator != null && animator.enabled && animator.runtimeAnimatorController != null)
            animator.SetBool(IsMovingParameter, false);
    }

    public void SampleMotion(Vector3 groundPosition, Camera camera, float deltaTime)
    {
        if (animationSet == null || animator == null || !animator.enabled) return;
        if (!sampled || deltaTime <= 0f)
        {
            ResetMotion(groundPosition);
            return;
        }

        Vector3 travel = groundPosition - previousGround;
        travel.y = 0f;
        previousGround = groundPosition;
        MoveSpeed = travel.magnitude / deltaTime;
        // Terrain recovery and staging can relocate a population between frames.
        if (MoveSpeed > animationSet.ReferenceMoveSpeed * 4f)
        {
            ResetMotion(groundPosition);
            return;
        }

        if (MoveSpeed >= (IsWalking ? StopSpeed : StartSpeed))
        {
            IsWalking = true;
            stationaryTime = 0f;
            animator.SetFloat(WalkSpeedParameter,
                Mathf.Clamp(MoveSpeed / animationSet.ReferenceMoveSpeed, 0.35f, 2.5f));
            if (camera != null)
            {
                Vector3 right = camera.transform.right;
                right.y = 0f;
                float horizontal = Vector3.Dot(travel.normalized, right.normalized);
                // Keep the last facing when moving almost directly towards the camera.
                if (Mathf.Abs(horizontal) > 0.15f)
                    image.flipX = animationSet.ArtworkFacesLeft ? horizontal > 0f : horizontal < 0f;
            }
        }
        else
        {
            stationaryTime += deltaTime;
            if (stationaryTime >= StopDelay) IsWalking = false;
        }
        animator.SetBool(IsMovingParameter, IsWalking);
    }

    private void OnEnable() => sampled = false;
}
