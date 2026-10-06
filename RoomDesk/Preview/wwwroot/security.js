(()=>{
 let active=false;
 const key='roomdesk-operation-session';
 async function securityCall(path,password){const board=await (await fetch('/api/board')).json();const r=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json','X-RoomDesk-Token':board.token},body:JSON.stringify({password})});const data=await r.json();if(!r.ok)throw Error(data.error||'验证失败');return data;}
 window.lockOperationSession=async()=>{const saved=JSON.parse(sessionStorage.getItem(key)||'null');sessionStorage.removeItem(key);if(saved)await securityCall('/api/security/lock',saved.token);};
 window.withOperationPassword=async(title,action)=>{
  if(active)return false;active=true;
  try{const saved=JSON.parse(sessionStorage.getItem(key)||'null');if(saved&&Date.parse(saved.expiresAt)>Date.now()&&(await securityCall('/api/security/session',saved.token)).valid){
    if(title.includes('删除')&&!confirm(title+'？（5 分钟免密有效）')){active=false;return false;}
    try{await action(saved.token);return true;}catch(e){alert(e.message);return false;}finally{active=false;}
  }}catch{}sessionStorage.removeItem(key);
  const dialog=document.createElement('dialog');dialog.className='password-dialog';
  dialog.innerHTML='<form><h2></h2><p class="password-note">本次操作需要验证操作密码。</p><label>操作密码<input type="password" autocomplete="current-password" maxlength="128" required></label><label class="remember-password"><input type="checkbox" checked>验证后 5 分钟免重复输入</label><p class="password-error form-error" role="alert"></p><div class="dialog-actions"><button type="button">取消</button><button type="submit" class="primary">验证并继续</button></div></form>';
  dialog.querySelector('h2').textContent=title;document.body.append(dialog);dialog.showModal();
  const form=dialog.querySelector('form'),input=dialog.querySelector('input'),error=dialog.querySelector('.password-error');let running=false;
  return await new Promise(resolve=>{
   function close(result){input.value='';dialog.close();dialog.remove();active=false;resolve(result);}
   dialog.querySelector('[type=button]').onclick=()=>{if(!running)close(false);};
   dialog.addEventListener('cancel',e=>{e.preventDefault();if(!running)close(false);});
   fetch('/api/security').then(r=>{if(!r.ok)throw Error('无法读取密码设置');return r.json();}).then(s=>{if(!s.configured){input.disabled=true;dialog.querySelector('[type=submit]').disabled=true;error.textContent='尚未设置操作密码，请先前往“数据与安全”设置。';const link=document.createElement('a');link.href='/security.html';link.textContent='前往数据与安全';link.className='nav-link';error.after(link);}}).catch(e=>{error.textContent=e.message;});
   form.onsubmit=async e=>{e.preventDefault();if(running||input.disabled)return;running=true;form.querySelectorAll('button').forEach(b=>b.disabled=true);error.textContent='';
    try{const session=await securityCall('/api/security/unlock',input.value);input.value='';const remember=dialog.querySelector('[type=checkbox]').checked;if(remember)sessionStorage.setItem(key,JSON.stringify(session));try{await action(session.token);}finally{if(!remember)await securityCall('/api/security/lock',session.token);}close(true);}catch(e){error.textContent=e.message;input.value='';input.focus();}
    finally{running=false;form.querySelectorAll('button').forEach(b=>b.disabled=false);}
   };input.focus();
  });
 };
 window.downloadProtectedFile=async(path,body,token,filename)=>{
  const response=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json','X-RoomDesk-Token':token},body:JSON.stringify(body)});
  if(!response.ok){const error=await response.json();throw Error(error.error||'导出失败');}
  const blob=await response.blob(),url=URL.createObjectURL(blob),link=document.createElement('a');link.href=url;link.download=filename;document.body.append(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);
 };
})();
