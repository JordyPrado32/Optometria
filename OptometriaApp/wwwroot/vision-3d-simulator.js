// ==========================================================================
// OPTOMETRÍA APP - SIMULADOR 3D INTERACTIVO (THREE.JS WEBGL ENGINE)
// Modo 1: Ojo Anatómico 3D con Trazado de Rayos Ópticos y Refracción
// Modo 2: Estudio 3D de Monturas, Armazones y Tratamientos de Lunas
// ==========================================================================

window.Vision3D = (function () {
    let eyeScene, eyeCamera, eyeRenderer, eyeControls, eyeAnimId;
    let eyeMeshes = {};
    let rayLines = [];
    let eyeContainer = null;
    let isEyeCrossSection = false;
    let eyeResizeObserver, glassesResizeObserver;
    const prefersReducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;

    function createRenderer(container) {
        try {
            const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
            if (!renderer.getContext()) throw new Error('WebGL unavailable');
            return renderer;
        } catch {
            container.innerHTML = '<div role="status" style="padding:2rem;color:#fff">La vista 3D no está disponible en este navegador. Usa los controles y la leyenda para revisar la explicación óptica en texto.</div>';
            return null;
        }
    }

    let glassesScene, glassesCamera, glassesRenderer, glassesControls, glassesAnimId;
    let glassesMeshes = {};
    let glassesContainer = null;
    let localCameraStream = null;

    // --------------------------------------------------------------------------
    // 1. SIMULADOR 3D DE OJO ANATÓMICO Y RAYOS REFRACTIVOS
    // --------------------------------------------------------------------------
    function initEyeSimulator(containerId) {
        eyeContainer = document.getElementById(containerId);
        if (!eyeContainer) return;

        // Limpiar previo
        if (eyeRenderer) {
            cancelAnimationFrame(eyeAnimId);
            eyeRenderer.dispose();
            eyeContainer.innerHTML = '';
        }

        const width = eyeContainer.clientWidth || 700;
        const height = eyeContainer.clientHeight || 450;

        // Escena
        eyeScene = new THREE.Scene();
        eyeScene.background = new THREE.Color(0x0a0e14);

        // Cámara
        eyeCamera = new THREE.PerspectiveCamera(45, width / height, 0.1, 100);
        eyeCamera.position.set(0, 1.5, 7.5);

        // Renderizador con antialias
        eyeRenderer = createRenderer(eyeContainer);
        if (!eyeRenderer) return;
        eyeRenderer.setSize(width, height);
        eyeRenderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
        eyeRenderer.toneMapping = THREE.ACESFilmicToneMapping;
        eyeRenderer.toneMappingExposure = 1.1;
        eyeContainer.appendChild(eyeRenderer.domElement);

        // Controles de órbita 3D
        eyeControls = new THREE.OrbitControls(eyeCamera, eyeRenderer.domElement);
        eyeControls.enableDamping = true;
        eyeControls.dampingFactor = 0.05;
        eyeControls.minDistance = 3;
        eyeControls.maxDistance = 14;
        eyeControls.maxPolarAngle = Math.PI / 2 + 0.3;

        // Luces
        const ambientLight = new THREE.AmbientLight(0xffffff, 0.7);
        eyeScene.add(ambientLight);

        const dirLight1 = new THREE.DirectionalLight(0xffffff, 1.2);
        dirLight1.position.set(5, 8, 6);
        eyeScene.add(dirLight1);

        const dirLight2 = new THREE.DirectionalLight(0x4e937a, 0.8);
        dirLight2.position.set(-6, -4, -4);
        eyeScene.add(dirLight2);

        // Cuadrícula sutil de referencia óptica
        const grid = new THREE.GridHelper(10, 20, 0x1d3557, 0x112233);
        grid.position.y = -2;
        eyeScene.add(grid);

        // Construir Modelo 3D del Ojo
        buildEyeModel();

        // Construir Haces de Rayos 3D
        buildOpticalRays();

        // Loop de render
        function animateEye() {
            eyeAnimId = requestAnimationFrame(animateEye);
            eyeControls.update();

            // Animación sutil de pulso en fóvea y haces de luz
            if (eyeMeshes.foveaGlow && !prefersReducedMotion) {
                const time = Date.now() * 0.003;
                const scale = 1 + Math.sin(time) * 0.15;
                eyeMeshes.foveaGlow.scale.set(scale, scale, scale);
            }

            eyeRenderer.render(eyeScene, eyeCamera);
        }
        animateEye();

        // Responsive resize
        if (eyeResizeObserver) eyeResizeObserver.disconnect();
        eyeResizeObserver = new ResizeObserver(onEyeResize);
        eyeResizeObserver.observe(eyeContainer);
    }

    function onEyeResize() {
        if (!eyeContainer || !eyeRenderer || !eyeCamera) return;
        const width = eyeContainer.clientWidth;
        const height = eyeContainer.clientHeight;
        if (width === 0 || height === 0) return;
        eyeCamera.aspect = width / height;
        eyeCamera.updateProjectionMatrix();
        eyeRenderer.setSize(width, height);
    }

    function buildEyeModel() {
        const eyeGroup = new THREE.Group();
        eyeGroup.position.set(1.2, 0, 0); // Desplazado a la derecha para dar espacio a los rayos y lente

        // 1. Esclera (Globo ocular blanco translúcido)
        const scleraGeo = new THREE.SphereGeometry(1.6, 48, 32);
        const scleraMat = new THREE.MeshPhysicalMaterial({
            color: 0xf8f9fa,
            roughness: 0.25,
            metalness: 0.05,
            clearcoat: 0.8,
            clearcoatRoughness: 0.1,
            transparent: true,
            opacity: 0.88,
            side: THREE.DoubleSide
        });
        const sclera = new THREE.Mesh(scleraGeo, scleraMat);
        eyeGroup.add(sclera);
        eyeMeshes.sclera = sclera;

        // 2. Córnea anterior transparente con brillo
        const corneaGeo = new THREE.SphereGeometry(0.85, 32, 24, 0, Math.PI * 2, 0, Math.PI / 2.2);
        const corneaMat = new THREE.MeshPhysicalMaterial({
            color: 0xccf0ff,
            transmission: 0.95,
            opacity: 1,
            transparent: true,
            roughness: 0.05,
            ior: 1.376, // Índice de refracción real de la córnea
            thickness: 0.3,
            specularIntensity: 1
        });
        const cornea = new THREE.Mesh(corneaGeo, corneaMat);
        cornea.rotation.z = Math.PI / 2;
        cornea.position.set(-1.35, 0, 0);
        eyeGroup.add(cornea);
        eyeMeshes.cornea = cornea;

        // 3. Iris
        const irisGeo = new THREE.RingGeometry(0.28, 0.78, 36);
        const irisMat = new THREE.MeshStandardMaterial({
            color: 0x1f77b4, // Azul profundo
            roughness: 0.5,
            side: THREE.DoubleSide
        });
        const iris = new THREE.Mesh(irisGeo, irisMat);
        iris.rotation.y = Math.PI / 2;
        iris.position.set(-1.28, 0, 0);
        eyeGroup.add(iris);
        eyeMeshes.iris = iris;

        // 4. Pupila
        const pupilGeo = new THREE.CircleGeometry(0.27, 32);
        const pupilMat = new THREE.MeshBasicMaterial({ color: 0x050505, side: THREE.DoubleSide });
        const pupil = new THREE.Mesh(pupilGeo, pupilMat);
        pupil.rotation.y = Math.PI / 2;
        pupil.position.set(-1.27, 0, 0);
        eyeGroup.add(pupil);
        eyeMeshes.pupil = pupil;

        // 5. Cristalino (Lente biconvexo interno)
        const lensGeo = new THREE.SphereGeometry(0.55, 32, 16);
        lensGeo.scale(0.35, 1, 1);
        const lensMat = new THREE.MeshPhysicalMaterial({
            color: 0xebf8ff,
            transmission: 0.92,
            transparent: true,
            opacity: 0.9,
            roughness: 0.1,
            ior: 1.406 // Índice de refracción del cristalino
        });
        const crystalLens = new THREE.Mesh(lensGeo, lensMat);
        crystalLens.position.set(-0.85, 0, 0);
        eyeGroup.add(crystalLens);
        eyeMeshes.crystalLens = crystalLens;

        // 6. Retina y Fóvea (Punto de máxima agudeza visual)
        const retinaGeo = new THREE.SphereGeometry(1.58, 32, 24, Math.PI * 0.75, Math.PI * 0.5, Math.PI * 0.25, Math.PI * 0.5);
        const retinaMat = new THREE.MeshStandardMaterial({
            color: 0xc84b31, // Tono rojizo/anaranjado de fondo de ojo
            roughness: 0.7,
            side: THREE.BackSide
        });
        const retina = new THREE.Mesh(retinaGeo, retinaMat);
        retina.rotation.y = Math.PI;
        retina.position.set(0, 0, 0);
        eyeGroup.add(retina);
        eyeMeshes.retina = retina;

        // Marcador brillante de Fóvea
        const foveaGeo = new THREE.SphereGeometry(0.09, 16, 16);
        const foveaMat = new THREE.MeshBasicMaterial({ color: 0xffdd00 });
        const fovea = new THREE.Mesh(foveaGeo, foveaMat);
        fovea.position.set(1.58, 0, 0);
        eyeGroup.add(fovea);
        eyeMeshes.fovea = fovea;

        const foveaGlowGeo = new THREE.SphereGeometry(0.18, 16, 16);
        const foveaGlowMat = new THREE.MeshBasicMaterial({ color: 0xffaa00, transparent: true, opacity: 0.45 });
        const foveaGlow = new THREE.Mesh(foveaGlowGeo, foveaGlowMat);
        foveaGlow.position.set(1.58, 0, 0);
        eyeGroup.add(foveaGlow);
        eyeMeshes.foveaGlow = foveaGlow;

        // 7. Nervio Óptico posterior
        const nerveGeo = new THREE.CylinderGeometry(0.24, 0.28, 0.85, 24);
        const nerveMat = new THREE.MeshStandardMaterial({ color: 0xf3d5b5, roughness: 0.6 });
        const nerve = new THREE.Mesh(nerveGeo, nerveMat);
        nerve.rotation.z = Math.PI / 2;
        nerve.position.set(1.95, -0.15, 0);
        eyeGroup.add(nerve);

        // 8. Lente Corrector Externo 3D
        const corrLensGeo = new THREE.CylinderGeometry(0.9, 0.9, 0.08, 36);
        corrLensGeo.rotateZ(Math.PI / 2);
        const corrLensMat = new THREE.MeshPhysicalMaterial({
            color: 0x90e0ef,
            transmission: 0.96,
            transparent: true,
            roughness: 0.05,
            clearcoat: 1,
            ior: 1.6
        });
        const correctiveLens = new THREE.Mesh(corrLensGeo, corrLensMat);
        correctiveLens.position.set(-2.6, 0, 0);
        eyeGroup.add(correctiveLens);
        eyeMeshes.correctiveLens = correctiveLens;

        // Marco del lente corrector
        const frameGeo = new THREE.TorusGeometry(0.92, 0.04, 16, 48);
        frameGeo.rotateY(Math.PI / 2);
        const frameMat = new THREE.MeshStandardMaterial({ color: 0x333333, metalness: 0.9, roughness: 0.2 });
        const lensFrame = new THREE.Mesh(frameGeo, frameMat);
        lensFrame.position.set(-2.6, 0, 0);
        eyeGroup.add(lensFrame);
        eyeMeshes.lensFrame = lensFrame;

        eyeScene.add(eyeGroup);
        eyeMeshes.eyeGroup = eyeGroup;
    }

    function buildOpticalRays() {
        // Limpiar rayos anteriores
        rayLines.forEach(r => eyeScene.remove(r));
        rayLines = [];

        // Generar 9 haces de luz láser
        const rayOffsets = [
            [0, 0],
            [0, 0.5], [0, -0.5],
            [0.5, 0], [-0.5, 0],
            [0.35, 0.35], [-0.35, 0.35],
            [0.35, -0.35], [-0.35, -0.35]
        ];

        rayOffsets.forEach(off => {
            const points = [
                new THREE.Vector3(-5.5, off[0], off[1]),
                new THREE.Vector3(-1.4, off[0] * 0.95, off[1] * 0.95), // Entrada a córnea
                new THREE.Vector3(0.35, off[0] * 0.45, off[1] * 0.45),  // Salida de cristalino
                new THREE.Vector3(2.78, 0, 0)                          // Foco en retina
            ];

            const geometry = new THREE.BufferGeometry().setFromPoints(points);
            const material = new THREE.LineBasicMaterial({
                color: 0x00ff88,
                linewidth: 2,
                transparent: true,
                opacity: 0.85
            });

            const line = new THREE.Line(geometry, material);
            eyeScene.add(line);
            rayLines.push({ line, baseOffset: off });
        });
    }

    // Actualiza los rayos y curvatura óptica según refracción y dioptrías
    function updateEyeOptics(defectType, diopters, cylinder, astigmatismAxis, hasCorrectiveLens, lensSphere) {
        if (!eyeScene || rayLines.length === 0) return;

        // Posición de la retina en el espacio mundial
        const retinaX = 2.78; // Posición focal ideal (fóvea)
        let focalX = retinaX;

        // Calcular desenfoque según el defecto
        if (defectType === "Miopia") {
            // El foco queda ANTES de la retina
            focalX = retinaX - (Math.abs(diopters) * 0.28);
        } else if (defectType === "Hipermetropia") {
            // El foco queda DETRÁS de la retina
            focalX = retinaX + (Math.abs(diopters) * 0.28);
        } else if (defectType === "Astigmatismo") {
            // Foco intermedio con dispersión conoide
            focalX = retinaX - (Math.abs(diopters) * 0.15);
        }

        // Si tiene lente correctora activada, compensar el foco
        if (hasCorrectiveLens) {
            if (defectType === "Miopia") {
                focalX -= lensSphere * 0.28; // la esfera miópica correctora es negativa
            } else if (defectType === "Hipermetropia") {
                focalX -= lensSphere * 0.28; // la esfera hipermetrópica correctora es positiva
            } else if (defectType === "Astigmatismo") {
                focalX += Math.abs(cylinder) * 0.15;
            }
            if (eyeMeshes.correctiveLens) eyeMeshes.correctiveLens.visible = true;
            if (eyeMeshes.lensFrame) eyeMeshes.lensFrame.visible = true;
        } else {
            if (eyeMeshes.correctiveLens) eyeMeshes.correctiveLens.visible = false;
            if (eyeMeshes.lensFrame) eyeMeshes.lensFrame.visible = false;
        }

        // Determinar si el foco está corregido (dentro de tolerancia de la retina)
        const isPerfectFocus = Math.abs(focalX - retinaX) < 0.12;
        const rayColor = isPerfectFocus ? 0x00ff88 : (focalX < retinaX ? 0xff4757 : 0xffa502);

        // Actualizar geometría de cada rayo
        rayLines.forEach(item => {
            const off = item.baseOffset;
            const points = [];

            // Rayo 1: Desde fuente de luz hasta el lente o córnea
            const lensX = -1.4; // Entrada a la lente correctora/córnea
            points.push(new THREE.Vector3(-5.5, off[0], off[1]));

            if (hasCorrectiveLens) {
                // Desviación en el lente corrector
            const lensBend = defectType === "Miopia" ? 1.15 : 0.85;
                points.push(new THREE.Vector3(-1.4, off[0] * lensBend, off[1] * lensBend));
            } else {
                points.push(new THREE.Vector3(-0.15, off[0], off[1])); // Llega directo a la córnea
            }

            // Rayo 2: Córnea y Cristalino
            points.push(new THREE.Vector3(0.35, off[0] * 0.5, off[1] * 0.5));

            // Rayo 3: Convergencia hacia el punto focal
            points.push(new THREE.Vector3(focalX, 0, 0));

            // Si el foco está antes de la retina, los rayos continúan y se cruzan difusos
            if (focalX < retinaX) {
                const spread = (retinaX - focalX) * 0.45;
                points.push(new THREE.Vector3(retinaX, -off[0] * spread, -off[1] * spread));
            }

            item.line.geometry.setFromPoints(points);
            item.line.material.color.setHex(rayColor);
        });

        // Actualizar curvatura del cristalino y forma de lente
        if (eyeMeshes.crystalLens) {
            const accommodationScale = 1 + (diopters * 0.05);
            eyeMeshes.crystalLens.scale.set(0.35 * accommodationScale, 1, 1);
        }
    }

    function setEyeIrisColor(hexColor) {
        if (eyeMeshes.iris) {
            eyeMeshes.iris.material.color.set(hexColor);
        }
    }

    function toggleEyeCrossSection() {
        isEyeCrossSection = !isEyeCrossSection;
        if (!eyeMeshes.sclera) return;

        if (isEyeCrossSection) {
            // Mostrar corte sagital (ocultar mitad superior)
            eyeMeshes.sclera.material.opacity = 0.35;
            eyeMeshes.sclera.material.transparent = true;
        } else {
            eyeMeshes.sclera.material.opacity = 0.88;
        }
    }

    function resetEyeCamera() {
        if (eyeCamera && eyeControls) {
            eyeCamera.position.set(0, 1.5, 7.5);
            eyeControls.target.set(0, 0, 0);
            eyeControls.update();
        }
    }

    // --------------------------------------------------------------------------
    // 2. ESTUDIO 3D DE MONTURAS Y TRATAMIENTOS DE LUNAS
    // --------------------------------------------------------------------------
    function initGlassesStudio(containerId) {
        glassesContainer = document.getElementById(containerId);
        if (!glassesContainer) return;

        if (glassesRenderer) {
            cancelAnimationFrame(glassesAnimId);
            glassesRenderer.dispose();
            glassesContainer.innerHTML = '';
        }

        const width = glassesContainer.clientWidth || 700;
        const height = glassesContainer.clientHeight || 450;

        glassesScene = new THREE.Scene();
        glassesScene.background = new THREE.Color(0x12161f);

        glassesCamera = new THREE.PerspectiveCamera(40, width / height, 0.1, 50);
        glassesCamera.position.set(0, 0.3, 4.2);

        glassesRenderer = createRenderer(glassesContainer);
        if (!glassesRenderer) return;
        glassesRenderer.setSize(width, height);
        glassesRenderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
        glassesRenderer.toneMapping = THREE.ACESFilmicToneMapping;
        glassesRenderer.toneMappingExposure = 1.25;
        glassesContainer.appendChild(glassesRenderer.domElement);

        glassesControls = new THREE.OrbitControls(glassesCamera, glassesRenderer.domElement);
        glassesControls.enableDamping = true;
        glassesControls.dampingFactor = 0.05;
        glassesControls.minDistance = 2;
        glassesControls.maxDistance = 8;
        glassesControls.autoRotate = !prefersReducedMotion;
        glassesControls.autoRotateSpeed = 1.2;

        // Iluminación de estudio
        const ambLight = new THREE.AmbientLight(0xffffff, 0.8);
        glassesScene.add(ambLight);

        const keyLight = new THREE.DirectionalLight(0xfff5ea, 1.4);
        keyLight.position.set(4, 5, 5);
        glassesScene.add(keyLight);

        const rimLight = new THREE.DirectionalLight(0x70d6ff, 1.0);
        rimLight.position.set(-5, 3, -4);
        glassesScene.add(rimLight);

        // Base podio
        const podiumGeo = new THREE.CylinderGeometry(1.6, 1.8, 0.15, 48);
        const podiumMat = new THREE.MeshStandardMaterial({ color: 0x1f242d, metalness: 0.8, roughness: 0.3 });
        const podium = new THREE.Mesh(podiumGeo, podiumMat);
        podium.position.y = -1.2;
        glassesScene.add(podium);

        // Construir Maniquí y Gafas
        buildMannequinAndGlasses();

        function animateGlasses() {
            glassesAnimId = requestAnimationFrame(animateGlasses);
            glassesControls.update();
            glassesRenderer.render(glassesScene, glassesCamera);
        }
        animateGlasses();

        if (glassesResizeObserver) glassesResizeObserver.disconnect();
        glassesResizeObserver = new ResizeObserver(onGlassesResize);
        glassesResizeObserver.observe(glassesContainer);
    }

    function onGlassesResize() {
        if (!glassesContainer || !glassesRenderer || !glassesCamera) return;
        const width = glassesContainer.clientWidth;
        const height = glassesContainer.clientHeight;
        if (width === 0 || height === 0) return;
        glassesCamera.aspect = width / height;
        glassesCamera.updateProjectionMatrix();
        glassesRenderer.setSize(width, height);
    }

    function buildMannequinAndGlasses() {
        const rootGroup = new THREE.Group();

        // 1. Cabeza / Maniquí estilizado minimalista
        const headGeo = new THREE.SphereGeometry(1.1, 32, 32);
        headGeo.scale(0.88, 1.15, 1);
        const headMat = new THREE.MeshStandardMaterial({
            color: 0x2b303c,
            roughness: 0.65,
            metalness: 0.15
        });
        const head = new THREE.Mesh(headGeo, headMat);
        head.position.set(0, 0, 0);
        rootGroup.add(head);

        // Nariz sutil para apoyo
        const noseGeo = new THREE.ConeGeometry(0.14, 0.35, 16);
        noseGeo.rotateX(Math.PI / 2);
        const noseMat = new THREE.MeshStandardMaterial({ color: 0x2b303c, roughness: 0.65 });
        const nose = new THREE.Mesh(noseGeo, noseMat);
        nose.position.set(0, 0.05, 1.05);
        rootGroup.add(nose);

        // 2. Grupo de Monturas 3D
        const glassesGroup = new THREE.Group();
        glassesGroup.position.set(0, 0.18, 0.98);

        // Lentes (Lunas de cristal)
        const leftLensGeo = new THREE.CircleGeometry(0.44, 32);
        const rightLensGeo = new THREE.CircleGeometry(0.44, 32);

        const lensMaterial = new THREE.MeshPhysicalMaterial({
            color: 0xffffff,
            transmission: 0.94,
            transparent: true,
            opacity: 0.85,
            roughness: 0.02,
            ior: 1.586, // Policarbonato
            clearcoat: 1.0,
            clearcoatRoughness: 0.05,
            reflectivity: 0.9
        });

        const leftLens = new THREE.Mesh(leftLensGeo, lensMaterial);
        leftLens.position.set(-0.52, 0, 0);
        glassesGroup.add(leftLens);

        const rightLens = new THREE.Mesh(rightLensGeo, lensMaterial.clone());
        rightLens.position.set(0.52, 0, 0);
        glassesGroup.add(rightLens);

        // Aros de montura (Torus)
        const frameMat = new THREE.MeshStandardMaterial({
            color: 0xd4af37, // Oro metálico clásico
            metalness: 0.85,
            roughness: 0.25
        });

        const leftRimGeo = new THREE.TorusGeometry(0.45, 0.04, 16, 48);
        const leftRim = new THREE.Mesh(leftRimGeo, frameMat);
        leftRim.position.set(-0.52, 0, 0.01);
        glassesGroup.add(leftRim);

        const rightRimGeo = new THREE.TorusGeometry(0.45, 0.04, 16, 48);
        const rightRim = new THREE.Mesh(rightRimGeo, frameMat);
        rightRim.position.set(0.52, 0, 0.01);
        glassesGroup.add(rightRim);

        // Puente nasal
        const bridgeGeo = new THREE.CylinderGeometry(0.03, 0.03, 0.32, 16);
        bridgeGeo.rotateZ(Math.PI / 2);
        const bridge = new THREE.Mesh(bridgeGeo, frameMat);
        bridge.position.set(0, 0.05, 0.02);
        glassesGroup.add(bridge);

        // Patillas
        const templeGeo = new THREE.CylinderGeometry(0.025, 0.025, 1.4, 16);
        templeGeo.rotateX(Math.PI / 2);

        const leftTemple = new THREE.Mesh(templeGeo, frameMat);
        leftTemple.position.set(-0.98, 0, -0.65);
        glassesGroup.add(leftTemple);

        const rightTemple = new THREE.Mesh(templeGeo, frameMat);
        rightTemple.position.set(0.98, 0, -0.65);
        glassesGroup.add(rightTemple);

        rootGroup.add(glassesGroup);
        glassesScene.add(rootGroup);

        glassesMeshes = {
            rootGroup,
            glassesGroup,
            leftLens,
            rightLens,
            leftRim,
            rightRim,
            bridge,
            leftTemple,
            rightTemple,
            frameMat,
            lensMaterial
        };
    }

    function setFrameColor(colorHex, metalness = 0.85, roughness = 0.25) {
        if (glassesMeshes.frameMat) {
            glassesMeshes.frameMat.color.set(colorHex);
            glassesMeshes.frameMat.metalness = metalness;
            glassesMeshes.frameMat.roughness = roughness;
        }
    }

    function setLensTreatment(treatmentType, intensity = 1.0) {
        if (!glassesMeshes.leftLens || !glassesMeshes.rightLens) return;

        const leftMat = glassesMeshes.leftLens.material;
        const rightMat = glassesMeshes.rightLens.material;

        if (treatmentType === "bluecut") {
            // Filtro azul: tinte cálido sutil y halo azul violáceo
            leftMat.color.set(0xfff7ed);
            rightMat.color.set(0xfff7ed);
            leftMat.transmission = 0.92;
            rightMat.transmission = 0.92;
            leftMat.reflectivity = 0.95;
            rightMat.reflectivity = 0.95;
        } else if (treatmentType === "antireflective") {
            // Antirreflejo Crizal: Verde esmeralda sutil, máxima transparencia
            leftMat.color.set(0xf0fff4);
            rightMat.color.set(0xf0fff4);
            leftMat.transmission = 0.98;
            rightMat.transmission = 0.98;
            leftMat.roughness = 0.01;
            rightMat.roughness = 0.01;
        } else if (treatmentType === "transitions") {
            // Fotocromático: oscurecimiento según intensidad UV (0 a 1)
            const tintFactor = 1 - (intensity * 0.75);
            leftMat.color.setRGB(tintFactor * 0.9, tintFactor * 0.88, tintFactor * 0.85);
            rightMat.color.setRGB(tintFactor * 0.9, tintFactor * 0.88, tintFactor * 0.85);
            leftMat.transmission = 0.95 - (intensity * 0.65);
            rightMat.transmission = 0.95 - (intensity * 0.65);
        } else if (treatmentType === "polarized") {
            // Polarizado Espejado: acabado cromo / plata oscuro
            leftMat.color.set(0x4a4e69);
            rightMat.color.set(0x4a4e69);
            leftMat.transmission = 0.35;
            rightMat.transmission = 0.35;
            leftMat.metalness = 0.7;
            rightMat.metalness = 0.7;
            leftMat.roughness = 0.05;
            rightMat.roughness = 0.05;
        } else {
            // Luna transparente estándar
            leftMat.color.set(0xffffff);
            rightMat.color.set(0xffffff);
            leftMat.transmission = 0.95;
            rightMat.transmission = 0.95;
            leftMat.metalness = 0.0;
            rightMat.metalness = 0.0;
        }
    }

    function setFrameStyle(styleName) {
        if (!glassesMeshes.leftRim || !glassesMeshes.rightRim) return;

        if (styleName === "aviator") {
            glassesMeshes.leftRim.scale.set(1.05, 1.2, 1);
            glassesMeshes.rightRim.scale.set(1.05, 1.2, 1);
            glassesMeshes.leftLens.scale.set(1.05, 1.2, 1);
            glassesMeshes.rightLens.scale.set(1.05, 1.2, 1);
        } else if (styleName === "round") {
            glassesMeshes.leftRim.scale.set(1, 1, 1);
            glassesMeshes.rightRim.scale.set(1, 1, 1);
            glassesMeshes.leftLens.scale.set(1, 1, 1);
            glassesMeshes.rightLens.scale.set(1, 1, 1);
        } else if (styleName === "wayfarer") {
            glassesMeshes.leftRim.scale.set(1.25, 0.95, 1);
            glassesMeshes.rightRim.scale.set(1.25, 0.95, 1);
            glassesMeshes.leftLens.scale.set(1.25, 0.95, 1);
            glassesMeshes.rightLens.scale.set(1.25, 0.95, 1);
        }
    }

    function toggleGlassesAutoRotate(enable) {
        if (glassesControls) {
            glassesControls.autoRotate = enable;
        }
    }

    function resetGlassesCamera() {
        if (glassesCamera && glassesControls) {
            glassesCamera.position.set(0, 0.3, 4.2);
            glassesControls.target.set(0, 0, 0);
            glassesControls.update();
        }
    }

    async function startLocalCamera(videoId) {
        const video = document.getElementById(videoId);
        if (!video || !navigator.mediaDevices?.getUserMedia) throw new Error('Camera unavailable');
        stopLocalCamera(videoId);
        localCameraStream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user' }, audio: false });
        video.srcObject = localCameraStream;
    }

    function stopLocalCamera(videoId) {
        if (localCameraStream) {
            localCameraStream.getTracks().forEach(track => track.stop());
            localCameraStream = null;
        }
        const video = document.getElementById(videoId);
        if (video) video.srcObject = null;
    }

    // Cleanup global
    function disposeAll() {
        stopLocalCamera('virtual-tryon-video');
        if (eyeResizeObserver) eyeResizeObserver.disconnect();
        if (glassesResizeObserver) glassesResizeObserver.disconnect();
        if (eyeRenderer) {
            cancelAnimationFrame(eyeAnimId);
            eyeRenderer.dispose();
            eyeRenderer.forceContextLoss();
        }
        if (glassesRenderer) {
            cancelAnimationFrame(glassesAnimId);
            glassesRenderer.dispose();
            glassesRenderer.forceContextLoss();
        }
    }

    return {
        initEyeSimulator,
        updateEyeOptics,
        setEyeIrisColor,
        toggleEyeCrossSection,
        resetEyeCamera,
        initGlassesStudio,
        setFrameColor,
        setLensTreatment,
        setFrameStyle,
        toggleGlassesAutoRotate,
        resetGlassesCamera,
        startLocalCamera,
        stopLocalCamera,
        disposeAll
    };
})();
