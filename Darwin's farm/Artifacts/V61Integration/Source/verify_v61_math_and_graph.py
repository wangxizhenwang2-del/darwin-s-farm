#!/usr/bin/env python3
# 《达尔文农场》V6.1 非 Unity、可重放的图及运动边际公式数学检查。
# stdlib only; running this script CANNOT prove complete Unity game stability.
from pathlib import Path
import math, hashlib, json

ROOT=Path(__file__).resolve().parent
SRC=ROOT/'bioweb13.json'
expected_hash='763fdd910f882df696688cf96a794155daa468cc34543a6077cd120df65c3dd5'
raw=SRC.read_bytes(); assert hashlib.sha256(raw).hexdigest()==expected_hash, 'SOURCE GRAPH HASH MISMATCH'
source=json.loads(raw)
assert len(source['species'])==15
assert len(source['evolutionLinks'])==20
assert len(source['foodLinks'])==25
id2name={s['id']:s['name'] for s in source['species']}
assert len(id2name)==15 and len(set(id2name.values()))==15
for edge in source['evolutionLinks']+source['foodLinks']:
    assert set(edge)=={'from','to'} and edge['from'] in id2name and edge['to'] in id2name
assert len(set((e['from'],e['to']) for e in source['foodLinks']))==25

species={s['name']:s for s in source['species']}
for name,triple in {'鸡':(10,8,71),'彩鸡':(15,14,75),'灵感菇狸':(35,20,65),'孔雀':(25,40,60),'鹿':(72,50,35)}.items():
    s=species[name]
    assert s['baseEcologicalNiche']=='Land'
    assert (s['baseMovementAbility'],s['baseSize'],s['baseFertility'])==triple

evol={frozenset([id2name[e['from']],id2name[e['to']]]) for e in source['evolutionLinks']}
for p,q in [('鸡','彩鸡'),('彩鸡','孔雀'),('鸡','灵感菇狸'),('灵感菇狸','鹿')]:
    assert frozenset([p,q]) in evol
food={(id2name[e['from']],id2name[e['to']]) for e in source['foodLinks']}
for pair in [('鸡','孔雀'),('鸡','灵感菇狸'),('彩鸡','灵感菇狸')]:assert pair in food
assert ('彩鸡','孔雀') not in food and ('鹿','灵感菇狸') not in food

LAMBDA=2.5

def gain(m: int, advantage:float, connected:float)->float:
    assert 0<=m<=99 and 0<=connected<=1
    opportunity=LAMBDA*max(0.0,advantage)*connected/100.0*(1-m/100.0)
    normalized_cost=0.4*(2*m+1)/10000.0
    return opportunity-normalized_cost

def end_point(start, advantage, connected):
    m=start
    while m<100 and gain(m,advantage,connected)>1e-12:
        m+=1
    return m

expected={0.0:(10,10,10),0.1:(24,14,10),0.3:(49,32,10),0.5:(61,44,10)}
for advantage,want in expected.items():
    got=tuple(end_point(10,advantage,c) for c in [1.0,0.5,0.0])
    assert got==want, f'endpoint delta={advantage}: {got} != {want}'

# Numeric contracts of the confirmed formula. Boundaries tested on a grid.
for m in range(100):
    for advantage in [0,0.01,0.10,0.30,0.50,0.75,1.00]:
        assert math.isfinite(gain(m,advantage,1))
        assert gain(m,advantage,0)<0
        assert gain(m,0,1)<0
        assert gain(m,advantage,1)>=gain(m,advantage,0.5)>=gain(m,advantage,0)
        if m<99:
            assert gain(m+1,advantage,1)<gain(m,advantage,1)
    for advantage in [0,0.1,0.3,0.5]:
        assert gain(m,advantage,1)<=gain(m,advantage+0.01,1)

# Non-zero below actual migration threshold when the neighbor is sufficiently advantageous.
assert gain(10,0.3,1)>0
assert gain(10,0.3,0.5)>0
assert end_point(10,0.3,1)>=35
assert end_point(10,0.3,0.5)<72

print('PASS: graph SHA256/15 species/20 bidirectional candidate evolution edges/25 directional food edges')
print('PASS: five target templates and food whitelist sanity')
print('PASS: revised DIMINISHING-RETURN movement equation, stopping points and boundary monotonicity')
print('Scope: math contracts ONLY. No independent Unity runtime testing, no inference of 1500-2000-day reachability.')
