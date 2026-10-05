(()=>{
 let active=false;
 window.withOperationPassword=async(title,action)=>{
  if(active)return false;active=true;
  const dialog=document.createElement('dialog');dialog.className='password-dialog';
  dialog.innerHTML='<form><h2></h2><p class="password-note">本次操作需要验证操作密码。</p><label>操作密码<input type="password" autocomplete="current-password" maxlength="128" required></label><p class="password-error form-error" role="alert"></p><div class="dialog-actions"><button type="button">取消</button><button type="submit" class="primary">验证并继续</button></div></form>';
  dialog.querySelector('h2').textContent=title;document.body.append(dialog);dialog.showModal();
  const form=dialog.querySelector('form'),input=dialog.querySelector('input'),error=dialog.querySelector('.password-error');let running=false;
  return await new Promise(resolve=>{
   function close(result){input.value='';dialog.close();dialog.remove();active=false;resolve(result);}
   dialog.querySelector('[type=button]').onclick=()=>{if(!running)close(false);};
   dialog.addEventListener('cancel',e=>{e.preventDefault();if(!running)close(false);});
   fetch('/api/security').then(r=>{if(!r.ok)throw Error('无法读取密码设置');return r.json();}).then(s=>{if(!s.configured){input.disabled=true;dialog.querySelector('[type=submit]').disabled=true;error.textContent='尚未设置操作密码，请先前往“数据与安全”设置。';const link=document.createElement('a');link.href='/security.html';link.textContent='前往数据与安全';link.className='nav-link';error.after(link);}}).catch(e=>{error.textContent=e.message;});
   form.onsubmit=async e=>{e.preventDefault();if(running||input.disabled)return;running=true;form.querySelectorAll('button').forEach(b=>b.disabled=true);error.textContent='';
    try{await action(input.value);close(true);}catch(e){error.textContent=e.message;input.value='';input.focus();}
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
