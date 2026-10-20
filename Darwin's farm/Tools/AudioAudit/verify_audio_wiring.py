from pathlib import Path

root = Path(__file__).resolve().parents[2]
banks = root / 'audio/Wwiseproject/DarwinsFarm_Audio/GeneratedSoundBanks/Windows'
for bank, event in [('SB_Animals', 'Play_Rc_ji'), ('SB_Animals', 'Play_Rc_lu'), ('SB_Animals', 'Play_Rc_caiji'), ('SB_UI', 'Play_UI_Handbook_Open')]:
    assert event in (banks / f'{bank}.txt').read_text(encoding='utf-8-sig'), (bank, event)
for asset, event in [
    ('Assets/BiowebV46/Species/鸡-7f113739.asset', 'Play_Rc_ji'),
    ('Assets/BiowebV46/Species/鹿-fd1c574b.asset', 'Play_Rc_lu'),
    ('Assets/BiowebV46/Species/彩鸡-b59a4a82.asset', 'Play_Rc_caiji'),
]:
    content = (root / asset).read_text(encoding='utf-8-sig')
    assert f'  animalPlayEvent: {event}\n' in content, asset
    assert '  animalSoundBank: SB_Animals\n' in content, asset
print('Wwise event and species asset contract OK')
