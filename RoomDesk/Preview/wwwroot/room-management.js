const $=id=>document.getElementById(id);
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
let rooms=[],token='',saving=false;
async function api(path,body){const response=await fetch(path,body?{method:'POST',headers:{'Content-Type':'application/json','X-RoomDesk-Token':token},body:JSON.stringify(body)}:{});const data=await response.json();if(!response.ok)throw Error(data.error||'请求失败');return data;}
function render(){
 const query=$('manage-query').value.trim().toLowerCase(),floor=$('manage-floor').value;
 const filtered=rooms.filter(r=>(String(r.number).includes(query)||r.type.toLowerCase().includes(query))&&(floor==='all'||r.floor===Number(floor)));
 $('manage-result').textContent=`显示 ${filtered.length} / ${rooms.length} 间`;
 $('manage-rows').innerHTML=filtered.length?filtered.map(r=>`<tr><td><strong>${r.number}</strong></td><td>${r.floor} 楼</td><td>${esc(r.type)}</td><td><span class="management-status ${r.statusKey}">● ${esc(r.status)}</span></td><td><form class="room-price-form" data-price-room="${r.id}"><input type="number" aria-label="${r.number} 默认价格" min="0" max="99999999.99" step="0.01" required value="${r.defaultPrice??''}" placeholder="未设置"><button type="submit">保存</button></form></td></tr>`).join(''):'<tr><td colspan="5">没有符合条件的房间。</td></tr>';
}
async function load(){
 const data=await api('/api/board');rooms=data.snapshot.rooms;token=data.token;
 const floors=[...new Set(rooms.map(r=>r.floor))],selected=$('manage-floor').value;
 $('room-total').textContent=rooms.length;$('floor-total').textContent=floors.length;$('type-total').textContent=new Set(rooms.map(r=>r.type)).size;
 $('manage-floor').innerHTML='<option value="all">全部楼层</option>'+floors.map(f=>`<option value="${f}">${f} 楼</option>`).join('');
 $('manage-floor').value=floors.some(f=>String(f)===selected)?selected:'all';
 $('floor-summary').innerHTML=floors.map(f=>`<span>${f} 楼 <strong>${rooms.filter(r=>r.floor===f).length}</strong> 间</span>`).join('');render();$('manage-save').disabled=false;
}
$('manage-query').addEventListener('input',render);$('manage-floor').addEventListener('change',render);
$('manage-refresh').onclick=async()=>{if(saving)return;try{await load();$('manage-feedback').textContent='房间清单已刷新';}catch(e){$('manage-feedback').textContent='读取失败：'+e.message;}};
$('manage-add-form').onsubmit=async e=>{
 e.preventDefault();if(saving||!token)return;const form=e.target,data=new FormData(form);
 saving=true;form.querySelectorAll('input,select,button').forEach(el=>el.disabled=true);$('manage-refresh').disabled=true;$('manage-error').textContent='';
 try{
  await api('/api/rooms',{number:Number(data.get('number')),floor:Number(data.get('floor')),type:data.get('type'),defaultPrice:data.get('defaultPrice')===''?null:Number(data.get('defaultPrice'))});
  form.elements.number.value='';$('manage-feedback').textContent=`${data.get('number')} 号房已添加，当前为待清扫`;
  try{await load();}catch{$('manage-feedback').textContent='房间已添加，清单刷新失败，请点击刷新。';}
 }catch(e){$('manage-error').textContent=e.message;}
 finally{saving=false;form.querySelectorAll('input,select,button').forEach(el=>el.disabled=false);$('manage-refresh').disabled=false;}
};
load().then(()=>$('manage-feedback').textContent='已载入本机房间清单').catch(e=>$('manage-feedback').textContent='读取失败：'+e.message);

$('manage-rows').addEventListener('submit',async e=>{e.preventDefault();if(saving)return;const form=e.target,room=rooms.find(r=>String(r.id)===form.dataset.priceRoom);if(!room)return;const price=Number(form.querySelector('input').value);saving=true;form.querySelector('button').disabled=true;
 try{await api(`/api/rooms/${room.id}/price`,{version:room.version,price});await load();$('manage-feedback').textContent=`${room.number} 默认价格已更新；历史售出价保持原值。`;}catch(e){$('manage-feedback').textContent=e.message;}finally{saving=false;form.querySelector('button').disabled=false;}});
