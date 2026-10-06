(() => {
  'use strict';
  const DATA_URL = 'https://raw.githubusercontent.com/wangxizhenwang2-del/darwin-s-farm/main/web-studio/data/bioweb.json';
  const STORAGE_KEY = 'darwin-atlas-draft-v1';
  const DIRTY_KEY = 'darwin-atlas-dirty-v1';
  const $ = id => document.getElementById(id);
  const clamp = (n, lo, hi) => Math.max(lo, Math.min(hi, n));
  const uuid = () => crypto.randomUUID ? crypto.randomUUID() : `sp-${Date.now()}-${Math.random().toString(36).slice(2)}`;
  const DEFAULT_TRAITS = {trophicLevel:0,baseMovementAbility:50,baseHabitatNiche:50,baseFitTemperature:50,baseFitHumidity:50,baseSize:10,baseFertility:50,baseEcologicalNiche:'Land'};
  let data = {schemaVersion:1,species:[],evolutionLinks:[],foodLinks:[]};
  let view = 'evolution', selected = null, connectFrom = null, connecting = false;
  let transform = {x:100,y:60,scale:1};
  let drag = null, saveTimer = null, toastTimer = null, remoteLoaded = false;
  const validId = id => typeof id === 'string' && id.length > 0 && id.length < 100;
  const range = (value,min,max,fallback) => Number.isFinite(Number(value)) ? clamp(Math.round(Number(value)),min,max) : fallback;
  const esc = s => String(s ?? '').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const safeImage = url => { try { const u = new URL(url, location.href); return ['https:','http:','data:'].includes(u.protocol) && (u.protocol !== 'data:' || /^data:image\/(png|jpeg|webp|gif);base64,/i.test(url)) ? url : ''; } catch { return ''; } };
  const imageMarkup = s => { const url = safeImage(s.image); return url ? `<img src="${esc(url)}" alt="">` : '✳'; };
  function clean(input) {
    if (!input || input.schemaVersion !== 1 || !Array.isArray(input.species)) throw new Error('文件不是 Darwin Atlas v1 数据');
    const ids = new Set();
    const species = input.species.map((raw,i) => {
      if (!validId(raw.id) || ids.has(raw.id)) throw new Error(`第 ${i+1} 个物种的 ID 无效或重复`);
      ids.add(raw.id);
      const s = {id:raw.id,name:String(raw.name || '未命名物种').slice(0,40),scientificName:String(raw.scientificName || '').slice(0,70),image:safeImage(raw.image || ''),x:range(raw.x,0,1800,200+i*190),y:range(raw.y,0,1200,200),...DEFAULT_TRAITS};
      for (const key of Object.keys(DEFAULT_TRAITS)) {
        if (key === 'baseEcologicalNiche') s[key] = ['Land','Water','Air'].includes(raw[key]) ? raw[key] : 'Land';
        else s[key] = range(raw[key], key === 'baseSize' ? 1 : 0, key === 'trophicLevel' ? 2 : 100,DEFAULT_TRAITS[key]);
      }
      return s;
    });
    const links = key => (Array.isArray(input[key]) ? input[key] : []).filter(l => ids.has(l.from) && ids.has(l.to) && l.from !== l.to).map(l => ({from:l.from,to:l.to})).filter((l,i,a)=>a.findIndex(t=>t.from===l.from&&t.to===l.to)===i);
    return {schemaVersion:1,species,evolutionLinks:links('evolutionLinks'),foodLinks:links('foodLinks')};
  }
  function save() {
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(data)); $('save-state').textContent='● 本地已保存'; }
    catch { $('save-state').textContent='● 本地存储空间不足，请导出 JSON'; }
  }
  function changed() { localStorage.setItem(DIRTY_KEY,'1'); $('save-state').textContent='● 正在保存…'; clearTimeout(saveTimer); saveTimer=setTimeout(save,250); renderCounts(); }
  function toast(message) { $('toast').textContent=message; $('toast').classList.add('show'); clearTimeout(toastTimer); toastTimer=setTimeout(()=>$('toast').classList.remove('show'),3100); }
  function currentLinks() { return data[view === 'evolution' ? 'evolutionLinks' : 'foodLinks']; }
  function setView(next) {
    view=next; connecting=false; connectFrom=null; $('connect-button').classList.remove('active');
    document.querySelectorAll('[data-view]').forEach(el=>el.classList.toggle('active',el.dataset.view===view));
    const evo=view==='evolution'; $('breadcrumb').textContent=$('page-title').textContent=evo?'进化网络':'食物网';
    $('page-description').textContent=evo?'绘制物种的演化路径，让每一次分化都有迹可循。':'梳理从猎物到捕食者的能量流动关系。';
    $('relation-label').textContent=evo?'进化关系':'食物关系'; $('mode-tip').textContent=evo?'从一个物种指向它的后代，连接清晰的进化脉络。':'箭头从被吃的物种指向捕食它的物种。';
    $('canvas-hint').textContent=evo?'拖动物种卡片调整位置 · 点击「连接物种」从祖先指向后代':'拖动物种卡片调整位置 · 点击「连接物种」从猎物指向捕食者';
    render();
  }
  function renderCounts() {
    $('species-stat').textContent=data.species.length;
    $('evolution-count').textContent=data.evolutionLinks.length;
    $('food-count').textContent=data.foodLinks.length;
    $('relation-stat').textContent=currentLinks().length;
    $('level-stat').textContent=new Set(data.species.map(s=>s.trophicLevel)).size;
  }
  function render() {
    renderCounts();
    $('nodes').innerHTML=data.species.map(s=>`<div class="node${selected===s.id?' selected':''}${connectFrom===s.id?' connect-source':''}" data-id="${esc(s.id)}" style="left:${s.x}px;top:${s.y}px" role="button" tabindex="0" aria-label="物种 ${esc(s.name)}"><div class="node-image">${imageMarkup(s)}</div><strong>${esc(s.name)}</strong><span class="latin">${esc(s.scientificName || 'Species')}</span><div class="traits"><span class="trait">☀ ${s.baseFitTemperature}</span><span class="trait level">层级 ${s.trophicLevel}</span></div></div>`).join('');
    const byId=Object.fromEntries(data.species.map(s=>[s.id,s]));
    $('edge-lines').innerHTML=currentLinks().map((l,i)=>{
      const a=byId[l.from],b=byId[l.to]; if(!a||!b)return '';
      const ax=a.x+82,ay=a.y+82,bx=b.x+82,by=b.y+82,dx=bx-ax,dy=by-ay,len=Math.hypot(dx,dy)||1,ux=dx/len,uy=dy/len;
      const startX=ax+ux*78,startY=ay+uy*78,endX=bx-ux*86,endY=by-uy*86;
      return `<path class="edge ${view}" data-index="${i}" d="M ${startX} ${startY} L ${endX} ${endY}" marker-end="url(#arrow-${view})"/>`;
    }).join('');
    $('world').style.transform=`translate(${transform.x}px,${transform.y}px) scale(${transform.scale})`;
    $('zoom-label').textContent=$('zoom-badge').textContent=`${Math.round(transform.scale*100)}%`;
    renderInspector();
  }
  function renderInspector() {
    const s=data.species.find(item=>item.id===selected);
    document.querySelector('.app').classList.toggle('show-inspector',!!s);
    if(!s){$('inspector-title').textContent='选择一个物种';$('inspector-body').className='inspector-empty';$('inspector-body').textContent='点击画布中的节点，查看并编辑物种属性。';return;}
    $('inspector-title').textContent=s.name;
    const input=(key,label,min=0,max=100)=>`<label class="field"><span>${label}</span><input data-field="${key}" type="number" min="${min}" max="${max}" value="${s[key]}"></label>`;
    $('inspector-body').className='inspector-content';
    $('inspector-body').innerHTML=`<div class="inspector-image">${imageMarkup(s)}</div><label class="upload-label">＋ 上传物种图片<input id="image-upload" type="file" accept="image/png,image/jpeg,image/webp,image/gif" hidden></label><label class="field"><span>物种名称</span><input data-field="name" maxlength="40" value="${esc(s.name)}"></label><label class="field"><span>学名 / 注释</span><input data-field="scientificName" maxlength="70" value="${esc(s.scientificName)}"></label><label class="field"><span>图片地址</span><input data-field="image" value="${esc(s.image)}" placeholder="https://..."></label><div class="inspector-divider"></div><div class="field-grid">${input('baseFitTemperature','适宜温度')}${input('baseFitHumidity','适宜湿度')}${input('baseMovementAbility','移动能力')}${input('baseHabitatNiche','栖息倾向')}${input('baseSize','体型',1)}${input('baseFertility','繁殖能力')}</div><label class="field"><span>营养级</span><select data-field="trophicLevel"><option value="0" ${s.trophicLevel===0?'selected':''}>0 · 食草</option><option value="1" ${s.trophicLevel===1?'selected':''}>1 · 一级捕食者</option><option value="2" ${s.trophicLevel===2?'selected':''}>2 · 二级捕食者</option></select></label><label class="field"><span>基础生态位</span><select data-field="baseEcologicalNiche"><option value="Land" ${s.baseEcologicalNiche==='Land'?'selected':''}>陆地</option><option value="Water" ${s.baseEcologicalNiche==='Water'?'selected':''}>水域</option><option value="Air" ${s.baseEcologicalNiche==='Air'?'selected':''}>空中</option></select></label><p class="field-note">数值范围与 Unity SpeciesData 保持一致；节点位置和图片仅用于网站展示。</p><button class="danger" id="delete-species">删除物种及其所有连线</button>`;
  }
  function toWorld(clientX,clientY){const rect=$('viewport').getBoundingClientRect();return {x:(clientX-rect.left-transform.x)/transform.scale,y:(clientY-rect.top-transform.y)/transform.scale};}
  function onNode(id){
    if(connecting){
      if(!connectFrom){connectFrom=id;toast(view==='evolution'?'请选择后代物种':'请选择捕食者物种');render();return;}
      if(connectFrom===id){toast('请选择另一个物种');return;}
      const links=currentLinks();if(links.some(l=>l.from===connectFrom&&l.to===id)){toast('这条关系已存在');return;}
      if(view==='food') {const a=data.species.find(s=>s.id===connectFrom),b=data.species.find(s=>s.id===id);if(a.trophicLevel>=b.trophicLevel) toast('提示：当前模拟器只支持 0 → 1 → 2 的营养级关系');}
      links.push({from:connectFrom,to:id});connectFrom=null;connecting=false;$('connect-button').classList.remove('active');changed();render();toast('关系已建立');return;
    }
    selected=id;render();
  }
  $('nodes').addEventListener('pointerdown',e=>{
    const el=e.target.closest('.node');if(!el)return;
    drag={id:el.dataset.id,origin:toWorld(e.clientX,e.clientY),x:Number(el.style.left.replace('px','')),y:Number(el.style.top.replace('px','')),moved:false};
    el.setPointerCapture(e.pointerId);e.preventDefault();
  });
  $('nodes').addEventListener('pointermove',e=>{if(!drag)return;const p=toWorld(e.clientX,e.clientY);const dx=p.x-drag.origin.x,dy=p.y-drag.origin.y;if(Math.abs(dx)+Math.abs(dy)>3)drag.moved=true;if(!drag.moved)return;const s=data.species.find(x=>x.id===drag.id);if(!s)return;s.x=clamp(Math.round(drag.x+dx),0,1800);s.y=clamp(Math.round(drag.y+dy),0,1200);const el=$('nodes').querySelector(`[data-id="${CSS.escape(s.id)}"]`);if(el){el.style.left=`${s.x}px`;el.style.top=`${s.y}px`;}renderEdgesOnly();});
  $('nodes').addEventListener('pointerup',e=>{if(!drag)return;const {id,moved}=drag;drag=null;if(moved)changed();else onNode(id);});
  $('nodes').addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){const el=e.target.closest('.node');if(el){e.preventDefault();onNode(el.dataset.id);}}});
  function renderEdgesOnly(){
    const byId=Object.fromEntries(data.species.map(s=>[s.id,s]));
    $('edge-lines').innerHTML=currentLinks().map((l,i)=>{const a=byId[l.from],b=byId[l.to];if(!a||!b)return '';const ax=a.x+82,ay=a.y+82,bx=b.x+82,by=b.y+82,dx=bx-ax,dy=by-ay,len=Math.hypot(dx,dy)||1,ux=dx/len,uy=dy/len;return `<path class="edge ${view}" data-index="${i}" d="M ${ax+ux*78} ${ay+uy*78} L ${bx-ux*86} ${by-uy*86}" marker-end="url(#arrow-${view})"/>`;}).join('');
  }
  $('edge-lines').addEventListener('click',e=>{const path=e.target.closest('.edge');if(!path)return;if(confirm('删除这条关系？')){currentLinks().splice(Number(path.dataset.index),1);changed();render();}});
  $('inspector-body').addEventListener('change',e=>{
    const s=data.species.find(x=>x.id===selected);if(!s)return;
    if(e.target.id==='image-upload'){
      const file=e.target.files?.[0];if(!file)return;if(file.size>900000){toast('图片请压缩到 900 KB 以下');return;}const reader=new FileReader();reader.onload=()=>{s.image=safeImage(reader.result);changed();render();};reader.readAsDataURL(file);return;
    }
    const key=e.target.dataset.field;if(!key)return;
    if(['name','scientificName'].includes(key))s[key]=String(e.target.value).trim().slice(0,key==='name'?40:70)|| (key==='name'?'未命名物种':'');
    else if(key==='image')s.image=safeImage(e.target.value.trim());
    else if(key==='baseEcologicalNiche')s[key]=['Land','Water','Air'].includes(e.target.value)?e.target.value:'Land';
    else s[key]=range(e.target.value,key==='baseSize'?1:0,key==='trophicLevel'?2:100,DEFAULT_TRAITS[key]);
    changed();render();
  });
  $('inspector-body').addEventListener('click',e=>{if(e.target.id!=='delete-species')return;const s=data.species.find(x=>x.id===selected);if(!s||!confirm(`删除「${s.name}」及其所有关系？`))return;data.species=data.species.filter(x=>x.id!==selected);data.evolutionLinks=data.evolutionLinks.filter(l=>l.from!==selected&&l.to!==selected);data.foodLinks=data.foodLinks.filter(l=>l.from!==selected&&l.to!==selected);selected=null;changed();render();toast('物种已删除');});
  $('close-inspector').onclick=()=>{selected=null;render();};
  document.querySelectorAll('[data-view]').forEach(el=>el.addEventListener('click',()=>setView(el.dataset.view)));
  $('connect-button').onclick=()=>{connecting=!connecting;connectFrom=null;$('connect-button').classList.toggle('active',connecting);toast(connecting?(view==='evolution'?'先点击祖先，再点击后代':'先点击猎物，再点击捕食者'):'已退出连线模式');render();};
  $('add-species').onclick=()=>$('species-dialog').showModal();
  $('species-form').addEventListener('submit',e=>{if(e.submitter?.value!=='save')return;e.preventDefault();const form=new FormData(e.currentTarget),name=String(form.get('name')||'').trim();if(!name)return;const p=toWorld($('viewport').getBoundingClientRect().left+$('viewport').clientWidth/2,$('viewport').getBoundingClientRect().top+$('viewport').clientHeight/2);const s={id:uuid(),name,scientificName:'',image:safeImage(String(form.get('image')||'')),x:clamp(Math.round(p.x-82),0,1800),y:clamp(Math.round(p.y-82),0,1200),...DEFAULT_TRAITS};data.species.push(s);selected=s.id;e.currentTarget.reset();$('species-dialog').close();changed();render();toast('已创建物种');});
  $('fit-button').onclick=()=>{if(!data.species.length){transform={x:80,y:50,scale:1};render();return;}const minX=Math.min(...data.species.map(s=>s.x)),maxX=Math.max(...data.species.map(s=>s.x+164)),minY=Math.min(...data.species.map(s=>s.y)),maxY=Math.max(...data.species.map(s=>s.y+170));const vp=$('viewport');const scale=clamp(Math.min((vp.clientWidth-90)/(maxX-minX),(vp.clientHeight-90)/(maxY-minY)),.4,1.4);transform={scale,x:(vp.clientWidth-(maxX-minX)*scale)/2-minX*scale,y:(vp.clientHeight-(maxY-minY)*scale)/2-minY*scale};render();};
  function zoom(factor){const vp=$('viewport'),cx=vp.clientWidth/2,cy=vp.clientHeight/2,next=clamp(transform.scale*factor,.35,2);transform.x=cx-(cx-transform.x)*next/transform.scale;transform.y=cy-(cy-transform.y)*next/transform.scale;transform.scale=next;render();}
  $('zoom-in').onclick=()=>zoom(1.2);$('zoom-out').onclick=()=>zoom(1/1.2);
  $('viewport').addEventListener('wheel',e=>{if(!e.ctrlKey)return;e.preventDefault();zoom(e.deltaY<0?1.1:1/1.1);},{passive:false});
  $('viewport').addEventListener('pointerdown',e=>{if(e.target.closest('.node')||e.target.closest('.edge'))return;drag={pan:true,x:e.clientX,y:e.clientY,tx:transform.x,ty:transform.y};$('viewport').setPointerCapture(e.pointerId);});
  $('viewport').addEventListener('pointermove',e=>{if(!drag?.pan)return;transform.x=drag.tx+e.clientX-drag.x;transform.y=drag.ty+e.clientY-drag.y;render();});
  $('viewport').addEventListener('pointerup',()=>{if(drag?.pan)drag=null;});
  $('export-button').onclick=()=>{const json=JSON.stringify(data,null,2)+'\n',url=URL.createObjectURL(new Blob([json],{type:'application/json'}));const a=document.createElement('a');a.href=url;a.download='bioweb.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);toast('已导出 bioweb.json');};
  $('import-button').onclick=()=>$('import-file').click();
  $('import-file').onchange=async e=>{const file=e.target.files?.[0];if(!file)return;try{const next=clean(JSON.parse(await file.text()));if(!confirm(`导入 ${next.species.length} 个物种？将替换当前浏览器中的工作副本。`))return;data=next;selected=null;changed();render();$('fit-button').click();toast('JSON 已导入');}catch(err){toast(`导入失败：${err.message}`);}finally{e.target.value='';}};
  async function loadRemote(force=false){
    const hasDraft=!!localStorage.getItem(STORAGE_KEY),dirty=localStorage.getItem(DIRTY_KEY)==='1';$('source-title').textContent='正在读取 GitHub…';
    try{const res=await fetch(`${DATA_URL}?t=${Date.now()}`,{cache:'no-store'});if(!res.ok)throw new Error(`HTTP ${res.status}`);const remote=clean(await res.json());remoteLoaded=true;
      if(force&&dirty&&!confirm('用 GitHub 数据替换当前浏览器的工作副本？未导出的编辑会丢失。')){$('source-title').textContent='本地工作副本';return;}
      if(!dirty||force||!hasDraft){data=remote;selected=null;localStorage.removeItem(DIRTY_KEY);save();render();$('fit-button').click();$('source-title').textContent='已读取 GitHub 数据';$('source-subtitle').textContent='编辑自动保存在本浏览器';if(force)toast('GitHub 数据已更新');}
      else {$('source-title').textContent='本地工作副本';$('source-subtitle').textContent='GitHub 有数据；可手动重新读取';}
    }catch{
      if(!hasDraft){try{const fallback=await fetch('./bioweb.json');if(fallback.ok){data=clean(await fallback.json());save();render();$('fit-button').click();$('source-title').textContent='内置示例数据';$('source-subtitle').textContent='GitHub 合并后将自动切换为团队数据';return;}}catch{}}
      $('source-title').textContent='离线工作副本';$('source-subtitle').textContent='无法读取 GitHub，仍可编辑和导出';if(force)toast('GitHub 读取失败，请检查网络或文件路径');
    }
  }
  $('refresh-remote').onclick=()=>loadRemote(true);
  $('help-button').onclick=()=>$('help-dialog').showModal();$('close-help').onclick=()=>$('help-dialog').close();
  try{const draft=localStorage.getItem(STORAGE_KEY);if(draft)data=clean(JSON.parse(draft));}catch{localStorage.removeItem(STORAGE_KEY);}
  render();requestAnimationFrame(()=>$('fit-button').click());loadRemote();
})();
