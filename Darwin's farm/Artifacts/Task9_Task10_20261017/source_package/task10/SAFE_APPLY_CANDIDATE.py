"""Exact-base guarded installer for Task10 candidate. No silent source overwrite.
Usage: python SAFE_APPLY_CANDIDATE.py --project PATH --check [or --apply]
Uses the two Task9 acceptance source files as known base. If project diverged,
stop and have Agent port the *small* patch manually; never overwrite edits.
"""
import argparse,hashlib,json,pathlib,sys
HERE=pathlib.Path(__file__).resolve().parent
FILES=['Assets/Scripts/Simulation/LandLifeRules.cs','Assets/Scripts/Simulation/LandRuleTuning.cs']
def norm(b):return b.replace(b'\r\n',b'\n').replace(b'\r',b'\n')
def sha(b):return hashlib.sha256(norm(b)).hexdigest()
def main():
    p=argparse.ArgumentParser()
    p.add_argument('--project',required=True)
    mx=p.add_mutually_exclusive_group(required=True)
    mx.add_argument('--check',action='store_true');mx.add_argument('--apply',action='store_true')
    a=p.parse_args()
    root=pathlib.Path(a.project)
    outcomes=[];change=[]
    for name in FILES:
        base=(HERE/'baseline'/name).read_bytes()
        cand=(HERE/'candidate'/name).read_bytes()
        target=root/name
        if not target.is_file():
            print('MISSING',target);sys.exit(3)
        current=target.read_bytes()
        if sha(current)==sha(cand):status='ALREADY_CANDIDATE'
        elif sha(current)==sha(base):status='EXACT_BASE';change.append((target,cand,current))
        else:
            print('CONFLICT',name,'current_sha',sha(current),'expected_base_sha',sha(base))
            print('Do NOT overwrite; manual targeted source integration required.')
            sys.exit(4)
        outcomes.append((name,status))
    for name,status in outcomes:print('OK',status,name)
    if a.apply:
        for target,candidate,original in change:
            output=candidate.replace(b'\n',b'\r\n') if b'\r\n' in original else candidate
            target.write_bytes(output)
            print('APPLIED',target)
        print('APPLIED_COMPLETELY',len(change),'FILES')
    else:print('CHECK_ONLY',len(change),'FILE_CHANGES_PENDING')
if __name__=='__main__':main()
