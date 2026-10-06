const $=id=>document.getElementById(id);
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
let rooms=[],token='',saving=false,batch=false;
async function api(path,body){const response=await fetch(path,body?{method:'POST',headers:{'Content-Type':'application/json','X-RoomDesk-Token':token},body:JSON.stringify(body)}:{});const data=await response.json();if(!response.ok)throw Error(data.error||'请求失败');return data;}
function render(){
 const query=$('manage-query').value.trim().toLowerCase(),floor=$('manage-floor').value;
 const deleted=$('manage-scope').value==='deleted';
 const filtered=rooms.filter(r=>r.isDeleted===deleted&&(String(r.number).includes(query)||r.type.toLowerCase().includes(query))&&(floor==='all'||r.floor===Number(floor)));
 $('manage-result').textContent=`显示 ${filtered.length} / ${rooms.filter(r=>r.isDeleted===deleted).length} 间`;
 $('manage-rows').innerHTML=filtered.length?filtered.map(r=>`<tr><td><strong>${r.number}</strong></td><td>${r.floor} 楼</td><td>${esc(r.type)}</td><td><span class="management-status ${r.statusKey}">● ${r.isDeleted?'已删除':esc(r.status)}</span></td><td><strong>${r.defaultPrice==null?'未设置':Number(r.defaultPrice).toFixed(2)}</strong></td><td>${r.isDeleted?`<button data-restore-room="${r.id}">恢复</button>`:`<button data-edit-room="${r.id}">编辑</button><button class="danger-link" data-delete-room="${r.id}" ${r.occupancy!=='Vacant'?'disabled title="请先退房或取消预订"':''}>删除 🔒</button>`}</td></tr>`).join(''):'<tr><td colspan="6">没有符合条件的房间。</td></tr>';
}
async function load(){
 const data=await api('/api/rooms/manage');rooms=data.snapshot.rooms;token=data.token;if(document.activeElement!==$('hotel-name'))$('hotel-name').value=data.hotelName;document.title='房间管理 · '+data.hotelName;document.querySelector('.brand-mark').textContent=Array.from(data.hotelName)[0];if(data.demo)document.querySelector('.preview-badge').textContent='独立演示数据';
 const floors=[...new Set(rooms.map(r=>r.floor))],selected=$('manage-floor').value;
 const active=rooms.filter(r=>!r.isDeleted);$('room-total').textContent=active.length;$('floor-total').textContent=new Set(active.map(r=>r.floor)).size;$('type-total').textContent=new Set(active.map(r=>r.type)).size;
 $('manage-floor').innerHTML='<option value="all">全部楼层</option>'+floors.map(f=>`<option value="${f}">${f} 楼</option>`).join('');
 $('manage-floor').value=floors.some(f=>String(f)===selected)?selected:'all';
 $('floor-summary').innerHTML=floors.map(f=>`<span>${f} 楼 <strong>${rooms.filter(r=>!r.isDeleted&&r.floor===f).length}</strong> 间</span>`).join('');render();$('manage-save').disabled=false;
}
$('manage-query').addEventListener('input',render);$('manage-floor').addEventListener('change',render);
$('manage-refresh').onclick=async()=>{if(saving)return;try{await load();$('manage-feedback').textContent='房间清单已刷新';}catch(e){$('manage-feedback').textContent='读取失败：'+e.message;}};
function parseNumbers(value){
 const parts=value.trim().replace(/\s*[-–—~～至]\s*/g,'-').split(/[\s,，、;；]+/),numbers=[],seen=new Set();
 if(!value.trim())throw Error('请填写批量房号');
 for(const part of parts){if(!part)continue;const m=part.match(/^([0-9]{1,5})(?:-([0-9]{1,5}))?$/);if(!m)throw Error('格式示例：401-408、410');const first=Number(m[1]),last=Number(m[2]??m[1]);if(first<1||last<first||last>99999)throw Error('房号须为 1–99999，范围须从小到大');if(numbers.length+last-first+1>200)throw Error('每批最多添加 200 间');for(let n=first;n<=last;n++){if(seen.has(n))throw Error(`本批房号 ${n} 重复`);seen.add(n);numbers.push(n);}}
 if(!numbers.length)throw Error('请填写批量房号');return numbers;
}
function previewBatch(){
 if(!batch)return;const form=$('manage-add-form'),box=$('batch-preview');
 try{const numbers=parseNumbers(form.elements.numbers.value),conflicts=numbers.filter(n=>rooms.some(r=>r.number===n));box.classList.toggle('batch-invalid',conflicts.length>0);box.textContent=conflicts.length?`房号已存在：${conflicts.join('、')}。整批不会添加。`:`将新增 ${numbers.length} 间 · ${form.elements.floor.value||'—'} 楼 · ${form.elements.type.value||'未填房型'}\n房号：${numbers.join('、')}`;$('manage-save').textContent=`添加 ${numbers.length} 间房间`;}
 catch(e){box.classList.add('batch-invalid');box.textContent=e.message;$('manage-save').textContent='批量添加房间';}
}
function setMode(value){if(saving)return;batch=value;$('single-number-label').hidden=batch;$('batch-number-label').hidden=!batch;$('batch-preview').hidden=!batch;const form=$('manage-add-form');form.elements.number.required=!batch;form.elements.number.disabled=batch;form.elements.numbers.required=batch;form.elements.numbers.disabled=!batch;$('mode-single').setAttribute('aria-pressed',String(!batch));$('mode-batch').setAttribute('aria-pressed',String(batch));$('manage-save').textContent=batch?'批量添加房间':'添加房间';$('manage-error').textContent='';previewBatch();}
$('mode-single').onclick=()=>setMode(false);$('mode-batch').onclick=()=>setMode(true);
$('manage-add-form').addEventListener('input',previewBatch);
$('manage-add-form').onsubmit=async e=>{
 e.preventDefault();if(saving||!token)return;const form=e.target,data=new FormData(form),isBatch=batch;
 if(isBatch){try{parseNumbers(data.get('numbers'));}catch(e){$('manage-error').textContent=e.message;return;}}
 saving=true;form.querySelectorAll('input,textarea,button').forEach(el=>el.disabled=true);$('manage-refresh').disabled=true;$('mode-single').disabled=true;$('mode-batch').disabled=true;$('manage-error').textContent='';
 try{
  const common={floor:Number(data.get('floor')),type:data.get('type'),defaultPrice:data.get('defaultPrice')===''?null:Number(data.get('defaultPrice'))};
  const result=await api(isBatch?'/api/rooms/batch':'/api/rooms',isBatch?{...common,numbers:data.get('numbers')}:{...common,number:Number(data.get('number'))});
  form.elements.number.value='';form.elements.numbers.value='';$('manage-feedback').textContent=isBatch?`已批量添加 ${result.count} 间房间，当前为待清扫`:`${data.get('number')} 号房已添加，当前为待清扫`;
  try{await load();}catch{$('manage-feedback').textContent='房间已添加，清单刷新失败，请点击刷新。';}
 }catch(e){$('manage-error').textContent=e.message;}
 finally{saving=false;form.querySelectorAll('input,textarea,button').forEach(el=>el.disabled=false);$('manage-refresh').disabled=false;$('mode-single').disabled=false;$('mode-batch').disabled=false;form.elements.number.disabled=batch;form.elements.numbers.disabled=!batch;previewBatch();}
};
setMode(false);platformFieldOpened('room',false);
load().then(()=>$('manage-feedback').textContent='已载入本机房间清单').catch(e=>$('manage-feedback').textContent='读取失败：'+e.message);

$('manage-scope').onchange=render;
$('hotel-name-form').onsubmit=async e=>{e.preventDefault();if(saving)return;saving=true;const button=e.target.querySelector('button');button.disabled=true;try{await api('/api/hotel/name',{name:$('hotel-name').value});await load();$('hotel-name-status').textContent='酒店名称已保存';}catch(e){$('hotel-name-status').textContent=e.message;}finally{saving=false;button.disabled=false;}};
$('manage-rows').addEventListener('click',async e=>{
 const button=e.target.closest('[data-delete-room],[data-restore-room]');if(!button||saving)return;const restore=!!button.dataset.restoreRoom;const room=rooms.find(r=>String(r.id)===(button.dataset.restoreRoom||button.dataset.deleteRoom));if(!room)return;saving=true;button.disabled=true;
 try{if(restore){await api(`/api/rooms/${room.id}/restore`,{version:room.version});await load();$('manage-feedback').textContent=`${room.number} 已恢复，当前为待清扫`;}else{await withOperationPassword(`删除 ${room.number} 号房（历史记录保留，可恢复）`,async password=>{await api(`/api/rooms/${room.id}/delete`,{version:room.version,password});await load();$('manage-feedback').textContent=`${room.number} 已移入“已删除房间”，历史记录保留`;});}}catch(e){$('manage-feedback').textContent=e.message;}finally{saving=false;button.disabled=false;}
});

$('manage-rows').addEventListener('click',e=>{const b=e.target.closest('[data-edit-room]');if(!b||saving)return;const room=rooms.find(r=>String(r.id)===b.dataset.editRoom);if(room)openRoomEditor(room,api,async()=>{await load();$('manage-feedback').textContent='房间修改已保存';});});
