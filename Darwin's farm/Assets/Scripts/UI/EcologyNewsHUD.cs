using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// One preauthored card, bounded priority queue, unscaled presentation clock.
public sealed class EcologyNewsHUD : MonoBehaviour
{
    [SerializeField] private RectTransform card;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TMP_Text heading, body, dayLabel;
    private readonly List<EcologicalEventData> pending=new List<EcologicalEventData>();
    private readonly HashSet<string> seen=new HashSet<string>();
    private EcologicalEventData current;
    private EcologyNewsDetector detector;
    private SpeciesUnlockPresenter unlockPresenter;
    private int phase;
    private float timer;
    private const int Capacity=64;
    [Serializable] public sealed class SaveState
    {
        public List<EcologicalEventData> pending=new List<EcologicalEventData>();
        public List<string> seen=new List<string>();
        public EcologicalEventData current;
        public int phase;
        public float timer;
    }
    public SaveState CaptureSaveState() => new SaveState {
        pending=new List<EcologicalEventData>(pending),seen=new List<string>(seen),
        current=current,phase=phase,timer=timer };
    public void RestoreSaveState(SaveState state)
    {
        if(state==null||state.pending==null||state.seen==null||
            state.pending.Count>Capacity||state.phase<0||state.phase>3)
            throw new ArgumentException("Invalid ecology news queue.");
        pending.Clear();pending.AddRange(state.pending);
        seen.Clear();foreach(string id in state.seen)seen.Add(id);
        current=state.current;phase=state.phase;timer=state.timer;
        if(current!=null)
        { heading.text=Title(current.type);body.text=current.message;
          dayLabel.text="第 "+current.gameDay+" 日"; }
        group.alpha=0;
    }
    private void Start()
    {
        detector=FindFirstObjectByType<EcologyNewsDetector>();
        unlockPresenter=FindFirstObjectByType<SpeciesUnlockPresenter>();
        if(detector!=null)detector.Bus.Published+=Enqueue;
        group.alpha=0;group.blocksRaycasts=false;group.interactable=false;
    }
    private void OnDestroy(){if(detector!=null)detector.Bus.Published-=Enqueue;}
    private void Enqueue(EcologicalEventData value)
    {
        if(value==null || !seen.Add(value.eventId))return;
        // OnSpeciesUnlocked owns the card. The detector can publish the same
        // discovery later in its day-end batch; its toast is released by the
        // card's reveal callback instead of appearing at the batch boundary.
        if(value.type==EcologicalEventType.NewSpecies && unlockPresenter!=null &&
            unlockPresenter.HandlesSpeciesId(value.speciesId))return;
        QueueReady(value);
    }
    public void ShowSpeciesUnlockAtReveal(string speciesId,string speciesName,
        bool startup,int gameDay)
    {
        string id="unlock:"+(string.IsNullOrEmpty(speciesId)?speciesName:speciesId);
        if(!seen.Add(id))return;
        var value=new EcologicalEventData {
            eventId=id,gameDay=gameDay,sequence=-1,type=EcologicalEventType.NewSpecies,
            speciesId=speciesId,speciesName=speciesName,
            message=startup?"开局观察到「"+speciesName+"」！":
                "新物种「"+speciesName+"」已经解锁！"
        };
        // A discovery must appear at the reveal even when an older news card
        // is still visible. Resume that card from the queue afterwards.
        if(current!=null)QueueReady(current);
        current=value;phase=0;timer=0f;
        heading.text=Title(value.type);body.text=value.message;
        dayLabel.text="第 "+value.gameDay+" 日";
    }
    private void QueueReady(EcologicalEventData value)
    {
        if(pending.Count>=Capacity)
        {
            int weakest=pending.FindIndex(x=>x.Priority==1);
            if(weakest<0 || value.Priority==1)return;
            pending.RemoveAt(weakest);
        }
        pending.Add(value);
    }
    private void Update()
    {
        // Menus hide the toast and freeze the presentation clock.
        if(Time.timeScale==0f){group.alpha=0;return;}
        if(unlockPresenter!=null && unlockPresenter.IsPresenting &&
            unlockPresenter.Phase<SpeciesUnlockPresenter.CardPhase.Revealing)
        {
            group.alpha=0;
            return;
        }
        if(current==null)
        {
            if(pending.Count==0){group.alpha=0;return;}
            pending.Sort((a,b)=>{int priority=b.Priority.CompareTo(a.Priority);
                if(priority!=0)return priority;
                int day=a.gameDay.CompareTo(b.gameDay);
                return day!=0?day:a.sequence.CompareTo(b.sequence);});
            current=pending[0];pending.RemoveAt(0);
            heading.text=Title(current.type);body.text=current.message;
            dayLabel.text="第 "+current.gameDay+" 日";phase=0;timer=0;
        }
        timer+=Time.unscaledDeltaTime;
        if(phase==0)
        {
            float t=Mathf.Clamp01(timer/.35f);group.alpha=t;
            card.anchoredPosition=new Vector2(0,Mathf.Lerp(92,-72,1-(1-t)*(1-t)));
            if(t>=1){phase=1;timer=0;}
        }
        else if(phase==1)
        {
            group.alpha=1;card.anchoredPosition=new Vector2(0,-72);
            if(timer>=3){phase=2;timer=0;}
        }
        else if(phase==2)
        {
            group.alpha=1;
            if(timer>=3 || pending.Count>0){phase=3;timer=0;}
        }
        else
        {
            float t=Mathf.Clamp01(timer/.30f);group.alpha=1-t;
            card.anchoredPosition=new Vector2(0,Mathf.Lerp(-72,92,t*t));
            if(t>=1){current=null;phase=0;timer=0;}
        }
    }
    private static string Title(EcologicalEventType type)
    {
        switch(type)
        {
            case EcologicalEventType.NewSpecies:return "✿ 新物种诞生";
            case EcologicalEventType.LocalExtinction:return "○ 本地灭绝";
            case EcologicalEventType.GlobalExtinction:return "◆ 全局灭绝";
            case EcologicalEventType.SpeciesReappeared:return "↻ 物种重新出现";
            case EcologicalEventType.SuccessfulMigration:return "↗ 成功迁徙";
            case EcologicalEventType.PopulationCrash:return "↓ 种群骤减";
            default:return "! 生态承载危机";
        }
    }
}
