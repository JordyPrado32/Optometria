// Each viewport owns its scene, renderer, resources and animation lifecycle.
let enginePromise;
async function engine() {
    if (window.THREE?.OrbitControls) return window.THREE;
    if (!enginePromise) enginePromise = (async () => {
        const load = src => new Promise((resolve, reject) => {
            const script = document.createElement("script"); script.src = src;
            script.onload = resolve; script.onerror = () => { script.remove(); reject(new Error("No se pudo cargar el motor 3D")); };
            document.head.appendChild(script);
        });
        if (!window.THREE) await load("https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js");
        if (!window.THREE.OrbitControls) await load("https://cdn.jsdelivr.net/npm/three@0.128.0/examples/js/controls/OrbitControls.js");
        return window.THREE;
    })().catch(e => { enginePromise = null; throw e; });
    return enginePromise;
}
export async function createScene(host, kind, initial) {
    const T = await engine();
    if (!host.isConnected) throw new Error("Vista cerrada");
    const renderer = new T.WebGLRenderer({ antialias: true, alpha: true });
    renderer.setPixelRatio(Math.min(devicePixelRatio || 1, 1.75));
    renderer.outputEncoding = T.sRGBEncoding;
    renderer.toneMapping = T.ACESFilmicToneMapping; renderer.toneMappingExposure = .9;
    renderer.localClippingEnabled = true;
    host.appendChild(renderer.domElement);
    const scene = new T.Scene();
    // The eye model is intentionally presented on a dark stage: translucent cornea,
    // light rays and retinal structures remain legible without changing the page theme.
    if(kind==="eye") scene.background=new T.Color(0x071217);
    const camera = new T.PerspectiveCamera(38, 1, .05, 100);
    camera.position.set(kind === "eye" ? -.5 : 0, .4, kind === "eye" ? 8.3 : 4.8);
    const controls = new T.OrbitControls(camera, renderer.domElement);
    controls.target.set(kind === "eye" ? -.2 : 0, .05, 0); controls.enableDamping = true; controls.dampingFactor = .09;
    controls.minDistance = kind === "eye" ? 5 : 3; controls.maxDistance = 16; controls.autoRotateSpeed = .55;
    scene.add(new T.HemisphereLight(0xdfeff5, 0x3b3027, .8));
    const key = new T.DirectionalLight(0xffe7ca, 2.1); key.position.set(-3,5,6); scene.add(key);
    const fill = new T.DirectionalLight(0xb7dcec, 1.25); fill.position.set(4,1,-3); scene.add(fill);
    const rim = new T.DirectionalLight(0xffffff, 1.8); rim.position.set(1,4,-5); scene.add(rim);
    const root = new T.Group(); scene.add(root);
    const meshes = {}, paths = [], pulses = [], labelSprites = [];
    let settings = {}, disposed = false, raf = 0, shown = true, rotating = false, dirty = true, last = 0, ticks = 0, shapeKey = "", lastDisease = "";
    const resources = new Set();
    function mesh(geo, mat, parent = root) { const o = new T.Mesh(geo, mat); parent.add(o); return o; }
    const standard = (color, extra = {}) => new T.MeshStandardMaterial({ color, roughness:.4, ...extra });
    function line(points,color,parent = root,opacity=1) {
        const o = new T.Line(new T.BufferGeometry().setFromPoints(points),new T.LineBasicMaterial({color,transparent:true,opacity}));
        parent.add(o); return o;
    }
    function label(text,x,y,z,side="left") {
        const canvas=document.createElement("canvas");canvas.width=420;canvas.height=92;
        const ctx=canvas.getContext("2d");
        if(kind==="eye"){
            const g=ctx.createLinearGradient(0,0,420,0);g.addColorStop(0,"rgba(5,17,22,0)");g.addColorStop(.16,"rgba(5,17,22,.78)");g.addColorStop(.84,"rgba(5,17,22,.78)");g.addColorStop(1,"rgba(5,17,22,0)");
            ctx.fillStyle=g;ctx.roundRect(12,13,396,57,10);ctx.fill();
            ctx.strokeStyle="rgba(132,225,208,.32)";ctx.lineWidth=1;ctx.roundRect(12,13,396,57,10);ctx.stroke();
            ctx.fillStyle="#d8f4ed";ctx.font="600 25px Arial";ctx.textAlign="center";ctx.textBaseline="middle";ctx.fillText(text,210,42);
            ctx.fillStyle="#76ddc2";ctx.beginPath();ctx.arc(side==="left"?31:389,42,4,0,Math.PI*2);ctx.fill();
        }else{ctx.font="600 24px Arial";ctx.fillStyle="#e7eff2";ctx.textAlign="center";ctx.fillText(text,210,48);}
        const texture=new T.CanvasTexture(canvas); resources.add(texture);
        const s=new T.Sprite(new T.SpriteMaterial({map:texture,transparent:true,depthTest:false}));
        s.scale.set(1.5,.3,1);s.position.set(x,y,z);root.add(s);labelSprites.push(s);
        return s;
    }
    function release(group) {
        const gs=new Set(),ms=new Set();
        group.traverse(o=>{if(o.geometry)gs.add(o.geometry);if(o.material)(Array.isArray(o.material)?o.material:[o.material]).forEach(m=>ms.add(m));});
        gs.forEach(g=>g.dispose());ms.forEach(m=>m.dispose());
    }
    if(kind==="eye") {
        meshes.anatomy = new T.Group(); root.add(meshes.anatomy);
        // Hemisphere facing the viewer is clipped away to expose the interior.
        const plane=new T.Plane(new T.Vector3(0,0,-1),.05);
        const sclera=mesh(new T.SphereGeometry(1.72,64,40),standard(0xf1eee8,{side:T.DoubleSide,roughness:.62,clippingPlanes:[plane]}),meshes.anatomy);
        meshes.sclera=sclera; meshes.clip=plane;
        const retinalCanvas=document.createElement("canvas");retinalCanvas.width=768;retinalCanvas.height=384;
        const retinalContext=retinalCanvas.getContext("2d");
        const retinalGradient=retinalContext.createRadialGradient(500,190,22,430,192,430);retinalGradient.addColorStop(0,"#ffca75");retinalGradient.addColorStop(.13,"#c96535");retinalGradient.addColorStop(.6,"#813317");retinalGradient.addColorStop(1,"#36140d");retinalContext.fillStyle=retinalGradient;retinalContext.fillRect(0,0,768,384);
        retinalContext.globalAlpha=.45;retinalContext.strokeStyle="#e98d5a";retinalContext.lineWidth=3;
        for(let i=0;i<44;i++){const a=i*2.399,sy=192+Math.sin(a)*12;retinalContext.beginPath();retinalContext.moveTo(505,192);for(let j=1;j<12;j++){const p=j/11,r=35+p*260;retinalContext.lineTo(505+Math.cos(a+p*.27)*r,sy+Math.sin(a+p*.27)*r*.45);}retinalContext.stroke();}
        const retinalTexture=new T.CanvasTexture(retinalCanvas);retinalTexture.encoding=T.sRGBEncoding;resources.add(retinalTexture);
        meshes.retina=mesh(new T.SphereGeometry(1.66,96,64),new T.MeshStandardMaterial({map:retinalTexture,side:T.DoubleSide,roughness:.74,metalness:0,clippingPlanes:[plane]}));
        const choroid=mesh(new T.SphereGeometry(1.695,96,64),new T.MeshStandardMaterial({color:0x3e150e,side:T.BackSide,roughness:.92,clippingPlanes:[plane]}));
        meshes.choroid=choroid;
        // Iris and cornea along the optical X axis.
        const iris=mesh(new T.RingGeometry(.25,.69,64),standard(0x658b87,{side:T.DoubleSide}),meshes.anatomy);
        iris.rotation.y=Math.PI/2;iris.position.x=-1.47;
        const pupil=mesh(new T.CircleGeometry(.252,48),new T.MeshStandardMaterial({color:0x071016,roughness:.18,metalness:.03,side:T.DoubleSide}),meshes.anatomy);pupil.rotation.y=Math.PI/2;pupil.position.x=-1.485;
        const irisRing=mesh(new T.TorusGeometry(.5,.018,10,56),new T.MeshStandardMaterial({color:0x98b7a6,roughness:.35}),meshes.anatomy);irisRing.rotation.y=Math.PI/2;irisRing.position.x=-1.49;
        for(let i=0;i<80;i++){
            const a=i/80*Math.PI*2;
            line([new T.Vector3(-1.48,Math.cos(a)*.27,Math.sin(a)*.27),new T.Vector3(-1.48,Math.cos(a)*.68,Math.sin(a)*.68)],i%2?0x405e58:0xa0b8a0,meshes.anatomy,.7);
        }
        const scleraVeins=new T.Group();meshes.anatomy.add(scleraVeins);
        for(let i=0;i<20;i++){const a=i*.79, points=[];for(let j=0;j<10;j++){const t=j/9, r=.72+t*.8;points.push(new T.Vector3(-Math.sqrt(Math.max(.15,1.72*1.72-r*r))-.015,Math.cos(a+t*.28)*r,Math.sin(a+t*.28)*r));}line(points,i%3?0xc88c8c:0xd4a8a0,scleraVeins,.26);}
        const vitreous=mesh(new T.SphereGeometry(1.58,72,48),new T.MeshPhysicalMaterial({color:0x69bdd7,transparent:true,opacity:.075,roughness:.03,side:T.DoubleSide,transmission:.15,clippingPlanes:[plane]}),meshes.anatomy);meshes.vitreous=vitreous;
        const glass=new T.MeshPhysicalMaterial({color:0xc4eaff,metalness:0,roughness:.06,transparent:true,opacity:.32,side:T.DoubleSide,clearcoat:1,clearcoatRoughness:.08});
        meshes.cornea=mesh(new T.SphereGeometry(.78,48,32),glass,meshes.anatomy);meshes.cornea.scale.set(.4,1,1);meshes.cornea.position.x=-1.52;
        meshes.crystal=mesh(new T.SphereGeometry(.54,48,32),glass.clone(),meshes.anatomy);meshes.crystal.scale.set(.38,1,1);meshes.crystal.position.x=-.98;
        const nerve=mesh(new T.CylinderGeometry(.19,.27,1.04,24),standard(0xdbc3a1),meshes.anatomy);nerve.rotation.z=Math.PI/2;nerve.position.set(2.05,-.1,-.18);
        const vessels=new T.Group();meshes.retina.add(vessels);
        for(let i=0;i<28;i++){
            const points=[];const a=i*2.399;
            for(let j=0;j<=28;j++){const t=j/28;const theta=.15+t*1.55;const phi=a+.13*Math.sin(t*9+i);points.push(new T.Vector3(1.635*Math.cos(theta),1.635*Math.sin(theta)*Math.cos(phi),-Math.abs(1.635*Math.sin(theta)*Math.sin(phi))-.012));}
            line(points,i%2?0x6d302c:0xce7955,vessels,.62);
        }
        meshes.fovea=mesh(new T.SphereGeometry(.065,24,16),new T.MeshBasicMaterial({color:0xffd98c}));meshes.fovea.position.set(1.63,0,.07);
        const correction=mesh(new T.SphereGeometry(.88,64,42),glass.clone());correction.position.x=-2.65;correction.scale.set(.08,1,1);meshes.correction=correction;
        const ring=mesh(new T.TorusGeometry(.88,.022,12,64),standard(0x9cc0c4,{metalness:.75,roughness:.25}));ring.rotation.y=Math.PI/2;ring.position.x=-2.65;meshes.correctionRing=ring;
        // Disease overlays are intentionally separate from anatomy: a condition can be switched
        // without rebuilding the scene or hiding the normal structures used for comparison.
        meshes.disease=new T.Group();root.add(meshes.disease);
        const haze=mesh(new T.SphereGeometry(.55,48,32),new T.MeshPhysicalMaterial({color:0xd7d3bd,transparent:true,opacity:0,roughness:.38,side:T.DoubleSide}),meshes.disease);haze.scale.set(.39,1,1);haze.position.x=-.98;meshes.cataract=haze;
        const cone=mesh(new T.SphereGeometry(.82,48,32),new T.MeshPhysicalMaterial({color:0xbde9f4,transparent:true,opacity:0,roughness:.07,side:T.DoubleSide,clearcoat:1}),meshes.disease);cone.scale.set(.62,1,1);cone.position.x=-1.62;meshes.keratoconus=cone;
        const glaucoma=mesh(new T.TorusGeometry(.18,.07,16,40),new T.MeshBasicMaterial({color:0xd9c38d,transparent:true,opacity:0}),meshes.disease);glaucoma.rotation.y=Math.PI/2;glaucoma.position.set(1.57,-.1,-.18);meshes.glaucoma=glaucoma;
        const retinopathy=new T.Group(),macular=new T.Group();meshes.disease.add(retinopathy,macular);meshes.retinopathy=retinopathy;meshes.macular=macular;
        for(let i=0;i<18;i++){const a=i*2.4,r=.28+(i%5)*.12;const lesion=mesh(new T.SphereGeometry(.028+(i%3)*.012,12,8),new T.MeshBasicMaterial({color:i%3?0xb9332e:0xf1cd7a,transparent:true,opacity:.88}),retinopathy);lesion.position.set(1.56,Math.cos(a)*r,-.07+Math.sin(a)*r*.38);}
        for(let i=0;i<15;i++){const a=i/15*Math.PI*2,r=.08+(i%4)*.04;const spot=mesh(new T.SphereGeometry(.035,12,8),new T.MeshBasicMaterial({color:0x5d3025,transparent:true,opacity:.86}),macular);spot.position.set(1.60,Math.cos(a)*r,.07+Math.sin(a)*r);}
        meshes.rays = new T.Group();root.add(meshes.rays);
        for(let i=0;i<9;i++){
            const ray=line([new T.Vector3(),new T.Vector3()],0xb6e9ff,meshes.rays,.95);
            const glow=line([new T.Vector3(),new T.Vector3()],0x7edaff,meshes.rays,.2);
            const pulse=mesh(new T.SphereGeometry(.026,10,8),new T.MeshBasicMaterial({color:0xf0fdff}),meshes.rays);
            paths.push({ray,glow,off:(i-4)*.16});pulses.push(pulse);
        }
        meshes.focus=mesh(new T.SphereGeometry(.075,20,16),new T.MeshBasicMaterial({color:0xffe3a3}),meshes.rays);
        // The scene starts at a neutral optical emitter.  No optotype is shown here so
        // the anatomical model stays focused on light propagation and correction.
        const sourceRing=mesh(new T.TorusGeometry(.28,.018,12,48),new T.MeshBasicMaterial({color:0x81ead5,transparent:true,opacity:.85}),meshes.rays);sourceRing.rotation.y=Math.PI/2;sourceRing.position.set(-4.43,0,.1);meshes.optotype=sourceRing;
        const projector=mesh(new T.CylinderGeometry(.33,.39,.52,32),new T.MeshStandardMaterial({color:0x173b42,metalness:.72,roughness:.22}),meshes.rays);projector.rotation.z=Math.PI/2;projector.position.set(-4.75,0,.1);meshes.projector=projector;
        const emitter=mesh(new T.CircleGeometry(.23,32),new T.MeshBasicMaterial({color:0xcffff4,transparent:true,opacity:.94,side:T.DoubleSide}),meshes.rays);emitter.rotation.y=Math.PI/2;emitter.position.set(-4.48,0,.1);meshes.emitter=emitter;
        const imageCanvas=document.createElement("canvas");imageCanvas.width=160;imageCanvas.height=160;const imageContext=imageCanvas.getContext("2d");imageContext.clearRect(0,0,160,160);imageContext.fillStyle="#ffd795";imageContext.font="700 118px Arial";imageContext.textAlign="center";imageContext.textBaseline="middle";imageContext.save();imageContext.translate(80,80);imageContext.rotate(Math.PI);imageContext.fillText("A",0,5);imageContext.restore();
        const imageTexture=new T.CanvasTexture(imageCanvas);resources.add(imageTexture);const retinalImage=mesh(new T.PlaneGeometry(.27,.27),new T.MeshBasicMaterial({map:imageTexture,transparent:true,opacity:.95,side:T.DoubleSide,depthWrite:false}),meshes.retina);retinalImage.position.set(1.61,0,.08);meshes.retinalImage=retinalImage;
        label("Córnea",-2.02,1.27,.3,"left");label("Cristalino",-.75,1.08,.3,"left");label("Retina",1.85,1.2,.3,"right");label("Fóvea",2.55,.36,.3,"right");label("Nervio óptico",2.67,-.88,.3,"right");
        const grid=new T.GridHelper(14,28,0x255a61,0x102d35);grid.material.transparent=true;grid.material.opacity=.38;grid.position.y=-1.92;scene.add(grid);
    } else {
        const floor=mesh(new T.PlaneGeometry(25,25),new T.MeshBasicMaterial({color:0x000000,transparent:true,opacity:.08,depthWrite:false}),scene);floor.rotation.x=-Math.PI/2;floor.position.y=-.95;
        // Soft procedural contact shadow; no external texture or model dependencies.
        const c=document.createElement("canvas");c.width=c.height=128;const ctx=c.getContext("2d"),g=ctx.createRadialGradient(64,64,2,64,64,64);
        g.addColorStop(0,"rgba(0,0,0,.85)");g.addColorStop(1,"rgba(0,0,0,0)");ctx.fillStyle=g;ctx.fillRect(0,0,128,128);
        const tex=new T.CanvasTexture(c);resources.add(tex);
        const shadow=mesh(new T.PlaneGeometry(4,2.5),new T.MeshBasicMaterial({map:tex,transparent:true,depthWrite:false}),scene);shadow.rotation.x=-Math.PI/2;shadow.position.set(0,-.94,-.25);
    }
    function buildFrames(s) {
        if(meshes.frame){release(meshes.frame);root.remove(meshes.frame);}
        const frame=new T.Group();root.add(frame);meshes.frame=frame;meshes.lenses=[];
        const metal=standard(s.color,{metalness:s.material==="metal"?.86:.12,roughness:s.material==="metal"?.23:.35});
        const lensMat=new T.MeshPhysicalMaterial({color:0xe2f0e9,transparent:true,opacity:.17,roughness:.06,metalness:.02,side:T.DoubleSide,clearcoat:1});
        const width=s.caliber/52,center=.61+s.bridge/180;
        function contour(sign){
            const sh=new T.Shape();
            if(s.shape==="round"){sh.absellipse(0,0,.56,.56,0,Math.PI*2,false,0);}
            else if(s.shape==="wayfarer"||s.shape==="browline"){
                sh.moveTo(-.58,.37);sh.quadraticCurveTo(0,.49,.58,.37);sh.lineTo(.47,-.3);sh.quadraticCurveTo(0,-.56,-.47,-.3);sh.closePath();
            }else if(s.shape==="rectangle"){
                sh.moveTo(-.6,.34);sh.lineTo(.6,.34);sh.lineTo(.55,-.32);sh.quadraticCurveTo(0,-.42,-.55,-.32);sh.closePath();
            }else if(s.shape==="cateye"){
                sh.moveTo(-.62,.22);sh.quadraticCurveTo(-.25,.65,.12,.43);sh.lineTo(.58,.62);sh.lineTo(.47,-.28);sh.quadraticCurveTo(-.04,-.52,-.55,-.28);sh.closePath();
            }else{
                sh.moveTo(-.56,.25);sh.bezierCurveTo(-.54,.62,.36,.6,.55,.26);
                sh.bezierCurveTo(.76,-.1,.37,-.72,-.06,-.55);sh.bezierCurveTo(-.43,-.45,-.61,-.14,-.56,.25);
            }
            const pts=sh.getPoints(72).map(p=>new T.Vector3(sign*(p.x*width+center),p.y,0));
            const curve=new T.CatmullRomCurve3(pts,true);
            mesh(new T.TubeGeometry(curve,96,s.material==="metal"?.022:.055,10,true),metal,frame);
            const lens=mesh(new T.ExtrudeGeometry(sh,{depth:Math.max(.018,s.edgeThickness||.025),bevelEnabled:false,curveSegments:48}),lensMat.clone(),frame);lens.scale.x=sign*width;lens.position.x=sign*center;lens.position.z=-(s.edgeThickness||.025)/2;meshes.lenses.push(lens);
        }
        contour(-1);contour(1);
        function tube(points,r=.022){mesh(new T.TubeGeometry(new T.CatmullRomCurve3(points.map(p=>new T.Vector3(...p))),28,r,10,false),metal,frame);}
        tube([[-.21,.15,0],[0,.26,.015],[.21,.15,0]]);
        if(s.shape==="aviator")tube([[-.48,.44,0],[0,.5,0],[.48,.44,0]],.018);
        for(const side of [-1,1]){
            const xx=side*(center+width*.52),length=s.temple/85;
            tube([[xx,.2,0],[xx+side*.08,.23,-.15],[xx+side*.03,.2,-length],[xx-side*.1,-.05,-length-.18]],s.material==="metal"?.022:.045);
            const hinge=mesh(new T.BoxGeometry(.085,.065,.12),metal,frame);hinge.position.set(xx,.2,-.05);
            const pad=mesh(new T.SphereGeometry(.075,16,12),standard(0xccb89c,{transparent:true,opacity:.6}),frame);pad.scale.set(.4,1,.65);pad.position.set(side*.23,-.03,-.13);
        }
        frame.position.y=.1;
    }
    function update(s) {
        if(disposed)return;settings=s||{};dirty=true;
        if(kind==="eye"){
            const {residual=0,axis=90,condition,diseases=[],focusDisease="healthy",corrected,cut,layers=[]}=settings;
            const has=d=>diseases.includes(d), diseaseKey=[...diseases].sort().join(",")+"|"+focusDisease;
            meshes.anatomy.visible=layers.includes("anatomy");meshes.retina.visible=layers.includes("retina");meshes.choroid.visible=layers.includes("retina");meshes.rays.visible=layers.includes("rays");
            meshes.optotype.visible=layers.includes("rays");
            meshes.sclera.material.clippingPlanes=cut?[meshes.clip]:[];
            meshes.retina.material.clippingPlanes=cut?[meshes.clip]:[];
            meshes.sclera.material.needsUpdate=true;meshes.retina.material.needsUpdate=true;
            meshes.correction.visible=meshes.correctionRing.visible=corrected&&layers.includes("lens");
            labelSprites.forEach(o=>o.visible=layers.includes("labels"));
            meshes.cataract.material.opacity=has("cataract")?.68:0;
            meshes.cataract.visible=has("cataract");
            meshes.crystal.material.color.setHex(has("cataract")?0xb9ad8b:0xc4eaff);
            meshes.crystal.material.opacity=has("cataract")?.58:.32;
            meshes.keratoconus.material.opacity=has("keratoconus")?.32:0;
            meshes.keratoconus.visible=has("keratoconus");
            meshes.cornea.material.opacity=has("keratoconus")?.15:.32;
            meshes.cornea.scale.set(has("keratoconus")?.58:.4,1,1);
            meshes.cornea.position.x=has("keratoconus")?-1.64:-1.52;
            meshes.glaucoma.material.opacity=has("glaucoma")?.92:0;
            meshes.glaucoma.visible=has("glaucoma");
            meshes.retinopathy.visible=has("retinopathy");
            meshes.macular.visible=has("macular");
            // On a disease change, guide the observation to the relevant anatomy without
            // removing manual orbit controls. Subsequent slider changes do not reset it.
            if(diseaseKey!==lastDisease){
                lastDisease=diseaseKey;
                if(["glaucoma","retinopathy","macular"].includes(focusDisease)) controls.target.set(1.1,0,0);
                else if(["cataract","keratoconus"].includes(focusDisease)) controls.target.set(-1.05,0,0);
                else controls.target.set(-.2,.05,0);
                controls.update();
            }
            const focus=1.63+residual*.22;
            meshes.focus.position.set(focus,0,.09);meshes.focus.material.color.setHex(Math.abs(residual)<.125?0xafffcb:0xffc382);
            paths.forEach((p,i)=>{
                const a=axis*Math.PI/180,astig=condition==="astigmatism";
                const fy=astig?1.63+residual*.22:focus, fz=astig?1.63:focus;
                const oy=p.off*Math.cos(a),oz=astig?p.off*Math.sin(a):.1;
                const points=[new T.Vector3(-4.5,p.off,.12),new T.Vector3(-2.65,p.off*(corrected?1.08:1),.12),new T.Vector3(-1.48,p.off,.12),new T.Vector3(-.98,astig?oy:p.off*.72,astig?oz:.12)];
                const end=Math.max(1.85,focus+.15);
                points.push(new T.Vector3(focus,0,.08));
                if(astig){points[4]=new T.Vector3(1.63,oy*(1-(1.63+.98)/(fy+.98)),oz*(1-(1.63+.98)/(fz+.98)));}
                if(focus<1.63&&!astig)points.push(new T.Vector3(end,-p.off*(end-focus)*.5,.08));
                p.points=points;p.ray.geometry.dispose();p.ray.geometry=new T.BufferGeometry().setFromPoints(points);p.glow.geometry.dispose();p.glow.geometry=new T.BufferGeometry().setFromPoints(points);
            });
        }else{
            const k=JSON.stringify([s.shape,s.material,s.color,s.caliber,s.bridge,s.temple]);
            if(k!==shapeKey){shapeKey=k;buildFrames(s);}
            const tint={clear:0xeaf2f1,bluecut:0xf0ddc4,antireflective:0xb0dbc9,photochromic:0x6e6255,polarized:0x354440,mirror:0x8aadc4};
            meshes.lenses.forEach(l=>{
                const m=l.material;m.color.setHex(tint[s.treatment]||0xeaf2f1);m.opacity=s.treatment==="photochromic"?.18+s.sun*.65:s.treatment==="polarized"?.78:s.treatment==="mirror"?.9:.16;
                m.metalness=s.treatment==="mirror"?.95:.03;m.roughness=s.treatment==="mirror"?.1:.07;
            });
            root.rotation.y=s.view==="profile"?Math.PI/2:s.view==="front"?0:-.45;root.rotation.x=s.view==="threequarter"?.12:0;
            key.intensity=s.bright?3:2.1;
        }
        schedule();
    }
    function schedule(){if(!raf&&!disposed&&shown&&!document.hidden)raf=requestAnimationFrame(render);}
    function render(time){
        raf=0;if(disposed||!shown||document.hidden)return;
        const reduced=settings.reduced||matchMedia("(prefers-reduced-motion: reduce)").matches;
        controls.autoRotate=rotating&&!reduced;
        controls.update();
        if(kind==="eye"&&!reduced){pulses.forEach((p,i)=>{const points=paths[i].points;if(!points)return;const t=((time*.00024+i*.12)%1)*(points.length-1),j=Math.floor(t);p.position.copy(points[j]).lerp(points[Math.min(j+1,points.length-1)],t-j);});}
        if(dirty||!reduced||ticks<30){renderer.render(scene,camera);dirty=false;ticks++;}
        if((kind==="eye"&&!reduced)||controls.autoRotate||ticks<30)schedule();
    }
    function resize(){
        if(disposed)return;
        const {width,height}=host.getBoundingClientRect();if(!width||!height)return;
        renderer.setSize(width,height,false);camera.aspect=width/height;camera.updateProjectionMatrix();
        // Frame the horizontal optical axis on narrow phones.
        camera.position.z=kind==="eye"?(camera.aspect<1?15:8.3):(camera.aspect<1?7.5:4.8);
        dirty=true;ticks=0;schedule();
    }
    const ro=new ResizeObserver(resize);ro.observe(host);
    const io=new IntersectionObserver(entries=>{shown=entries[0].isIntersecting;if(shown)schedule();else if(raf){cancelAnimationFrame(raf);raf=0;}});io.observe(host);
    const visibility=()=>{if(document.hidden){cancelAnimationFrame(raf);raf=0;}else{dirty=true;schedule();}};
    document.addEventListener("visibilitychange",visibility);
    controls.addEventListener("change",()=>{dirty=true;ticks=0;schedule();});
    function action(command){
        if(command==="rotate")rotating=!rotating;
        if(command==="zoom")camera.position.multiplyScalar(.86);
        if(command==="reset"){camera.position.set(kind==="eye"?-.5:0,.4,kind==="eye"?(camera.aspect<1?15:8.3):(camera.aspect<1?7.5:4.8));controls.target.set(kind==="eye"?-.2:0,.05,0);root.rotation.set(0,kind==="frames"?-.45:0,0);}
        if(command==="fullscreen"){const panel=host.parentElement;if(document.fullscreenElement)document.exitFullscreen?.();else panel.requestFullscreen?.().catch(()=>{});}
        dirty=true;ticks=0;schedule();
    }
    function dispose(){
        if(disposed)return;disposed=true;cancelAnimationFrame(raf);ro.disconnect();io.disconnect();controls.dispose();document.removeEventListener("visibilitychange",visibility);
        release(scene);resources.forEach(x=>x.dispose());renderer.dispose();renderer.forceContextLoss();renderer.domElement.remove();
    }
    renderer.domElement.addEventListener("webglcontextlost",e=>{e.preventDefault();dispose();host.textContent="La vista 3D se interrumpió. Cambia de módulo para recargarla.";},{once:true});
    update(initial);resize();return {update,action,dispose};
}
export async function startCamera(video){
    if(!navigator.mediaDevices?.getUserMedia)throw new Error("Cámara no disponible");
    const stream=await navigator.mediaDevices.getUserMedia({video:{facingMode:"user"},audio:false});
    if(!video.isConnected){stream.getTracks().forEach(t=>t.stop());throw new Error("Vista cerrada");}
    video.srcObject=stream;await video.play();
    let disposed=false;
    const stop=()=>{if(disposed)return;disposed=true;stream.getTracks().forEach(t=>t.stop());video.srcObject=null;document.removeEventListener("visibilitychange",hide);};
    const hide=()=>{if(document.hidden)stop();};document.addEventListener("visibilitychange",hide);
    const capture=()=>{const canvas=document.createElement("canvas");canvas.width=video.videoWidth||640;canvas.height=video.videoHeight||480;const context=canvas.getContext("2d");context.translate(canvas.width,0);context.scale(-1,1);context.drawImage(video,0,0,canvas.width,canvas.height);return canvas.toDataURL("image/jpeg",.86);};
    // FaceDetector is a browser-provided, on-device API. We only retain an ephemeral
    // bounding box to align the try-on; no image or biometric template is uploaded.
    const analyzeFace=async()=>{
        if(!("FaceDetector" in window))throw new Error("FaceDetector no disponible");
        const detector=new FaceDetector({fastMode:true,maxDetectedFaces:1});
        const faces=await detector.detect(video);const face=faces[0];
        if(!face)return null;
        const b=face.boundingBox,w=video.videoWidth||640,h=video.videoHeight||480;
        const ratio=b.width/Math.max(1,b.height);
        const shape=ratio>.91?"round":ratio<.72?"heart":ratio<.82?"oval":"square";
        const label={oval:"Ovalado",round:"Redondo",square:"Cuadrado",heart:"Corazón"}[shape];
        return {shape,shapeLabel:label,confidence:Math.round(Math.min(96,72+Math.abs(ratio-.82)*55)),x:100-(b.x+b.width/2)/w*100,y:(b.y+b.height*.44)/h*100,width:Math.min(280,Math.max(100,b.width/w*235))};
    };
    return {dispose:stop,capture,analyzeFace};
}
export function saveDemoCase(data){
    localStorage.setItem("visual-lab-demo-case",JSON.stringify({...data,savedAt:new Date().toISOString()}));
}
export async function copyText(text){
    try { await navigator.clipboard.writeText(text); return true; }
    catch { return false; }
}
