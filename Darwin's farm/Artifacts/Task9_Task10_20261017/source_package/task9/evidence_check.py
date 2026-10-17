"""One-shot evidence integrity and risk assertions; NOT a Unity simulator."""
import csv,json,pathlib
p=pathlib.Path(__file__).resolve().parent
rows=list(csv.DictReader((p/'UNITY_TRAJECTORIES.csv').open(encoding='utf-8-sig',newline='')))
m={(r['scenario'],int(r['day'])):r for r in rows}
get=lambda s,d,k:float(m[(s,d)][k])
assert get('predator',429,'civet_N')==267
assert get('predator',493,'civet_N')==1975
assert get('predator',512,'civet_N')==0
assert get('predator',498,'chicken_N')==395
assert get('color',132,'color_N')==374
assert get('color',632,'color_N')==58
assert get('color',750,'color_N')==28
assert get('predator',495,'civet_sat')<.7
neg=json.loads((p/'TASK9_ACCEPTANCE_RESULT.json').read_text())
assert neg['negativeControls']['status']=='PASS'
assert neg['defaultEnabled'] is False
assert neg['mushroom']['day425PredatorTrials'][0]['preyRenewal']==0
assert sum(x['pass'] for x in neg['mushroom']['day425PredatorTrials'])>=2
assert any(x['pass'] for x in neg['mushroom']['day425PredatorTrials'][1:])
src=json.loads((p/'task9-mushroom-formal.json').read_text())
stop=json.loads((p/'task9-mushroom-stop-after-429.json').read_text())
assert [a for a in src['actions'] if a['day']>429]==[a for a in src['actions'] if a['day'] in (434,448,462,476,490,504,518,532,546)]
assert stop['actions']==[a for a in src['actions'] if a['day']<=429]
for f in p.glob('task9-predator-founder-*.json'):
 d=json.loads(f.read_text());assert d['t95PredatorFounderFraction'] in (.005,.01,.02,.05)
 assert d['actions']==stop['actions']
print('PASS: 17+ evidence checks, no model simulation claimed')
