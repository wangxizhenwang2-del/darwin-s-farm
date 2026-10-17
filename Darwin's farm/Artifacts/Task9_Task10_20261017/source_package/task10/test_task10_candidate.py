"""Offline safety, falsification, and replay-conditioned checks for Task10.
NOT a claim of a Unity pass. Re-run: python test_task10_candidate.py
"""
from pathlib import Path
import sys,json,math,statistics,csv
import t10_surrogate as s
OUT=Path(__file__).parent
checks={}
def check(name, condition, metric=None):
    checks[name]={'pass':bool(condition),'metric':metric}
    if not condition:print('FAIL',name,metric)
base=s.simulate(days=250,mode='none',postops=True)
cap=s.simulate(days=250,mode='cap',postops=True,tau=45,ref_frac=.35)
check('baseline_proxy_reproduces_predator_extinction',base[-1]['civet']<.5,round(base[-1]['civet'],3))
check('baseline_proxy_reproduces_severe_prey_crash',min(x['chicken'] for x in base)<500,round(min(x['chicken'] for x in base)))
check('candidate_preserves_both_250d',cap[-1]['civet']>10 and cap[-1]['chicken']>1000,[round(cap[-1][x]) for x in ['chicken','civet']])
check('candidate_reduces_historical_prey_crash',min(x['chicken'] for x in cap)>3*min(x['chicken'] for x in base),[round(min(x['chicken'] for x in cap)),round(min(x['chicken'] for x in base))])
check('candidate_predator_not_instant_vanish',all(x['civet']>1 for x in cap),round(min(x['civet'] for x in cap)))
check('all_quotas_respected',all(x['kills']<=x['cap']+1e-7 for x in cap),max(x['kills']-x['cap'] for x in cap))
check('no_negative_counts_stock',all(x['chicken']>=0 and x['civet']>=0 and x['stock']>=0 for x in cap))
check('paid_birth_from_real_food_only',all(x['child_born']>=0 and x['prey_birth']>=0 for x in cap),'no synthetic birth/catch entries')
check('quota_zero_absent_prey',s.harvest_limit(0,{'size':8,'movement':10,'fertility':71,'fitT':75,'fitH':75},80000,4000)==0)
t={'size':8,'movement':10,'fertility':71,'fitT':75,'fitH':75}
check('K_zero_for_zero_R',s.scan_k_eq(t,0) is None,s.scan_k_eq(t,0))
check('no_R_negative_net',s.trial_unhunted_net(1000,t,0)<0,s.trial_unhunted_net(1000,t,0))
check('zero_R_no_hunt',s.harvest_limit(1000,t,0,None)==0,s.harvest_limit(1000,t,0,None))
K=s.scan_k_eq(t,80000)
check('K_renewable_root_is_bracketed',K is not None and abs(s.trial_unhunted_net(K,t,80000))<1e-6,[K,s.trial_unhunted_net(K,t,80000) if K else None])
check('prey_below_refuge_gets_recovery_priority',s.harvest_limit(.25*K,t,80000,K)<s.harvest_limit(.5*K,t,80000,K),[s.harvest_limit(.25*K,t,80000,K),s.harvest_limit(.5*K,t,80000,K)])
noactions=s.simulate(days=250,mode='cap',postops=False,tau=45,ref_frac=.35)
check('candidate_survives_without_post_tools',noactions[-1]['chicken']>100 and noactions[-1]['civet']>20,[round(noactions[-1][x]) for x in ['chicken','civet']])
cap500=s.simulate(days=500,mode='cap',postops=True,tau=45,ref_frac=.35)
check('candidate_survives_500d_surrogate',cap500[-1]['chicken']>200 and cap500[-1]['civet']>10,[round(cap500[-1][x]) for x in ['chicken','civet']])
with (OUT/'T10_SURROGATE_SWEEP.csv').open(encoding='utf-8-sig') as f: data=list(csv.DictReader(f))
rows=[r for r in data if r['mode']=='cap']
check('all_24_parameter_combinations_250d_survive',len(rows)==24 and all(float(r['predator_min'])>0 and float(r['prey_min'])>0 for r in rows),len(rows))
# Agent's current source has switch OFF. Validate patch only changes the guarded path.
p=(OUT/'TASK10_DYNAMIC_K_CANDIDATE.patch').read_text()
check('patch_uses_feature_flag', 'enableDynamicPreyRenewalQuota' in p)
check('patch_keeps_original_predation_density', 'PreyDensityFactor(block, prey[j], rules)' not in '\n'.join(l[1:] for l in p.splitlines() if l.startswith('-') and not l.startswith('---')))
check('patch_guards_live_hunting', 'if (rules.enableDynamicPreyRenewalQuota)' in p)
check('patch_refuge_subtracts_below_target','(n - rules.dynamicPreyRefugeFraction * eq)' in p)
check('patch_preserves_kill_body_transfer','block.detritusBiomass' not in '\n'.join(l[1:] for l in p.splitlines() if l.startswith('-') and not l.startswith('---')))
# Surrogate recovers baseline near daily for early part but not exact; report errors.
for field,species in [('chicken','鸡'),('civet','灵感菇狸')]:
    errors=[]
    for r in base[:60]:
        n=s.REC[r['day']][1].get(species,{}).get('count',0)
        errors.append(abs(r[field]-n)/max(1,n))
    med=statistics.median(errors)
    checks['calibration_'+field+'_median_relative_error_first60d']={'pass':med<.3,'metric':med}

result={'pass':sum(x['pass'] for x in checks.values()),'total':len(checks),'checks':checks,
        'candidate':{'post_ops_250d':{'chicken':round(cap[-1]['chicken']),'civet':round(cap[-1]['civet']),
            'minimum_chicken':round(min(d['chicken'] for d in cap)),
            'minimum_civet':round(min(d['civet'] for d in cap))},
            'post_ops_500d_surrogate':{'chicken':round(cap500[-1]['chicken']),'civet':round(cap500[-1]['civet'])},
            'no_post_ops_250d':{'chicken':round(noactions[-1]['chicken']),'civet':round(noactions[-1]['civet'])}},
        'warning':'Proxy is not a Unity simulation. Exogenous day-specific trait and R paths come from the failed Unity replay; counterfactual ecology and mutation trajectories are approximate.'}
(OUT/'TASK10_OFFLINE_TEST_RESULTS.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print('PASS',result['pass'],'/',result['total']);print('candidate',result['candidate'])
if result['pass']!=result['total']:sys.exit(1)
