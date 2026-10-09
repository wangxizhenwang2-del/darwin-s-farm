(() => {
  'use strict';
  const DATA_URL = 'https://raw.githubusercontent.com/wangxizhenwang2-del/darwin-s-farm/main/web-studio/data/bioweb.json';
  const STORAGE_KEY = 'darwin-atlas-draft-v1';
  const DIRTY_KEY = 'darwin-atlas-dirty-v1';
  const MAX_UPLOAD_BYTES = 10 * 1024 * 1024;
  const MAX_STORED_IMAGE_BYTES = 256 * 1024;
  const $ = id => document.getElementById(id);
  const clamp = (n, lo, hi) => Math.max(lo, Math.min(hi, n));
  const uuid = () => crypto.randomUUID ? crypto.randomUUID() : `sp-${Date.now()}-${Math.random().toString(36).slice(2)}`;
  const DEFAULT_TRAITS = {trophicLevel:0,baseMovementAbility:50,baseHabitatNiche:50,baseFitTemperature:50,baseFitHumidity:50,baseSize:10,baseFertility:50,baseEcologicalNiche:'Land'};
  let data = {schemaVersion:1,species:[],evolutionLinks:[],foodLinks:[]};
  let view = 'evolution', lastGraphView = 'evolution', selected = null, connectFrom = null, connecting = false;
  let transform = {x:100,y:60,scale:1};
  let drag = null, saveTimer = null, toastTimer = null, remoteLoaded = false;
  const validId = id => typeof id === 'string' && id.length > 0 && id.length < 100;
  const range = (value,min,max,fallback) => Number.isFinite(Number(value)) ? clamp(Math.round(Number(value)),min,max) : fallback;
  const esc = s => String(s ?? '').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const safeImage = url => { try { const u = new URL(url, location.href); return ['https:','http:','data:'].includes(u.protocol) && (u.protocol !== 'data:' || /^data:image\/(png|jpeg|webp|gif);base64,/i.test(url)) ? url : ''; } catch { return ''; } };
  const imageMarkup = s => { const url = safeImage(s.image); return url ? `<img src="${esc(url)}" alt="">` : '✳'; };
  const dataUrlBytes = url => Math.ceil((url.length - url.indexOf(',') - 1) * 3 / 4);
  function readFileAsDataURL(file) {
    return new Promise((resolve,reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result));
      reader.onerror = () => reject(new Error('无法读取图片文件'));
      reader.readAsDataURL(file);
    });
  }
  function loadImage(file) {
    return new Promise((resolve,reject) => {
      const url=URL.createObjectURL(file),image=new Image();
      image.onload=()=>{URL.revokeObjectURL(url);resolve(image);};
      image.onerror=()=>{URL.revokeObjectURL(url);reject(new Error('图片格式无法解码'));};
      image.src=url;
    });
  }
  async function prepareImage(file) {
    if (!['image/png','image/jpeg','image/webp','image/gif'].includes(file.type)) throw new Error('请选择 PNG、JPEG、WebP 或 GIF 图片');
    if (file.size > MAX_UPLOAD_BYTES) throw new Error('原图请控制在 10 MB 以下');
    if (file.size <= MAX_STORED_IMAGE_BYTES) return safeImage(await readFileAsDataURL(file));
    const image=await loadImage(file),canvas=document.createElement('canvas'),context=canvas.getContext('2d');
    if (!context || !image.naturalWidth || !image.naturalHeight) throw new Error('无法处理图片');
    for (const edge of [768,640,512,384]) {
      const ratio=Math.min(1,edge/Math.max(image.naturalWidth,image.naturalHeight));
      canvas.width=Math.max(1,Math.round(image.naturalWidth*ratio));
      canvas.height=Math.max(1,Math.round(image.naturalHeight*ratio));
      context.clearRect(0,0,canvas.width,canvas.height);
      context.drawImage(image,0,0,canvas.width,canvas.height);
      for (const quality of [.82,.7,.58,.46]) {
        const url=canvas.toDataURL('image/webp',quality);
        if (url.startsWith('data:image/webp;') && dataUrlBytes(url)<=MAX_STORED_IMAGE_BYTES) return url;
      }
    }
    throw new Error('图片压缩后仍过大，请换一张图片');
  }
  function clean(input) {
    if (!input || input.schemaVersion !== 1 || !Array.isArray(input.species)) throw new Error('文件不是 Darwin Atlas v1 数据');
    const ids = new Set();
    const species = input.species.map((raw,i) => {
      if (!validId(raw.id) || ids.has(raw.id)) throw new Error(`第 ${i+1} 个物种的 ID 无效或重复`);
      ids.add(raw.id);
      const x=range(raw.x,0,1800,200+i*190),y=range(raw.y,0,1200,200);
      const s = {id:raw.id,name:String(raw.name || '未命名物种').slice(0,40),description:String(raw.description ?? raw.scientificName ?? '').slice(0,240),image:safeImage(raw.image || ''),x,y,foodX:range(raw.foodX,0,1800,x),foodY:range(raw.foodY,0,1200,y),...DEFAULT_TRAITS};
      for (const key of Object.keys(DEFAULT_TRAITS)) {
        if (key === 'baseEcologicalNiche') s[key] = ['Land','Water','Air'].includes(raw[key]) ? raw[key] : 'Land';
        else s[key] = range(raw[key], key === 'baseSize' ? 1 : 0, key === 'trophicLevel' ? 2 : 100,DEFAULT_TRAITS[key]);
      }
      return s;
    });
    const links = key => (Array.isArray(input[key]) ? input[key] : []).filter(l => ids.has(l.from) && ids.has(l.to) && l.from !== l.to).map(l => key === 'evolutionLinks' && l.from > l.to ? {from:l.to,to:l.from} : {from:l.from,to:l.to}).filter((l,i,a)=>a.findIndex(t=>t.from===l.from&&t.to===l.to)===i);
    return {schemaVersion:1,species,evolutionLinks:links('evolutionLinks'),foodLinks:links('foodLinks')};
  }
  function save() {
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(data)); $('save-state').textContent='● 本地已保存'; }
    catch { $('save-state').textContent='● 本地存储空间不足，请导出 JSON'; }
  }
  function changed() { localStorage.setItem(DIRTY_KEY,'1'); $('save-state').textContent='● 正在保存…'; clearTimeout(saveTimer); saveTimer=setTimeout(save,250); renderCounts(); }
  function toast(message) { $('toast').textContent=message; $('toast').classList.add('show'); clearTimeout(toastTimer); toastTimer=setTimeout(()=>$('toast').classList.remove('show'),3100); }
  function graphView() { return view==='table' ? lastGraphView : view; }
  function position(s) { return graphView()==='food' ? {x:s.foodX,y:s.foodY} : {x:s.x,y:s.y}; }
  function currentLinks() { return data[graphView() === 'evolution' ? 'evolutionLinks' : 'foodLinks']; }
  function setView(next) {
    if(!['evolution','food','table'].includes(next))return;
    if(next!=='table')lastGraphView=next;
    view=next; connecting=false; connectFrom=null; $('connect-button').classList.remove('active');
    document.querySelectorAll('[data-view]').forEach(el=>el.classList.toggle('active',el.dataset.view===view));
    const table=view==='table';$('table-shell').hidden=!table;document.querySelector('.canvas-shell').hidden=table;$('fit-button').hidden=table;$('export-image').hidden=table;document.querySelector('.stats').hidden=table;
    if(table){$('breadcrumb').textContent=$('page-title').textContent='物种数据表';$('page-description').textContent='并排查看与调整所有物种的初始参数。';render();return;}
    const evo=view==='evolution'; $('breadcrumb').textContent=$('page-title').textContent=evo?'进化网络':'食物网';
    $('page-description').textContent=evo?'用实线连接可相互演化的物种，也包含退化路径。':'梳理从猎物到捕食者的能量流动关系。';
    $('relation-label').textContent=evo?'进化关系':'食物关系'; $('mode-tip').textContent=evo?'无箭头实线表示双向演化；食物网仍用单向箭头。':'箭头从被吃的物种指向捕食它的物种。';
    $('canvas-hint').textContent=evo?'拖动物种卡片调整位置 · 点击「连接物种」建立双向关系':'拖动物种卡片调整位置 · 点击「连接物种」从猎物指向捕食者';
    render();
  }
  function renderCounts() {
    $('species-stat').textContent=data.species.length;
    $('evolution-count').textContent=data.evolutionLinks.length;
    $('food-count').textContent=data.foodLinks.length;
    $('relation-stat').textContent=currentLinks().length;
    $('level-stat').textContent=new Set(data.species.map(s=>s.trophicLevel)).size;
  }
  function edgeMarkup() {
    const byId=Object.fromEntries(data.species.map(s=>[s.id,s]));
    return currentLinks().map((l,i)=>{
      const a=byId[l.from],b=byId[l.to]; if(!a||!b)return '';
      const pa=position(a),pb=position(b),ax=pa.x+82,ay=pa.y+82,bx=pb.x+82,by=pb.y+82,dx=bx-ax,dy=by-ay,len=Math.hypot(dx,dy)||1,ux=dx/len,uy=dy/len;
      const endOffset=graphView()==='food'?86:78;
      const marker=graphView()==='food'?' marker-end="url(#arrow-food)"':'';
      return `<path class="edge ${graphView()}" data-index="${i}" d="M ${ax+ux*78} ${ay+uy*78} L ${bx-ux*endOffset} ${by-uy*endOffset}"${marker}/>`;
    }).join('');
  }
  function render() {
    renderCounts();
    $('nodes').innerHTML=data.species.map(s=>{const p=position(s);return `<div class="node${selected===s.id?' selected':''}${connectFrom===s.id?' connect-source':''}" data-id="${esc(s.id)}" style="left:${p.x}px;top:${p.y}px" role="button" tabindex="0" aria-label="物种 ${esc(s.name)}"><div class="node-image">${imageMarkup(s)}</div><strong>${esc(s.name)}</strong><span class="node-description" title="${esc(s.description)}">${esc(s.description || '暂无简介')}</span><div class="traits"><span class="trait">☀ ${s.baseFitTemperature}</span><span class="trait level">层级 ${s.trophicLevel}</span></div></div>`;}).join('');
    $('edge-lines').innerHTML=edgeMarkup();
    $('world').style.transform=`translate(${transform.x}px,${transform.y}px) scale(${transform.scale})`;
    $('zoom-label').textContent=$('zoom-badge').textContent=`${Math.round(transform.scale*100)}%`;
    if(view==='table'){document.querySelector('.app').classList.remove('show-inspector');renderTable();}else renderInspector();
  }
  const TABLE_NUMBERS=[['baseFitTemperature',0,100],['baseFitHumidity',0,100],['baseMovementAbility',0,100],['baseHabitatNiche',0,100],['baseSize',1,100],['baseFertility',0,100]];
  function renderTable() {
    const query=$('table-search').value.trim().toLocaleLowerCase();
    const rows=data.species.filter(s=>`${s.name} ${s.description}`.toLocaleLowerCase().includes(query));
    const numberCell=(s,[key,min,max])=>`<td><input type="number" data-id="${esc(s.id)}" data-field="${key}" min="${min}" max="${max}" value="${s[key]}" aria-label="${esc(s.name)} ${key}"></td>`;
    $('table-body').innerHTML=rows.map(s=>`<tr><td class="species-cell"><div class="table-species"><span class="table-avatar">${imageMarkup(s)}</span><strong>${esc(s.name)}</strong></div></td><td><input data-id="${esc(s.id)}" data-field="description" maxlength="240" value="${esc(s.description)}" aria-label="${esc(s.name)} 物种简介" title="${esc(s.description)}"></td><td><select data-id="${esc(s.id)}" data-field="trophicLevel" aria-label="${esc(s.name)} 营养级"><option value="0" ${s.trophicLevel===0?'selected':''}>0 · 食草</option><option value="1" ${s.trophicLevel===1?'selected':''}>1 · 一级捕食</option><option value="2" ${s.trophicLevel===2?'selected':''}>2 · 二级捕食</option></select></td>${TABLE_NUMBERS.map(col=>numberCell(s,col)).join('')}<td><select data-id="${esc(s.id)}" data-field="baseEcologicalNiche" aria-label="${esc(s.name)} 基础生态位"><option value="Land" ${s.baseEcologicalNiche==='Land'?'selected':''}>陆地</option><option value="Water" ${s.baseEcologicalNiche==='Water'?'selected':''}>水域</option><option value="Air" ${s.baseEcologicalNiche==='Air'?'selected':''}>空中</option></select></td><td><button class="table-detail" data-detail="${esc(s.id)}">编辑详情 ↗</button></td></tr>`).join('') || '<tr><td colspan="11" class="table-empty">没有匹配的物种</td></tr>';
    $('table-foot').textContent=`显示 ${rows.length} / ${data.species.length} 个物种 · 修改后自动保存在本浏览器，请导出 JSON 与组员共享。`;
  }
  $('table-search').addEventListener('input',renderTable);
  $('table-body').addEventListener('change',e=>{
    const input=e.target, s=data.species.find(item=>item.id===input.dataset.id),key=input.dataset.field;if(!s||!key)return;
    if(key==='description')s[key]=String(input.value).trim().slice(0,240);
    else if(key==='baseEcologicalNiche')s[key]=['Land','Water','Air'].includes(input.value)?input.value:'Land';
    else s[key]=range(input.value,key==='baseSize'?1:0,key==='trophicLevel'?2:100,DEFAULT_TRAITS[key]);
    changed();render();
  });
  $('table-body').addEventListener('click',e=>{const id=e.target.closest('[data-detail]')?.dataset.detail;if(!id)return;selected=id;setView(lastGraphView);});
  function renderInspector() {
    const s=data.species.find(item=>item.id===selected);
    document.querySelector('.app').classList.toggle('show-inspector',!!s);
    if(!s){$('inspector-title').textContent='选择一个物种';$('inspector-body').className='inspector-empty';$('inspector-body').textContent='点击画布中的节点，查看并编辑物种属性。';return;}
    $('inspector-title').textContent=s.name;
    const input=(key,label,help,min=0,max=100)=>`<label class="field trait-field"><span>${label} <em>${min}–${max}</em></span><input data-field="${key}" type="number" min="${min}" max="${max}" value="${s[key]}"><small class="field-help">${help}</small></label>`;
    $('inspector-body').className='inspector-content';
    $('inspector-body').innerHTML=`
      <div class="inspector-image">${imageMarkup(s)}</div>
      <label class="upload-label">＋ 上传物种图片<input id="image-upload" type="file" accept="image/png,image/jpeg,image/webp,image/gif" hidden></label>
      <small class="upload-note">支持最大 10 MB 原图，上传后自动压缩。</small>
      <label class="field"><span>物种名称</span><input data-field="name" maxlength="40" value="${esc(s.name)}"></label>
      <label class="field"><span>物种简介</span><textarea data-field="description" maxlength="240" rows="4" placeholder="描述栖息地、食性或生态角色">${esc(s.description)}</textarea><small class="field-help">最多 240 字；会显示在节点和导出图片中。</small></label>
      <label class="field"><span>图片地址</span><input data-field="image" value="${esc(s.image)}" placeholder="https://..."></label>
      <div class="inspector-divider"></div>
      <div class="guide-banner"><b>如何填写初始属性</b><p>数值是游戏内的 0–100 刻度，不是现实单位。以下区间只是第一轮试填起点，请结合目标地块和模拟结果调整。</p></div>
      <div class="field-grid">
        ${input('baseFitTemperature','适宜温度','与地块温度越接近，适应度越高。0 极寒、100 极热；常温地块可从 50 左右试填。')}
        ${input('baseFitHumidity','适宜湿度','陆地上与地块湿度越接近，适应度越高。0 干燥、100 潮湿；可从 50 左右试填。水域当前只计算温度。')}
        ${input('baseMovementAbility','移动能力','提高觅食、捕获机会，也增加每日能耗。可从 30–60 试填；敏捷物种再向上调。')}
        ${input('baseHabitatNiche','栖息倾向','旧数值字段：0 偏水域、100 偏空中。当前模拟暂未使用它，建议保持 50。')}
        ${input('baseSize','体型','越大通常能量需求越高，也影响猎物能量和捕食。1–7 属小型赛道；先按相对大小试填。',1)}
        ${input('baseFertility','繁殖能力','越高基础出生率越高；超过 80 会增加能耗。可从 40–80 试填，再看种群变化。')}
      </div>
      <label class="field trait-field"><span>营养级</span><select data-field="trophicLevel"><option value="0" ${s.trophicLevel===0?'selected':''}>0 · 食草</option><option value="1" ${s.trophicLevel===1?'selected':''}>1 · 一级捕食者</option><option value="2" ${s.trophicLevel===2?'selected':''}>2 · 二级捕食者</option></select><small class="field-help">0 吃植物，1 吃 0 级，2 吃 1 级。当前模拟只按等级捕食，尚不读取食物网箭头。</small></label>
      <label class="field trait-field"><span>基础生态位</span><select data-field="baseEcologicalNiche"><option value="Land" ${s.baseEcologicalNiche==='Land'?'selected':''}>陆地</option><option value="Water" ${s.baseEcologicalNiche==='Water'?'selected':''}>水域</option><option value="Air" ${s.baseEcologicalNiche==='Air'?'selected':''}>空中</option></select><small class="field-help">选择物种初始生活区域；它与上面的保留字段「栖息倾向」不同。</small></label>
      <p class="field-note">节点位置和图片用于网站展示。建议值是试填参考，不保证在所有地图都最优。</p>
      <button class="danger" id="delete-species">删除物种及其所有连线</button>`;
  }
  function toWorld(clientX,clientY){const rect=$('viewport').getBoundingClientRect();return {x:(clientX-rect.left-transform.x)/transform.scale,y:(clientY-rect.top-transform.y)/transform.scale};}
  function onNode(id){
    if(connecting){
      if(!connectFrom){connectFrom=id;toast(view==='evolution'?'请选择第二个物种':'请选择捕食者物种');render();return;}
      if(connectFrom===id){toast('请选择另一个物种');return;}
      const links=currentLinks();if(links.some(l=>l.from===connectFrom&&l.to===id || view==='evolution'&&l.from===id&&l.to===connectFrom)){toast('这条关系已存在');return;}
      if(view==='food') {const a=data.species.find(s=>s.id===connectFrom),b=data.species.find(s=>s.id===id);if(a.trophicLevel>=b.trophicLevel) toast('提示：当前模拟器只支持 0 → 1 → 2 的营养级关系');}
      links.push(view==='evolution'&&connectFrom>id?{from:id,to:connectFrom}:{from:connectFrom,to:id});connectFrom=null;connecting=false;$('connect-button').classList.remove('active');changed();render();toast('关系已建立');return;
    }
    selected=id;render();
  }
  $('nodes').addEventListener('pointerdown',e=>{
    const el=e.target.closest('.node');if(!el)return;
    drag={id:el.dataset.id,origin:toWorld(e.clientX,e.clientY),x:Number(el.style.left.replace('px','')),y:Number(el.style.top.replace('px','')),graph:graphView(),moved:false};
    el.setPointerCapture(e.pointerId);e.preventDefault();
  });
  $('nodes').addEventListener('pointermove',e=>{if(!drag)return;const p=toWorld(e.clientX,e.clientY);const dx=p.x-drag.origin.x,dy=p.y-drag.origin.y;if(Math.abs(dx)+Math.abs(dy)>3)drag.moved=true;if(!drag.moved)return;const s=data.species.find(x=>x.id===drag.id);if(!s)return;const x=clamp(Math.round(drag.x+dx),0,1800),y=clamp(Math.round(drag.y+dy),0,1200);if(drag.graph==='food'){s.foodX=x;s.foodY=y;}else{s.x=x;s.y=y;}const el=$('nodes').querySelector(`[data-id="${CSS.escape(s.id)}"]`);if(el){el.style.left=`${x}px`;el.style.top=`${y}px`;}renderEdgesOnly();});
  $('nodes').addEventListener('pointerup',e=>{if(!drag)return;const {id,moved}=drag;drag=null;if(moved)changed();else onNode(id);});
  $('nodes').addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){const el=e.target.closest('.node');if(el){e.preventDefault();onNode(el.dataset.id);}}});
  function renderEdgesOnly(){ $('edge-lines').innerHTML=edgeMarkup(); }
  $('edge-lines').addEventListener('click',e=>{const path=e.target.closest('.edge');if(!path)return;if(confirm('删除这条关系？')){currentLinks().splice(Number(path.dataset.index),1);changed();render();}});
  $('inspector-body').addEventListener('change',async e=>{
    const s=data.species.find(x=>x.id===selected);if(!s)return;
    if(e.target.id==='image-upload'){
      const file=e.target.files?.[0];if(!file)return;
      toast('正在处理图片…');
      try { const image=await prepareImage(file);if(!data.species.includes(s))return;s.image=image;changed();render();toast('图片已上传并优化'); }
      catch(error){toast(error.message || '图片处理失败');}
      return;
    }
    const key=e.target.dataset.field;if(!key)return;
    if(key==='name')s[key]=String(e.target.value).trim().slice(0,40)||'未命名物种';
    else if(key==='description')s[key]=String(e.target.value).trim().slice(0,240);
    else if(key==='image')s.image=safeImage(e.target.value.trim());
    else if(key==='baseEcologicalNiche')s[key]=['Land','Water','Air'].includes(e.target.value)?e.target.value:'Land';
    else s[key]=range(e.target.value,key==='baseSize'?1:0,key==='trophicLevel'?2:100,DEFAULT_TRAITS[key]);
    changed();render();
  });
  $('inspector-body').addEventListener('click',e=>{if(e.target.id!=='delete-species')return;const s=data.species.find(x=>x.id===selected);if(!s||!confirm(`删除「${s.name}」及其所有关系？`))return;data.species=data.species.filter(x=>x.id!==selected);data.evolutionLinks=data.evolutionLinks.filter(l=>l.from!==selected&&l.to!==selected);data.foodLinks=data.foodLinks.filter(l=>l.from!==selected&&l.to!==selected);selected=null;changed();render();toast('物种已删除');});
  $('close-inspector').onclick=()=>{selected=null;render();};
  document.querySelectorAll('[data-view]').forEach(el=>el.addEventListener('click',()=>setView(el.dataset.view)));
  $('connect-button').onclick=()=>{connecting=!connecting;connectFrom=null;$('connect-button').classList.toggle('active',connecting);toast(connecting?(view==='evolution'?'依次点击两个可相互演化的物种':'先点击猎物，再点击捕食者'):'已退出连线模式');render();};
  $('add-species').onclick=()=>$('species-dialog').showModal();
  $('species-form').addEventListener('submit',e=>{if(e.submitter?.value!=='save')return;e.preventDefault();const form=new FormData(e.currentTarget),name=String(form.get('name')||'').trim();if(!name)return;const p=toWorld($('viewport').getBoundingClientRect().left+$('viewport').clientWidth/2,$('viewport').getBoundingClientRect().top+$('viewport').clientHeight/2);const x=clamp(Math.round(p.x-82),0,1800),y=clamp(Math.round(p.y-82),0,1200);const s={id:uuid(),name,description:String(form.get('description')||'').trim().slice(0,240),image:safeImage(String(form.get('image')||'')),x,y,foodX:x,foodY:y,...DEFAULT_TRAITS};data.species.push(s);selected=s.id;e.currentTarget.reset();$('species-dialog').close();changed();render();toast('已创建物种');});
  $('fit-button').onclick=()=>{if(!data.species.length){transform={x:80,y:50,scale:1};render();return;}const points=data.species.map(position),minX=Math.min(...points.map(p=>p.x)),maxX=Math.max(...points.map(p=>p.x+164)),minY=Math.min(...points.map(p=>p.y)),maxY=Math.max(...points.map(p=>p.y+170));const vp=$('viewport');const scale=clamp(Math.min((vp.clientWidth-90)/(maxX-minX),(vp.clientHeight-90)/(maxY-minY)),.4,1.4);transform={scale,x:(vp.clientWidth-(maxX-minX)*scale)/2-minX*scale,y:(vp.clientHeight-(maxY-minY)*scale)/2-minY*scale};render();};
  function zoom(factor){const vp=$('viewport'),cx=vp.clientWidth/2,cy=vp.clientHeight/2,next=clamp(transform.scale*factor,.35,2);transform.x=cx-(cx-transform.x)*next/transform.scale;transform.y=cy-(cy-transform.y)*next/transform.scale;transform.scale=next;render();}
  $('zoom-in').onclick=()=>zoom(1.2);$('zoom-out').onclick=()=>zoom(1/1.2);
  $('viewport').addEventListener('wheel',e=>{if(!e.ctrlKey)return;e.preventDefault();zoom(e.deltaY<0?1.1:1/1.1);},{passive:false});
  $('viewport').addEventListener('pointerdown',e=>{if(e.target.closest('.node')||e.target.closest('.edge'))return;drag={pan:true,x:e.clientX,y:e.clientY,tx:transform.x,ty:transform.y};$('viewport').setPointerCapture(e.pointerId);});
  $('viewport').addEventListener('pointermove',e=>{if(!drag?.pan)return;transform.x=drag.tx+e.clientX-drag.x;transform.y=drag.ty+e.clientY-drag.y;render();});
  $('viewport').addEventListener('pointerup',()=>{if(drag?.pan)drag=null;});
  function imageForExport(url) {
    return new Promise(resolve=>{
      if(!url){resolve(null);return;}
      const image=new Image();
      if(/^https?:/i.test(new URL(url,location.href).protocol))image.crossOrigin='anonymous';
      image.onload=()=>resolve(image);image.onerror=()=>resolve(null);image.src=url;
    });
  }
  function shortText(ctx,value,maxWidth) {
    let label=String(value||'');while(label.length && ctx.measureText(label).width>maxWidth)label=label.slice(0,-1);
    return label.length<String(value||'').length ? `${label.slice(0,-1)}…` : label;
  }
  async function exportGraphImage() {
    if(view==='table')return;
    const kind=view,items=data.species.map(s=>({...s,...position(s)})),links=currentLinks().map(l=>({...l}));
    const minX=items.length?Math.min(...items.map(s=>s.x)):0,minY=items.length?Math.min(...items.map(s=>s.y)):0;
    const maxX=items.length?Math.max(...items.map(s=>s.x+164)):640,maxY=items.length?Math.max(...items.map(s=>s.y+170)):360;
    const width=Math.max(820,Math.ceil(maxX-minX+160)),height=Math.max(560,Math.ceil(maxY-minY+190));
    const canvas=document.createElement('canvas'),scale=2;canvas.width=width*scale;canvas.height=height*scale;
    const ctx=canvas.getContext('2d');if(!ctx){toast('浏览器无法生成图片');return;}ctx.scale(scale,scale);
    ctx.fillStyle='#f8faf7';ctx.fillRect(0,0,width,height);
    ctx.fillStyle='#233e31';ctx.font='bold 27px "Noto Sans SC",sans-serif';ctx.fillText(kind==='evolution'?'进化网络':'食物网',68,57);
    ctx.fillStyle='#7a927e';ctx.font='13px "Noto Sans SC",sans-serif';ctx.fillText(`DARWIN ATLAS   ·   ${items.length} 个物种   ·   ${links.length} 条关系`,68,81);
    const point=s=>({x:s.x-minX+80+82,y:s.y-minY+120+82}),byId=Object.fromEntries(items.map(s=>[s.id,s]));
    ctx.lineWidth=3;ctx.strokeStyle=kind==='food'?'#d0a25c':'#91b99b';ctx.fillStyle='#d0a25c';
    for(const link of links){const a=byId[link.from],b=byId[link.to];if(!a||!b)continue;const pa=point(a),pb=point(b),dx=pb.x-pa.x,dy=pb.y-pa.y,len=Math.hypot(dx,dy)||1,ux=dx/len,uy=dy/len,offset=kind==='food'?86:78;const sx=pa.x+ux*78,sy=pa.y+uy*78,ex=pb.x-ux*offset,ey=pb.y-uy*offset;ctx.beginPath();ctx.moveTo(sx,sy);ctx.lineTo(ex,ey);ctx.stroke();if(kind==='food'){ctx.beginPath();ctx.moveTo(ex,ey);ctx.lineTo(ex-ux*13-uy*7,ey-uy*13+ux*7);ctx.lineTo(ex-ux*13+uy*7,ey-uy*13-ux*7);ctx.closePath();ctx.fill();}}
    const images=await Promise.all(items.map(s=>imageForExport(safeImage(s.image))));
    items.forEach((s,i)=>{const x=s.x-minX+80,y=s.y-minY+120;
      ctx.fillStyle='#ffffff';ctx.strokeStyle='#dfe8df';ctx.lineWidth=1;ctx.beginPath();ctx.roundRect(x,y,164,166,11);ctx.fill();ctx.stroke();
      ctx.fillStyle='#edf2e8';ctx.beginPath();ctx.arc(x+82,y+53,37,0,Math.PI*2);ctx.fill();
      if(images[i]){ctx.save();ctx.beginPath();ctx.arc(x+82,y+53,33,0,Math.PI*2);ctx.clip();const im=images[i],factor=Math.max(66/im.naturalWidth,66/im.naturalHeight),w=im.naturalWidth*factor,h=im.naturalHeight*factor;ctx.drawImage(im,x+82-w/2,y+53-h/2,w,h);ctx.restore();}
      else{ctx.fillStyle='#749277';ctx.font='29px sans-serif';ctx.textAlign='center';ctx.fillText('✳',x+82,y+63);}
      ctx.textAlign='center';ctx.fillStyle='#324b39';ctx.font='bold 13px "Noto Sans SC",sans-serif';ctx.fillText(shortText(ctx,s.name,145),x+82,y+110);
      ctx.fillStyle='#91a194';ctx.font='10px "Noto Sans SC",sans-serif';ctx.fillText(shortText(ctx,s.description||'暂无简介',145),x+82,y+127);
      ctx.fillStyle='#557c5f';ctx.font='10px "Noto Sans SC",sans-serif';ctx.fillText(`☀ ${s.baseFitTemperature}     层级 ${s.trophicLevel}`,x+82,y+148);
    });
    canvas.toBlob(blob=>{if(!blob){toast('图片导出失败');return;}const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=kind==='evolution'?'darwin-evolution-network.png':'darwin-food-web.png';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);toast('图片已导出');},'image/png');
  }
  $('export-image').onclick=()=>exportGraphImage().catch(()=>toast('图片导出失败'));
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
