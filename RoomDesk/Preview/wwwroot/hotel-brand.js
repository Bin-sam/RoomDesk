fetch('/api/hotel').then(r=>{if(!r.ok)throw Error();return r.json();}).then(h=>{document.title=document.title.split(' · ')[0]+' · '+h.name;}).catch(()=>{});
