// prints a jump.js result line compactly
let s=''; process.stdin.on('data',d=>s+=d).on('end',()=>{ const line=s.trim().split('\n').pop(); try { const j=JSON.parse(line);
 console.log(JSON.stringify({t:j.toggle,z:j.zoom,w:j.where,si:j.si,before:j.before,after:j.after,worst:{t:j.worst.t,top:j.worst.top,y:j.worst.y}}));
 console.log('  changes:', j.changes.map(c=>c.t+':y'+c.y+',top'+c.top+',h'+c.h+(c.anchor?'':',NA')+(c.focus?','+c.focus:'')).join(' | ').slice(0,1500)); if (j.errors.length) console.log('  errors', j.errors);
} catch(e) { console.log(s.slice(0,2000)); } });
