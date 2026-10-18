using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public sealed class AnimationLabController : MonoBehaviour
{
    [Serializable]
    public sealed class Animal
    {
        public string displayName;
        public PopulationAnimationSet animations;
        public GameObject root;
        public SpriteRenderer sprite;
        public PopulationFrameAnimationController player;
    }

    [SerializeField] private Animal[] animals;
    [SerializeField] private float travelSpeed = 1.8f;
    [SerializeField] private float leftEdge = -3f;
    [SerializeField] private float rightEdge = 3f;
    private int selected;
    private int direction = 1;
    private bool moving = true;

    public void Configure(Animal[] configuredAnimals) => animals = configuredAnimals;

    public PopulationAnimationSet AnimationFor(PopulationFrameAnimationController player)
    {
        if (animals == null) return null;
        foreach (Animal animal in animals)
            if (animal != null && animal.player == player) return animal.animations;
        return null;
    }

    private void Start()
    {
        if (animals == null) return;
        foreach (Animal animal in animals)
        {
            if (animal?.root == null || animal.player == null || animal.sprite == null) continue;
            animal.player.Bind(animal.sprite, animal.animations, Color.white);
            animal.root.SetActive(false);
        }
        Select(0);
    }

    private void Update()
    {
        if (animals == null || animals.Length == 0) return;
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        bool left = keyboard != null && keyboard.leftArrowKey.wasPressedThisFrame;
        bool right = keyboard != null && keyboard.rightArrowKey.wasPressedThisFrame;
        bool space = keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
#else
        bool left = Input.GetKeyDown(KeyCode.LeftArrow);
        bool right = Input.GetKeyDown(KeyCode.RightArrow);
        bool space = Input.GetKeyDown(KeyCode.Space);
#endif
        if (left) Select((selected - 1 + animals.Length) % animals.Length);
        if (right) Select((selected + 1) % animals.Length);
        Animal animal = animals[selected];
        if (animal?.player == null || animal.animations == null || animal.root == null || animal.sprite == null) return;
        if (animal.player.IsCommonLoop)
        {
            if (space) moving = !moving;
            animal.player.Tick(Mathf.Min(Time.deltaTime, 0.2f), Time.time);
            if (moving)
            {
                float next = animal.root.transform.position.x + direction * travelSpeed * Time.deltaTime;
                if (next >= rightEdge) { next = rightEdge; direction = -1; }
                else if (next <= leftEdge) { next = leftEdge; direction = 1; }
                animal.root.transform.position = new Vector3(next, 0f, 0f);
            }
            animal.player.FaceHorizontal(direction);
            return;
        }
        if (space)
        {
            moving = !moving;
            if (moving) animal.player.RequestStart();
            else animal.player.RequestStop();
        }
        if (moving)
        {
            animal.player.RequestStart();
            if (animal.player.CanBeginWalk)
            {
                float remaining = direction > 0 ? rightEdge - animal.root.transform.position.x :
                    animal.root.transform.position.x - leftEdge;
                if (remaining < 0.5f)
                {
                    direction = -direction;
                    remaining = rightEdge - leftEdge;
                }
                animal.player.BeginWalk(remaining, travelSpeed);
            }
        }
        float distance = animal.player.Tick(Mathf.Min(Time.deltaTime, 0.2f), Time.time);
        animal.root.transform.position += Vector3.right * (direction * distance);
        animal.player.FaceHorizontal(direction);
        if (animal.player.HasTransition && !moving && animal.player.CanBeginWalk)
            moving = true;
    }

    private void Select(int index)
    {
        if (animals == null || animals.Length == 0) return;
        if (animals[selected]?.root != null) animals[selected].root.SetActive(false);
        selected = index;
        Animal animal = animals[selected];
        if (animal?.root == null) return;
        direction = 1;
        moving = true;
        animal.root.transform.position = new Vector3(leftEdge, 0f, 0f);
        animal.player.Bind(animal.sprite, animal.animations, Color.white);
        animal.root.SetActive(true);
        animal.player.RequestStart();
    }

    private void OnGUI()
    {
        if (animals == null || animals.Length == 0) return;
        Animal animal = animals[selected];
        GUILayout.BeginArea(new Rect(18, 18, 540, 120), GUI.skin.box);
        GUILayout.Label(animal.player.IsCommonLoop ?
            "Animation Lab  |  Left / Right: animal  |  Space: move / stop (animation independent)" :
            "Animation Lab  |  Left / Right: animal  |  Space: start / stop");
        GUILayout.Label($"{selected + 1}/{animals.Length}  {animal.displayName}  |  " +
            $"{animal.player.Phase}  |  frame {animal.player.CurrentFrame}  |  " +
            (moving ? "start requested" : "stop requested"));
        GUILayout.EndArea();
    }
}
