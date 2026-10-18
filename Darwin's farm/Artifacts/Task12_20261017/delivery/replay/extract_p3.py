"""Extract v0.5 P3 Unity evidence; no model computations or edits."""
import csv, json, re
from collections import defaultdict
from pathlib import Path
BASE=Path(__file__).resolve().parent.parent
RUNS=BASE/'runs'
FOCAL=[f'task12-rainforest-two-species-seed{s}' for s in (240,241,242)]
MIGRATION=['task12-mig-flat-production','task12-mig-slope1-production','task12-mig-slope2-production']

def read(stem):
    days={};events=[];ecology=[];actions=[]
    for line in (RUNS/(stem+'.jsonl')).open(encoding='utf-8-sig'):
        if line.startswith('{') and '"modelHash"' in line:
            d=json.loads(line);days[d['day']]=d
        elif line.startswith('event '):events.append(line.strip())
        elif line.startswith('ecology '):ecology.append(line.strip())
        elif line.startswith('action '):actions.append(line.strip())
    return days,events,ecology,actions

def kv(line):return dict(re.findall(r'(\w+)=([^ ]+)',line))

def write_csv(name,fields,rows):
    with (BASE/name).open('w',encoding='utf-8-sig',newline='') as f:
        w=csv.DictWriter(f,fieldnames=fields);w.writeheader();w.writerows(rows)

def main():
    daily=[];evo=[];mig=[];mass=[];summary={}
    for stem in FOCAL:
        days,events,ecology,actions=read(stem);seed=int(stem[-3:]);species={}
        for day,d in sorted(days.items()):
            for t in d['tiles']:
                for v in t['populations']:
                    daily.append(dict(scenario=stem,seed=seed,day=day,tile_x=t['x'],tile_y=t['y'],species=v['species'],N=v['count'],K_est=v['environmentalK'],food_K=v['foodK'],daily_intake_capacity=v['carryingCapacity'],allocated_food=v['allocatedFood'],births=v['births'],deaths=v['deaths'],predation_deaths=v['predationDeaths'],body=v['body'],reserve=v['reserve'],stock=t['stock'],recovery=t['recovery'],temperature=t['temperature'],humidity=t['humidity'],total_material=d['totalMaterial'],plant_growth=d['plantGrowthToday'],accounted_sink=d['accountedBiomassSinkToday'],model_hash=d['modelHash']))
                    species.setdefault(v['species'],{})[day]=v['count']
        for event in events:
            if 'kind=PresetEvolution' in event:
                x=kv(event);payment=next((kv(y).get('plantPayment') for y in ecology if y.startswith('ecology day='+x['day']+' ') and 'kind=preset_evolution' in y),None)
                evo.append(dict(scenario=stem,seed=seed,day=x['day'],source=x['source'],target=x['target'],species=x['species'],amount=x['amount'],source_before=x['sourceBefore'],source_after=x['sourceAfter'],target_before=x['targetBefore'],target_after=x['targetAfter'],plant_payment=payment,raw_event=event))
        acts=defaultdict(float)
        for line in actions:
            x=kv(line)
            if 'materialDelta' in x:acts[int(x['day'])]+=float(x['materialDelta'])
        for day in range(1,max(days)+1):
            a,b=days[day-1],days[day]
            delta=b['totalMaterial']-a['totalMaterial'];external=b['plantGrowthToday']-b['accountedBiomassSinkToday']+acts[day]
            mass.append(dict(scenario=stem,seed=seed,day=day,material_delta=delta,plant_growth=b['plantGrowthToday'],accounted_sink=b['accountedBiomassSinkToday'],player_material_delta=acts[day],residual=delta-external,total_material=b['totalMaterial']))
        summary[stem]={'formalEvolutions':len([x for x in events if 'kind=PresetEvolution' in x]),'final':{k:v.get(1300,0) for k,v in species.items()},'minimumAfterRecovery':{k:min(v.get(i,0) for i in range(275,1301)) for k,v in species.items()},'maximumAbsoluteMassResidual':max(abs(float(r['residual'])) for r in mass if r['scenario']==stem),'finalTotalMaterial':days[1300]['totalMaterial']}
    for stem in MIGRATION:
        _,events,_,_=read(stem)
        for event in events:
            if 'kind=Migration' not in event:continue
            x=kv(event)
            src=int(x['sourceBefore'])-int(x['sourceAfter']);dst=int(x['targetAfter'])-int(x['targetBefore']);amount=int(x['amount'])
            mig.append(dict(scenario=stem,seed=240,day=x['day'],source=x['source'],target=x['target'],amount=amount,source_before=x['sourceBefore'],source_after=x['sourceAfter'],target_before=x['targetBefore'],target_after=x['targetAfter'],outcome=x['outcome'],event_conserved=src==dst==amount,raw_event=event))
    write_csv('P3_DAILY_ECOLOGY.csv',list(daily[0]),daily)
    write_csv('P3_EVOLUTION_EVENTS.csv',list(evo[0]),evo)
    write_csv('P3_MIGRATION_EVENTS.csv',list(mig[0]),mig)
    write_csv('P3_MATERIAL_LEDGER.csv',list(mass[0]),mass)
    (BASE/'P3_SUMMARY.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'daily':len(daily),'formal':len(evo),'migration':len(mig),'mass':len(mass)},ensure_ascii=False))
if __name__=='__main__':main()
