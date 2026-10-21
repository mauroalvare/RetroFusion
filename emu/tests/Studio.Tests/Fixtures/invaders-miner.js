// Mechanical fixture. Native fleet, cannon, bunkers, score and controls remain.
var inv=games.a, mm=games.b, sprite=0, lastCount=0, timer=0, reserves=0;
function init(){
    mm.pause();sprite=mm.sprite('willy');lastCount=inv.wave().left;
    mm.set('air',44);reserves=inv.get('ships');game.state('playing');
}
function tick(i){
    ['left','right','fire'].forEach(function(k){inv.control(k,i[k]);});
    var count=inv.wave().left;
    if(count<lastCount){
        var before=mm.get('air');mm.addAir((lastCount-count)*3);
        game.score((lastCount-count)*10);log('aire por disparo '+JSON.stringify({before:before,after:mm.get('air'),killed:lastCount-count}));
    }
    lastCount=count;
    if(inv.cannon().alive && ++timer>=60){mm.addAir(-1);timer=0;}
    if(mm.get('air')<=36 && inv.cannon().alive){
        inv.hitCannon();mm.set('air',44);log('aire agotado: explosión nativa');
    }
    if(inv.get('ships')<reserves){game.death('bomba o falta de aire');log('reserva nativa '+inv.get('ships'));}
    reserves=inv.get('ships');
}
function draw(){
    var c=inv.cannon();
    if(c.alive){screen.rect(c.x,c.y,16,8,0);screen.sprite(sprite,c.x,c.y-8,0x00D700);}
    screen.box(177,233,29,4,0x00D700);screen.rect(178,234,mm.get('air')-36,2,0x00D700);
}
