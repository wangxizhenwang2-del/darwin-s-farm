"""Task10 offline replay-conditioned mechanistic proxy, not Unity or a gameplay patch.
Calibrates exact daily birth/paid-cost/death and hunt demand formulas to historical
Unity states, then tests an explicit renewable harvest envelope. No economic system.
"""
from __future__ import annotations
import json, csv, math, copy, statistics, argparse
from pathlib import Path
HERE=Path(__file__).parent
RAW=HERE/'Artifacts/Task9_20261016/runs/task9-mushroom-formal.jsonl'
REC={}
for line in RAW.open(encoding='utf-8-sig'):
    if line.startswith('{"day"'):
        x=json.loads(line)
        # observed file may contain more than one tile, use origin (0,0)
        ts=[t for t in x['tiles'] if t['x']==0 and t['y']==0]
        if ts:
            pop={p['species']:p for p in ts[0]['populations']}
            REC[x['day']]=(ts[0],pop)

# Lightweight floats (Unity has per-species fractional carried births/deaths,
# and actual integer material transfers; expected-valued surrogate here).
def adult(sz,m): return .4*(sz+200/max(1,sz))*(1+.4*(min(100,max(0,m))/100)**2)
def young(sz): return .4*sz+.02*sz**2
def mass(sz): return 40*max(1,sz)
def fit(p,T,H): return max(0,min(1,1-(abs(T-p['fitT'])+abs(H-p['fitH']))/90))
def death0(n,sz,T,fitness):
    return n*(.008*20/(20+max(1,sz))+.5/(1+max(1,sz))**2 + .005*max(0,(50-T)/50)+.01*(1-fitness))
def stress(b,sz,fert):return b*.8*min(1,max(0,fert/100))*young(sz)/max(1,sz)
def per_prey(pred,prey):return mass(prey['size'])*min(1,pred['size']/(prey['size']+15))
def capture(pred,prey):
    chase=max(.1,min(1.5,1+(pred['movement']-prey['movement'])/40))
    sz=max(.1,min(1.5,((pred['size']+10)/(prey['size']+10))**2))
    return min(1,.5*chase*sz)
def pred_density(n,adultprey,ref=50000):
    h=max(1,.58*max(1,ref)/max(.0001,adultprey));return n*n/(n*n+h*h)

def feed_prey(n,traits,stock,grow,R,pred_kills=0,pred_traits=None):
    """Emulates single-herbivore adult-first paid births (not plant niche competition)."""
    ne=max(0,n-pred_kills); f=fit(traits,75,75)
    budget=min(max(0,stock+grow-max(1e5, min(1e5,(stock+grow)*.5))), max(0,grow)+.1*max(0,stock))
    # actual reserve floor for max S 1e6 is min(100000, stock_after_growth/2)
    stockag=stock+grow; floor=min(100000,stockag*.5)
    budget=min(max(0,stockag-floor),grow + min(max(0,stock-floor), stock*.1))
    demand=ne*adult(traits['size'],traits['movement'])
    opp=ne*.1*(traits['fertility']/100)*f
    childcost=mass(traits['size'])+young(traits['size'])
    allocation=min(budget,demand+opp*childcost)
    satisfaction=min(1,allocation/max(.0001,demand))
    opportunity=opp*satisfaction
    born=min(opportunity,max(0,(allocation-demand)/childcost))
    dead=death0(ne,traits['size'],75,f) + ne*.6*(1-satisfaction)+stress(born,traits['size'],traits['fertility'])
    # day-end biomass stock is growth+stock minus allocated plant
    return dict(n=max(0,ne+born-dead), born=born, natural=dead, allocation=allocation,
                satisfaction=satisfaction,stock=max(0,stockag-allocation),base=born-dead, adult=demand)

def unpredicted_k_food(traits,R):
    a=adult(traits['size'],traits['movement'])
    return max(1,R/a) if a>0 else 0

def trial_unhunted_net(n,traits,R):
    # Same paid birth and death as live single-prey fixture, on sustainable R,
    # not stored stock or simulation of the full plant-niche allocator.
    ne=max(0,n)
    f=fit(traits,75,75)
    demand=ne*adult(traits['size'],traits['movement'])
    food=min(max(0,R), demand+ne*.1*traits['fertility']/100*f*(mass(traits['size'])+young(traits['size'])))
    sat=min(1,food/max(1e-6,demand))
    births=min(ne*.1*traits['fertility']/100*f*sat,max(0,(food-demand)/(mass(traits['size'])+young(traits['size']))))
    dead=death0(ne,traits['size'],75,f)+ ne*.6*(1-sat) +stress(births,traits['size'],traits['fertility'])
    return births-dead

def scan_k_eq(traits,R):
    # The stable (+ to nonpositive) root in R-only budget; exact 1-prey surrogate.
    upper=max(100,4*R/max(.01,adult(traits['size'],traits['movement'])))
    xs=[1+i*(upper-1)/160 for i in range(161)]
    ys=[trial_unhunted_net(n,traits,R) for n in xs]
    roots=[]
    for i in range(160):
        if ys[i]>0 and ys[i+1]<=0:
            lo,hi=xs[i:i+2]
            for j in range(35):
                m=(lo+hi)/2
                if trial_unhunted_net(m,traits,R)>0:lo=m
                else:hi=m
            roots.append((lo+hi)/2)
    return max(roots) if roots else None

def harvest_limit(N,traits,R,k_est,rest_tau=50,ref_frac=.35):
    # G0(N) uses no predation, same R-only plant budget. The 'buffer' is excess
    # prey above a refuge Nref that is only exhausted over rest_tau days.
    if N <= 0 or not k_est or k_est<=0:return max(0,trial_unhunted_net(N,traits,R))
    refuge=ref_frac*k_est
    replen=trial_unhunted_net(N,traits,R)
    # Negative when prey falls below refuge: leave more food for recovery.
    return max(0,replen+(N-refuge)/rest_tau)

def simulate(start=429,days=250,mode='none',postops=True,founder_scale=1.,tau=50,ref_frac=.35):
    s0,p0=REC[start]
    c=next(x for x in p0.values() if x['species']=='鸡')
    cv=next(x for x in p0.values() if x['species']=='灵感菇狸')
    Nc=float(c['count']);Np=float(cv['count'])*founder_scale
    Sc=float(s0['stock'])
    Rc=float(s0['recovery']);reserve=float(cv['reserve'])*founder_scale
    data=[]
    prev_traits=(None,None)
    cachedK=None
    for day in range(start+1,start+days+1):
        krec=min(day,max(REC))
        # Route-conditioned traits are taken from actual Unity record at same day.
        # When the target is extinct, freeze last living predator traits (day511).
        ti,pi=REC[krec];cobs=pi['鸡'];vobs=pi.get('灵感菇狸',{})
        if mode=='fixed_traits':
            cp=copy.copy(c);vp=copy.copy(cv)
        else:
            cp=copy.copy(cobs)
            vp=copy.copy(vobs or (REC[min(krec,511)][1].get('灵感菇狸') or cv))
        # when postops disabled, no mechanical tool impulses and hold trait state
        # at first conversion day, but environment recovery may otherwise vary.
        if not postops:
            cp=copy.copy(c);vp=copy.copy(cv)
            Rc=float(s0['recovery'])
        else:
            Rc=float(ti['recovery'])
            if day>=start+1 and (day-1 in REC):
                # negative impulses from legal plant-stock ops at days > start
                # derive only scheduled actions, not a comparison to baseline stock
                # use 25000 for each -1 action; will test no-ops separately
                pass
            # Explicit stock actions from target scenario after 429
            for a in ACTIONS.get(day,[]):
                if a['kind']=='PlantStockChange':
                    Sc=max(0,min(1e6,Sc + 25000*a['tier']))
        growth=min(max(0,1e6-Sc),max(0,Rc))
        Sc+=growth
        f0=fit(cp,75,75);f1=fit(vp,75,75)
        energy=per_prey(vp,cp)
        desire=Np*(adult(vp['size'],vp['movement']) + .1*vp['fertility']/100*f1*(young(vp['size'])+mass(vp['size'])) +.1*adult(vp['size'],vp['movement']))-reserve
        # Unity per-head hunt attempt, then WholePreyClaim(targetEnergy) limit
        intent=Np*min(1,10/max(1,cp['size']))*capture(vp,cp)*pred_density(Nc,adult(cp['size'],cp['movement']))
        wanted=min(Nc,intent,max(0,math.ceil(max(0,desire)/max(.0001,energy))))
        kkey=(cp['size'],cp['movement'],cp['fertility'],Rc)
        if mode in ('cap','fixed_traits') and kkey !=prev_traits:
            cachedK=scan_k_eq(cp,Rc);prev_traits=kkey
        if mode in ('cap','fixed_traits'):
            limit=harvest_limit(Nc,cp,Rc,cachedK,tau,ref_frac)
            kills=min(wanted,limit)
        else: limit=float('inf');kills=wanted
        prey=feed_prey(Nc,cp,max(0,Sc-growth),growth,Rc,kills)
        Nc=prey['n'];Sc=prey['stock']
        # Predator is paid using prey physical energy moved to reserve. Actual
        # adult-first settlement, plus reserve, newborns fully paid.
        available=reserve+kills*energy
        pdemand=Np*adult(vp['size'],vp['movement'])
        psatisf=min(1,available/max(.0001,pdemand))
        popp=Np*.1*vp['fertility']/100*f1*psatisf
        pcost=young(vp['size'])+mass(vp['size'])
        pbirth=min(popp,max(0,(available-pdemand)/pcost))
        pdead=death0(Np,vp['size'],75,f1)+Np*.6*(1-psatisf)+stress(pbirth,vp['size'],vp['fertility'])
        Np=max(0,Np+pbirth-pdead)
        reserve=max(0,min((available-min(available,pdemand)-pbirth*pcost),Np*adult(vp['size'],vp['movement'])*10))
        data.append(dict(day=day,chicken=Nc,civet=Np,kills=kills,wanted=wanted,cap=limit,stock=Sc,renewal=Rc,
                         child_born=pbirth,child_death=pdead,prey_birth=prey['born'],prey_death=prey['natural'],
                         chickenK_eq=cachedK if cachedK else 0,sourceSatisfaction=prey['satisfaction']))
    return data

# Tools from exact replay input
INPUT=HERE/'Artifacts/Task9_20261016/inputs/task9-mushroom-formal.json'
ACTIONS={}
for a in json.loads(INPUT.read_text(encoding='utf-8-sig'))['actions']:
    ACTIONS.setdefault(a['day'],[]).append(a)

if __name__=='__main__':
    import argparse
    a=argparse.ArgumentParser();a.add_argument('--out',default=str(HERE/'T10_SURROGATE_SWEEP.csv'));z=a.parse_args()
    results=[]
    for post in [True,False]:
      for mode in ['none','cap']:
        for tau in ([0] if mode=='none' else [30,45,60,90]):
         for frac in ([0] if mode=='none' else [.25,.35,.45]):
          dat=simulate(mode=mode,postops=post,tau=tau or 50,ref_frac=frac)
          last=dat[-1];prey_min=min(x['chicken'] for x in dat);p_min=min(x['civet'] for x in dat)
          outcome=dict(mode=mode,post_actions=post,tau=tau,refuge_fraction=frac,
             prey_min=round(prey_min),predator_min=round(p_min),
             prey_d100=round(dat[99]['chicken']),pred_d100=round(dat[99]['civet']),
             prey_d250=round(last['chicken']),pred_d250=round(last['civet']),
             max_predator=round(max(x['civet'] for x in dat)),
             max_kills=round(max(x['kills'] for x in dat)),
             cap_active_days=sum(x['kills']<x['wanted']-1e-4 for x in dat),
             prey_k_eq_d1=round(dat[0]['chickenK_eq']))
          results.append(outcome)
    with open(z.out,'w',newline='',encoding='utf-8-sig') as f:
        w=csv.DictWriter(f,fieldnames=list(results[0]));w.writeheader();w.writerows(results)
    print(json.dumps(results,ensure_ascii=False,indent=2))
