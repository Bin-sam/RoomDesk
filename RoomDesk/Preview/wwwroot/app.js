const $=id=>document.getElementById(id);
let state={rooms:[],activities:[]},token='',selected=null,busy=false;
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
$('date').textContent=new Date().toLocaleDateString('zh-CN',{month:'long',day:'numeric',weekday:'short'});
async function api(path,body){const response=await fetch(path,body?{method:'POST',headers:{'Content-Type':'application/json','X-RoomDesk-Token':token},body:JSON.stringify(body)}:{});const data=await response.json();if(!response.ok)throw Error(data.error||'请求失败');return data;}
async function load(){const data=await api('/api/board');state=data.snapshot;token=data.token;if(!selected)selected=state.rooms[0]?.id;const floor=$('floor').value;$('floor').innerHTML='<option value="all">全部楼层</option>'+[...new Set(state.rooms.map(r=>r.floor))].map(f=>`<option value="${f}">${f} 楼</option>`).join('');$('floor').value=[...$('floor').options].some(o=>o.value===floor)?floor:'all';render();}
function render(){const stats=[['可入住',state.ready,'ready','可直接办理入住'],['在住',state.occupied,'occupied','当前已入住客房'],['已预订',state.reserved,'reserved','等待客人到店'],['待清扫',state.dirty,'dirty','含在住及维修房间'],['维修 / 停用',state.unavailable,'maintenance','暂不可安排入住']];$('stats').innerHTML=stats.map(([label,value,key,hint])=>`<div class="stat stat-${key}" title="${hint}"><div><div class="stat-label">${label}</div><div class="stat-hint">${hint}</div></div><div class="stat-value">${value}<span class="stat-unit">间</span></div></div>`).join('');renderRooms();renderDetail();}
function renderRooms(){const status=$('status').value,floor=$('floor').value,query=$('search').value.trim();const rooms=state.rooms.filter(r=>String(r.number).includes(query)&&(floor==='all'||r.floor===Number(floor))&&(status==='all'||(status==='dirty'?!r.isClean:r.statusKey===status)));$('result').textContent=`显示 ${rooms.length} / ${state.rooms.length} 间`;
 $('rooms').innerHTML=rooms.length?[...new Set(rooms.map(r=>r.floor))].map(f=>`<section class="floor-row"><div class="floor-title">${String(f).padStart(2,'0')}<span>楼</span><small>${rooms.filter(r=>r.floor===f).length} 间</small></div><div class="room-grid">${rooms.filter(r=>r.floor===f).map(r=>`<button class="room ${r.statusKey} ${r.id===selected?'selected':''}" data-room="${r.id}" aria-label="${r.number} ${r.status} ${r.cleanLabel} ${esc(r.guestLabel)}" aria-pressed="${r.id===selected}"><div class="room-top"><div class="room-number">${r.number}</div><div class="room-type" title="${esc(r.type)}">${esc(r.type)}</div></div><div class="room-guest ${r.occupancy==='Occupied'?'':'room-meta'}" title="${esc(r.guestLabel||r.cleanLabel)}">${esc(r.occupancy==='Occupied'?(r.guestName||'未登记入住人'):(r.isClean?'已清洁':'等待清扫'))}</div><div class="room-bottom"><b>● ${esc(r.status)}</b>${!r.isClean&&r.statusKey!=='dirty'?'<span class="clean-alert">待清扫</span>':''}</div></button>`).join('')}</div></section>`).join(''):'<div class="empty">没有符合条件的房间，请调整筛选。</div>';}
function renderDetail(){const r=state.rooms.find(r=>r.id===selected);if(!r){$('detail').innerHTML='<p>选择房间查看详情。</p>';return;}$('detail').innerHTML=`<div class="overline">房间详情 / ROOM DETAILS</div><div class="detail-number">${r.number}<small>号房</small></div><div class="detail-type">${esc(r.type)} <span>· ${r.floor} 楼</span></div><div class="detail-status ${r.statusKey}">● ${esc(r.status)}</div><div class="clean-label ${!r.isClean?'needs-cleaning':''}">清洁状态：${esc(r.cleanLabel)}</div>${r.guestLabel?`<div class="detail-guest">${esc(r.guestLabel)}</div>`:''}<div class="action-list">${r.occupancy==='Occupied'?`<button data-view-guest ${busy?'disabled':''}>查看入住信息</button>`:''}${r.actions.map((a,i)=>`<button data-action="${a.key}" ${busy?'disabled':''} class="${i===0?'primary':''}">${esc(a.label)} <span>↗</span></button>`).join('')}</div><div class="detail-note">退房后自动转为待清扫。<br>清扫完成后，空房才可再次入住。</div>`;}
async function perform(fn,message){if(busy)return;busy=true;renderDetail();$('feedback').textContent='正在处理…';try{await fn();$('feedback').textContent=message;}catch(e){$('feedback').textContent='操作未完成：'+e.message;try{await load();}catch{} }finally{busy=false;renderDetail();}}
$('rooms').addEventListener('click',e=>{const card=e.target.closest('[data-room]');if(card&&!busy){selected=Number(card.dataset.room);renderRooms();renderDetail();}});
$('detail').addEventListener('click',e=>{const b=e.target.closest('[data-action]');if(!b||busy)return;const room=state.rooms.find(r=>r.id===selected),action=b.dataset.action;if(action==='CheckIn'){openCheckIn(room);return;}if(['CheckOut','Disable'].includes(action)&&!confirm(`确认对 ${room.number} 执行“${room.actions.find(a=>a.key===action).label}”？`))return;perform(async()=>{await api(`/api/rooms/${room.id}/action`,{version:room.version,action});await load();},`${room.number} 房态已保存到本机`);});
['floor','status'].forEach(id=>$(id).addEventListener('change',renderRooms));$('search').addEventListener('input',renderRooms);
$('refresh').onclick=()=>perform(load,'房态已刷新');
$('history').onclick=()=>{$('history-list').innerHTML=state.activities.length?state.activities.map(a=>`<div class="activity"><small>${esc(new Date(a.atUtc.endsWith('Z') ? a.atUtc : a.atUtc+'Z').toLocaleString('zh-CN'))}</small><strong>${a.roomNumber} · ${esc(a.action)}</strong><p>${esc(a.before)} → ${esc(a.after)}</p></div>`).join(''):'<p>尚无操作记录。</p>';$('history-dialog').showModal();};
document.querySelectorAll('[data-close]').forEach(b=>b.onclick=()=>$(b.dataset.close).close());

let checkInRoom=null,checkInSaving=false;
function openCheckIn(room,guest=null,readOnly=false){
 resetGuestSuggestions();$('guest-fill-status').textContent='';checkInRoom=room;const form=$('checkin-form');form.reset();$('checkin-error').textContent='';
 $('checkin-title').textContent=readOnly?'入住信息':'办理入住';
 $('checkin-room').textContent=`${room.number} 号房 · ${room.type} · ${room.floor} 楼`;
 $('checkin-hint').textContent=readOnly?(guest?'当前在住客人的登记信息。':'此房间是已有示例房态，尚无入住登记信息。'):'填写入住人信息，保存后房间将转为在住。';
 for(const key of ['name','phone','documentType','documentNumber','notes']){form.elements[key].value=guest?.[key]||'';form.elements[key].disabled=readOnly;}
 $('reader-panel').hidden=readOnly;$('reader-panel').open=false;$('reader-status').textContent='填入后请核对，再保存入住。此功能不核验身份证真伪。';$('guest-sale-price').disabled=readOnly;$('guest-sale-price').value=readOnly?(guest?.salePrice??''):(room.defaultPrice??'');$('guest-price-hint').textContent=readOnly?'':`房间默认价：${room.defaultPrice==null?'未设置':Number(room.defaultPrice).toFixed(2)+' 元'}；登记为本次入住总价。`;$('checkin-save').hidden=readOnly;$('checkin-cancel').textContent=readOnly?'关闭':'取消';$('checkin-dialog').showModal();
}
$('detail').addEventListener('click',async e=>{if(!e.target.closest('[data-view-guest]')||busy)return;const room=state.rooms.find(r=>r.id===selected);await perform(async()=>{const data=await api(`/api/rooms/${room.id}/guest`);openCheckIn(room,data.guest,true);},'已读取入住信息');});
$('checkin-dialog').addEventListener('cancel',e=>{if(checkInSaving)e.preventDefault();});
$('checkin-dialog').addEventListener('close',()=>{resetGuestSuggestions();$('checkin-form').reset();checkInRoom=null;});
$('checkin-form').onsubmit=async e=>{
 e.preventDefault();if(checkInSaving||!checkInRoom)return;
 const form=e.target,room=checkInRoom,data=new FormData(form),guest=Object.fromEntries(data);guest.salePrice=data.get('salePrice')===''?null:Number(data.get('salePrice'));
 if(!guest.name.trim()){$('checkin-error').textContent='请填写入住人姓名。';$('guest-name').focus();return;}
 checkInSaving=true;$('checkin-error').textContent='';form.querySelectorAll('button').forEach(b=>b.disabled=true);$('checkin-save').textContent='正在保存…';
 try{
  await api(`/api/rooms/${room.id}/action`,{version:room.version,action:'CheckIn',guest});
  $('checkin-dialog').close();
  try{await load();$('feedback').textContent=`${room.number} 入住信息已保存，房间已转为在住`;}catch{$('feedback').textContent='入住已保存，请刷新房态列表。';}
 }catch(error){$('checkin-error').textContent=error.message;}
 finally{checkInSaving=false;form.querySelectorAll('button').forEach(b=>b.disabled=false);$('checkin-save').textContent='保存并入住';}
};



const guestLookups=[{field:'name',input:$('guest-name'),list:$('name-suggestions')},{field:'document',input:$('guest-document-number'),list:$('document-suggestions')}];
let guestLookupSequence=0,guestLookupTimer,guestLookupItems=[],guestLookupActive=null,guestLookupIndex=-1;
function resetGuestSuggestions(){clearTimeout(guestLookupTimer);guestLookupSequence++;guestLookupItems=[];guestLookupActive=null;guestLookupIndex=-1;for(const item of guestLookups){item.list.hidden=true;item.list.innerHTML='';item.input.setAttribute('aria-expanded','false');item.input.removeAttribute('aria-activedescendant');}}
function chooseGuestSuggestion(index){const item=guestLookupItems[index];if(!item||checkInSaving)return;const form=$('checkin-form');form.elements.name.value=item.name;form.elements.phone.value=item.phone;form.elements.documentType.value=item.documentType;form.elements.documentNumber.value=item.documentNumber;resetGuestSuggestions();$('guest-fill-status').textContent='已填入历史住客信息，请核对后保存。';$('guest-phone').focus();}
async function lookupGuest(config){
 resetGuestSuggestions();const text=config.input.value.trim();if(!text||config.input.disabled||checkInSaving)return;
 const sequence=guestLookupSequence;guestLookupActive=config;
 config.list.hidden=false;config.list.innerHTML='<span class="suggestion-empty">正在匹配历史记录…</span>';config.input.setAttribute('aria-expanded','true');
 try{const data=await api('/api/guests/suggestions?'+new URLSearchParams({query:text,field:config.field}));if(sequence!==guestLookupSequence||!$('checkin-dialog').open||document.activeElement!==config.input)return;
 guestLookupItems=data;guestLookupIndex=-1;config.list.innerHTML=data.length?data.map((g,i)=>`<button type="button" role="option" id="${config.field}-suggest-${i}" aria-selected="false" data-suggestion="${i}">${esc(g.name)}<small>${esc(g.documentType||'未登记证件')} ${esc(g.maskedDocument)} · 最近入住 ${esc(g.lastCheckInDate)}</small></button>`).join(''):'<span class="suggestion-empty">没有匹配记录，可继续手动填写。</span>';
 }catch(e){if(sequence===guestLookupSequence)config.list.innerHTML='<span class="suggestion-empty">历史匹配暂不可用，可手动填写。</span>';}
}
for(const config of guestLookups){
 config.input.addEventListener('input',()=>{resetGuestSuggestions();$('guest-fill-status').textContent='';guestLookupTimer=setTimeout(()=>lookupGuest(config),200);});
 config.input.addEventListener('focus',()=>{if(config.input.value.trim())lookupGuest(config);});
 config.input.addEventListener('keydown',e=>{
 if(config.list.hidden)return;
 if(e.key==='Escape'){e.preventDefault();e.stopPropagation();resetGuestSuggestions();return;}
 if(e.key==='ArrowDown'||e.key==='ArrowUp'){e.preventDefault();if(!guestLookupItems.length)return;guestLookupIndex=guestLookupIndex<0?(e.key==='ArrowDown'?0:guestLookupItems.length-1):(guestLookupIndex+(e.key==='ArrowDown'?1:-1)+guestLookupItems.length)%guestLookupItems.length;config.list.querySelectorAll('[role=option]').forEach((o,i)=>o.setAttribute('aria-selected',String(i===guestLookupIndex)));config.input.setAttribute('aria-activedescendant',`${config.field}-suggest-${guestLookupIndex}`);}
 if(e.key==='Enter'){e.preventDefault();if(guestLookupIndex>=0)chooseGuestSuggestion(guestLookupIndex);}
 });
 config.list.addEventListener('mousedown',e=>e.preventDefault());
 config.list.addEventListener('click',e=>{const option=e.target.closest('[data-suggestion]');if(option)chooseGuestSuggestion(Number(option.dataset.suggestion));});
}
$('checkin-form').addEventListener('focusout',e=>{if(guestLookupActive&&!guestLookupActive.list.contains(e.relatedTarget)&&e.relatedTarget!==guestLookupActive.input)resetGuestSuggestions();});
$('checkin-form').addEventListener('pointerdown',e=>{if(guestLookupActive&&!guestLookupActive.list.contains(e.target)&&e.target!==guestLookupActive.input)resetGuestSuggestions();});

let readerReading=false;
$('reader-text').addEventListener('keydown',e=>{
 if(e.key==='Tab'&&!e.shiftKey){e.preventDefault();const input=e.target;input.setRangeText('\t',input.selectionStart,input.selectionEnd,'end');}
 if(e.key==='Enter'&&e.ctrlKey){e.preventDefault();fillFromReader();}
});
$('reader-fill').onclick=fillFromReader;
async function fillFromReader(){
 if(readerReading||checkInSaving||!checkInRoom||$('reader-panel').hidden)return;
 const room=checkInRoom,raw=$('reader-text').value;readerReading=true;$('reader-fill').disabled=true;
 try{
  const card=await api('/api/identity/parse',{text:raw});
  if(!$('checkin-dialog').open||checkInRoom!==room||checkInSaving||$('reader-text').value!==raw)return;
  resetGuestSuggestions();$('guest-name').value=card.name;$('guest-document-type').value='身份证';$('guest-document-number').value=card.documentNumber;$('guest-phone').value='';
  $('reader-text').value='';$('reader-panel').open=false;$('guest-fill-status').textContent='已填入姓名和身份证号，请核对并补充联系电话后保存。';$('guest-phone').focus();
 }catch(e){$('reader-status').textContent=e.message;}
 finally{readerReading=false;$('reader-fill').disabled=checkInSaving;}
}

let staysResult={records:[],total:0,page:1,pageSize:30},staysFilter={search:'',status:'all'},staysSequence=0,staysLoading=false;
function staysParams(){return new URLSearchParams(staysFilter);}
async function loadStays(page=1){
 const sequence=++staysSequence;staysLoading=true;$('stays-message').textContent='正在读取入住记录…';$('stays-export').setAttribute('aria-disabled','true');$('stays-prev').disabled=true;$('stays-next').disabled=true;
 $('stay-record-detail').hidden=true;$('stays-rows').innerHTML='';
 try{
  const params=staysParams();params.set('page',page);const data=await api('/api/stays?'+params);if(sequence!==staysSequence)return;
  staysResult=data;
  $('stays-rows').innerHTML=data.records.length?data.records.map(r=>`<tr><td>${r.roomNumber}</td><td>${esc(r.name)}</td><td>${esc(r.phone||'—')}</td><td>${esc(r.checkInText)}</td><td>${esc(r.checkOutText)}</td><td>${esc(r.status)}</td><td><button data-stay-id="${r.id}">详情</button>${r.checkedOutAtUtc?` <button data-delete-stay="${r.id}">删除 🔒</button>`:''}</td></tr>`).join(''):'<tr><td colspan="7">没有符合条件的入住记录。</td></tr>';
  $('stays-count').textContent=`共 ${data.total} 条 · 第 ${data.page} / ${Math.max(1,Math.ceil(data.total/data.pageSize))} 页`;
  $('stays-message').textContent=staysFilter.search?`搜索结果：${staysFilter.search}`:'包含当前在住与已退房的登记记录';
  $('stays-export').setAttribute('aria-disabled','false');$('stays-prev').disabled=data.page<=1;$('stays-next').disabled=data.page*data.pageSize>=data.total;
 }catch(e){if(sequence===staysSequence){$('stays-message').textContent='读取失败：'+e.message;$('stays-count').textContent='';}}
 finally{if(sequence===staysSequence)staysLoading=false;}
}
function openStayRecords(){staysFilter={search:'',status:'all'};$('stays-search-form').reset();$('stays-dialog').showModal();loadStays();}
$('stay-records').onclick=openStayRecords;
$('stays-search-form').onsubmit=e=>{e.preventDefault();staysFilter={search:$('stays-query').value.trim(),status:$('stays-status').value};loadStays();};
let staysSearchTimer;
$('stays-query').addEventListener('input',()=>{clearTimeout(staysSearchTimer);staysSearchTimer=setTimeout(()=>{staysFilter={search:$('stays-query').value.trim(),status:$('stays-status').value};loadStays();},250);});
$('stays-status').addEventListener('change',()=>{clearTimeout(staysSearchTimer);staysFilter={search:$('stays-query').value.trim(),status:$('stays-status').value};loadStays();});
$('stays-reset').onclick=()=>{staysFilter={search:'',status:'all'};$('stays-search-form').reset();loadStays();};
$('stays-prev').onclick=()=>{if(!staysLoading)loadStays(staysResult.page-1);};$('stays-next').onclick=()=>{if(!staysLoading)loadStays(staysResult.page+1);};
$('stays-rows').onclick=async e=>{const remove=e.target.closest('[data-delete-stay]');if(remove){const id=Number(remove.dataset.deleteStay);const row=staysResult.records.find(r=>r.id===id);if(!row)return;await withOperationPassword(`删除 #${id} · ${row.roomNumber} 号房的已退房记录（将移出账单）`,async password=>{await api(`/api/stays/${id}/delete`,{password});await loadStays();});return;}const button=e.target.closest('[data-stay-id]');if(!button)return;const r=staysResult.records.find(r=>String(r.id)===button.dataset.stayId);if(!r)return;
 $('stay-record-detail').textContent=`记录 #${r.id} · ${r.roomNumber} 号房 · ${r.status}\n入住人：${r.name}    联系电话：${r.phone||'—'}\n证件类型：${r.documentType||'—'}    证件号码：${r.documentNumber||'—'}\n入住：${r.checkInText}    退房：${r.checkOutText}\n售出总价：${r.salePrice==null?'未登记':r.salePriceText+' 元'}\n备注：${r.notes||'—'}`;$('stay-record-detail').hidden=false;};
$('stays-export').onclick=async()=>{if(staysLoading||$('stays-export').getAttribute('aria-disabled')==='true')return;const filters={...staysFilter};await withOperationPassword('导出入住记录',password=>downloadProtectedFile('/api/stays/export',{...filters,password},token,'入住记录.csv'));};
perform(async()=>{await load();const number=new URLSearchParams(location.search).get('checkin');const room=state.rooms.find(r=>String(r.number)===number&&r.actions.some(a=>a.key==='CheckIn'));if(new URLSearchParams(location.search).get('view')==='stays'){openStayRecords();}else if(room){selected=room.id;render();openCheckIn(room);}},'已载入本地房态 · 选择房间进行操作');
