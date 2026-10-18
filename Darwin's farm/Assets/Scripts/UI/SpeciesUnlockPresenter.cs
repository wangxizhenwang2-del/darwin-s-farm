using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The visuals are authored in WhiteBox/JournalCanvas. This component only
// binds the existing views and plays one card at a time.
[DefaultExecutionOrder(-800)]
public sealed class SpeciesUnlockPresenter : MonoBehaviour
{
    public enum CardPhase { Hidden, Entering, Mystery, Revealing, Description, Ready }

    [Serializable]
    public sealed class SpeciesUnlockData
    {
        public Sprite CreatureSprite;
        public string SpeciesId, SpeciesName, Description;
        public bool IsStartupSpecies;
    }

    [Header("Authored card")]
    [SerializeField] private RectTransform safeArea, cardRoot, questionMarkGroup, sparkleGroup;
    [SerializeField] private Image silhouetteImage, creatureImage;
    [SerializeField] private CanvasGroup overlayGroup, questionGroup, sparkleCanvasGroup;
    [SerializeField] private CanvasGroup descriptionGroup, continueGroup;
    [SerializeField] private TMP_Text nameText, descriptionText;
    [SerializeField] private Button continueButton;

    [Header("Unscaled seconds")]
    [SerializeField, Min(0f)] private float startupDelay = 3f;
    [SerializeField, Min(.01f)] private float entryDuration = .5f;
    [SerializeField, Min(.01f)] private float mysteryDuration = 1.3f;
    [SerializeField, Min(.01f)] private float revealDuration = .7f;
    [SerializeField, Min(.01f)] private float descriptionDuration = .5f;
    [SerializeField, Range(1f, 1.3f)] private float entryOvershoot = 1.05f;
    [SerializeField, Range(1f, 1.3f)] private float revealPop = 1.12f;

    public event Action<SpeciesUnlockData> OnRevealStarted;
    public CardPhase Phase { get; private set; } = CardPhase.Hidden;
    public bool IsPresenting => Phase != CardPhase.Hidden;
    public int QueuedCount => requests.Count;
    public int RevealCount { get; private set; }

    private readonly Queue<SpeciesUnlockData> requests = new Queue<SpeciesUnlockData>();
    private readonly HashSet<string> acceptedSpecies = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<SpeciesData> beforeGame = new HashSet<SpeciesData>();
    private DarwinJournalCanvas journal;
    private SimulationController simulation;
    private EcologyNewsHUD newsHud;
    private SimulationTime clock;
    private Coroutine playing;
    private bool gameEntered;
    private Vector2 lastSafeSize;
    private SpeciesUnlockData current;
    private readonly Vector2 cardRest = new Vector2(0f, -42f);
    private readonly Vector2 questionRest = new Vector2(104f, 68f);
    private readonly Vector2 descriptionRest = new Vector2(0f, -214f);

    private void Awake()
    {
        journal = FindFirstObjectByType<DarwinJournalCanvas>();
        simulation = FindFirstObjectByType<SimulationController>();
        newsHud = FindFirstObjectByType<EcologyNewsHUD>(FindObjectsInactive.Include);
        clock = FindFirstObjectByType<SimulationTime>();
        if (journal != null) journal.GameEntered += EnterGame;
        if (simulation != null) simulation.OnSpeciesUnlocked += SpeciesUnlocked;
        continueButton.onClick.AddListener(Continue);
        overlayGroup.alpha = 0f;
        overlayGroup.blocksRaycasts = false;
        overlayGroup.interactable = false;
        ResetCard();
    }

    private void OnDestroy()
    {
        if (journal != null) journal.GameEntered -= EnterGame;
        if (simulation != null) simulation.OnSpeciesUnlocked -= SpeciesUnlocked;
        if (continueButton != null) continueButton.onClick.RemoveListener(Continue);
        if (playing != null) StopCoroutine(playing);
    }

    private void LateUpdate()
    {
        if (safeArea == null) return;
        Vector2 size = safeArea.rect.size;
        if (size == lastSafeSize) return;
        lastSafeSize = size;
        float scale = Mathf.Min(1f, size.x / 430f, size.y / 700f);
        cardRoot.localScale = Vector3.one * Mathf.Max(.25f, scale);
    }

    private void EnterGame(bool loadedSave)
    {
        gameEntered = true;
        if (loadedSave)
        {
            beforeGame.Clear();
            return;
        }
        StartCoroutine(QueueStartingSpecies());
    }

    private IEnumerator QueueStartingSpecies()
    {
        float elapsed = 0f;
        while (elapsed < startupDelay)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        while (Time.timeScale <= 0f) yield return null;
        var starting = new HashSet<SpeciesData>(beforeGame);
        if (simulation != null)
            foreach (BlockInfo block in simulation.ReadBlocks())
                if (block != null && block.community != null)
                    foreach (PopulationData population in block.community)
                        if (population != null && population.speciesAmount > 0 && population.species != null)
                            starting.Add(population.species);
        foreach (SpeciesData species in starting.Where(s => s != null)
                     .OrderBy(s => s.SpeciesId, StringComparer.Ordinal))
            QueueSpecies(species, true);
        beforeGame.Clear();
    }

    private void SpeciesUnlocked(SpeciesData species)
    {
        if (species == null) return;
        if (!gameEntered) { beforeGame.Add(species); return; }
        QueueSpecies(species, false);
    }

    private void QueueSpecies(SpeciesData species, bool startup)
    {
        if (species == null || species.Portrait == null) return;
        Show(new SpeciesUnlockData {
            CreatureSprite = species.Portrait,
            SpeciesId = string.IsNullOrWhiteSpace(species.SpeciesId) ? species.SpeciesName : species.SpeciesId,
            SpeciesName = species.SpeciesName,
            Description = species.Description,
            IsStartupSpecies = startup
        });
    }

    public bool HandlesSpeciesId(string speciesId) =>
        !string.IsNullOrEmpty(speciesId) && acceptedSpecies.Contains(speciesId);

    public void Show(Sprite creatureSprite, string speciesName, string description,
        bool isStartupSpecies = false)
    {
        Show(new SpeciesUnlockData {
            CreatureSprite = creatureSprite,
            SpeciesId = speciesName,
            SpeciesName = speciesName,
            Description = description,
            IsStartupSpecies = isStartupSpecies
        });
    }

    public void Show(SpeciesUnlockData data)
    {
        if (data == null || data.CreatureSprite == null ||
            string.IsNullOrWhiteSpace(data.SpeciesName)) return;
        string key = string.IsNullOrWhiteSpace(data.SpeciesId) ? data.SpeciesName : data.SpeciesId;
        if (!acceptedSpecies.Add(key)) return;
        requests.Enqueue(data);
        if (!IsPresenting) PlayNext();
    }

    private void PlayNext()
    {
        if (requests.Count == 0)
        {
            current = null;
            Phase = CardPhase.Hidden;
            overlayGroup.alpha = 0f;
            overlayGroup.blocksRaycasts = false;
            overlayGroup.interactable = false;
            return;
        }
        current = requests.Dequeue();
        ResetCard();
        overlayGroup.alpha = 1f;
        overlayGroup.blocksRaycasts = true;
        overlayGroup.interactable = true;
        playing = StartCoroutine(Animate());
    }

    private void ResetCard()
    {
        silhouetteImage.sprite = current?.CreatureSprite;
        creatureImage.sprite = current?.CreatureSprite;
        silhouetteImage.color = Color.black;
        creatureImage.color = new Color(1f, 1f, 1f, 0f);
        silhouetteImage.rectTransform.localScale = Vector3.one;
        creatureImage.rectTransform.localScale = Vector3.one;
        nameText.text = "我是谁？？？";
        descriptionText.text = current?.Description ?? string.Empty;
        questionGroup.alpha = 1f;
        sparkleCanvasGroup.alpha = 0f;
        descriptionGroup.alpha = 0f;
        continueGroup.alpha = 0f;
        continueGroup.blocksRaycasts = false;
        continueGroup.interactable = false;
        continueButton.interactable = false;
        questionMarkGroup.anchoredPosition = questionRest;
        sparkleGroup.localScale = Vector3.one * .7f;
        descriptionGroup.transform.localPosition = descriptionRest + new Vector2(0f, -18f);
        cardRoot.anchoredPosition = cardRest;
        cardRoot.localRotation = Quaternion.identity;
    }

    private IEnumerator Animate()
    {
        Phase = CardPhase.Entering;
        yield return Tween(entryDuration, t => {
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            cardRoot.anchoredPosition = Vector2.Lerp(cardRest + Vector2.down * 520f, cardRest, eased);
            float scale = t < .75f ? Mathf.Lerp(.85f, entryOvershoot, t / .75f) :
                Mathf.Lerp(entryOvershoot, 1f, (t - .75f) / .25f);
            cardRoot.localScale = Vector3.one * scale * LayoutScale();
            cardRoot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-4f, 0f, eased));
        });
        Phase = CardPhase.Mystery;
        yield return Tween(mysteryDuration, t => {
            silhouetteImage.rectTransform.localScale = Vector3.one * (1f + .02f * (1f - Mathf.Cos(t * Mathf.PI * 4f)));
            questionMarkGroup.anchoredPosition = questionRest + new Vector2(0f, Mathf.Sin(t * Mathf.PI * 3f) * 6f);
        });
        Phase = CardPhase.Revealing;
        nameText.text = current.SpeciesName;
        RevealCount++;
        OnRevealStarted?.Invoke(current);
        if (newsHud != null)
            newsHud.ShowSpeciesUnlockAtReveal(current.SpeciesId, current.SpeciesName,
                current.IsStartupSpecies, clock != null ? clock.currentDay : 0);
        yield return Tween(revealDuration, t => {
            silhouetteImage.color = new Color(0f, 0f, 0f, 1f - t);
            creatureImage.color = new Color(1f, 1f, 1f, t);
            questionGroup.alpha = 1f - t;
            sparkleCanvasGroup.alpha = Mathf.Sin(t * Mathf.PI);
            sparkleGroup.localScale = Vector3.one * Mathf.Lerp(.7f, 1.15f, t);
            float pop = t < .4f ? Mathf.Lerp(1f, revealPop, t / .4f) :
                Mathf.Lerp(revealPop, 1f, (t - .4f) / .6f);
            creatureImage.rectTransform.localScale = Vector3.one * pop;
        });
        Phase = CardPhase.Description;
        yield return Tween(descriptionDuration, t => {
            descriptionGroup.alpha = t;
            descriptionGroup.transform.localPosition = descriptionRest + new Vector2(0f, Mathf.Lerp(-18f, 0f, t));
            continueGroup.alpha = t;
        });
        continueGroup.blocksRaycasts = true;
        continueGroup.interactable = true;
        continueButton.interactable = true;
        Phase = CardPhase.Ready;
        playing = null;
    }

    private IEnumerator Tween(float duration, Action<float> update)
    {
        float elapsed = 0f;
        update(0f);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            update(Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        update(1f);
    }

    private float LayoutScale()
    {
        if (safeArea == null) return 1f;
        Vector2 size = safeArea.rect.size;
        return Mathf.Max(.25f, Mathf.Min(1f, size.x / 430f, size.y / 700f));
    }

    public void Continue()
    {
        if (Phase != CardPhase.Ready) return;
        PlayNext();
    }
}
