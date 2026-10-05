(()=>{
 const byId=id=>document.getElementById(id);
 let words=[],sequence=0,managing=false;
 const groups=[...document.querySelectorAll('[data-platform-field]')];
 function chip(word,input){
  const button=document.createElement('button');button.type='button';button.className='platform-chip';
  button.dataset.word=word.id;button.textContent=word.name;button.setAttribute('aria-pressed',String(input.value.trim()===word.name));return button;
 }
 function draw(){
  for(const group of groups){
   const prefix=group.dataset.platformField,input=byId(prefix+'-platform'),list=byId(prefix+'-platform-chips');
   list.replaceChildren(...words.map(w=>chip(w,input)));
   if(!words.length)list.textContent='暂无常用词，可自由输入或添加。';
  }
  const list=byId('platform-manager-list');list.replaceChildren();
  for(const word of words){
   const item=document.createElement('div');item.className='platform-managed-word';
   const name=document.createElement('span');name.textContent=word.name;
   const remove=document.createElement('button');remove.type='button';remove.dataset.removeWord=word.id;remove.textContent='删除 🔒';remove.setAttribute('aria-label','删除常用平台 '+word.name);
   item.append(name,remove);list.append(item);
  }
  if(!words.length)list.textContent='还没有常用词，在上方添加一个吧。';
 }
 async function refresh(){
  const request=++sequence;
  try{const data=await api('/api/platform-presets');if(request!==sequence)return false;words=data;draw();return true;}
  catch(e){if(request!==sequence)return false;byId('platform-manager-status').textContent='读取失败：'+e.message;for(const group of groups)byId(group.dataset.platformField+'-platform-chips').textContent='常用词暂不可用，仍可手动输入。';return false;}
 }
 window.platformFieldOpened=(prefix,readOnly)=>{
  const group=groups.find(g=>g.dataset.platformField===prefix);
  group.querySelector('.platform-shortcuts').hidden=readOnly;
  if(!readOnly){draw();refresh();}
 };
 for(const group of groups){
  const prefix=group.dataset.platformField,input=byId(prefix+'-platform');
  input.addEventListener('input',()=>{for(const button of group.querySelectorAll('[data-word]'))button.setAttribute('aria-pressed',String(words.find(w=>String(w.id)===button.dataset.word)?.name===input.value.trim()));});
  group.addEventListener('click',e=>{
   if(input.disabled)return;
   const selected=e.target.closest('[data-word]');
   if(selected){const word=words.find(w=>String(w.id)===selected.dataset.word);if(word){input.value=word.name;input.dispatchEvent(new Event('input',{bubbles:true}));input.focus();}return;}
   if(e.target.closest('[data-manage-platform]')){
    byId('platform-new').value=words.some(w=>w.name===input.value.trim())?'':input.value.trim();byId('platform-manager-status').textContent='';byId('platform-manager').showModal();refresh();
   }
  });
 }
 byId('platform-add-form').onsubmit=async e=>{
  e.preventDefault();if(managing)return;managing=true;byId('platform-add').disabled=true;
  try{await api('/api/platform-presets',{name:byId('platform-new').value});byId('platform-new').value='';if(await refresh())byId('platform-manager-status').textContent='已添加，预订与入住均可使用。';}
  catch(e){byId('platform-manager-status').textContent=e.message;}
  finally{managing=false;byId('platform-add').disabled=false;}
 };
 byId('platform-manager').addEventListener('cancel',e=>{if(managing)e.preventDefault();});
 byId('platform-manager-list').onclick=async e=>{
  const button=e.target.closest('[data-remove-word]');if(!button||managing)return;
  const word=words.find(w=>String(w.id)===button.dataset.removeWord);if(!word)return;
  managing=true;
  try{await withOperationPassword('删除常用平台“'+word.name+'”',async password=>{await api('/api/platform-presets/'+word.id+'/delete',{password});if(await refresh())byId('platform-manager-status').textContent='气泡已删除，已填写的平台和历史记录保持不变。';});}
  finally{managing=false;}
 };
})();
