// Mechanical fixture: the native cave acquires chase/edible-ghost decisions.
var mm=games.a, pac=games.b, ghost, ghostSprite, powered=0, pill=true, lives=0;
function init(){
    pac.pause();mm.set('air',50);var g=pac.ghosts()[0];
    // This edition's verified boot sprite has a 14x11 visible body at +16,+1.
    ghostSprite=pac.capture(g.x+16,g.y+1,14,11,0);
    ghost={x:90,y:100,w:14,h:11};lives=mm.get('lives');game.state('playing');game.level(mm.cavern()+1);
}
function tick(i){
    ['left','right','jump'].forEach(function(k){mm.control(k,i[k]);});
    var w=mm.willy();
    if(mm.get('lives')<lives){game.death('caverna o fantasma');ghost.x=90;powered=0;pill=true;}
    lives=mm.get('lives');if(w.dying)return;
    if(pill&&overlap(w,{x:51,y:102,w:4,h:4})){pill=false;powered=240;log('píldora: perseguir cambia a huir');}
    if(powered>0)powered--;
    // Kept in a verified open corridor of Central Cavern; not a generalized map solver.
    var direction=w.x<ghost.x?-1:1;
    ghost.x=clamp(ghost.x+direction*(powered?-0.12:0.4),19,220);
    if(overlap(w,ghost)){
        if(powered){
            var before=mm.get('air');mm.addAir(4);mm.addScore(200);game.score(200);
            log('fantasma comido '+JSON.stringify({before:before,after:mm.get('air')}));ghost.x=220;powered=0;
        }else{mm.killWilly();log('fantasma toca Willy');}
    }
}
function draw(){
    if(pill){screen.rect(51,102,4,4,0xB8C76F);}
    screen.sprite(ghostSprite,ghost.x,ghost.y,powered?0x0000D7:undefined);
}
