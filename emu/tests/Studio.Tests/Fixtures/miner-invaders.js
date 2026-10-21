// Mechanical fixture, not a finished game or an AI design example.
var mm=games.a, inv=games.b, lasers=[], drops=[], frames=[], fireWait=0, cavern=-1, lives=0, facing=1;
function place(a){return {x:a.x+16,y:108-a.row*16,w:16,h:8,index:a.index,type:a.type,points:a.points};}
function visible(a){return mm.pixel(mm.area.x+a.x+8,mm.area.y+a.y+4)===0;}
function init(){
    ['alienA','alienB','alienC'].forEach(function(s){frames.push(inv.sprite(s));});
    inv.keepRows(2);inv.setRack(16);inv.volume=0.15;
    mm.set('air',50);
    cavern=mm.cavern();lives=mm.get('lives');game.state('playing');game.level(cavern+1);
    log('willy '+JSON.stringify(mm.willy()));
}
function tick(i){
    ['left','right','jump'].forEach(function(k){mm.control(k,i[k]);});
    var w=mm.willy(); inv.moveCannon(w.x-16);
    if(i.left)facing=-1;if(i.right)facing=1;
    if(mm.cavern()!==cavern){cavern=mm.cavern();inv.restart();inv.keepRows(2);inv.setRack(16);lasers=[];drops=[];game.level(cavern+1);}
    if(mm.get('lives')<lives){game.death('reglas de la caverna');lasers=[];drops=[];}
    lives=mm.get('lives');
    if(w.dying)return;
    if(fireWait>0)fireWait--;
    if(i.fire && !fireWait){lasers.push({x:w.x+(facing>0?11:-3),y:w.y+6,w:4,h:1,dx:facing*3});fireWait=12;}
    var aliens=inv.aliens().map(place).filter(visible);
    lasers=lasers.filter(function(l){
        l.x+=l.dx;
        for(var a of aliens)if(overlap(l,a)&&inv.killAlien(a.index)){
            var before=mm.get('air'); mm.addAir(2);mm.addScore(a.points);game.score(a.points);
            log('impacto '+JSON.stringify({alien:a.index,airBefore:before,airAfter:mm.get('air'),alive:inv.wave().left}));return false;
        }
        return l.x>=8 && l.x<248 && mm.pixel(mm.area.x+l.x,mm.area.y+l.y)===0;
    });
    inv.bombs().forEach(function(b){inv.removeBomb(b.index);drops.push({x:b.x+16,y:12,w:2,h:5});});
    drops=drops.filter(function(b){
        b.y+=0.6;
        if(overlap(b,w)){mm.killWilly();log('bomba toca Willy');return false;}
        // Native pixels, not an invented flat floor. Check Willy before scenery.
        if(b.y>127)return false;
        var color=mm.pixel(mm.area.x+b.x,mm.area.y+b.y+b.h);
        return color===0;
    });
}
function draw(){
    inv.aliens().map(place).filter(visible).forEach(function(a){screen.sprite(frames[a.type==='A'?0:a.type==='B'?1:2],a.x,a.y,0x00D700);});
    lasers.forEach(function(l){screen.rect(l.x,l.y,l.w,l.h,0xFFFFFF);});
    drops.forEach(function(b){screen.rect(b.x,b.y,b.w,b.h,0x00D700);});
}
